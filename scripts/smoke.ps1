[CmdletBinding()]
param(
    [switch]$NoBuild,
    [switch]$ShowOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$SolutionPath = Join-Path $RepoRoot 'navlyn.slnx'
$ProjectPath = Join-Path $RepoRoot 'navlyn/navlyn.csproj'
$ProjectDir = Join-Path $RepoRoot 'navlyn'
$NormalizeScript = Join-Path $RepoRoot 'scripts/normalize-csharp-files.ps1'
$FormatCheckScript = Join-Path $RepoRoot 'scripts/test-csharp-file-format.ps1'

[xml]$ProjectXml = Get-Content -Raw -LiteralPath $ProjectPath
$TargetFramework = [string]$ProjectXml.Project.PropertyGroup.TargetFramework
$NavlynDll = Join-Path $ProjectDir "bin/Debug/$TargetFramework/navlyn.dll"

function Invoke-CheckedProcess {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [int]$ExpectedExitCode,

        [string]$WorkingDirectory = $RepoRoot,

        [string]$StandardInput = $null
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.RedirectStandardInput = $null -ne $StandardInput
    $startInfo.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $startInfo.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    $startInfo.UseShellExecute = $false
    $startInfo.Arguments = Join-ProcessArguments -Arguments $Arguments

    $process = [System.Diagnostics.Process]::Start($startInfo)
    if ($null -ne $StandardInput) {
        $process.StandardInput.Write($StandardInput)
        $process.StandardInput.Close()
    }

    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    $exitCode = $process.ExitCode

    if ($exitCode -ne $ExpectedExitCode) {
        throw @"
$Name failed with exit code $exitCode. Expected $ExpectedExitCode.
Command: $FilePath $($Arguments -join ' ')
stdout:
$stdout
stderr:
$stderr
"@
    }

    $result = [pscustomobject]@{
        Name = $Name
        ExitCode = $exitCode
        Stdout = $stdout
        Stderr = $stderr
    }

    if ($ShowOutput) {
        Write-ProcessResult `
            -Name $Name `
            -FilePath $FilePath `
            -Arguments $Arguments `
            -Result $result
    }

    $result
}

function Write-ProcessResult {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [psobject]$Result
    )

    Write-Host ''
    Write-Host "[$Name]"
    Write-Host "command: $FilePath $($Arguments -join ' ')"
    Write-Host "exit: $($Result.ExitCode)"
    Write-Host 'stdout:'
    if ($Result.Stdout.Length -eq 0) {
        Write-Host '<empty>'
    }
    else {
        Write-Host $Result.Stdout.TrimEnd()
    }

    Write-Host 'stderr:'
    if ($Result.Stderr.Length -eq 0) {
        Write-Host '<empty>'
    }
    else {
        Write-Host $Result.Stderr.TrimEnd()
    }
}

function Join-ProcessArguments {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    ($Arguments | ForEach-Object { ConvertTo-ProcessArgument -Argument $_ }) -join ' '
}

function ConvertTo-ProcessArgument {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Argument
    )

    if ($Argument.Length -eq 0) {
        return '""'
    }

    if ($Argument.IndexOfAny([char[]]@(' ', "`t", '"')) -lt 0) {
        return $Argument
    }

    return '"' + $Argument.Replace('"', '\"') + '"'
}

function Invoke-Navlyn {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [int]$ExpectedExitCode,

        [string]$WorkingDirectory = $RepoRoot,

        [string]$StandardInput = $null
    )

    $dotnetArguments = @($NavlynDll) + $Arguments

    Invoke-CheckedProcess `
        -Name $Name `
        -FilePath 'dotnet' `
        -Arguments $dotnetArguments `
        -ExpectedExitCode $ExpectedExitCode `
        -WorkingDirectory $WorkingDirectory `
        -StandardInput $StandardInput
}

function Assert-Contains {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Text,

        [Parameter(Mandatory = $true)]
        [string]$Expected
    )

    if ($Text.IndexOf($Expected, [StringComparison]::Ordinal) -lt 0) {
        throw "$Name did not contain expected text '$Expected'. Actual text: $Text"
    }
}

function Assert-Equal {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [AllowNull()]
        [object]$Actual,

        [AllowNull()]
        [object]$Expected
    )

    if ($Actual -ne $Expected) {
        throw "$Name expected '$Expected' but was '$Actual'."
    }
}

function Assert-Empty {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Text
    )

    if ($Text.Length -ne 0) {
        throw "$Name was expected to be empty. Actual text: $Text"
    }
}

Push-Location $RepoRoot
try {
    & $NormalizeScript -Quiet
    & $FormatCheckScript -Quiet

    if (!$NoBuild) {
        Write-Host 'Building navlyn...'
        Invoke-CheckedProcess `
            -Name 'dotnet build' `
            -FilePath 'dotnet' `
            -Arguments @('build', $SolutionPath) `
            -ExpectedExitCode 0 | Out-Null
    }

    if (!(Test-Path -LiteralPath $NavlynDll)) {
        throw "Navlyn executable was not found: $NavlynDll. Run without -NoBuild first."
    }

    Write-Host 'Running smoke checks...'

    $validCheck = Invoke-Navlyn `
        -Name 'check valid workspace' `
        -Arguments @('check', '--workspace', '.\navlyn.slnx') `
        -ExpectedExitCode 0

    $validJson = $validCheck.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'check valid workspace ok' -Actual $validJson.ok -Expected $true
    Assert-Equal -Name 'check valid workspace workspace' -Actual $validJson.workspace -Expected 'navlyn.slnx'
    Assert-Equal -Name 'check valid workspace kind' -Actual $validJson.kind -Expected 'solution'
    Assert-Equal -Name 'check valid workspace projects' -Actual $validJson.projects -Expected 1

    $overview = Invoke-Navlyn `
        -Name 'overview valid workspace' `
        -Arguments @('overview', '--workspace', '.\navlyn.slnx') `
        -ExpectedExitCode 0

    $overviewJson = $overview.Stdout | ConvertFrom-Json
    $overviewProject = @($overviewJson.projects)[0]
    Assert-Equal -Name 'overview workspace' -Actual $overviewJson.workspace -Expected 'navlyn.slnx'
    Assert-Equal -Name 'overview kind' -Actual $overviewJson.kind -Expected 'solution'
    Assert-Equal -Name 'overview project count' -Actual @($overviewJson.projects).Count -Expected 1
    Assert-Equal -Name 'overview project name' -Actual $overviewProject.name -Expected 'navlyn'
    Assert-Equal -Name 'overview project path' -Actual $overviewProject.path -Expected (Join-Path 'navlyn' 'navlyn.csproj')
    Assert-Equal -Name 'overview project language' -Actual $overviewProject.language -Expected 'C#'
    Assert-Equal -Name 'overview project assembly name' -Actual $overviewProject.assemblyName -Expected 'navlyn'

    $subdirectoryOverview = Invoke-Navlyn `
        -Name 'overview from subdirectory with repo-relative workspace' `
        -Arguments @('overview', '--workspace', 'navlyn.slnx') `
        -ExpectedExitCode 0 `
        -WorkingDirectory $ProjectDir

    $subdirectoryOverviewJson = $subdirectoryOverview.Stdout | ConvertFrom-Json
    $subdirectoryOverviewProject = @($subdirectoryOverviewJson.projects)[0]
    Assert-Equal -Name 'subdirectory overview workspace' -Actual $subdirectoryOverviewJson.workspace -Expected 'navlyn.slnx'
    Assert-Equal -Name 'subdirectory overview project path' -Actual $subdirectoryOverviewProject.path -Expected (Join-Path 'navlyn' 'navlyn.csproj')

    $subdirectoryProjectFilter = Invoke-Navlyn `
        -Name 'diagnostics from subdirectory with repo-relative project filter' `
        -Arguments @('diagnostics', '--workspace', 'navlyn.slnx', '--project', (Join-Path 'navlyn' 'navlyn.csproj'), '--limit', '1') `
        -ExpectedExitCode 0 `
        -WorkingDirectory $ProjectDir

    $subdirectoryProjectFilterJson = $subdirectoryProjectFilter.Stdout | ConvertFrom-Json
    $subdirectoryAppliedProject = @($subdirectoryProjectFilterJson.projects)[0]
    Assert-Equal -Name 'subdirectory project filter path' -Actual $subdirectoryAppliedProject.path -Expected (Join-Path 'navlyn' 'navlyn.csproj')

    $subdirectorySymbolAt = Invoke-Navlyn `
        -Name 'symbol-at from subdirectory with repo-relative source file' `
        -Arguments @('symbol-at', '--workspace', 'navlyn.slnx', '--file', (Join-Path 'navlyn' (Join-Path 'Cli' (Join-Path 'Commands' 'CheckCommand.cs'))), '--line', '6', '--column', '23') `
        -ExpectedExitCode 0 `
        -WorkingDirectory $ProjectDir

    $subdirectorySymbolAtJson = $subdirectorySymbolAt.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'subdirectory symbol-at file' -Actual $subdirectorySymbolAtJson.file -Expected (Join-Path 'navlyn' (Join-Path 'Cli' (Join-Path 'Commands' 'CheckCommand.cs')))
    Assert-Equal -Name 'subdirectory symbol-at symbol path' -Actual $subdirectorySymbolAtJson.symbol.path -Expected (Join-Path 'navlyn' (Join-Path 'Cli' (Join-Path 'Commands' 'CheckCommand.cs')))

    $diagnostics = Invoke-Navlyn `
        -Name 'diagnostics valid workspace' `
        -Arguments @('diagnostics', '--workspace', '.\navlyn.slnx') `
        -ExpectedExitCode 0

    $diagnosticsJson = $diagnostics.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'diagnostics workspace' -Actual $diagnosticsJson.workspace -Expected 'navlyn.slnx'
    Assert-Equal -Name 'diagnostics kind' -Actual $diagnosticsJson.kind -Expected 'solution'
    Assert-Equal -Name 'diagnostics total is non-negative' -Actual ($diagnosticsJson.totalDiagnostics -ge 0) -Expected $true

    $batchInput = @'
{
  "defaults": {
    "project": "navlyn"
  },
  "requests": [
    {
      "id": "overview",
      "command": "overview"
    },
    {
      "id": "symbols",
      "command": "symbols",
      "query": "Check",
      "limit": 1
    },
    {
      "id": "outline",
      "command": "outline",
      "file": "navlyn/Cli/Commands/CheckCommand.cs"
    },
    {
      "id": "symbol-info",
      "command": "symbol-info",
      "file": "navlyn/Cli/NavlynCli.cs",
      "line": 31,
      "column": 37
    },
    {
      "id": "type-hierarchy",
      "command": "type-hierarchy",
      "file": "navlyn/Cli/Commands/CheckCommand.cs",
      "line": 6,
      "column": 23
    },
    {
      "id": "callers",
      "command": "callers",
      "file": "navlyn/Cli/Commands/CheckCommand.cs",
      "line": 8,
      "column": 27
    },
    {
      "id": "calls",
      "command": "calls",
      "file": "navlyn/Cli/NavlynCli.cs",
      "line": 28,
      "column": 32
    },
    {
      "id": "definition-no-source",
      "command": "definition",
      "file": "navlyn/Cli/NavlynCli.cs",
      "line": 12,
      "column": 9
    },
    {
      "id": "definition-metadata",
      "command": "definition",
      "file": "navlyn/Cli/NavlynCli.cs",
      "line": 12,
      "column": 9,
      "includeMetadata": true
    }
  ]
}
'@

    $batch = Invoke-Navlyn `
        -Name 'batch stdin' `
        -Arguments @('batch', '--workspace', '.\navlyn.slnx') `
        -ExpectedExitCode 0 `
        -StandardInput $batchInput

    Assert-Empty -Name 'batch stdin stderr' -Text $batch.Stderr
    $batchJson = $batch.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'batch stdin workspace' -Actual $batchJson.workspace -Expected 'navlyn.slnx'
    Assert-Equal -Name 'batch stdin request count' -Actual $batchJson.totalRequests -Expected 9
    Assert-Equal -Name 'batch stdin succeeded count' -Actual $batchJson.succeededRequests -Expected 8
    Assert-Equal -Name 'batch stdin failed count' -Actual $batchJson.failedRequests -Expected 1
    Assert-Equal -Name 'batch stdin first id' -Actual @($batchJson.results)[0].id -Expected 'overview'
    Assert-Equal -Name 'batch stdin second command' -Actual @($batchJson.results)[1].command -Expected 'symbols'
    Assert-Equal -Name 'batch stdin symbols limit' -Actual @($batchJson.results)[1].result.limit -Expected 1
    Assert-Equal -Name 'batch stdin symbols span' -Actual (@($batchJson.results)[1].result.matches)[0].endColumn -Expected 35
    Assert-Equal -Name 'batch stdin outline command' -Actual @($batchJson.results)[2].command -Expected 'outline'
    Assert-Equal -Name 'batch stdin symbol-info command' -Actual @($batchJson.results)[3].command -Expected 'symbol-info'
    Assert-Equal -Name 'batch stdin type-hierarchy command' -Actual @($batchJson.results)[4].command -Expected 'type-hierarchy'
    Assert-Equal -Name 'batch stdin callers command' -Actual @($batchJson.results)[5].command -Expected 'callers'
    Assert-Equal -Name 'batch stdin calls command' -Actual @($batchJson.results)[6].command -Expected 'calls'
    Assert-Equal -Name 'batch stdin failure ok' -Actual @($batchJson.results)[7].ok -Expected $false
    Assert-Equal -Name 'batch stdin failure code' -Actual @($batchJson.results)[7].error.code -Expected 'NAVLYN1305'
    Assert-Equal -Name 'batch stdin metadata definition ok' -Actual @($batchJson.results)[8].ok -Expected $true
    Assert-Equal -Name 'batch stdin metadata definition include' -Actual @($batchJson.results)[8].result.includeMetadata -Expected $true
    Assert-Equal -Name 'batch stdin metadata definition count' -Actual @(@($batchJson.results)[8].result.definitions).Count -Expected 0

    $fuzzyBatchInput = @'
{
  "requests": [
    {
      "id": "find",
      "command": "find",
      "query": "CheckCommand",
      "assumeKind": "NamedType"
    },
    {
      "id": "where-used",
      "command": "where-used",
      "query": "CheckCommand",
      "assumeKind": "NamedType",
      "limit": 1
    },
    {
      "id": "about",
      "command": "about",
      "query": "CheckCommand",
      "assumeKind": "NamedType",
      "memberLimit": 1,
      "referenceLimit": 1
    }
  ]
}
'@

    $fuzzyBatch = Invoke-Navlyn `
        -Name 'batch fuzzy commands' `
        -Arguments @('batch', '--workspace', '.\navlyn.slnx') `
        -ExpectedExitCode 0 `
        -StandardInput $fuzzyBatchInput

    Assert-Empty -Name 'batch fuzzy stderr' -Text $fuzzyBatch.Stderr
    $fuzzyBatchJson = $fuzzyBatch.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'batch fuzzy request count' -Actual $fuzzyBatchJson.totalRequests -Expected 3
    Assert-Equal -Name 'batch fuzzy succeeded count' -Actual $fuzzyBatchJson.succeededRequests -Expected 3
    Assert-Equal -Name 'batch fuzzy find confidence' -Actual @($fuzzyBatchJson.results)[0].result.confidence -Expected 'high'
    Assert-Equal -Name 'batch fuzzy find span' -Actual @($fuzzyBatchJson.results)[0].result.selectedCandidate.endColumn -Expected 35
    Assert-Equal -Name 'batch fuzzy where-used total' -Actual @($fuzzyBatchJson.results)[1].result.totalMatches -Expected 1
    Assert-Equal -Name 'batch fuzzy where-used span' -Actual (@($fuzzyBatchJson.results)[1].result.references)[0].endColumn -Expected 49
    Assert-Equal -Name 'batch fuzzy about members' -Actual @(@($fuzzyBatchJson.results)[2].result.members.members).Count -Expected 1

    $batchFile = New-TemporaryFile
    try {
        Set-Content -LiteralPath $batchFile.FullName -Encoding utf8NoBOM -Value @'
{
  "requests": [
    {
      "id": "diagnostics",
      "command": "diagnostics",
      "project": "navlyn"
    }
  ]
}
'@

        $batchFromFile = Invoke-Navlyn `
            -Name 'batch input file' `
            -Arguments @('batch', '--workspace', '.\navlyn.slnx', '--input', $batchFile.FullName) `
            -ExpectedExitCode 0

        Assert-Empty -Name 'batch input file stderr' -Text $batchFromFile.Stderr
        $batchFromFileJson = $batchFromFile.Stdout | ConvertFrom-Json
        Assert-Equal -Name 'batch input file request count' -Actual $batchFromFileJson.totalRequests -Expected 1
        Assert-Equal -Name 'batch input file command' -Actual @($batchFromFileJson.results)[0].command -Expected 'diagnostics'
        Assert-Equal -Name 'batch input file diagnostics non-negative' -Actual (@($batchFromFileJson.results)[0].result.totalDiagnostics -ge 0) -Expected $true
    }
    finally {
        Remove-Item -LiteralPath $batchFile.FullName -ErrorAction SilentlyContinue
    }

    $batchInvalidJson = Invoke-Navlyn `
        -Name 'batch invalid json' `
        -Arguments @('batch', '--workspace', '.\navlyn.slnx') `
        -ExpectedExitCode 2 `
        -StandardInput '{'
    Assert-Empty -Name 'batch invalid json stdout' -Text $batchInvalidJson.Stdout
    Assert-Contains -Name 'batch invalid json stderr' -Text $batchInvalidJson.Stderr -Expected 'NAVLYN1008:'

    $symbols = Invoke-Navlyn `
        -Name 'symbols partial query' `
        -Arguments @('symbols', '--workspace', '.\navlyn.slnx', '--query', 'Check') `
        -ExpectedExitCode 0

    $symbolsJson = $symbols.Stdout | ConvertFrom-Json
    $symbolMatch = @($symbolsJson.matches | Where-Object { $_.name -eq 'CheckCommand' })[0]
    Assert-Equal -Name 'symbols query' -Actual $symbolsJson.query -Expected 'Check'
    Assert-Equal -Name 'symbols match mode' -Actual $symbolsJson.match -Expected 'contains'
    Assert-Equal -Name 'symbols case sensitivity' -Actual $symbolsJson.caseSensitive -Expected $false
    Assert-Equal -Name 'symbols kind filter count' -Actual @($symbolsJson.kinds).Count -Expected 0
    Assert-Equal -Name 'symbols limit' -Actual $symbolsJson.limit -Expected $null
    Assert-Equal -Name 'symbols total matches' -Actual $symbolsJson.totalMatches -Expected 2
    Assert-Equal -Name 'symbols contains CheckCommand' -Actual $symbolMatch.name -Expected 'CheckCommand'
    Assert-Equal -Name 'symbols CheckCommand kind' -Actual $symbolMatch.kind -Expected 'NamedType'
    Assert-Equal -Name 'symbols CheckCommand path' -Actual $symbolMatch.path -Expected (Join-Path 'navlyn' (Join-Path 'Cli' (Join-Path 'Commands' 'CheckCommand.cs')))
    Assert-Equal -Name 'symbols CheckCommand line' -Actual $symbolMatch.line -Expected 6
    Assert-Equal -Name 'symbols CheckCommand column' -Actual $symbolMatch.column -Expected 23
    Assert-Equal -Name 'symbols CheckCommand end line' -Actual $symbolMatch.endLine -Expected 6
    Assert-Equal -Name 'symbols CheckCommand end column' -Actual $symbolMatch.endColumn -Expected 35

    $symbolsLimit = Invoke-Navlyn `
        -Name 'symbols limited query' `
        -Arguments @('symbols', '--workspace', '.\navlyn.slnx', '--query', 'Check', '--limit', '1') `
        -ExpectedExitCode 0

    $symbolsLimitJson = $symbolsLimit.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbols limited query limit' -Actual $symbolsLimitJson.limit -Expected 1
    Assert-Equal -Name 'symbols limited query total matches' -Actual $symbolsLimitJson.totalMatches -Expected 2
    Assert-Equal -Name 'symbols limited query match count' -Actual @($symbolsLimitJson.matches).Count -Expected 1
    Assert-Equal -Name 'symbols limited query first match name' -Actual @($symbolsLimitJson.matches)[0].name -Expected 'CheckCommand'

    $symbolsKind = Invoke-Navlyn `
        -Name 'symbols kind filter query' `
        -Arguments @('symbols', '--workspace', '.\navlyn.slnx', '--query', 'Check', '--kind', 'NamedType') `
        -ExpectedExitCode 0

    $symbolsKindJson = $symbolsKind.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbols kind filter count' -Actual @($symbolsKindJson.kinds).Count -Expected 1
    Assert-Equal -Name 'symbols kind filter value' -Actual @($symbolsKindJson.kinds)[0] -Expected 'NamedType'
    Assert-Equal -Name 'symbols kind filter total matches' -Actual $symbolsKindJson.totalMatches -Expected 2
    Assert-Equal -Name 'symbols kind filter first match kind' -Actual @($symbolsKindJson.matches)[0].kind -Expected 'NamedType'

    $symbolsNamespace = Invoke-Navlyn `
        -Name 'symbols namespace container accessibility filters' `
        -Arguments @('symbols', '--workspace', '.\navlyn.slnx', '--query', 'Create', '--namespace', 'Navlyn.Cli.Commands', '--namespace-match', 'exact', '--container', 'CheckCommand', '--container-match', 'contains', '--accessibility', 'Public', '--limit', '1') `
        -ExpectedExitCode 0

    $symbolsNamespaceJson = $symbolsNamespace.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbols namespace filter total matches' -Actual $symbolsNamespaceJson.totalMatches -Expected 1
    Assert-Equal -Name 'symbols namespace filter namespace' -Actual @($symbolsNamespaceJson.namespaces)[0] -Expected 'Navlyn.Cli.Commands'
    Assert-Equal -Name 'symbols namespace filter match name' -Actual @($symbolsNamespaceJson.matches)[0].name -Expected 'Create'
    Assert-Equal -Name 'symbols namespace filter accessibility fact' -Actual @($symbolsNamespaceJson.matches)[0].facts.accessibility -Expected 'Public'

    $symbolsExact = Invoke-Navlyn `
        -Name 'symbols exact query' `
        -Arguments @('symbols', '--workspace', '.\navlyn.slnx', '--query', 'CheckCommand', '--match', 'exact') `
        -ExpectedExitCode 0

    $symbolsExactJson = $symbolsExact.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbols exact match mode' -Actual $symbolsExactJson.match -Expected 'exact'
    Assert-Equal -Name 'symbols exact match count' -Actual @($symbolsExactJson.matches).Count -Expected 1
    Assert-Equal -Name 'symbols exact match name' -Actual @($symbolsExactJson.matches)[0].name -Expected 'CheckCommand'

    $symbolsRegex = Invoke-Navlyn `
        -Name 'symbols regex query' `
        -Arguments @('symbols', '--workspace', '.\navlyn.slnx', '--query', '^Check.*Command$', '--match', 'regex') `
        -ExpectedExitCode 0

    $symbolsRegexJson = $symbolsRegex.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbols regex match mode' -Actual $symbolsRegexJson.match -Expected 'regex'
    Assert-Equal -Name 'symbols regex match count' -Actual @($symbolsRegexJson.matches).Count -Expected 1
    Assert-Equal -Name 'symbols regex match name' -Actual @($symbolsRegexJson.matches)[0].name -Expected 'CheckCommand'

    $symbolsCaseSensitive = Invoke-Navlyn `
        -Name 'symbols case-sensitive query' `
        -Arguments @('symbols', '--workspace', '.\navlyn.slnx', '--query', 'check', '--case-sensitive') `
        -ExpectedExitCode 0

    $symbolsCaseSensitiveJson = $symbolsCaseSensitive.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbols case-sensitive flag' -Actual $symbolsCaseSensitiveJson.caseSensitive -Expected $true
    Assert-Equal -Name 'symbols case-sensitive match count' -Actual @($symbolsCaseSensitiveJson.matches).Count -Expected 0

    $symbolsInvalidRegex = Invoke-Navlyn `
        -Name 'symbols invalid regex' `
        -Arguments @('symbols', '--workspace', '.\navlyn.slnx', '--query', '[', '--match', 'regex') `
        -ExpectedExitCode 2
    Assert-Empty -Name 'symbols invalid regex stdout' -Text $symbolsInvalidRegex.Stdout
    Assert-Contains -Name 'symbols invalid regex stderr' -Text $symbolsInvalidRegex.Stderr -Expected 'NAVLYN1002:'

    $symbolsInvalidMatch = Invoke-Navlyn `
        -Name 'symbols invalid match mode' `
        -Arguments @('symbols', '--workspace', '.\navlyn.slnx', '--query', 'Check', '--match', 'starts-with') `
        -ExpectedExitCode 2
    Assert-Empty -Name 'symbols invalid match stdout' -Text $symbolsInvalidMatch.Stdout
    Assert-Contains -Name 'symbols invalid match stderr' -Text $symbolsInvalidMatch.Stderr -Expected 'NAVLYN1001:'

    $symbolsInvalidLimit = Invoke-Navlyn `
        -Name 'symbols invalid limit' `
        -Arguments @('symbols', '--workspace', '.\navlyn.slnx', '--query', 'Check', '--limit', '0') `
        -ExpectedExitCode 2
    Assert-Empty -Name 'symbols invalid limit stdout' -Text $symbolsInvalidLimit.Stdout
    Assert-Contains -Name 'symbols invalid limit stderr' -Text $symbolsInvalidLimit.Stderr -Expected 'NAVLYN1003:'

    $symbolsInvalidKind = Invoke-Navlyn `
        -Name 'symbols invalid kind' `
        -Arguments @('symbols', '--workspace', '.\navlyn.slnx', '--query', 'Check', '--kind', '1') `
        -ExpectedExitCode 2
    Assert-Empty -Name 'symbols invalid kind stdout' -Text $symbolsInvalidKind.Stdout
    Assert-Contains -Name 'symbols invalid kind stderr' -Text $symbolsInvalidKind.Stderr -Expected 'NAVLYN1004:'

    $symbolsIn = Invoke-Navlyn `
        -Name 'symbols-in source line' `
        -Arguments @('symbols-in', '--workspace', '.\navlyn.slnx', '--file', '.\navlyn\Cli\NavlynCli.cs', '--line', '31') `
        -ExpectedExitCode 0

    $symbolsInJson = $symbolsIn.Stdout | ConvertFrom-Json
    $symbolsInMatch = @($symbolsInJson.symbols | Where-Object { $_.name -eq 'CheckCommand' })[0]
    Assert-Equal -Name 'symbols-in file' -Actual $symbolsInJson.file -Expected (Join-Path 'navlyn' (Join-Path 'Cli' 'NavlynCli.cs'))
    Assert-Equal -Name 'symbols-in line' -Actual $symbolsInJson.line -Expected 31
    Assert-Equal -Name 'symbols-in start column' -Actual $symbolsInJson.startColumn -Expected 1
    Assert-Equal -Name 'symbols-in end column' -Actual $symbolsInJson.endColumn -Expected 60
    Assert-Equal -Name 'symbols-in contains CheckCommand' -Actual $symbolsInMatch.name -Expected 'CheckCommand'
    Assert-Equal -Name 'symbols-in CheckCommand kind' -Actual $symbolsInMatch.kind -Expected 'NamedType'
    Assert-Equal -Name 'symbols-in CheckCommand line' -Actual $symbolsInMatch.line -Expected 31
    Assert-Equal -Name 'symbols-in CheckCommand column' -Actual $symbolsInMatch.column -Expected 37
    Assert-Equal -Name 'symbols-in CheckCommand end line' -Actual $symbolsInMatch.endLine -Expected 31
    Assert-Equal -Name 'symbols-in CheckCommand end column' -Actual $symbolsInMatch.endColumn -Expected 49

    $symbolsInSpan = Invoke-Navlyn `
        -Name 'symbols-in source span' `
        -Arguments @('symbols-in', '--workspace', '.\navlyn.slnx', '--file', '.\navlyn\Cli\NavlynCli.cs', '--line', '31', '--start-column', '37', '--end-column', '49') `
        -ExpectedExitCode 0

    $symbolsInSpanJson = $symbolsInSpan.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbols-in span start column' -Actual $symbolsInSpanJson.startColumn -Expected 37
    Assert-Equal -Name 'symbols-in span end column' -Actual $symbolsInSpanJson.endColumn -Expected 49
    Assert-Equal -Name 'symbols-in span match count' -Actual @($symbolsInSpanJson.symbols).Count -Expected 1
    Assert-Equal -Name 'symbols-in span match name' -Actual @($symbolsInSpanJson.symbols)[0].name -Expected 'CheckCommand'

    $symbolsInEmpty = Invoke-Navlyn `
        -Name 'symbols-in no symbols' `
        -Arguments @('symbols-in', '--workspace', '.\navlyn.slnx', '--file', '.\navlyn\Cli\Commands\CheckCommand.cs', '--line', '3') `
        -ExpectedExitCode 0

    $symbolsInEmptyJson = $symbolsInEmpty.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbols-in no symbols count' -Actual @($symbolsInEmptyJson.symbols).Count -Expected 0

    $outline = Invoke-Navlyn `
        -Name 'outline source file' `
        -Arguments @('outline', '--workspace', '.\navlyn.slnx', '--file', '.\navlyn\Cli\Commands\CheckCommand.cs') `
        -ExpectedExitCode 0

    $outlineJson = $outline.Stdout | ConvertFrom-Json
    $outlineCreate = @($outlineJson.entries | Where-Object { $_.name -eq 'Create' -and $_.kind -eq 'Method' })[0]
    Assert-Equal -Name 'outline file' -Actual $outlineJson.file -Expected (Join-Path 'navlyn' (Join-Path 'Cli' (Join-Path 'Commands' 'CheckCommand.cs')))
    Assert-Equal -Name 'outline contains Create method' -Actual $outlineCreate.name -Expected 'Create'
    Assert-Equal -Name 'outline Create facts accessibility' -Actual $outlineCreate.facts.accessibility -Expected 'Public'

    $symbolsInInvalidSpan = Invoke-Navlyn `
        -Name 'symbols-in invalid span' `
        -Arguments @('symbols-in', '--workspace', '.\navlyn.slnx', '--file', '.\navlyn\Cli\NavlynCli.cs', '--line', '31', '--start-column', '37', '--end-column', '37') `
        -ExpectedExitCode 2
    Assert-Empty -Name 'symbols-in invalid span stdout' -Text $symbolsInInvalidSpan.Stdout
    Assert-Contains -Name 'symbols-in invalid span stderr' -Text $symbolsInInvalidSpan.Stderr -Expected 'NAVLYN1303:'

    $symbolAt = Invoke-Navlyn `
        -Name 'symbol-at declaration' `
        -Arguments @('symbol-at', '--workspace', '.\navlyn.slnx', '--file', '.\navlyn\Cli\Commands\CheckCommand.cs', '--line', '6', '--column', '23') `
        -ExpectedExitCode 0

    $symbolAtJson = $symbolAt.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbol-at file' -Actual $symbolAtJson.file -Expected (Join-Path 'navlyn' (Join-Path 'Cli' (Join-Path 'Commands' 'CheckCommand.cs')))
    Assert-Equal -Name 'symbol-at line' -Actual $symbolAtJson.line -Expected 6
    Assert-Equal -Name 'symbol-at column' -Actual $symbolAtJson.column -Expected 23
    Assert-Equal -Name 'symbol-at name' -Actual $symbolAtJson.symbol.name -Expected 'CheckCommand'
    Assert-Equal -Name 'symbol-at kind' -Actual $symbolAtJson.symbol.kind -Expected 'NamedType'
    Assert-Equal -Name 'symbol-at container' -Actual $symbolAtJson.symbol.container -Expected 'Navlyn.Cli.Commands'
    Assert-Equal -Name 'symbol-at path' -Actual $symbolAtJson.symbol.path -Expected (Join-Path 'navlyn' (Join-Path 'Cli' (Join-Path 'Commands' 'CheckCommand.cs')))
    Assert-Equal -Name 'symbol-at declaration line' -Actual $symbolAtJson.symbol.line -Expected 6
    Assert-Equal -Name 'symbol-at declaration column' -Actual $symbolAtJson.symbol.column -Expected 23
    Assert-Equal -Name 'symbol-at declaration end line' -Actual $symbolAtJson.symbol.endLine -Expected 6
    Assert-Equal -Name 'symbol-at declaration end column' -Actual $symbolAtJson.symbol.endColumn -Expected 35
    Assert-Equal -Name 'symbol-at facts project' -Actual $symbolAtJson.symbol.facts.project -Expected 'navlyn'

    $symbolInfo = Invoke-Navlyn `
        -Name 'symbol-info invocation' `
        -Arguments @('symbol-info', '--workspace', '.\navlyn.slnx', '--file', '.\navlyn\Cli\NavlynCli.cs', '--line', '31', '--column', '37') `
        -ExpectedExitCode 0

    $symbolInfoJson = $symbolInfo.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbol-info symbol name' -Actual $symbolInfoJson.symbol.name -Expected 'CheckCommand'
    Assert-Equal -Name 'symbol-info invocation target' -Actual $symbolInfoJson.invocation.target.displayName -Expected 'Navlyn.Cli.Commands.CheckCommand.Create()'

    $typeHierarchy = Invoke-Navlyn `
        -Name 'type-hierarchy non-derived type' `
        -Arguments @('type-hierarchy', '--workspace', '.\navlyn.slnx', '--file', '.\navlyn\Cli\Commands\CheckCommand.cs', '--line', '6', '--column', '23') `
        -ExpectedExitCode 0

    $typeHierarchyJson = $typeHierarchy.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'type-hierarchy symbol name' -Actual $typeHierarchyJson.symbol.name -Expected 'CheckCommand'

    $symbolAtInvalidLine = Invoke-Navlyn `
        -Name 'symbol-at invalid line' `
        -Arguments @('symbol-at', '--workspace', '.\navlyn.slnx', '--file', '.\navlyn\Cli\Commands\CheckCommand.cs', '--line', '999', '--column', '1') `
        -ExpectedExitCode 2
    Assert-Empty -Name 'symbol-at invalid line stdout' -Text $symbolAtInvalidLine.Stdout
    Assert-Contains -Name 'symbol-at invalid line stderr' -Text $symbolAtInvalidLine.Stderr -Expected 'NAVLYN1303:'

    $symbolAtNoSymbol = Invoke-Navlyn `
        -Name 'symbol-at no symbol' `
        -Arguments @('symbol-at', '--workspace', '.\navlyn.slnx', '--file', '.\navlyn\Cli\Commands\CheckCommand.cs', '--line', '3', '--column', '1') `
        -ExpectedExitCode 2
    Assert-Empty -Name 'symbol-at no symbol stdout' -Text $symbolAtNoSymbol.Stdout
    Assert-Contains -Name 'symbol-at no symbol stderr' -Text $symbolAtNoSymbol.Stderr -Expected 'NAVLYN1304:'

    $definition = Invoke-Navlyn `
        -Name 'definition type reference' `
        -Arguments @('definition', '--workspace', '.\navlyn.slnx', '--file', '.\navlyn\Cli\NavlynCli.cs', '--line', '31', '--column', '37') `
        -ExpectedExitCode 0

    $definitionJson = $definition.Stdout | ConvertFrom-Json
    $definitionLocation = @($definitionJson.definitions)[0]
    Assert-Equal -Name 'definition file' -Actual $definitionJson.file -Expected (Join-Path 'navlyn' (Join-Path 'Cli' 'NavlynCli.cs'))
    Assert-Equal -Name 'definition line' -Actual $definitionJson.line -Expected 31
    Assert-Equal -Name 'definition column' -Actual $definitionJson.column -Expected 37
    Assert-Equal -Name 'definition symbol name' -Actual $definitionJson.symbol.name -Expected 'CheckCommand'
    Assert-Equal -Name 'definition symbol kind' -Actual $definitionJson.symbol.kind -Expected 'NamedType'
    Assert-Equal -Name 'definition symbol container' -Actual $definitionJson.symbol.container -Expected 'Navlyn.Cli.Commands'
    Assert-Equal -Name 'definition count' -Actual @($definitionJson.definitions).Count -Expected 1
    Assert-Equal -Name 'definition path' -Actual $definitionLocation.path -Expected (Join-Path 'navlyn' (Join-Path 'Cli' (Join-Path 'Commands' 'CheckCommand.cs')))
    Assert-Equal -Name 'definition declaration line' -Actual $definitionLocation.line -Expected 6
    Assert-Equal -Name 'definition declaration column' -Actual $definitionLocation.column -Expected 23
    Assert-Equal -Name 'definition declaration end line' -Actual $definitionLocation.endLine -Expected 6
    Assert-Equal -Name 'definition declaration end column' -Actual $definitionLocation.endColumn -Expected 35

    $definitionNoSource = Invoke-Navlyn `
        -Name 'definition no source' `
        -Arguments @('definition', '--workspace', '.\navlyn.slnx', '--file', '.\navlyn\Cli\NavlynCli.cs', '--line', '12', '--column', '9') `
        -ExpectedExitCode 2
    Assert-Empty -Name 'definition no source stdout' -Text $definitionNoSource.Stdout
    Assert-Contains -Name 'definition no source stderr' -Text $definitionNoSource.Stderr -Expected 'NAVLYN1305:'

    $definitionMetadata = Invoke-Navlyn `
        -Name 'definition include metadata' `
        -Arguments @('definition', '--workspace', '.\navlyn.slnx', '--file', '.\navlyn\Cli\NavlynCli.cs', '--line', '12', '--column', '9', '--include-metadata') `
        -ExpectedExitCode 0
    $definitionMetadataJson = $definitionMetadata.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'definition include metadata flag' -Actual $definitionMetadataJson.includeMetadata -Expected $true
    Assert-Equal -Name 'definition include metadata count' -Actual @($definitionMetadataJson.definitions).Count -Expected 0
    Assert-Equal -Name 'definition include metadata fact' -Actual $definitionMetadataJson.symbol.facts.isMetadata -Expected $true

    $references = Invoke-Navlyn `
        -Name 'references type reference' `
        -Arguments @('references', '--workspace', '.\navlyn.slnx', '--file', '.\navlyn\Cli\NavlynCli.cs', '--line', '31', '--column', '37') `
        -ExpectedExitCode 0

    $referencesJson = $references.Stdout | ConvertFrom-Json
    $referenceLocation = @($referencesJson.references)[0]
    Assert-Equal -Name 'references file' -Actual $referencesJson.file -Expected (Join-Path 'navlyn' (Join-Path 'Cli' 'NavlynCli.cs'))
    Assert-Equal -Name 'references line' -Actual $referencesJson.line -Expected 31
    Assert-Equal -Name 'references column' -Actual $referencesJson.column -Expected 37
    Assert-Equal -Name 'references symbol name' -Actual $referencesJson.symbol.name -Expected 'CheckCommand'
    Assert-Equal -Name 'references symbol kind' -Actual $referencesJson.symbol.kind -Expected 'NamedType'
    Assert-Equal -Name 'references symbol container' -Actual $referencesJson.symbol.container -Expected 'Navlyn.Cli.Commands'
    Assert-Equal -Name 'references count' -Actual @($referencesJson.references).Count -Expected 1
    Assert-Equal -Name 'references path' -Actual $referenceLocation.path -Expected (Join-Path 'navlyn' (Join-Path 'Cli' 'NavlynCli.cs'))
    Assert-Equal -Name 'references reference line' -Actual $referenceLocation.line -Expected 31
    Assert-Equal -Name 'references reference column' -Actual $referenceLocation.column -Expected 37
    Assert-Equal -Name 'references reference end line' -Actual $referenceLocation.endLine -Expected 31
    Assert-Equal -Name 'references reference end column' -Actual $referenceLocation.endColumn -Expected 49
    Assert-Equal -Name 'references containing symbol name' -Actual $referenceLocation.containingSymbol.name -Expected 'CreateRootCommand'
    Assert-Equal -Name 'references containing symbol kind' -Actual $referenceLocation.containingSymbol.kind -Expected 'Method'

    $find = Invoke-Navlyn `
        -Name 'find fuzzy unique type' `
        -Arguments @('find', '--workspace', '.\navlyn.slnx', '--query', 'CheckCommand', '--assume-kind', 'NamedType') `
        -ExpectedExitCode 0

    $findJson = $find.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'find fuzzy confidence' -Actual $findJson.confidence -Expected 'high'
    Assert-Equal -Name 'find fuzzy selected name' -Actual $findJson.selectedCandidate.name -Expected 'CheckCommand'
    Assert-Equal -Name 'find fuzzy selected end column' -Actual $findJson.selectedCandidate.endColumn -Expected 35
    Assert-Equal -Name 'find fuzzy reason exact' -Actual (@($findJson.selectedCandidate.reasonCodes) -contains 'exact-name-match') -Expected $true

    $whereUsed = Invoke-Navlyn `
        -Name 'where-used fuzzy references' `
        -Arguments @('where-used', '--workspace', '.\navlyn.slnx', '--query', 'CheckCommand', '--assume-kind', 'NamedType', '--limit', '1', '--include-snippets', '--snippet-lines', '0') `
        -ExpectedExitCode 0

    $whereUsedJson = $whereUsed.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'where-used fuzzy confidence' -Actual $whereUsedJson.confidence -Expected 'high'
    Assert-Equal -Name 'where-used fuzzy total matches' -Actual $whereUsedJson.totalMatches -Expected 1
    Assert-Equal -Name 'where-used fuzzy reference end column' -Actual @($whereUsedJson.references)[0].endColumn -Expected 49
    Assert-Equal -Name 'where-used fuzzy snippet line' -Actual @(@($whereUsedJson.references)[0].snippet.lines)[0] -Expected '        rootCommand.Subcommands.Add(CheckCommand.Create());'

    $about = Invoke-Navlyn `
        -Name 'about fuzzy summary' `
        -Arguments @('about', '--workspace', '.\navlyn.slnx', '--query', 'CheckCommand', '--assume-kind', 'NamedType', '--member-limit', '2', '--reference-limit', '1') `
        -ExpectedExitCode 0

    $aboutJson = $about.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'about fuzzy selected' -Actual $aboutJson.selectedCandidate.name -Expected 'CheckCommand'
    Assert-Equal -Name 'about fuzzy members returned' -Actual @($aboutJson.members.members).Count -Expected 2

    $related = Invoke-Navlyn `
        -Name 'related fuzzy files' `
        -Arguments @('related', '--workspace', '.\navlyn.slnx', '--query', 'CheckCommand', '--assume-kind', 'NamedType', '--limit', '2') `
        -ExpectedExitCode 0

    $relatedJson = $related.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'related fuzzy total files' -Actual $relatedJson.totalFiles -Expected 2
    Assert-Equal -Name 'related fuzzy first reason' -Actual @(@($relatedJson.files)[0].reasons)[0] -Expected 'declares-selected-symbol'

    $impact = Invoke-Navlyn `
        -Name 'impact fuzzy files' `
        -Arguments @('impact', '--workspace', '.\navlyn.slnx', '--query', 'CheckCommand', '--assume-kind', 'NamedType', '--limit', '2') `
        -ExpectedExitCode 0

    $impactJson = $impact.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'impact fuzzy total files' -Actual $impactJson.totalFiles -Expected 1
    Assert-Equal -Name 'impact fuzzy level' -Actual @($impactJson.files)[0].impactLevel -Expected 'direct'

    $entrypoints = Invoke-Navlyn `
        -Name 'entrypoints fuzzy no chains for type' `
        -Arguments @('entrypoints', '--workspace', '.\navlyn.slnx', '--query', 'CheckCommand', '--assume-kind', 'NamedType', '--limit', '2') `
        -ExpectedExitCode 0

    $entrypointsJson = $entrypoints.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'entrypoints fuzzy total chains' -Actual $entrypointsJson.totalChains -Expected 0

    $implementations = Invoke-Navlyn `
        -Name 'implementations non-applicable symbol' `
        -Arguments @('implementations', '--workspace', '.\navlyn.slnx', '--file', '.\navlyn\Cli\Commands\CheckCommand.cs', '--line', '6', '--column', '23') `
        -ExpectedExitCode 0

    $implementationsJson = $implementations.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'implementations file' -Actual $implementationsJson.file -Expected (Join-Path 'navlyn' (Join-Path 'Cli' (Join-Path 'Commands' 'CheckCommand.cs')))
    Assert-Equal -Name 'implementations line' -Actual $implementationsJson.line -Expected 6
    Assert-Equal -Name 'implementations column' -Actual $implementationsJson.column -Expected 23
    Assert-Equal -Name 'implementations symbol name' -Actual $implementationsJson.symbol.name -Expected 'CheckCommand'
    Assert-Equal -Name 'implementations symbol kind' -Actual $implementationsJson.symbol.kind -Expected 'NamedType'
    Assert-Equal -Name 'implementations count' -Actual @($implementationsJson.implementations).Count -Expected 0

    $callers = Invoke-Navlyn `
        -Name 'callers method declaration' `
        -Arguments @('callers', '--workspace', '.\navlyn.slnx', '--file', '.\navlyn\Cli\Commands\CheckCommand.cs', '--line', '8', '--column', '27') `
        -ExpectedExitCode 0

    $callersJson = $callers.Stdout | ConvertFrom-Json
    $callerGroup = @($callersJson.callers | Where-Object { $_.symbol.name -eq 'CreateRootCommand' })[0]
    Assert-Equal -Name 'callers file' -Actual $callersJson.file -Expected (Join-Path 'navlyn' (Join-Path 'Cli' (Join-Path 'Commands' 'CheckCommand.cs')))
    Assert-Equal -Name 'callers symbol name' -Actual $callersJson.symbol.name -Expected 'Create'
    Assert-Equal -Name 'callers symbol kind' -Actual $callersJson.symbol.kind -Expected 'Method'
    Assert-Equal -Name 'callers contains CreateRootCommand' -Actual $callerGroup.symbol.name -Expected 'CreateRootCommand'
    Assert-Equal -Name 'callers location line' -Actual @($callerGroup.locations)[0].line -Expected 31
    Assert-Equal -Name 'callers location has span' -Actual (@($callerGroup.locations)[0].endColumn -gt @($callerGroup.locations)[0].column) -Expected $true

    $calls = Invoke-Navlyn `
        -Name 'calls containing member' `
        -Arguments @('calls', '--workspace', '.\navlyn.slnx', '--file', '.\navlyn\Cli\NavlynCli.cs', '--line', '28', '--column', '32') `
        -ExpectedExitCode 0

    $callsJson = $calls.Stdout | ConvertFrom-Json
    $checkCreateCall = @($callsJson.calls | Where-Object { $_.symbol.container -eq 'Navlyn.Cli.Commands.CheckCommand' -and $_.symbol.name -eq 'Create' })[0]
    Assert-Equal -Name 'calls file' -Actual $callsJson.file -Expected (Join-Path 'navlyn' (Join-Path 'Cli' 'NavlynCli.cs'))
    Assert-Equal -Name 'calls caller name' -Actual $callsJson.caller.name -Expected 'CreateRootCommand'
    Assert-Equal -Name 'calls contains CheckCommand.Create' -Actual $checkCreateCall.symbol.name -Expected 'Create'
    Assert-Equal -Name 'calls CheckCommand.Create location line' -Actual @($checkCreateCall.locations)[0].line -Expected 31
    Assert-Equal -Name 'calls CheckCommand.Create location has span' -Actual (@($checkCreateCall.locations)[0].endColumn -gt @($checkCreateCall.locations)[0].column) -Expected $true

    $callsMetadata = Invoke-Navlyn `
        -Name 'calls include metadata' `
        -Arguments @('calls', '--workspace', '.\navlyn.slnx', '--file', '.\navlyn\Cli\NavlynCli.cs', '--line', '28', '--column', '32', '--include-metadata', '--result-kind', 'Method', '--limit', '1') `
        -ExpectedExitCode 0
    $callsMetadataJson = $callsMetadata.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'calls include metadata flag' -Actual $callsMetadataJson.includeMetadata -Expected $true
    Assert-Equal -Name 'calls include metadata limit' -Actual $callsMetadataJson.limit -Expected 1

    $missingOption = Invoke-Navlyn `
        -Name 'check missing workspace option' `
        -Arguments @('check') `
        -ExpectedExitCode 2
    Assert-Empty -Name 'missing workspace stdout' -Text $missingOption.Stdout
    Assert-Contains -Name 'missing workspace stderr' -Text $missingOption.Stderr -Expected 'NAVLYN1001:'

    $invalidExtension = Invoke-Navlyn `
        -Name 'check invalid workspace extension' `
        -Arguments @('check', '--workspace', '.\AGENTS.md') `
        -ExpectedExitCode 2
    Assert-Empty -Name 'invalid extension stdout' -Text $invalidExtension.Stdout
    Assert-Contains -Name 'invalid extension stderr' -Text $invalidExtension.Stderr -Expected 'NAVLYN1101:'

    $missingWorkspace = Invoke-Navlyn `
        -Name 'check missing workspace file' `
        -Arguments @('check', '--workspace', '.\missing.slnx') `
        -ExpectedExitCode 2
    Assert-Empty -Name 'missing workspace file stdout' -Text $missingWorkspace.Stdout
    Assert-Contains -Name 'missing workspace file stderr' -Text $missingWorkspace.Stderr -Expected 'NAVLYN1102:'

    Write-Host 'Smoke checks passed.'
}
finally {
    Pop-Location
}

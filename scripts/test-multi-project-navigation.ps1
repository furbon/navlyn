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
$FixtureSolutionPath = Join-Path $RepoRoot 'tests/fixtures/MultiProjectFixture/MultiProjectFixture.slnx'
$AppSourcePath = Join-Path $RepoRoot 'tests/fixtures/MultiProjectFixture/App/CrossProjectRunner.cs'
$LibrarySourcePath = Join-Path $RepoRoot 'tests/fixtures/MultiProjectFixture/Library/SharedWidget.cs'
$AppProjectPath = Join-Path $RepoRoot 'tests/fixtures/MultiProjectFixture/App/App.csproj'
$LibraryProjectPath = Join-Path $RepoRoot 'tests/fixtures/MultiProjectFixture/Library/Library.csproj'
$AppDisplayPath = 'tests/fixtures/MultiProjectFixture/App/CrossProjectRunner.cs'
$LibraryDisplayPath = 'tests/fixtures/MultiProjectFixture/Library/SharedWidget.cs'
$AppProjectDisplayPath = 'tests/fixtures/MultiProjectFixture/App/App.csproj'
$LibraryProjectDisplayPath = 'tests/fixtures/MultiProjectFixture/Library/Library.csproj'
$TargetFrameworkScript = Join-Path $RepoRoot 'scripts/lib/navlyn-target-framework.ps1'

. $TargetFrameworkScript

$TargetFramework = Get-NavlynPreferredTargetFramework -ProjectPath $ProjectPath
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
        [int]$ExpectedExitCode
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.WorkingDirectory = $RepoRoot
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $startInfo.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    $startInfo.UseShellExecute = $false
    $startInfo.Arguments = Join-ProcessArguments -Arguments $Arguments

    $process = [System.Diagnostics.Process]::Start($startInfo)
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
        [int]$ExpectedExitCode
    )

    $dotnetArguments = @($NavlynDll) + $Arguments

    Invoke-CheckedProcess `
        -Name $Name `
        -FilePath 'dotnet' `
        -Arguments $dotnetArguments `
        -ExpectedExitCode $ExpectedExitCode
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

function Assert-Contains {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Actual,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedSubstring
    )

    if (!$Actual.Contains($ExpectedSubstring, [StringComparison]::Ordinal)) {
        throw "$Name expected to contain '$ExpectedSubstring' but was '$Actual'."
    }
}

function Get-SourcePosition {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$LineContains,

        [Parameter(Mandatory = $true)]
        [string]$Target,

        [int]$Occurrence = 1
    )

    if ($Occurrence -lt 1) {
        throw "Occurrence must be 1 or greater. Actual value: $Occurrence."
    }

    [string[]]$lines = Get-Content -LiteralPath $Path
    for ($lineIndex = 0; $lineIndex -lt $lines.Count; $lineIndex++) {
        $lineText = $lines[$lineIndex]
        if (!$lineText.Contains($LineContains, [StringComparison]::Ordinal)) {
            continue
        }

        $searchStart = 0
        for ($matchIndex = 1; $matchIndex -le $Occurrence; $matchIndex++) {
            $columnIndex = $lineText.IndexOf($Target, $searchStart, [StringComparison]::Ordinal)
            if ($columnIndex -lt 0) {
                break
            }

            if ($matchIndex -eq $Occurrence) {
                return [pscustomobject]@{
                    Line = $lineIndex + 1
                    Column = $columnIndex + 1
                }
            }

            $searchStart = $columnIndex + $Target.Length
        }
    }

    throw "Could not find occurrence $Occurrence of '$Target' on a line containing '$LineContains'."
}

function Assert-Location {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [object]$Actual,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedPath,

        [Parameter(Mandatory = $true)]
        [object]$ExpectedPosition
    )

    Assert-Equal -Name "$Name path" -Actual $Actual.path -Expected $ExpectedPath
    Assert-Equal -Name "$Name line" -Actual $Actual.line -Expected $ExpectedPosition.Line
    Assert-Equal -Name "$Name column" -Actual $Actual.column -Expected $ExpectedPosition.Column
}

Push-Location $RepoRoot
try {
    Write-Host 'Restoring multi-project fixture...'
    Invoke-CheckedProcess `
        -Name 'dotnet restore multi-project fixture' `
        -FilePath 'dotnet' `
        -Arguments @('restore', $FixtureSolutionPath) `
        -ExpectedExitCode 0 | Out-Null

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

    Write-Host 'Running multi-project navigation checks...'

    $overview = Invoke-Navlyn `
        -Name 'overview multi-project fixture' `
        -Arguments @('overview', '--workspace', $FixtureSolutionPath) `
        -ExpectedExitCode 0

    $overviewJson = $overview.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'overview workspace' -Actual $overviewJson.workspace -Expected 'tests/fixtures/MultiProjectFixture/MultiProjectFixture.slnx'
    Assert-Equal -Name 'overview kind' -Actual $overviewJson.kind -Expected 'solution'
    Assert-Equal -Name 'overview project count' -Actual @($overviewJson.projects).Count -Expected 2
    Assert-Equal -Name 'overview first project name' -Actual @($overviewJson.projects)[0].name -Expected 'App'
    Assert-Equal -Name 'overview first project path' -Actual @($overviewJson.projects)[0].path -Expected 'tests/fixtures/MultiProjectFixture/App/App.csproj'
    Assert-Equal -Name 'overview second project name' -Actual @($overviewJson.projects)[1].name -Expected 'Library'
    Assert-Equal -Name 'overview second project path' -Actual @($overviewJson.projects)[1].path -Expected 'tests/fixtures/MultiProjectFixture/Library/Library.csproj'

    $symbolsByProjectName = Invoke-Navlyn `
        -Name 'symbols project filter by name' `
        -Arguments @(
            'symbols',
            '--workspace',
            $FixtureSolutionPath,
            '--query',
            'SharedWidget',
            '--project',
            'Library') `
        -ExpectedExitCode 0

    $symbolsByProjectNameJson = $symbolsByProjectName.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbols project filter count' -Actual $symbolsByProjectNameJson.totalMatches -Expected 1
    Assert-Equal -Name 'symbols project filter project count' -Actual @($symbolsByProjectNameJson.projects).Count -Expected 1
    Assert-Equal -Name 'symbols project filter project name' -Actual @($symbolsByProjectNameJson.projects)[0].name -Expected 'Library'
    Assert-Equal -Name 'symbols project filter project path' -Actual @($symbolsByProjectNameJson.projects)[0].path -Expected $LibraryProjectDisplayPath
    Assert-Equal -Name 'symbols project filter match name' -Actual @($symbolsByProjectNameJson.matches)[0].name -Expected 'SharedWidget'
    Assert-Equal -Name 'symbols project filter match path' -Actual @($symbolsByProjectNameJson.matches)[0].path -Expected $LibraryDisplayPath

    $symbolsByProjectPath = Invoke-Navlyn `
        -Name 'symbols project filter by path' `
        -Arguments @(
            'symbols',
            '--workspace',
            $FixtureSolutionPath,
            '--query',
            'SharedWidget',
            '--project',
            $LibraryProjectPath) `
        -ExpectedExitCode 0

    $symbolsByProjectPathJson = $symbolsByProjectPath.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbols project path filter count' -Actual $symbolsByProjectPathJson.totalMatches -Expected 1
    Assert-Equal -Name 'symbols project path filter project path' -Actual @($symbolsByProjectPathJson.projects)[0].path -Expected $LibraryProjectDisplayPath

    $unknownProject = Invoke-Navlyn `
        -Name 'symbols unknown project filter' `
        -Arguments @(
            'symbols',
            '--workspace',
            $FixtureSolutionPath,
            '--query',
            'SharedWidget',
            '--project',
            'MissingProject') `
        -ExpectedExitCode 2

    Assert-Equal -Name 'symbols unknown project stdout' -Actual $unknownProject.Stdout -Expected ''
    Assert-Contains -Name 'symbols unknown project stderr' -Actual $unknownProject.Stderr -ExpectedSubstring 'NAVLYN1006'

    $sharedWidgetUse = Get-SourcePosition `
        -Path $AppSourcePath `
        -LineContains 'SharedWidget widget = new SharedWidget();' `
        -Target 'SharedWidget'
    $sharedWidgetCreation = Get-SourcePosition `
        -Path $AppSourcePath `
        -LineContains 'SharedWidget widget = new SharedWidget();' `
        -Target 'SharedWidget' `
        -Occurrence 2
    $sharedWidgetDeclaration = Get-SourcePosition `
        -Path $LibrarySourcePath `
        -LineContains 'public sealed class SharedWidget' `
        -Target 'SharedWidget'
    $formatUse = Get-SourcePosition `
        -Path $AppSourcePath `
        -LineContains 'return widget.Format("agent");' `
        -Target 'Format'
    $formatDeclaration = Get-SourcePosition `
        -Path $LibrarySourcePath `
        -LineContains 'public string Format(string value)' `
        -Target 'Format'

    $symbolsInScoped = Invoke-Navlyn `
        -Name 'symbols-in project scoped' `
        -Arguments @(
            'symbols-in',
            '--workspace',
            $FixtureSolutionPath,
            '--project',
            'App',
            '--file',
            $AppSourcePath,
            '--line',
            [string]$sharedWidgetUse.Line) `
        -ExpectedExitCode 0

    $symbolsInScopedJson = $symbolsInScoped.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbols-in project scoped project name' -Actual $symbolsInScopedJson.project.name -Expected 'App'
    Assert-Equal -Name 'symbols-in project scoped project path' -Actual $symbolsInScopedJson.project.path -Expected $AppProjectDisplayPath
    Assert-Equal -Name 'symbols-in project scoped has symbols' -Actual (@($symbolsInScopedJson.symbols).Count -gt 0) -Expected $true

    $symbolAtType = Invoke-Navlyn `
        -Name 'symbol-at cross-project type' `
        -Arguments @(
            'symbol-at',
            '--workspace',
            $FixtureSolutionPath,
            '--file',
            $AppSourcePath,
            '--line',
            [string]$sharedWidgetUse.Line,
            '--column',
            [string]$sharedWidgetUse.Column) `
        -ExpectedExitCode 0

    $symbolAtTypeJson = $symbolAtType.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbol-at type file' -Actual $symbolAtTypeJson.file -Expected $AppDisplayPath
    Assert-Equal -Name 'symbol-at type name' -Actual $symbolAtTypeJson.symbol.name -Expected 'SharedWidget'
    Assert-Equal -Name 'symbol-at type kind' -Actual $symbolAtTypeJson.symbol.kind -Expected 'NamedType'
    Assert-Equal -Name 'symbol-at type container' -Actual $symbolAtTypeJson.symbol.container -Expected 'MultiProjectFixture.Library'
    Assert-Equal -Name 'symbol-at type declaration path' -Actual $symbolAtTypeJson.symbol.path -Expected $LibraryDisplayPath
    Assert-Equal -Name 'symbol-at type declaration line' -Actual $symbolAtTypeJson.symbol.line -Expected $sharedWidgetDeclaration.Line
    Assert-Equal -Name 'symbol-at type declaration column' -Actual $symbolAtTypeJson.symbol.column -Expected $sharedWidgetDeclaration.Column

    $symbolAtTypeScoped = Invoke-Navlyn `
        -Name 'symbol-at cross-project type scoped' `
        -Arguments @(
            'symbol-at',
            '--workspace',
            $FixtureSolutionPath,
            '--project',
            'App',
            '--file',
            $AppSourcePath,
            '--line',
            [string]$sharedWidgetUse.Line,
            '--column',
            [string]$sharedWidgetUse.Column) `
        -ExpectedExitCode 0

    $symbolAtTypeScopedJson = $symbolAtTypeScoped.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbol-at scoped project name' -Actual $symbolAtTypeScopedJson.project.name -Expected 'App'
    Assert-Equal -Name 'symbol-at scoped project path' -Actual $symbolAtTypeScopedJson.project.path -Expected $AppProjectDisplayPath
    Assert-Equal -Name 'symbol-at scoped type name' -Actual $symbolAtTypeScopedJson.symbol.name -Expected 'SharedWidget'

    $symbolAtWrongProject = Invoke-Navlyn `
        -Name 'symbol-at source file outside selected project' `
        -Arguments @(
            'symbol-at',
            '--workspace',
            $FixtureSolutionPath,
            '--project',
            'Library',
            '--file',
            $AppSourcePath,
            '--line',
            [string]$sharedWidgetUse.Line,
            '--column',
            [string]$sharedWidgetUse.Column) `
        -ExpectedExitCode 2

    Assert-Equal -Name 'symbol-at wrong project stdout' -Actual $symbolAtWrongProject.Stdout -Expected ''
    Assert-Contains -Name 'symbol-at wrong project stderr' -Actual $symbolAtWrongProject.Stderr -ExpectedSubstring 'NAVLYN1306'

    $definitionType = Invoke-Navlyn `
        -Name 'definition cross-project type' `
        -Arguments @(
            'definition',
            '--workspace',
            $FixtureSolutionPath,
            '--file',
            $AppSourcePath,
            '--line',
            [string]$sharedWidgetUse.Line,
            '--column',
            [string]$sharedWidgetUse.Column) `
        -ExpectedExitCode 0

    $definitionTypeJson = $definitionType.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'definition type symbol name' -Actual $definitionTypeJson.symbol.name -Expected 'SharedWidget'
    Assert-Equal -Name 'definition type count' -Actual @($definitionTypeJson.definitions).Count -Expected 1
    Assert-Location `
        -Name 'definition type location' `
        -Actual @($definitionTypeJson.definitions)[0] `
        -ExpectedPath $LibraryDisplayPath `
        -ExpectedPosition $sharedWidgetDeclaration

    $definitionTypeScoped = Invoke-Navlyn `
        -Name 'definition cross-project type scoped by path' `
        -Arguments @(
            'definition',
            '--workspace',
            $FixtureSolutionPath,
            '--project',
            $AppProjectPath,
            '--file',
            $AppSourcePath,
            '--line',
            [string]$sharedWidgetUse.Line,
            '--column',
            [string]$sharedWidgetUse.Column) `
        -ExpectedExitCode 0

    $definitionTypeScopedJson = $definitionTypeScoped.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'definition scoped project path' -Actual $definitionTypeScopedJson.project.path -Expected $AppProjectDisplayPath
    Assert-Equal -Name 'definition scoped symbol name' -Actual $definitionTypeScopedJson.symbol.name -Expected 'SharedWidget'

    $referencesType = Invoke-Navlyn `
        -Name 'references cross-project type' `
        -Arguments @(
            'references',
            '--workspace',
            $FixtureSolutionPath,
            '--file',
            $AppSourcePath,
            '--line',
            [string]$sharedWidgetUse.Line,
            '--column',
            [string]$sharedWidgetUse.Column) `
        -ExpectedExitCode 0

    $referencesTypeJson = $referencesType.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'references type symbol name' -Actual $referencesTypeJson.symbol.name -Expected 'SharedWidget'
    Assert-Equal -Name 'references type count' -Actual @($referencesTypeJson.references).Count -Expected 2
    Assert-Location `
        -Name 'references type first location' `
        -Actual @($referencesTypeJson.references)[0] `
        -ExpectedPath $AppDisplayPath `
        -ExpectedPosition $sharedWidgetUse
    Assert-Location `
        -Name 'references type second location' `
        -Actual @($referencesTypeJson.references)[1] `
        -ExpectedPath $AppDisplayPath `
        -ExpectedPosition $sharedWidgetCreation

    $referencesTypeScoped = Invoke-Navlyn `
        -Name 'references cross-project type scoped' `
        -Arguments @(
            'references',
            '--workspace',
            $FixtureSolutionPath,
            '--project',
            'App',
            '--file',
            $AppSourcePath,
            '--line',
            [string]$sharedWidgetUse.Line,
            '--column',
            [string]$sharedWidgetUse.Column) `
        -ExpectedExitCode 0

    $referencesTypeScopedJson = $referencesTypeScoped.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'references scoped project name' -Actual $referencesTypeScopedJson.project.name -Expected 'App'
    Assert-Equal -Name 'references scoped type count' -Actual @($referencesTypeScopedJson.references).Count -Expected 2

    $symbolAtMethod = Invoke-Navlyn `
        -Name 'symbol-at cross-project method' `
        -Arguments @(
            'symbol-at',
            '--workspace',
            $FixtureSolutionPath,
            '--file',
            $AppSourcePath,
            '--line',
            [string]$formatUse.Line,
            '--column',
            [string]$formatUse.Column) `
        -ExpectedExitCode 0

    $symbolAtMethodJson = $symbolAtMethod.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbol-at method name' -Actual $symbolAtMethodJson.symbol.name -Expected 'Format'
    Assert-Equal -Name 'symbol-at method kind' -Actual $symbolAtMethodJson.symbol.kind -Expected 'Method'
    Assert-Equal -Name 'symbol-at method container' -Actual $symbolAtMethodJson.symbol.container -Expected 'MultiProjectFixture.Library.SharedWidget'
    Assert-Equal -Name 'symbol-at method declaration path' -Actual $symbolAtMethodJson.symbol.path -Expected $LibraryDisplayPath
    Assert-Equal -Name 'symbol-at method declaration line' -Actual $symbolAtMethodJson.symbol.line -Expected $formatDeclaration.Line
    Assert-Equal -Name 'symbol-at method declaration column' -Actual $symbolAtMethodJson.symbol.column -Expected $formatDeclaration.Column

    $definitionMethod = Invoke-Navlyn `
        -Name 'definition cross-project method' `
        -Arguments @(
            'definition',
            '--workspace',
            $FixtureSolutionPath,
            '--file',
            $AppSourcePath,
            '--line',
            [string]$formatUse.Line,
            '--column',
            [string]$formatUse.Column) `
        -ExpectedExitCode 0

    $definitionMethodJson = $definitionMethod.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'definition method symbol name' -Actual $definitionMethodJson.symbol.name -Expected 'Format'
    Assert-Equal -Name 'definition method symbol kind' -Actual $definitionMethodJson.symbol.kind -Expected 'Method'
    Assert-Equal -Name 'definition method symbol container' -Actual $definitionMethodJson.symbol.container -Expected 'MultiProjectFixture.Library.SharedWidget'
    Assert-Equal -Name 'definition method count' -Actual @($definitionMethodJson.definitions).Count -Expected 1
    Assert-Location `
        -Name 'definition method location' `
        -Actual @($definitionMethodJson.definitions)[0] `
        -ExpectedPath $LibraryDisplayPath `
        -ExpectedPosition $formatDeclaration

    $referencesMethod = Invoke-Navlyn `
        -Name 'references cross-project method' `
        -Arguments @(
            'references',
            '--workspace',
            $FixtureSolutionPath,
            '--file',
            $AppSourcePath,
            '--line',
            [string]$formatUse.Line,
            '--column',
            [string]$formatUse.Column) `
        -ExpectedExitCode 0

    $referencesMethodJson = $referencesMethod.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'references method symbol name' -Actual $referencesMethodJson.symbol.name -Expected 'Format'
    Assert-Equal -Name 'references method count' -Actual @($referencesMethodJson.references).Count -Expected 1
    Assert-Location `
        -Name 'references method location' `
        -Actual @($referencesMethodJson.references)[0] `
        -ExpectedPath $AppDisplayPath `
        -ExpectedPosition $formatUse

    Write-Host 'Multi-project navigation checks passed.'
}
finally {
    Pop-Location
}

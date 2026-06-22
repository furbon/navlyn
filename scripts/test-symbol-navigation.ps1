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
$FixtureProjectPath = Join-Path $RepoRoot 'tests/fixtures/SymbolNavigationFixture/SymbolNavigationFixture.csproj'
$FixtureSourcePath = Join-Path $RepoRoot 'tests/fixtures/SymbolNavigationFixture/FixtureCode.cs'
$GeneratedSourcePath = Join-Path $RepoRoot 'tests/fixtures/SymbolNavigationFixture/GeneratedThing.g.cs'
$FixtureDisplayPath = Join-Path 'tests' (Join-Path 'fixtures' (Join-Path 'SymbolNavigationFixture' 'FixtureCode.cs'))
$GeneratedDisplayPath = Join-Path 'tests' (Join-Path 'fixtures' (Join-Path 'SymbolNavigationFixture' 'GeneratedThing.g.cs'))

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
        [string]$Text,

        [Parameter(Mandatory = $true)]
        [string]$Expected
    )

    if ($Text.IndexOf($Expected, [StringComparison]::Ordinal) -lt 0) {
        throw "$Name did not contain expected text '$Expected'. Actual text: $Text"
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

function Get-SourcePosition {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LineContains,

        [Parameter(Mandatory = $true)]
        [string]$Target,

        [int]$Occurrence = 1
    )

    if ($Occurrence -lt 1) {
        throw "Occurrence must be 1 or greater. Actual value: $Occurrence."
    }

    [string[]]$lines = Get-Content -LiteralPath $FixtureSourcePath
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
                    EndLine = $lineIndex + 1
                    EndColumn = $columnIndex + $Target.Length + 1
                }
            }

            $searchStart = $columnIndex + $Target.Length
        }
    }

    throw "Could not find occurrence $Occurrence of '$Target' on a line containing '$LineContains'."
}

function Get-SourceLine {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LineContains
    )

    [string[]]$lines = Get-Content -LiteralPath $FixtureSourcePath
    for ($lineIndex = 0; $lineIndex -lt $lines.Count; $lineIndex++) {
        if ($lines[$lineIndex].Contains($LineContains, [StringComparison]::Ordinal)) {
            return $lineIndex + 1
        }
    }

    throw "Could not find a line containing '$LineContains'."
}

function Get-SourceLineEndColumn {
    param(
        [Parameter(Mandatory = $true)]
        [int]$Line
    )

    [string[]]$lines = Get-Content -LiteralPath $FixtureSourcePath
    return $lines[$Line - 1].Length + 1
}

function New-ExpectedSymbolIn {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Kind,

        [string]$Container,

        [Parameter(Mandatory = $true)]
        [string]$LineContains,

        [Parameter(Mandatory = $true)]
        [string]$Target,

        [int]$Occurrence = 1
    )

    [pscustomobject]@{
        Name = $Name
        Kind = $Kind
        Container = $Container
        LineContains = $LineContains
        Target = $Target
        Occurrence = $Occurrence
    }
}

function Assert-SymbolsIn {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$QueryLineContains,

        [int]$StartColumn,

        [int]$EndColumn,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [object[]]$ExpectedSymbols
    )

    $line = Get-SourceLine -LineContains $QueryLineContains
    $expectedStartColumn = 1
    $expectedEndColumn = Get-SourceLineEndColumn -Line $line
    $arguments = @(
        'symbols-in',
        '--workspace',
        $FixtureProjectPath,
        '--file',
        $FixtureSourcePath,
        '--line',
        [string]$line)

    if ($PSBoundParameters.ContainsKey('StartColumn')) {
        $expectedStartColumn = $StartColumn
        $arguments += @('--start-column', [string]$StartColumn)
    }

    if ($PSBoundParameters.ContainsKey('EndColumn')) {
        $expectedEndColumn = $EndColumn
        $arguments += @('--end-column', [string]$EndColumn)
    }

    $expectedSymbolPositions = @($ExpectedSymbols | ForEach-Object {
        Get-SourcePosition `
            -LineContains $_.LineContains `
            -Target $_.Target `
            -Occurrence $_.Occurrence
    })

    $result = Invoke-Navlyn `
        -Name $Name `
        -Arguments $arguments `
        -ExpectedExitCode 0

    $json = $result.Stdout | ConvertFrom-Json
    Assert-Equal -Name "$Name file" -Actual $json.file -Expected $FixtureDisplayPath
    Assert-Equal -Name "$Name line" -Actual $json.line -Expected $line
    Assert-Equal -Name "$Name start column" -Actual $json.startColumn -Expected $expectedStartColumn
    Assert-Equal -Name "$Name end column" -Actual $json.endColumn -Expected $expectedEndColumn
    Assert-Equal -Name "$Name symbol count" -Actual @($json.symbols).Count -Expected $ExpectedSymbols.Count

    for ($symbolIndex = 0; $symbolIndex -lt $ExpectedSymbols.Count; $symbolIndex++) {
        $actualSymbol = @($json.symbols)[$symbolIndex]
        $expectedSymbol = $ExpectedSymbols[$symbolIndex]
        $expectedPosition = $expectedSymbolPositions[$symbolIndex]

        Assert-Equal -Name "$Name symbol $symbolIndex name" -Actual $actualSymbol.name -Expected $expectedSymbol.Name
        Assert-Equal -Name "$Name symbol $symbolIndex kind" -Actual $actualSymbol.kind -Expected $expectedSymbol.Kind

        if (![string]::IsNullOrEmpty($expectedSymbol.Container)) {
            Assert-Equal -Name "$Name symbol $symbolIndex container" -Actual $actualSymbol.container -Expected $expectedSymbol.Container
        }

        Assert-Equal -Name "$Name symbol $symbolIndex line" -Actual $actualSymbol.line -Expected $expectedPosition.Line
        Assert-Equal -Name "$Name symbol $symbolIndex column" -Actual $actualSymbol.column -Expected $expectedPosition.Column
        Assert-Equal -Name "$Name symbol $symbolIndex end line" -Actual $actualSymbol.endLine -Expected $expectedPosition.EndLine
        Assert-Equal -Name "$Name symbol $symbolIndex end column" -Actual $actualSymbol.endColumn -Expected $expectedPosition.EndColumn
    }
}

function Assert-SymbolAt {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$QueryLineContains,

        [Parameter(Mandatory = $true)]
        [string]$QueryTarget,

        [int]$QueryOccurrence = 1,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedName,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedKind,

        [string]$ExpectedContainer,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedDeclarationLineContains,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedDeclarationTarget,

        [int]$ExpectedDeclarationOccurrence = 1
    )

    $queryPosition = Get-SourcePosition `
        -LineContains $QueryLineContains `
        -Target $QueryTarget `
        -Occurrence $QueryOccurrence

    $expectedDeclarationPosition = Get-SourcePosition `
        -LineContains $ExpectedDeclarationLineContains `
        -Target $ExpectedDeclarationTarget `
        -Occurrence $ExpectedDeclarationOccurrence

    $result = Invoke-Navlyn `
        -Name $Name `
        -Arguments @(
            'symbol-at',
            '--workspace',
            $FixtureProjectPath,
            '--file',
            $FixtureSourcePath,
            '--line',
            [string]$queryPosition.Line,
            '--column',
            [string]$queryPosition.Column) `
        -ExpectedExitCode 0

    $json = $result.Stdout | ConvertFrom-Json
    Assert-Equal -Name "$Name file" -Actual $json.file -Expected $FixtureDisplayPath
    Assert-Equal -Name "$Name line" -Actual $json.line -Expected $queryPosition.Line
    Assert-Equal -Name "$Name column" -Actual $json.column -Expected $queryPosition.Column
    Assert-Equal -Name "$Name symbol name" -Actual $json.symbol.name -Expected $ExpectedName
    Assert-Equal -Name "$Name symbol kind" -Actual $json.symbol.kind -Expected $ExpectedKind

    if (![string]::IsNullOrEmpty($ExpectedContainer)) {
        Assert-Equal -Name "$Name symbol container" -Actual $json.symbol.container -Expected $ExpectedContainer
    }

    Assert-Equal -Name "$Name symbol path" -Actual $json.symbol.path -Expected $FixtureDisplayPath
    Assert-Equal -Name "$Name symbol line" -Actual $json.symbol.line -Expected $expectedDeclarationPosition.Line
    Assert-Equal -Name "$Name symbol column" -Actual $json.symbol.column -Expected $expectedDeclarationPosition.Column
    Assert-Equal -Name "$Name symbol end line" -Actual $json.symbol.endLine -Expected $expectedDeclarationPosition.EndLine
    Assert-Equal -Name "$Name symbol end column" -Actual $json.symbol.endColumn -Expected $expectedDeclarationPosition.EndColumn
}

function Assert-SymbolsExact {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Query,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedName,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedKind,

        [string]$ExpectedContainer,

        [Parameter(Mandatory = $true)]
        [object[]]$ExpectedDeclarations
    )

    $expectedDeclarationPositions = @($ExpectedDeclarations | ForEach-Object {
        Get-SourcePosition `
            -LineContains $_.LineContains `
            -Target $_.Target `
            -Occurrence $_.Occurrence
    })

    $result = Invoke-Navlyn `
        -Name $Name `
        -Arguments @(
            'symbols',
            '--workspace',
            $FixtureProjectPath,
            '--query',
            $Query,
            '--match',
            'exact',
            '--case-sensitive') `
        -ExpectedExitCode 0

    $json = $result.Stdout | ConvertFrom-Json
    Assert-Equal -Name "$Name query" -Actual $json.query -Expected $Query
    Assert-Equal -Name "$Name match" -Actual $json.match -Expected 'exact'
    Assert-Equal -Name "$Name case sensitivity" -Actual $json.caseSensitive -Expected $true
    Assert-Equal -Name "$Name kind filter count" -Actual @($json.kinds).Count -Expected 0
    Assert-Equal -Name "$Name limit" -Actual $json.limit -Expected $null
    Assert-Equal -Name "$Name total matches" -Actual $json.totalMatches -Expected $expectedDeclarationPositions.Count
    Assert-Equal -Name "$Name match count" -Actual @($json.matches).Count -Expected $expectedDeclarationPositions.Count

    for ($matchIndex = 0; $matchIndex -lt $expectedDeclarationPositions.Count; $matchIndex++) {
        $actualMatch = @($json.matches)[$matchIndex]
        $expectedDeclaration = $expectedDeclarationPositions[$matchIndex]

        Assert-Equal -Name "$Name match $matchIndex name" -Actual $actualMatch.name -Expected $ExpectedName
        Assert-Equal -Name "$Name match $matchIndex kind" -Actual $actualMatch.kind -Expected $ExpectedKind

        if (![string]::IsNullOrEmpty($ExpectedContainer)) {
            Assert-Equal -Name "$Name match $matchIndex container" -Actual $actualMatch.container -Expected $ExpectedContainer
        }

        Assert-Equal -Name "$Name match $matchIndex path" -Actual $actualMatch.path -Expected $FixtureDisplayPath
        Assert-Equal -Name "$Name match $matchIndex line" -Actual $actualMatch.line -Expected $expectedDeclaration.Line
        Assert-Equal -Name "$Name match $matchIndex column" -Actual $actualMatch.column -Expected $expectedDeclaration.Column
        Assert-Equal -Name "$Name match $matchIndex end line" -Actual $actualMatch.endLine -Expected $expectedDeclaration.EndLine
        Assert-Equal -Name "$Name match $matchIndex end column" -Actual $actualMatch.endColumn -Expected $expectedDeclaration.EndColumn
    }
}

function Assert-SymbolsFilteredExact {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Query,

        [Parameter(Mandatory = $true)]
        [string[]]$Kinds,

        [int]$Limit,

        [Parameter(Mandatory = $true)]
        [int]$ExpectedTotalMatches,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedName,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedKind,

        [string]$ExpectedContainer,

        [Parameter(Mandatory = $true)]
        [object[]]$ExpectedDeclarations
    )

    $expectedDeclarationPositions = @($ExpectedDeclarations | ForEach-Object {
        Get-SourcePosition `
            -LineContains $_.LineContains `
            -Target $_.Target `
            -Occurrence $_.Occurrence
    })

    $arguments = @(
        'symbols',
        '--workspace',
        $FixtureProjectPath,
        '--query',
        $Query,
        '--match',
        'exact',
        '--case-sensitive')

    foreach ($kind in $Kinds) {
        $arguments += @('--kind', $kind)
    }

    if ($PSBoundParameters.ContainsKey('Limit')) {
        $arguments += @('--limit', [string]$Limit)
    }

    $result = Invoke-Navlyn `
        -Name $Name `
        -Arguments $arguments `
        -ExpectedExitCode 0

    $json = $result.Stdout | ConvertFrom-Json
    Assert-Equal -Name "$Name query" -Actual $json.query -Expected $Query
    Assert-Equal -Name "$Name match" -Actual $json.match -Expected 'exact'
    Assert-Equal -Name "$Name case sensitivity" -Actual $json.caseSensitive -Expected $true
    Assert-Equal -Name "$Name kind filter count" -Actual @($json.kinds).Count -Expected $Kinds.Count

    for ($kindIndex = 0; $kindIndex -lt $Kinds.Count; $kindIndex++) {
        Assert-Equal -Name "$Name kind filter $kindIndex" -Actual @($json.kinds)[$kindIndex] -Expected $Kinds[$kindIndex]
    }

    if ($PSBoundParameters.ContainsKey('Limit')) {
        Assert-Equal -Name "$Name limit" -Actual $json.limit -Expected $Limit
    }
    else {
        Assert-Equal -Name "$Name limit" -Actual $json.limit -Expected $null
    }

    Assert-Equal -Name "$Name total matches" -Actual $json.totalMatches -Expected $ExpectedTotalMatches
    Assert-Equal -Name "$Name match count" -Actual @($json.matches).Count -Expected $expectedDeclarationPositions.Count

    for ($matchIndex = 0; $matchIndex -lt $expectedDeclarationPositions.Count; $matchIndex++) {
        $actualMatch = @($json.matches)[$matchIndex]
        $expectedDeclaration = $expectedDeclarationPositions[$matchIndex]

        Assert-Equal -Name "$Name match $matchIndex name" -Actual $actualMatch.name -Expected $ExpectedName
        Assert-Equal -Name "$Name match $matchIndex kind" -Actual $actualMatch.kind -Expected $ExpectedKind

        if (![string]::IsNullOrEmpty($ExpectedContainer)) {
            Assert-Equal -Name "$Name match $matchIndex container" -Actual $actualMatch.container -Expected $ExpectedContainer
        }

        Assert-Equal -Name "$Name match $matchIndex path" -Actual $actualMatch.path -Expected $FixtureDisplayPath
        Assert-Equal -Name "$Name match $matchIndex line" -Actual $actualMatch.line -Expected $expectedDeclaration.Line
        Assert-Equal -Name "$Name match $matchIndex column" -Actual $actualMatch.column -Expected $expectedDeclaration.Column
        Assert-Equal -Name "$Name match $matchIndex end line" -Actual $actualMatch.endLine -Expected $expectedDeclaration.EndLine
        Assert-Equal -Name "$Name match $matchIndex end column" -Actual $actualMatch.endColumn -Expected $expectedDeclaration.EndColumn
    }
}

function New-ExpectedDefinition {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LineContains,

        [Parameter(Mandatory = $true)]
        [string]$Target,

        [int]$Occurrence = 1
    )

    [pscustomobject]@{
        LineContains = $LineContains
        Target = $Target
        Occurrence = $Occurrence
    }
}

function Assert-Definition {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$QueryLineContains,

        [Parameter(Mandatory = $true)]
        [string]$QueryTarget,

        [int]$QueryOccurrence = 1,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedName,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedKind,

        [string]$ExpectedContainer,

        [Parameter(Mandatory = $true)]
        [object[]]$ExpectedDefinitions
    )

    $queryPosition = Get-SourcePosition `
        -LineContains $QueryLineContains `
        -Target $QueryTarget `
        -Occurrence $QueryOccurrence

    $expectedDefinitionPositions = @($ExpectedDefinitions | ForEach-Object {
        Get-SourcePosition `
            -LineContains $_.LineContains `
            -Target $_.Target `
            -Occurrence $_.Occurrence
    })

    $result = Invoke-Navlyn `
        -Name $Name `
        -Arguments @(
            'definition',
            '--workspace',
            $FixtureProjectPath,
            '--file',
            $FixtureSourcePath,
            '--line',
            [string]$queryPosition.Line,
            '--column',
            [string]$queryPosition.Column) `
        -ExpectedExitCode 0

    $json = $result.Stdout | ConvertFrom-Json
    Assert-Equal -Name "$Name file" -Actual $json.file -Expected $FixtureDisplayPath
    Assert-Equal -Name "$Name line" -Actual $json.line -Expected $queryPosition.Line
    Assert-Equal -Name "$Name column" -Actual $json.column -Expected $queryPosition.Column
    Assert-Equal -Name "$Name symbol name" -Actual $json.symbol.name -Expected $ExpectedName
    Assert-Equal -Name "$Name symbol kind" -Actual $json.symbol.kind -Expected $ExpectedKind

    if (![string]::IsNullOrEmpty($ExpectedContainer)) {
        Assert-Equal -Name "$Name symbol container" -Actual $json.symbol.container -Expected $ExpectedContainer
    }

    Assert-Equal -Name "$Name definition count" -Actual @($json.definitions).Count -Expected $expectedDefinitionPositions.Count

    for ($definitionIndex = 0; $definitionIndex -lt $expectedDefinitionPositions.Count; $definitionIndex++) {
        $actualDefinition = @($json.definitions)[$definitionIndex]
        $expectedDefinition = $expectedDefinitionPositions[$definitionIndex]

        Assert-Equal -Name "$Name definition $definitionIndex path" -Actual $actualDefinition.path -Expected $FixtureDisplayPath
        Assert-Equal -Name "$Name definition $definitionIndex line" -Actual $actualDefinition.line -Expected $expectedDefinition.Line
        Assert-Equal -Name "$Name definition $definitionIndex column" -Actual $actualDefinition.column -Expected $expectedDefinition.Column
        Assert-Equal -Name "$Name definition $definitionIndex end line" -Actual $actualDefinition.endLine -Expected $expectedDefinition.EndLine
        Assert-Equal -Name "$Name definition $definitionIndex end column" -Actual $actualDefinition.endColumn -Expected $expectedDefinition.EndColumn
    }
}

function Assert-DefinitionError {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$QueryLineContains,

        [Parameter(Mandatory = $true)]
        [string]$QueryTarget,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedDiagnostic
    )

    $queryPosition = Get-SourcePosition `
        -LineContains $QueryLineContains `
        -Target $QueryTarget

    $result = Invoke-Navlyn `
        -Name $Name `
        -Arguments @(
            'definition',
            '--workspace',
            $FixtureProjectPath,
            '--file',
            $FixtureSourcePath,
            '--line',
            [string]$queryPosition.Line,
            '--column',
            [string]$queryPosition.Column) `
        -ExpectedExitCode 2

    Assert-Empty -Name "$Name stdout" -Text $result.Stdout
    Assert-Contains -Name "$Name stderr" -Text $result.Stderr -Expected $ExpectedDiagnostic
}

function Assert-DefinitionMetadata {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$QueryLineContains,

        [Parameter(Mandatory = $true)]
        [string]$QueryTarget,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedName,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedKind,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedContainer,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedAssembly,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedDocumentationCommentId
    )

    $queryPosition = Get-SourcePosition `
        -LineContains $QueryLineContains `
        -Target $QueryTarget

    $result = Invoke-Navlyn `
        -Name $Name `
        -Arguments @(
            'definition',
            '--workspace',
            $FixtureProjectPath,
            '--file',
            $FixtureSourcePath,
            '--line',
            [string]$queryPosition.Line,
            '--column',
            [string]$queryPosition.Column,
            '--include-metadata') `
        -ExpectedExitCode 0

    $json = $result.Stdout | ConvertFrom-Json
    Assert-Equal -Name "$Name includeMetadata" -Actual $json.includeMetadata -Expected $true
    Assert-Equal -Name "$Name definition count" -Actual @($json.definitions).Count -Expected 0
    Assert-Equal -Name "$Name symbol name" -Actual $json.symbol.name -Expected $ExpectedName
    Assert-Equal -Name "$Name symbol kind" -Actual $json.symbol.kind -Expected $ExpectedKind
    Assert-Equal -Name "$Name symbol container" -Actual $json.symbol.container -Expected $ExpectedContainer
    Assert-Equal -Name "$Name metadata fact" -Actual $json.symbol.facts.isMetadata -Expected $true
    Assert-Equal -Name "$Name source fact" -Actual $json.symbol.facts.isSource -Expected $false
    Assert-Equal -Name "$Name assembly" -Actual $json.symbol.facts.assembly -Expected $ExpectedAssembly
    Assert-Equal -Name "$Name documentation comment id" -Actual $json.symbol.facts.documentationCommentId -Expected $ExpectedDocumentationCommentId
}

function Assert-References {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$QueryLineContains,

        [Parameter(Mandatory = $true)]
        [string]$QueryTarget,

        [int]$QueryOccurrence = 1,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedName,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedKind,

        [string]$ExpectedContainer,

        [Parameter(Mandatory = $true)]
        [object[]]$ExpectedReferences
    )

    $queryPosition = Get-SourcePosition `
        -LineContains $QueryLineContains `
        -Target $QueryTarget `
        -Occurrence $QueryOccurrence

    $expectedReferencePositions = @($ExpectedReferences | ForEach-Object {
        Get-SourcePosition `
            -LineContains $_.LineContains `
            -Target $_.Target `
            -Occurrence $_.Occurrence
    })

    $result = Invoke-Navlyn `
        -Name $Name `
        -Arguments @(
            'references',
            '--workspace',
            $FixtureProjectPath,
            '--file',
            $FixtureSourcePath,
            '--line',
            [string]$queryPosition.Line,
            '--column',
            [string]$queryPosition.Column) `
        -ExpectedExitCode 0

    $json = $result.Stdout | ConvertFrom-Json
    Assert-Equal -Name "$Name file" -Actual $json.file -Expected $FixtureDisplayPath
    Assert-Equal -Name "$Name line" -Actual $json.line -Expected $queryPosition.Line
    Assert-Equal -Name "$Name column" -Actual $json.column -Expected $queryPosition.Column
    Assert-Equal -Name "$Name symbol name" -Actual $json.symbol.name -Expected $ExpectedName
    Assert-Equal -Name "$Name symbol kind" -Actual $json.symbol.kind -Expected $ExpectedKind

    if (![string]::IsNullOrEmpty($ExpectedContainer)) {
        Assert-Equal -Name "$Name symbol container" -Actual $json.symbol.container -Expected $ExpectedContainer
    }

    Assert-Equal -Name "$Name reference count" -Actual @($json.references).Count -Expected $expectedReferencePositions.Count

    for ($referenceIndex = 0; $referenceIndex -lt $expectedReferencePositions.Count; $referenceIndex++) {
        $actualReference = @($json.references)[$referenceIndex]
        $expectedReference = $expectedReferencePositions[$referenceIndex]

        Assert-Equal -Name "$Name reference $referenceIndex path" -Actual $actualReference.path -Expected $FixtureDisplayPath
        Assert-Equal -Name "$Name reference $referenceIndex line" -Actual $actualReference.line -Expected $expectedReference.Line
        Assert-Equal -Name "$Name reference $referenceIndex column" -Actual $actualReference.column -Expected $expectedReference.Column
        Assert-Equal -Name "$Name reference $referenceIndex end line" -Actual $actualReference.endLine -Expected $expectedReference.EndLine
        Assert-Equal -Name "$Name reference $referenceIndex end column" -Actual $actualReference.endColumn -Expected $expectedReference.EndColumn
    }
}

function Assert-Implementations {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$QueryLineContains,

        [Parameter(Mandatory = $true)]
        [string]$QueryTarget,

        [int]$QueryOccurrence = 1,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedName,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedKind,

        [string]$ExpectedContainer,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [object[]]$ExpectedImplementations
    )

    $queryPosition = Get-SourcePosition `
        -LineContains $QueryLineContains `
        -Target $QueryTarget `
        -Occurrence $QueryOccurrence

    $expectedImplementationPositions = @($ExpectedImplementations | ForEach-Object {
        Get-SourcePosition `
            -LineContains $_.LineContains `
            -Target $_.Target `
            -Occurrence $_.Occurrence
    })

    $result = Invoke-Navlyn `
        -Name $Name `
        -Arguments @(
            'implementations',
            '--workspace',
            $FixtureProjectPath,
            '--file',
            $FixtureSourcePath,
            '--line',
            [string]$queryPosition.Line,
            '--column',
            [string]$queryPosition.Column) `
        -ExpectedExitCode 0

    $json = $result.Stdout | ConvertFrom-Json
    Assert-Equal -Name "$Name file" -Actual $json.file -Expected $FixtureDisplayPath
    Assert-Equal -Name "$Name line" -Actual $json.line -Expected $queryPosition.Line
    Assert-Equal -Name "$Name column" -Actual $json.column -Expected $queryPosition.Column
    Assert-Equal -Name "$Name symbol name" -Actual $json.symbol.name -Expected $ExpectedName
    Assert-Equal -Name "$Name symbol kind" -Actual $json.symbol.kind -Expected $ExpectedKind

    if (![string]::IsNullOrEmpty($ExpectedContainer)) {
        Assert-Equal -Name "$Name symbol container" -Actual $json.symbol.container -Expected $ExpectedContainer
    }

    Assert-Equal -Name "$Name implementation count" -Actual @($json.implementations).Count -Expected $expectedImplementationPositions.Count

    for ($implementationIndex = 0; $implementationIndex -lt $expectedImplementationPositions.Count; $implementationIndex++) {
        $actualImplementation = @($json.implementations)[$implementationIndex]
        $expectedImplementation = $expectedImplementationPositions[$implementationIndex]

        Assert-Equal -Name "$Name implementation $implementationIndex path" -Actual $actualImplementation.path -Expected $FixtureDisplayPath
        Assert-Equal -Name "$Name implementation $implementationIndex line" -Actual $actualImplementation.line -Expected $expectedImplementation.Line
        Assert-Equal -Name "$Name implementation $implementationIndex column" -Actual $actualImplementation.column -Expected $expectedImplementation.Column
        Assert-Equal -Name "$Name implementation $implementationIndex end line" -Actual $actualImplementation.endLine -Expected $expectedImplementation.EndLine
        Assert-Equal -Name "$Name implementation $implementationIndex end column" -Actual $actualImplementation.endColumn -Expected $expectedImplementation.EndColumn
    }
}

function Assert-CallersContain {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$QueryLineContains,

        [Parameter(Mandatory = $true)]
        [string]$QueryTarget,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedName,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedKind,

        [string]$ExpectedContainer,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedCallerName,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedCallerKind,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedCallerContainer,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedLocationLineContains,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedLocationTarget,

        [int]$ExpectedLocationOccurrence = 1
    )

    $queryPosition = Get-SourcePosition `
        -LineContains $QueryLineContains `
        -Target $QueryTarget

    $expectedLocation = Get-SourcePosition `
        -LineContains $ExpectedLocationLineContains `
        -Target $ExpectedLocationTarget `
        -Occurrence $ExpectedLocationOccurrence

    $result = Invoke-Navlyn `
        -Name $Name `
        -Arguments @(
            'callers',
            '--workspace',
            $FixtureProjectPath,
            '--file',
            $FixtureSourcePath,
            '--line',
            [string]$queryPosition.Line,
            '--column',
            [string]$queryPosition.Column) `
        -ExpectedExitCode 0

    $json = $result.Stdout | ConvertFrom-Json
    Assert-Equal -Name "$Name symbol name" -Actual $json.symbol.name -Expected $ExpectedName
    Assert-Equal -Name "$Name symbol kind" -Actual $json.symbol.kind -Expected $ExpectedKind

    if (![string]::IsNullOrEmpty($ExpectedContainer)) {
        Assert-Equal -Name "$Name symbol container" -Actual $json.symbol.container -Expected $ExpectedContainer
    }

    $caller = @($json.callers | Where-Object {
        $_.symbol.name -eq $ExpectedCallerName -and
        $_.symbol.kind -eq $ExpectedCallerKind -and
        $_.symbol.container -eq $ExpectedCallerContainer
    })[0]

    if ($null -eq $caller) {
        throw "$Name did not contain expected caller $ExpectedCallerContainer.$ExpectedCallerName."
    }

    $location = @($caller.locations | Where-Object {
        $_.path -eq $FixtureDisplayPath -and
        $_.line -eq $expectedLocation.Line -and
        $_.column -eq $expectedLocation.Column
    })[0]

    if ($null -eq $location) {
        throw "$Name did not contain expected caller location $($expectedLocation.Line):$($expectedLocation.Column)."
    }

    Assert-Equal -Name "$Name caller location end line" -Actual $location.endLine -Expected $expectedLocation.EndLine
    Assert-Equal -Name "$Name caller location end column" -Actual $location.endColumn -Expected $expectedLocation.EndColumn
}

function Assert-CallsContain {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$QueryLineContains,

        [Parameter(Mandatory = $true)]
        [string]$QueryTarget,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedCallerName,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedCallerKind,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedCallerContainer,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedCalleeName,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedCalleeKind,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedCalleeContainer,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedLocationLineContains,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedLocationTarget,

        [int]$ExpectedLocationOccurrence = 1
    )

    $queryPosition = Get-SourcePosition `
        -LineContains $QueryLineContains `
        -Target $QueryTarget

    $expectedLocation = Get-SourcePosition `
        -LineContains $ExpectedLocationLineContains `
        -Target $ExpectedLocationTarget `
        -Occurrence $ExpectedLocationOccurrence

    $result = Invoke-Navlyn `
        -Name $Name `
        -Arguments @(
            'calls',
            '--workspace',
            $FixtureProjectPath,
            '--file',
            $FixtureSourcePath,
            '--line',
            [string]$queryPosition.Line,
            '--column',
            [string]$queryPosition.Column) `
        -ExpectedExitCode 0

    $json = $result.Stdout | ConvertFrom-Json
    Assert-Equal -Name "$Name caller name" -Actual $json.caller.name -Expected $ExpectedCallerName
    Assert-Equal -Name "$Name caller kind" -Actual $json.caller.kind -Expected $ExpectedCallerKind
    Assert-Equal -Name "$Name caller container" -Actual $json.caller.container -Expected $ExpectedCallerContainer

    $call = @($json.calls | Where-Object {
        $_.symbol.name -eq $ExpectedCalleeName -and
        $_.symbol.kind -eq $ExpectedCalleeKind -and
        $_.symbol.container -eq $ExpectedCalleeContainer
    })[0]

    if ($null -eq $call) {
        throw "$Name did not contain expected callee $ExpectedCalleeContainer.$ExpectedCalleeName."
    }

    $locationMatches = @($call.locations | Where-Object {
        $_.path -eq $FixtureDisplayPath -and
        $_.line -eq $expectedLocation.Line -and
        $_.column -eq $expectedLocation.Column
    })
    $location = $locationMatches | Select-Object -First 1

    if ($null -eq $location) {
        throw "$Name did not contain expected call location $($expectedLocation.Line):$($expectedLocation.Column)."
    }

    Assert-Equal -Name "$Name call location has span" -Actual ($location.endLine -ge $location.line) -Expected $true
    Assert-Equal -Name "$Name call location end column advances" -Actual ($location.endColumn -gt $location.column) -Expected $true
}

function Assert-CallsMetadataBehavior {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$QueryLineContains,

        [Parameter(Mandatory = $true)]
        [string]$QueryTarget,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedCalleeName,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedCalleeKind,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedCalleeContainer,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedAssembly,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedLocationLineContains,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedLocationTarget
    )

    $queryPosition = Get-SourcePosition `
        -LineContains $QueryLineContains `
        -Target $QueryTarget

    $expectedLocation = Get-SourcePosition `
        -LineContains $ExpectedLocationLineContains `
        -Target $ExpectedLocationTarget

    $defaultResult = Invoke-Navlyn `
        -Name "$Name default source-only" `
        -Arguments @(
            'calls',
            '--workspace',
            $FixtureProjectPath,
            '--file',
            $FixtureSourcePath,
            '--line',
            [string]$queryPosition.Line,
            '--column',
            [string]$queryPosition.Column) `
        -ExpectedExitCode 0

    $defaultJson = $defaultResult.Stdout | ConvertFrom-Json
    $defaultMatches = @($defaultJson.calls | Where-Object {
        $_.symbol.name -eq $ExpectedCalleeName -and
        $_.symbol.kind -eq $ExpectedCalleeKind -and
        $_.symbol.container -eq $ExpectedCalleeContainer
    })
    Assert-Equal -Name "$Name default metadata callee count" -Actual $defaultMatches.Count -Expected 0

    $metadataResult = Invoke-Navlyn `
        -Name "$Name include metadata" `
        -Arguments @(
            'calls',
            '--workspace',
            $FixtureProjectPath,
            '--file',
            $FixtureSourcePath,
            '--line',
            [string]$queryPosition.Line,
            '--column',
            [string]$queryPosition.Column,
            '--include-metadata') `
        -ExpectedExitCode 0

    $metadataJson = $metadataResult.Stdout | ConvertFrom-Json
    Assert-Equal -Name "$Name includeMetadata" -Actual $metadataJson.includeMetadata -Expected $true

    $metadataMatches = @($metadataJson.calls | Where-Object {
        $_.symbol.name -eq $ExpectedCalleeName -and
        $_.symbol.kind -eq $ExpectedCalleeKind -and
        $_.symbol.container -eq $ExpectedCalleeContainer
    })
    $call = $metadataMatches | Select-Object -First 1

    if ($null -eq $call) {
        throw "$Name did not contain expected metadata callee $ExpectedCalleeContainer.$ExpectedCalleeName."
    }

    Assert-Equal -Name "$Name metadata fact" -Actual $call.symbol.facts.isMetadata -Expected $true
    Assert-Equal -Name "$Name source fact" -Actual $call.symbol.facts.isSource -Expected $false
    Assert-Equal -Name "$Name assembly" -Actual $call.symbol.facts.assembly -Expected $ExpectedAssembly
    Assert-Equal -Name "$Name source path" -Actual $call.symbol.path -Expected $null
    Assert-Equal -Name "$Name source line" -Actual $call.symbol.line -Expected $null
    Assert-Equal -Name "$Name source column" -Actual $call.symbol.column -Expected $null
    Assert-Equal -Name "$Name source end line" -Actual $call.symbol.endLine -Expected $null
    Assert-Equal -Name "$Name source end column" -Actual $call.symbol.endColumn -Expected $null

    $locationMatches = @($call.locations | Where-Object {
        $_.path -eq $FixtureDisplayPath -and
        $_.line -eq $expectedLocation.Line -and
        $_.column -eq $expectedLocation.Column
    })
    $location = $locationMatches | Select-Object -First 1

    if ($null -eq $location) {
        throw "$Name did not contain expected metadata call location $($expectedLocation.Line):$($expectedLocation.Column)."
    }

    Assert-Equal -Name "$Name metadata call location has span" -Actual ($location.endLine -ge $location.line) -Expected $true
    Assert-Equal -Name "$Name metadata call location end column advances" -Actual ($location.endColumn -gt $location.column) -Expected $true
}

Push-Location $RepoRoot
try {
    Write-Host 'Restoring symbol navigation fixture...'
    Invoke-CheckedProcess `
        -Name 'dotnet restore fixture' `
        -FilePath 'dotnet' `
        -Arguments @('restore', $FixtureProjectPath) `
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

    Write-Host 'Running symbol navigation checks...'

    Assert-SymbolsExact `
        -Name 'symbols partial type declarations' `
        -Query 'Widget' `
        -ExpectedName 'Widget' `
        -ExpectedKind 'NamedType' `
        -ExpectedContainer 'SymbolNavigationFixture' `
        -ExpectedDeclarations @(
            (New-ExpectedDefinition -LineContains 'public partial class Widget' -Target 'Widget'),
            (New-ExpectedDefinition -LineContains 'public partial class Widget // Factory members' -Target 'Widget')
        )

    Assert-SymbolsExact `
        -Name 'symbols alias declaration' `
        -Query 'AliasRunner' `
        -ExpectedName 'AliasRunner' `
        -ExpectedKind 'Alias' `
        -ExpectedDeclarations @(
            (New-ExpectedDefinition -LineContains 'using AliasRunner = SymbolNavigationFixture.Runner;' -Target 'AliasRunner')
        )

    Assert-SymbolsFilteredExact `
        -Name 'symbols named type kind filter' `
        -Query 'Widget' `
        -Kinds @('NamedType') `
        -ExpectedTotalMatches 2 `
        -ExpectedName 'Widget' `
        -ExpectedKind 'NamedType' `
        -ExpectedContainer 'SymbolNavigationFixture' `
        -ExpectedDeclarations @(
            (New-ExpectedDefinition -LineContains 'public partial class Widget' -Target 'Widget'),
            (New-ExpectedDefinition -LineContains 'public partial class Widget // Factory members' -Target 'Widget')
        )

    Assert-SymbolsFilteredExact `
        -Name 'symbols method kind filter with limit' `
        -Query 'Format' `
        -Kinds @('Method') `
        -Limit 1 `
        -ExpectedTotalMatches 2 `
        -ExpectedName 'Format' `
        -ExpectedKind 'Method' `
        -ExpectedContainer 'SymbolNavigationFixture.Widget' `
        -ExpectedDeclarations @(
            (New-ExpectedDefinition -LineContains 'public string Format(int count)' -Target 'Format')
        )

    Assert-SymbolsFilteredExact `
        -Name 'symbols alias kind filter' `
        -Query 'AliasRunner' `
        -Kinds @('Alias') `
        -ExpectedTotalMatches 1 `
        -ExpectedName 'AliasRunner' `
        -ExpectedKind 'Alias' `
        -ExpectedDeclarations @(
            (New-ExpectedDefinition -LineContains 'using AliasRunner = SymbolNavigationFixture.Runner;' -Target 'AliasRunner')
        )

    Assert-SymbolsFilteredExact `
        -Name 'symbols parameter kind filter' `
        -Query 'count' `
        -Kinds @('Parameter') `
        -ExpectedTotalMatches 1 `
        -ExpectedName 'count' `
        -ExpectedKind 'Parameter' `
        -ExpectedContainer 'SymbolNavigationFixture.Widget.Format(int)' `
        -ExpectedDeclarations @(
            (New-ExpectedDefinition -LineContains 'public string Format(int count)' -Target 'count')
        )

    Assert-SymbolsFilteredExact `
        -Name 'symbols local kind filter' `
        -Query 'formatted' `
        -Kinds @('Local') `
        -ExpectedTotalMatches 1 `
        -ExpectedName 'formatted' `
        -ExpectedKind 'Local' `
        -ExpectedContainer 'SymbolNavigationFixture.Runner.Run()' `
        -ExpectedDeclarations @(
            (New-ExpectedDefinition -LineContains 'string formatted = widget.Format(3);' -Target 'formatted')
        )

    $generatedSymbols = Invoke-Navlyn `
        -Name 'symbols include generated by default' `
        -Arguments @(
            'symbols',
            '--workspace',
            $FixtureProjectPath,
            '--query',
            'GeneratedThing',
            '--match',
            'exact',
            '--case-sensitive') `
        -ExpectedExitCode 0

    $generatedSymbolsJson = $generatedSymbols.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbols generated default total' -Actual $generatedSymbolsJson.totalMatches -Expected 1
    Assert-Equal -Name 'symbols generated default path' -Actual @($generatedSymbolsJson.matches)[0].path -Expected $GeneratedDisplayPath

    $excludedGeneratedSymbols = Invoke-Navlyn `
        -Name 'symbols exclude generated' `
        -Arguments @(
            'symbols',
            '--workspace',
            $FixtureProjectPath,
            '--query',
            'GeneratedThing',
            '--match',
            'exact',
            '--case-sensitive',
            '--exclude-generated') `
        -ExpectedExitCode 0

    $excludedGeneratedSymbolsJson = $excludedGeneratedSymbols.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbols exclude generated flag' -Actual $excludedGeneratedSymbolsJson.excludeGenerated -Expected $true
    Assert-Equal -Name 'symbols exclude generated total' -Actual $excludedGeneratedSymbolsJson.totalMatches -Expected 0

    $generatedSymbolAtExcluded = Invoke-Navlyn `
        -Name 'symbol-at generated source excluded' `
        -Arguments @(
            'symbol-at',
            '--workspace',
            $FixtureProjectPath,
            '--file',
            $GeneratedSourcePath,
            '--line',
            '3',
            '--column',
            '21',
            '--exclude-generated') `
        -ExpectedExitCode 2

    Assert-Empty -Name 'symbol-at generated source excluded stdout' -Text $generatedSymbolAtExcluded.Stdout
    Assert-Contains -Name 'symbol-at generated source excluded stderr' -Text $generatedSymbolAtExcluded.Stderr -Expected 'NAVLYN1307:'

    Assert-SymbolsIn `
        -Name 'symbols-in full line' `
        -QueryLineContains 'string formatted = widget.Format(3);' `
        -ExpectedSymbols @(
            (New-ExpectedSymbolIn `
                -Name 'formatted' `
                -Kind 'Local' `
                -Container 'SymbolNavigationFixture.Runner.Run()' `
                -LineContains 'string formatted = widget.Format(3);' `
                -Target 'formatted'),
            (New-ExpectedSymbolIn `
                -Name 'widget' `
                -Kind 'Local' `
                -Container 'SymbolNavigationFixture.Runner.Run()' `
                -LineContains 'string formatted = widget.Format(3);' `
                -Target 'widget'),
            (New-ExpectedSymbolIn `
                -Name 'Format' `
                -Kind 'Method' `
                -Container 'SymbolNavigationFixture.Widget' `
                -LineContains 'string formatted = widget.Format(3);' `
                -Target 'Format')
        )

    Assert-SymbolsIn `
        -Name 'symbols-in column span' `
        -QueryLineContains 'string formatted = widget.Format(3);' `
        -StartColumn 25 `
        -EndColumn 39 `
        -ExpectedSymbols @(
            (New-ExpectedSymbolIn `
                -Name 'widget' `
                -Kind 'Local' `
                -Container 'SymbolNavigationFixture.Runner.Run()' `
                -LineContains 'string formatted = widget.Format(3);' `
                -Target 'widget'),
            (New-ExpectedSymbolIn `
                -Name 'Format' `
                -Kind 'Method' `
                -Container 'SymbolNavigationFixture.Widget' `
                -LineContains 'string formatted = widget.Format(3);' `
                -Target 'Format')
        )

    Assert-SymbolsIn `
        -Name 'symbols-in no symbols' `
        -QueryLineContains '    }' `
        -ExpectedSymbols @()

    Assert-SymbolsIn `
        -Name 'symbols-in remains identifier-only on operator line' `
        -QueryLineContains 'NumberBox total = first + second;' `
        -ExpectedSymbols @(
            (New-ExpectedSymbolIn `
                -Name 'NumberBox' `
                -Kind 'NamedType' `
                -Container 'SymbolNavigationFixture' `
                -LineContains 'NumberBox total = first + second;' `
                -Target 'NumberBox'),
            (New-ExpectedSymbolIn `
                -Name 'total' `
                -Kind 'Local' `
                -Container 'SymbolNavigationFixture.SemanticEdgeCases.Exercise()' `
                -LineContains 'NumberBox total = first + second;' `
                -Target 'total'),
            (New-ExpectedSymbolIn `
                -Name 'first' `
                -Kind 'Local' `
                -Container 'SymbolNavigationFixture.SemanticEdgeCases.Exercise()' `
                -LineContains 'NumberBox total = first + second;' `
                -Target 'first'),
            (New-ExpectedSymbolIn `
                -Name 'second' `
                -Kind 'Local' `
                -Container 'SymbolNavigationFixture.SemanticEdgeCases.Exercise()' `
                -LineContains 'NumberBox total = first + second;' `
                -Target 'second')
        )

    Assert-SymbolAt `
        -Name 'symbol-at type reference' `
        -QueryLineContains 'Widget widget = Widget.CreateDefault();' `
        -QueryTarget 'Widget' `
        -ExpectedName 'Widget' `
        -ExpectedKind 'NamedType' `
        -ExpectedContainer 'SymbolNavigationFixture' `
        -ExpectedDeclarationLineContains 'public partial class Widget' `
        -ExpectedDeclarationTarget 'Widget'

    Assert-SymbolAt `
        -Name 'symbol-at static method reference' `
        -QueryLineContains 'Widget widget = Widget.CreateDefault();' `
        -QueryTarget 'CreateDefault' `
        -ExpectedName 'CreateDefault' `
        -ExpectedKind 'Method' `
        -ExpectedContainer 'SymbolNavigationFixture.Widget' `
        -ExpectedDeclarationLineContains 'public static Widget CreateDefault()' `
        -ExpectedDeclarationTarget 'CreateDefault'

    Assert-SymbolAt `
        -Name 'symbol-at object creation type reference' `
        -QueryLineContains 'return new Widget("default");' `
        -QueryTarget 'Widget' `
        -ExpectedName 'Widget' `
        -ExpectedKind 'NamedType' `
        -ExpectedContainer 'SymbolNavigationFixture' `
        -ExpectedDeclarationLineContains 'public partial class Widget' `
        -ExpectedDeclarationTarget 'Widget'

    Assert-SymbolAt `
        -Name 'symbol-at extension method reference' `
        -QueryLineContains 'string extensionFormatted = widget.Describe();' `
        -QueryTarget 'Describe' `
        -ExpectedName 'Describe' `
        -ExpectedKind 'Method' `
        -ExpectedContainer 'SymbolNavigationFixture.WidgetExtensions' `
        -ExpectedDeclarationLineContains 'public static string Describe(this Widget widget)' `
        -ExpectedDeclarationTarget 'Describe'

    Assert-SymbolAt `
        -Name 'symbol-at using static method reference' `
        -QueryLineContains 'string staticFormatted = Label(widget);' `
        -QueryTarget 'Label' `
        -ExpectedName 'Label' `
        -ExpectedKind 'Method' `
        -ExpectedContainer 'SymbolNavigationFixture.WidgetText' `
        -ExpectedDeclarationLineContains 'public static string Label(Widget widget)' `
        -ExpectedDeclarationTarget 'Label'

    Assert-SymbolAt `
        -Name 'symbol-at overloaded method reference' `
        -QueryLineContains 'string formatted = widget.Format(3);' `
        -QueryTarget 'Format' `
        -ExpectedName 'Format' `
        -ExpectedKind 'Method' `
        -ExpectedContainer 'SymbolNavigationFixture.Widget' `
        -ExpectedDeclarationLineContains 'public string Format(int count)' `
        -ExpectedDeclarationTarget 'Format'

    Assert-SymbolAt `
        -Name 'symbol-at property reference' `
        -QueryLineContains 'return $"{Name}:{count}";' `
        -QueryTarget 'Name' `
        -ExpectedName 'Name' `
        -ExpectedKind 'Property' `
        -ExpectedContainer 'SymbolNavigationFixture.Widget' `
        -ExpectedDeclarationLineContains 'public string Name { get; }' `
        -ExpectedDeclarationTarget 'Name'

    Assert-SymbolAt `
        -Name 'symbol-at parameter reference' `
        -QueryLineContains 'return $"{Name}:{count}";' `
        -QueryTarget 'count' `
        -ExpectedName 'count' `
        -ExpectedKind 'Parameter' `
        -ExpectedContainer 'SymbolNavigationFixture.Widget.Format(int)' `
        -ExpectedDeclarationLineContains 'public string Format(int count)' `
        -ExpectedDeclarationTarget 'count'

    Assert-SymbolAt `
        -Name 'symbol-at local reference' `
        -QueryLineContains 'return $"{formatted}|{extensionFormatted}|{staticFormatted}";' `
        -QueryTarget 'formatted' `
        -ExpectedName 'formatted' `
        -ExpectedKind 'Local' `
        -ExpectedContainer 'SymbolNavigationFixture.Runner.Run()' `
        -ExpectedDeclarationLineContains 'string formatted = widget.Format(3);' `
        -ExpectedDeclarationTarget 'formatted'

    Assert-SymbolAt `
        -Name 'symbol-at partial declaration' `
        -QueryLineContains 'public partial class Widget // Factory members' `
        -QueryTarget 'Widget' `
        -ExpectedName 'Widget' `
        -ExpectedKind 'NamedType' `
        -ExpectedContainer 'SymbolNavigationFixture' `
        -ExpectedDeclarationLineContains 'public partial class Widget // Factory members' `
        -ExpectedDeclarationTarget 'Widget'

    Assert-SymbolAt `
        -Name 'symbol-at alias reference' `
        -QueryLineContains 'public AliasRunner CreateRunner()' `
        -QueryTarget 'AliasRunner' `
        -ExpectedName 'AliasRunner' `
        -ExpectedKind 'Alias' `
        -ExpectedDeclarationLineContains 'using AliasRunner = SymbolNavigationFixture.Runner;' `
        -ExpectedDeclarationTarget 'AliasRunner'

    Assert-SymbolAt `
        -Name 'symbol-at constructed generic type' `
        -QueryLineContains 'GenericBox<int> box = new GenericBox<int>(1);' `
        -QueryTarget 'GenericBox' `
        -ExpectedName 'GenericBox' `
        -ExpectedKind 'NamedType' `
        -ExpectedContainer 'SymbolNavigationFixture' `
        -ExpectedDeclarationLineContains 'public sealed class GenericBox<T>' `
        -ExpectedDeclarationTarget 'GenericBox'

    Assert-SymbolAt `
        -Name 'symbol-at generic method call' `
        -QueryLineContains 'int echoed = Echo<int>(box.Value);' `
        -QueryTarget 'Echo' `
        -ExpectedName 'Echo' `
        -ExpectedKind 'Method' `
        -ExpectedContainer 'SymbolNavigationFixture.SemanticEdgeCases' `
        -ExpectedDeclarationLineContains 'public T Echo<T>(T value)' `
        -ExpectedDeclarationTarget 'Echo'

    Assert-SymbolAt `
        -Name 'symbol-at optional parameter method call' `
        -QueryLineContains 'string optional = FormatOptional();' `
        -QueryTarget 'FormatOptional' `
        -ExpectedName 'FormatOptional' `
        -ExpectedKind 'Method' `
        -ExpectedContainer 'SymbolNavigationFixture.SemanticEdgeCases' `
        -ExpectedDeclarationLineContains 'public string FormatOptional(string text = "optional")' `
        -ExpectedDeclarationTarget 'FormatOptional'

    Assert-SymbolAt `
        -Name 'symbol-at params method call' `
        -QueryLineContains 'string joined = JoinItems("a", "b");' `
        -QueryTarget 'JoinItems' `
        -ExpectedName 'JoinItems' `
        -ExpectedKind 'Method' `
        -ExpectedContainer 'SymbolNavigationFixture.SemanticEdgeCases' `
        -ExpectedDeclarationLineContains 'public string JoinItems(params string[] items)' `
        -ExpectedDeclarationTarget 'JoinItems'

    Assert-SymbolAt `
        -Name 'symbol-at local function call' `
        -QueryLineContains 'string local = BuildLocal("local");' `
        -QueryTarget 'BuildLocal' `
        -ExpectedName 'BuildLocal' `
        -ExpectedKind 'Method' `
        -ExpectedContainer 'SymbolNavigationFixture.SemanticEdgeCases.Exercise()' `
        -ExpectedDeclarationLineContains 'static string BuildLocal(string value)' `
        -ExpectedDeclarationTarget 'BuildLocal'

    Assert-SymbolAt `
        -Name 'symbol-at lambda parameter reference' `
        -QueryLineContains 'Func<string, string> normalize = input => input.Trim();' `
        -QueryTarget 'input' `
        -QueryOccurrence 2 `
        -ExpectedName 'input' `
        -ExpectedKind 'Parameter' `
        -ExpectedContainer 'lambda expression' `
        -ExpectedDeclarationLineContains 'Func<string, string> normalize = input => input.Trim();' `
        -ExpectedDeclarationTarget 'input'

    Assert-SymbolAt `
        -Name 'symbol-at pattern variable reference' `
        -QueryLineContains 'string patternName = candidate is Widget matchedWidget' `
        -QueryTarget 'matchedWidget' `
        -QueryOccurrence 2 `
        -ExpectedName 'matchedWidget' `
        -ExpectedKind 'Local' `
        -ExpectedContainer 'SymbolNavigationFixture.SemanticEdgeCases.Exercise()' `
        -ExpectedDeclarationLineContains 'string patternName = candidate is Widget matchedWidget' `
        -ExpectedDeclarationTarget 'matchedWidget'

    Assert-SymbolAt `
        -Name 'symbol-at deconstruction variable reference' `
        -QueryLineContains 'return $"{echoed}|{optional}|{joined}|{normalized}|{directTrimmed}|{patternName}|{viaInterface}|{rendered}|{left}|{right}' `
        -QueryTarget 'left' `
        -ExpectedName 'left' `
        -ExpectedKind 'Local' `
        -ExpectedContainer 'SymbolNavigationFixture.SemanticEdgeCases.Exercise()' `
        -ExpectedDeclarationLineContains 'var (left, right) = CreatePair();' `
        -ExpectedDeclarationTarget 'left'

    Assert-SymbolAt `
        -Name 'symbol-at overloaded operator reference' `
        -QueryLineContains 'NumberBox total = first + second;' `
        -QueryTarget '+' `
        -ExpectedName 'op_Addition' `
        -ExpectedKind 'Method' `
        -ExpectedContainer 'SymbolNavigationFixture.NumberBox' `
        -ExpectedDeclarationLineContains 'public static NumberBox operator +(NumberBox left, NumberBox right)' `
        -ExpectedDeclarationTarget '+'

    Assert-SymbolAt `
        -Name 'symbol-at indexer declaration' `
        -QueryLineContains 'public int this[int index]' `
        -QueryTarget 'this' `
        -ExpectedName 'this[]' `
        -ExpectedKind 'Property' `
        -ExpectedContainer 'SymbolNavigationFixture.NumberBox' `
        -ExpectedDeclarationLineContains 'public int this[int index]' `
        -ExpectedDeclarationTarget 'this'

    Assert-SymbolAt `
        -Name 'symbol-at conversion operator declaration' `
        -QueryLineContains 'public static implicit operator int(NumberBox box)' `
        -QueryTarget 'operator' `
        -ExpectedName 'op_Implicit' `
        -ExpectedKind 'Method' `
        -ExpectedContainer 'SymbolNavigationFixture.NumberBox' `
        -ExpectedDeclarationLineContains 'public static implicit operator int(NumberBox box)' `
        -ExpectedDeclarationTarget 'int'

    Assert-SymbolAt `
        -Name 'symbol-at event subscription reference' `
        -QueryLineContains 'source.Changed += HandleChanged;' `
        -QueryTarget 'Changed' `
        -ExpectedName 'Changed' `
        -ExpectedKind 'Event' `
        -ExpectedContainer 'SymbolNavigationFixture.SemanticEdgeCases' `
        -ExpectedDeclarationLineContains 'public event EventHandler? Changed;' `
        -ExpectedDeclarationTarget 'Changed'

    Assert-SymbolAt `
        -Name 'symbol-at record positional property reference' `
        -QueryLineContains 'string recordName = record.Name;' `
        -QueryTarget 'Name' `
        -QueryOccurrence 2 `
        -ExpectedName 'Name' `
        -ExpectedKind 'Property' `
        -ExpectedContainer 'SymbolNavigationFixture.WidgetRecord' `
        -ExpectedDeclarationLineContains 'public record WidgetRecord(string Name, int Count);' `
        -ExpectedDeclarationTarget 'Name'

    Assert-SymbolAt `
        -Name 'symbol-at primary constructor property reference' `
        -QueryLineContains 'string primaryName = primary.Name;' `
        -QueryTarget 'Name' `
        -QueryOccurrence 2 `
        -ExpectedName 'Name' `
        -ExpectedKind 'Property' `
        -ExpectedContainer 'SymbolNavigationFixture.PrimaryWidget' `
        -ExpectedDeclarationLineContains 'public string Name => name;' `
        -ExpectedDeclarationTarget 'Name'

    Assert-SymbolAt `
        -Name 'symbol-at property get accessor normalizes to property' `
        -QueryLineContains '        get' `
        -QueryTarget 'get' `
        -ExpectedName 'ExplicitCounter' `
        -ExpectedKind 'Property' `
        -ExpectedContainer 'SymbolNavigationFixture.SemanticEdgeCases' `
        -ExpectedDeclarationLineContains 'public int ExplicitCounter' `
        -ExpectedDeclarationTarget 'ExplicitCounter'

    Assert-SymbolAt `
        -Name 'symbol-at property set accessor normalizes to property' `
        -QueryLineContains '        set' `
        -QueryTarget 'set' `
        -ExpectedName 'ExplicitCounter' `
        -ExpectedKind 'Property' `
        -ExpectedContainer 'SymbolNavigationFixture.SemanticEdgeCases' `
        -ExpectedDeclarationLineContains 'public int ExplicitCounter' `
        -ExpectedDeclarationTarget 'ExplicitCounter'

    Assert-SymbolAt `
        -Name 'symbol-at event add accessor normalizes to event' `
        -QueryLineContains '        add' `
        -QueryTarget 'add' `
        -ExpectedName 'ExplicitChanged' `
        -ExpectedKind 'Event' `
        -ExpectedContainer 'SymbolNavigationFixture.SemanticEdgeCases' `
        -ExpectedDeclarationLineContains 'public event EventHandler? ExplicitChanged' `
        -ExpectedDeclarationTarget 'ExplicitChanged'

    Assert-SymbolAt `
        -Name 'symbol-at event remove accessor normalizes to event' `
        -QueryLineContains '        remove' `
        -QueryTarget 'remove' `
        -ExpectedName 'ExplicitChanged' `
        -ExpectedKind 'Event' `
        -ExpectedContainer 'SymbolNavigationFixture.SemanticEdgeCases' `
        -ExpectedDeclarationLineContains 'public event EventHandler? ExplicitChanged' `
        -ExpectedDeclarationTarget 'ExplicitChanged'

    Assert-SymbolAt `
        -Name 'symbol-at shortened attribute reference' `
        -QueryLineContains '[WidgetMarker]' `
        -QueryTarget 'WidgetMarker' `
        -ExpectedName '.ctor' `
        -ExpectedKind 'Method' `
        -ExpectedContainer 'SymbolNavigationFixture.WidgetMarkerAttribute' `
        -ExpectedDeclarationLineContains 'public sealed class WidgetMarkerAttribute : Attribute;' `
        -ExpectedDeclarationTarget 'WidgetMarkerAttribute'

    Assert-Definition `
        -Name 'definition type reference' `
        -QueryLineContains 'Widget widget = Widget.CreateDefault();' `
        -QueryTarget 'Widget' `
        -ExpectedName 'Widget' `
        -ExpectedKind 'NamedType' `
        -ExpectedContainer 'SymbolNavigationFixture' `
        -ExpectedDefinitions @(
            (New-ExpectedDefinition -LineContains 'public partial class Widget' -Target 'Widget'),
            (New-ExpectedDefinition -LineContains 'public partial class Widget // Factory members' -Target 'Widget')
        )

    Assert-Definition `
        -Name 'definition static method reference' `
        -QueryLineContains 'Widget widget = Widget.CreateDefault();' `
        -QueryTarget 'CreateDefault' `
        -ExpectedName 'CreateDefault' `
        -ExpectedKind 'Method' `
        -ExpectedContainer 'SymbolNavigationFixture.Widget' `
        -ExpectedDefinitions @(
            (New-ExpectedDefinition -LineContains 'public static Widget CreateDefault()' -Target 'CreateDefault')
        )

    Assert-Definition `
        -Name 'definition object creation type reference' `
        -QueryLineContains 'return new Widget("default");' `
        -QueryTarget 'Widget' `
        -ExpectedName 'Widget' `
        -ExpectedKind 'NamedType' `
        -ExpectedContainer 'SymbolNavigationFixture' `
        -ExpectedDefinitions @(
            (New-ExpectedDefinition -LineContains 'public partial class Widget' -Target 'Widget'),
            (New-ExpectedDefinition -LineContains 'public partial class Widget // Factory members' -Target 'Widget')
        )

    Assert-Definition `
        -Name 'definition extension method reference' `
        -QueryLineContains 'string extensionFormatted = widget.Describe();' `
        -QueryTarget 'Describe' `
        -ExpectedName 'Describe' `
        -ExpectedKind 'Method' `
        -ExpectedContainer 'SymbolNavigationFixture.WidgetExtensions' `
        -ExpectedDefinitions @(
            (New-ExpectedDefinition -LineContains 'public static string Describe(this Widget widget)' -Target 'Describe')
        )

    Assert-Definition `
        -Name 'definition using static method reference' `
        -QueryLineContains 'string staticFormatted = Label(widget);' `
        -QueryTarget 'Label' `
        -ExpectedName 'Label' `
        -ExpectedKind 'Method' `
        -ExpectedContainer 'SymbolNavigationFixture.WidgetText' `
        -ExpectedDefinitions @(
            (New-ExpectedDefinition -LineContains 'public static string Label(Widget widget)' -Target 'Label')
        )

    Assert-Definition `
        -Name 'definition overloaded method reference' `
        -QueryLineContains 'string formatted = widget.Format(3);' `
        -QueryTarget 'Format' `
        -ExpectedName 'Format' `
        -ExpectedKind 'Method' `
        -ExpectedContainer 'SymbolNavigationFixture.Widget' `
        -ExpectedDefinitions @(
            (New-ExpectedDefinition -LineContains 'public string Format(int count)' -Target 'Format')
        )

    Assert-Definition `
        -Name 'definition property reference' `
        -QueryLineContains 'return $"{Name}:{count}";' `
        -QueryTarget 'Name' `
        -ExpectedName 'Name' `
        -ExpectedKind 'Property' `
        -ExpectedContainer 'SymbolNavigationFixture.Widget' `
        -ExpectedDefinitions @(
            (New-ExpectedDefinition -LineContains 'public string Name { get; }' -Target 'Name')
        )

    Assert-Definition `
        -Name 'definition parameter reference' `
        -QueryLineContains 'return $"{Name}:{count}";' `
        -QueryTarget 'count' `
        -ExpectedName 'count' `
        -ExpectedKind 'Parameter' `
        -ExpectedContainer 'SymbolNavigationFixture.Widget.Format(int)' `
        -ExpectedDefinitions @(
            (New-ExpectedDefinition -LineContains 'public string Format(int count)' -Target 'count')
        )

    Assert-Definition `
        -Name 'definition local reference' `
        -QueryLineContains 'return $"{formatted}|{extensionFormatted}|{staticFormatted}";' `
        -QueryTarget 'formatted' `
        -ExpectedName 'formatted' `
        -ExpectedKind 'Local' `
        -ExpectedContainer 'SymbolNavigationFixture.Runner.Run()' `
        -ExpectedDefinitions @(
            (New-ExpectedDefinition -LineContains 'string formatted = widget.Format(3);' -Target 'formatted')
        )

    Assert-Definition `
        -Name 'definition alias reference' `
        -QueryLineContains 'public AliasRunner CreateRunner()' `
        -QueryTarget 'AliasRunner' `
        -ExpectedName 'AliasRunner' `
        -ExpectedKind 'Alias' `
        -ExpectedDefinitions @(
            (New-ExpectedDefinition -LineContains 'using AliasRunner = SymbolNavigationFixture.Runner;' -Target 'AliasRunner')
        )

    Assert-Definition `
        -Name 'definition generic method call' `
        -QueryLineContains 'int echoed = Echo<int>(box.Value);' `
        -QueryTarget 'Echo' `
        -ExpectedName 'Echo' `
        -ExpectedKind 'Method' `
        -ExpectedContainer 'SymbolNavigationFixture.SemanticEdgeCases' `
        -ExpectedDefinitions @(
            (New-ExpectedDefinition -LineContains 'public T Echo<T>(T value)' -Target 'Echo')
        )

    Assert-Definition `
        -Name 'definition optional parameter method call' `
        -QueryLineContains 'string optional = FormatOptional();' `
        -QueryTarget 'FormatOptional' `
        -ExpectedName 'FormatOptional' `
        -ExpectedKind 'Method' `
        -ExpectedContainer 'SymbolNavigationFixture.SemanticEdgeCases' `
        -ExpectedDefinitions @(
            (New-ExpectedDefinition -LineContains 'public string FormatOptional(string text = "optional")' -Target 'FormatOptional')
        )

    Assert-Definition `
        -Name 'definition params method call' `
        -QueryLineContains 'string joined = JoinItems("a", "b");' `
        -QueryTarget 'JoinItems' `
        -ExpectedName 'JoinItems' `
        -ExpectedKind 'Method' `
        -ExpectedContainer 'SymbolNavigationFixture.SemanticEdgeCases' `
        -ExpectedDefinitions @(
            (New-ExpectedDefinition -LineContains 'public string JoinItems(params string[] items)' -Target 'JoinItems')
        )

    Assert-Definition `
        -Name 'definition local function call' `
        -QueryLineContains 'string local = BuildLocal("local");' `
        -QueryTarget 'BuildLocal' `
        -ExpectedName 'BuildLocal' `
        -ExpectedKind 'Method' `
        -ExpectedContainer 'SymbolNavigationFixture.SemanticEdgeCases.Exercise()' `
        -ExpectedDefinitions @(
            (New-ExpectedDefinition -LineContains 'static string BuildLocal(string value)' -Target 'BuildLocal')
        )

    Assert-Definition `
        -Name 'definition lambda parameter reference' `
        -QueryLineContains 'Func<string, string> normalize = input => input.Trim();' `
        -QueryTarget 'input' `
        -QueryOccurrence 2 `
        -ExpectedName 'input' `
        -ExpectedKind 'Parameter' `
        -ExpectedContainer 'lambda expression' `
        -ExpectedDefinitions @(
            (New-ExpectedDefinition -LineContains 'Func<string, string> normalize = input => input.Trim();' -Target 'input')
        )

    Assert-Definition `
        -Name 'definition pattern variable reference' `
        -QueryLineContains 'string patternName = candidate is Widget matchedWidget' `
        -QueryTarget 'matchedWidget' `
        -QueryOccurrence 2 `
        -ExpectedName 'matchedWidget' `
        -ExpectedKind 'Local' `
        -ExpectedContainer 'SymbolNavigationFixture.SemanticEdgeCases.Exercise()' `
        -ExpectedDefinitions @(
            (New-ExpectedDefinition -LineContains 'string patternName = candidate is Widget matchedWidget' -Target 'matchedWidget')
        )

    Assert-Definition `
        -Name 'definition deconstruction variable reference' `
        -QueryLineContains 'return $"{echoed}|{optional}|{joined}|{normalized}|{directTrimmed}|{patternName}|{viaInterface}|{rendered}|{left}|{right}' `
        -QueryTarget 'left' `
        -ExpectedName 'left' `
        -ExpectedKind 'Local' `
        -ExpectedContainer 'SymbolNavigationFixture.SemanticEdgeCases.Exercise()' `
        -ExpectedDefinitions @(
            (New-ExpectedDefinition -LineContains 'var (left, right) = CreatePair();' -Target 'left')
        )

    Assert-Definition `
        -Name 'definition overloaded operator reference' `
        -QueryLineContains 'NumberBox total = first + second;' `
        -QueryTarget '+' `
        -ExpectedName 'op_Addition' `
        -ExpectedKind 'Method' `
        -ExpectedContainer 'SymbolNavigationFixture.NumberBox' `
        -ExpectedDefinitions @(
            (New-ExpectedDefinition -LineContains 'public static NumberBox operator +(NumberBox left, NumberBox right)' -Target '+')
        )

    Assert-Definition `
        -Name 'definition event subscription reference' `
        -QueryLineContains 'source.Changed += HandleChanged;' `
        -QueryTarget 'Changed' `
        -ExpectedName 'Changed' `
        -ExpectedKind 'Event' `
        -ExpectedContainer 'SymbolNavigationFixture.SemanticEdgeCases' `
        -ExpectedDefinitions @(
            (New-ExpectedDefinition -LineContains 'public event EventHandler? Changed;' -Target 'Changed')
        )

    Assert-Definition `
        -Name 'definition record positional property reference' `
        -QueryLineContains 'string recordName = record.Name;' `
        -QueryTarget 'Name' `
        -QueryOccurrence 2 `
        -ExpectedName 'Name' `
        -ExpectedKind 'Property' `
        -ExpectedContainer 'SymbolNavigationFixture.WidgetRecord' `
        -ExpectedDefinitions @(
            (New-ExpectedDefinition -LineContains 'public record WidgetRecord(string Name, int Count);' -Target 'Name')
        )

    Assert-Definition `
        -Name 'definition primary constructor property reference' `
        -QueryLineContains 'string primaryName = primary.Name;' `
        -QueryTarget 'Name' `
        -QueryOccurrence 2 `
        -ExpectedName 'Name' `
        -ExpectedKind 'Property' `
        -ExpectedContainer 'SymbolNavigationFixture.PrimaryWidget' `
        -ExpectedDefinitions @(
            (New-ExpectedDefinition -LineContains 'public string Name => name;' -Target 'Name')
        )

    Assert-Definition `
        -Name 'definition property accessor normalizes to property' `
        -QueryLineContains '        get' `
        -QueryTarget 'get' `
        -ExpectedName 'ExplicitCounter' `
        -ExpectedKind 'Property' `
        -ExpectedContainer 'SymbolNavigationFixture.SemanticEdgeCases' `
        -ExpectedDefinitions @(
            (New-ExpectedDefinition -LineContains 'public int ExplicitCounter' -Target 'ExplicitCounter')
        )

    Assert-Definition `
        -Name 'definition event accessor normalizes to event' `
        -QueryLineContains '        add' `
        -QueryTarget 'add' `
        -ExpectedName 'ExplicitChanged' `
        -ExpectedKind 'Event' `
        -ExpectedContainer 'SymbolNavigationFixture.SemanticEdgeCases' `
        -ExpectedDefinitions @(
            (New-ExpectedDefinition -LineContains 'public event EventHandler? ExplicitChanged' -Target 'ExplicitChanged')
        )

    Assert-Definition `
        -Name 'definition source-backed synthesized record deconstruct' `
        -QueryLineContains 'record.Deconstruct(out string recordLabel, out int recordCount);' `
        -QueryTarget 'Deconstruct' `
        -ExpectedName 'Deconstruct' `
        -ExpectedKind 'Method' `
        -ExpectedContainer 'SymbolNavigationFixture.WidgetRecord' `
        -ExpectedDefinitions @(
            (New-ExpectedDefinition -LineContains 'public record WidgetRecord(string Name, int Count);' -Target 'WidgetRecord')
        )

    Assert-Definition `
        -Name 'definition shortened attribute reference' `
        -QueryLineContains '[WidgetMarker]' `
        -QueryTarget 'WidgetMarker' `
        -ExpectedName '.ctor' `
        -ExpectedKind 'Method' `
        -ExpectedContainer 'SymbolNavigationFixture.WidgetMarkerAttribute' `
        -ExpectedDefinitions @(
            (New-ExpectedDefinition -LineContains 'public sealed class WidgetMarkerAttribute : Attribute;' -Target 'WidgetMarkerAttribute')
        )

    Assert-DefinitionError `
        -Name 'definition metadata symbol' `
        -QueryLineContains 'string formatted = widget.Format(3);' `
        -QueryTarget 'string' `
        -ExpectedDiagnostic 'NAVLYN1305:'

    Assert-DefinitionMetadata `
        -Name 'definition metadata symbol included' `
        -QueryLineContains 'string formatted = widget.Format(3);' `
        -QueryTarget 'string' `
        -ExpectedName 'String' `
        -ExpectedKind 'NamedType' `
        -ExpectedContainer 'System' `
        -ExpectedAssembly 'System.Runtime' `
        -ExpectedDocumentationCommentId 'T:System.String'

    Assert-References `
        -Name 'references type reference' `
        -QueryLineContains 'Widget widget = Widget.CreateDefault();' `
        -QueryTarget 'Widget' `
        -ExpectedName 'Widget' `
        -ExpectedKind 'NamedType' `
        -ExpectedContainer 'SymbolNavigationFixture' `
        -ExpectedReferences @(
            (New-ExpectedDefinition -LineContains 'public static Widget CreateDefault()' -Target 'Widget'),
            (New-ExpectedDefinition -LineContains 'return new Widget("default");' -Target 'Widget'),
            (New-ExpectedDefinition -LineContains 'public static string Describe(this Widget widget)' -Target 'Widget'),
            (New-ExpectedDefinition -LineContains 'public static string Label(Widget widget)' -Target 'Widget'),
            (New-ExpectedDefinition -LineContains 'string FormatWidget(Widget widget);' -Target 'Widget' -Occurrence 2),
            (New-ExpectedDefinition -LineContains 'public string FormatWidget(Widget widget)' -Target 'Widget' -Occurrence 2),
            (New-ExpectedDefinition -LineContains 'string GetName(Widget widget);' -Target 'Widget'),
            (New-ExpectedDefinition -LineContains 'string IWidgetIdentity.GetName(Widget widget)' -Target 'Widget' -Occurrence 2),
            (New-ExpectedDefinition -LineContains 'T Project(Widget widget);' -Target 'Widget'),
            (New-ExpectedDefinition -LineContains 'public string Project(Widget widget)' -Target 'Widget'),
            (New-ExpectedDefinition -LineContains 'public abstract string Render(Widget widget);' -Target 'Widget'),
            (New-ExpectedDefinition -LineContains 'public virtual string DescribeWidget(Widget widget)' -Target 'Widget' -Occurrence 2),
            (New-ExpectedDefinition -LineContains 'public override string Render(Widget widget)' -Target 'Widget'),
            (New-ExpectedDefinition -LineContains 'public override string DescribeWidget(Widget widget)' -Target 'Widget' -Occurrence 2),
            (New-ExpectedDefinition -LineContains 'Widget widget = Widget.CreateDefault();' -Target 'Widget'),
            (New-ExpectedDefinition -LineContains 'Widget widget = Widget.CreateDefault();' -Target 'Widget' -Occurrence 2),
            (New-ExpectedDefinition -LineContains 'object candidate = Widget.CreateDefault();' -Target 'Widget'),
            (New-ExpectedDefinition -LineContains 'string patternName = candidate is Widget matchedWidget' -Target 'Widget'),
            (New-ExpectedDefinition -LineContains 'string viaInterface = formatter.FormatWidget(Widget.CreateDefault());' -Target 'Widget' -Occurrence 2),
            (New-ExpectedDefinition -LineContains 'string rendered = renderer.Render(Widget.CreateDefault());' -Target 'Widget'),
            (New-ExpectedDefinition -LineContains 'Widget targetTypedWidget = new("target");' -Target 'Widget'),
            (New-ExpectedDefinition -LineContains 'Widget targetTypedWidget = new("target");' -Target 'new')
        )

    Assert-References `
        -Name 'references object creation type reference' `
        -QueryLineContains 'return new Widget("default");' `
        -QueryTarget 'Widget' `
        -ExpectedName 'Widget' `
        -ExpectedKind 'NamedType' `
        -ExpectedContainer 'SymbolNavigationFixture' `
        -ExpectedReferences @(
            (New-ExpectedDefinition -LineContains 'public static Widget CreateDefault()' -Target 'Widget'),
            (New-ExpectedDefinition -LineContains 'return new Widget("default");' -Target 'Widget'),
            (New-ExpectedDefinition -LineContains 'public static string Describe(this Widget widget)' -Target 'Widget'),
            (New-ExpectedDefinition -LineContains 'public static string Label(Widget widget)' -Target 'Widget'),
            (New-ExpectedDefinition -LineContains 'string FormatWidget(Widget widget);' -Target 'Widget' -Occurrence 2),
            (New-ExpectedDefinition -LineContains 'public string FormatWidget(Widget widget)' -Target 'Widget' -Occurrence 2),
            (New-ExpectedDefinition -LineContains 'string GetName(Widget widget);' -Target 'Widget'),
            (New-ExpectedDefinition -LineContains 'string IWidgetIdentity.GetName(Widget widget)' -Target 'Widget' -Occurrence 2),
            (New-ExpectedDefinition -LineContains 'T Project(Widget widget);' -Target 'Widget'),
            (New-ExpectedDefinition -LineContains 'public string Project(Widget widget)' -Target 'Widget'),
            (New-ExpectedDefinition -LineContains 'public abstract string Render(Widget widget);' -Target 'Widget'),
            (New-ExpectedDefinition -LineContains 'public virtual string DescribeWidget(Widget widget)' -Target 'Widget' -Occurrence 2),
            (New-ExpectedDefinition -LineContains 'public override string Render(Widget widget)' -Target 'Widget'),
            (New-ExpectedDefinition -LineContains 'public override string DescribeWidget(Widget widget)' -Target 'Widget' -Occurrence 2),
            (New-ExpectedDefinition -LineContains 'Widget widget = Widget.CreateDefault();' -Target 'Widget'),
            (New-ExpectedDefinition -LineContains 'Widget widget = Widget.CreateDefault();' -Target 'Widget' -Occurrence 2),
            (New-ExpectedDefinition -LineContains 'object candidate = Widget.CreateDefault();' -Target 'Widget'),
            (New-ExpectedDefinition -LineContains 'string patternName = candidate is Widget matchedWidget' -Target 'Widget'),
            (New-ExpectedDefinition -LineContains 'string viaInterface = formatter.FormatWidget(Widget.CreateDefault());' -Target 'Widget' -Occurrence 2),
            (New-ExpectedDefinition -LineContains 'string rendered = renderer.Render(Widget.CreateDefault());' -Target 'Widget'),
            (New-ExpectedDefinition -LineContains 'Widget targetTypedWidget = new("target");' -Target 'Widget'),
            (New-ExpectedDefinition -LineContains 'Widget targetTypedWidget = new("target");' -Target 'new')
        )

    Assert-References `
        -Name 'references static method reference' `
        -QueryLineContains 'Widget widget = Widget.CreateDefault();' `
        -QueryTarget 'CreateDefault' `
        -ExpectedName 'CreateDefault' `
        -ExpectedKind 'Method' `
        -ExpectedContainer 'SymbolNavigationFixture.Widget' `
        -ExpectedReferences @(
            (New-ExpectedDefinition -LineContains 'Widget widget = Widget.CreateDefault();' -Target 'CreateDefault'),
            (New-ExpectedDefinition -LineContains 'object candidate = Widget.CreateDefault();' -Target 'CreateDefault'),
            (New-ExpectedDefinition -LineContains 'string viaInterface = formatter.FormatWidget(Widget.CreateDefault());' -Target 'CreateDefault'),
            (New-ExpectedDefinition -LineContains 'string rendered = renderer.Render(Widget.CreateDefault());' -Target 'CreateDefault')
        )

    Assert-References `
        -Name 'references extension method reference' `
        -QueryLineContains 'string extensionFormatted = widget.Describe();' `
        -QueryTarget 'Describe' `
        -ExpectedName 'Describe' `
        -ExpectedKind 'Method' `
        -ExpectedContainer 'SymbolNavigationFixture.WidgetExtensions' `
        -ExpectedReferences @(
            (New-ExpectedDefinition -LineContains 'string extensionFormatted = widget.Describe();' -Target 'Describe')
        )

    Assert-References `
        -Name 'references using static method reference' `
        -QueryLineContains 'string staticFormatted = Label(widget);' `
        -QueryTarget 'Label' `
        -ExpectedName 'Label' `
        -ExpectedKind 'Method' `
        -ExpectedContainer 'SymbolNavigationFixture.WidgetText' `
        -ExpectedReferences @(
            (New-ExpectedDefinition -LineContains 'string staticFormatted = Label(widget);' -Target 'Label')
        )

    Assert-References `
        -Name 'references overloaded method reference' `
        -QueryLineContains 'string formatted = widget.Format(3);' `
        -QueryTarget 'Format' `
        -ExpectedName 'Format' `
        -ExpectedKind 'Method' `
        -ExpectedContainer 'SymbolNavigationFixture.Widget' `
        -ExpectedReferences @(
            (New-ExpectedDefinition -LineContains 'string formatted = widget.Format(3);' -Target 'Format')
        )

    Assert-References `
        -Name 'references property reference' `
        -QueryLineContains 'return $"{Name}:{count}";' `
        -QueryTarget 'Name' `
        -ExpectedName 'Name' `
        -ExpectedKind 'Property' `
        -ExpectedContainer 'SymbolNavigationFixture.Widget' `
        -ExpectedReferences @(
            (New-ExpectedDefinition -LineContains 'Name = name;' -Target 'Name'),
            (New-ExpectedDefinition -LineContains 'return $"{Name}:{count}";' -Target 'Name'),
            (New-ExpectedDefinition -LineContains 'return $"{Name}:{suffix}";' -Target 'Name'),
            (New-ExpectedDefinition -LineContains 'return $"explicit:{widget.Name}";' -Target 'Name'),
            (New-ExpectedDefinition -LineContains 'return $"project:{widget.Name}";' -Target 'Name'),
            (New-ExpectedDefinition -LineContains 'return $"base:{widget.Name}";' -Target 'Name'),
            (New-ExpectedDefinition -LineContains 'return $"<span>{widget.Name}</span>";' -Target 'Name'),
            (New-ExpectedDefinition -LineContains 'return $"html:{widget.Name}";' -Target 'Name'),
            (New-ExpectedDefinition -LineContains 'string patternName = candidate is Widget matchedWidget' -Target 'Name' -Occurrence 2),
            (New-ExpectedDefinition -LineContains 'var inferredName = targetTypedWidget.Name;' -Target 'Name' -Occurrence 2)
        )

    Assert-References `
        -Name 'references property accessor normalizes to property' `
        -QueryLineContains '        get' `
        -QueryTarget 'get' `
        -ExpectedName 'ExplicitCounter' `
        -ExpectedKind 'Property' `
        -ExpectedContainer 'SymbolNavigationFixture.SemanticEdgeCases' `
        -ExpectedReferences @(
            (New-ExpectedDefinition -LineContains 'ExplicitCounter = ExplicitCounter + 1;' -Target 'ExplicitCounter'),
            (New-ExpectedDefinition -LineContains 'ExplicitCounter = ExplicitCounter + 1;' -Target 'ExplicitCounter' -Occurrence 2)
        )

    Assert-References `
        -Name 'references event accessor normalizes to event' `
        -QueryLineContains '        add' `
        -QueryTarget 'add' `
        -ExpectedName 'ExplicitChanged' `
        -ExpectedKind 'Event' `
        -ExpectedContainer 'SymbolNavigationFixture.SemanticEdgeCases' `
        -ExpectedReferences @(
            (New-ExpectedDefinition -LineContains 'source.ExplicitChanged += HandleChanged;' -Target 'ExplicitChanged'),
            (New-ExpectedDefinition -LineContains 'source.ExplicitChanged -= HandleChanged;' -Target 'ExplicitChanged')
        )

    Assert-References `
        -Name 'references parameter reference' `
        -QueryLineContains 'return $"{Name}:{count}";' `
        -QueryTarget 'count' `
        -ExpectedName 'count' `
        -ExpectedKind 'Parameter' `
        -ExpectedContainer 'SymbolNavigationFixture.Widget.Format(int)' `
        -ExpectedReferences @(
            (New-ExpectedDefinition -LineContains 'return $"{Name}:{count}";' -Target 'count')
        )

    Assert-References `
        -Name 'references local reference' `
        -QueryLineContains 'return $"{formatted}|{extensionFormatted}|{staticFormatted}";' `
        -QueryTarget 'formatted' `
        -ExpectedName 'formatted' `
        -ExpectedKind 'Local' `
        -ExpectedContainer 'SymbolNavigationFixture.Runner.Run()' `
        -ExpectedReferences @(
            (New-ExpectedDefinition -LineContains 'return $"{formatted}|{extensionFormatted}|{staticFormatted}";' -Target 'formatted')
        )

    Assert-References `
        -Name 'references alias reference' `
        -QueryLineContains 'public AliasRunner CreateRunner()' `
        -QueryTarget 'AliasRunner' `
        -ExpectedName 'AliasRunner' `
        -ExpectedKind 'Alias' `
        -ExpectedReferences @(
            (New-ExpectedDefinition -LineContains 'using AliasRunner = SymbolNavigationFixture.Runner;' -Target 'Runner' -Occurrence 2),
            (New-ExpectedDefinition -LineContains 'public AliasRunner CreateRunner()' -Target 'AliasRunner'),
            (New-ExpectedDefinition -LineContains 'return new AliasRunner();' -Target 'AliasRunner')
        )

    Assert-Implementations `
        -Name 'implementations interface type' `
        -QueryLineContains 'public interface IWidgetFormatter' `
        -QueryTarget 'IWidgetFormatter' `
        -ExpectedName 'IWidgetFormatter' `
        -ExpectedKind 'NamedType' `
        -ExpectedContainer 'SymbolNavigationFixture' `
        -ExpectedImplementations @(
            (New-ExpectedDefinition -LineContains 'public sealed class DefaultWidgetFormatter : IWidgetFormatter' -Target 'DefaultWidgetFormatter')
        )

    Assert-Implementations `
        -Name 'implementations interface member' `
        -QueryLineContains 'string FormatWidget(Widget widget);' `
        -QueryTarget 'FormatWidget' `
        -ExpectedName 'FormatWidget' `
        -ExpectedKind 'Method' `
        -ExpectedContainer 'SymbolNavigationFixture.IWidgetFormatter' `
        -ExpectedImplementations @(
            (New-ExpectedDefinition -LineContains 'public string FormatWidget(Widget widget)' -Target 'FormatWidget')
        )

    Assert-Implementations `
        -Name 'implementations non-applicable symbol' `
        -QueryLineContains 'public sealed class Runner' `
        -QueryTarget 'Runner' `
        -ExpectedName 'Runner' `
        -ExpectedKind 'NamedType' `
        -ExpectedContainer 'SymbolNavigationFixture' `
        -ExpectedImplementations @()

    Assert-Implementations `
        -Name 'implementations explicit interface member' `
        -QueryLineContains 'string GetName(Widget widget);' `
        -QueryTarget 'GetName' `
        -ExpectedName 'GetName' `
        -ExpectedKind 'Method' `
        -ExpectedContainer 'SymbolNavigationFixture.IWidgetIdentity' `
        -ExpectedImplementations @(
            (New-ExpectedDefinition -LineContains 'string IWidgetIdentity.GetName(Widget widget)' -Target 'GetName')
        )

    Assert-Implementations `
        -Name 'implementations generic interface member' `
        -QueryLineContains 'T Project(Widget widget);' `
        -QueryTarget 'Project' `
        -ExpectedName 'Project' `
        -ExpectedKind 'Method' `
        -ExpectedContainer 'SymbolNavigationFixture.IWidgetProjector<T>' `
        -ExpectedImplementations @(
            (New-ExpectedDefinition -LineContains 'public string Project(Widget widget)' -Target 'Project')
        )

    Assert-Implementations `
        -Name 'implementations abstract override' `
        -QueryLineContains 'public abstract string Render(Widget widget);' `
        -QueryTarget 'Render' `
        -ExpectedName 'Render' `
        -ExpectedKind 'Method' `
        -ExpectedContainer 'SymbolNavigationFixture.WidgetRenderer' `
        -ExpectedImplementations @(
            (New-ExpectedDefinition -LineContains 'public override string Render(Widget widget)' -Target 'Render')
        )

    Assert-Implementations `
        -Name 'implementations virtual override' `
        -QueryLineContains 'public virtual string DescribeWidget(Widget widget)' `
        -QueryTarget 'DescribeWidget' `
        -ExpectedName 'DescribeWidget' `
        -ExpectedKind 'Method' `
        -ExpectedContainer 'SymbolNavigationFixture.WidgetRenderer' `
        -ExpectedImplementations @(
            (New-ExpectedDefinition -LineContains 'public override string DescribeWidget(Widget widget)' -Target 'DescribeWidget')
        )

    Assert-CallersContain `
        -Name 'callers direct static method' `
        -QueryLineContains 'public static Widget CreateDefault()' `
        -QueryTarget 'CreateDefault' `
        -ExpectedName 'CreateDefault' `
        -ExpectedKind 'Method' `
        -ExpectedContainer 'SymbolNavigationFixture.Widget' `
        -ExpectedCallerName 'Run' `
        -ExpectedCallerKind 'Method' `
        -ExpectedCallerContainer 'SymbolNavigationFixture.Runner' `
        -ExpectedLocationLineContains 'Widget widget = Widget.CreateDefault();' `
        -ExpectedLocationTarget 'CreateDefault'

    Assert-CallersContain `
        -Name 'callers interface member' `
        -QueryLineContains 'string FormatWidget(Widget widget);' `
        -QueryTarget 'FormatWidget' `
        -ExpectedName 'FormatWidget' `
        -ExpectedKind 'Method' `
        -ExpectedContainer 'SymbolNavigationFixture.IWidgetFormatter' `
        -ExpectedCallerName 'Exercise' `
        -ExpectedCallerKind 'Method' `
        -ExpectedCallerContainer 'SymbolNavigationFixture.SemanticEdgeCases' `
        -ExpectedLocationLineContains 'string viaInterface = formatter.FormatWidget(Widget.CreateDefault());' `
        -ExpectedLocationTarget 'FormatWidget'

    Assert-CallersContain `
        -Name 'callers abstract dispatch' `
        -QueryLineContains 'public abstract string Render(Widget widget);' `
        -QueryTarget 'Render' `
        -ExpectedName 'Render' `
        -ExpectedKind 'Method' `
        -ExpectedContainer 'SymbolNavigationFixture.WidgetRenderer' `
        -ExpectedCallerName 'Exercise' `
        -ExpectedCallerKind 'Method' `
        -ExpectedCallerContainer 'SymbolNavigationFixture.SemanticEdgeCases' `
        -ExpectedLocationLineContains 'string rendered = renderer.Render(Widget.CreateDefault());' `
        -ExpectedLocationTarget 'Render'

    Assert-CallersContain `
        -Name 'callers property accessor normalizes to property' `
        -QueryLineContains '        get' `
        -QueryTarget 'get' `
        -ExpectedName 'ExplicitCounter' `
        -ExpectedKind 'Property' `
        -ExpectedContainer 'SymbolNavigationFixture.SemanticEdgeCases' `
        -ExpectedCallerName 'IncrementCounter' `
        -ExpectedCallerKind 'Method' `
        -ExpectedCallerContainer 'SymbolNavigationFixture.SemanticEdgeCases' `
        -ExpectedLocationLineContains 'ExplicitCounter = ExplicitCounter + 1;' `
        -ExpectedLocationTarget 'ExplicitCounter'

    Assert-CallersContain `
        -Name 'callers event accessor normalizes to event' `
        -QueryLineContains '        add' `
        -QueryTarget 'add' `
        -ExpectedName 'ExplicitChanged' `
        -ExpectedKind 'Event' `
        -ExpectedContainer 'SymbolNavigationFixture.SemanticEdgeCases' `
        -ExpectedCallerName 'Subscribe' `
        -ExpectedCallerKind 'Method' `
        -ExpectedCallerContainer 'SymbolNavigationFixture.SemanticEdgeCases' `
        -ExpectedLocationLineContains 'source.ExplicitChanged += HandleChanged;' `
        -ExpectedLocationTarget 'ExplicitChanged'

    Assert-CallsContain `
        -Name 'calls direct method from Runner.Run' `
        -QueryLineContains 'public string Run()' `
        -QueryTarget 'Run' `
        -ExpectedCallerName 'Run' `
        -ExpectedCallerKind 'Method' `
        -ExpectedCallerContainer 'SymbolNavigationFixture.Runner' `
        -ExpectedCalleeName 'Format' `
        -ExpectedCalleeKind 'Method' `
        -ExpectedCalleeContainer 'SymbolNavigationFixture.Widget' `
        -ExpectedLocationLineContains 'string formatted = widget.Format(3);' `
        -ExpectedLocationTarget 'widget'

    Assert-CallsContain `
        -Name 'calls event from IncrementCounter' `
        -QueryLineContains 'public void IncrementCounter()' `
        -QueryTarget 'IncrementCounter' `
        -ExpectedCallerName 'IncrementCounter' `
        -ExpectedCallerKind 'Method' `
        -ExpectedCallerContainer 'SymbolNavigationFixture.SemanticEdgeCases' `
        -ExpectedCalleeName 'Changed' `
        -ExpectedCalleeKind 'Event' `
        -ExpectedCalleeContainer 'SymbolNavigationFixture.SemanticEdgeCases' `
        -ExpectedLocationLineContains 'Changed?.Invoke(this, EventArgs.Empty);' `
        -ExpectedLocationTarget 'Changed'

    Assert-CallsContain `
        -Name 'calls property from IncrementCounter' `
        -QueryLineContains 'public void IncrementCounter()' `
        -QueryTarget 'IncrementCounter' `
        -ExpectedCallerName 'IncrementCounter' `
        -ExpectedCallerKind 'Method' `
        -ExpectedCallerContainer 'SymbolNavigationFixture.SemanticEdgeCases' `
        -ExpectedCalleeName 'Counter' `
        -ExpectedCalleeKind 'Property' `
        -ExpectedCalleeContainer 'SymbolNavigationFixture.SemanticEdgeCases' `
        -ExpectedLocationLineContains 'Counter = Counter + 1;' `
        -ExpectedLocationTarget 'Counter'

    Assert-CallsContain `
        -Name 'calls explicit property accessor as property from IncrementCounter' `
        -QueryLineContains 'public void IncrementCounter()' `
        -QueryTarget 'IncrementCounter' `
        -ExpectedCallerName 'IncrementCounter' `
        -ExpectedCallerKind 'Method' `
        -ExpectedCallerContainer 'SymbolNavigationFixture.SemanticEdgeCases' `
        -ExpectedCalleeName 'ExplicitCounter' `
        -ExpectedCalleeKind 'Property' `
        -ExpectedCalleeContainer 'SymbolNavigationFixture.SemanticEdgeCases' `
        -ExpectedLocationLineContains 'ExplicitCounter = ExplicitCounter + 1;' `
        -ExpectedLocationTarget 'ExplicitCounter'

    Assert-CallsContain `
        -Name 'calls explicit event subscription from Subscribe' `
        -QueryLineContains 'public void Subscribe(SemanticEdgeCases source)' `
        -QueryTarget 'Subscribe' `
        -ExpectedCallerName 'Subscribe' `
        -ExpectedCallerKind 'Method' `
        -ExpectedCallerContainer 'SymbolNavigationFixture.SemanticEdgeCases' `
        -ExpectedCalleeName 'ExplicitChanged' `
        -ExpectedCalleeKind 'Event' `
        -ExpectedCalleeContainer 'SymbolNavigationFixture.SemanticEdgeCases' `
        -ExpectedLocationLineContains 'source.ExplicitChanged += HandleChanged;' `
        -ExpectedLocationTarget 'ExplicitChanged'

    Assert-CallsContain `
        -Name 'calls delegate local invocation from Exercise' `
        -QueryLineContains 'public string Exercise()' `
        -QueryTarget 'Exercise' `
        -ExpectedCallerName 'Exercise' `
        -ExpectedCallerKind 'Method' `
        -ExpectedCallerContainer 'SymbolNavigationFixture.SemanticEdgeCases' `
        -ExpectedCalleeName 'normalize' `
        -ExpectedCalleeKind 'Local' `
        -ExpectedCalleeContainer 'SymbolNavigationFixture.SemanticEdgeCases.Exercise()' `
        -ExpectedLocationLineContains 'string normalized = normalize(" value ");' `
        -ExpectedLocationTarget 'normalize' `
        -ExpectedLocationOccurrence 2

    Assert-CallsMetadataBehavior `
        -Name 'calls metadata callee from Exercise' `
        -QueryLineContains 'public string Exercise()' `
        -QueryTarget 'Exercise' `
        -ExpectedCalleeName 'Trim' `
        -ExpectedCalleeKind 'Method' `
        -ExpectedCalleeContainer 'string' `
        -ExpectedAssembly 'System.Runtime' `
        -ExpectedLocationLineContains 'string directTrimmed = " direct ".Trim();' `
        -ExpectedLocationTarget '" direct "'

    Assert-CallsContain `
        -Name 'calls local function from Exercise' `
        -QueryLineContains 'public string Exercise()' `
        -QueryTarget 'Exercise' `
        -ExpectedCallerName 'Exercise' `
        -ExpectedCallerKind 'Method' `
        -ExpectedCallerContainer 'SymbolNavigationFixture.SemanticEdgeCases' `
        -ExpectedCalleeName 'BuildLocal' `
        -ExpectedCalleeKind 'Method' `
        -ExpectedCalleeContainer 'SymbolNavigationFixture.SemanticEdgeCases.Exercise()' `
        -ExpectedLocationLineContains 'string local = BuildLocal("local");' `
        -ExpectedLocationTarget 'BuildLocal'

    Assert-CallsContain `
        -Name 'calls overloaded operator from Exercise' `
        -QueryLineContains 'public string Exercise()' `
        -QueryTarget 'Exercise' `
        -ExpectedCallerName 'Exercise' `
        -ExpectedCallerKind 'Method' `
        -ExpectedCallerContainer 'SymbolNavigationFixture.SemanticEdgeCases' `
        -ExpectedCalleeName 'op_Addition' `
        -ExpectedCalleeKind 'Method' `
        -ExpectedCalleeContainer 'SymbolNavigationFixture.NumberBox' `
        -ExpectedLocationLineContains 'NumberBox total = first + second;' `
        -ExpectedLocationTarget 'first'

    Assert-CallsContain `
        -Name 'calls indexer from Exercise' `
        -QueryLineContains 'public string Exercise()' `
        -QueryTarget 'Exercise' `
        -ExpectedCallerName 'Exercise' `
        -ExpectedCallerKind 'Method' `
        -ExpectedCallerContainer 'SymbolNavigationFixture.SemanticEdgeCases' `
        -ExpectedCalleeName 'this[]' `
        -ExpectedCalleeKind 'Property' `
        -ExpectedCalleeContainer 'SymbolNavigationFixture.NumberBox' `
        -ExpectedLocationLineContains 'int indexed = total[0];' `
        -ExpectedLocationTarget 'total'

    $formatPosition = Get-SourcePosition `
        -LineContains 'string formatted = widget.Format(3);' `
        -Target 'Format'

    $formatInfo = Invoke-Navlyn `
        -Name 'symbol-info overload argument binding' `
        -Arguments @(
            'symbol-info',
            '--workspace',
            $FixtureProjectPath,
            '--file',
            $FixtureSourcePath,
            '--line',
            [string]$formatPosition.Line,
            '--column',
            [string]$formatPosition.Column) `
        -ExpectedExitCode 0

    $formatInfoJson = $formatInfo.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbol-info overload target name' -Actual $formatInfoJson.invocation.target.displayName -Expected 'SymbolNavigationFixture.Widget.Format(int)'
    Assert-Equal -Name 'symbol-info overload parameter name' -Actual @($formatInfoJson.invocation.arguments)[0].parameter.name -Expected 'count'
    Assert-Equal -Name 'symbol-info overload return type' -Actual $formatInfoJson.invocation.target.returnType.name -Expected 'string'

    $optionalPosition = Get-SourcePosition `
        -LineContains 'string optional = FormatOptional();' `
        -Target 'FormatOptional'

    $optionalInfo = Invoke-Navlyn `
        -Name 'symbol-info optional argument binding' `
        -Arguments @(
            'symbol-info',
            '--workspace',
            $FixtureProjectPath,
            '--file',
            $FixtureSourcePath,
            '--line',
            [string]$optionalPosition.Line,
            '--column',
            [string]$optionalPosition.Column) `
        -ExpectedExitCode 0

    $optionalInfoJson = $optionalInfo.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbol-info optional target name' -Actual $optionalInfoJson.invocation.target.displayName -Expected 'SymbolNavigationFixture.SemanticEdgeCases.FormatOptional(string)'
    Assert-Equal -Name 'symbol-info optional argument count' -Actual @($optionalInfoJson.invocation.arguments).Count -Expected 1
    Assert-Equal -Name 'symbol-info optional parameter name' -Actual @($optionalInfoJson.invocation.arguments)[0].parameter.name -Expected 'text'

    $attributePosition = Get-SourcePosition `
        -LineContains '[WidgetMarker]' `
        -Target 'WidgetMarker'

    $attributeInfo = Invoke-Navlyn `
        -Name 'symbol-info attribute constructor' `
        -Arguments @(
            'symbol-info',
            '--workspace',
            $FixtureProjectPath,
            '--file',
            $FixtureSourcePath,
            '--line',
            [string]$attributePosition.Line,
            '--column',
            [string]$attributePosition.Column) `
        -ExpectedExitCode 0

    $attributeInfoJson = $attributeInfo.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbol-info attribute type' -Actual $attributeInfoJson.attribute.type.displayName -Expected 'SymbolNavigationFixture.WidgetMarkerAttribute'
    Assert-Equal -Name 'symbol-info attribute constructor flag' -Actual $attributeInfoJson.attribute.constructor.facts.isConstructor -Expected $true

    $targetTypedNewPosition = Get-SourcePosition `
        -LineContains 'Widget targetTypedWidget = new("target");' `
        -Target 'new'

    $targetTypedNewInfo = Invoke-Navlyn `
        -Name 'symbol-info target typed new inferred constructed type' `
        -Arguments @(
            'symbol-info',
            '--workspace',
            $FixtureProjectPath,
            '--file',
            $FixtureSourcePath,
            '--line',
            [string]$targetTypedNewPosition.Line,
            '--column',
            [string]$targetTypedNewPosition.Column) `
        -ExpectedExitCode 0

    $targetTypedNewJson = $targetTypedNewInfo.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbol-info target typed new kind' -Actual $targetTypedNewJson.invocation.kind -Expected 'ObjectCreation'
    Assert-Equal -Name 'symbol-info target typed new constructed type' -Actual $targetTypedNewJson.invocation.constructedType.name -Expected 'SymbolNavigationFixture.Widget'

    $inferredLocalPosition = Get-SourcePosition `
        -LineContains 'var inferredName = targetTypedWidget.Name;' `
        -Target 'targetTypedWidget'

    $inferredLocalInfo = Invoke-Navlyn `
        -Name 'symbol-info inferred local expression type' `
        -Arguments @(
            'symbol-info',
            '--workspace',
            $FixtureProjectPath,
            '--file',
            $FixtureSourcePath,
            '--line',
            [string]$inferredLocalPosition.Line,
            '--column',
            [string]$inferredLocalPosition.Column) `
        -ExpectedExitCode 0

    $inferredLocalJson = $inferredLocalInfo.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbol-info inferred local expression type' -Actual $inferredLocalJson.expression.type.name -Expected 'SymbolNavigationFixture.Widget'

    $outline = Invoke-Navlyn `
        -Name 'outline fixture source' `
        -Arguments @(
            'outline',
            '--workspace',
            $FixtureProjectPath,
            '--file',
            $FixtureSourcePath) `
        -ExpectedExitCode 0

    $outlineJson = $outline.Stdout | ConvertFrom-Json
    $numberBoxEntry = @($outlineJson.entries | Where-Object { $_.name -eq 'NumberBox' -and $_.kind -eq 'NamedType' })[0]
    $operatorEntry = @($outlineJson.entries | Where-Object { $_.name -eq 'op_Addition' -and $_.kind -eq 'Method' })[0]
    Assert-Equal -Name 'outline fixture contains NumberBox' -Actual $numberBoxEntry.name -Expected 'NumberBox'
    Assert-Equal -Name 'outline fixture operator flag' -Actual $operatorEntry.facts.isOperator -Expected $true

    $hierarchyPosition = Get-SourcePosition `
        -LineContains 'public interface IWidgetFormatter' `
        -Target 'IWidgetFormatter'

    $hierarchy = Invoke-Navlyn `
        -Name 'type-hierarchy interface implementation' `
        -Arguments @(
            'type-hierarchy',
            '--workspace',
            $FixtureProjectPath,
            '--file',
            $FixtureSourcePath,
            '--line',
            [string]$hierarchyPosition.Line,
            '--column',
            [string]$hierarchyPosition.Column) `
        -ExpectedExitCode 0

    $hierarchyJson = $hierarchy.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'type-hierarchy implementation name' -Actual @($hierarchyJson.implementingTypes)[0].name -Expected 'DefaultWidgetFormatter'

    $symbolsFiltered = Invoke-Navlyn `
        -Name 'symbols namespace container accessibility filters' `
        -Arguments @(
            'symbols',
            '--workspace',
            $FixtureProjectPath,
            '--query',
            'Format',
            '--namespace',
            'SymbolNavigationFixture',
            '--namespace-match',
            'exact',
            '--container',
            'SymbolNavigationFixture.Widget',
            '--container-match',
            'exact',
            '--accessibility',
            'Public') `
        -ExpectedExitCode 0

    $symbolsFilteredJson = $symbolsFiltered.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbols filtered total' -Actual $symbolsFilteredJson.totalMatches -Expected 2
    Assert-Equal -Name 'symbols filtered first container' -Actual @($symbolsFilteredJson.matches)[0].container -Expected 'SymbolNavigationFixture.Widget'

    Write-Host 'Symbol navigation checks passed.'
}
finally {
    Pop-Location
}

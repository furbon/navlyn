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
$FixtureSolutionPath = Join-Path $RepoRoot 'tests/fixtures/WorkspaceSemanticsFixture/WorkspaceSemanticsFixture.slnx'
$ConditionalSourcePath = Join-Path $RepoRoot 'tests/fixtures/WorkspaceSemanticsFixture/Conditional/ConditionalCode.cs'
$MultiTargetSourcePath = Join-Path $RepoRoot 'tests/fixtures/WorkspaceSemanticsFixture/MultiTarget/TargetSpecificCode.cs'
$LinkedSourcePath = Join-Path $RepoRoot 'tests/fixtures/WorkspaceSemanticsFixture/Shared/LinkedContext.cs'
$ConditionalDisplayPath = 'tests/fixtures/WorkspaceSemanticsFixture/Conditional/ConditionalCode.cs'
$MultiTargetDisplayPath = 'tests/fixtures/WorkspaceSemanticsFixture/MultiTarget/TargetSpecificCode.cs'
$LinkedDisplayPath = 'tests/fixtures/WorkspaceSemanticsFixture/Shared/LinkedContext.cs'
$MultiTargetProjectDisplayPath = 'tests/fixtures/WorkspaceSemanticsFixture/MultiTarget/MultiTarget.csproj'
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

function Assert-SequenceContains {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [object[]]$Actual,

        [Parameter(Mandatory = $true)]
        [string]$Expected
    )

    if ($Actual -notcontains $Expected) {
        throw "$Name expected to contain '$Expected' but was '$($Actual -join ', ')'."
    }
}

function Get-Project {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Projects,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    foreach ($project in $Projects) {
        if ($project.name -eq $Name) {
            return $project
        }
    }

    throw "Could not find project '$Name'."
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

Push-Location $RepoRoot
try {
    Write-Host 'Restoring workspace semantics fixture...'
    Invoke-CheckedProcess `
        -Name 'dotnet restore workspace semantics fixture' `
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

    Write-Host 'Running workspace semantics checks...'

    $activeUse = Get-SourcePosition `
        -Path $ConditionalSourcePath `
        -LineContains 'ActiveBranchSymbol active = new ActiveBranchSymbol();' `
        -Target 'ActiveBranchSymbol'
    $activeCreation = Get-SourcePosition `
        -Path $ConditionalSourcePath `
        -LineContains 'ActiveBranchSymbol active = new ActiveBranchSymbol();' `
        -Target 'ActiveBranchSymbol' `
        -Occurrence 2
    $activeDeclaration = Get-SourcePosition `
        -Path $ConditionalSourcePath `
        -LineContains 'public sealed class ActiveBranchSymbol' `
        -Target 'ActiveBranchSymbol'
    $inactiveUse = Get-SourcePosition `
        -Path $ConditionalSourcePath `
        -LineContains 'InactiveBranchSymbol inactive = new InactiveBranchSymbol();' `
        -Target 'InactiveBranchSymbol'
    $net10Use = Get-SourcePosition `
        -Path $MultiTargetSourcePath `
        -LineContains 'Net10OnlyValue value = new Net10OnlyValue();' `
        -Target 'Net10OnlyValue'
    $net10Declaration = Get-SourcePosition `
        -Path $MultiTargetSourcePath `
        -LineContains 'public sealed class Net10OnlyValue' `
        -Target 'Net10OnlyValue'
    $netStandardUse = Get-SourcePosition `
        -Path $MultiTargetSourcePath `
        -LineContains 'NetStandardOnlyValue value = new NetStandardOnlyValue();' `
        -Target 'NetStandardOnlyValue'
    $netStandardDeclaration = Get-SourcePosition `
        -Path $MultiTargetSourcePath `
        -LineContains 'public sealed class NetStandardOnlyValue' `
        -Target 'NetStandardOnlyValue'
    $alphaUse = Get-SourcePosition `
        -Path $LinkedSourcePath `
        -LineContains 'AlphaLinkedOnly value = new AlphaLinkedOnly();' `
        -Target 'AlphaLinkedOnly'
    $alphaDeclaration = Get-SourcePosition `
        -Path $LinkedSourcePath `
        -LineContains 'public sealed class AlphaLinkedOnly' `
        -Target 'AlphaLinkedOnly'
    $betaUse = Get-SourcePosition `
        -Path $LinkedSourcePath `
        -LineContains 'BetaLinkedOnly value = new BetaLinkedOnly();' `
        -Target 'BetaLinkedOnly'
    $betaDeclaration = Get-SourcePosition `
        -Path $LinkedSourcePath `
        -LineContains 'public sealed class BetaLinkedOnly' `
        -Target 'BetaLinkedOnly'

    $overview = Invoke-Navlyn `
        -Name 'overview workspace semantics fixture' `
        -Arguments @('overview', '--workspace', $FixtureSolutionPath) `
        -ExpectedExitCode 0

    $overviewJson = $overview.Stdout | ConvertFrom-Json
    $projects = @($overviewJson.projects)
    Assert-Equal -Name 'overview project count' -Actual $projects.Count -Expected 5
    Assert-Equal -Name 'overview conditional target framework' -Actual (Get-Project -Projects $projects -Name 'Conditional').targetFramework -Expected 'net10.0'
    Assert-SequenceContains -Name 'overview conditional preprocessor symbols' -Actual @((Get-Project -Projects $projects -Name 'Conditional').preprocessorSymbols) -Expected 'NAVLYN_ACTIVE_BRANCH'
    Assert-Equal -Name 'overview multitarget net10 target framework' -Actual (Get-Project -Projects $projects -Name 'MultiTarget(net10.0)').targetFramework -Expected 'net10.0'
    Assert-SequenceContains -Name 'overview multitarget net10 symbols' -Actual @((Get-Project -Projects $projects -Name 'MultiTarget(net10.0)').preprocessorSymbols) -Expected 'NAVLYN_TFM_NET10'
    Assert-Equal -Name 'overview multitarget netstandard target framework' -Actual (Get-Project -Projects $projects -Name 'MultiTarget(netstandard2.0)').targetFramework -Expected 'netstandard2.0'
    Assert-SequenceContains -Name 'overview multitarget netstandard symbols' -Actual @((Get-Project -Projects $projects -Name 'MultiTarget(netstandard2.0)').preprocessorSymbols) -Expected 'NAVLYN_TFM_NETSTANDARD'

    $activeSymbols = Invoke-Navlyn `
        -Name 'symbols active preprocessor branch' `
        -Arguments @('symbols', '--workspace', $FixtureSolutionPath, '--query', 'ActiveBranchSymbol') `
        -ExpectedExitCode 0
    $activeSymbolsJson = $activeSymbols.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'active symbols count' -Actual $activeSymbolsJson.totalMatches -Expected 1
    Assert-Equal -Name 'active symbol path' -Actual @($activeSymbolsJson.matches)[0].path -Expected $ConditionalDisplayPath

    $inactiveSymbols = Invoke-Navlyn `
        -Name 'symbols inactive preprocessor branch' `
        -Arguments @('symbols', '--workspace', $FixtureSolutionPath, '--query', 'InactiveBranchSymbol') `
        -ExpectedExitCode 0
    $inactiveSymbolsJson = $inactiveSymbols.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'inactive symbols count' -Actual $inactiveSymbolsJson.totalMatches -Expected 0

    $symbolsInActive = Invoke-Navlyn `
        -Name 'symbols-in active preprocessor branch' `
        -Arguments @('symbols-in', '--workspace', $FixtureSolutionPath, '--file', $ConditionalSourcePath, '--line', [string]$activeUse.Line) `
        -ExpectedExitCode 0
    $symbolsInActiveJson = $symbolsInActive.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbols-in active has active symbol' -Actual (@($symbolsInActiveJson.symbols) | Where-Object { $_.name -eq 'ActiveBranchSymbol' }).Count -Expected 2

    $symbolsInInactive = Invoke-Navlyn `
        -Name 'symbols-in inactive preprocessor branch' `
        -Arguments @('symbols-in', '--workspace', $FixtureSolutionPath, '--file', $ConditionalSourcePath, '--line', [string]$inactiveUse.Line) `
        -ExpectedExitCode 0
    $symbolsInInactiveJson = $symbolsInInactive.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbols-in inactive count' -Actual @($symbolsInInactiveJson.symbols).Count -Expected 0

    $symbolAtActive = Invoke-Navlyn `
        -Name 'symbol-at active preprocessor branch' `
        -Arguments @('symbol-at', '--workspace', $FixtureSolutionPath, '--file', $ConditionalSourcePath, '--line', [string]$activeUse.Line, '--column', [string]$activeUse.Column) `
        -ExpectedExitCode 0
    $symbolAtActiveJson = $symbolAtActive.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbol-at active name' -Actual $symbolAtActiveJson.symbol.name -Expected 'ActiveBranchSymbol'
    Assert-Equal -Name 'symbol-at active declaration line' -Actual $symbolAtActiveJson.symbol.line -Expected $activeDeclaration.Line

    $symbolAtInactive = Invoke-Navlyn `
        -Name 'symbol-at inactive preprocessor branch' `
        -Arguments @('symbol-at', '--workspace', $FixtureSolutionPath, '--file', $ConditionalSourcePath, '--line', [string]$inactiveUse.Line, '--column', [string]$inactiveUse.Column) `
        -ExpectedExitCode 2
    Assert-Equal -Name 'symbol-at inactive stdout' -Actual $symbolAtInactive.Stdout -Expected ''
    Assert-Contains -Name 'symbol-at inactive stderr' -Actual $symbolAtInactive.Stderr -ExpectedSubstring 'NAVLYN1304'

    $definitionActive = Invoke-Navlyn `
        -Name 'definition active preprocessor branch' `
        -Arguments @('definition', '--workspace', $FixtureSolutionPath, '--file', $ConditionalSourcePath, '--line', [string]$activeUse.Line, '--column', [string]$activeUse.Column) `
        -ExpectedExitCode 0
    $definitionActiveJson = $definitionActive.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'definition active count' -Actual @($definitionActiveJson.definitions).Count -Expected 1
    Assert-Equal -Name 'definition active line' -Actual @($definitionActiveJson.definitions)[0].line -Expected $activeDeclaration.Line

    $referencesActive = Invoke-Navlyn `
        -Name 'references active preprocessor branch' `
        -Arguments @('references', '--workspace', $FixtureSolutionPath, '--file', $ConditionalSourcePath, '--line', [string]$activeUse.Line, '--column', [string]$activeUse.Column) `
        -ExpectedExitCode 0
    $referencesActiveJson = $referencesActive.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'references active count' -Actual $referencesActiveJson.totalMatches -Expected 3
    Assert-Equal -Name 'references active first line' -Actual @($referencesActiveJson.references)[0].line -Expected $activeUse.Line
    Assert-Equal -Name 'references active second line' -Actual @($referencesActiveJson.references)[1].line -Expected $activeCreation.Line

    $symbolsNet10 = Invoke-Navlyn `
        -Name 'symbols multitarget net10' `
        -Arguments @('symbols', '--workspace', $FixtureSolutionPath, '--query', 'Net10OnlyValue', '--project', 'MultiTarget(net10.0)') `
        -ExpectedExitCode 0
    $symbolsNet10Json = $symbolsNet10.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbols net10 project target framework' -Actual @($symbolsNet10Json.projects)[0].targetFramework -Expected 'net10.0'
    Assert-Equal -Name 'symbols net10 count' -Actual $symbolsNet10Json.totalMatches -Expected 1
    Assert-Equal -Name 'symbols net10 declaration line' -Actual @($symbolsNet10Json.matches)[0].line -Expected $net10Declaration.Line

    $symbolsNetStandard = Invoke-Navlyn `
        -Name 'symbols multitarget netstandard' `
        -Arguments @('symbols', '--workspace', $FixtureSolutionPath, '--query', 'NetStandardOnlyValue', '--project', 'MultiTarget(netstandard2.0)') `
        -ExpectedExitCode 0
    $symbolsNetStandardJson = $symbolsNetStandard.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbols netstandard project target framework' -Actual @($symbolsNetStandardJson.projects)[0].targetFramework -Expected 'netstandard2.0'
    Assert-Equal -Name 'symbols netstandard count' -Actual $symbolsNetStandardJson.totalMatches -Expected 1
    Assert-Equal -Name 'symbols netstandard declaration line' -Actual @($symbolsNetStandardJson.matches)[0].line -Expected $netStandardDeclaration.Line

    $ambiguousMultiTargetProjectPath = Invoke-Navlyn `
        -Name 'symbols multitarget project path ambiguous' `
        -Arguments @('symbols', '--workspace', $FixtureSolutionPath, '--query', 'Net10OnlyValue', '--project', (Join-Path $RepoRoot $MultiTargetProjectDisplayPath)) `
        -ExpectedExitCode 2
    Assert-Equal -Name 'ambiguous multitarget path stdout' -Actual $ambiguousMultiTargetProjectPath.Stdout -Expected ''
    Assert-Contains -Name 'ambiguous multitarget path stderr' -Actual $ambiguousMultiTargetProjectPath.Stderr -ExpectedSubstring 'NAVLYN1007'

    $symbolAtNetStandard = Invoke-Navlyn `
        -Name 'symbol-at multitarget netstandard' `
        -Arguments @('symbol-at', '--workspace', $FixtureSolutionPath, '--project', 'MultiTarget(netstandard2.0)', '--file', $MultiTargetSourcePath, '--line', [string]$netStandardUse.Line, '--column', [string]$netStandardUse.Column) `
        -ExpectedExitCode 0
    $symbolAtNetStandardJson = $symbolAtNetStandard.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbol-at netstandard project target framework' -Actual $symbolAtNetStandardJson.project.targetFramework -Expected 'netstandard2.0'
    Assert-Equal -Name 'symbol-at netstandard name' -Actual $symbolAtNetStandardJson.symbol.name -Expected 'NetStandardOnlyValue'
    Assert-Equal -Name 'symbol-at netstandard path' -Actual $symbolAtNetStandardJson.symbol.path -Expected $MultiTargetDisplayPath

    $symbolAtNet10 = Invoke-Navlyn `
        -Name 'symbol-at multitarget net10' `
        -Arguments @('symbol-at', '--workspace', $FixtureSolutionPath, '--project', 'MultiTarget(net10.0)', '--file', $MultiTargetSourcePath, '--line', [string]$net10Use.Line, '--column', [string]$net10Use.Column) `
        -ExpectedExitCode 0
    $symbolAtNet10Json = $symbolAtNet10.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbol-at net10 project target framework' -Actual $symbolAtNet10Json.project.targetFramework -Expected 'net10.0'
    Assert-Equal -Name 'symbol-at net10 name' -Actual $symbolAtNet10Json.symbol.name -Expected 'Net10OnlyValue'

    $symbolAtLinkedAlpha = Invoke-Navlyn `
        -Name 'symbol-at linked alpha' `
        -Arguments @('symbol-at', '--workspace', $FixtureSolutionPath, '--project', 'LinkedAlpha', '--file', $LinkedSourcePath, '--line', [string]$alphaUse.Line, '--column', [string]$alphaUse.Column) `
        -ExpectedExitCode 0
    $symbolAtLinkedAlphaJson = $symbolAtLinkedAlpha.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbol-at linked alpha project' -Actual $symbolAtLinkedAlphaJson.project.name -Expected 'LinkedAlpha'
    Assert-Equal -Name 'symbol-at linked alpha name' -Actual $symbolAtLinkedAlphaJson.symbol.name -Expected 'AlphaLinkedOnly'
    Assert-Equal -Name 'symbol-at linked alpha path' -Actual $symbolAtLinkedAlphaJson.symbol.path -Expected $LinkedDisplayPath
    Assert-Equal -Name 'symbol-at linked alpha declaration line' -Actual $symbolAtLinkedAlphaJson.symbol.line -Expected $alphaDeclaration.Line

    $symbolAtLinkedBeta = Invoke-Navlyn `
        -Name 'symbol-at linked beta' `
        -Arguments @('symbol-at', '--workspace', $FixtureSolutionPath, '--project', 'LinkedBeta', '--file', $LinkedSourcePath, '--line', [string]$betaUse.Line, '--column', [string]$betaUse.Column) `
        -ExpectedExitCode 0
    $symbolAtLinkedBetaJson = $symbolAtLinkedBeta.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbol-at linked beta project' -Actual $symbolAtLinkedBetaJson.project.name -Expected 'LinkedBeta'
    Assert-Equal -Name 'symbol-at linked beta name' -Actual $symbolAtLinkedBetaJson.symbol.name -Expected 'BetaLinkedOnly'
    Assert-Equal -Name 'symbol-at linked beta path' -Actual $symbolAtLinkedBetaJson.symbol.path -Expected $LinkedDisplayPath
    Assert-Equal -Name 'symbol-at linked beta declaration line' -Actual $symbolAtLinkedBetaJson.symbol.line -Expected $betaDeclaration.Line

    $symbolAtLinkedDefault = Invoke-Navlyn `
        -Name 'symbol-at linked default context' `
        -Arguments @('symbol-at', '--workspace', $FixtureSolutionPath, '--file', $LinkedSourcePath, '--line', [string]$alphaUse.Line, '--column', [string]$alphaUse.Column) `
        -ExpectedExitCode 0
    $symbolAtLinkedDefaultJson = $symbolAtLinkedDefault.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbol-at linked default project fact' -Actual $symbolAtLinkedDefaultJson.symbol.facts.project -Expected 'LinkedAlpha'

    Write-Host 'Workspace semantics checks passed.'
}
finally {
    Pop-Location
}

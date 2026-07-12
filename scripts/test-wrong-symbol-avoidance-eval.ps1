[CmdletBinding()]
param(
    [string]$ScenarioFile = "docs/evals/wrong-symbol-avoidance.scenarios.json",
    [string]$Output = "artifacts/evals/wrong-symbol-avoidance-report.json",
    [switch]$NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$ProjectPath = Join-Path $RepoRoot 'navlyn/navlyn.csproj'
$TargetFrameworkScript = Join-Path $RepoRoot 'scripts/lib/navlyn-target-framework.ps1'
. $TargetFrameworkScript

$TargetFramework = Get-NavlynPreferredTargetFramework -ProjectPath $ProjectPath
$NavlynDll = Join-Path $RepoRoot "navlyn/bin/Debug/$TargetFramework/navlyn.dll"
$ScenarioPath = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $ScenarioFile))
$OutputPath = if ([System.IO.Path]::IsPathRooted($Output)) {
    [System.IO.Path]::GetFullPath($Output)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $Output))
}

if (!(Test-Path -LiteralPath $ScenarioPath)) {
    throw "Scenario file does not exist: $ScenarioPath"
}

if (!$NoBuild) {
    dotnet build (Join-Path $RepoRoot 'navlyn.slnx')
    if ($LASTEXITCODE -ne 0) {
        throw 'dotnet build failed.'
    }
}

if (!(Test-Path -LiteralPath $NavlynDll)) {
    throw "Navlyn executable was not found: $NavlynDll"
}

$scenarioDocument = Get-Content -Raw -LiteralPath $ScenarioPath | ConvertFrom-Json -Depth 100
if ($scenarioDocument.schemaVersion -ne 'navlyn.wrong-symbol-avoidance-eval.v1') {
    throw "Unsupported wrong-symbol eval schemaVersion: $($scenarioDocument.schemaVersion)"
}

function Invoke-NavlynJson {
    param([string[]]$Arguments)

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'dotnet'
    $startInfo.WorkingDirectory = $RepoRoot
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.UseShellExecute = $false
    foreach ($argument in @($NavlynDll) + $Arguments) {
        [void]$startInfo.ArgumentList.Add($argument)
    }

    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $process = [System.Diagnostics.Process]::Start($startInfo)
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    $watch.Stop()

    $json = $null
    $jsonValid = $false
    try {
        $json = $stdout | ConvertFrom-Json -Depth 100
        $jsonValid = $true
    }
    catch {
        $jsonValid = $false
    }

    [pscustomobject]@{
        exitCode = $process.ExitCode
        elapsedMs = [Math]::Round($watch.Elapsed.TotalMilliseconds, 3)
        stdoutChars = $stdout.Length
        stderrChars = $stderr.Length
        stderr = $stderr
        jsonValid = $jsonValid
        json = $json
    }
}

function Get-FirstTextMatchLine {
    param(
        [string]$RelativePath,
        [string]$Query
    )

    $path = Join-Path $RepoRoot $RelativePath
    $lineNumber = 0
    foreach ($line in [System.IO.File]::ReadLines($path)) {
        $lineNumber++
        if ($line.Contains($Query, [System.StringComparison]::Ordinal)) {
            return $lineNumber
        }
    }

    return $null
}

$results = [System.Collections.Generic.List[object]]::new()
foreach ($scenario in @($scenarioDocument.scenarios)) {
    $firstTextLine = Get-FirstTextMatchLine -RelativePath $scenario.file -Query $scenario.query
    $textSearchWrong = $null -ne $firstTextLine -and [int]$firstTextLine -eq [int]$scenario.textSearchExpectedWrongLine

    $target = Invoke-NavlynJson -Arguments @(
        'target',
        '--workspace', [string]$scenario.workspace,
        '--query', [string]$scenario.query,
        '--assume-kind', 'NamedType',
        '--limit', '10'
    )

    $targetConfidence = if ($target.jsonValid -and $target.json.PSObject.Properties.Name -contains 'confidence') { [string]$target.json.confidence } else { $null }
    $targetFailClosed = $target.exitCode -eq 0 -and
        $target.jsonValid -and
        $target.stderrChars -eq 0 -and
        $targetConfidence -eq [string]$scenario.navlynExpectedFirstConfidence -and
        (-not ($target.json.PSObject.Properties.Name -contains 'selectedTarget'))

    $narrowed = $null
    $narrowedAnchored = $true
    if ($scenario.PSObject.Properties.Name -contains 'intendedLine') {
        $narrowed = Invoke-NavlynJson -Arguments @(
            'target',
            '--workspace', [string]$scenario.workspace,
            '--file', [string]$scenario.file,
            '--line', [string]$scenario.intendedLine,
            '--column', [string]$scenario.intendedColumn
        )

        $narrowedAnchored = $narrowed.exitCode -eq 0 -and
            $narrowed.jsonValid -and
            $narrowed.stderrChars -eq 0 -and
            [string]$narrowed.json.confidence -eq [string]$scenario.navlynExpectedNarrowedConfidence -and
            [string]$narrowed.json.selectedTarget.facts.fullyQualifiedName -eq [string]$scenario.intendedFullyQualifiedName
    }

    $passed = $textSearchWrong -and $targetFailClosed -and $narrowedAnchored
    $results.Add([pscustomobject]@{
        id = [string]$scenario.id
        passed = $passed
        textSearch = [pscustomobject]@{
            query = [string]$scenario.query
            firstMatchLine = $firstTextLine
            expectedWrongLine = [int]$scenario.textSearchExpectedWrongLine
            wrongSymbolRiskDemonstrated = $textSearchWrong
        }
        navlyn = [pscustomobject]@{
            firstConfidence = $targetConfidence
            failClosed = $targetFailClosed
            firstExitCode = $target.exitCode
            firstJsonValid = $target.jsonValid
            firstStderrChars = $target.stderrChars
            firstStdoutChars = $target.stdoutChars
            narrowedAnchored = $narrowedAnchored
            narrowedStdoutChars = if ($null -eq $narrowed) { $null } else { $narrowed.stdoutChars }
        }
    })
}

$passedCount = @($results | Where-Object { $_.passed }).Count
$totalCount = $results.Count
$score = if ($totalCount -eq 0) { 0.0 } else { [Math]::Round($passedCount / $totalCount, 4) }
$report = [ordered]@{
    schemaVersion = 'navlyn.wrong-symbol-avoidance-eval.report.v1'
    scenarioFile = [System.IO.Path]::GetRelativePath($RepoRoot, $ScenarioPath).Replace('\', '/')
    total = $totalCount
    passed = $passedCount
    failed = $totalCount - $passedCount
    score = $score
    minimumScore = [double]$scenarioDocument.minimumScore
    results = $results.ToArray()
}

$outputDirectory = [System.IO.Path]::GetDirectoryName($OutputPath)
if (![string]::IsNullOrWhiteSpace($outputDirectory)) {
    [System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
}

$report | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $OutputPath -Encoding utf8
Write-Host "Wrong-symbol avoidance eval report: $OutputPath"

if ($score -lt [double]$scenarioDocument.minimumScore) {
    throw "Wrong-symbol avoidance eval score $score is below minimum $($scenarioDocument.minimumScore)."
}

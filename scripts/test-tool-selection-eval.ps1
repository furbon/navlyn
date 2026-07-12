[CmdletBinding()]
param(
    [string]$ScenarioFile = "docs/evals/tool-selection.scenarios.json",
    [string]$TraceFile = $null,
    [string]$Output = $null,
    [switch]$UseBaselineTraces,
    [switch]$NoBuild,
    [double]$MinimumScore = 0.95
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$ScenarioPath = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $ScenarioFile))
if (!(Test-Path -LiteralPath $ScenarioPath)) {
    throw "Scenario file does not exist: $ScenarioPath"
}

$scenarioDocument = Get-Content -Raw -LiteralPath $ScenarioPath | ConvertFrom-Json -Depth 100
if ($scenarioDocument.schemaVersion -ne 'navlyn.tool-selection-eval.v1') {
    throw "Unsupported tool-selection eval schemaVersion: $($scenarioDocument.schemaVersion)"
}

if ($NoBuild -and !$UseBaselineTraces -and [string]::IsNullOrWhiteSpace($TraceFile)) {
    $UseBaselineTraces = $true
}

if (!$UseBaselineTraces -and [string]::IsNullOrWhiteSpace($TraceFile)) {
    throw 'Pass -UseBaselineTraces or provide -TraceFile with actual traces.'
}

$traceByScenario = @{}
if ($UseBaselineTraces) {
    foreach ($scenario in @($scenarioDocument.scenarios)) {
        $traceByScenario[$scenario.id] = $scenario.baselineTrace
    }
}
else {
    $tracePath = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $TraceFile))
    if (!(Test-Path -LiteralPath $tracePath)) {
        throw "Trace file does not exist: $tracePath"
    }

    $traceDocument = Get-Content -Raw -LiteralPath $tracePath | ConvertFrom-Json -Depth 100
    foreach ($trace in @($traceDocument.traces)) {
        $traceByScenario[$trace.scenarioId] = $trace
    }
}

function Test-ContainsAny {
    param(
        [object[]]$Values,
        [object[]]$Candidates
    )

    foreach ($value in @($Values)) {
        foreach ($candidate in @($Candidates)) {
            if ([string]$value -eq [string]$candidate) {
                return $true
            }
        }
    }

    return $false
}

function Get-BooleanOrDefault {
    param(
        [object]$Object,
        [string]$Name,
        [bool]$Default
    )

    if ($null -eq $Object) {
        return $Default
    }

    if ($Object.PSObject.Properties.Name -contains $Name) {
        return [bool]$Object.$Name
    }

    return $Default
}

$results = New-Object System.Collections.Generic.List[object]
$totalPoints = 0
$maxPoints = 0

foreach ($scenario in @($scenarioDocument.scenarios)) {
    $trace = $traceByScenario[$scenario.id]
    $surface = if ($scenario.PSObject.Properties.Name -contains 'surface') { [string]$scenario.surface } else { 'unified' }
    [object[]]$chosenSequence = if ($null -eq $trace -or $null -eq $trace.chosenSequence) { @() } else { @($trace.chosenSequence) }
    $firstStep = if ($chosenSequence.Count -eq 0) { $null } else { [string]$chosenSequence[0] }
    $points = 0
    $criteria = [ordered]@{}

    $criteria.correctSmallestUsefulTool = $null -ne $firstStep -and (Test-ContainsAny -Values @($scenario.expectedFirstSteps) -Candidates @($firstStep))
    if ($criteria.correctSmallestUsefulTool) { $points++ }

    $usesNavlyn = @($chosenSequence | Where-Object { ([string]$_).StartsWith('navlyn_', [System.StringComparison]::Ordinal) -or [string]$_ -in @('target', 'read', 'prepare-edit', 'verify-edit', 'review', 'find', 'resolve-target', 'repo-graph', 'outline', 'symbol-source', 'references', 'callers', 'calls', 'context-pack', 'review-diff', 'route-map', 'route-impact') }).Count -gt 0
    $criteria.wrongSymbolAvoidance = if ([bool]$scenario.semanticIdentityRequired) {
        $usesNavlyn
    }
    elseif (-not [bool]$scenario.navlynExpected) {
        -not $usesNavlyn
    }
    else {
        $true
    }
    if ($criteria.wrongSymbolAvoidance) { $points++ }

    $hasAvoidedTool = Test-ContainsAny -Values @($chosenSequence) -Candidates @($scenario.avoidTools)
    $criteria.overBroadChecklistAvoidance = -not $hasAvoidedTool -and $chosenSequence.Count -le [int]$scenario.maxToolCount
    if ($criteria.overBroadChecklistAvoidance) { $points++ }

    $stopCondition = if ($null -eq $trace -or $trace.PSObject.Properties.Name -notcontains 'stopCondition') { $null } else { [string]$trace.stopCondition }
    $criteria.stopCondition = $null -ne $stopCondition -and (Test-ContainsAny -Values @($scenario.acceptedStopConditions) -Candidates @($stopCondition))
    if ($criteria.stopCondition) { $points++ }

    $criteria.stdoutStderrJsonBehavior = (Get-BooleanOrDefault -Object $trace -Name 'stdoutJsonValid' -Default $false) -and (Get-BooleanOrDefault -Object $trace -Name 'stderrClean' -Default $false)
    if ($criteria.stdoutStderrJsonBehavior) { $points++ }

    $maxPoints += 5
    $totalPoints += $points
    $results.Add([pscustomobject]@{
        id = $scenario.id
        prompt = $scenario.prompt
        surface = $surface
        points = $points
        maxPoints = 5
        passed = $points -eq 5
        chosenSequence = $chosenSequence
        expectedFirstSteps = @($scenario.expectedFirstSteps)
        stopCondition = $stopCondition
        criteria = [pscustomobject]$criteria
    })
}

$score = if ($maxPoints -eq 0) { 0.0 } else { [Math]::Round($totalPoints / $maxPoints, 4) }
$report = [ordered]@{
    schemaVersion = 'navlyn.tool-selection-eval.report.v1'
    scenarioFile = [System.IO.Path]::GetRelativePath($RepoRoot, $ScenarioPath).Replace('\', '/')
    traceSource = if ($UseBaselineTraces) { 'baseline' } else { [System.IO.Path]::GetRelativePath($RepoRoot, [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $TraceFile))).Replace('\', '/') }
    scenarioCount = @($scenarioDocument.scenarios).Count
    totalPoints = $totalPoints
    maxPoints = $maxPoints
    score = $score
    passed = $score -ge $MinimumScore
    minimumScore = $MinimumScore
    results = $results.ToArray()
}

$json = $report | ConvertTo-Json -Depth 100
if ([string]::IsNullOrWhiteSpace($Output)) {
    $json
}
else {
    $outputPath = if ([System.IO.Path]::IsPathRooted($Output)) {
        [System.IO.Path]::GetFullPath($Output)
    }
    else {
        [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $Output))
    }
    $outputDirectory = [System.IO.Path]::GetDirectoryName($outputPath)
    if (![string]::IsNullOrWhiteSpace($outputDirectory)) {
        [System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
    }

    Set-Content -LiteralPath $outputPath -Value $json -Encoding utf8
}

if ($score -lt $MinimumScore) {
    throw "Tool-selection eval score $score is below minimum $MinimumScore."
}

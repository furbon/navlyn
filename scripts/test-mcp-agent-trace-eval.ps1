[CmdletBinding()]
param(
    [string]$TraceFile = "docs/evals/mcp-agent-traces.replay.json",
    [string]$Output = "artifacts/evals/mcp-agent-trace-report.json"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$TracePath = if ([System.IO.Path]::IsPathRooted($TraceFile)) {
    [System.IO.Path]::GetFullPath($TraceFile)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $TraceFile))
}
$OutputPath = if ([System.IO.Path]::IsPathRooted($Output)) {
    [System.IO.Path]::GetFullPath($Output)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $Output))
}

if (!(Test-Path -LiteralPath $TracePath)) {
    throw "Trace file does not exist: $TracePath"
}

$document = Get-Content -Raw -LiteralPath $TracePath | ConvertFrom-Json -Depth 100
if ($document.schemaVersion -ne 'navlyn.mcp-agent-trace-eval.v1') {
    throw "Unsupported MCP agent trace schemaVersion: $($document.schemaVersion)"
}

$traces = @($document.traces)
if ($traces.Count -eq 0) {
    throw 'Trace file contains no traces.'
}

$textOnly = @($traces | Where-Object { $_.kind -eq 'text-only' })
$textOnlyFalsePositive = @($textOnly | Where-Object { [bool]$_.usedNavlyn })
$broadChecklist = @($traces | Where-Object { [bool]$_.broadChecklist })
$riskyEditBeforeAnchor = @($traces | Where-Object { -not [bool]$_.semanticAnchorBeforeEdit })
$stopConditionSuccess = @($traces | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_.stopCondition) })
$canonicalStdout = @($traces | Where-Object { $_.surface -eq 'canonical' -and [int]$_.stdoutChars -gt 0 } | ForEach-Object { [int]$_.stdoutChars } | Sort-Object)

function Get-Rate {
    param([int]$Numerator, [int]$Denominator)
    if ($Denominator -eq 0) { return 0.0 }
    return [Math]::Round($Numerator / $Denominator, 4)
}

function Get-P95 {
    param([int[]]$Values)
    if ($Values.Count -eq 0) { return 0 }
    $index = [Math]::Ceiling($Values.Count * 0.95) - 1
    $index = [Math]::Max(0, [Math]::Min($index, $Values.Count - 1))
    return $Values[$index]
}

$thresholds = $document.thresholds
$textOnlyFalsePositiveRate = Get-Rate $textOnlyFalsePositive.Count $textOnly.Count
$broadChecklistRate = Get-Rate $broadChecklist.Count $traces.Count
$stopConditionRate = Get-Rate $stopConditionSuccess.Count $traces.Count
$canonicalP95 = Get-P95 -Values $canonicalStdout
$canonicalMax = if ($canonicalStdout.Count -eq 0) { 0 } else { ($canonicalStdout | Measure-Object -Maximum).Maximum }

$checks = [ordered]@{
    textOnlyFalsePositiveRate = $textOnlyFalsePositiveRate -le [double]$thresholds.maxTextOnlyFalsePositiveRate
    broadChecklistRate = $broadChecklistRate -le [double]$thresholds.maxBroadChecklistRate
    riskyEditBeforeAnchor = $riskyEditBeforeAnchor.Count -le [int]$thresholds.maxRiskyEditBeforeAnchor
    stopConditionRate = $stopConditionRate -ge [double]$thresholds.minStopConditionRate
    canonicalP95StdoutChars = $canonicalP95 -le [int]$thresholds.maxCanonicalP95StdoutChars
    canonicalMaxStdoutChars = $canonicalMax -le [int]$thresholds.maxCanonicalMaxStdoutChars
}

$passedChecks = @($checks.GetEnumerator() | Where-Object { [bool]$_.Value }).Count
$score = [Math]::Round($passedChecks / $checks.Count, 4)
$passed = $score -ge [double]$thresholds.minimumScore -and @($checks.GetEnumerator() | Where-Object { -not [bool]$_.Value }).Count -eq 0

$report = [ordered]@{
    schemaVersion = 'navlyn.mcp-agent-trace-eval.report.v1'
    traceFile = [System.IO.Path]::GetRelativePath($RepoRoot, $TracePath).Replace('\', '/')
    traceCount = $traces.Count
    textOnlyCount = $textOnly.Count
    textOnlyFalsePositiveRate = $textOnlyFalsePositiveRate
    broadChecklistRate = $broadChecklistRate
    riskyEditBeforeAnchor = $riskyEditBeforeAnchor.Count
    stopConditionRate = $stopConditionRate
    canonicalP95StdoutChars = $canonicalP95
    canonicalMaxStdoutChars = $canonicalMax
    score = $score
    passed = $passed
    checks = [pscustomobject]$checks
}

$outputDirectory = [System.IO.Path]::GetDirectoryName($OutputPath)
if (![string]::IsNullOrWhiteSpace($outputDirectory)) {
    [System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
}

$report | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $OutputPath -Encoding utf8
Write-Host "MCP agent trace eval report: $OutputPath"

if (!$passed) {
    throw "MCP agent trace eval failed with score $score."
}

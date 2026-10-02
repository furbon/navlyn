[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RunResult,
    [Parameter(Mandatory)][string]$ScoreId,
    [string]$OutputDirectory = 'artifacts/evals/real-task-scores',
    [int]$TimeoutSeconds = 1500
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
function Assert([bool]$Condition, [string]$Code) { if (-not $Condition) { throw $Code } }
function FullPath([string]$Path) { [IO.Path]::GetFullPath($Path) }
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function ReadJson([string]$Path) { Get-Content -LiteralPath $Path -Raw -Encoding utf8 | ConvertFrom-Json -AsHashtable }
function SaveJson([string]$Path, [object]$Value) {
    $temp = "$Path.tmp-$([guid]::NewGuid().ToString('N'))"
    try {
        [IO.File]::WriteAllText($temp, (($Value | ConvertTo-Json -Depth 50) + "`n"), [Text.UTF8Encoding]::new($false))
        Move-Item -LiteralPath $temp -Destination $Path
    }
    finally { if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp } }
}

Assert ($ScoreId -cmatch '^[a-z0-9][a-z0-9-]{5,79}$') 'SCORE_ID'
Assert ($TimeoutSeconds -ge 1 -and $TimeoutSeconds -le 3600) 'TIMEOUT_RANGE'
$runPath = FullPath $RunResult
Assert (Test-Path -LiteralPath $runPath -PathType Leaf) 'RUN_RESULT_MISSING'
$run = ReadJson $runPath
Assert ($run.schema -ceq 'navlyn.real-task-run-result.v1' -and $run.status -ceq 'completed') 'RUN_NOT_COMPLETE'
Assert ($run.taskId -cmatch '^[A-Z][A-Z0-9-]{2,30}$' -and $run.reportSha256 -cmatch '^[0-9a-f]{64}$') 'RUN_IDENTITY'
$manifestPath = FullPath ([string]$run.manifestPath)
Assert ((Test-Path -LiteralPath $manifestPath -PathType Leaf) -and (Hash $manifestPath) -ceq $run.manifestSha256) 'CAMPAIGN_MANIFEST_CHANGED'
$campaign = ReadJson $manifestPath
Assert ($campaign.schema -ceq 'navlyn.real-task-campaign.v1') 'CAMPAIGN_SCHEMA'
$matched = @($campaign.attempts | Where-Object { $_.id -ceq $run.id })
Assert ($matched.Count -eq 1) 'CAMPAIGN_ATTEMPT_IDENTITY'
$attempt = $matched[0]
Assert ($attempt.taskId -ceq $run.taskId -and $attempt.arm -ceq $run.arm -and
    $attempt.model -ceq $run.model -and $attempt.effort -ceq $run.effort -and
    $attempt.oracleSha256 -ceq $run.oracleSha256 -and
    $attempt.runner.sha256 -ceq $run.runnerSha256 -and $attempt.input.sha256 -ceq $run.inputSha256 -and
    $attempt.scorePlan.sha256 -ceq $run.scorePlanSha256) 'CAMPAIGN_ATTEMPT_CHANGED'
$scorePlanPath = FullPath ([string]$run.scorePlanPath)
$registeredPlan = [string]$attempt.scorePlan.path
if (-not [IO.Path]::IsPathRooted($registeredPlan)) { $registeredPlan = Join-Path (Split-Path -Parent $manifestPath) $registeredPlan }
Assert ($scorePlanPath -ceq (FullPath $registeredPlan)) 'SCORE_PLAN_PATH_CHANGED'
Assert ((Test-Path -LiteralPath $scorePlanPath -PathType Leaf) -and (Hash $scorePlanPath) -ceq $run.scorePlanSha256) 'SCORE_PLAN_CHANGED'
$scorePlan = ReadJson $scorePlanPath
Assert ($scorePlan.schema -ceq 'navlyn.real-task-score-plan.v1' -and $scorePlan.taskId -ceq $run.taskId -and $scorePlan.oracleSha256 -ceq $run.oracleSha256) 'SCORE_PLAN_BINDING'
$adapter = [string]$scorePlan.adapter.path
if (-not [IO.Path]::IsPathRooted($adapter)) { $adapter = Join-Path (Split-Path -Parent $scorePlanPath) $adapter }
$adapter = FullPath $adapter
$AdapterSha256 = [string]$scorePlan.adapter.sha256
Assert ($AdapterSha256 -cmatch '^[0-9a-f]{64}$' -and (Test-Path -LiteralPath $adapter -PathType Leaf) -and (Hash $adapter) -ceq $AdapterSha256) 'ADAPTER_HASH_MISMATCH'
Assert (Test-Path -LiteralPath $run.reportPath -PathType Leaf) 'ACTOR_REPORT_MISSING'
Assert ((Hash $run.reportPath) -ceq $run.reportSha256) 'ACTOR_REPORT_CHANGED'
$actorReport = ReadJson $run.reportPath
Assert ($actorReport.id -ceq $run.id -and $actorReport.taskId -ceq $run.taskId -and $actorReport.arm -ceq $run.arm -and
    $actorReport.requestedModel -ceq $run.model -and $actorReport.requestedEffort -ceq $run.effort -and
    $actorReport.sourceBindings.inputSha256 -ceq $run.inputSha256 -and
    $actorReport.sourceBindings.actorBrokerSha256 -ceq $run.runnerSha256 -and
    $actorReport.completed.turn.status -ceq 'completed' -and $actorReport.clientClosed -eq $true -and
    $actorReport.nativeStreamsClosed -eq $true -and $actorReport.workerClosed -eq $true -and
    @($actorReport.failures).Count -eq 0) 'ACTOR_REPORT_IDENTITY'
$worker = [string]$actorReport.workerAuditId
Assert ($worker -cmatch '^task-v19-[0-9a-f]{32}$') 'WORKER_IDENTITY'

$scoreRoot = Join-Path (FullPath $OutputDirectory) $ScoreId
Assert (-not (Test-Path -LiteralPath $scoreRoot)) 'SCORE_ALREADY_EXISTS'
New-Item -ItemType Directory -Path $scoreRoot -Force | Out-Null
SaveJson (Join-Path $scoreRoot 'launch.json') ([ordered]@{
    schema = 'navlyn.real-task-score-launch.v1'; scoreId = $ScoreId
    runResultSha256 = Hash $runPath; actorReportSha256 = $run.reportSha256
    scorePlanSha256 = $run.scorePlanSha256; oracleSha256 = $run.oracleSha256
    adapterSha256 = $AdapterSha256; taskId = $run.taskId; workerAuditId = $worker
    startedUtc = [DateTime]::UtcNow.ToString('o')
})

$startInfo = [Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = 'pwsh'
$startInfo.UseShellExecute = $false
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
foreach ($argument in @('-NoProfile', '-File', $adapter, '-TaskId', $run.taskId, '-WorkerId', $worker, '-ScoreId', $ScoreId)) {
    [void]$startInfo.ArgumentList.Add([string]$argument)
}
$process = [Diagnostics.Process]::Start($startInfo)
Assert ($null -ne $process) 'SCORER_START_FAILED'
try {
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $ended = $process.WaitForExit($TimeoutSeconds * 1000)
    $cleanupComplete = $true
    if (-not $ended) {
        try { $process.Kill($true) } catch { }
        try { $cleanupComplete = $process.WaitForExit(10000) } catch { $cleanupComplete = $false }
    }
    $exitCode = if ($ended) { $process.ExitCode } else { $null }
    $stdout = '[stream-unclosed]'
    $stderr = '[stream-unclosed]'
    try { if ($stdoutTask.Wait(2000)) { $stdout = $stdoutTask.GetAwaiter().GetResult() } } catch { $stdout = '[stream-read-failed]' }
    try { if ($stderrTask.Wait(2000)) { $stderr = $stderrTask.GetAwaiter().GetResult() } } catch { $stderr = '[stream-read-failed]' }
}
finally { $process.Dispose() }
[IO.File]::WriteAllText((Join-Path $scoreRoot 'stdout.txt'), $stdout, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $scoreRoot 'stderr.txt'), $stderr, [Text.UTF8Encoding]::new($false))
$result = $null
try { $result = $stdout | ConvertFrom-Json -AsHashtable } catch { }
$valid = $null -ne $result -and $result.taskId -ceq $run.taskId -and $result.workerAuditId -ceq $worker -and
    $result.scoreId -ceq $ScoreId -and $result.hiddenOracleSha256 -ceq $run.oracleSha256 -and
    $result.behaviorPassed -is [bool]
$status = if ($ended -and $cleanupComplete -and $exitCode -eq 0 -and $valid) { 'scored' } else { 'failed' }
SaveJson (Join-Path $scoreRoot 'result.json') ([ordered]@{
    schema = 'navlyn.real-task-score-result.v1'; scoreId = $ScoreId; taskId = $run.taskId
    workerAuditId = $worker; status = $status; behaviorPassed = $(if ($valid) { $result.behaviorPassed } else { $null })
    independentDiffReviewPending = $(if ($valid) { $result.independentDiffReviewPending } else { $null })
    acceptedActorScore = $false; runResultSha256 = Hash $runPath; actorReportSha256 = $run.reportSha256
    scorePlanSha256 = $run.scorePlanSha256; oracleSha256 = $run.oracleSha256
    adapterSha256 = $AdapterSha256; exitCode = $exitCode; timedOut = -not $ended; cleanupComplete = $cleanupComplete
    stdoutSha256 = Hash (Join-Path $scoreRoot 'stdout.txt'); stderrSha256 = Hash (Join-Path $scoreRoot 'stderr.txt')
    finishedUtc = [DateTime]::UtcNow.ToString('o')
})
@{ scoreId = $ScoreId; status = $status; behaviorPassed = $(if ($valid) { $result.behaviorPassed } else { $null }); acceptedActorScore = $false } | ConvertTo-Json -Compress

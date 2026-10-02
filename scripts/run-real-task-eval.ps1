[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Register', 'Preflight', 'Run', 'Resume', 'Aggregate')]
    [string]$Action,
    [Parameter(Mandatory)]
    [string]$Manifest,
    [string]$OutputDirectory = 'artifacts/evals/real-tasks',
    [string]$ScoreDirectory,
    [string]$AttemptId
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-AbsolutePath([string]$Path, [string]$Base) {
    if ([IO.Path]::IsPathRooted($Path)) { return [IO.Path]::GetFullPath($Path) }
    return [IO.Path]::GetFullPath((Join-Path $Base $Path))
}

function Get-Hash([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Assert-Value([bool]$Condition, [string]$Code) {
    if (-not $Condition) { throw $Code }
}

function Save-Json([string]$Path, [object]$Value) {
    $temp = "$Path.tmp-$([guid]::NewGuid().ToString('N'))"
    try {
        [IO.File]::WriteAllText($temp, (($Value | ConvertTo-Json -Depth 100) + "`n"), [Text.UTF8Encoding]::new($false))
        Move-Item -LiteralPath $temp -Destination $Path -ErrorAction Stop
    }
    finally {
        if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp }
    }
}

function Read-Json([string]$Path) {
    return Get-Content -LiteralPath $Path -Raw -Encoding utf8 | ConvertFrom-Json -AsHashtable
}

function Assert-ExactKeys([System.Collections.IDictionary]$Object, [string[]]$Names, [string]$Code) {
    Assert-Value ($null -ne $Object) $Code
    $actual = @($Object.Keys | Sort-Object -CaseSensitive)
    $expected = @($Names | Sort-Object -CaseSensitive)
    Assert-Value (($actual -join "`n") -ceq ($expected -join "`n")) $Code
}

function Resolve-Input([System.Collections.IDictionary]$Value, [string]$Base, [string]$Code) {
    Assert-ExactKeys $Value @('path', 'sha256') $Code
    Assert-Value ($Value.sha256 -cmatch '^[0-9a-f]{64}$') $Code
    $path = Get-AbsolutePath ([string]$Value.path) $Base
    Assert-Value (Test-Path -LiteralPath $path -PathType Leaf) $Code
    Assert-Value ((Get-Hash $path) -ceq $Value.sha256) "$Code`:HASH"
    return $path
}

function Invoke-BoundedNode([string]$Script, [string[]]$Arguments, [int]$TimeoutSeconds) {
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'node'
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    [void]$startInfo.ArgumentList.Add($Script)
    foreach ($argument in $Arguments) { [void]$startInfo.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($startInfo)
    Assert-Value ($null -ne $process) 'PROCESS_START_FAILED'
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $ended = $process.WaitForExit($TimeoutSeconds * 1000)
        $cleanupComplete = $true
        if (-not $ended) {
            try { $process.Kill($true) } catch { }
            try { $cleanupComplete = $process.WaitForExit(10000) } catch { $cleanupComplete = $false }
        }
        $stdoutText = '[stream-unclosed]'
        $stderrText = '[stream-unclosed]'
        try { if ($stdout.Wait(2000)) { $stdoutText = $stdout.GetAwaiter().GetResult() } } catch { $stdoutText = '[stream-read-failed]' }
        try { if ($stderr.Wait(2000)) { $stderrText = $stderr.GetAwaiter().GetResult() } } catch { $stderrText = '[stream-read-failed]' }
        [pscustomobject]@{
            exitCode = $(if ($ended) { $process.ExitCode } else { $null })
            timedOut = -not $ended
            cleanupComplete = $cleanupComplete
            stdout = $stdoutText
            stderr = $stderrText
        }
    }
    finally { $process.Dispose() }
}

function Test-ClosedActorReport([System.Collections.IDictionary]$Report, [System.Collections.IDictionary]$Attempt) {
    $observedModels = @($Report.observedRequestModels)
    $observedEfforts = @($Report.observedRequestEfforts)
    return $Report.id -ceq $Attempt.id -and $Report.taskId -ceq $Attempt.taskId -and $Report.arm -ceq $Attempt.arm -and
        $Report.requestedModel -ceq $Attempt.model -and $Report.requestedEffort -ceq $Attempt.effort -and
        $observedModels.Count -eq 1 -and $observedModels[0] -ceq $Attempt.model -and
        $observedEfforts.Count -eq 1 -and $observedEfforts[0] -ceq $Attempt.effort -and
        $Report.sourceBindings.inputSha256 -ceq $Attempt.input.sha256 -and
        $Report.sourceBindings.actorBrokerSha256 -ceq $Attempt.runner.sha256 -and
        $Report.launches -eq 1 -and $Report.completed.turn.status -ceq 'completed' -and
        $Report.clientClosed -eq $true -and $Report.nativeStreamsClosed -eq $true -and
        $Report.workerClosed -eq $true -and $Report.activeToolTasksAtClose -eq 0 -and
        @($Report.failures).Count -eq 0
}

function Assert-ResultIdentity([System.Collections.IDictionary]$Result, [System.Collections.IDictionary]$Attempt, [string]$Path, [string]$ManifestSha256, [string]$ManifestPath) {
    Assert-Value ($Result.schema -ceq 'navlyn.real-task-run-result.v1' -and
        $Result.id -ceq $Attempt.id -and $Result.taskId -ceq $Attempt.taskId -and $Result.arm -ceq $Attempt.arm -and
        $Result.model -ceq $Attempt.model -and $Result.effort -ceq $Attempt.effort -and
        $Result.manifestSha256 -ceq $ManifestSha256 -and $Result.manifestPath -ceq $ManifestPath -and
        $Result.inputSha256 -ceq $Attempt.input.sha256 -and $Result.runnerSha256 -ceq $Attempt.runner.sha256 -and
        $Result.scorePlanSha256 -ceq $Attempt.scorePlan.sha256 -and $Result.oracleSha256 -ceq $Attempt.oracleSha256 -and
        $Result.status -cin @('completed', 'failed')) 'RESULT_IDENTITY_CHANGED'
    $directory = Split-Path -Parent $Path
    foreach ($name in @('stdout', 'stderr')) {
        $file = Join-Path $directory "$name.txt"
        Assert-Value ((Test-Path -LiteralPath $file -PathType Leaf) -and (Get-Hash $file) -ceq $Result["${name}Sha256"]) 'RESULT_OUTPUT_CHANGED'
    }
    if ($Result.reportPath) {
        Assert-Value ((Test-Path -LiteralPath $Result.reportPath -PathType Leaf) -and (Get-Hash $Result.reportPath) -ceq $Result.reportSha256) 'RESULT_REPORT_CHANGED'
    }
    if ($Result.status -ceq 'completed') {
        Assert-Value ($Result.exitCode -eq 0 -and $Result.timedOut -eq $false -and $Result.cleanupComplete -eq $true -and
            $Result.reportPath -and (Test-ClosedActorReport (Read-Json $Result.reportPath) $Attempt)) 'RESULT_COMPLETION_CHANGED'
    }
}

function Assert-ScoreIdentity([System.Collections.IDictionary]$Score, [string]$ScorePath, [System.Collections.IDictionary]$Run, [string]$RunPath) {
    $scoreRoot = Split-Path -Parent $ScorePath
    $plan = Read-Json $Run.scorePlanPath
    $launchPath = Join-Path $scoreRoot 'launch.json'
    Assert-Value (Test-Path -LiteralPath $launchPath -PathType Leaf) 'SCORE_LAUNCH_MISSING'
    $launch = Read-Json $launchPath
    $actor = Read-Json $Run.reportPath
    $runHash = Get-Hash $RunPath
    Assert-Value ($Score.schema -ceq 'navlyn.real-task-score-result.v1' -and
        $Score.scoreId -ceq (Split-Path -Leaf $scoreRoot) -and $Score.taskId -ceq $Run.taskId -and
        $Score.workerAuditId -ceq $actor.workerAuditId -and
        $Score.runResultSha256 -ceq $runHash -and $Score.actorReportSha256 -ceq $Run.reportSha256 -and
        $Score.scorePlanSha256 -ceq $Run.scorePlanSha256 -and $Score.oracleSha256 -ceq $Run.oracleSha256 -and
        $Score.adapterSha256 -ceq $plan.adapter.sha256 -and
        $launch.scoreId -ceq $Score.scoreId -and $launch.taskId -ceq $Run.taskId -and
        $launch.workerAuditId -ceq $actor.workerAuditId -and $launch.runResultSha256 -ceq $runHash -and
        $launch.actorReportSha256 -ceq $Run.reportSha256 -and $launch.scorePlanSha256 -ceq $Run.scorePlanSha256 -and
        $launch.oracleSha256 -ceq $Run.oracleSha256 -and $launch.adapterSha256 -ceq $plan.adapter.sha256 -and
        $Score.status -cin @('scored', 'failed') -and $Score.acceptedActorScore -eq $false) 'SCORE_IDENTITY_CHANGED'
    foreach ($name in @('stdout', 'stderr')) {
        $file = Join-Path $scoreRoot "$name.txt"
        Assert-Value ((Test-Path -LiteralPath $file -PathType Leaf) -and (Get-Hash $file) -ceq $Score["${name}Sha256"]) 'SCORE_OUTPUT_CHANGED'
    }
    if ($Score.status -ceq 'scored') {
        Assert-Value ($Score.exitCode -eq 0 -and $Score.timedOut -eq $false -and $Score.cleanupComplete -eq $true -and
            $Score.behaviorPassed -is [bool]) 'SCORE_PROCESS_CHANGED'
        $output = Read-Json (Join-Path $scoreRoot 'stdout.txt')
        Assert-Value ($output.scoreId -ceq $Score.scoreId -and $output.taskId -ceq $Run.taskId -and
            $output.workerAuditId -ceq $actor.workerAuditId -and $output.hiddenOracleSha256 -ceq $Run.oracleSha256 -and
            $output.behaviorPassed -ceq $Score.behaviorPassed) 'SCORE_BEHAVIOR_CHANGED'
    }
}

$manifestPath = Get-AbsolutePath $Manifest (Get-Location).Path
$manifestBase = Split-Path -Parent $manifestPath
$manifestHash = Get-Hash $manifestPath
$campaign = Read-Json $manifestPath
Assert-ExactKeys $campaign @('schema', 'campaignId', 'attempts') 'MANIFEST_KEYS'
Assert-Value ($campaign.schema -ceq 'navlyn.real-task-campaign.v1') 'MANIFEST_SCHEMA'
Assert-Value ($campaign.campaignId -cmatch '^[a-z0-9][a-z0-9-]{5,79}$') 'CAMPAIGN_ID'
Assert-Value ($campaign.attempts -is [array] -and $campaign.attempts.Count -gt 0) 'ATTEMPTS_MISSING'

$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$blocks = @{}
foreach ($attempt in $campaign.attempts) {
    Assert-ExactKeys $attempt @('id', 'taskId', 'repetition', 'arm', 'model', 'effort', 'baseSha', 'promptSha256', 'oracleSha256', 'conditionsSha256', 'runner', 'input', 'scorePlan') 'ATTEMPT_KEYS'
    Assert-Value ($attempt.id -cmatch '^[a-z0-9][a-z0-9-]{5,79}$' -and $seen.Add([string]$attempt.id)) 'DUPLICATE_OR_INVALID_ATTEMPT_ID'
    Assert-Value ($attempt.taskId -cmatch '^[A-Z][A-Z0-9-]{2,30}$') 'TASK_ID'
    Assert-Value (($attempt.repetition -is [int] -or $attempt.repetition -is [long]) -and $attempt.repetition -ge 1) 'REPETITION'
    Assert-Value ($attempt.arm -cin @('A', 'B', 'C', 'D', 'skilloff')) 'ARM'
    Assert-Value ($attempt.model -cmatch '^gpt-[a-z0-9-]+$' -and $attempt.effort -cin @('low', 'medium', 'high')) 'MODEL_EFFORT'
    foreach ($name in @('baseSha', 'promptSha256', 'oracleSha256', 'conditionsSha256')) {
        Assert-Value ($attempt[$name] -cmatch '^[0-9a-f]{40,64}$') "HASH_FORMAT:$name"
    }
    $null = Resolve-Input $attempt.runner $manifestBase 'RUNNER'
    $null = Resolve-Input $attempt.input $manifestBase 'ACTOR_INPUT'
    $scorePlanPath = Resolve-Input $attempt.scorePlan $manifestBase 'SCORE_PLAN'
    $scorePlan = Read-Json $scorePlanPath
    Assert-ExactKeys $scorePlan @('schema', 'taskId', 'oracleSha256', 'adapter') 'SCORE_PLAN_KEYS'
    Assert-Value ($scorePlan.schema -ceq 'navlyn.real-task-score-plan.v1' -and $scorePlan.taskId -ceq $attempt.taskId -and $scorePlan.oracleSha256 -ceq $attempt.oracleSha256) 'SCORE_PLAN_BINDING'
    $null = Resolve-Input $scorePlan.adapter (Split-Path -Parent $scorePlanPath) 'SCORE_ADAPTER'
    $blockKey = "$($attempt.taskId)/$($attempt.repetition)"
    $conditions = "$($attempt.model)/$($attempt.effort)/$($attempt.baseSha)/$($attempt.promptSha256)/$($attempt.oracleSha256)/$($attempt.conditionsSha256)"
    if ($blocks.ContainsKey($blockKey)) { Assert-Value ($blocks[$blockKey] -ceq $conditions) "UNMATCHED_BLOCK:$blockKey" }
    else { $blocks[$blockKey] = $conditions }
}

$outputRoot = Get-AbsolutePath $OutputDirectory (Get-Location).Path
$registrationPath = Join-Path $outputRoot 'registration.json'
if ($Action -eq 'Register') {
    New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
    if (Test-Path -LiteralPath $registrationPath) {
        $existing = Read-Json $registrationPath
        Assert-Value ($existing.manifestSha256 -ceq $manifestHash) 'REGISTRATION_CONFLICT'
    }
    else {
        Save-Json $registrationPath ([ordered]@{ schema = 'navlyn.real-task-registration.v1'; campaignId = $campaign.campaignId; manifestSha256 = $manifestHash; registeredUtc = [DateTime]::UtcNow.ToString('o') })
    }
    @{ action = 'Register'; campaignId = $campaign.campaignId; attempts = $campaign.attempts.Count; manifestSha256 = $manifestHash } | ConvertTo-Json -Compress
    return
}

Assert-Value (Test-Path -LiteralPath $registrationPath -PathType Leaf) 'REGISTRATION_MISSING'
$registration = Read-Json $registrationPath
Assert-Value ($registration.manifestSha256 -ceq $manifestHash -and $registration.campaignId -ceq $campaign.campaignId) 'REGISTRATION_CHANGED'
$selected = @($campaign.attempts | Where-Object { -not $AttemptId -or $_.id -ceq $AttemptId })
Assert-Value ($selected.Count -gt 0) 'ATTEMPT_NOT_FOUND'

if ($Action -eq 'Aggregate') {
    $scores = @()
    if ($ScoreDirectory) {
        $scoreRoot = Get-AbsolutePath $ScoreDirectory (Get-Location).Path
        if (Test-Path -LiteralPath $scoreRoot) {
            foreach ($directory in @(Get-ChildItem -LiteralPath $scoreRoot -Directory)) {
                $scorePath = Join-Path $directory.FullName 'result.json'
                if (-not (Test-Path -LiteralPath $scorePath -PathType Leaf)) { continue }
                $score = Read-Json $scorePath
                Assert-Value ($score.schema -ceq 'navlyn.real-task-score-result.v1' -and $score.scoreId -ceq $directory.Name) 'SCORE_SCHEMA_OR_IDENTITY'
                $scores += [pscustomobject]@{ path = $scorePath; value = $score; resultSha256 = Get-Hash $scorePath }
            }
        }
    }
    $rows = foreach ($attempt in $selected) {
        $path = Join-Path (Join-Path $outputRoot $attempt.id) 'result.json'
        if (Test-Path -LiteralPath $path) {
            $result = Read-Json $path
            Assert-ResultIdentity $result $attempt $path $manifestHash $manifestPath
            $resultHash = Get-Hash $path
            $matchedScores = @($scores | Where-Object { $_.value.runResultSha256 -ceq $resultHash -and $_.value.taskId -ceq $attempt.taskId } | ForEach-Object {
                Assert-ScoreIdentity $_.value $_.path $result $path
                [ordered]@{ scoreId = $_.value.scoreId; status = $_.value.status; behaviorPassed = $(if ($_.value.status -ceq 'scored') { $_.value.behaviorPassed } else { $null }); acceptedActorScore = $false; resultSha256 = $_.resultSha256 }
            } | Sort-Object scoreId)
            [ordered]@{ id = $attempt.id; taskId = $attempt.taskId; arm = $attempt.arm; status = $result.status; reportSha256 = $result.reportSha256; scores = $matchedScores }
        }
        else { [ordered]@{ id = $attempt.id; taskId = $attempt.taskId; arm = $attempt.arm; status = 'not-run'; reportSha256 = $null; scores = @() } }
    }
    $matchedIds = @($rows | ForEach-Object { $_.scores } | ForEach-Object { $_.scoreId })
    $orphanScores = @($scores | Where-Object { $_.value.scoreId -cnotin $matchedIds } | ForEach-Object { [ordered]@{ scoreId = $_.value.scoreId; status = 'unmatched'; resultSha256 = $_.resultSha256 } } | Sort-Object scoreId)
    @{ schema = 'navlyn.real-task-aggregate.v1'; campaignId = $campaign.campaignId; manifestSha256 = $manifestHash; attempts = $rows; orphanScores = $orphanScores } | ConvertTo-Json -Depth 20
    return
}

foreach ($attempt in $selected) {
    $runner = Resolve-Input $attempt.runner $manifestBase 'RUNNER'
    $input = Resolve-Input $attempt.input $manifestBase 'ACTOR_INPUT'
    $scorePlanPath = Resolve-Input $attempt.scorePlan $manifestBase 'SCORE_PLAN'
    $attemptRoot = Join-Path $outputRoot $attempt.id
    $resultPath = Join-Path $attemptRoot 'result.json'
    if ($Action -eq 'Resume' -and (Test-Path -LiteralPath $resultPath)) {
        $result = Read-Json $resultPath
        Assert-ResultIdentity $result $attempt $resultPath $manifestHash $manifestPath
        @{ id = $attempt.id; action = 'Resume'; status = $(if ($result.status -ceq 'completed') { 'skipped-completed' } else { 'preserved-failure' }) } | ConvertTo-Json -Compress
        continue
    }
    if ($Action -eq 'Resume' -and (Test-Path -LiteralPath $attemptRoot)) {
        @{ id = $attempt.id; action = 'Resume'; status = 'preserved-incomplete' } | ConvertTo-Json -Compress
        continue
    }
    if ($Action -eq 'Preflight') {
        $preflightDirectory = Join-Path $outputRoot 'preflight'
        New-Item -ItemType Directory -Force -Path $preflightDirectory | Out-Null
        $preflightPath = Join-Path $preflightDirectory "$($attempt.id).json"
        if (Test-Path -LiteralPath $preflightPath) {
            $prior = Read-Json $preflightPath
            Assert-Value ($prior.runnerSha256 -ceq $attempt.runner.sha256 -and $prior.inputSha256 -ceq $attempt.input.sha256) 'PREFLIGHT_IDENTITY_CHANGED'
            @{ id = $attempt.id; action = 'Preflight'; passed = $prior.passed; preserved = $true } | ConvertTo-Json -Compress
            Assert-Value $prior.passed "PREFLIGHT_FAILED:$($attempt.id)"
            continue
        }
        $check = Invoke-BoundedNode $runner @($input, '--check') 60
        $validCheck = $null
        try { $validCheck = $check.stdout | ConvertFrom-Json -AsHashtable } catch { }
        $passed = $check.exitCode -eq 0 -and -not $check.timedOut -and $check.cleanupComplete -and
            $null -ne $validCheck -and $validCheck.valid -ceq $true -and $validCheck.modelLaunches -eq 0 -and
            (-not $validCheck.ContainsKey('id') -or $validCheck.id -ceq $attempt.id)
        Save-Json $preflightPath ([ordered]@{ schema = 'navlyn.real-task-preflight.v1'; id = $attempt.id; runnerSha256 = $attempt.runner.sha256; inputSha256 = $attempt.input.sha256; passed = $passed; exitCode = $check.exitCode; timedOut = $check.timedOut; stdout = $check.stdout; stderr = $check.stderr; finishedUtc = [DateTime]::UtcNow.ToString('o') })
        @{ id = $attempt.id; action = 'Preflight'; passed = $passed; preserved = $false } | ConvertTo-Json -Compress
        Assert-Value $passed "PREFLIGHT_FAILED:$($attempt.id)"
        continue
    }
    $preflightPath = Join-Path (Join-Path $outputRoot 'preflight') "$($attempt.id).json"
    Assert-Value (Test-Path -LiteralPath $preflightPath -PathType Leaf) "PREFLIGHT_MISSING:$($attempt.id)"
    $preflight = Read-Json $preflightPath
    Assert-Value ($preflight.passed -and $preflight.runnerSha256 -ceq $attempt.runner.sha256 -and $preflight.inputSha256 -ceq $attempt.input.sha256) "PREFLIGHT_CHANGED:$($attempt.id)"
    Assert-Value (-not (Test-Path -LiteralPath $attemptRoot)) "ATTEMPT_ALREADY_EXISTS:$($attempt.id)"
    New-Item -ItemType Directory -Path $attemptRoot | Out-Null
    Save-Json (Join-Path $attemptRoot 'launch.json') ([ordered]@{ schema = 'navlyn.real-task-launch.v1'; id = $attempt.id; manifestSha256 = $manifestHash; runnerSha256 = $attempt.runner.sha256; inputSha256 = $attempt.input.sha256; scorePlanSha256 = $attempt.scorePlan.sha256; startedUtc = [DateTime]::UtcNow.ToString('o') })
    $run = Invoke-BoundedNode $runner @($input) 1500
    [IO.File]::WriteAllText((Join-Path $attemptRoot 'stdout.txt'), $run.stdout, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $attemptRoot 'stderr.txt'), $run.stderr, [Text.UTF8Encoding]::new($false))
    $reportPath = $null
    $reportHash = $null
    $status = 'failed'
    if ($run.exitCode -eq 0 -and -not $run.timedOut -and $run.cleanupComplete) {
        try {
            $summary = $run.stdout | ConvertFrom-Json -AsHashtable
            if ($summary.id -ceq $attempt.id) {
                $reportPath = Join-Path ([string]$summary.out) 'report.json'
                if (Test-Path -LiteralPath $reportPath -PathType Leaf) {
                    $report = Read-Json $reportPath
                    $reportHash = Get-Hash $reportPath
                    if (Test-ClosedActorReport $report $attempt) { $status = 'completed' }
                }
            }
        }
        catch { $status = 'failed' }
    }
    Save-Json $resultPath ([ordered]@{ schema = 'navlyn.real-task-run-result.v1'; id = $attempt.id; taskId = $attempt.taskId; arm = $attempt.arm; model = $attempt.model; effort = $attempt.effort; manifestPath = $manifestPath; manifestSha256 = $manifestHash; runnerSha256 = $attempt.runner.sha256; inputSha256 = $attempt.input.sha256; scorePlanPath = $scorePlanPath; scorePlanSha256 = $attempt.scorePlan.sha256; oracleSha256 = $attempt.oracleSha256; status = $status; exitCode = $run.exitCode; timedOut = $run.timedOut; cleanupComplete = $run.cleanupComplete; stdoutSha256 = Get-Hash (Join-Path $attemptRoot 'stdout.txt'); stderrSha256 = Get-Hash (Join-Path $attemptRoot 'stderr.txt'); reportPath = $reportPath; reportSha256 = $reportHash; finishedUtc = [DateTime]::UtcNow.ToString('o') })
    @{ id = $attempt.id; action = $Action; status = $status; reportSha256 = $reportHash } | ConvertTo-Json -Compress
}

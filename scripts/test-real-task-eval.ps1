[CmdletBinding()]
param([string]$OutputDirectory = 'artifacts/evals/real-task-harness-test')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$scriptPath = Join-Path $PSScriptRoot 'run-real-task-eval.ps1'
$root = Join-Path ([IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory))) ([guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root -Force | Out-Null

function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Write-Json([string]$Path, [object]$Value) {
    [IO.File]::WriteAllText($Path, (($Value | ConvertTo-Json -Depth 20) + "`n"), [Text.UTF8Encoding]::new($false))
}
function Invoke-Harness([string]$Action, [string]$Manifest) {
    $output = & $scriptPath -Action $Action -Manifest $Manifest -OutputDirectory (Join-Path $root 'results') 2>&1
    return @($output)
}

$runner = Join-Path $root 'fake-runner.mjs'
[IO.File]::WriteAllText($runner, @'
import {readFileSync,writeFileSync,mkdirSync} from 'node:fs';
import {createHash} from 'node:crypto';
const input=JSON.parse(readFileSync(process.argv[2]));
if(process.argv[3]==='--check'){console.log(JSON.stringify({valid:!input.preflightInvalid,modelLaunches:0}));process.exit(0);}
if(input.fail){console.error('synthetic failure');process.exit(17);}
mkdirSync(input.out,{recursive:true});
const sha=p=>createHash('sha256').update(readFileSync(p)).digest('hex');
writeFileSync(input.out+'/report.json',JSON.stringify({id:input.id,taskId:'FIXTURE-B',arm:input.arm,requestedModel:'gpt-6-luna',requestedEffort:'medium',observedRequestModels:['gpt-6-luna'],observedRequestEfforts:['medium'],sourceBindings:{inputSha256:sha(process.argv[2]),actorBrokerSha256:sha(process.argv[1])},launches:1,clientClosed:true,nativeStreamsClosed:true,workerClosed:true,activeToolTasksAtClose:0,workerAuditId:'task-v19-00000000000000000000000000000001',completed:{turn:{status:'completed'}},failures:[]})+'\n');
console.log(JSON.stringify({id:input.id,out:input.out,modelLaunches:0}));
'@, [Text.UTF8Encoding]::new($false))
$input = Join-Path $root 'input.json'
Write-Json $input @{ id = 'fixture-a-000001'; arm = 'A'; out = (Join-Path $root 'broker-report') }
$failedInput = Join-Path $root 'failed-input.json'
Write-Json $failedInput @{ id = 'fixture-b-000001'; arm = 'B'; out = (Join-Path $root 'unused-report'); fail = $true }
$digest = ('a' * 64)
$oracle = ('c' * 64)
$adapter = Join-Path $root 'fake-scorer.ps1'
$adapterSource = @'
param([string]$TaskId, [string]$WorkerId, [string]$ScoreId)
$oracle = if ($ScoreId -eq 'fixture-score-wrong') { 'd' * 64 } else { '<ORACLE>' }
@{ taskId = $TaskId; workerAuditId = $WorkerId; hiddenOracleSha256 = $oracle; behaviorPassed = $true; independentDiffReviewPending = $true; scoreId = $ScoreId } | ConvertTo-Json -Compress
'@
[IO.File]::WriteAllText($adapter, $adapterSource.Replace('<ORACLE>', $oracle), [Text.UTF8Encoding]::new($false))
$scorePlan = Join-Path $root 'score-plan.json'
Write-Json $scorePlan @{ schema = 'navlyn.real-task-score-plan.v1'; taskId = 'FIXTURE-B'; oracleSha256 = $oracle; adapter = @{ path = $adapter; sha256 = (Hash $adapter) } }
$attempt = [ordered]@{
    id = 'fixture-a-000001'; taskId = 'FIXTURE-B'; repetition = 1; arm = 'A'
    model = 'gpt-6-luna'; effort = 'medium'; baseSha = ('b' * 40)
    promptSha256 = $digest; oracleSha256 = $oracle; conditionsSha256 = ('d' * 64)
    runner = @{ path = $runner; sha256 = (Hash $runner) }
    input = @{ path = $input; sha256 = (Hash $input) }
    scorePlan = @{ path = $scorePlan; sha256 = (Hash $scorePlan) }
}
$manifest = Join-Path $root 'campaign.json'
$failedAttempt = [ordered]@{}
foreach ($key in $attempt.Keys) { $failedAttempt[$key] = $attempt[$key] }
$failedAttempt.id = 'fixture-b-000001'
$failedAttempt.arm = 'B'
$failedAttempt.input = @{ path = $failedInput; sha256 = (Hash $failedInput) }
Write-Json $manifest @{ schema = 'navlyn.real-task-campaign.v1'; campaignId = 'fixture-campaign-001'; attempts = @($attempt, $failedAttempt) }

$registered = Invoke-Harness 'Register' $manifest
if (($registered | ConvertFrom-Json).attempts -ne 2) { throw 'Registration count' }
$preflight = @(Invoke-Harness 'Preflight' $manifest) | ForEach-Object { $_ | ConvertFrom-Json }
if ($preflight.Count -ne 2 -or @($preflight | Where-Object { -not $_.passed }).Count) { throw 'Preflight' }
$run = @(Invoke-Harness 'Run' $manifest) | ForEach-Object { $_ | ConvertFrom-Json }
if ($run.Count -ne 2 -or $run[0].status -ne 'completed' -or $run[1].status -ne 'failed') { throw 'Run' }
$resume = @(Invoke-Harness 'Resume' $manifest) | ForEach-Object { $_ | ConvertFrom-Json }
if ($resume.Count -ne 2 -or $resume[0].status -ne 'skipped-completed' -or $resume[1].status -ne 'preserved-failure') { throw 'Resume' }
$aggregate = Invoke-Harness 'Aggregate' $manifest | ConvertFrom-Json
if ($aggregate.attempts.Count -ne 2 -or $aggregate.attempts[0].status -ne 'completed' -or $aggregate.attempts[1].status -ne 'failed') { throw 'Aggregate' }

$scoreScript = Join-Path $PSScriptRoot 'score-real-task-eval.ps1'
$scoreResult = & $scoreScript -RunResult (Join-Path $root 'results/fixture-a-000001/result.json') -ScoreId 'fixture-score-000001' -OutputDirectory (Join-Path $root 'scores') | ConvertFrom-Json
if ($scoreResult.status -ne 'scored' -or -not $scoreResult.behaviorPassed -or $scoreResult.acceptedActorScore) { throw 'Score adapter' }
$scoreAgainFailed = $false
try { $null = & $scoreScript -RunResult (Join-Path $root 'results/fixture-a-000001/result.json') -ScoreId 'fixture-score-000001' -OutputDirectory (Join-Path $root 'scores') } catch { $scoreAgainFailed = $_.Exception.Message -match 'SCORE_ALREADY_EXISTS' }
if (-not $scoreAgainFailed) { throw 'Score overwrite accepted' }
$wrongScore = & $scoreScript -RunResult (Join-Path $root 'results/fixture-a-000001/result.json') -ScoreId 'fixture-score-wrong' -OutputDirectory (Join-Path $root 'scores') | ConvertFrom-Json
if ($wrongScore.status -ne 'failed' -or $wrongScore.acceptedActorScore) { throw 'Wrong oracle accepted' }
$scoredAggregate = & $scriptPath -Action Aggregate -Manifest $manifest -OutputDirectory (Join-Path $root 'results') -ScoreDirectory (Join-Path $root 'scores') | ConvertFrom-Json
if ($scoredAggregate.attempts[0].scores.Count -ne 2 -or $scoredAggregate.attempts[0].scores[0].status -ne 'scored' -or $scoredAggregate.attempts[0].scores[1].status -ne 'failed' -or $scoredAggregate.orphanScores.Count -ne 0) { throw 'Score aggregate' }
$scoreReceiptPath = Join-Path $root 'scores/fixture-score-000001/result.json'
$scoreReceiptBytes = [IO.File]::ReadAllBytes($scoreReceiptPath)
try {
    $tamperedScore = Get-Content $scoreReceiptPath -Raw | ConvertFrom-Json -AsHashtable
    $tamperedScore.acceptedActorScore = $true
    Write-Json $scoreReceiptPath $tamperedScore
    $scoreTamperRejected = $false
    try { $null = & $scriptPath -Action Aggregate -Manifest $manifest -OutputDirectory (Join-Path $root 'results') -ScoreDirectory (Join-Path $root 'scores') } catch { $scoreTamperRejected = $_.Exception.Message -match 'SCORE_IDENTITY_CHANGED' }
    if (-not $scoreTamperRejected) { throw 'Tampered score accepted' }
}
finally { [IO.File]::WriteAllBytes($scoreReceiptPath, $scoreReceiptBytes) }
$runReceiptPath = Join-Path $root 'results/fixture-a-000001/result.json'
$runReceiptBytes = [IO.File]::ReadAllBytes($runReceiptPath)
try {
    $tamperedRun = Get-Content $runReceiptPath -Raw | ConvertFrom-Json -AsHashtable
    $tamperedRun.timedOut = $true
    Write-Json $runReceiptPath $tamperedRun
    $runTamperRejected = $false
    try { $null = Invoke-Harness 'Resume' $manifest } catch { $runTamperRejected = $_.Exception.Message -match 'RESULT_COMPLETION_CHANGED' }
    if (-not $runTamperRejected) { throw 'Tampered run accepted' }
}
finally { [IO.File]::WriteAllBytes($runReceiptPath, $runReceiptBytes) }

$badPreflightInput = Join-Path $root 'bad-preflight-input.json'
Write-Json $badPreflightInput @{ id = 'fixture-c-000001'; arm = 'C'; out = (Join-Path $root 'unused-preflight-report'); preflightInvalid = $true }
$badPreflightAttempt = [ordered]@{}
foreach ($key in $attempt.Keys) { $badPreflightAttempt[$key] = $attempt[$key] }
$badPreflightAttempt.id = 'fixture-c-000001'
$badPreflightAttempt.arm = 'C'
$badPreflightAttempt.input = @{ path = $badPreflightInput; sha256 = (Hash $badPreflightInput) }
$badPreflightManifest = Join-Path $root 'bad-preflight-campaign.json'
Write-Json $badPreflightManifest @{ schema = 'navlyn.real-task-campaign.v1'; campaignId = 'fixture-preflight-001'; attempts = @($badPreflightAttempt) }
$badPreflightRoot = Join-Path $root 'bad-preflight-results'
$null = & $scriptPath -Action Register -Manifest $badPreflightManifest -OutputDirectory $badPreflightRoot
$badPreflightRejected = $false
try { $null = & $scriptPath -Action Preflight -Manifest $badPreflightManifest -OutputDirectory $badPreflightRoot } catch { $badPreflightRejected = $_.Exception.Message -match 'PREFLIGHT_FAILED' }
if (-not $badPreflightRejected -or (Get-Content (Join-Path $badPreflightRoot 'preflight/fixture-c-000001.json') -Raw | ConvertFrom-Json).passed -or (Test-Path (Join-Path $badPreflightRoot 'fixture-c-000001'))) { throw 'Invalid preflight accepted' }

$invalidManifest = Join-Path $root 'invalid-campaign.json'
$invalidAttempt = [ordered]@{}
foreach ($key in $failedAttempt.Keys) { $invalidAttempt[$key] = $failedAttempt[$key] }
$invalidAttempt.conditionsSha256 = ('e' * 64)
Write-Json $invalidManifest @{ schema = 'navlyn.real-task-campaign.v1'; campaignId = 'fixture-invalid-001'; attempts = @($attempt, $invalidAttempt) }
$invalidRejected = $false
try { $null = & $scriptPath -Action Register -Manifest $invalidManifest -OutputDirectory (Join-Path $root 'invalid-results') } catch { $invalidRejected = $_.Exception.Message -match 'UNMATCHED_BLOCK' }
if (-not $invalidRejected -or (Test-Path (Join-Path $root 'invalid-results'))) { throw 'Unmatched block was accepted' }

# A registered source mutation must fail before any replacement execution.
[IO.File]::AppendAllText($runner, "`n// changed`n")
$failed = $false
try { $null = Invoke-Harness 'Run' $manifest } catch { $failed = $_.Exception.Message -match 'RUNNER:HASH' }
if (-not $failed) { throw 'Changed runner was accepted' }
if ((Get-Content (Join-Path $root 'results/fixture-a-000001/result.json') -Raw | ConvertFrom-Json).status -ne 'completed') { throw 'Original result changed' }

@{ schema = 'navlyn.real-task-harness-test.v1'; noModel = $true; registration = $true; preflight = $true; invalidPreflightRejected = $true; run = $true; failurePreserved = $true; resume = $true; aggregate = $true; score = $true; scoreAggregate = $true; wrongOracleRejected = $true; scoreOverwriteRejected = $true; runTamperRejected = $true; scoreTamperRejected = $true; unmatchedBlockRejected = $true; hashMutationRejected = $true } | ConvertTo-Json -Compress

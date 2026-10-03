$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/navlyn-validated-release.ps1')
$sha = 'a' * 40
$run = @{ repository=@{full_name='furbon/navlyn'}; head_repository=@{full_name='furbon/navlyn'}; head_sha=$sha; head_branch='main'; event='push'; path='.github/workflows/ci.yml'; status='completed'; conclusion='success' }
$jobs = @('windows','ubuntu','macos') | ForEach-Object { @{name="Build and test ($_-latest)"; status='completed'; conclusion='success'} }
Assert-NavlynValidatedReleaseRun $run $sha $jobs
foreach ($case in @(@('head_sha',('b'*40)),@('head_branch','codex/candidate'),@('event','pull_request'),@('path','.github/workflows/other.yml'),@('conclusion','failure'),@('status','in_progress'))) {
    $changed = $run.Clone(); $changed[$case[0]] = $case[1]
    $rejected = $false
    try { Assert-NavlynValidatedReleaseRun $changed $sha $jobs } catch { $rejected = $true }
    if (!$rejected) { throw "Unvalidated release accepted: $($case[0])" }
}
$jobs[0].conclusion = 'failure'
$rejected = $false
try { Assert-NavlynValidatedReleaseRun $run $sha $jobs } catch { $rejected = $true }
if (!$rejected) { throw 'Failed required platform accepted.' }
$jobs[0].conclusion = 'success'
$run.id = 123; $run.run_attempt = 2
$run.repository.id = 456; $run.head_repository.id = 456
$run.run_started_at = '2026-10-03T01:00:00Z'; $run.updated_at = '2026-10-03T02:00:00Z'
$artifact = @{
    id = 789; name = 'navlyn-validated-release-123-2'; expired = $false
    digest = 'sha256:' + ('c' * 64); created_at = '2026-10-03T01:30:00Z'
    workflow_run = @{id=123; repository_id=456; head_repository_id=456; head_branch='main'; head_sha=$sha}
}
$state = @{run=$run; jobs=$jobs; artifacts=@($artifact)}
$read = {
    param($Route)
    if ($Route -like '*/workflows/ci.yml/runs?*') { return @{total_count=1; workflow_runs=@($state.run)} }
    if ($Route -like '*/attempts/2/jobs?*') { return @{total_count=3; jobs=$state.jobs} }
    if ($Route -like '*/123/artifacts?*') { return @{total_count=$state.artifacts.Count; artifacts=$state.artifacts} }
    if ($Route -ceq '/repos/furbon/navlyn/actions/runs/123/attempts/2') { return $state.run }
    throw "Unexpected fixture route: $Route"
}.GetNewClosure()
$validated = Get-NavlynValidatedRelease $sha $read
if ($validated.artifact.id -ne 789 -or $validated.digest -cne ('c'*64)) { throw 'Wrong validated inputs selected.' }
foreach ($case in @(@('name','navlyn-validated-release-123-1'),@('expired',$true),@('created_at','2026-10-03T00:00:00Z'),@('digest','invalid'))) {
    $changed = $artifact.Clone(); $changed[$case[0]] = $case[1]; $state.artifacts = @($changed)
    $rejected = $false
    try { Get-NavlynValidatedRelease $sha $read | Out-Null } catch { $rejected = $true }
    if (!$rejected) { throw "Invalid retained release accepted: $($case[0])" }
}
$state.artifacts = @($artifact, $artifact)
$rejected = $false
try { Get-NavlynValidatedRelease $sha $read | Out-Null } catch { $rejected = $true }
if (!$rejected) { throw 'Ambiguous retained release accepted.' }
Write-Output 'Validated release source and required-platform checks passed.'

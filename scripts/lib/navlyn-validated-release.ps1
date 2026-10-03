. (Join-Path $PSScriptRoot 'navlyn-publish-services.ps1')

function Assert-NavlynValidatedReleaseRun {
    param([Collections.IDictionary]$Run, [string]$SourceSha, [object[]]$Jobs)
    if ($Run.repository.full_name -cne 'furbon/navlyn' -or $Run.head_repository.full_name -cne 'furbon/navlyn' -or
        $Run.head_sha -cne $SourceSha -or $Run.head_branch -cne 'main' -or $Run.event -cne 'push' -or
        $Run.path -cnotin @('.github/workflows/ci.yml', '.github/workflows/ci.yml@main') -or
        $Run.status -cne 'completed' -or $Run.conclusion -cne 'success') {
        throw 'Release inputs require successful exact-main CI.'
    }
    foreach ($name in @('Build and test (windows-latest)','Build and test (ubuntu-latest)','Build and test (macos-latest)')) {
        $job = @($Jobs | Where-Object name -ceq $name)
        if ($job.Count -ne 1 -or $job[0].status -cne 'completed' -or $job[0].conclusion -cne 'success') { throw "Required CI job did not succeed: $name" }
    }
}

function Get-NavlynValidatedRelease {
    param([string]$SourceSha, [scriptblock]$Read)
    $runs = Get-NavlynGitHubPages "/repos/furbon/navlyn/actions/workflows/ci.yml/runs?head_sha=$SourceSha&event=push" 'workflow_runs' $Read 1000
    $run = @($runs | Where-Object head_branch -ceq 'main' | Sort-Object id -Descending | Select-Object -First 1)
    if ($run.Count -ne 1) { throw 'No exact-main CI run was found.' }
    $record = & $Read "/repos/furbon/navlyn/actions/runs/$($run[0].id)/attempts/$($run[0].run_attempt)"
    $jobs = Get-NavlynGitHubPages "/repos/furbon/navlyn/actions/runs/$($record.id)/attempts/$($record.run_attempt)/jobs" 'jobs' $Read
    Assert-NavlynValidatedReleaseRun $record $SourceSha $jobs
    $name = "navlyn-validated-release-$($record.id)-$($record.run_attempt)"
    $artifacts = Get-NavlynGitHubPages "/repos/furbon/navlyn/actions/runs/$($record.id)/artifacts" 'artifacts' $Read
    $artifact = @($artifacts | Where-Object name -ceq $name)
    if ($artifact.Count -ne 1 -or $artifact[0].digest -cnotmatch '^sha256:([a-f0-9]{64})$') { throw 'Validated release artifact is missing or ambiguous.' }
    $digest = $Matches[1]
    Assert-NavlynGitHubArtifact $artifact[0] $record $artifact[0].id $digest $name
    return @{ run = $record; artifact = $artifact[0]; digest = $digest }
}

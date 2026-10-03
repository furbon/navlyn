. (Join-Path $PSScriptRoot 'navlyn-publish-services.ps1')

function Assert-NavlynValidatedReleaseSource {
    param([Collections.IDictionary]$Run, [string]$SourceSha)
    if ($Run.repository.full_name -cne 'furbon/navlyn' -or $Run.head_repository.full_name -cne 'furbon/navlyn' -or
        $Run.head_sha -cne $SourceSha -or $Run.head_branch -cne 'main' -or $Run.event -cne 'push' -or
        $Run.path -cnotin @('.github/workflows/ci.yml', '.github/workflows/ci.yml@main') -or
        $Run.status -cne 'completed') {
        throw 'Release inputs require authentic completed exact-main CI.'
    }
}

function Assert-NavlynValidatedReleaseRun {
    param([Collections.IDictionary]$Run, [string]$SourceSha, [object[]]$Jobs)
    Assert-NavlynValidatedReleaseSource $Run $SourceSha
    if ($Run.conclusion -cne 'success') { throw 'Release inputs require successful exact-main CI.' }
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
    $jobs = Get-NavlynGitHubPages "/repos/furbon/navlyn/actions/runs/$($record.id)/jobs?filter=latest" 'jobs' $Read
    Assert-NavlynValidatedReleaseRun $record $SourceSha $jobs
    # Failed-only reruns retain successful jobs and their artifacts from earlier attempts.
    $windows = @($jobs | Where-Object name -ceq 'Build and test (windows-latest)')[0]
    $attempt = Get-NavlynPublicationPositiveInteger $windows.run_attempt 'validated Windows attempt' ([int]::MaxValue)
    $origin = if ($attempt -eq $record.run_attempt) { $record } else { & $Read "/repos/furbon/navlyn/actions/runs/$($record.id)/attempts/$attempt" }
    Assert-NavlynValidatedReleaseSource $origin $SourceSha
    if ($origin.id -ne $record.id -or $origin.run_attempt -ne $attempt -or $attempt -gt $record.run_attempt) { throw 'Validated artifact attempt differs from successful Windows CI.' }
    $name = "navlyn-validated-release-$($record.id)-$attempt"
    $artifacts = Get-NavlynGitHubPages "/repos/furbon/navlyn/actions/runs/$($record.id)/artifacts" 'artifacts' $Read
    $artifact = @($artifacts | Where-Object name -ceq $name)
    if ($artifact.Count -ne 1 -or $artifact[0].digest -cnotmatch '^sha256:([a-f0-9]{64})$') { throw 'Validated release artifact is missing or ambiguous.' }
    $digest = $Matches[1]
    Assert-NavlynGitHubArtifact $artifact[0] $origin $artifact[0].id $digest $name
    return @{ run = $record; artifactRun = $origin; artifact = $artifact[0]; digest = $digest }
}

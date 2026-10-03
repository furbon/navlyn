Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'navlyn-publish-services.ps1')

function Write-NavlynPublicationProgress {
    param([string]$Id, [string]$Stage, [int]$RemainingSeconds)
    if ($env:GITHUB_STEP_SUMMARY) {
        $remaining = if ($Stage -ceq 'waitingForIndexing') { "; ${RemainingSeconds}s remaining" } else { '' }
        "- $([DateTime]::UtcNow.ToString('HH:mm:ss')) UTC: $Id — $Stage$remaining" | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY
    }
}

function Invoke-NavlynExactPublication {
    param([Collections.IDictionary]$Manifest, [string]$InputRoot, [Collections.IDictionary]$Journal,
        [string]$JournalPath, [string[]]$PriorIntentIds, [scriptblock]$Observe, [scriptblock]$Push,
        [scriptblock]$Wait = { param($Seconds) Start-Sleep -Seconds $Seconds }, [int]$PollSeconds = 600,
        [scriptblock]$Now = { [DateTime]::UtcNow },
        [scriptblock]$Progress = { param($Id, $Stage, $Remaining) Write-NavlynPublicationProgress $Id $Stage $Remaining })
    if ($PollSeconds -lt 0 -or $PollSeconds -gt 900) { throw 'Publication polling must be bounded at 900 seconds.' }
    if ($Journal.phase -cne 'initialized') { throw 'Only a new retained attempt may execute publication.' }
    # Initialize durably even if observation, credentials or the first push fails.
    Write-NavlynPublicationJournal $JournalPath $Journal
    try {
        foreach ($id in @('navlyn', 'navlyn-mcp')) {
            $package = @($Manifest.packages | Where-Object { $_.id -ceq $id })
            if ($package.Count -ne 1) { throw 'Both retained package identities are required.' }
            $entry = $package[0]
            & $Progress $id 'observing' 0
            $file = Get-NavlynPublicationInputFile $InputRoot $entry.path
            $observation = & $Observe $entry $file
            $decision = Get-NavlynPublicationDecision $id ($id -cin $PriorIntentIds) $observation
            $state = @($Journal.packages | Where-Object { $_.id -ceq $id })[0]
            if ($decision -ceq 'skipExactPublic') {
                $state.state = 'alreadyPublic'; $state.result = $observation
                Write-NavlynPublicationJournal $JournalPath $Journal
                & $Progress $id 'alreadyPublic' 0
                continue
            }
            if ($decision -ceq 'indeterminate') { throw "Publication of $id is indeterminate; absent indexing cannot authorize retry." }
            if ((Get-NavlynPublicationHash $file) -cne $entry.sha256) { throw 'Retained package changed before push.' }
            Assert-NavlynPublicationPackageIdentity $file $id $entry.version $Manifest.sourceSha
            Set-NavlynPublicationIntent $Journal $id $JournalPath
            & $Progress $id 'intentRetained' 0
            try {
                $result = & $Push $entry $file
                $state.result = $result
                $state.state = if ($result.exitCode -eq 0) { 'intent' } else { 'failed' }
                Write-NavlynPublicationJournal $JournalPath $Journal
                & $Progress $id $(if ($result.exitCode -eq 0) { 'pushSubmitted' } else { 'pushFailedAwaitingVerification' }) 0
            } catch {
                $state.state = 'indeterminate'; $state.result = @{ error = $_.Exception.Message }
                Write-NavlynPublicationJournal $JournalPath $Journal
                & $Progress $id 'indeterminate' 0
                throw
            }
            # A failed/timed-out push may have been accepted. Observe it, never repeat it.
            $started = & $Now
            do {
                $observed = & $Observe $entry $file
                $after = Get-NavlynPublicationDecision $id $true $observed
                if ($after -ceq 'skipExactPublic') { break }
                $remaining = $PollSeconds - ((& $Now) - $started).TotalSeconds
                if ($remaining -le 0) { break }
                [Console]::Error.WriteLine("Waiting for NuGet indexing of $id $($entry.version) ($([int]$remaining)s remaining).")
                & $Progress $id 'waitingForIndexing' ([int]$remaining)
                & $Wait ([Math]::Min(15, $remaining))
            } while ($true)
            if ($after -cne 'skipExactPublic') {
                $state.state = 'indeterminate'
                Write-NavlynPublicationJournal $JournalPath $Journal
                & $Progress $id 'indeterminate' 0
                throw "Acceptance/public verification of $id is indeterminate; reconcile this retained intent before resuming."
            }
            $state.state = 'verified'; $state.result = @{ push = $result; public = $observed }
            Write-NavlynPublicationJournal $JournalPath $Journal
            & $Progress $id 'verified' 0
        }
        $Journal.phase = 'complete'
    } catch {
        $Journal.phase = 'blocked'
        throw
    } finally {
        $Journal.updatedUtc = [DateTime]::UtcNow.ToString('O')
        Write-NavlynPublicationJournal $JournalPath $Journal
    }
}

function Get-NavlynRetainedChain {
    param([Collections.IDictionary]$Identity, [object[]]$Inventory, [long]$LatestRunId, [int]$LatestAttempt,
        [long]$LatestArtifactId, [string]$LatestDigest, [string]$Root, [scriptblock]$Read, [scriptblock]$Download)
    $records = [Collections.Generic.List[object]]::new()
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $runId = $LatestRunId; $attempt = $LatestAttempt; $artifactId = $LatestArtifactId; $digest = $LatestDigest
    while ($true) {
        if (!$seen.Add("${runId}:$attempt") -or $seen.Count -gt 1000) { throw 'Forked or oversized retained journal chain.' }
        $run = & $Read "/repos/furbon/navlyn/actions/runs/$runId/attempts/$attempt"
        Assert-NavlynPublisherAttempt $run $Identity.sourceSha $runId $attempt
        $name = "navlyn-publication-journal-$runId-$attempt"
        $artifacts = Get-NavlynGitHubPages "/repos/furbon/navlyn/actions/runs/$runId/artifacts" 'artifacts' $Read
        $matches = @($artifacts | Where-Object { $_.name -ceq $name })
        if ($matches.Count -ne 1) { throw 'Retained journal artifact is missing, repeated or expired.' }
        $artifact = $matches[0]
        if ($artifactId -eq 0) { $artifactId = Get-NavlynPublicationPositiveInteger $artifact.id 'predecessor artifact' }
        Assert-NavlynGitHubArtifact $artifact $run $artifactId $digest $name
        $zip = Join-Path $Root "$runId-$attempt.zip"
        & $Download $artifactId $digest $zip
        $directory = Join-Path $Root "$runId-$attempt"
        Expand-NavlynPublicationArtifact $zip $directory
        if (@(Get-ChildItem -LiteralPath $directory -File).Count -ne 1) { throw 'Journal artifact must contain exactly one journal.' }
        $path = Get-NavlynPublicationInputFile $directory 'navlyn-publication-journal.json'
        $record = @{ path = $path; sha256 = Get-NavlynPublicationHash $path }
        $verified = Read-NavlynPublicationJournalRecord $record
        if ($verified.journal.runId -ne $runId -or $verified.journal.runAttempt -ne $attempt) { throw 'Authenticated artifact and journal attempt differ.' }
        $records.Insert(0, $record)
        $predecessor = $verified.journal.predecessor
        if ($null -eq $predecessor) { break }
        $runId = Get-NavlynPublicationPositiveInteger $predecessor.runId 'predecessor run'
        $attempt = Get-NavlynPublicationPositiveInteger $predecessor.runAttempt 'predecessor attempt' ([int]::MaxValue)
        $digest = $predecessor.sha256
        # Predecessor refers to journal file bytes, not the enclosing service ZIP.
        $previousRun = & $Read "/repos/furbon/navlyn/actions/runs/$runId/attempts/$attempt"
        Assert-NavlynPublisherAttempt $previousRun $Identity.sourceSha $runId $attempt
        $previousArtifacts = Get-NavlynGitHubPages "/repos/furbon/navlyn/actions/runs/$runId/artifacts" 'artifacts' $Read
        $previous = @($previousArtifacts | Where-Object { $_.name -ceq "navlyn-publication-journal-$runId-$attempt" })
        if ($previous.Count -ne 1 -or $previous[0].digest -cnotmatch '^sha256:([a-f0-9]{64})$') { throw 'Predecessor artifact unavailable.' }
        $artifactId = [long]$previous[0].id; $digest = $Matches[1]
    }
    Test-NavlynPublicationChain $Identity $records.ToArray() $Inventory
}

function Assert-NavlynProtectedPublisher {
    param([string]$ExpectedSha)
    if ($ExpectedSha -cnotmatch '^[a-f0-9]{40}$' -or $env:GITHUB_ACTIONS -cne 'true' -or
        $env:GITHUB_REPOSITORY -cne 'furbon/navlyn' -or $env:GITHUB_REF -cne 'refs/heads/main' -or
        $env:GITHUB_SHA -cne $ExpectedSha -or $env:GITHUB_EVENT_NAME -cne 'workflow_dispatch' -or
        $env:GITHUB_WORKFLOW_REF -cne 'furbon/navlyn/.github/workflows/publish-nuget.yml@refs/heads/main') { throw 'Exact protected main publisher identity is required.' }
    $head = (& git -C (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $head -cne $ExpectedSha) { throw 'Checkout differs from explicit expected main SHA.' }
}

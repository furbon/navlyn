[CmdletBinding()]
param(
    [string]$JournalPath = 'artifacts/publication-attempt/journal/navlyn-publication-journal.json',
    [string]$JournalArtifactId = $env:JOURNAL_ARTIFACT_ID,
    [string]$JournalArtifactDigest = $env:JOURNAL_ARTIFACT_DIGEST
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/navlyn-publish-recovery.ps1')
if (!$env:GITHUB_STEP_SUMMARY) { return }
$lines = [Collections.Generic.List[string]]::new()
$lines.Add('## Publication result and recovery')
if (!(Test-Path -LiteralPath $JournalPath -PathType Leaf)) {
    $lines.Add('No publication journal was initialized. Inspect the failed preparation step before choosing normal or resume mode.')
} else {
    $journal = Read-NavlynPublicationJson ([IO.Path]::GetFullPath($JournalPath))
    $identity = $journal.identity
    [void](Get-NavlynPublicationIdentityText $identity)
    $runId = Get-NavlynPublicationPositiveInteger $journal.runId 'journal run'
    $attempt = Get-NavlynPublicationPositiveInteger $journal.runAttempt 'journal attempt'
    $lines.Add("Release $($identity.version), source ``$($identity.sourceSha)``, attempt ${runId}:$attempt, phase **$($journal.phase)**.")
    $lines.Add('')
    $lines.Add('| Package | State | Retained push intent |')
    $lines.Add('| --- | --- | --- |')
    foreach ($package in $journal.packages) { $lines.Add("| $($package.id) | $($package.state) | $($package.intentUtc) |") }
    $lines.Add('')
    if ($journal.phase -ceq 'complete') {
        $lines.Add('Both public packages match the retained inputs. No recovery is needed.')
    } elseif ($JournalArtifactId -and $JournalArtifactDigest) {
        $artifactId = Get-NavlynPublicationPositiveInteger $JournalArtifactId 'retained journal artifact'
        Assert-NavlynPublicationDigest $JournalArtifactDigest 'retained journal artifact digest'
        $lines.Add('After reconciling public indexing, resume with these exact retained identities. Resume verifies public packages and never repeats a retained push intent.')
        $lines.Add('')
        $lines.Add('```sh')
        $lines.Add("gh workflow run publish-nuget.yml --repo furbon/navlyn --ref main -f mode=resume -f expected-main-sha=$($identity.sourceSha) -f input-run-attempt=$($identity.inputRunId):$($identity.inputRunAttempt) -f input-artifact-id=$($identity.artifactId) -f input-artifact-sha256=$($identity.artifactDigest) -f manifest-sha256=$($identity.manifestSha256) -f latest-run-attempt=${runId}:$attempt -f latest-journal-artifact-id=$artifactId -f latest-journal-artifact-sha256=$JournalArtifactDigest")
        $lines.Add('```')
    } else {
        $lines.Add('Journal artifact retention was not confirmed. Recover the immutable artifact ID and service SHA-256 before resuming; do not reset with normal mode.')
    }
}
[IO.File]::AppendAllText($env:GITHUB_STEP_SUMMARY, ($lines -join "`n") + "`n", [Text.UTF8Encoding]::new($false))

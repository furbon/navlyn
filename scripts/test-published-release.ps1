[CmdletBinding()]
param([Parameter(Mandatory)][string]$SourceSha, [Parameter(Mandatory)][string]$Tag)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/navlyn-validated-release.ps1')
. (Join-Path $PSScriptRoot 'lib/navlyn-release-version.ps1')
$repo = Split-Path -Parent $PSScriptRoot
if ($Tag -cne ('v' + (Get-NavlynReleaseVersion)) -or (& git -C $repo rev-parse HEAD).Trim() -cne $SourceSha) { throw 'Tag and checked-out release identity differ.' }
$read = { param($Route) Invoke-NavlynGitHubJson $Route (Join-Path $repo 'artifacts/tag-provenance') }
$tagRef = & $read "/repos/furbon/navlyn/git/ref/tags/$Tag"
if ($tagRef.object.type -cne 'tag') { throw 'An annotated release tag is required.' }
$tagObject = & $read "/repos/furbon/navlyn/git/tags/$($tagRef.object.sha)"
if ($tagObject.object.type -cne 'commit' -or $tagObject.object.sha -cne $SourceSha) { throw 'Annotated tag points to different source.' }
& (Join-Path $PSScriptRoot 'reuse-validated-release.ps1') -ExpectedSha $SourceSha -Output 'artifacts/tag-inputs'
$root = Join-Path $repo 'artifacts/tag-inputs'
$manifestHash = Get-NavlynPublicationHash (Join-Path $root 'navlyn-publication-inputs.json')
$inputs = Read-NavlynPublicationInputs $root $SourceSha $manifestHash -RequireAssets
$runs = Get-NavlynGitHubPages "/repos/furbon/navlyn/actions/workflows/publish-nuget.yml/runs?head_sha=$SourceSha" 'workflow_runs' $read 1000
$published = @($runs | Where-Object { $_.head_branch -ceq 'main' -and $_.status -ceq 'completed' -and $_.conclusion -ceq 'success' } | Sort-Object id -Descending | Select-Object -First 1)
if ($published.Count -ne 1) { throw 'Successful protected publication was not found.' }
$run = & $read "/repos/furbon/navlyn/actions/runs/$($published[0].id)/attempts/$($published[0].run_attempt)"
Assert-NavlynPublisherAttempt $run $SourceSha $run.id $run.run_attempt
$name = "navlyn-publication-journal-$($run.id)-$($run.run_attempt)"
$artifacts = Get-NavlynGitHubPages "/repos/furbon/navlyn/actions/runs/$($run.id)/artifacts" 'artifacts' $read
$artifact = @($artifacts | Where-Object name -ceq $name)
if ($artifact.Count -ne 1 -or $artifact[0].digest -cnotmatch '^sha256:([a-f0-9]{64})$') { throw 'Complete publication journal is unavailable.' }
$digest = $Matches[1]
Assert-NavlynGitHubArtifact $artifact[0] $run $artifact[0].id $digest $name
$journalZip = Join-Path $repo 'artifacts/tag-journal.zip'
Save-NavlynGitHubArtifact $artifact[0].id $digest $journalZip
$journalRoot = Join-Path $repo 'artifacts/tag-journal'
Expand-NavlynPublicationArtifact $journalZip $journalRoot
$journal = Read-NavlynPublicationJson (Join-Path $journalRoot 'navlyn-publication-journal.json')
if ($journal.phase -cne 'complete' -or $journal.identity.sourceSha -cne $SourceSha -or $journal.identity.manifestSha256 -cne $manifestHash) { throw 'Published inputs differ from the tested main release.' }
$verificationRoot = Join-Path $repo 'artifacts/tag-public-verification'
$trust = New-NavlynNuGetTrust $verificationRoot
foreach ($package in $inputs.packages) {
    $public = Get-NavlynNuGetObservation $package (Get-NavlynPublicationInputFile $root $package.path) $trust $verificationRoot
    if ($public.status -cne 'present' -or !$public.signatureTrusted -or !$public.canonicalMatch) { throw "Public package is not the tested release: $($package.id)" }
    Write-Output "$($package.id) $($package.version): public signature and tested content verified."
}
Write-Output "Annotated $Tag matches successful main CI and protected publication at $SourceSha."

[CmdletBinding()]
param([Parameter(Mandatory)][ValidateSet('Normal', 'Resume', 'Publish')][string]$Mode,
    [Parameter(Mandatory)][string]$ExpectedSha, [string]$ManifestSha256,
    [object]$InputRunId = 0, [object]$InputRunAttempt = 0, [object]$ArtifactId = 0, [string]$ArtifactDigest,
    [object]$LatestRunId = 0, [object]$LatestRunAttempt = 0, [object]$LatestJournalArtifactId = 0, [string]$LatestJournalDigest,
    [string]$Root = 'artifacts/publication-attempt')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/navlyn-publish-orchestration.ps1')
Assert-NavlynProtectedPublisher $ExpectedSha
$repo = Split-Path -Parent $PSScriptRoot
$rootPath = [IO.Path]::GetFullPath($(if ([IO.Path]::IsPathFullyQualified($Root)) { $Root } else { Join-Path $repo $Root }))
$owned = [IO.Path]::GetFullPath((Join-Path $repo 'artifacts')).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
if (!$rootPath.StartsWith($owned, [StringComparison]::Ordinal)) { throw 'Attempt output must be owned ignored artifacts.' }
Assert-NavlynPublicationNoReparse $rootPath
$currentId = Get-NavlynPublicationPositiveInteger $env:GITHUB_RUN_ID 'current run'
$currentAttempt = Get-NavlynPublicationPositiveInteger $env:GITHUB_RUN_ATTEMPT 'current attempt' ([int]::MaxValue)
$journalPath = Join-Path $rootPath 'journal/navlyn-publication-journal.json'
$statePath = Join-Path $rootPath 'prepared.json'
$read = { param($Route) Invoke-NavlynGitHubJson $Route (Join-Path $rootPath 'provenance') }
$download = { param($Id, $Digest, $Path) Save-NavlynGitHubArtifact $Id $Digest $Path }
if ($Mode -ceq 'Publish') {
    $state = Read-NavlynPublicationJson $statePath
    $journal = Read-NavlynPublicationJson $journalPath
    [void](Get-NavlynPublicationIdentityText $journal.identity)
    if ($journal.runId -ne $currentId -or $journal.runAttempt -ne $currentAttempt -or $journal.identity.sourceSha -cne $ExpectedSha -or
        $journal.phase -cne 'initialized') { throw "Publication requires this attempt's prepared journal." }
    # Reauthenticate at the publication boundary. Prepared local state never grants
    # permission to erase an earlier intent or replace the immutable input bytes.
    $identity = $journal.identity
    $inputRun = & $read "/repos/furbon/navlyn/actions/runs/$($identity.inputRunId)/attempts/$($identity.inputRunAttempt)"
    $sameAttempt = $identity.inputRunId -eq $currentId -and $identity.inputRunAttempt -eq $currentAttempt
    Assert-NavlynPublisherAttempt $inputRun $ExpectedSha $identity.inputRunId $identity.inputRunAttempt -Current:$sameAttempt
    $artifact = & $read "/repos/furbon/navlyn/actions/artifacts/$($identity.artifactId)"
    Assert-NavlynGitHubArtifact $artifact $inputRun $identity.artifactId $identity.artifactDigest "navlyn-publication-inputs-$($identity.inputRunId)-$($identity.inputRunAttempt)"
    $zip = Join-Path $rootPath 'publish-inputs.zip'
    & $download $identity.artifactId $identity.artifactDigest $zip
    $publishRoot = Join-Path $rootPath 'publish-inputs'
    Expand-NavlynPublicationArtifact $zip $publishRoot
    $inputs = Read-NavlynPublicationInputs $publishRoot $ExpectedSha $identity.manifestSha256 -RequireAssets
    Assert-NavlynReleaseAssets $publishRoot $inputs $repo
    $inventory = Get-NavlynSubmittingAttempts $ExpectedSha $currentId $currentAttempt $read
    if ($null -eq $journal.predecessor) {
        if (!$sameAttempt -or @($inventory).Count) { throw 'Original publication cannot reset a prior attempt.' }
        $priorIntent = @()
    } else {
        $verificationRoot = Join-Path $rootPath 'publish-chain'; [IO.Directory]::CreateDirectory($verificationRoot) | Out-Null
        $verified = Get-NavlynRetainedChain $identity $inventory $state.latestRunId $state.latestRunAttempt $state.latestArtifactId $state.latestDigest $verificationRoot $read $download
        if ($verified.prior.sha256 -cne $journal.predecessor.sha256 -or $verified.prior.journal.runId -ne $journal.predecessor.runId -or $verified.prior.journal.runAttempt -ne $journal.predecessor.runAttempt) { throw 'Prepared predecessor differs from the authenticated complete chain.' }
        $priorIntent = @($verified.intendedIds)
    }
    $trust = New-NavlynNuGetTrust (Join-Path $rootPath 'public-verification')
    $observe = { param($Package, $File) Get-NavlynNuGetObservation $Package $File $trust (Join-Path $rootPath 'public-verification') }
    $push = {
        param($Package, $File)
        if ([string]::IsNullOrWhiteSpace($env:NUGET_API_KEY)) { throw 'Trusted Publishing API key is required.' }
        $output = & dotnet nuget push $File --api-key $env:NUGET_API_KEY --source https://api.nuget.org/v3/index.json --timeout 60 2>&1
        $exitCode = $LASTEXITCODE
        $log = Join-Path $rootPath ("push-$($Package.id).txt")
        $output | Set-Content -LiteralPath $log -Encoding utf8
        @{ exitCode = $exitCode; diagnostic = [IO.Path]::GetFileName($log); completedUtc = [DateTime]::UtcNow.ToString('O') }
    }
    Invoke-NavlynExactPublication $inputs $publishRoot $journal $journalPath $priorIntent $observe $push
    exit 0
}
if (Test-Path -LiteralPath $rootPath) { throw 'Preparing an attempt requires a new output directory.' }
if ($Mode -ceq 'Resume') {
    $InputRunId = Get-NavlynPublicationPositiveInteger $InputRunId 'original run'
    $InputRunAttempt = Get-NavlynPublicationPositiveInteger $InputRunAttempt 'original attempt' ([int]::MaxValue)
    $LatestRunId = Get-NavlynPublicationPositiveInteger $LatestRunId 'latest run'
    $LatestRunAttempt = Get-NavlynPublicationPositiveInteger $LatestRunAttempt 'latest attempt' ([int]::MaxValue)
    $LatestJournalArtifactId = Get-NavlynPublicationPositiveInteger $LatestJournalArtifactId 'latest journal artifact'
}
$ArtifactId = Get-NavlynPublicationPositiveInteger $ArtifactId 'input artifact'
Assert-NavlynPublicationDigest $ArtifactDigest 'input artifact'
Assert-NavlynPublicationDigest $ManifestSha256 'input manifest'
[IO.Directory]::CreateDirectory($rootPath) | Out-Null
$inventory = Get-NavlynSubmittingAttempts $ExpectedSha $currentId $currentAttempt $read
if ($Mode -ceq 'Normal') {
    if (@($inventory).Count -ne 0 -or $LatestRunId -or $LatestJournalArtifactId) { throw 'Normal publication cannot reset an earlier possibly submitting attempt. Use explicit resume.' }
    $InputRunId = $currentId; $InputRunAttempt = $currentAttempt
    $inputRoot = Join-Path $repo 'artifacts/publication-inputs'
    $predecessor = $null; $intended = @()
} else {
    foreach ($number in @($InputRunId, $InputRunAttempt, $ArtifactId, $LatestRunId, $LatestRunAttempt, $LatestJournalArtifactId)) { [void](Get-NavlynPublicationPositiveInteger $number 'resume identity') }
    foreach ($digest in @($ArtifactDigest, $ManifestSha256, $LatestJournalDigest)) { Assert-NavlynPublicationDigest $digest 'resume digest' }
    $inputRun = & $read "/repos/furbon/navlyn/actions/runs/$InputRunId/attempts/$InputRunAttempt"
    Assert-NavlynPublisherAttempt $inputRun $ExpectedSha $InputRunId $InputRunAttempt
    $artifact = & $read "/repos/furbon/navlyn/actions/artifacts/$ArtifactId"
    Assert-NavlynGitHubArtifact $artifact $inputRun $ArtifactId $ArtifactDigest "navlyn-publication-inputs-$InputRunId-$InputRunAttempt"
    $zip = Join-Path $rootPath 'inputs.zip'
    & $download $ArtifactId $ArtifactDigest $zip
    $inputRoot = Join-Path $rootPath 'inputs'
    Expand-NavlynPublicationArtifact $zip $inputRoot
}
$inputs = Read-NavlynPublicationInputs $inputRoot $ExpectedSha $ManifestSha256 -RequireAssets
Assert-NavlynReleaseAssets $inputRoot $inputs $repo
$identity = @{ repository = 'furbon/navlyn'; workflow = '.github/workflows/publish-nuget.yml'; sourceSha = $ExpectedSha; version = $inputs.version; inputRunId = $InputRunId; inputRunAttempt = $InputRunAttempt; artifactId = $ArtifactId; artifactDigest = $ArtifactDigest; manifestSha256 = $ManifestSha256 }
if ($Mode -ceq 'Normal') {
    [void](Get-NavlynPublicationIdentityText $identity)
    $currentRun = & $read "/repos/furbon/navlyn/actions/runs/$currentId/attempts/$currentAttempt"
    Assert-NavlynPublisherAttempt $currentRun $ExpectedSha $currentId $currentAttempt -Current
    $artifact = & $read "/repos/furbon/navlyn/actions/artifacts/$ArtifactId"
    Assert-NavlynGitHubArtifact $artifact $currentRun $ArtifactId $ArtifactDigest "navlyn-publication-inputs-$currentId-$currentAttempt"
} else {
    $chainRoot = Join-Path $rootPath 'chain'; [IO.Directory]::CreateDirectory($chainRoot) | Out-Null
    $chain = Get-NavlynRetainedChain $identity $inventory $LatestRunId $LatestRunAttempt $LatestJournalArtifactId $LatestJournalDigest $chainRoot $read $download
    $predecessor = @{ runId = $chain.prior.journal.runId; runAttempt = $chain.prior.journal.runAttempt; sha256 = $chain.prior.sha256 }
    $intended = @($chain.intendedIds)
}
$journal = New-NavlynPublicationJournal $identity $currentId $currentAttempt $predecessor
Write-NavlynPublicationJournal $journalPath $journal
[IO.File]::WriteAllText($statePath, (@{ inputRoot = $inputRoot; latestRunId = $LatestRunId; latestRunAttempt = $LatestRunAttempt; latestArtifactId = $LatestJournalArtifactId; latestDigest = $LatestJournalDigest } | ConvertTo-Json -Depth 30))
Write-Output 'Exact retained inputs and complete prior attempt chain verified; journal initialized before login.'

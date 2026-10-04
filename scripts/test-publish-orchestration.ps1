[CmdletBinding()]
param([string]$Output = '', [switch]$LiveBaselineTrust,
    [string]$BaselinePackageRoot, [string]$BaselinePublisherArchive)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/navlyn-publish-orchestration.ps1')
if ($LiveBaselineTrust) {
    if (![IO.Path]::IsPathFullyQualified($BaselinePackageRoot) -or ![IO.Path]::IsPathFullyQualified($BaselinePublisherArchive)) {
        throw 'Live baseline trust checks require absolute BaselinePackageRoot and BaselinePublisherArchive paths.'
    }
    foreach ($inputPath in @($BaselinePackageRoot, $BaselinePublisherArchive)) { Assert-NavlynPublicationNoReparse $inputPath }
    if (!(Test-Path -LiteralPath $BaselinePackageRoot -PathType Container) -or !(Test-Path -LiteralPath $BaselinePublisherArchive -PathType Leaf)) {
        throw 'Retained baseline package directory and publisher archive must exist.'
    }
}
$repo = Split-Path -Parent $PSScriptRoot
$owned = [IO.Path]::GetFullPath((Join-Path $repo 'artifacts')).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
$root = if ($Output) { [IO.Path]::GetFullPath($(if ([IO.Path]::IsPathFullyQualified($Output)) { $Output } else { Join-Path $repo $Output })) } else { Join-Path $owned ('publish-orchestration-tests/' + [guid]::NewGuid().ToString('N')) }
if (!$root.StartsWith($owned, [StringComparison]::Ordinal) -or (Test-Path -LiteralPath $root)) { throw 'Test requires a new owned artifacts directory.' }
Assert-NavlynPublicationNoReparse $root
[IO.Directory]::CreateDirectory($root) | Out-Null
$results = [Collections.Generic.List[object]]::new()
function Case([string]$Name, [scriptblock]$Body, [switch]$Reject) {
    $exception = $null; $started = [DateTime]::UtcNow
    try { & $Body | Out-Null } catch { $exception = $_.Exception.Message }
    $passed = if ($Reject) { $null -ne $exception } else { $null -eq $exception }
    $results.Add(@{ name = $Name; passed = $passed; expected = if ($Reject) { 'reject' } else { 'accept' }; error = $exception; elapsedMs = ([DateTime]::UtcNow - $started).TotalMilliseconds })
}
function Equal($Actual, $Expected) { if ($Actual -cne $Expected) { throw "Expected $Expected; received $Actual" } }
function Clone($Value) { $Value | ConvertTo-Json -Depth 100 | ConvertFrom-Json -AsHashtable -Depth 100 }
function Zip([string]$Path, [object[]]$Entries) {
    $archive = [IO.Compression.ZipFile]::Open($Path, [IO.Compression.ZipArchiveMode]::Create)
    try { foreach ($entry in $Entries) { $item = $archive.CreateEntry($entry.name); $stream = $item.Open(); try { $stream.Write([Text.UTF8Encoding]::new($false).GetBytes($entry.text)) } finally { $stream.Dispose() } } } finally { $archive.Dispose() }
}
$sha = 'a' * 40
$identity = @{ repository = 'furbon/navlyn'; workflow = '.github/workflows/publish-nuget.yml'; sourceSha = $sha; version = '0.8.2'; inputRunId = 100; inputRunAttempt = 1; artifactId = 500; artifactDigest = 'b' * 64; manifestSha256 = 'c' * 64 }
$inputRoot = Join-Path $root 'inputs'; [IO.Directory]::CreateDirectory($inputRoot) | Out-Null
$packages = @()
foreach ($id in @('navlyn', 'navlyn-mcp')) {
    $name = "$id.0.8.2.nupkg"; $path = Join-Path $inputRoot $name
    Zip $path @(@{ name = "$id.nuspec"; text = '<package><metadata><id>' + $id + '</id><version>0.8.2</version><repository type="git" url="https://github.com/furbon/navlyn" commit="' + $sha + '" /></metadata></package>' }, @{ name = 'payload.txt'; text = 'synthetic unsigned package' })
    $packages += @{ id = $id; version = '0.8.2'; path = $name; sha256 = Get-NavlynPublicationHash $path }
}
$manifest = @{ packages = $packages; sourceSha = $sha }
$exact = @{ status = 'present'; signatureTrusted = $true; canonicalMatch = $true; signedSha256 = 'd' * 64; signatureFingerprint = 'e' * 64 }
function Exercise([string]$Name, [string[]]$Prior, [hashtable]$Behavior, [switch]$Reject) {
    $exception = $null; $started = [DateTime]::UtcNow
    try {
        $directory = Join-Path $root ([guid]::NewGuid().ToString('N')); [IO.Directory]::CreateDirectory($directory) | Out-Null
        $path = Join-Path $directory 'navlyn-publication-journal.json'
        $journal = New-NavlynPublicationJournal $identity 101 1 @{ runId = 100; runAttempt = 1; sha256 = 'f' * 64 }
        $state = @{ pushes = [Collections.Generic.List[string]]::new(); counts = @{}; seconds = 0; pushedAt = @{}; progress = [Collections.Generic.List[object]]::new() }
        $publicExact = $exact
        $observe = {
            param($Package, $File)
            $id = $Package.id
            if (!$state.counts.ContainsKey($id)) { $state.counts[$id] = 0 }
            $state.counts[$id]++
            $spec = $Behavior[$id]
            if ($spec -ceq 'present') { return $publicExact }
            if ($spec -ceq 'unavailable') { return @{ status = 'unavailable' } }
            if ($spec -ceq 'mismatch') { return @{ status = 'present'; signatureTrusted = $true; canonicalMatch = $false; signedSha256 = 'd' * 64; signatureFingerprint = 'e' * 64 } }
            if ($spec -ceq 'signature') { return @{ status = 'present'; signatureTrusted = $false; canonicalMatch = $true; signedSha256 = 'd' * 64; signatureFingerprint = 'e' * 64 } }
            if ($spec -cin @('push', 'acceptedFailure') -and $state.pushes.Contains($id)) { return $publicExact }
            if ($spec -ceq 'delayed' -and $state.pushes.Contains($id) -and $state.seconds - $state.pushedAt[$id] -ge 45) { return $publicExact }
            return @{ status = 'absent' }
        }.GetNewClosure()
        $push = {
            param($Package, $File)
            $retained = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json -AsHashtable
            $packageState = @($retained.packages | Where-Object { $_.id -ceq $Package.id })[0]
            if ($packageState.state -cne 'intent') { throw 'Intent not written before push' }
            if (!$packageState.intentUtc) { throw 'Intent not durable before push callback.' }
            if ((Get-FileHash -LiteralPath $File -Algorithm SHA256).Hash.ToLowerInvariant() -cne $Package.sha256) { throw 'Push bytes changed' }
            $state.pushes.Add($Package.id)
            $state.pushedAt[$Package.id] = $state.seconds
            if ($Behavior[$Package.id] -ceq 'throw') { throw 'synthetic timeout after acceptance uncertainty' }
            @{ exitCode = if ($Behavior[$Package.id] -ceq 'acceptedFailure') { 1 } else { 0 } }
        }.GetNewClosure()
        $wait = { param($Seconds) $state.seconds += $Seconds }.GetNewClosure()
        $now = { [DateTime]::new(2026, 10, 3).AddSeconds($state.seconds) }.GetNewClosure()
        $pollSeconds = if ('delayed' -cin $Behavior.Values) { 60 } else { 0 }
        $progress = { param($Id, $Stage, $Remaining) $state.progress.Add(@{ id = $Id; stage = $Stage; remaining = $Remaining }) }.GetNewClosure()
        try { Invoke-NavlynExactPublication $manifest $inputRoot $journal $path $Prior $observe $push -PollSeconds $pollSeconds -Wait $wait -Now $now -Progress $progress }
        finally {
            $saved = Read-NavlynPublicationJson $path
            foreach ($id in $state.pushes) { $s = @($saved.packages | Where-Object { $_.id -ceq $id })[0]; if (!$s.intentUtc -or $s.state -ceq 'notAttempted') { throw 'Push intent was lost on failure.' } }
            foreach ($id in $Prior) { if ($Behavior[$id] -ceq 'absent' -and $state.pushes.Contains($id)) { throw 'Prior intent was repushed after absent indexing.' } }
            $expectedPushes = if ($Behavior.navlyn -ceq 'throw') { 1 }
                elseif ($Behavior.navlyn -ceq 'absent' -and 'navlyn' -cnotin $Prior) { 2 }
                elseif ($Behavior.'navlyn-mcp' -ceq 'throw') { 2 }
                elseif ($Reject) { 0 }
                else { @($Behavior.Values | Where-Object { $_ -cin @('push', 'acceptedFailure', 'delayed') }).Count }
            if ($state.pushes.Count -ne $expectedPushes) { throw "Expected $expectedPushes exact pushes; observed $($state.pushes.Count)." }
            if (@($state.pushes | Select-Object -Unique).Count -ne $state.pushes.Count) { throw 'An ID was repushed within the attempt.' }
            if ($Behavior.navlyn -ceq 'delayed' -and $Behavior.'navlyn-mcp' -ceq 'delayed' -and $state.seconds -gt 60) { throw 'Indexing waits exceeded the shared polling budget.' }
            if (!$Reject -and $saved.phase -cne 'complete') { throw 'Successful publication did not complete the retained journal.' }
            foreach ($id in $state.pushes) {
                if (@($state.progress | Where-Object { $_.id -ceq $id -and $_.stage -ceq 'intentRetained' }).Count -ne 1) { throw 'Progress lost retained push intent.' }
            }
            if ('delayed' -cin $Behavior.Values -and @($state.progress | Where-Object { $_.stage -ceq 'waitingForIndexing' -and $_.remaining -gt 0 }).Count -lt 2) { throw 'Progress lost bounded indexing wait.' }
            if (!$Reject -and @($state.progress | Where-Object { $_.stage -cin @('verified', 'alreadyPublic') }).Count -ne 2) { throw 'Progress lost verified package completion.' }
        }
    } catch { $exception = $_.Exception.Message }
    $passed = if ($Reject) { $null -ne $exception } else { $null -eq $exception }
    $results.Add(@{ name = $Name; passed = $passed; expected = if ($Reject) { 'reject' } else { 'accept' }; error = $exception; elapsedMs = ([DateTime]::UtcNow - $started).TotalMilliseconds })
}
Exercise 'zero present: both exact retained inputs and intent before every push' @() @{ navlyn = 'push'; 'navlyn-mcp' = 'push' }
Exercise 'both packages indexed after 45 seconds complete in one attempt without repush' @() @{ navlyn = 'delayed'; 'navlyn-mcp' = 'delayed' }
Exercise 'one present: exact skip then second retained push' @() @{ navlyn = 'present'; 'navlyn-mcp' = 'push' }
Exercise 'two present: both exact verified skips' @('navlyn', 'navlyn-mcp') @{ navlyn = 'present'; 'navlyn-mcp' = 'present' }
Exercise 'first push accepted despite failure exit: verify then continue' @() @{ navlyn = 'acceptedFailure'; 'navlyn-mcp' = 'push' }
Exercise 'first push uncertain timeout preserves intent' @() @{ navlyn = 'throw'; 'navlyn-mcp' = 'push' } -Reject
Exercise 'second push uncertain timeout preserves both intents' @() @{ navlyn = 'push'; 'navlyn-mcp' = 'throw' } -Reject
Exercise 'accepted but delayed public indexing never causes automatic repush' @() @{ navlyn = 'absent'; 'navlyn-mcp' = 'push' } -Reject
Exercise 'second/third resume absent prior intent cannot reset' @('navlyn') @{ navlyn = 'absent'; 'navlyn-mcp' = 'push' } -Reject
Exercise 'previous resume timeout second intent cannot reset' @('navlyn-mcp') @{ navlyn = 'present'; 'navlyn-mcp' = 'absent' } -Reject

Case 'Actions recovery summary uses original immutable inputs and latest retained journal' {
    $summary = Join-Path $root 'summary.md'; $path = Join-Path $root 'summary-journal.json'
    $journal = New-NavlynPublicationJournal $identity 102 2 @{ runId = 101; runAttempt = 1; sha256 = 'f' * 64 }
    $journal.phase = 'blocked'
    Write-NavlynPublicationJournal $path $journal
    $previous = $env:GITHUB_STEP_SUMMARY
    try {
        $env:GITHUB_STEP_SUMMARY = $summary
        & (Join-Path $PSScriptRoot 'write-publication-summary.ps1') -JournalPath $path -JournalArtifactId 600 -JournalArtifactDigest ('f' * 64)
        $text = Get-Content -Raw $summary
        foreach ($expected in @('mode=resume', "expected-main-sha=$sha", 'input-run-attempt=100:1', 'input-artifact-id=500', ('input-artifact-sha256=' + 'b' * 64), ('manifest-sha256=' + 'c' * 64), 'latest-run-attempt=102:2', 'latest-journal-artifact-id=600', ('latest-journal-artifact-sha256=' + 'f' * 64))) {
            if (!$text.Contains($expected)) { throw "Recovery summary lost $expected" }
        }
        $journal.phase = 'complete'; Write-NavlynPublicationJournal $path $journal
        $env:GITHUB_STEP_SUMMARY = Join-Path $root 'completed-summary.md'
        & (Join-Path $PSScriptRoot 'write-publication-summary.ps1') -JournalPath $path
        $text = Get-Content -Raw $env:GITHUB_STEP_SUMMARY
        if (!$text.Contains('No recovery is needed.') -or $text.Contains('mode=resume')) { throw 'Completed publication summary suggests another attempt.' }
    } finally { $env:GITHUB_STEP_SUMMARY = $previous }
}
Exercise 'feed unavailable without intent is fail closed' @() @{ navlyn = 'unavailable'; 'navlyn-mcp' = 'push' } -Reject
Exercise 'canonical mismatch is hard stop before push' @() @{ navlyn = 'mismatch'; 'navlyn-mcp' = 'push' } -Reject
Exercise 'signature mismatch is hard stop before push' @() @{ navlyn = 'signature'; 'navlyn-mcp' = 'push' } -Reject

$run = @{ id = 100; run_attempt = 1; repository = @{ full_name = 'furbon/navlyn'; id = 12 }; head_repository = @{ full_name = 'furbon/navlyn'; id = 12 }; event = 'workflow_dispatch'; head_branch = 'main'; head_sha = $sha; path = '.github/workflows/publish-nuget.yml'; status = 'completed'; conclusion = 'failure'; run_started_at = '2026-09-28T00:00:00Z'; updated_at = '2026-09-28T01:00:00Z' }
Case 'failed authentic publisher is eligible artifact provenance' { Assert-NavlynPublisherAttempt $run $sha 100 1 }
foreach ($field in @('event', 'head_branch', 'head_sha', 'path', 'status')) {
    Case "wrong publisher $field rejected" { $r = Clone $run; $r[$field] = 'wrong'; Assert-NavlynPublisherAttempt $r $sha 100 1 } -Reject
}
Case 'fork head repository rejected' { $r = Clone $run; $r.head_repository.id = 13; Assert-NavlynPublisherAttempt $r $sha 100 1 } -Reject
Case 'wrong run_attempt rejected independently of run ID' { Assert-NavlynPublisherAttempt $run $sha 100 2 } -Reject
$artifact = @{ id = 500; name = 'navlyn-publication-inputs-100-1'; expired = $false; digest = 'sha256:' + ('b' * 64); created_at = '2026-09-28T00:30:00Z'; workflow_run = @{ id = 100; repository_id = 12; head_repository_id = 12; head_branch = 'main'; head_sha = $sha } }
Case 'authentic immutable artifact metadata accepted' { Assert-NavlynGitHubArtifact $artifact $run 500 ('b' * 64) $artifact.name }
foreach ($field in @('id', 'name', 'digest', 'expired', 'created_at')) {
    Case "artifact $field mismatch rejected" { $a = Clone $artifact; $a[$field] = switch ($field) { 'id' { 501 }; 'expired' { $true }; 'created_at' { '2026-09-27T00:00:00Z' }; default { 'wrong' } }; Assert-NavlynGitHubArtifact $a $run 500 ('b' * 64) $artifact.name } -Reject
}
foreach ($name in @('../escape.json', '/root.json', 'nested/file.json', 'A.json', 'bad..json')) {
    Case "unsafe service ZIP entry rejected $name" { $path = Join-Path $root ([guid]::NewGuid().ToString('N') + '.zip'); Zip $path @(@{ name = $name; text = 'bad' }); Expand-NavlynPublicationArtifact $path ($path + '-extracted') } -Reject
}
Case 'duplicate service ZIP root names rejected' { $path = Join-Path $root 'duplicate.zip'; Zip $path @(@{ name = 'journal.json'; text = 'a' }, @{ name = 'journal.json'; text = 'b' }); Expand-NavlynPublicationArtifact $path ($path + '-extracted') } -Reject
Case 'run_attempt reruns enumerate independently and skipped proof excludes only skipped attempt' {
    $fixtureRun = $run
    $read = {
        param($Route)
        if ($Route.Contains('/workflows/')) { return @{ total_count = 2; workflow_runs = @(@{ id = 100; run_attempt = 2 }, @{ id = 101; run_attempt = 1 }) } }
        if ($Route.Contains('/jobs')) { if ($Route.Contains('/attempts/1/') -and $Route.Contains('/runs/100/')) { return @{ total_count = 1; jobs = @(@{ steps = @(@{ name = 'Publish exact retained inputs'; status = 'completed'; conclusion = 'skipped' }) }) } }; return @{ total_count = 1; jobs = @(@{ steps = @(@{ name = 'Publish exact retained inputs'; status = 'completed'; conclusion = 'failure' }) }) } }
        if ($Route -match '/runs/([0-9]+)/attempts/([0-9]+)$') { $r = $fixtureRun | ConvertTo-Json -Depth 30 | ConvertFrom-Json -AsHashtable; $r.id = [long]$Matches[1]; $r.run_attempt = [int]$Matches[2]; return $r }
        throw 'Unexpected synthetic route'
    }.GetNewClosure()
    $attempts = Get-NavlynSubmittingAttempts $sha 102 1 $read
    Equal $attempts.Count 2
    Equal $attempts[0].runAttempt 2
    Equal $attempts[1].runId 101
}
Case 'API filtered inventory at 1000 cap fails closed' { Get-NavlynGitHubPages '/repos/furbon/navlyn/actions/runs' 'workflow_runs' { @{ total_count = 1000; workflow_runs = @() } } 1000 } -Reject
Case 'protected publisher refuses ordinary local invocation' { Assert-NavlynProtectedPublisher $sha } -Reject

# Exercise actual downloaded ZIP/journal byte binding, not mutable journal objects.
$api = @{}; $downloads = @{}; $previous = $null; $journalArtifacts = @{}; $inventory = @()
foreach ($spec in @(@{ id = 100; attempt = 1; artifact = 700 }, @{ id = 101; attempt = 1; artifact = 701 }, @{ id = 101; attempt = 2; artifact = 702 })) {
    $journal = New-NavlynPublicationJournal $identity $spec.id $spec.attempt $previous
    if ($spec.id -eq 100) { $journal.packages[0].state = 'intent'; $journal.packages[0].intentUtc = '2026-09-28T00:10:00Z' }
    if ($spec.id -eq 101 -and $spec.attempt -eq 1) { $journal.packages[1].state = 'indeterminate'; $journal.packages[1].intentUtc = '2026-09-28T00:11:00Z' }
    $journalFile = Join-Path $root "$($spec.id)-$($spec.attempt).json"
    Write-NavlynPublicationJournal $journalFile $journal
    $previous = @{ runId = $spec.id; runAttempt = $spec.attempt; sha256 = Get-NavlynPublicationHash $journalFile }
    $archive = Join-Path $root "$($spec.id)-$($spec.attempt).zip"
    Zip $archive @(@{ name = 'navlyn-publication-journal.json'; text = [IO.File]::ReadAllText($journalFile) })
    $digest = Get-NavlynPublicationHash $archive
    $r = Clone $run; $r.id = $spec.id; $r.run_attempt = $spec.attempt
    $api["/repos/furbon/navlyn/actions/runs/$($spec.id)/attempts/$($spec.attempt)"] = $r
    $a = Clone $artifact; $a.id = $spec.artifact; $a.name = "navlyn-publication-journal-$($spec.id)-$($spec.attempt)"; $a.digest = "sha256:$digest"; $a.workflow_run.id = $spec.id
    if (!$journalArtifacts.ContainsKey([string]$spec.id)) { $journalArtifacts[[string]$spec.id] = @() }
    $journalArtifacts[[string]$spec.id] += $a
    $downloads[[string]$spec.artifact] = @{ path = $archive; digest = $digest }
    $inventory += @{ runId = $spec.id; runAttempt = $spec.attempt }
}
foreach ($key in $journalArtifacts.Keys) { $api["/repos/furbon/navlyn/actions/runs/$key/artifacts?per_page=100&page=1"] = @{ total_count = $journalArtifacts[$key].Count; artifacts = $journalArtifacts[$key] } }
function ChainProbe([object[]]$Attempts) {
    $localApi = $api; $localDownloads = $downloads
    $read = { param($Route) if (!$localApi.ContainsKey($Route)) { throw "Unexpected fixture route $Route" }; $localApi[$Route] }.GetNewClosure()
    $download = { param($Id, $Digest, $Path) $source = $localDownloads[[string]$Id]; if ($source.digest -cne $Digest) { throw 'Fake download expected digest differs' }; Copy-Item -LiteralPath $source.path -Destination $Path; if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $Digest) { throw 'Fake downloaded bytes differ' } }.GetNewClosure()
    $directory = Join-Path $root ('chain-' + [guid]::NewGuid().ToString('N')); [IO.Directory]::CreateDirectory($directory) | Out-Null
    Get-NavlynRetainedChain $identity $Attempts 101 2 702 $downloads['702'].digest $directory $read $download
}
Case 'authenticated three-attempt ZIP chain preserves intents from both earlier attempts' { $verified = ChainProbe $inventory; Equal $verified.attempts 3; foreach ($id in @('navlyn', 'navlyn-mcp')) { if ($id -cnotin $verified.intendedIds) { throw 'Authenticated earlier intent lost' } } }
Case 'omitted possibly submitting attempt rejects authenticated ZIP chain' { ChainProbe @($inventory[0], $inventory[2]) } -Reject
Case 'expired journal cannot be resumed even with exact original bytes' { $a = $journalArtifacts['101'][1]; $a.expired = $true; try { ChainProbe $inventory } finally { $a.expired = $false } } -Reject
Case 'missing predecessor service artifact fails closed' { $a = $api['/repos/furbon/navlyn/actions/runs/100/artifacts?per_page=100&page=1']; $prior = $a.artifacts; $a.artifacts = @(); try { ChainProbe $inventory } finally { $a.artifacts = $prior } } -Reject

$assetRoot = Join-Path $root 'assets'; [IO.Directory]::CreateDirectory($assetRoot) | Out-Null
& (Join-Path $PSScriptRoot 'build-setup-bundle.ps1') -Output (Join-Path $root 'setup-source') -SourceCommit $sha | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'setup-source.zip') -Destination (Join-Path $assetRoot 'navlyn-setup.zip')
$assetManifest = @{ sourceSha = $sha; version = '0.8.2'; assets = @(@{ kind = 'setup'; path = 'navlyn-setup.zip' }) }
Case 'setup bundle integrity binds every payload file to exact checkout and source manifest' { Assert-NavlynReleaseAssets $assetRoot $assetManifest $repo }
Case 'setup source identity cannot be substituted by an outer manifest claim' { $changed = Clone $assetManifest; $changed.sourceSha = 'b' * 40; Assert-NavlynReleaseAssets $assetRoot $changed $repo } -Reject

if ($LiveBaselineTrust) {
    Case 'genuine public baseline packages verified with fresh isolated current NuGet trust' {
        $trust = New-NavlynNuGetTrust (Join-Path $root 'live-trust')
        foreach ($id in @('navlyn', 'navlyn-mcp')) {
            $path = Join-Path $BaselinePackageRoot "$id.0.8.1.nupkg"
            Test-NavlynNuGetSignature $path $trust (Join-Path $root "live-trust/$id.verify.txt")
        }
    }
    Case 'genuine fresh public baseline observation requires signature and every original canonical entry' {
        $directory = Join-Path $root 'live-public'; [IO.Directory]::CreateDirectory($directory) | Out-Null
        $trust = New-NavlynNuGetTrust (Join-Path $directory 'trust')
        $retained = $BaselinePublisherArchive
        Equal (Get-NavlynPublicationHash $retained) '2b95b3b254507216cf6442d8c1fbf994b333a4931997ee1b20c5aecd782fbebb'
        $zip = [IO.Compression.ZipFile]::OpenRead($retained)
        try {
            foreach ($id in @('navlyn', 'navlyn-mcp')) {
                $name = "$id.0.8.1.nupkg"; $file = Join-Path $directory $name
                [IO.Compression.ZipFileExtensions]::ExtractToFile($zip.GetEntry($name), $file, $false)
                $observation = Get-NavlynNuGetObservation @{ id = $id; version = '0.8.1' } $file $trust $directory
                Equal (Get-NavlynPublicationDecision $id $true $observation) 'skipExactPublic'
            }
        } finally { $zip.Dispose() }
    }
    Case 'genuine repository signed attribute rejects a different service authority' {
        $path = Join-Path $BaselinePackageRoot 'navlyn.0.8.1.nupkg'
        Get-NavlynRepositorySignatureIdentity $path 'https://example.invalid/v3/index.json'
    } -Reject
}
$passed = @($results | Where-Object { $_.passed }).Count
$report = @{ schema = 'navlyn.publication-orchestration-tests.v1'; utc = [DateTime]::UtcNow.ToString('O'); synthetic = $true; liveBaselineTrustRequested = [bool]$LiveBaselineTrust; passed = $passed; total = $results.Count; cases = $results }
$report | ConvertTo-Json -Depth 40 | Set-Content (Join-Path $root 'report.json') -Encoding utf8
foreach ($failure in @($results | Where-Object { !$_.passed })) { [Console]::Error.WriteLine("FAILED: $($failure.name): $($failure.error)") }
Write-Output "Publication orchestration tests: $passed/$($results.Count). Report: $root/report.json"
if ($passed -ne $results.Count) { exit 1 }

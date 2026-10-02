[CmdletBinding()]
param([string]$Output = '')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/navlyn-publish-recovery.ps1')
$repoRoot = Split-Path -Parent $PSScriptRoot
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts')).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
$testRoot = if ($Output) { [IO.Path]::GetFullPath($Output) } else { Join-Path $artifactsRoot ('publish-recovery-tests/' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N')) }
$comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
if (!$testRoot.StartsWith($artifactsRoot, $comparison) -or (Test-Path -LiteralPath $testRoot)) { throw 'Tests require a new owned directory under ignored artifacts.' }
Assert-NavlynPublicationNoReparse $testRoot
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
[IO.File]::WriteAllText((Join-Path $testRoot 'ownership.json'), '{"owner":"test-publish-recovery","evidence":"synthetic fixtures; no NuGet pushes, no real signatures or model inference"}')
$script:results = [Collections.Generic.List[object]]::new()
function Test-RecoveryCase([string]$Name, [scriptblock]$Body, [switch]$Reject) {
    $started = [DateTime]::UtcNow
    $exception = $null
    try { & $Body | Out-Null } catch { $exception = $_.Exception.Message }
    $passed = if ($Reject) { $null -ne $exception } else { $null -eq $exception }
    $script:results.Add([ordered]@{ name = $Name; expected = if ($Reject) { 'reject' } else { 'accept' }; passed = $passed; error = $exception; elapsedMs = ([DateTime]::UtcNow - $started).TotalMilliseconds })
}
function Assert-RecoveryEqual($Actual, $Expected) { if ($Actual -cne $Expected) { throw "Expected '$Expected', observed '$Actual'." } }
function Copy-RecoveryObject($Value) { $Value | ConvertTo-Json -Depth 100 | ConvertFrom-Json -AsHashtable -Depth 100 }
function New-RecoveryChainFixture([object[]]$Chain, [int]$Index, [scriptblock]$Mutation, [switch]$KeepDigest) {
    $copy = Copy-RecoveryObject $Chain
    $journals = @($Chain | ForEach-Object { Read-NavlynPublicationJson $_.path })
    & $Mutation $journals[$Index]
    for ($i = $Index; $i -lt $copy.Count; $i++) {
        $copy[$i].path = Join-Path $testRoot ('mutation-' + [guid]::NewGuid().ToString('N') + '.json')
        if (!$KeepDigest -and $i -gt $Index) { $journals[$i].predecessor.sha256 = $copy[$i - 1].sha256 }
        Write-NavlynPublicationJournal $copy[$i].path $journals[$i]
        if (!$KeepDigest) { $copy[$i].sha256 = Get-NavlynPublicationHash $copy[$i].path }
    }
    return ,$copy
}
function New-RecoveryZip([string]$Path, [object[]]$Entries) {
    $zip = [IO.Compression.ZipFile]::Open($Path, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($spec in $Entries) {
            $entry = $zip.CreateEntry($spec.name)
            $stream = $entry.Open()
            try { $bytes = [Text.UTF8Encoding]::new($false).GetBytes($spec.text); $stream.Write($bytes) }
            finally { $stream.Dispose() }
        }
    } finally { $zip.Dispose() }
}
$sourceSha = 'a' * 40
$version = '0.8.2'
$inputRoot = Join-Path $testRoot 'inputs'
[IO.Directory]::CreateDirectory($inputRoot) | Out-Null
$packages = @()
foreach ($id in @('navlyn', 'navlyn-mcp')) {
    $name = "$id.$version.nupkg"
    $path = Join-Path $inputRoot $name
    $nuspec = '<package><metadata><id>' + $id + '</id><version>' + $version + '</version><repository type="git" url="https://github.com/furbon/navlyn" commit="' + $sourceSha + '" /></metadata></package>'
    New-RecoveryZip $path @(@{ name = "$id.nuspec"; text = $nuspec }, @{ name = 'tools/net10.0/any/payload.txt'; text = 'unsigned test content' })
    $packages += [ordered]@{ id = $id; version = $version; path = $name; sha256 = Get-NavlynPublicationHash $path }
}
$manifest = [ordered]@{ schema = 'navlyn.publication-inputs.v1'; repository = 'furbon/navlyn'; workflow = '.github/workflows/publish-nuget.yml'; sourceSha = $sourceSha; version = $version; packages = $packages; assets = @() }
$manifestPath = Join-Path $inputRoot 'navlyn-publication-inputs.json'
function Write-RecoveryManifest($Value) { [IO.File]::WriteAllText($manifestPath, ($Value | ConvertTo-Json -Depth 20)); Get-NavlynPublicationHash $manifestPath }
$manifestHash = Write-RecoveryManifest $manifest
Test-RecoveryCase 'two exact input packages and source-bound nuspecs' { Read-NavlynPublicationInputs $inputRoot $sourceSha $manifestHash }
Test-RecoveryCase 'changed source SHA' { Read-NavlynPublicationInputs $inputRoot ('b' * 40) $manifestHash } -Reject
Test-RecoveryCase 'changed manifest digest' { Read-NavlynPublicationInputs $inputRoot $sourceSha ('b' * 64) } -Reject
Test-RecoveryCase 'wrong repository provenance' { $m = Copy-RecoveryObject $manifest; $m.repository = 'other/repo'; $h = Write-RecoveryManifest $m; Read-NavlynPublicationInputs $inputRoot $sourceSha $h } -Reject
Test-RecoveryCase 'wrong workflow provenance' { $m = Copy-RecoveryObject $manifest; $m.workflow = '.github/workflows/ci.yml'; $h = Write-RecoveryManifest $m; Read-NavlynPublicationInputs $inputRoot $sourceSha $h } -Reject
Test-RecoveryCase 'rooted package path' { $m = Copy-RecoveryObject $manifest; $m.packages[0].path = '/outside/navlyn.0.8.2.nupkg'; $h = Write-RecoveryManifest $m; Read-NavlynPublicationInputs $inputRoot $sourceSha $h } -Reject
Test-RecoveryCase 'traversal package path' { $m = Copy-RecoveryObject $manifest; $m.packages[0].path = '../navlyn.0.8.2.nupkg'; $h = Write-RecoveryManifest $m; Read-NavlynPublicationInputs $inputRoot $sourceSha $h } -Reject
Test-RecoveryCase 'duplicate package ID' { $m = Copy-RecoveryObject $manifest; $m.packages[1].id = 'navlyn'; $h = Write-RecoveryManifest $m; Read-NavlynPublicationInputs $inputRoot $sourceSha $h } -Reject
Test-RecoveryCase 'changed package digest' { $m = Copy-RecoveryObject $manifest; $m.packages[0].sha256 = 'f' * 64; $h = Write-RecoveryManifest $m; Read-NavlynPublicationInputs $inputRoot $sourceSha $h } -Reject
Test-RecoveryCase 'missing required setup asset' { $h = Write-RecoveryManifest $manifest; Read-NavlynPublicationInputs $inputRoot $sourceSha $h -RequireAssets } -Reject
Test-RecoveryCase 'duplicate JSON properties' { $p = Join-Path $testRoot 'duplicate.json'; [IO.File]::WriteAllText($p, '{"key":1,"key":2}'); Read-NavlynPublicationJson $p } -Reject
$manifestHash = Write-RecoveryManifest $manifest

$publicObservation = @{ status = 'present'; signatureTrusted = $true; canonicalMatch = $true; signedSha256 = 'c' * 64; signatureFingerprint = 'd' * 64 }
Test-RecoveryCase 'neither package present: exact retained push allowed' { foreach ($id in @('navlyn', 'navlyn-mcp')) { Assert-RecoveryEqual (Get-NavlynPublicationDecision $id $false @{ status = 'absent' }) 'pushExactRetained' } }
Test-RecoveryCase 'both packages already exact public: skip' { foreach ($id in @('navlyn', 'navlyn-mcp')) { Assert-RecoveryEqual (Get-NavlynPublicationDecision $id $true $publicObservation) 'skipExactPublic' } }
Test-RecoveryCase 'first public second never attempted: skip first and push retained second' { Assert-RecoveryEqual (Get-NavlynPublicationDecision 'navlyn' $true $publicObservation) 'skipExactPublic'; Assert-RecoveryEqual (Get-NavlynPublicationDecision 'navlyn-mcp' $false @{ status = 'absent' }) 'pushExactRetained' }
Test-RecoveryCase 'accepted but timed out and absent indexing: no repush' { Assert-RecoveryEqual (Get-NavlynPublicationDecision 'navlyn' $true @{ status = 'absent' }) 'indeterminate' }
Test-RecoveryCase 'second package failed after intent: no absent repush' { Assert-RecoveryEqual (Get-NavlynPublicationDecision 'navlyn-mcp' $true @{ status = 'absent' }) 'indeterminate' }
Test-RecoveryCase 'feed unavailable without prior intent: no push' { Assert-RecoveryEqual (Get-NavlynPublicationDecision 'navlyn' $false @{ status = 'unavailable' }) 'indeterminate' }
Test-RecoveryCase 'untrusted public signature: hard stop' { $o = Copy-RecoveryObject $publicObservation; $o.signatureTrusted = $false; Get-NavlynPublicationDecision 'navlyn' $true $o } -Reject
Test-RecoveryCase 'public contents mismatch: hard stop' { $o = Copy-RecoveryObject $publicObservation; $o.canonicalMatch = $false; Get-NavlynPublicationDecision 'navlyn' $true $o } -Reject
foreach ($nonBoolean in @('true', 1)) {
    Test-RecoveryCase "untyped signature flag rejected: $($nonBoolean.GetType().Name)" { $o = Copy-RecoveryObject $publicObservation; $o.signatureTrusted = $nonBoolean; Get-NavlynPublicationDecision 'navlyn' $true $o } -Reject
    Test-RecoveryCase "untyped canonical flag rejected: $($nonBoolean.GetType().Name)" { $o = Copy-RecoveryObject $publicObservation; $o.canonicalMatch = $nonBoolean; Get-NavlynPublicationDecision 'navlyn' $true $o } -Reject
}

$identity = [ordered]@{ repository = 'furbon/navlyn'; workflow = '.github/workflows/publish-nuget.yml'; sourceSha = $sourceSha; version = $version; inputRunId = 100; inputRunAttempt = 1; artifactId = 500; artifactDigest = 'e' * 64; manifestSha256 = $manifestHash }
$original = New-NavlynPublicationJournal $identity 100 1 $null
$originalPath = Join-Path $testRoot 'original-journal.json'
Test-RecoveryCase 'intent atomically retained before any push' { Set-NavlynPublicationIntent $original 'navlyn' $originalPath; $j = Read-NavlynPublicationJson $originalPath; Assert-RecoveryEqual $j.packages[0].state 'intent'; if (!$j.packages[0].intentUtc) { throw 'Intent timestamp missing' } }
$originalHash = Get-NavlynPublicationHash $originalPath
$second = New-NavlynPublicationJournal $identity 101 1 @{ runId = 100; runAttempt = 1; sha256 = $originalHash }
$secondPath = Join-Path $testRoot 'second-journal.json'
Write-NavlynPublicationJournal $secondPath $second
$secondHash = Get-NavlynPublicationHash $secondPath
$third = New-NavlynPublicationJournal $identity 101 2 @{ runId = 101; runAttempt = 1; sha256 = $secondHash }
$thirdPath = Join-Path $testRoot 'third-journal.json'
Write-NavlynPublicationJournal $thirdPath $third
$chain = @(@{ path = $originalPath; sha256 = $originalHash }, @{ path = $secondPath; sha256 = $secondHash }, @{ path = $thirdPath; sha256 = Get-NavlynPublicationHash $thirdPath })
$attempts = @(@{ runId = 100; runAttempt = 1 }, @{ runId = 101; runAttempt = 1 }, @{ runId = 101; runAttempt = 2 })
Test-RecoveryCase 'second and third resume retain first intent including run_attempt rerun' { $r = Test-NavlynPublicationChain $identity $chain $attempts; Assert-RecoveryEqual $r.attempts 3; if ('navlyn' -cnotin $r.intendedIds) { throw 'Earlier intent was lost' }; Assert-RecoveryEqual (Get-NavlynPublicationDecision 'navlyn' ('navlyn' -cin $r.intendedIds) @{ status = 'absent' }) 'indeterminate' }
Test-RecoveryCase 'previous resume timeout retains second package intent' { $c = New-RecoveryChainFixture $chain 1 { param($j) $j.packages[1].state = 'indeterminate'; $j.packages[1].intentUtc = [DateTime]::UtcNow.ToString('O') }; $r = Test-NavlynPublicationChain $identity $c $attempts; if ('navlyn-mcp' -cnotin $r.intendedIds) { throw 'Resume intent was lost' } }
Test-RecoveryCase 'erased prior intent with retained digest is rejected before decision' { $c = New-RecoveryChainFixture $chain 0 { param($j) $j.packages[0].state = 'notAttempted'; $j.packages[0].intentUtc = $null } -KeepDigest; Test-NavlynPublicationChain $identity $c $attempts } -Reject
Test-RecoveryCase 'mutable parsed payload cannot be supplied beside claimed digest' { Test-NavlynPublicationChain $identity @(@{ journal = $original; sha256 = $originalHash }) @($attempts[0]) } -Reject
Test-RecoveryCase 'missing prior journal after possible submission' { Test-NavlynPublicationChain $identity @($chain[0], $chain[2]) $attempts } -Reject
Test-RecoveryCase 'expired journal removed from end of chain' { Test-NavlynPublicationChain $identity @($chain[0], $chain[1]) $attempts } -Reject
Test-RecoveryCase 'omitted workflow run attempt' { Test-NavlynPublicationChain $identity $chain @($attempts + @{ runId = 102; runAttempt = 1 }) } -Reject
Test-RecoveryCase 'forked predecessor digest' { $c = New-RecoveryChainFixture $chain 2 { param($j) $j.predecessor.sha256 = 'f' * 64 }; Test-NavlynPublicationChain $identity $c $attempts } -Reject
Test-RecoveryCase 'original run selected again cannot erase later attempts' { Test-NavlynPublicationChain $identity @($chain[0]) $attempts } -Reject
Test-RecoveryCase 'altered retained input artifact identity' { $c = New-RecoveryChainFixture $chain 1 { param($j) $j.identity.artifactId = 501 }; Test-NavlynPublicationChain $identity $c $attempts } -Reject
Test-RecoveryCase 'duplicate attempt inventory' { Test-NavlynPublicationChain $identity $chain @($attempts[0], $attempts[0], $attempts[2]) } -Reject
Test-RecoveryCase 'duplicate journal attempt' { Test-NavlynPublicationChain $identity @($chain[0], $chain[1], $chain[1]) $attempts } -Reject
Test-RecoveryCase 'lost intent timestamp' { $c = New-RecoveryChainFixture $chain 0 { param($j) $j.packages[0].intentUtc = $null }; Test-NavlynPublicationChain $identity $c $attempts } -Reject
Test-RecoveryCase 'second write cannot replace previous intent' { Set-NavlynPublicationIntent $original 'navlyn' $originalPath } -Reject
foreach ($invalidInteger in @(1.25, '01', '1.0', $true, 0, -1, '9223372036854775808')) {
    Test-RecoveryCase "noncanonical identity rejected: $invalidInteger" { $i = Copy-RecoveryObject $identity; $i.inputRunAttempt = $invalidInteger; Get-NavlynPublicationIdentityText $i } -Reject
    Test-RecoveryCase "noncanonical journal attempt rejected: $invalidInteger" { $c = New-RecoveryChainFixture $chain 1 { param($j) $j.runAttempt = $invalidInteger }; Test-NavlynPublicationChain $identity $c $attempts } -Reject
    Test-RecoveryCase "noncanonical inventory attempt rejected: $invalidInteger" { $a = Copy-RecoveryObject $attempts; $a[1].runAttempt = $invalidInteger; Test-NavlynPublicationChain $identity $chain $a } -Reject
}
Test-RecoveryCase 'canonical decimal strings normalize to the same identity' { $i = Copy-RecoveryObject $identity; $i.inputRunId = '100'; $i.inputRunAttempt = '1'; $i.artifactId = '500'; Assert-RecoveryEqual (Get-NavlynPublicationIdentityText $i) (Get-NavlynPublicationIdentityText $identity) }
Test-RecoveryCase 'fractional new journal attempt rejected before conversion' { New-NavlynPublicationJournal $identity 102 1.25 $null } -Reject

$unsigned = Join-Path $testRoot 'unsigned.nupkg'
$signed = Join-Path $testRoot 'signed-fixture.nupkg'
New-RecoveryZip $unsigned @(@{ name = 'data.txt'; text = 'identical content' }, @{ name = 'package/meta.xml'; text = '<meta />' })
New-RecoveryZip $signed @(@{ name = 'package/meta.xml'; text = '<meta />' }, @{ name = '.signature.p7s'; text = 'FAKE TEST BYTES - NOT A TRUSTED SIGNATURE' }, @{ name = 'data.txt'; text = 'identical content' })
Test-RecoveryCase 'canonical contents ignore archive order and permit only added signature entry' { $r = Compare-NavlynCanonicalPackage $unsigned $signed; Assert-RecoveryEqual $r.entriesMatched 2; if ($r.unsignedSha256 -ceq $r.signedSha256) { throw 'Raw hash unexpectedly equal' } }
Test-RecoveryCase 'canonical content modification' { $p = Join-Path $testRoot 'changed.nupkg'; New-RecoveryZip $p @(@{ name = 'data.txt'; text = 'changed content' }, @{ name = 'package/meta.xml'; text = '<meta />' }, @{ name = '.signature.p7s'; text = 'fake' }); Compare-NavlynCanonicalPackage $unsigned $p } -Reject
Test-RecoveryCase 'canonical extra payload' { $p = Join-Path $testRoot 'extra.nupkg'; New-RecoveryZip $p @(@{ name = 'data.txt'; text = 'identical content' }, @{ name = 'package/meta.xml'; text = '<meta />' }, @{ name = '.signature.p7s'; text = 'fake' }, @{ name = 'extra.txt'; text = 'added' }); Compare-NavlynCanonicalPackage $unsigned $p } -Reject
Test-RecoveryCase 'duplicate ZIP entries' { $p = Join-Path $testRoot 'duplicate.nupkg'; New-RecoveryZip $p @(@{ name = 'data.txt'; text = '1' }, @{ name = 'data.txt'; text = '2' }); Get-NavlynCanonicalPackage $p } -Reject
Test-RecoveryCase 'ZIP traversal' { $p = Join-Path $testRoot 'traversal.nupkg'; New-RecoveryZip $p @(@{ name = '../outside.txt'; text = '1' }); Get-NavlynCanonicalPackage $p } -Reject
Test-RecoveryCase 'case collision in ZIP across supported platforms' { $p = Join-Path $testRoot 'case-collision.nupkg'; New-RecoveryZip $p @(@{ name = 'data.txt'; text = '1' }, @{ name = 'Data.txt'; text = '2' }); Get-NavlynCanonicalPackage $p } -Reject
Test-RecoveryCase 'source identity in nuspec cannot differ from manifest' { Assert-NavlynPublicationPackageIdentity (Join-Path $inputRoot $packages[0].path) 'navlyn' $version ('b' * 40) } -Reject

$failed = @($script:results | Where-Object { !$_.passed })
$report = [ordered]@{ schema = 'navlyn.publish-recovery-test-report.v1'; evidence = 'synthetic deterministic tests; no live push or cryptographic trust claim'; utc = [DateTime]::UtcNow.ToString('O'); passed = $failed.Count -eq 0; cases = $script:results.ToArray() }
$report | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $testRoot 'report.json') -Encoding utf8
foreach ($failure in $failed) { [Console]::Error.WriteLine("FAILED: $($failure.name): $($failure.error)") }
Write-Host "Publication recovery tests: $($script:results.Count - $failed.Count)/$($script:results.Count). Report: $testRoot/report.json"
if ($failed.Count) { exit 1 }

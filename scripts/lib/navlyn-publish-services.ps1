Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'navlyn-publish-recovery.ps1')

function Invoke-NavlynGitHubJson {
    param([string]$Route, [string]$EvidenceRoot)
    if ($Route -cnotmatch '^/repos/furbon/navlyn/actions/[a-zA-Z0-9_./?=&%-]+$') { throw 'Unexpected GitHub API route.' }
    if ([string]::IsNullOrWhiteSpace($env:GH_TOKEN)) { throw 'Authenticated Actions read token is required.' }
    $response = Invoke-WebRequest -Uri ('https://api.github.com' + $Route) -Headers @{
        Authorization = "Bearer $env:GH_TOKEN"; Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2026-03-10'
    } -ConnectionTimeoutSeconds 15 -OperationTimeoutSeconds 30 -MaximumRedirection 0
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes([string]$response.Content)
    if ($EvidenceRoot) {
        [IO.Directory]::CreateDirectory($EvidenceRoot) | Out-Null
        [IO.File]::WriteAllBytes((Join-Path $EvidenceRoot ([guid]::NewGuid().ToString('N') + '.json')), $bytes)
    }
    ConvertFrom-NavlynPublicationJsonBytes $bytes
}

function Assert-NavlynPublisherAttempt {
    param([Collections.IDictionary]$Run, [string]$SourceSha, [long]$RunId, [int]$Attempt, [switch]$Current)
    if ($Run.id -ne $RunId -or $Run.run_attempt -ne $Attempt -or $Run.repository.full_name -cne 'furbon/navlyn' -or
        $Run.head_repository.full_name -cne 'furbon/navlyn' -or $Run.repository.id -ne $Run.head_repository.id -or
        $Run.event -cne 'workflow_dispatch' -or $Run.head_branch -cne 'main' -or $Run.head_sha -cne $SourceSha -or
        $Run.path -cnotin @('.github/workflows/publish-nuget.yml', '.github/workflows/publish-nuget.yml@main') -or
        (!$Current -and ($Run.status -cne 'completed' -or $Run.conclusion -cnotin @('success', 'failure', 'cancelled', 'timed_out', 'neutral', 'skipped', 'action_required', 'stale'))) -or
        ($Current -and $Run.status -cne 'in_progress')) {
        throw 'Retained input must originate from an ended authentic main publisher attempt.'
    }
}

function Get-NavlynGitHubPages {
    param([string]$Route, [string]$Property, [scriptblock]$Read, [int]$Maximum = 10000)
    $items = [Collections.Generic.List[object]]::new()
    for ($page = 1; $page -le 100; $page++) {
        $separator = if ($Route.Contains('?')) { '&' } else { '?' }
        $result = & $Read ($Route + $separator + "per_page=100&page=$page")
        if ($result.Contains('total_count') -and $result.total_count -ge $Maximum) { throw 'GitHub inventory exceeds complete enumeration bounds.' }
        $batch = @($result[$Property])
        foreach ($item in $batch) { $items.Add($item) }
        if ($batch.Count -lt 100) { return ,$items.ToArray() }
    }
    throw 'GitHub inventory pagination did not terminate.'
}

function Get-NavlynSubmittingAttempts {
    param([string]$SourceSha, [long]$CurrentRunId, [int]$CurrentAttempt, [scriptblock]$Read)
    if ($SourceSha -cnotmatch '^[a-f0-9]{40}$') { throw 'Exact source SHA required.' }
    $runs = Get-NavlynGitHubPages "/repos/furbon/navlyn/actions/workflows/publish-nuget.yml/runs?head_sha=$SourceSha" 'workflow_runs' $Read 1000
    $inventory = [Collections.Generic.List[object]]::new()
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($run in $runs) {
        $id = Get-NavlynPublicationPositiveInteger $run.id 'workflow run'
        $last = Get-NavlynPublicationPositiveInteger $run.run_attempt 'workflow attempt' ([int]::MaxValue)
        for ($attempt = 1; $attempt -le $last; $attempt++) {
            if ($id -eq $CurrentRunId -and $attempt -eq $CurrentAttempt) { continue }
            $record = & $Read "/repos/furbon/navlyn/actions/runs/$id/attempts/$attempt"
            Assert-NavlynPublisherAttempt $record $SourceSha $id $attempt
            if (!$seen.Add("${id}:$attempt")) { throw 'Duplicate workflow attempt inventory.' }
            $jobs = Get-NavlynGitHubPages "/repos/furbon/navlyn/actions/runs/$id/attempts/$attempt/jobs" 'jobs' $Read
            $steps = @($jobs | ForEach-Object { $_.steps } | Where-Object { $_.name -cin @('Publish', 'Publish exact retained inputs') })
            # Only an explicit ended skipped publication step proves a pre-publication failure.
            if ($steps.Count -eq 1 -and $steps[0].status -ceq 'completed' -and $steps[0].conclusion -ceq 'skipped') { continue }
            $inventory.Add(@{ runId = $id; runAttempt = $attempt })
        }
    }
    return ,$inventory.ToArray()
}

function Assert-NavlynGitHubArtifact {
    param([Collections.IDictionary]$Artifact, [Collections.IDictionary]$Run, [long]$ArtifactId, [string]$Digest, [string]$Name)
    Assert-NavlynPublicationDigest $Digest 'service artifact'
    if ($Artifact.id -ne $ArtifactId -or $Artifact.name -cne $Name -or $Artifact.expired -isnot [bool] -or $Artifact.expired -or
        $Artifact.digest -cne "sha256:$Digest" -or $Artifact.workflow_run.id -ne $Run.id -or
        $Artifact.workflow_run.repository_id -ne $Run.repository.id -or $Artifact.workflow_run.head_repository_id -ne $Run.repository.id -or
        $Artifact.workflow_run.head_branch -cne 'main' -or $Artifact.workflow_run.head_sha -cne $Run.head_sha -or
        [DateTimeOffset]::Parse($Artifact.created_at) -lt [DateTimeOffset]::Parse($Run.run_started_at) -or
        ($Run.status -ceq 'completed' -and [DateTimeOffset]::Parse($Artifact.created_at) -gt [DateTimeOffset]::Parse($Run.updated_at))) { throw 'Artifact provenance, attempt window or immutable service digest differs.' }
}

function Save-NavlynGitHubArtifact {
    param([long]$ArtifactId, [string]$Digest, [string]$Path)
    Assert-NavlynPublicationDigest $Digest 'downloaded artifact'
    Assert-NavlynPublicationNoReparse $Path
    if (Test-Path -LiteralPath $Path) { throw 'Artifact download requires a new owned file.' }
    $handler = [Net.Http.HttpClientHandler]::new(); $handler.AllowAutoRedirect = $false
    $client = [Net.Http.HttpClient]::new($handler); $client.Timeout = [TimeSpan]::FromSeconds(60)
    $deadline = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(60))
    try {
        $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get, "https://api.github.com/repos/furbon/navlyn/actions/artifacts/$ArtifactId/zip")
        $request.Headers.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $env:GH_TOKEN)
        $request.Headers.Add('Accept', 'application/vnd.github+json'); $request.Headers.Add('X-GitHub-Api-Version', '2026-03-10'); $request.Headers.Add('User-Agent', 'navlyn-publication')
        $redirect = $client.SendAsync($request, $deadline.Token).GetAwaiter().GetResult()
        try {
            if ([int]$redirect.StatusCode -ne 302 -or $redirect.Headers.Location.Scheme -cne 'https' -or $redirect.Headers.Location.UserInfo) { throw 'Unexpected GitHub artifact download redirect.' }
            $url = $redirect.Headers.Location
        } finally { $redirect.Dispose(); $request.Dispose() }
        # A separate request deliberately carries no GitHub token to the temporary storage URL.
        $response = $client.GetAsync($url, [Net.Http.HttpCompletionOption]::ResponseHeadersRead, $deadline.Token).GetAwaiter().GetResult()
        try {
            $response.EnsureSuccessStatusCode() | Out-Null
            $inputStream = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
            $outputStream = [IO.FileStream]::new($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
            try {
                $buffer = [byte[]]::new(65536); [long]$total = 0
                while (($count = $inputStream.ReadAsync($buffer, 0, $buffer.Length, $deadline.Token).GetAwaiter().GetResult()) -gt 0) {
                    $total += $count; if ($total -gt 2147483648) { throw 'Artifact exceeds download bounds.' }
                    $outputStream.Write($buffer, 0, $count)
                }
            } finally { $outputStream.Dispose(); $inputStream.Dispose() }
        } finally { $response.Dispose() }
    } finally { $deadline.Dispose(); $client.Dispose(); $handler.Dispose() }
    if ((Get-NavlynPublicationHash $Path) -cne $Digest) { throw 'Downloaded artifact does not match immutable service digest.' }
}

function Expand-NavlynPublicationArtifact {
    param([string]$Path, [string]$Root)
    Assert-NavlynPublicationNoReparse $Root
    if (Test-Path -LiteralPath $Root) { throw 'Extraction requires a new owned directory.' }
    $zip = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase); [long]$total = 0
        foreach ($entry in $zip.Entries) {
            # Publisher artifacts contain fixed root files, never directories or executable links.
            if ($entry.FullName -cnotmatch '^[a-z][a-z0-9.-]+$' -or $entry.FullName.Contains('..') -or
                !$names.Add($entry.FullName) -or (($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000) { throw 'Unsafe or duplicate retained artifact entry.' }
            $total += $entry.Length; if ($total -gt 2147483648 -or $names.Count -gt 32) { throw 'Retained artifact exceeds extraction bounds.' }
        }
        [IO.Directory]::CreateDirectory($Root) | Out-Null
        foreach ($entry in $zip.Entries) { [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $Root $entry.FullName), $false) }
    } finally { $zip.Dispose() }
}

function New-NavlynNuGetTrust {
    param([string]$Root)
    [IO.Directory]::CreateDirectory($Root) | Out-Null
    $index = Invoke-RestMethod 'https://api.nuget.org/v3/index.json' -ConnectionTimeoutSeconds 15 -OperationTimeoutSeconds 30 -Headers @{ 'Cache-Control' = 'no-cache' }
    $resources = @($index.resources | Where-Object { $_.'@type' -ceq 'RepositorySignatures/5.0.0' })
    if ($resources.Count -ne 1 -or ([uri]$resources[0].'@id').Host -cne 'api.nuget.org' -or ([uri]$resources[0].'@id').Scheme -cne 'https') { throw 'Unexpected NuGet signature service identity.' }
    $signatures = Invoke-RestMethod $resources[0].'@id' -ConnectionTimeoutSeconds 15 -OperationTimeoutSeconds 30 -Headers @{ 'Cache-Control' = 'no-cache' }
    $signatures | ConvertTo-Json -Depth 30 | Set-Content (Join-Path $Root 'repository-signatures.json') -Encoding utf8
    if ($signatures.allRepositorySigned -isnot [bool] -or !$signatures.allRepositorySigned) { throw 'NuGet does not assert repository signing.' }
    $now = [DateTimeOffset]::UtcNow
    $active = @($signatures.signingCertificates | Where-Object { [DateTimeOffset]::Parse($_.notBefore) -le $now -and [DateTimeOffset]::Parse($_.notAfter) -gt $now })
    if (!$active.Count) { throw 'No current NuGet repository signing certificate.' }
    $fingerprints = @()
    foreach ($certificate in $active) {
        $fingerprint = ([string]$certificate.fingerprints.'2.16.840.1.101.3.4.2.1').ToLowerInvariant()
        Assert-NavlynPublicationDigest $fingerprint 'NuGet repository certificate'
        $fingerprints += $fingerprint
    }
    $certificates = ($fingerprints | ForEach-Object { '<certificate fingerprint="' + $_ + '" hashAlgorithm="SHA256" allowUntrustedRoot="false" />' }) -join ''
    $config = Join-Path $Root 'nuget-signature-trust.config'
    [IO.File]::WriteAllText($config, '<configuration><packageSources><clear /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources><config><add key="signatureValidationMode" value="require" /></config><trustedSigners><clear /><repository name="nuget.org" serviceIndex="https://api.nuget.org/v3/index.json">' + $certificates + '</repository></trustedSigners></configuration>')
    @{ config = $config; fingerprints = $fingerprints }
}

function Test-NavlynNuGetSignature {
    param([string]$Path, [Collections.IDictionary]$Trust, [string]$Log)
    $arguments = @('nuget', 'verify', $Path, '--all', '--configfile', $Trust.config, '--verbosity', 'normal')
    foreach ($fingerprint in $Trust.fingerprints) { $arguments += @('--certificate-fingerprint', $fingerprint) }
    $output = & dotnet @arguments 2>&1
    $exitCode = $LASTEXITCODE
    $output | Set-Content -LiteralPath $Log -Encoding utf8
    if ($exitCode -ne 0) { throw "NuGet signature verification failed (exit $exitCode); retained diagnostic: $Log" }
    # `verify` does not enforce trustedSigners serviceIndex selection. Bind the
    # signed repository attribute explicitly after its cryptographic/trust check.
    $identity = Get-NavlynRepositorySignatureIdentity $Path
    if ($identity.fingerprint -cnotin $Trust.fingerprints) { throw 'Primary repository certificate is not a current advertised NuGet signer.' }
    $identity.fingerprint
}

function Get-NavlynRepositorySignatureIdentity {
    param([string]$Path, [string]$ServiceIndex = 'https://api.nuget.org/v3/index.json')
    $zip = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entries = @($zip.Entries | Where-Object { $_.FullName -ceq '.signature.p7s' })
        if ($entries.Count -ne 1 -or $entries[0].Length -gt 1048576) { throw 'Public repository signature missing, repeated or oversized.' }
        $stream = $entries[0].Open(); $memory = [IO.MemoryStream]::new()
        try { $stream.CopyTo($memory); $bytes = $memory.ToArray() } finally { $memory.Dispose(); $stream.Dispose() }
    } finally { $zip.Dispose() }
    $cms = [Security.Cryptography.Pkcs.SignedCms]::new()
    $cms.Decode($bytes)
    if ($cms.SignerInfos.Count -ne 1) { throw 'Public package must have exactly one primary repository signer.' }
    $signer = $cms.SignerInfos[0]
    $attributes = @($signer.SignedAttributes | Where-Object { $_.Oid.Value -ceq '1.3.6.1.4.1.311.84.2.1.1.1' })
    if ($attributes.Count -ne 1 -or $attributes[0].Values.Count -ne 1) { throw 'Primary signature does not have a unique signed NuGet repository service index.' }
    $reader = [Formats.Asn1.AsnReader]::new([ReadOnlyMemory[byte]]::new($attributes[0].Values[0].RawData), [Formats.Asn1.AsnEncodingRules]::DER)
    $url = $reader.ReadCharacterString([Formats.Asn1.UniversalTagNumber]::IA5String)
    $reader.ThrowIfNotEmpty()
    if ($url -cne $ServiceIndex) { throw 'Signed repository service index differs from NuGet authority.' }
    if (!$signer.Certificate) { throw 'Primary repository signing certificate missing.' }
    $cms.CheckSignature($true)
    @{ serviceIndex = $url; fingerprint = $signer.Certificate.GetCertHashString([Security.Cryptography.HashAlgorithmName]::SHA256).ToLowerInvariant() }
}

function Get-NavlynNuGetObservation {
    param([Collections.IDictionary]$Package, [string]$Unsigned, [Collections.IDictionary]$Trust, [string]$Root)
    $id = $Package.id; $version = $Package.version
    if ($id -cnotin @('navlyn', 'navlyn-mcp') -or $version -cnotmatch '^\d+\.\d+\.\d+$') { throw 'Unexpected NuGet package identity.' }
    $url = "https://api.nuget.org/v3-flatcontainer/$id/$version/$id.$version.nupkg?navlyn=" + [guid]::NewGuid().ToString('N')
    $file = Join-Path $Root ("public-$id-" + [guid]::NewGuid().ToString('N') + '.nupkg')
    try { Invoke-WebRequest -Uri $url -OutFile $file -ConnectionTimeoutSeconds 15 -OperationTimeoutSeconds 30 -Headers @{ 'Cache-Control' = 'no-cache'; Pragma = 'no-cache' } | Out-Null }
    catch {
        if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 404) { return @{ status = 'absent' } }
        return @{ status = 'unavailable' }
    }
    $fingerprint = Test-NavlynNuGetSignature $file $Trust ($file + '.verify.txt')
    $canonical = Compare-NavlynCanonicalPackage $Unsigned $file
    @{ status = 'present'; signatureTrusted = $true; canonicalMatch = $true; signedSha256 = $canonical.signedSha256; signatureFingerprint = $fingerprint }
}

function Assert-NavlynReleaseAssets {
    param([string]$Root, [Collections.IDictionary]$Manifest, [string]$RepositoryRoot)
    $assets = @($Manifest.assets)
    if ($assets.Count -ne 2 -or @($assets | Where-Object { $_.kind -ceq 'setup' -and $_.path -ceq 'navlyn-setup.zip' }).Count -ne 1 -or
        @($assets | Where-Object { $_.kind -ceq 'evaluation' -and $_.path -ceq 'navlyn-curated-evaluation.json' }).Count -ne 1) { throw 'Fixed setup and curated evaluation asset identities are required.' }
    $evaluation = Read-NavlynPublicationJson (Get-NavlynPublicationInputFile $Root 'navlyn-curated-evaluation.json')
    if ($evaluation.schema -cne 'navlyn.curated-evaluation.v1' -or $evaluation.sourceSha -cne $Manifest.sourceSha -or
        $evaluation.version -cne $Manifest.version -or $evaluation.passed -isnot [bool] -or !$evaluation.passed) { throw 'Curated evaluation asset has no exact passed source/version binding.' }
    $zip = [IO.Compression.ZipFile]::OpenRead((Get-NavlynPublicationInputFile $Root 'navlyn-setup.zip'))
    try {
        $allowed = @('setup-navlyn.ps1', 'lib/navlyn-jsonc.ps1', 'README.md', 'integrity.json', 'lib/')
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($entry in $zip.Entries) {
            if ($entry.FullName -cnotin $allowed -or !$seen.Add($entry.FullName) -or $entry.Length -gt 33554432 -or
                (($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000) { throw 'Setup bundle contains unsafe, duplicate or unexpected source files.' }
        }
        $integrity = @($zip.Entries | Where-Object { $_.FullName -ceq 'integrity.json' })
        if ($integrity.Count -ne 1 -or $integrity[0].Length -gt 1048576) { throw 'Setup integrity manifest missing or oversized.' }
        $stream = $integrity[0].Open(); $memory = [IO.MemoryStream]::new()
        try { $stream.CopyTo($memory); $bundle = ConvertFrom-NavlynPublicationJsonBytes $memory.ToArray() } finally { $memory.Dispose(); $stream.Dispose() }
        Assert-NavlynPublicationKeys $bundle @('schema', 'sourceCommit', 'files')
        if ($bundle.schema -cne 'navlyn.setup-bundle.v1' -or $bundle.sourceCommit -cne $Manifest.sourceSha -or @($bundle.files).Count -ne 3) { throw 'Setup bundle source binding differs.' }
        $files = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($file in $bundle.files) {
            Assert-NavlynPublicationKeys $file @('path', 'sha256')
            if ($file.path -cnotin @('setup-navlyn.ps1', 'lib/navlyn-jsonc.ps1', 'README.md') -or !$files.Add($file.path)) { throw 'Setup integrity contains unexpected or duplicate paths.' }
            Assert-NavlynPublicationDigest $file.sha256 'setup source'
            $entry = @($zip.Entries | Where-Object { $_.FullName -ceq $file.path })
            if ($entry.Count -ne 1) { throw 'Setup source file missing.' }
            $stream = $entry[0].Open()
            try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() } finally { $stream.Dispose() }
            $relative = if ($file.path -ceq 'README.md') { 'scripts/setup-bundle/README.md' } else { 'scripts/' + $file.path }
            $source = Join-Path $RepositoryRoot $relative
            Assert-NavlynPublicationNoReparse $source
            if ($hash -cne $file.sha256 -or (Get-NavlynPublicationHash $source) -cne $hash) { throw 'Setup bundle payload differs from exact checked-out source.' }
        }
    } finally { $zip.Dispose() }
}

Set-StrictMode -Version Latest

function Get-NavlynPublicationHash {
    param([Parameter(Mandatory)][string]$Path)
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Read-NavlynPublicationJson {
    param([Parameter(Mandatory)][string]$Path)
    ConvertFrom-NavlynPublicationJsonBytes ([IO.File]::ReadAllBytes($Path))
}

function ConvertFrom-NavlynPublicationJsonBytes {
    param([Parameter(Mandatory)][byte[]]$Bytes)
    $text = [Text.UTF8Encoding]::new($false, $true).GetString($Bytes).TrimStart([char]0xfeff)
    $document = [Text.Json.JsonDocument]::Parse($text)
    try {
        function Test-UniquePublicationKeys([Text.Json.JsonElement]$Element) {
            if ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Object) {
                $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                foreach ($property in $Element.EnumerateObject()) {
                    if (!$names.Add($property.Name)) { throw 'Publication JSON has duplicate properties.' }
                    Test-UniquePublicationKeys $property.Value
                }
            } elseif ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Array) {
                foreach ($item in $Element.EnumerateArray()) { Test-UniquePublicationKeys $item }
            }
        }
        Test-UniquePublicationKeys $document.RootElement
    } finally { $document.Dispose() }
    ConvertFrom-Json -InputObject $text -AsHashtable -Depth 100
}

function Assert-NavlynPublicationKeys {
    param([Collections.IDictionary]$Value, [string[]]$Required, [string[]]$Optional = @())
    if ($null -eq $Value) { throw 'Publication object is missing.' }
    foreach ($key in $Required) {
        if (@($Value.Keys | Where-Object { [string]$_ -ceq $key }).Count -ne 1) { throw "Missing publication property: $key" }
    }
    foreach ($key in $Value.Keys) {
        if ([string]$key -cnotin @($Required + $Optional)) { throw "Unexpected publication property: $key" }
    }
}

function Assert-NavlynPublicationNoReparse {
    param([Parameter(Mandatory)][string]$Path)
    $current = [IO.Path]::GetFullPath($Path)
    while ($current) {
        $item = Get-Item -LiteralPath $current -Force -ErrorAction SilentlyContinue
        if ($item -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Publication paths cannot contain reparse points.' }
        $parent = Split-Path -Parent $current
        if ($parent -eq $current) { break }
        $current = $parent
    }
}

function Get-NavlynPublicationInputFile {
    param([Parameter(Mandatory)][string]$Root, [Parameter(Mandatory)][string]$Name)
    if ($Name -cnotmatch '^[a-z][a-z0-9.-]+$' -or $Name.Contains('..')) { throw 'Publication input must be a fixed relative filename.' }
    $base = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $path = [IO.Path]::GetFullPath((Join-Path $base $Name))
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    if (!$path.StartsWith($base, $comparison)) { throw 'Publication input escaped its root.' }
    Assert-NavlynPublicationNoReparse $path
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Publication input is missing: $Name" }
    $path
}

function Assert-NavlynPublicationDigest {
    param([string]$Digest, [string]$Label)
    if ($Digest -cnotmatch '^[a-f0-9]{64}$') { throw "Invalid SHA-256: $Label" }
}

function Get-NavlynPublicationPositiveInteger {
    param([object]$Value, [string]$Label, [long]$Maximum = [long]::MaxValue)
    if ($Value -isnot [string] -and $Value -isnot [byte] -and $Value -isnot [sbyte] -and
        $Value -isnot [short] -and $Value -isnot [ushort] -and $Value -isnot [int] -and
        $Value -isnot [uint] -and $Value -isnot [long] -and $Value -isnot [ulong]) { throw "Invalid integral identity: $Label" }
    $text = [Convert]::ToString($Value, [Globalization.CultureInfo]::InvariantCulture)
    [long]$number = 0
    if ($text -cnotmatch '^[1-9][0-9]*$' -or
        ![long]::TryParse($text, [Globalization.NumberStyles]::None, [Globalization.CultureInfo]::InvariantCulture, [ref]$number) -or
        $number -gt $Maximum) { throw "Invalid positive identity: $Label" }
    $number
}

function Read-NavlynPublicationJournalRecord {
    param([Collections.IDictionary]$Record)
    Assert-NavlynPublicationKeys $Record @('path', 'sha256')
    Assert-NavlynPublicationDigest $Record.sha256 'journal'
    if ($Record.path -isnot [string] -or ![IO.Path]::IsPathFullyQualified($Record.path)) { throw 'Retained journal requires an absolute owned file path.' }
    Assert-NavlynPublicationNoReparse $Record.path
    $stream = [IO.FileStream]::new($Record.path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        if ($stream.Length -gt 1048576) { throw 'Retained journal exceeds verification bounds.' }
        $bytes = [byte[]]::new([int]$stream.Length)
        $stream.ReadExactly($bytes)
    } finally { $stream.Dispose() }
    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
    if ($hash -cne $Record.sha256) { throw 'Retained journal bytes differ from the expected digest.' }
    [pscustomobject]@{ path = $Record.path; sha256 = $hash; journal = ConvertFrom-NavlynPublicationJsonBytes $bytes }
}

function Read-NavlynPublicationInputs {
    param([string]$Root, [string]$ExpectedSha, [string]$ExpectedManifestHash, [switch]$RequireAssets)
    if ($ExpectedSha -cnotmatch '^[a-f0-9]{40}$') { throw 'Exact expected source SHA is required.' }
    $path = Get-NavlynPublicationInputFile $Root 'navlyn-publication-inputs.json'
    Assert-NavlynPublicationDigest $ExpectedManifestHash 'input manifest'
    $verifiedManifest = Read-NavlynPublicationJournalRecord @{ path = $path; sha256 = $ExpectedManifestHash }
    $manifest = $verifiedManifest.journal
    Assert-NavlynPublicationKeys $manifest @('schema', 'repository', 'workflow', 'sourceSha', 'version', 'packages', 'assets') @('createdUtc')
    if ($manifest.schema -cne 'navlyn.publication-inputs.v1' -or $manifest.repository -cne 'furbon/navlyn' -or
        $manifest.workflow -cne '.github/workflows/publish-nuget.yml' -or $manifest.sourceSha -cne $ExpectedSha -or
        $manifest.version -cnotmatch '^\d+\.\d+\.\d+$') { throw 'Publication input identity is invalid.' }
    $packages = @($manifest.packages)
    if ($packages.Count -ne 2 -or (@($packages.id | Sort-Object) -join ',') -cne 'navlyn,navlyn-mcp') { throw 'Both exact tool packages are required.' }
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($package in $packages) {
        Assert-NavlynPublicationKeys $package @('id', 'version', 'path', 'sha256')
        if ($package.version -cne $manifest.version -or $package.path -cne "$($package.id).$($manifest.version).nupkg") { throw 'Package identity or filename differs.' }
        Assert-NavlynPublicationDigest $package.sha256 $package.id
        if (!$names.Add($package.path)) { throw 'Duplicate publication filename.' }
        $file = Get-NavlynPublicationInputFile $Root $package.path
        if ((Get-NavlynPublicationHash $file) -cne $package.sha256) { throw "Package bytes differ: $($package.id)" }
        Assert-NavlynPublicationPackageIdentity $file $package.id $manifest.version $ExpectedSha
    }
    $assets = @($manifest.assets)
    if (($RequireAssets -or $assets.Count -gt 0) -and ($assets.Count -notin @(1, 2) -or @($assets | Where-Object { $_.path -ceq 'navlyn-setup.zip' }).Count -ne 1)) { throw 'Exact setup asset is required.' }
    foreach ($asset in $assets) {
        Assert-NavlynPublicationKeys $asset @('kind', 'path', 'sha256', 'sourceSha')
        if ($asset.kind -cne 'setup' -or $asset.path -cnotin @('navlyn-setup.zip', "navlyn-setup-$($manifest.version).zip") -or $asset.sourceSha -cne $ExpectedSha -or !$names.Add($asset.path)) { throw 'Publication asset identity is invalid.' }
        Assert-NavlynPublicationDigest $asset.sha256 $asset.kind
        if ((Get-NavlynPublicationHash (Get-NavlynPublicationInputFile $Root $asset.path)) -cne $asset.sha256) { throw 'Publication asset bytes differ.' }
    }
    if ($assets.Count -eq 2 -and $assets[0].sha256 -cne $assets[1].sha256) { throw 'Setup aliases must contain identical bytes.' }
    $manifest
}

function Assert-NavlynPublicationPackageIdentity {
    param([string]$Path, [string]$Id, [string]$Version, [string]$SourceSha)
    $zip = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entries = @($zip.Entries | Where-Object { $_.FullName -ceq "$Id.nuspec" })
        if ($entries.Count -ne 1 -or $entries[0].Length -gt 1048576) { throw 'Package nuspec is missing, repeated or oversized.' }
        $settings = [Xml.XmlReaderSettings]::new()
        $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit; $settings.XmlResolver = $null; $settings.MaxCharactersInDocument = 1048576
        $stream = $entries[0].Open()
        $reader = [Xml.XmlReader]::Create($stream, $settings)
        try { $document = [Xml.XmlDocument]::new(); $document.XmlResolver = $null; $document.Load($reader) }
        finally { $reader.Dispose(); $stream.Dispose() }
        $metadata = $document.SelectSingleNode('/*[local-name()="package"]/*[local-name()="metadata"]')
        if (!$metadata) { throw 'Package metadata is missing.' }
        $idNodes = $metadata.SelectNodes('*[local-name()="id"]')
        $versionNodes = $metadata.SelectNodes('*[local-name()="version"]')
        $repoNodes = $metadata.SelectNodes('*[local-name()="repository"]')
        if ($idNodes.Count -ne 1 -or $versionNodes.Count -ne 1 -or $repoNodes.Count -ne 1 -or
            $idNodes[0].InnerText -cne $Id -or $versionNodes[0].InnerText -cne $Version -or
            $repoNodes[0].GetAttribute('type') -cne 'git' -or $repoNodes[0].GetAttribute('url') -cne 'https://github.com/furbon/navlyn' -or
            $repoNodes[0].GetAttribute('commit') -cne $SourceSha) { throw 'Package nuspec does not bind the exact release source identity.' }
    } finally { $zip.Dispose() }
    $canonical = Get-NavlynCanonicalPackage $Path
    if ($canonical.ContainsKey('.signature.p7s')) { throw 'Retained publisher input must be the original unsigned package.' }
}

function Get-NavlynCanonicalPackage {
    param([Parameter(Mandatory)][string]$Path)
    $zip = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entries = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
        $collisions = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        [long]$total = 0
        foreach ($entry in $zip.Entries) {
            $name = $entry.FullName
            if ($name.EndsWith('/')) { throw 'Unexpected directory ZIP entry.' }
            if ($name -match '[\\:\x00-\x1f]' -or $name.StartsWith('/') -or
                @($name.Split('/') | Where-Object { $_ -in @('', '.', '..') }).Count -ne 0 -or
                !$collisions.Add($name.Normalize([Text.NormalizationForm]::FormC))) { throw 'Unsafe or duplicate package ZIP name.' }
            $unixType = (($entry.ExternalAttributes -shr 16) -band 0xF000)
            if ($unixType -eq 0xA000) { throw 'Package ZIP links are not permitted.' }
            $total += $entry.Length
            if ($total -gt 536870912 -or $entries.Count -ge 10000) { throw 'Package ZIP exceeds verification bounds.' }
            $stream = $entry.Open()
            try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() }
            finally { $stream.Dispose() }
            $entries.Add($name, [pscustomobject]@{ length = $entry.Length; sha256 = $hash })
        }
        return ,$entries
    } finally { $zip.Dispose() }
}

function Compare-NavlynCanonicalPackage {
    param([Parameter(Mandatory)][string]$Unsigned, [Parameter(Mandatory)][string]$Signed)
    $original = Get-NavlynCanonicalPackage $Unsigned
    $public = Get-NavlynCanonicalPackage $Signed
    if ($original.ContainsKey('.signature.p7s') -or !$public.ContainsKey('.signature.p7s') -or $public.Count -ne $original.Count + 1) { throw 'Signed package has unexpected entries.' }
    foreach ($name in $original.Keys) {
        if (!$public.ContainsKey($name) -or $original[$name].length -ne $public[$name].length -or $original[$name].sha256 -cne $public[$name].sha256) { throw "Public package content differs: $name" }
    }
    [pscustomobject]@{ entriesMatched = $original.Count; unsignedSha256 = Get-NavlynPublicationHash $Unsigned; signedSha256 = Get-NavlynPublicationHash $Signed }
}

function Write-NavlynPublicationJournal {
    param([string]$Path, [Collections.IDictionary]$Journal)
    Assert-NavlynPublicationNoReparse $Path
    $parent = Split-Path -Parent ([IO.Path]::GetFullPath($Path))
    [IO.Directory]::CreateDirectory($parent) | Out-Null
    $temporary = Join-Path $parent ('.journal-' + [guid]::NewGuid().ToString('N') + '.tmp')
    try {
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($Journal | ConvertTo-Json -Depth 100))
        $stream = [IO.FileStream]::new($temporary, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $stream.Write($bytes); $stream.Flush($true) } finally { $stream.Dispose() }
        [IO.File]::Move($temporary, [IO.Path]::GetFullPath($Path), $true)
    } finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary } }
}

function Get-NavlynPublicationIdentityText {
    param([Collections.IDictionary]$Identity)
    Assert-NavlynPublicationKeys $Identity @('repository', 'workflow', 'sourceSha', 'version', 'inputRunId', 'inputRunAttempt', 'artifactId', 'artifactDigest', 'manifestSha256')
    if ($Identity.repository -cne 'furbon/navlyn' -or $Identity.workflow -cne '.github/workflows/publish-nuget.yml' -or
        $Identity.sourceSha -cnotmatch '^[a-f0-9]{40}$' -or $Identity.version -cnotmatch '^\d+\.\d+\.\d+$') { throw 'Journal release identity is invalid.' }
    $inputRunId = Get-NavlynPublicationPositiveInteger $Identity.inputRunId 'input run'
    $inputRunAttempt = Get-NavlynPublicationPositiveInteger $Identity.inputRunAttempt 'input attempt' ([int]::MaxValue)
    $artifactId = Get-NavlynPublicationPositiveInteger $Identity.artifactId 'input artifact'
    Assert-NavlynPublicationDigest $Identity.artifactDigest 'artifact'
    Assert-NavlynPublicationDigest $Identity.manifestSha256 'manifest'
    @($Identity.repository, $Identity.workflow, $Identity.sourceSha, $Identity.version, $inputRunId,
        $inputRunAttempt, $artifactId, $Identity.artifactDigest, $Identity.manifestSha256) -join '|'
}

function Test-NavlynPublicationChain {
    param([Collections.IDictionary]$Identity, [object[]]$Chain, [object[]]$PossiblySubmittingAttempts)
    $identityText = Get-NavlynPublicationIdentityText $Identity
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $intended = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $prior = $null
    foreach ($record in $Chain) {
        $verified = Read-NavlynPublicationJournalRecord $record
        $journal = $verified.journal
        Assert-NavlynPublicationKeys $journal @('schema', 'identity', 'runId', 'runAttempt', 'predecessor', 'packages', 'phase', 'updatedUtc')
        if ($journal.schema -cne 'navlyn.publication-journal.v1' -or (Get-NavlynPublicationIdentityText $journal.identity) -cne $identityText) { throw 'Publication chain identity differs.' }
        $runId = Get-NavlynPublicationPositiveInteger $journal.runId 'journal run'
        $runAttempt = Get-NavlynPublicationPositiveInteger $journal.runAttempt 'journal attempt' ([int]::MaxValue)
        $key = "${runId}:$runAttempt"
        if (!$seen.Add($key)) { throw 'Publication chain has duplicate attempts.' }
        if ($null -eq $prior) {
            if ($null -ne $journal.predecessor -or [long]$journal.runId -ne [long]$Identity.inputRunId -or [int]$journal.runAttempt -ne [int]$Identity.inputRunAttempt) { throw 'Publication chain does not begin at original input attempt.' }
        } else {
            Assert-NavlynPublicationKeys $journal.predecessor @('runId', 'runAttempt', 'sha256')
            $predecessorId = Get-NavlynPublicationPositiveInteger $journal.predecessor.runId 'predecessor run'
            $predecessorAttempt = Get-NavlynPublicationPositiveInteger $journal.predecessor.runAttempt 'predecessor attempt' ([int]::MaxValue)
            Assert-NavlynPublicationDigest $journal.predecessor.sha256 'predecessor'
            if ($predecessorId -ne [long]$prior.journal.runId -or $predecessorAttempt -ne [int]$prior.journal.runAttempt -or
                $journal.predecessor.sha256 -cne $prior.sha256) { throw 'Publication chain is incomplete or forked.' }
        }
        $packages = @($journal.packages)
        if ($packages.Count -ne 2 -or (@($packages.id | Sort-Object) -join ',') -cne 'navlyn,navlyn-mcp') { throw 'Journal must describe both packages.' }
        foreach ($package in $packages) {
            Assert-NavlynPublicationKeys $package @('id', 'state', 'intentUtc', 'result')
            if ($package.state -cnotin @('notAttempted', 'intent', 'verified', 'failed', 'indeterminate', 'alreadyPublic')) { throw 'Unsupported publication state.' }
            if ($package.state -ceq 'notAttempted' -or $package.state -ceq 'alreadyPublic') {
                if ($null -ne $package.intentUtc) { throw 'Journal intent/state is inconsistent.' }
            } else {
                if ([string]::IsNullOrWhiteSpace([string]$package.intentUtc)) { throw 'Journal lost its publication intent.' }
                [void]$intended.Add($package.id)
            }
        }
        $prior = $verified
    }
    $inventory = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($attempt in $PossiblySubmittingAttempts) {
        Assert-NavlynPublicationKeys $attempt @('runId', 'runAttempt')
        $runId = Get-NavlynPublicationPositiveInteger $attempt.runId 'inventory run'
        $runAttempt = Get-NavlynPublicationPositiveInteger $attempt.runAttempt 'inventory attempt' ([int]::MaxValue)
        $key = "${runId}:$runAttempt"
        if (!$inventory.Add($key)) { throw 'Attempt inventory contains duplicates.' }
        if (!$seen.Contains($key)) { throw "Possibly submitting attempt has no retained journal: $key" }
    }
    if ($seen.Count -ne $inventory.Count) { throw 'Journal chain does not match complete attempt inventory.' }
    [pscustomobject]@{ prior = $prior; intendedIds = @($intended); attempts = $seen.Count }
}

function Get-NavlynPublicationDecision {
    param([string]$Id, [bool]$PriorIntent, [Collections.IDictionary]$Observation)
    if ($Id -cnotin @('navlyn', 'navlyn-mcp')) { throw 'Unexpected package ID.' }
    Assert-NavlynPublicationKeys $Observation @('status') @('signatureTrusted', 'canonicalMatch', 'signedSha256', 'signatureFingerprint')
    switch -CaseSensitive ($Observation.status) {
        'present' {
            if (!$Observation.Contains('signatureTrusted') -or !$Observation.Contains('canonicalMatch') -or
                $Observation.signatureTrusted -isnot [bool] -or $Observation.canonicalMatch -isnot [bool] -or
                !$Observation.signatureTrusted -or !$Observation.canonicalMatch) { throw 'Public package cannot be skipped without signature trust and exact canonical contents.' }
            Assert-NavlynPublicationDigest $Observation.signedSha256 'public package'
            Assert-NavlynPublicationDigest $Observation.signatureFingerprint 'public signer'
            'skipExactPublic'
        }
        'absent' { if ($PriorIntent) { 'indeterminate' } else { 'pushExactRetained' } }
        'unavailable' { 'indeterminate' }
        default { throw 'Unsupported public observation.' }
    }
}

function New-NavlynPublicationJournal {
    param([Collections.IDictionary]$Identity, [object]$RunId, [object]$RunAttempt, [object]$Predecessor)
    [void](Get-NavlynPublicationIdentityText $Identity)
    $RunId = Get-NavlynPublicationPositiveInteger $RunId 'current run'
    $RunAttempt = Get-NavlynPublicationPositiveInteger $RunAttempt 'current attempt' ([int]::MaxValue)
    [ordered]@{
        schema = 'navlyn.publication-journal.v1'; identity = $Identity; runId = $RunId; runAttempt = $RunAttempt
        predecessor = $Predecessor; phase = 'initialized'; updatedUtc = [DateTime]::UtcNow.ToString('O')
        packages = @(
            [ordered]@{ id = 'navlyn'; state = 'notAttempted'; intentUtc = $null; result = $null },
            [ordered]@{ id = 'navlyn-mcp'; state = 'notAttempted'; intentUtc = $null; result = $null }
        )
    }
}

function Set-NavlynPublicationIntent {
    param([Collections.IDictionary]$Journal, [string]$Id, [string]$Path)
    $package = @($Journal.packages | Where-Object { $_.id -ceq $Id })
    if ($package.Count -ne 1 -or $package[0].state -cne 'notAttempted') { throw 'Publication intent cannot overwrite a prior state.' }
    $package[0].state = 'intent'; $package[0].intentUtc = [DateTime]::UtcNow.ToString('O')
    $Journal.phase = 'publishing'; $Journal.updatedUtc = [DateTime]::UtcNow.ToString('O')
    Write-NavlynPublicationJournal $Path $Journal
}

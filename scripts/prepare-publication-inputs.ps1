[CmdletBinding()]
param([Parameter(Mandatory)][string]$SourceSha, [Parameter(Mandatory)][string]$SetupBundle,
    [string]$Manifest = 'artifacts/packages/navlyn-release-pack.json',
    [string]$Output = 'artifacts/publication-inputs')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/navlyn-publish-services.ps1')
$repo = Split-Path -Parent $PSScriptRoot
if ($SourceSha -cnotmatch '^[a-f0-9]{40}$' -or (& git -C $repo rev-parse HEAD).Trim() -cne $SourceSha) { throw 'Publication inputs require exact checked-out source SHA.' }
$packPath = if ([IO.Path]::IsPathFullyQualified($Manifest)) { $Manifest } else { Join-Path $repo $Manifest }
$pack = Read-NavlynPublicationJson ([IO.Path]::GetFullPath($packPath))
if ($pack.schemaVersion -cne 'navlyn.release-pack.v1' -or @($pack.packages).Count -ne 2) { throw 'Both validated release packages required.' }
$version = [string]$pack.packages[0].version
$root = [IO.Path]::GetFullPath($(if ([IO.Path]::IsPathFullyQualified($Output)) { $Output } else { Join-Path $repo $Output }))
$owned = [IO.Path]::GetFullPath((Join-Path $repo 'artifacts')).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
if (!$root.StartsWith($owned, [StringComparison]::Ordinal) -or (Test-Path -LiteralPath $root)) { throw 'Publication preparation requires a new owned artifacts directory.' }
Assert-NavlynPublicationNoReparse $root
$setup = [IO.Path]::GetFullPath($(if ([IO.Path]::IsPathFullyQualified($SetupBundle)) { $SetupBundle } else { Join-Path $repo $SetupBundle }))
Assert-NavlynPublicationNoReparse $setup
if (!(Test-Path -LiteralPath $setup -PathType Leaf)) { throw 'Exact setup bundle must exist before publication preparation.' }
[IO.Directory]::CreateDirectory($root) | Out-Null
$packages = @()
foreach ($entry in $pack.packages) {
    $path = [IO.Path]::GetFullPath((Join-Path $repo $entry.path))
    Assert-NavlynPublicationNoReparse $path
    if ($entry.id -cnotin @('navlyn', 'navlyn-mcp') -or $entry.version -cne $version -or (Get-NavlynPublicationHash $path) -cne $entry.sha256) { throw 'Validated release pack identity changed.' }
    Assert-NavlynPublicationPackageIdentity $path $entry.id $version $SourceSha
    $name = "$($entry.id).$version.nupkg"
    Copy-Item -LiteralPath $path -Destination (Join-Path $root $name)
    $packages += @{ id = $entry.id; version = $version; path = $name; sha256 = $entry.sha256 }
}
Copy-Item -LiteralPath $setup -Destination (Join-Path $root 'navlyn-setup.zip')
Copy-Item -LiteralPath $setup -Destination (Join-Path $root "navlyn-setup-$version.zip")
$inputs = [ordered]@{ schema = 'navlyn.publication-inputs.v1'; repository = 'furbon/navlyn'; workflow = '.github/workflows/publish-nuget.yml'; sourceSha = $SourceSha; version = $version; packages = $packages; assets = @(
    @{ kind = 'setup'; path = 'navlyn-setup.zip'; sha256 = Get-NavlynPublicationHash (Join-Path $root 'navlyn-setup.zip'); sourceSha = $SourceSha },
    @{ kind = 'setup'; path = "navlyn-setup-$version.zip"; sha256 = Get-NavlynPublicationHash (Join-Path $root "navlyn-setup-$version.zip"); sourceSha = $SourceSha }
) }
$path = Join-Path $root 'navlyn-publication-inputs.json'
[IO.File]::WriteAllText($path, ($inputs | ConvertTo-Json -Depth 30))
$hash = Get-NavlynPublicationHash $path
[void](Read-NavlynPublicationInputs $root $SourceSha $hash -RequireAssets)
Assert-NavlynReleaseAssets $root $inputs $repo
Write-Output "Publication manifest SHA-256: $hash"
if ($env:GITHUB_OUTPUT) { "manifest-sha256=$hash" | Add-Content -LiteralPath $env:GITHUB_OUTPUT }

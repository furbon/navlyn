[CmdletBinding()]
param(
    [string]$Manifest = 'artifacts/packages/navlyn-release-pack.json',
    [string[]]$PackagePath = @(),
    [string]$PackageSource = 'https://api.nuget.org/v3/index.json',
    [string]$ApiKeyEnvironmentVariable = 'NUGET_API_KEY',
    [switch]$DryRun,
    [switch]$Publish
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot

if ($DryRun -and $Publish) {
    throw 'Specify either -DryRun or -Publish, not both.'
}

$manifestPath = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $Manifest))
if (!(Test-Path -LiteralPath $manifestPath)) {
    throw "Package manifest was not found: $manifestPath"
}

if ($Publish) {
    throw 'Publication requires the protected exact-artifact workflow and invoke-exact-publication.ps1. This legacy entry point remains available for dry-run package inspection.'
}
$manifestJson = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
$manifestPackages = @($manifestJson.packages)
if ($manifestJson.schemaVersion -ne 'navlyn.release-pack.v1' -or $manifestPackages.Count -ne 2 -or
    (@($manifestPackages | ForEach-Object { $_.id } | Sort-Object) -join ',') -ne 'navlyn,navlyn-mcp' -or
    @($manifestPackages | ForEach-Object { $_.version } | Select-Object -Unique).Count -ne 1) {
    throw 'Release manifest must contain the matching navlyn and navlyn-mcp packages.'
}

$manifestEntries = @($manifestPackages | ForEach-Object {
    $path = if ([System.IO.Path]::IsPathRooted($_.path)) { $_.path } else { Join-Path $RepoRoot $_.path }
    [pscustomobject]@{ id = [string]$_.id; version = [string]$_.version; path = [System.IO.Path]::GetFullPath($path); sha256 = [string]$_.sha256 }
})
$resolvedPackages = if ($PackagePath.Count -eq 0) { @($manifestEntries | ForEach-Object { $_.path }) } else {
    @($PackagePath | ForEach-Object {
        $path = if ([System.IO.Path]::IsPathRooted($_)) { $_ } else { Join-Path $RepoRoot $_ }
        [System.IO.Path]::GetFullPath($path)
    })
}
if (@($resolvedPackages | Select-Object -Unique).Count -ne $resolvedPackages.Count -or
    @($resolvedPackages | Where-Object { $_ -notin @($manifestEntries | ForEach-Object { $_.path }) }).Count -ne 0) {
    throw 'Every selected package must appear exactly once in the release manifest.'
}

foreach ($entry in $manifestEntries) {
    if (!(Test-Path -LiteralPath $entry.path -PathType Leaf)) {
        throw "Package was not found: $($entry.path)"
    }
    $actualHash = (Get-FileHash -LiteralPath $entry.path -Algorithm SHA256).Hash
    if ($entry.sha256 -notmatch '^[a-fA-F0-9]{64}$' -or $actualHash -ine $entry.sha256) {
        throw "Release manifest SHA-256 mismatch for $($entry.id): $actualHash"
    }
}

if ($DryRun -or !$Publish) {
    Write-Host 'Dry run only. Publication requires the protected exact-artifact workflow.'
    foreach ($package in $resolvedPackages) {
        Write-Host "Would push $package to $PackageSource"
    }
    exit 0
}

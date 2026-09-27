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
    Write-Host 'Dry run only. Pass -Publish to push packages.'
    foreach ($package in $resolvedPackages) {
        Write-Host "Would push $package to $PackageSource"
    }
    exit 0
}

$apiKey = [Environment]::GetEnvironmentVariable($ApiKeyEnvironmentVariable)
if ([string]::IsNullOrWhiteSpace($apiKey)) {
    throw "Environment variable $ApiKeyEnvironmentVariable is required to publish. In GitHub Actions, set it from the NuGet/login Trusted Publishing output."
}

foreach ($package in $resolvedPackages) {
    $entry = @($manifestEntries | Where-Object { $_.path -eq $package })[0]
    if ($PackageSource -eq 'https://api.nuget.org/v3/index.json') {
        $index = Invoke-RestMethod -Uri "https://api.nuget.org/v3-flatcontainer/$($entry.id)/index.json"
        if (@($index.versions) -contains $entry.version) {
            throw "$($entry.id) $($entry.version) already exists on NuGet; inspect published bytes before any retry."
        }
    }
    if ((Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ine $entry.sha256) {
        throw "Package changed after manifest validation: $package"
    }
    Write-Host "Publishing $package..."
    & dotnet nuget push $package --api-key $apiKey --source $PackageSource
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet nuget push failed for $package with exit code $LASTEXITCODE."
    }
}

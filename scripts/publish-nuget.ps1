[CmdletBinding()]
param(
    [string]$Manifest = 'artifacts/packages/navlyn-release-pack.json',
    [string[]]$PackagePath = @(),
    [string]$PackageSource = 'https://api.nuget.org/v3/index.json',
    [string]$ApiKeyEnvironmentVariable = 'NUGET_API_KEY',
    [switch]$Publish
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot

if ($PackagePath.Count -eq 0) {
    $manifestPath = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $Manifest))
    if (!(Test-Path -LiteralPath $manifestPath)) {
        throw "Package manifest was not found: $manifestPath"
    }

    $manifestJson = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    $PackagePath = @($manifestJson.packages | ForEach-Object { [string]$_.path })
}

$resolvedPackages = @($PackagePath | ForEach-Object {
    $path = if ([System.IO.Path]::IsPathRooted($_)) { $_ } else { Join-Path $RepoRoot $_ }
    [System.IO.Path]::GetFullPath($path)
})

foreach ($package in $resolvedPackages) {
    if (!(Test-Path -LiteralPath $package)) {
        throw "Package was not found: $package"
    }
}

if (!$Publish) {
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
    Write-Host "Publishing $package..."
    & dotnet nuget push $package --api-key $apiKey --source $PackageSource --skip-duplicate
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet nuget push failed for $package with exit code $LASTEXITCODE."
    }
}

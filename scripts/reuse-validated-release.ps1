[CmdletBinding()]
param([Parameter(Mandatory)][string]$ExpectedSha, [string]$Output = 'artifacts/publication-inputs')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/navlyn-validated-release.ps1')
$repo = Split-Path -Parent $PSScriptRoot
if ($ExpectedSha -cnotmatch '^[a-f0-9]{40}$' -or (& git -C $repo rev-parse HEAD).Trim() -cne $ExpectedSha) { throw 'Exact checked-out source required.' }
$outputPath = [IO.Path]::GetFullPath((Join-Path $repo $Output))
$owned = [IO.Path]::GetFullPath((Join-Path $repo 'artifacts')).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
if (!$outputPath.StartsWith($owned, [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $outputPath)) { throw 'A new owned output directory is required.' }
$read = { param($Route) Invoke-NavlynGitHubJson $Route (Join-Path $repo 'artifacts/validated-release-provenance') }
$validated = Get-NavlynValidatedRelease $ExpectedSha $read
$zip = Join-Path $repo 'artifacts/validated-release.zip'
Save-NavlynGitHubArtifact $validated.artifact.id $validated.digest $zip
Expand-NavlynPublicationArtifact $zip $outputPath
$hash = Get-NavlynPublicationHash (Join-Path $outputPath 'navlyn-publication-inputs.json')
$inputs = Read-NavlynPublicationInputs $outputPath $ExpectedSha $hash -RequireAssets
Assert-NavlynReleaseAssets $outputPath $inputs $repo
Write-Output "Reused tested release inputs from exact-main CI $($validated.run.id), Windows attempt $($validated.artifactRun.run_attempt); latest CI attempt $($validated.run.run_attempt) succeeded."
if ($env:GITHUB_OUTPUT) { "manifest-sha256=$hash" | Add-Content -LiteralPath $env:GITHUB_OUTPUT }

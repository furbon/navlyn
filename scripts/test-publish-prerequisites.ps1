[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
& $PSScriptRoot/test-release.ps1 -Configuration Release
Write-Host 'Publish prerequisites passed.'

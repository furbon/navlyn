[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

function Invoke-Dotnet {
    param([string]$Name, [string[]]$Arguments)

    Write-Host "Running $Name..."
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

Push-Location $repoRoot
try {
    Invoke-Dotnet -Name 'restore' -Arguments @('restore', 'navlyn.slnx')
    Invoke-Dotnet -Name 'build' -Arguments @('build', 'navlyn.slnx', '--no-restore')
    Invoke-Dotnet -Name 'test' -Arguments @('test', 'navlyn.slnx', '--no-build')
    & ./scripts/test-csharp-file-format.ps1
    & ./scripts/test-release-contract.ps1
    Write-Host 'Publish prerequisites passed.'
}
finally {
    Pop-Location
}

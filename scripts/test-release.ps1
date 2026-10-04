[CmdletBinding()]
param([switch]$ShowOutput, [switch]$NoBuild, [switch]$SkipDotnetTest,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$env:NAVLYN_TEST_CONFIGURATION = $Configuration
Push-Location $repo
try {
    if (!$NoBuild) {
        dotnet build navlyn.slnx -c $Configuration
        if ($LASTEXITCODE) { throw 'Build failed.' }
    }
    if (!$SkipDotnetTest) {
        dotnet test navlyn.Tests/navlyn.Tests.csproj --no-build -c $Configuration --framework net10.0
        if ($LASTEXITCODE) { throw 'Product tests failed.' }
    }
    foreach ($name in @('test-csharp-file-format.ps1','test-release-version.ps1','test-release-contract.ps1',
        'test-validated-release.ps1','test-ci-diagnostics.ps1','test-setup-navlyn.ps1',
        'test-publish-recovery.ps1','test-publish-orchestration.ps1')) {
        & (Join-Path $PSScriptRoot $name)
        if (!$?) { throw "$name failed." }
    }
    Write-Host 'Fast release checks passed. Package smoke runs once on the retained CI packages.'
} finally { Pop-Location }

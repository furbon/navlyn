[CmdletBinding()]
param([switch]$NoBuild, [switch]$SkipDotnetTest, [switch]$ShowOutput,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$env:NAVLYN_TEST_CONFIGURATION = $Configuration
. $PSScriptRoot/lib/navlyn-test-harness.ps1
Initialize-NavlynTestHarness -RepoRoot $repo -ShowOutput:$ShowOutput
& $PSScriptRoot/test-csharp-file-format.ps1 -Quiet
if (!$NoBuild) {
    Invoke-CheckedProcess -Name build -FilePath dotnet -Arguments @('build', $script:NavlynTestSolutionPath, '-c', $Configuration) -ExpectedExitCode 0 -TimeoutSeconds 180 | Out-Null
}
if (!$SkipDotnetTest) {
    Invoke-CheckedProcess -Name product-tests -FilePath dotnet -Arguments @('test', (Join-Path $repo 'navlyn.Tests/navlyn.Tests.csproj'), '--no-build', '-c', $Configuration, '--framework', 'net10.0') -ExpectedExitCode 0 -TimeoutSeconds 300 | Out-Null
}
& $PSScriptRoot/test-portable-smoke.ps1 -Configuration $Configuration
Write-Host 'Quick checks passed.'

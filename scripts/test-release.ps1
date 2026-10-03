[CmdletBinding()]
param(
    [switch]$ShowOutput,
    [switch]$NoBuild,
    [switch]$SkipDotnetTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$HarnessPath = Join-Path $PSScriptRoot 'lib/navlyn-test-harness.ps1'
$FormatCheckScript = Join-Path $PSScriptRoot 'test-csharp-file-format.ps1'
$QuickScript = Join-Path $PSScriptRoot 'test-quick.ps1'
$CliContractScript = Join-Path $PSScriptRoot 'test-cli-contract.ps1'
$PackageInstallScript = Join-Path $PSScriptRoot 'test-package-install.ps1'
$PerformanceScript = Join-Path $PSScriptRoot 'measure-navlyn-performance.ps1'
$ToolSelectionEvalScript = Join-Path $PSScriptRoot 'test-tool-selection-eval.ps1'
$AgentEvidenceEvalScript = Join-Path $PSScriptRoot 'test-agent-evidence-eval.ps1'
$WrongSymbolEvalScript = Join-Path $PSScriptRoot 'test-wrong-symbol-avoidance-eval.ps1'
$McpAgentTraceEvalScript = Join-Path $PSScriptRoot 'test-mcp-agent-trace-eval.ps1'
$FocusedScripts = @(
    'test-symbol-navigation.ps1',
    'test-fuzzy-discovery.ps1',
    'test-multi-project-navigation.ps1',
    'test-workspace-semantics.ps1',
    'test-diagnostics.ps1'
)
$AuditScript = Join-Path $PSScriptRoot 'audit-public-readiness.ps1'

. $HarnessPath
Initialize-NavlynTestHarness -RepoRoot $RepoRoot -ShowOutput:$ShowOutput

Push-Location $RepoRoot
try {
    if (!$NoBuild) {
        Write-Host 'Restoring navlyn...'
        Invoke-CheckedProcess `
            -Name 'dotnet restore' `
            -FilePath 'dotnet' `
            -Arguments @('restore', $script:NavlynTestSolutionPath) `
            -ExpectedExitCode 0 | Out-Null

        Write-Host 'Building navlyn...'
        Invoke-CheckedProcess `
            -Name 'dotnet build' `
            -FilePath 'dotnet' `
            -Arguments @('build', $script:NavlynTestSolutionPath, '--no-restore') `
            -ExpectedExitCode 0 | Out-Null

    }
    if (!$SkipDotnetTest) {
        Write-Host 'Running xUnit tests on net8.0...'
        Invoke-CheckedProcess `
            -Name 'dotnet test net8.0' `
            -FilePath 'dotnet' `
            -Arguments @('test', $script:NavlynTestSolutionPath, '--framework', 'net8.0', '--no-build') `
            -ExpectedExitCode 0 | Out-Null

        Write-Host 'Running xUnit tests on net10.0...'
        Invoke-CheckedProcess `
            -Name 'dotnet test net10.0' `
            -FilePath 'dotnet' `
            -Arguments @('test', $script:NavlynTestSolutionPath, '--framework', 'net10.0', '--no-build') `
            -ExpectedExitCode 0 | Out-Null

    }
    & $FormatCheckScript -Quiet
    & (Join-Path $PSScriptRoot 'test-release-version.ps1')
    & (Join-Path $PSScriptRoot 'test-schema-wrapper.ps1')
    & (Join-Path $PSScriptRoot 'test-ci-diagnostics.ps1')
    & (Join-Path $PSScriptRoot 'test-validated-release.ps1')
    & $QuickScript -NoBuild -SkipDotnetTest -ShowOutput:$ShowOutput
    & $CliContractScript -NoBuild -Suite all -ShowOutput:$ShowOutput
    & (Join-Path $PSScriptRoot 'test-reproduction-kind-parity.ps1') -NoBuild
    & $ToolSelectionEvalScript -UseBaselineTraces -NoBuild -Output 'artifacts/evals/tool-selection-release-report.json'
    & $AgentEvidenceEvalScript -NoBuild -SkipMcpLatency -OutputDirectory 'artifacts/evals/release-agent-evidence'
    & $WrongSymbolEvalScript -NoBuild -Output 'artifacts/evals/wrong-symbol-avoidance-release-report.json'
    & $McpAgentTraceEvalScript -Output 'artifacts/evals/mcp-agent-trace-release-report.json'

    foreach ($scriptName in $FocusedScripts) {
        $scriptPath = Join-Path $PSScriptRoot $scriptName
        & $scriptPath -NoBuild -ShowOutput:$ShowOutput
    }

    & $AuditScript
    & $PerformanceScript -Workspace $script:NavlynTestSolutionPath -Scenario quick -Iterations 1 -Warmup 0 -NoBuild -Output 'artifacts/performance-smoke/navlyn-quick.json'
    & (Join-Path $PSScriptRoot 'test-performance-contract.ps1')
    & $PerformanceScript -Workspace $script:NavlynTestSolutionPath -Scenario mcp -Profile compact -Iterations 1 -Warmup 0 -NoBuild -Output 'artifacts/performance-smoke/navlyn-mcp.json'
    & $PackageInstallScript

    & (Join-Path $PSScriptRoot 'test-setup-prerequisites.ps1')
    & (Join-Path $PSScriptRoot 'test-setup-navlyn.ps1')
    & (Join-Path $PSScriptRoot 'test-publish-recovery.ps1')
    & (Join-Path $PSScriptRoot 'test-publish-orchestration.ps1')
    & (Join-Path $PSScriptRoot 'test-real-task-eval.ps1')

    Write-Host 'Release validation passed.'
}
finally {
    Pop-Location
}

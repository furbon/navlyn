[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/navlyn-test-harness.ps1')
$repo = Split-Path -Parent $PSScriptRoot
Initialize-NavlynTestHarness -RepoRoot $repo
$root = Join-Path $repo ('artifacts/performance-contract/' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root) | Out-Null
$report = Join-Path $root 'failed.json'
$workspace = Join-Path $root 'invalid.csproj'
[IO.File]::WriteAllText($workspace, '<Project')
$result = Invoke-CheckedProcess -Name 'performance failure propagation' -FilePath pwsh -Arguments @('-NoProfile','-File',(Join-Path $PSScriptRoot 'measure-navlyn-performance.ps1'),'-Workspace',$workspace,'-Scenario','quick','-Iterations','1','-Warmup','0','-NoBuild','-Output',$report) -ExpectedExitCode 1
if (!(Test-Path -LiteralPath $report)) { throw "Performance failure did not save its report: $($result.Stderr)" }
$json = Get-Content -Raw $report | ConvertFrom-Json -Depth 100
if ($json.summary.failed -ne 4 -or $json.summary.succeeded -ne 0 -or !$result.Stderr.Contains('failed/invalid measurements')) { throw 'Performance failure was not retained and propagated.' }
$report = Join-Path $root 'missing-mcp.json'
Invoke-CheckedProcess -Name 'performance missing MCP prerequisite' -FilePath pwsh -Arguments @('-NoProfile','-File',(Join-Path $PSScriptRoot 'measure-navlyn-performance.ps1'),'-Workspace',(Join-Path $repo 'navlyn.slnx'),'-Scenario','mcp','-McpDll',(Join-Path $root 'missing.dll'),'-Iterations','1','-Warmup','0','-NoBuild','-Output',$report) -ExpectedExitCode 1 | Out-Null
$json = Get-Content -Raw $report | ConvertFrom-Json -Depth 100
if ($json.summary.skipped -ne 1 -or $json.summary.succeeded -ne 0) { throw 'Missing performance prerequisite was hidden.' }
Write-Host 'Performance report failure propagation passed.'

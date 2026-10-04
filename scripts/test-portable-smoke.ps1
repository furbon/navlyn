[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/navlyn-test-harness.ps1')
$repo = Split-Path -Parent $PSScriptRoot
$env:NAVLYN_TEST_CONFIGURATION = $Configuration
Initialize-NavlynTestHarness -RepoRoot $repo
$root = Join-Path $repo ('artifacts/portable smoke/' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root) | Out-Null
$project = Join-Path $root 'Smoke.csproj'
[IO.File]::WriteAllText($project, '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>')
[IO.File]::WriteAllText((Join-Path $root 'Smoke.cs'), 'public class Smoke { public int Value => 1; }')
Invoke-CheckedProcess -Name 'restore tiny smoke fixture' -FilePath dotnet -Arguments @('restore', $project, '--ignore-failed-sources') -ExpectedExitCode 0 | Out-Null
$result = Invoke-Navlyn -Name 'portable kind and spaced workspace' -Arguments @('symbols','--workspace',$project,'--query','Smoke','--kind',' CLASS ') -ExpectedExitCode 0
Assert-Empty -Name 'symbols stderr' -Text $result.Stderr
$json = $result.Stdout | ConvertFrom-Json -Depth 100
Assert-Equal -Name 'canonical kind' -Actual $json.kinds[0] -Expected 'NamedType'
Assert-Equal -Name 'symbol found' -Actual $json.matches[0].name -Expected 'Smoke'
$inputJson = '{"requests":[{"id":"symbol","command":"symbols","query":"Smoke","kinds":["class"]},{"id":"outline","command":"outline","file":"Smoke.cs"}]}'
$result = Invoke-Navlyn -Name 'portable batch stdin' -Arguments @('batch','--workspace',$project) -StandardInput $inputJson -ExpectedExitCode 0
Assert-Empty -Name 'batch stderr' -Text $result.Stderr
$json = $result.Stdout | ConvertFrom-Json -Depth 100
Assert-Equal -Name 'batch successes' -Actual $json.succeededRequests -Expected 2
$result = Invoke-Navlyn -Name 'portable usage error' -Arguments @('check') -ExpectedExitCode 2
Assert-Empty -Name 'usage stdout' -Text $result.Stdout
Assert-Contains -Name 'usage diagnostic' -Text $result.Stderr -Expected 'NAVLYN1001'
Write-Host 'Portable CLI whitespace, kinds, batch stdin, JSON, stderr, and exit-code smoke passed.'

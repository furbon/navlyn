[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/navlyn-test-harness.ps1')
$repo = Split-Path -Parent $PSScriptRoot
Initialize-NavlynTestHarness -RepoRoot $repo
$root = Join-Path $repo ('artifacts/ci-diagnostics-tests/' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root) | Out-Null
$probe = Join-Path $root 'probe.ps1'
[IO.File]::WriteAllText($probe, 'param([int]$Code) Write-Output "probe stdout"; [Console]::Error.WriteLine("probe stderr"); exit $Code')
$priorSummary = $env:GITHUB_STEP_SUMMARY
try {
    $env:GITHUB_STEP_SUMMARY = Join-Path $root 'summary.md'
    foreach ($code in @(0, 37)) {
        $name = "probe-$code"
        # Pass the array through a child script so PowerShell does not consume native switches.
        $driver = Join-Path $root "$name.ps1"
        $literalRoot = $root.Replace("'", "''"); $literalProbe = $probe.Replace("'", "''")
        $wrapper = (Join-Path $PSScriptRoot 'invoke-ci-step.ps1').Replace("'", "''")
        [IO.File]::WriteAllText($driver, "& '$wrapper' -Name '$name' -FilePath pwsh -Arguments @('-NoProfile','-File','$literalProbe','-Code','$code') -OutputDirectory '$literalRoot'")
        Invoke-CheckedProcess -Name $name -FilePath pwsh -Arguments @('-NoProfile', '-File', $driver) -ExpectedExitCode $(if ($code) { 1 } else { 0 }) | Out-Null
        $log = Get-Content -Raw (Join-Path $root "$name.log")
        if ($log -notmatch 'probe stdout' -or $log -notmatch 'probe stderr') { throw 'CI diagnostics lost process output.' }
    }
    $records = @(Get-Content (Join-Path $root 'timings.jsonl') | ForEach-Object { $_ | ConvertFrom-Json })
    if ($records.Count -ne 2 -or $records[0].exitCode -ne 0 -or $records[1].exitCode -ne 37 -or $records[1].elapsedSeconds -le 0) { throw 'CI timing/exit evidence is incorrect.' }
    if ((Get-Content -Raw $env:GITHUB_STEP_SUMMARY) -notmatch 'exit 37') { throw 'CI summary lost failure.' }
    $argumentProbe = Join-Path $root 'arguments.ps1'
    [IO.File]::WriteAllText($argumentProbe, '[Console]::WriteLine((ConvertTo-Json -InputObject @($args) -Compress))')
    $arguments = @('', 'a b', 'quote"value', 'trailing space\')
    $received = (Invoke-CheckedProcess -Name arguments -FilePath pwsh -Arguments (@('-NoProfile','-File',$argumentProbe) + $arguments) -ExpectedExitCode 0).Stdout | ConvertFrom-Json
    if (($received | ConvertTo-Json -Compress) -cne ($arguments | ConvertTo-Json -Compress)) { throw 'Process arguments changed in transit.' }
    $pidFile = Join-Path $root 'deadline.pid'
    $deadlineProbe = Join-Path $root 'deadline.ps1'
    [IO.File]::WriteAllText($deadlineProbe, "`$PID | Set-Content -LiteralPath '$($pidFile.Replace("'", "''"))'; Start-Sleep -Seconds 30")
    $clock = [Diagnostics.Stopwatch]::StartNew()
    try {
        Invoke-CheckedProcess -Name deadline -FilePath pwsh -Arguments @('-NoProfile','-File',$deadlineProbe) -ExpectedExitCode 0 -TimeoutSeconds 1 | Out-Null
        throw 'Deadline probe unexpectedly completed.'
    } catch {
        if ($_.Exception.Message -notmatch 'exceeded its 1 second process deadline') { throw }
    }
    if ($clock.Elapsed.TotalSeconds -gt 5 -or (Get-Process -Id ([int](Get-Content $pidFile)) -ErrorAction SilentlyContinue)) { throw 'Deadline did not stop/reap its process.' }
    Write-Host 'CI diagnostics success/failure retention checks passed.'
} finally { $env:GITHUB_STEP_SUMMARY = $priorSummary }

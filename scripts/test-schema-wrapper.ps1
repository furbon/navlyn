[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Join-Path (Split-Path -Parent $PSScriptRoot) ('artifacts/schema-wrapper-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root) | Out-Null
$probe = Join-Path $root 'probe.ps1'
[IO.File]::WriteAllText($probe, @'
param($Mode, $Wrapper, $Log)
$ErrorActionPreference = 'Stop'
function global:dotnet {
    $operation = $args[0]
    [IO.File]::AppendAllText($Log, $operation + "`n")
    $code = if (($Mode -eq 'build-failure' -and $operation -eq 'build') -or ($Mode -eq 'test-failure' -and $operation -eq 'test')) { 37 } else { 0 }
    & (Get-Process -Id $PID).Path -NoProfile -Command "exit $code"
}
if ($Mode -eq 'test-failure') { & $Wrapper -NoBuild } else { & $Wrapper }
'@)
foreach ($mode in @('build-failure', 'test-failure', 'success')) {
    $log = Join-Path $root "$mode.txt"
    $start = [Diagnostics.ProcessStartInfo]::new((Get-Process -Id $PID).Path)
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    foreach ($value in @('-NoProfile','-File',$probe,$mode,(Join-Path $PSScriptRoot 'test-contract-schemas.ps1'),$log)) { $start.ArgumentList.Add($value) }
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    $output = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
    [IO.File]::WriteAllText((Join-Path $root "$mode-output.txt"), $output)
    $code = $process.ExitCode; $process.Dispose()
    $calls = @(Get-Content -LiteralPath $log)
    if ($mode -eq 'success') {
        if ($code -ne 0 -or ($calls -join ',') -cne 'build,test') { throw 'Successful schema wrapper failed.' }
    } else {
        if ($code -eq 0 -or $output -notmatch 'exit code 37') { throw "Schema wrapper masked $mode." }
        if ($mode -eq 'build-failure' -and ($calls -join ',') -cne 'build') { throw 'Schema wrapper tested stale binaries after build failure.' }
    }
}
Write-Output 'Schema wrapper success and native failure propagation passed.'

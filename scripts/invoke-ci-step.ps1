[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+$')][string]$Name,
    [Parameter(Mandatory)][string]$FilePath,
    [string[]]$Arguments = @(),
    [string]$OutputDirectory = 'artifacts/ci-diagnostics'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$directory = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($directory) | Out-Null
$log = Join-Path $directory "$Name.log"
$clock = [Diagnostics.Stopwatch]::StartNew()
$exitCode = 1
Write-Host "[$Name] starting"
try {
    $global:LASTEXITCODE = 0
    & $FilePath @Arguments *>&1 | Tee-Object -FilePath $log | Out-Host
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) { throw "$Name failed with exit code $exitCode. See $log." }
} catch {
    $_ | Out-String | Add-Content -LiteralPath $log
    throw
} finally {
    $clock.Stop()
    $record = [ordered]@{ stage = $Name; exitCode = $exitCode; elapsedSeconds = [Math]::Round($clock.Elapsed.TotalSeconds, 3); log = "$Name.log" }
    $record | ConvertTo-Json -Compress | Add-Content -LiteralPath (Join-Path $directory 'timings.jsonl')
    if ($env:GITHUB_STEP_SUMMARY) {
        "- $Name`: exit $exitCode; $($record.elapsedSeconds) seconds; log ``$Name.log``" | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY
    }
}

[CmdletBinding()]
param(
    [ValidateSet('quick', 'xunit', 'contract-core', 'contract-navigation', 'contract-workflow', 'contract-domain', 'contract-mcp-adjacent', 'contract-all', 'release', 'all')]
    [string]$Lane = 'quick',

    [switch]$NoBuild,

    [string]$OutputDirectory = 'artifacts/test-timings',

    [int]$TimeoutSeconds = 1800
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($TimeoutSeconds -lt 1) {
    throw 'TimeoutSeconds must be 1 or greater.'
}

$RepoRoot = Split-Path -Parent $PSScriptRoot
$pwshCommand = Get-Command pwsh -ErrorAction SilentlyContinue
$PowerShellExecutable = if ($null -ne $pwshCommand) { $pwshCommand.Source } else { 'powershell' }

function New-Step {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    [pscustomobject]@{
        name = $Name
        filePath = $FilePath
        arguments = $Arguments
    }
}

function Get-ContractStep {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Suite
    )

    $arguments = @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'test-cli-contract.ps1'), '-Suite', $Suite)
    if ($NoBuild) {
        $arguments += '-NoBuild'
    }

    New-Step -Name "contract-$Suite" -FilePath $PowerShellExecutable -Arguments $arguments
}

function Get-Steps {
    $quickArguments = @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'test-quick.ps1'), '-SkipDotnetTest')
    if ($NoBuild) {
        $quickArguments += '-NoBuild'
    }

    switch ($Lane) {
        'quick' {
            return @(New-Step -Name 'quick' -FilePath $PowerShellExecutable -Arguments $quickArguments)
        }
        'xunit' {
            return @(New-Step -Name 'xunit' -FilePath 'dotnet' -Arguments @('test', (Join-Path $RepoRoot 'navlyn.slnx'), '--no-build'))
        }
        'contract-core' {
            return @(Get-ContractStep -Suite 'core')
        }
        'contract-navigation' {
            return @(Get-ContractStep -Suite 'navigation')
        }
        'contract-workflow' {
            return @(Get-ContractStep -Suite 'workflow')
        }
        'contract-domain' {
            return @(Get-ContractStep -Suite 'domain')
        }
        'contract-mcp-adjacent' {
            return @(Get-ContractStep -Suite 'mcp-adjacent')
        }
        'contract-all' {
            return @(Get-ContractStep -Suite 'all')
        }
        'release' {
            return @(New-Step -Name 'release' -FilePath $PowerShellExecutable -Arguments @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'test-release.ps1')))
        }
        'all' {
            return @(
                New-Step -Name 'xunit' -FilePath 'dotnet' -Arguments @('test', (Join-Path $RepoRoot 'navlyn.slnx'), '--no-build'),
                New-Step -Name 'quick' -FilePath $PowerShellExecutable -Arguments $quickArguments,
                (Get-ContractStep -Suite 'core'),
                (Get-ContractStep -Suite 'navigation'),
                (Get-ContractStep -Suite 'workflow'),
                (Get-ContractStep -Suite 'domain'),
                (Get-ContractStep -Suite 'mcp-adjacent')
            )
        }
        default {
            throw "Unknown lane: $Lane"
        }
    }
}

function Invoke-MeasuredStep {
    param(
        [Parameter(Mandatory = $true)]
        [psobject]$Step
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $Step.filePath
    $startInfo.WorkingDirectory = $RepoRoot
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $startInfo.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    $startInfo.UseShellExecute = $false
    foreach ($argument in $Step.arguments) {
        [void]$startInfo.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    [void]$process.Start()
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $finished = $process.WaitForExit($TimeoutSeconds * 1000)
    if (!$finished) {
        try {
            $process.Kill($true)
        }
        catch {
        }
    }

    $stopwatch.Stop()
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    $exitCode = if ($finished) { $process.ExitCode } else { -1 }

    [pscustomobject]@{
        name = $Step.name
        command = [pscustomobject]@{
            executable = $Step.filePath
            arguments = $Step.arguments
        }
        exitCode = $exitCode
        elapsedMs = [int][Math]::Round($stopwatch.Elapsed.TotalMilliseconds)
        stdoutChars = $stdout.Length
        stderrChars = $stderr.Length
        timedOut = !$finished
    }
}

$steps = Get-Steps
$results = New-Object System.Collections.Generic.List[object]
foreach ($step in $steps) {
    Write-Host "Measuring validation lane step: $($step.name)"
    $result = Invoke-MeasuredStep -Step $step
    $results.Add($result)
    if ($result.exitCode -ne 0) {
        break
    }
}

$resultArray = $results.ToArray()
$failed = @($resultArray | Where-Object { $_.exitCode -ne 0 })
$summary = [ordered]@{
    totalSteps = @($resultArray).Count
    succeeded = @($resultArray | Where-Object { $_.exitCode -eq 0 }).Count
    failed = $failed.Count
    totalElapsedMs = [int](($resultArray | Measure-Object -Property elapsedMs -Sum).Sum)
}

$report = [ordered]@{
    schemaVersion = 'navlyn.validation-timing.v1'
    createdUtc = [DateTimeOffset]::UtcNow.ToString('o')
    lane = $Lane
    noBuild = [bool]$NoBuild
    timeoutSeconds = $TimeoutSeconds
    environment = [ordered]@{
        os = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
        dotnetVersion = [string](& dotnet --version)
        processorCount = [Environment]::ProcessorCount
    }
    summary = $summary
    steps = $resultArray
}

$outputRoot = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $OutputDirectory))
[System.IO.Directory]::CreateDirectory($outputRoot) | Out-Null
$timestamp = [DateTimeOffset]::UtcNow.ToString('yyyyMMdd-HHmmss')
$outputPath = Join-Path $outputRoot "$timestamp-$Lane.json"
$report | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $outputPath -Encoding utf8
Write-Host "Validation timing report written to $outputPath"

if ($failed.Count -gt 0) {
    $firstFailure = @($failed)[0]
    throw "Validation step '$($firstFailure.name)' failed with exit code $($firstFailure.exitCode). Timing report: $outputPath"
}

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Workspace,
    [string]$Output = 'artifacts/navlyn-pr-facts',
    [string]$Base = 'HEAD',
    [string]$NavlynExecutable = 'dotnet',
    [string[]]$NavlynPrefixArguments = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$OutputPath = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $Output))
[System.IO.Directory]::CreateDirectory($OutputPath) | Out-Null

function Get-ProjectTargetFramework {
    param([string]$ProjectPath)

    [xml]$projectXml = Get-Content -Raw -LiteralPath $ProjectPath
    return [string]$projectXml.Project.PropertyGroup.TargetFramework
}

if ($NavlynPrefixArguments.Count -eq 0 -and $NavlynExecutable -eq 'dotnet') {
    $targetFramework = Get-ProjectTargetFramework -ProjectPath (Join-Path $RepoRoot 'navlyn/navlyn.csproj')
    $NavlynPrefixArguments = @("navlyn/bin/Debug/$targetFramework/navlyn.dll")
}

function Invoke-NavlynFact {
    param(
        [string]$Name,
        [string[]]$Arguments
    )

    $stdoutPath = Join-Path $OutputPath "$Name.json"
    $stderrPath = Join-Path $OutputPath "$Name.stderr.txt"
    $fullArguments = @($NavlynPrefixArguments + $Arguments)
    Write-Host "Writing $Name..."
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $NavlynExecutable
    $startInfo.WorkingDirectory = $RepoRoot
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.UseShellExecute = $false
    foreach ($argument in $fullArguments) {
        [void]$startInfo.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::Start($startInfo)
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    Set-Content -LiteralPath $stdoutPath -Value $stdout -NoNewline -Encoding utf8
    Set-Content -LiteralPath $stderrPath -Value $stderr -NoNewline -Encoding utf8
    $stderr = if (Test-Path -LiteralPath $stderrPath) { [string](Get-Content -Raw -LiteralPath $stderrPath) } else { '' }
    $jsonValid = $false
    $truncated = $false
    $warningCount = 0
    if ($process.ExitCode -eq 0 -and (Test-Path -LiteralPath $stdoutPath)) {
        try {
            $parsed = Get-Content -Raw -LiteralPath $stdoutPath | ConvertFrom-Json -Depth 100
            $jsonValid = $true
            if ($parsed.PSObject.Properties.Name -contains 'truncated') {
                $truncated = $parsed.truncated -eq $true
            }
            if ($parsed.PSObject.Properties.Name -contains 'warnings') {
                $warningCount = @($parsed.warnings).Count
            }
        }
        catch {
            $jsonValid = $false
        }
    }

    [pscustomobject]@{
        name = $Name
        file = (Split-Path -Leaf $stdoutPath)
        exitCode = $process.ExitCode
        jsonValid = $jsonValid
        truncated = $truncated
        warningCount = $warningCount
        stderr = if ($null -eq $stderr) { '' } else { $stderr.Trim() }
    }
}

Push-Location $RepoRoot
try {
    $measurements = @()
    $measurements += Invoke-NavlynFact -Name 'review-diff' -Arguments @('review-diff', '--workspace', $Workspace, '--base', $Base, '--profile', 'evidence', '--symbol-limit', '50', '--impact-limit', '100', '--diagnostic-limit', '100', '--related-test-limit', '50')
    $measurements += Invoke-NavlynFact -Name 'context-pack' -Arguments @('context-pack', '--workspace', $Workspace, '--diff', '--base', $Base, '--profile', 'compact', '--budget-tokens', '8000')
    $measurements += Invoke-NavlynFact -Name 'tests-for-diff' -Arguments @('tests-for-diff', '--workspace', $Workspace, '--base', $Base, '--profile', 'compact', '--test-limit', '50')
    $measurements += Invoke-NavlynFact -Name 'public-api-diff' -Arguments @('public-api-diff', '--workspace', $Workspace, '--base', $Base, '--profile', 'evidence', '--change-limit', '100')
    $measurements += Invoke-NavlynFact -Name 'review-pack' -Arguments @('review-pack', '--workspace', $Workspace, '--base', $Base, '--profile', 'evidence', '--finding-limit', '100')

    $summaryPath = Join-Path $OutputPath 'summary.md'
    $lines = @(
        '# Navlyn Facts For This PR',
        '',
        '| Fact | Exit | JSON | Truncated | Warnings | Artifact |',
        '| --- | ---: | --- | --- | ---: | --- |'
    )
    foreach ($measurement in $measurements) {
        $lines += "| $($measurement.name) | $($measurement.exitCode) | $($measurement.jsonValid) | $($measurement.truncated) | $($measurement.warningCount) | `$($measurement.file)` |"
    }
    $lines += ''
    $lines += 'Artifacts contain deterministic Navlyn JSON facts. The summary intentionally reports counts and status only.'
    Set-Content -LiteralPath $summaryPath -Value $lines -Encoding utf8

    $measurements | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $OutputPath 'manifest.json') -Encoding utf8
    Write-Host "Navlyn PR facts written to $OutputPath"
}
finally {
    Pop-Location
}

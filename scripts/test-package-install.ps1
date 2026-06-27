[CmdletBinding()]
param(
    [switch]$NoBuild,
    [string]$Output = 'artifacts/package-smoke/packages'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$PackageOutput = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $Output))
$ToolPath = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot 'artifacts/package-smoke/tools'))

function Get-ProjectVersion {
    param([string]$ProjectPath)

    [xml]$projectXml = Get-Content -Raw -LiteralPath $ProjectPath
    return [string]$projectXml.Project.PropertyGroup.Version
}

function Get-ProjectTargetFramework {
    param([string]$ProjectPath)

    [xml]$projectXml = Get-Content -Raw -LiteralPath $ProjectPath
    return [string]$projectXml.Project.PropertyGroup.TargetFramework
}

function Get-ToolExecutable {
    param([string]$Name)

    $extension = if ($IsWindows) { '.exe' } else { '' }
    return Join-Path $ToolPath "$Name$extension"
}

function Invoke-Checked {
    param(
        [string]$Name,
        [string]$FilePath,
        [string[]]$Arguments
    )

    Write-Host "Running $Name..."
    $stdout = [System.IO.Path]::GetTempFileName()
    $stderr = [System.IO.Path]::GetTempFileName()
    try {
        $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = $FilePath
        $startInfo.WorkingDirectory = $RepoRoot
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        $startInfo.UseShellExecute = $false
        foreach ($argument in $Arguments) {
            [void]$startInfo.ArgumentList.Add($argument)
        }

        $process = [System.Diagnostics.Process]::Start($startInfo)
        $out = $process.StandardOutput.ReadToEnd()
        $err = $process.StandardError.ReadToEnd()
        $process.WaitForExit()
        Set-Content -LiteralPath $stdout -Value $out -NoNewline
        Set-Content -LiteralPath $stderr -Value $err -NoNewline
        if ($process.ExitCode -ne 0) {
            throw "$Name failed with exit code $($process.ExitCode).`nstdout:`n$out`nstderr:`n$err"
        }
    }
    finally {
        Remove-Item -LiteralPath @($stdout, $stderr) -ErrorAction SilentlyContinue
    }
}

if (!$NoBuild) {
    Invoke-Checked -Name 'dotnet build' -FilePath 'dotnet' -Arguments @('build', 'navlyn.slnx', '-c', 'Release')
}
else {
    $navlynTargetFramework = Get-ProjectTargetFramework -ProjectPath (Join-Path $RepoRoot 'navlyn/navlyn.csproj')
    $mcpTargetFramework = Get-ProjectTargetFramework -ProjectPath (Join-Path $RepoRoot 'navlyn.Mcp/navlyn.Mcp.csproj')
    $navlynReleaseDll = Join-Path $RepoRoot "navlyn/bin/Release/$navlynTargetFramework/navlyn.dll"
    $mcpReleaseDll = Join-Path $RepoRoot "navlyn.Mcp/bin/Release/$mcpTargetFramework/navlyn.Mcp.dll"
    if (!(Test-Path -LiteralPath $navlynReleaseDll) -or !(Test-Path -LiteralPath $mcpReleaseDll)) {
        throw 'Release outputs were not found. Run without -NoBuild first.'
    }
}

[System.IO.Directory]::CreateDirectory($PackageOutput) | Out-Null
$toolRoot = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot 'artifacts/package-smoke'))
if (!(Test-Path -LiteralPath $toolRoot)) {
    [System.IO.Directory]::CreateDirectory($toolRoot) | Out-Null
}

if (Test-Path -LiteralPath $ToolPath) {
    Remove-Item -LiteralPath $ToolPath -Recurse -Force
}
[System.IO.Directory]::CreateDirectory($ToolPath) | Out-Null

$packArgs = @('pack', 'navlyn/navlyn.csproj', '-c', 'Release', '-o', $PackageOutput)
if ($NoBuild) {
    $packArgs += '--no-build'
}
Invoke-Checked -Name 'pack navlyn' -FilePath 'dotnet' -Arguments $packArgs

$mcpPackArgs = @('pack', 'navlyn.Mcp/navlyn.Mcp.csproj', '-c', 'Release', '-o', $PackageOutput)
if ($NoBuild) {
    $mcpPackArgs += '--no-build'
}
Invoke-Checked -Name 'pack navlyn-mcp' -FilePath 'dotnet' -Arguments $mcpPackArgs

$navlynVersion = Get-ProjectVersion -ProjectPath (Join-Path $RepoRoot 'navlyn/navlyn.csproj')
$mcpVersion = Get-ProjectVersion -ProjectPath (Join-Path $RepoRoot 'navlyn.Mcp/navlyn.Mcp.csproj')

Invoke-Checked -Name 'install navlyn tool' -FilePath 'dotnet' -Arguments @('tool', 'install', 'navlyn', '--tool-path', $ToolPath, '--add-source', $PackageOutput, '--version', $navlynVersion)
Invoke-Checked -Name 'install navlyn-mcp tool' -FilePath 'dotnet' -Arguments @('tool', 'install', 'navlyn-mcp', '--tool-path', $ToolPath, '--add-source', $PackageOutput, '--version', $mcpVersion)

$navlyn = Get-ToolExecutable -Name 'navlyn'
$navlynMcp = Get-ToolExecutable -Name 'navlyn-mcp'
Invoke-Checked -Name 'navlyn help' -FilePath $navlyn -Arguments @('--help')
Invoke-Checked -Name 'navlyn check' -FilePath $navlyn -Arguments @('check', '--workspace', 'navlyn.slnx')
Invoke-Checked -Name 'navlyn repo-graph compact' -FilePath $navlyn -Arguments @('repo-graph', '--workspace', 'navlyn.slnx', '--profile', 'compact')
Invoke-Checked -Name 'navlyn-mcp help' -FilePath $navlynMcp -Arguments @('--help')

Write-Host 'Package install smoke passed.'

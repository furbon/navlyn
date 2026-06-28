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

function Write-McpFrame {
    param(
        [System.IO.StreamWriter]$Writer,
        [object]$Payload
    )

    $json = $Payload | ConvertTo-Json -Depth 50 -Compress
    $Writer.WriteLine($json)
    $Writer.Flush()
}

function Read-McpFrame {
    param(
        [System.IO.StreamReader]$Reader,
        [int]$TimeoutMilliseconds = 60000
    )

    $readTask = $Reader.ReadLineAsync()
    if (!$readTask.Wait($TimeoutMilliseconds)) {
        throw "Timed out waiting for MCP server response after $TimeoutMilliseconds ms."
    }

    $line = $readTask.GetAwaiter().GetResult()
    if ($null -eq $line) {
        throw 'MCP server closed stdout before writing a response.'
    }

    return $line
}

function Invoke-McpInstalledToolSmoke {
    param(
        [string]$McpExecutable,
        [string]$CliExecutable
    )

    Write-Host 'Running navlyn-mcp stdio smoke...'
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $McpExecutable
    $startInfo.WorkingDirectory = $RepoRoot
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.StandardInputEncoding = [System.Text.Encoding]::UTF8
    $startInfo.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $startInfo.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    $startInfo.UseShellExecute = $false
    foreach ($argument in @('--workspace', 'navlyn.slnx', '--navlyn-executable', $CliExecutable, '--working-directory', $RepoRoot, '--timeout-ms', '60000', '--max-json-chars', '4000000')) {
        [void]$startInfo.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    [void]$process.Start()
    $stderrTask = $process.StandardError.ReadToEndAsync()

    try {
        Write-McpFrame -Writer $process.StandardInput -Payload @{
            jsonrpc = '2.0'
            id = 1
            method = 'initialize'
            params = @{
                protocolVersion = '2025-06-18'
                capabilities = @{}
                clientInfo = @{
                    name = 'navlyn-package-smoke'
                    version = '0.1.0'
                }
            }
        }
        [void](Read-McpFrame -Reader $process.StandardOutput)
        Write-McpFrame -Writer $process.StandardInput -Payload @{
            jsonrpc = '2.0'
            method = 'notifications/initialized'
            params = @{}
        }

        Write-McpFrame -Writer $process.StandardInput -Payload @{
            jsonrpc = '2.0'
            id = 2
            method = 'tools/call'
            params = @{
                name = 'navlyn_workspace_summary'
                arguments = @{
                    relationshipLimit = 5
                    profile = 'compact'
                }
            }
        }

        $responseJson = Read-McpFrame -Reader $process.StandardOutput
        $response = $responseJson | ConvertFrom-Json -Depth 100
        if ($response.PSObject.Properties.Name -contains 'error' -and $null -ne $response.error) {
            throw "MCP tool call returned JSON-RPC error: $($response.error | ConvertTo-Json -Depth 20 -Compress)"
        }

        if (!($response.PSObject.Properties.Name -contains 'result')) {
            throw "MCP tool call response did not include a result: $responseJson"
        }

        if ($response.result.PSObject.Properties.Name -contains 'isError' -and $response.result.isError -eq $true) {
            throw "MCP tool call returned an error result: $responseJson"
        }

        $structured = $response.result.structuredContent
        if ($null -eq $structured -or $structured.ok -ne $true -or $structured.result.command -ne 'repo-graph') {
            throw "MCP structuredContent did not contain successful repo-graph output: $responseJson"
        }
    }
    finally {
        try {
            $process.StandardInput.Close()
        }
        catch {
        }

        if (!$process.WaitForExit(5000)) {
            try {
                $process.Kill($true)
            }
            catch {
            }
        }

        $stderr = ''
        try {
            $stderr = $stderrTask.GetAwaiter().GetResult()
        }
        catch {
        }

        if ($process.ExitCode -ne 0 -and ![string]::IsNullOrWhiteSpace($stderr)) {
            throw "navlyn-mcp stdio smoke exited with $($process.ExitCode). stderr:`n$stderr"
        }
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
Invoke-McpInstalledToolSmoke -McpExecutable $navlynMcp -CliExecutable $navlyn

Write-Host 'Package install smoke passed.'

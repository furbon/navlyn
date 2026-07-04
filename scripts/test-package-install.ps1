[CmdletBinding()]
param(
    [switch]$NoBuild,
    [string]$Output = 'artifacts/package-smoke/packages',
    [ValidateSet('net8.0', 'net10.0')]
    [string[]]$Frameworks = @('net8.0', 'net10.0')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$PackageOutput = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $Output))
$ToolRoot = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot 'artifacts/package-smoke'))
$TargetFrameworkScript = Join-Path $RepoRoot 'scripts/lib/navlyn-target-framework.ps1'

. $TargetFrameworkScript

function Get-ProjectVersion {
    param([string]$ProjectPath)

    [xml]$projectXml = Get-Content -Raw -LiteralPath $ProjectPath
    return [string]$projectXml.Project.PropertyGroup.Version
}

function Get-ProjectTargetFramework {
    param([string]$ProjectPath)

    return Get-NavlynPreferredTargetFramework -ProjectPath $ProjectPath
}

function Get-ToolExecutable {
    param(
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [string]$Root
    )

    $extension = if ($IsWindows) { '.exe' } else { '' }
    return Join-Path $Root "$Name$extension"
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

function Invoke-ToolInstall {
    param(
        [string]$Name,
        [string]$PackageId,
        [string]$ToolPath,
        [string]$Version,
        [string]$Framework
    )

    Invoke-Checked -Name $Name -FilePath 'dotnet' -Arguments @(
        'tool',
        'install',
        $PackageId,
        '--tool-path',
        $ToolPath,
        '--add-source',
        $PackageOutput,
        '--version',
        $Version,
        '--framework',
        $Framework)
}

function Invoke-InstalledToolSmoke {
    param(
        [string]$Framework,
        [string]$NavlynVersion,
        [string]$McpVersion
    )

    $frameworkDirectoryName = $Framework.Replace('.', '-')
    $navlynOnlyToolPath = [System.IO.Path]::GetFullPath((Join-Path $ToolRoot "tools-navlyn-only-$frameworkDirectoryName"))
    $mcpOnlyToolPath = [System.IO.Path]::GetFullPath((Join-Path $ToolRoot "tools-mcp-only-$frameworkDirectoryName"))
    $combinedToolPath = [System.IO.Path]::GetFullPath((Join-Path $ToolRoot "tools-$frameworkDirectoryName"))

    foreach ($path in @($navlynOnlyToolPath, $mcpOnlyToolPath, $combinedToolPath)) {
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Recurse -Force
        }

        [System.IO.Directory]::CreateDirectory($path) | Out-Null
    }

    Invoke-ToolInstall -Name "install navlyn-only tool $Framework" -PackageId 'navlyn' -ToolPath $navlynOnlyToolPath -Version $NavlynVersion -Framework $Framework
    $navlynOnly = Get-ToolExecutable -Name 'navlyn' -Root $navlynOnlyToolPath
    Invoke-Checked -Name "navlyn-only help $Framework" -FilePath $navlynOnly -Arguments @('--help')
    Invoke-Checked -Name "navlyn-only check $Framework" -FilePath $navlynOnly -Arguments @('check', '--workspace', 'navlyn.slnx')

    Invoke-ToolInstall -Name "install navlyn-mcp-only tool $Framework" -PackageId 'navlyn-mcp' -ToolPath $mcpOnlyToolPath -Version $McpVersion -Framework $Framework
    $mcpOnly = Get-ToolExecutable -Name 'navlyn-mcp' -Root $mcpOnlyToolPath
    Invoke-Checked -Name "navlyn-mcp-only help $Framework" -FilePath $mcpOnly -Arguments @('--help')
    Invoke-McpInstalledToolSmoke -McpExecutable $mcpOnly

    Invoke-ToolInstall -Name "install navlyn tool $Framework" -PackageId 'navlyn' -ToolPath $combinedToolPath -Version $NavlynVersion -Framework $Framework
    Invoke-ToolInstall -Name "install navlyn-mcp tool $Framework" -PackageId 'navlyn-mcp' -ToolPath $combinedToolPath -Version $McpVersion -Framework $Framework

    $navlyn = Get-ToolExecutable -Name 'navlyn' -Root $combinedToolPath
    $navlynMcp = Get-ToolExecutable -Name 'navlyn-mcp' -Root $combinedToolPath
    Invoke-Checked -Name "navlyn help $Framework" -FilePath $navlyn -Arguments @('--help')
    Invoke-Checked -Name "navlyn check $Framework" -FilePath $navlyn -Arguments @('check', '--workspace', 'navlyn.slnx')
    Invoke-Checked -Name "navlyn repo-graph compact $Framework" -FilePath $navlyn -Arguments @('repo-graph', '--workspace', 'navlyn.slnx', '--profile', 'compact')
    Invoke-Checked -Name "navlyn-mcp help $Framework" -FilePath $navlynMcp -Arguments @('--help')
    Invoke-McpInstalledToolSmoke -McpExecutable $navlynMcp
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
        [string]$McpExecutable
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
    foreach ($argument in @('--workspace', 'navlyn.slnx', '--working-directory', $RepoRoot, '--timeout-ms', '60000', '--max-json-chars', '4000000')) {
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
                    version = '0.5.0'
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
[System.IO.Directory]::CreateDirectory($ToolRoot) | Out-Null

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

foreach ($framework in $Frameworks) {
    Invoke-InstalledToolSmoke -Framework $framework -NavlynVersion $navlynVersion -McpVersion $mcpVersion
}

Write-Host "Package install smoke passed for navlyn-only, navlyn-mcp-only, and combined installs on: $($Frameworks -join ', ')."

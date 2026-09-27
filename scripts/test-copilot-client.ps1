[CmdletBinding()]
param(
    [string]$Manifest = 'artifacts/release-readiness-goal-20260926/p2-packages/navlyn-release-pack.json',
    [string]$ConsumerRoot = (Join-Path ([System.IO.Path]::GetTempPath()) "navlyn-copilot-consumer-$([guid]::NewGuid().ToString('N'))"),
    [Parameter(Mandatory = $true)]
    [string]$OutputReport,
    [switch]$RunPrompt,
    [string]$Model = 'gpt-5.6-luna',
    [ValidateSet('low', 'medium', 'high', 'xhigh', 'max')]
    [string]$ReasoningEffort = 'low'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$pathComparison = if ($IsWindows) { [System.StringComparison]::OrdinalIgnoreCase } else { [System.StringComparison]::Ordinal }

function Resolve-InputPath {
    param([string]$Path)
    if ([System.IO.Path]::IsPathRooted($Path)) { return [System.IO.Path]::GetFullPath($Path) }
    return [System.IO.Path]::GetFullPath((Join-Path $repoRoot $Path))
}

function Assert-NoReparsePoint {
    param([string]$Path)
    $current = [System.IO.Path]::GetFullPath($Path)
    while (![string]::IsNullOrWhiteSpace($current)) {
        $item = Get-Item -LiteralPath $current -Force -ErrorAction SilentlyContinue
        if ($null -ne $item -and ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint)) {
            throw 'A path contains a reparse point.'
        }
        $parent = Split-Path -Parent $current
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $current) { break }
        $current = $parent
    }
}

function Assert-NoReparsePointInTree {
    param([string]$Path)
    foreach ($child in @(Get-ChildItem -LiteralPath $Path -Force)) {
        Assert-NoReparsePoint $child.FullName
        if ($child.PSIsContainer) { Assert-NoReparsePointInTree $child.FullName }
    }
}

function Protect-Text {
    param([string]$Text)
    $safe = $Text -replace '(?i)(password|token|secret|api[_-]?key)(\s*[:=]\s*)[^\s,;]+', '$1$2[redacted]'
    $safe = $safe.Replace($repoRoot, '[repo]').Replace($script:rootPath, '[consumer-root]')
    $safe = $safe -replace '(?i)\b[A-Z]:\\[^\s"'']+', '[local-path]'
    $safe = $safe -replace '(?i)\\\\[^\s"'']+', '[network-path]'
    if ($safe.Length -gt 1200) { $safe = $safe.Substring(0, 1200) }
    return $safe
}

function Invoke-Process {
    param([string]$FilePath, [string[]]$Arguments, [string]$WorkingDirectory, [int]$TimeoutSeconds = 240)
    $info = [System.Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $FilePath
    $info.WorkingDirectory = $WorkingDirectory
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.Environment['COPILOT_HOME'] = (Join-Path $script:rootPath 'copilot-home')
    $info.Environment['COPILOT_CACHE_HOME'] = (Join-Path $script:rootPath 'copilot-cache')
    $info.Environment['DOTNET_CLI_HOME'] = (Join-Path $script:rootPath 'dotnet-home')
    $info.Environment['NUGET_PACKAGES'] = (Join-Path $script:rootPath 'nuget-packages')
    $info.Environment['NUGET_HTTP_CACHE_PATH'] = (Join-Path $script:rootPath 'nuget-http-cache')
    foreach ($argument in $Arguments) { [void]$info.ArgumentList.Add($argument) }
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $info
    [void]$process.Start()
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    if (!$process.WaitForExit($TimeoutSeconds * 1000)) {
        try { $process.Kill($true) } catch {}
        [void]$process.WaitForExit(5000)
        throw "Process exceeded its $TimeoutSeconds second timeout and was stopped."
    }
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    if ($process.ExitCode -ne 0) { throw "Process exited with code $($process.ExitCode)." }
    return [pscustomobject]@{ exitCode = $process.ExitCode; stdout = $stdout; stderr = $stderr }
}

function Write-Report {
    param([string]$Path, [object]$Value)
    $json = $Value | ConvertTo-Json -Depth 20
    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    try {
        $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes($json + "`n")
        $stream.Write($bytes, 0, $bytes.Length)
    }
    finally { $stream.Dispose() }
}

$manifestPath = Resolve-InputPath $Manifest
$reportPath = Resolve-InputPath $OutputReport
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts')).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
if (!$reportPath.StartsWith($artifactsRoot, $pathComparison)) { throw 'OutputReport must be beneath ignored artifacts/.' }
$reportDirectory = Split-Path -Parent $reportPath
if (!(Test-Path -LiteralPath $reportDirectory -PathType Container)) { throw 'OutputReport parent directory must already exist.' }
if (Test-Path -LiteralPath $reportPath) { throw 'OutputReport already exists; choose a unique path.' }
& git -C $repoRoot check-ignore -q -- $reportPath
if ($LASTEXITCODE -ne 0) { throw 'OutputReport must be ignored by Git.' }
Assert-NoReparsePoint $reportDirectory

if (!(Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'The package manifest was not found.' }
Assert-NoReparsePoint $manifestPath
$manifestDoc = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifestDoc.schemaVersion -cne 'navlyn.release-pack.v1' -or @($manifestDoc.packages).Count -ne 2) { throw 'The package manifest has an unsupported shape.' }
$package = @($manifestDoc.packages | Where-Object { $_.id -ceq 'navlyn-mcp' })
if ($package.Count -ne 1 -or $package[0].version -cne '0.8.0') { throw 'The manifest must identify exactly one locked navlyn-mcp 0.8.0 package.' }
$packagePath = Resolve-InputPath ([string]$package[0].path)
if (!(Test-Path -LiteralPath $packagePath -PathType Leaf)) { throw 'The manifest package file was not found.' }
Assert-NoReparsePoint $packagePath
$packageHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($packageHash -cne ([string]$package[0].sha256).ToLowerInvariant()) { throw 'The navlyn-mcp package hash does not match the manifest.' }
if ([System.IO.Path]::GetFileName($packagePath) -cne "navlyn-mcp.$($package[0].version).nupkg") { throw 'The package filename does not match its manifest identity.' }

$script:rootPath = [System.IO.Path]::GetFullPath($ConsumerRoot)
if (Test-Path -LiteralPath $script:rootPath) { throw 'ConsumerRoot must be a new, unique directory.' }
$tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
if (!$script:rootPath.StartsWith($tempRoot, $pathComparison)) { throw 'ConsumerRoot must be beneath the system temporary directory.' }
$rootParent = Split-Path -Parent $script:rootPath
if (!(Test-Path -LiteralPath $rootParent -PathType Container)) { throw 'ConsumerRoot parent directory does not exist.' }
Assert-NoReparsePoint $rootParent

$report = [ordered]@{
    schemaVersion = 'navlyn.copilot-client.v1'
    startedUtc = [DateTimeOffset]::UtcNow.ToString('o')
    status = 'running'
    package = [ordered]@{ id = 'navlyn-mcp'; version = [string]$package[0].version; sha256 = $packageHash }
    client = [ordered]@{ name = 'GitHub Copilot CLI'; version = $null; promptMode = [bool]$RunPrompt; model = if ($RunPrompt) { $Model } else { $null }; reasoningEffort = if ($RunPrompt) { $ReasoningEffort } else { $null } }
    mcp = [ordered]@{ configFormat = 'project .mcp.json / mcpServers'; startupObserved = $false; semanticTool = if ($RunPrompt) { 'navlyn_target' } else { $null }; semanticCallObserved = $false; semanticResultObserved = $false; eventTypes = @(); startedTools = @(); responseSummary = $null }
    cleanup = [ordered]@{ status = 'pending'; retained = $false }
    error = $null
}
$rootOwned = $false
$cleanupMarker = '.navlyn-copilot-owner.json'
try {
    [System.IO.Directory]::CreateDirectory($script:rootPath) | Out-Null
    if (@(Get-ChildItem -LiteralPath $script:rootPath -Force).Count -ne 0) { throw 'ConsumerRoot was populated during setup; refusing ownership.' }
    $marker = [ordered]@{ schemaVersion = 'navlyn-copilot-owner.v1'; root = $script:rootPath; createdUtc = [DateTimeOffset]::UtcNow.ToString('o') } | ConvertTo-Json
    [System.IO.File]::WriteAllText((Join-Path $script:rootPath $cleanupMarker), $marker, [System.Text.UTF8Encoding]::new($false))
    $rootOwned = $true

    $feedPath = Join-Path $script:rootPath 'feed'
    $toolPath = Join-Path $script:rootPath 'tools'
    $workspacePath = Join-Path $script:rootPath 'workspace'
    foreach ($directory in @($feedPath, $toolPath, $workspacePath)) { [System.IO.Directory]::CreateDirectory($directory) | Out-Null }
    $localPackage = Join-Path $feedPath ([System.IO.Path]::GetFileName($packagePath))
    Copy-Item -LiteralPath $packagePath -Destination $localPackage
    if ((Get-FileHash -LiteralPath $localPackage -Algorithm SHA256).Hash.ToLowerInvariant() -cne $packageHash) { throw 'The copied package failed its SHA-256 check.' }
    $projectPath = Join-Path $workspacePath 'ConsumerWorkspace.csproj'
    @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
'@ | Set-Content -LiteralPath $projectPath -Encoding utf8
    'namespace CopilotConsumerFixture; public sealed class ConsumerProbe { public string Name => "consumer"; }' | Set-Content -LiteralPath (Join-Path $workspacePath 'ConsumerProbe.cs') -Encoding utf8
    [void](Invoke-Process -FilePath 'dotnet' -Arguments @('tool', 'install', 'navlyn-mcp', '--tool-path', $toolPath, '--source', $feedPath, '--version', [string]$package[0].version, '--framework', 'net8.0', '--verbosity', 'quiet') -WorkingDirectory $workspacePath)
    $mcpExecutable = Join-Path $toolPath 'navlyn-mcp.exe'
    if (!(Test-Path -LiteralPath $mcpExecutable -PathType Leaf)) { throw 'The isolated tool install did not produce navlyn-mcp.exe.' }
    $mcpArgs = @('--workspace', $projectPath, '--working-directory', $workspacePath, '--timeout-ms', '60000', '--max-json-chars', '4000000')
    $mcpConfig = [ordered]@{ mcpServers = [ordered]@{ navlyn = [ordered]@{ type = 'local'; command = $mcpExecutable; args = $mcpArgs; tools = @('navlyn_target') } } }
    $configPath = Join-Path $workspacePath '.mcp.json'
    [System.IO.File]::WriteAllText($configPath, ($mcpConfig | ConvertTo-Json -Depth 10), [System.Text.UTF8Encoding]::new($false))

    $client = Invoke-Process -FilePath 'copilot.cmd' -Arguments @('--version') -WorkingDirectory $workspacePath -TimeoutSeconds 30
    $versionText = Protect-Text ($client.stdout + ' ' + $client.stderr)
    if ($versionText -notmatch '(?i)copilot') { throw 'The Copilot CLI did not return a recognizable version.' }
    $report.client.version = ($versionText -replace '[\r\n]+', ' ').Trim()

    if ($RunPrompt) {
        $prompt = 'Use the navlyn_target MCP tool exactly once with query ConsumerProbe, assumeKind NamedType, and limit 1. Report only the selected symbol name. Do not use shell or edit files.'
        $arguments = @('--no-auto-update', '--no-remote', '--no-remote-export', '--no-ask-user', '--disable-builtin-mcps', '--allow-all-tools', '--deny-tool', 'shell,write,url,memory', '--model', $Model, '--reasoning-effort', $ReasoningEffort, '--max-ai-credits', '30', '--additional-mcp-config', "@$configPath", '--output-format', 'json', '-p', $prompt)
        $result = Invoke-Process -FilePath 'copilot.cmd' -Arguments $arguments -WorkingDirectory $workspacePath -TimeoutSeconds 180
        $report.mcp.clientExitCode = $result.exitCode
        $report.mcp.stderrBytes = [System.Text.Encoding]::UTF8.GetByteCount($result.stderr)
        $startedCalls = @{}
        $eventTypes = [System.Collections.Generic.HashSet[string]]::new()
        $startedTools = [System.Collections.Generic.List[string]]::new()
        foreach ($line in @($result.stdout -split "`r?`n" | Where-Object { $_.TrimStart().StartsWith('{') })) {
            try { $event = $line | ConvertFrom-Json -Depth 50 } catch { continue }
            [void]$eventTypes.Add([string]$event.type)
            if ($event.type -eq 'tool.execution_start') { [void]$startedTools.Add([string]$event.data.toolName) }
            if ($event.type -eq 'assistant.message' -and $event.data.content) { $report.mcp.responseSummary = Protect-Text ([string]$event.data.content) }
            if ($event.type -eq 'tool.execution_start' -and $event.data.mcpServerName -eq 'navlyn' -and $event.data.mcpToolName -eq 'navlyn_target') {
                $startedCalls[[string]$event.data.toolCallId] = $true
            }
            if ($event.type -eq 'tool.execution_complete' -and $startedCalls.ContainsKey([string]$event.data.toolCallId) -and $event.data.success -eq $true) {
                $report.mcp.semanticCallObserved = $true
                $resultText = [string]$event.data.result.content
                $report.mcp.semanticResultObserved = $resultText.Contains('ConsumerProbe')
            }
        }
        $report.mcp.eventTypes = @($eventTypes | Sort-Object)
        $report.mcp.startedTools = $startedTools.ToArray()
        $report.mcp.startupObserved = [bool]$report.mcp.semanticCallObserved
        if (!$report.mcp.semanticCallObserved -or !$report.mcp.semanticResultObserved) { throw 'Copilot did not return an observed successful Navlyn semantic result.' }
    }
    $report.status = if ($RunPrompt) { 'passed' } else { 'prepared' }
}
catch {
    $report.status = 'failed'
    $report.error = Protect-Text $_.Exception.Message
}
finally {
    $report.completedUtc = [DateTimeOffset]::UtcNow.ToString('o')
    if ($rootOwned) {
        try {
            $actualRoot = [System.IO.Path]::GetFullPath($script:rootPath)
            Assert-NoReparsePoint $actualRoot
            if ($actualRoot -cne $script:rootPath) { throw 'ConsumerRoot identity changed before cleanup.' }
            $markerPath = Join-Path $actualRoot $cleanupMarker
            if (!(Test-Path -LiteralPath $markerPath -PathType Leaf)) { throw 'The owner marker is missing.' }
            Assert-NoReparsePoint $markerPath
            $owner = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
            if ([System.IO.Path]::GetFullPath([string]$owner.root) -cne $actualRoot) { throw 'The owner marker does not match ConsumerRoot.' }
            foreach ($entry in @(Get-ChildItem -LiteralPath $actualRoot -Force)) {
                Assert-NoReparsePoint $entry.FullName
                if ($entry.PSIsContainer) {
                    Assert-NoReparsePointInTree $entry.FullName
                    Remove-Item -LiteralPath $entry.FullName -Recurse -Force
                }
                else { Remove-Item -LiteralPath $entry.FullName -Force }
            }
            if (@(Get-ChildItem -LiteralPath $actualRoot -Force).Count -ne 0) { throw 'ConsumerRoot was not empty after cleanup.' }
            Remove-Item -LiteralPath $actualRoot -Force
            $report.cleanup.status = 'passed'
        }
        catch {
            $report.cleanup.status = 'failed'
            $report.error = 'Owned temporary workspace cleanup failed.'
            $report.status = 'failed'
        }
    }
    elseif (Test-Path -LiteralPath $script:rootPath) { $report.cleanup.status = 'unclaimed-root-retained' }
    else { $report.cleanup.status = 'not-created' }
    $report.completedUtc = [DateTimeOffset]::UtcNow.ToString('o')
    try { Write-Report -Path $reportPath -Value $report }
    catch { $report.status = 'failed'; $report.error = 'Could not write the sanitized report.' }
}

$report | ConvertTo-Json -Depth 20
if ($report.status -eq 'failed' -or $report.cleanup.status -eq 'failed') { exit 1 }

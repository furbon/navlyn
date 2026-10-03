[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Manifest,
    [string]$RollbackManifest,
    [string]$ConsumerRoot = (Join-Path ([System.IO.Path]::GetTempPath()) "navlyn-consumer-$([guid]::NewGuid().ToString('N'))"),
    [Parameter(Mandatory = $true)]
    [string]$OutputReport,
    [switch]$KeepArtifacts,
    [ValidateSet('net8.0', 'net10.0')]
    [string[]]$Frameworks = @('net8.0', 'net10.0')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$RepoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$PathComparison = if ($IsWindows) { [System.StringComparison]::OrdinalIgnoreCase } else { [System.StringComparison]::Ordinal }
$RequiredTools = @(
    'navlyn_target', 'navlyn_read', 'navlyn_file_outline', 'navlyn_navigate', 'navlyn_prepare_edit',
    'navlyn_verify_edit', 'navlyn_review', 'navlyn_workspace_summary', 'navlyn_workspace_status',
    'navlyn_workspace_refresh', 'navlyn_doctor', 'navlyn_impact', 'navlyn_context_pack',
    'navlyn_entrypoints', 'navlyn_tests_for_symbol', 'navlyn_tests_for_diff', 'navlyn_diagnostics',
    'navlyn_di', 'navlyn_public_api_diff', 'navlyn_routes', 'navlyn_options', 'navlyn_messages',
    'navlyn_ef', 'navlyn_packages', 'navlyn_batch'
)

function Resolve-InputPath {
    param([string]$Path)
    if ([System.IO.Path]::IsPathRooted($Path)) { return [System.IO.Path]::GetFullPath($Path) }
    return [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $Path))
}

function Assert-NotReparsePoint {
    param([string]$Path)
    $current = [System.IO.Path]::GetFullPath($Path)
    while (![string]::IsNullOrWhiteSpace($current)) {
        $item = Get-Item -LiteralPath $current -Force -ErrorAction SilentlyContinue
        if ($null -ne $item -and ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint)) {
            throw 'A supplied path contains a reparse point.'
        }
        $parent = Split-Path -Parent $current
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $current) { break }
        $current = $parent
    }
}

function Assert-UnderRoot {
    param([string]$Path, [string]$Root)
    $full = [System.IO.Path]::GetFullPath($Path).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $base = [System.IO.Path]::GetFullPath($Root).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (!$full.StartsWith($base, $PathComparison)) { throw 'A cleanup path escaped the consumer root.' }
    return $full
}

function Assert-NoReparsePointInTree {
    param([string]$Path)
    foreach ($child in @(Get-ChildItem -LiteralPath $Path -Force)) {
        Assert-NotReparsePoint -Path $child.FullName
        if ($child.PSIsContainer) { Assert-NoReparsePointInTree -Path $child.FullName }
    }
}

function Get-ConsumerPath {
    $separator = [System.IO.Path]::PathSeparator
    $entries = @($env:PATH -split [regex]::Escape([string]$separator) | Where-Object {
        if ([string]::IsNullOrWhiteSpace($_)) { return $false }
        $entry = [System.IO.Path]::GetFullPath($_)
        if (!$entry.StartsWith($RepoRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar, $PathComparison)) { return $true }
        return $entry -notmatch '(?i)[\\/](bin|obj)(?:[\\/]|$)'
    })
    return ($entries -join $separator)
}

function Get-ManifestPackages {
    param([string]$Path)
    $fullPath = Resolve-InputPath -Path $Path
    if (!(Test-Path -LiteralPath $fullPath -PathType Leaf)) { throw 'A package manifest was not found.' }
    Assert-NotReparsePoint -Path $fullPath
    $json = Get-Content -LiteralPath $fullPath -Raw | ConvertFrom-Json
    if ($json.schemaVersion -ne 'navlyn.release-pack.v1' -or @($json.packages).Count -ne 2) { throw 'A package manifest has an unsupported shape.' }
    $result = @{}
    foreach ($package in $json.packages) {
        if ($package.id -notin @('navlyn', 'navlyn-mcp') -or $result.ContainsKey([string]$package.id)) { throw 'A package manifest has invalid package IDs.' }
        $packagePath = Resolve-InputPath -Path ([string]$package.path)
        if (!(Test-Path -LiteralPath $packagePath -PathType Leaf)) { throw 'A manifest package file was not found.' }
        Assert-NotReparsePoint -Path $packagePath
        if ([System.IO.Path]::GetFileName($packagePath) -cne "$($package.id).$($package.version).nupkg") { throw 'A manifest package filename does not match its package identity.' }
        $actualHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -ne ([string]$package.sha256).ToLowerInvariant()) { throw "Manifest package hash mismatch for $($package.id)." }
        $result[[string]$package.id] = [pscustomobject]@{ id = [string]$package.id; version = [string]$package.version; path = $packagePath; sha256 = $actualHash }
    }
    if (!$result.ContainsKey('navlyn') -or !$result.ContainsKey('navlyn-mcp')) { throw 'A package manifest is missing a required package.' }
    if ($result.navlyn.version -ne $result['navlyn-mcp'].version) { throw 'Tool package versions do not match. Pack or install both tools at the same release version, then regenerate the manifest.' }
    return $result
}

function Protect-ReportText {
    param([string]$Text)
    $safe = $Text -replace '(?i)(password|token|secret|api[_-]?key)(\s*[:=]\s*)[^\s,;]+', '$1$2[redacted]'
    $safe = $safe.Replace($RepoRoot, '[repo]').Replace($script:RootPath, '[consumer-root]').Replace($script:HomePath, '[isolated-home]')
    $safe = $safe -replace '(?i)\b[A-Z]:\\[^\s"'']+', '[local-path]'
    $safe = $safe -replace '(?i)\\\\[^\s"'']+', '[network-path]'
    $safe = $safe -replace '(?i)(?<![\w:])/(?:home|Users|tmp|private/tmp|workspace)/[^\s"'']+', '[local-path]'
    if ($safe.Length -gt 2000) { $safe = $safe.Substring(0, 2000) }
    return $safe
}

function Invoke-Process {
    param(
        [string]$Name,
        [string]$FilePath,
        [string[]]$Arguments,
        [string]$WorkingDirectory,
        [switch]$JsonOutput,
        [switch]$Interactive,
        [int]$TimeoutSeconds = 120
    )
    $started = [DateTimeOffset]::UtcNow
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $info = [System.Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $FilePath
    $info.WorkingDirectory = $WorkingDirectory
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.RedirectStandardInput = [bool]$Interactive
    if ($Interactive) { $info.StandardInputEncoding = [System.Text.Encoding]::UTF8 }
    $info.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $info.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    foreach ($arg in $Arguments) { [void]$info.ArgumentList.Add($arg) }
    $info.Environment['HOME'] = $script:HomePath
    $info.Environment['USERPROFILE'] = $script:HomePath
    $info.Environment['APPDATA'] = Join-Path $script:HomePath 'AppData/Roaming'
    $info.Environment['LOCALAPPDATA'] = Join-Path $script:HomePath 'AppData/Local'
    $info.Environment['DOTNET_CLI_HOME'] = Join-Path $script:RootPath 'isolated-home/dotnet-cli'
    $info.Environment['NUGET_PACKAGES'] = Join-Path $script:RootPath 'isolated-home/nuget-packages'
    $info.Environment['TEMP'] = Join-Path $script:RootPath 'transient-reports'
    $info.Environment['TMP'] = Join-Path $script:RootPath 'transient-reports'
    $info.Environment['DOTNET_NOLOGO'] = '1'
    $info.Environment['DOTNET_CLI_TELEMETRY_OPTOUT'] = '1'
    $info.Environment['PATH'] = Get-ConsumerPath
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $info
    [void]$process.Start()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $stdoutTask = if ($Interactive) { $null } else { $process.StandardOutput.ReadToEndAsync() }
    $record = [ordered]@{ name = $Name; startedUtc = $started.ToString('o'); durationMs = 0; exitCode = $null; status = 'running' }
    $script:CurrentOperation = $Name
    $stdout = ''
    $parsedJson = $null
    try {
        if ($Interactive) {
            Invoke-McpExchange -Process $process -StderrTask $stderrTask
            if (!$process.WaitForExit(10000)) { try { $process.Kill($true) } catch {}; throw "Process '$Name' did not exit after MCP shutdown." }
        }
        else {
            if (!$process.WaitForExit($TimeoutSeconds * 1000)) {
                try { $process.Kill($true) } catch {}
                [void]$process.WaitForExit(5000)
                throw "Process '$Name' exceeded its $TimeoutSeconds second timeout and was stopped."
            }
            $stdout = $stdoutTask.GetAwaiter().GetResult()
            $stderr = $stderrTask.GetAwaiter().GetResult()
            $record.stdoutBytes = [System.Text.Encoding]::UTF8.GetByteCount($stdout)
            $record.stderrBytes = [System.Text.Encoding]::UTF8.GetByteCount($stderr)
            if ($process.ExitCode -ne 0) { throw "Process '$Name' exited with code $($process.ExitCode). $(Protect-ReportText -Text $stderr)" }
            if ($JsonOutput) {
                try { $parsedJson = $stdout | ConvertFrom-Json -Depth 100 }
                catch { throw "Process '$Name' did not produce valid JSON on stdout." }
                if ($parsedJson.PSObject.Properties.Name -contains 'ok' -and $parsedJson.ok -eq $false) { throw "Process '$Name' reported ok=false." }
                if (![string]::IsNullOrWhiteSpace($stderr)) { throw "Process '$Name' wrote diagnostics to stderr: $(Protect-ReportText -Text $stderr)" }
            }
            elseif (![string]::IsNullOrWhiteSpace($stderr)) { $record.stderrSummary = Protect-ReportText -Text $stderr }
        }
        $record.exitCode = $process.ExitCode
        $record.status = 'passed'
    }
    catch {
        $record.status = 'failed'
        if ($process.HasExited) { $record.exitCode = $process.ExitCode }
        $record.error = Protect-ReportText -Text $_.Exception.Message
        throw
    }
    finally {
        $watch.Stop()
        $record.durationMs = $watch.ElapsedMilliseconds
        if (!$process.HasExited) { try { $process.Kill($true) } catch {} }
        $script:LastProcessRecord = [pscustomobject]$record
    }
    return [pscustomobject]@{ record = [pscustomobject]$record; json = $parsedJson; stdout = if (!$JsonOutput) { $stdout } else { $null } }
}

function Write-McpMessage {
    param([System.Diagnostics.Process]$Process, [object]$Payload)
    $line = $Payload | ConvertTo-Json -Depth 60 -Compress
    $Process.StandardInput.WriteLine($line)
    $Process.StandardInput.Flush()
}

function Read-McpMessage {
    param([System.Diagnostics.Process]$Process, [int]$TimeoutMilliseconds = 60000)
    $task = $Process.StandardOutput.ReadLineAsync()
    if (!$task.Wait($TimeoutMilliseconds)) { throw 'MCP response timed out.' }
    $line = $task.GetAwaiter().GetResult()
    if ($null -eq $line) { throw 'MCP server closed stdout before a response.' }
    try { return $line | ConvertFrom-Json -Depth 100 }
    catch { throw 'MCP stdout contained a non-JSON protocol line.' }
}

function Invoke-McpExchange {
    param([System.Diagnostics.Process]$Process, [System.Threading.Tasks.Task[string]]$StderrTask)
    $workspaceProject = Join-Path $script:WorkspacePath 'ConsumerWorkspace.csproj'
    Write-McpMessage -Process $Process -Payload @{ jsonrpc = '2.0'; id = 1; method = 'initialize'; params = @{ protocolVersion = '2025-06-18'; capabilities = @{}; clientInfo = @{ name = 'navlyn-consumer-install'; version = '1.0' } } }
    $initialize = Read-McpMessage -Process $Process
    if ($initialize.id -ne 1 -or $null -eq $initialize.result -or $initialize.result.protocolVersion -ne '2025-06-18') { throw 'MCP initialize response failed the protocol check.' }
    Write-McpMessage -Process $Process -Payload @{ jsonrpc = '2.0'; method = 'notifications/initialized'; params = @{} }
    Write-McpMessage -Process $Process -Payload @{ jsonrpc = '2.0'; id = 2; method = 'tools/list'; params = @{} }
    $listing = Read-McpMessage -Process $Process
    if ($listing.id -ne 2 -or $null -eq $listing.result.tools) { throw 'MCP tools/list response was malformed.' }
    $actualNames = @($listing.result.tools | ForEach-Object { [string]$_.name })
    if (($actualNames -join "`n") -cne ($script:RequiredTools -join "`n")) { throw 'MCP tools/list did not match the locked 25-tool order.' }
    Write-McpMessage -Process $Process -Payload @{ jsonrpc = '2.0'; id = 3; method = 'tools/call'; params = @{ name = 'navlyn_target'; arguments = @{ query = 'ConsumerProbe'; assumeKind = 'NamedType'; limit = 3 } } }
    $call = Read-McpMessage -Process $Process
    if ($call.jsonrpc -ne '2.0' -or $call.id -ne 3 -or $null -eq $call.result -or $call.result.isError -eq $true -or $null -eq $call.result.structuredContent) { throw 'MCP semantic tool call did not return successful structured content.' }
    if ($call.result.structuredContent.ok -ne $true -or $call.result.structuredContent.result.selectedTarget.name -ne 'ConsumerProbe') { throw 'MCP semantic tool call did not select the consumer fixture symbol.' }
    if ($actualNames.Count -ne 25) { throw 'MCP tool count differed from 25.' }
    $trailingOutputTask = $Process.StandardOutput.ReadLineAsync()
    $Process.StandardInput.Close()
    if (!$Process.WaitForExit(5000)) { $Process.Kill($true); throw 'MCP server did not stop after stdin closed.' }
    $trailingOutput = $trailingOutputTask.GetAwaiter().GetResult()
    if ($null -ne $trailingOutput) { throw 'MCP server wrote an unexpected stdout line outside the request/response exchange.' }
    $stderr = $StderrTask.GetAwaiter().GetResult()
    if ($stderr.Length -gt 16000) { throw 'MCP server stderr exceeded the bounded 16 KiB limit.' }
    if ($Process.ExitCode -ne 0) { throw "MCP server exited with code $($Process.ExitCode). $(Protect-ReportText -Text $stderr)" }
    $script:McpResult = [pscustomobject]@{ initialized = $true; toolCount = $actualNames.Count; toolNames = $actualNames; semanticCall = 'navlyn_target'; stderrBytes = [System.Text.Encoding]::UTF8.GetByteCount($stderr) }
}

function Invoke-McpTool {
    param([string]$Executable, [string]$WorkingDirectory)
    $args = @('--workspace', (Join-Path $script:WorkspacePath 'ConsumerWorkspace.csproj'), '--working-directory', $script:WorkspacePath, '--timeout-ms', '60000', '--max-json-chars', '4000000')
    return Invoke-Process -Name 'mcp-stdio-contract' -FilePath $Executable -Arguments $args -WorkingDirectory $WorkingDirectory -Interactive -TimeoutSeconds 200
}

function Get-ToolPath {
    param([string]$Name, [string]$Directory)
    $extension = if ($IsWindows) { '.exe' } else { '' }
    return Join-Path $Directory "$Name$extension"
}

function Invoke-ToolInstall {
    param([string]$PackageId, [string]$ToolPath, [object]$Package, [string]$Framework, [string]$Feed)
    return Invoke-Process -Name "install-$PackageId-$Framework" -FilePath 'dotnet' -Arguments @('tool', 'install', $PackageId, '--tool-path', $ToolPath, '--source', $Feed, '--version', $Package.version, '--framework', $Framework, '--verbosity', 'quiet') -WorkingDirectory $script:WorkspacePath -TimeoutSeconds 240
}

function Invoke-ToolUninstall {
    param([string]$PackageId, [string]$ToolPath, [string]$Framework)
    return Invoke-Process -Name "uninstall-$PackageId-$Framework" -FilePath 'dotnet' -Arguments @('tool', 'uninstall', $PackageId, '--tool-path', $ToolPath) -WorkingDirectory $script:WorkspacePath
}

function Assert-ToolVersion {
    param([string]$PackageId, [string]$ToolPath, [string]$Version, [string]$Framework, [string]$Stage)
    $listing = Invoke-Process -Name "tool-list-$Stage-$PackageId-$Framework" -FilePath 'dotnet' -Arguments @('tool', 'list', '--tool-path', $ToolPath) -WorkingDirectory $script:WorkspacePath
    $escapedId = [regex]::Escape($PackageId)
    $escapedVersion = [regex]::Escape($Version)
    if ($listing.stdout -notmatch "(?im)^\s*$escapedId\s+$escapedVersion(?:\s|$)") { throw "Tool '$PackageId' did not report expected version '$Version' after $Stage." }
}

function Assert-CommandRemoved {
    param([string]$Name, [string]$Directory)
    if (Test-Path -LiteralPath (Get-ToolPath -Name $Name -Directory $Directory)) { throw "Uninstall left command '$Name' behind." }
}

function Write-JsonAtomically {
    param([string]$Path, [object]$Value, [switch]$Overwrite)
    $json = $Value | ConvertTo-Json -Depth 40
    $mode = if ($Overwrite) { [System.IO.FileMode]::Create } else { [System.IO.FileMode]::CreateNew }
    $stream = [System.IO.File]::Open($Path, $mode, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    try {
        $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes($json + "`n")
        $stream.Write($bytes, 0, $bytes.Length)
    }
    finally { $stream.Dispose() }
}

$reportPath = Resolve-InputPath -Path $OutputReport
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot 'artifacts'))
if (!$reportPath.StartsWith($artifactsRoot + [System.IO.Path]::DirectorySeparatorChar, $PathComparison)) { throw 'OutputReport must be under ignored artifacts/.' }
$reportDirectory = Split-Path -Parent $reportPath
if (!(Test-Path -LiteralPath $reportDirectory -PathType Container)) { throw 'OutputReport parent directory must already exist.' }
$ignored = & git -C $RepoRoot check-ignore -q -- $reportPath
if ($LASTEXITCODE -ne 0) { throw 'OutputReport must be ignored by Git.' }
Assert-NotReparsePoint -Path $reportDirectory
if (Test-Path -LiteralPath $reportPath) { throw 'OutputReport already exists; choose a unique report path.' }
$failurePath = [System.IO.Path]::ChangeExtension($reportPath, '.first-failure.json')
if (Test-Path -LiteralPath $failurePath) { throw 'First-failure evidence path already exists; choose a unique report path.' }
$failureIgnored = & git -C $RepoRoot check-ignore -q -- $failurePath
if ($LASTEXITCODE -ne 0) { throw 'First-failure evidence must be ignored by Git.' }

$script:RootPath = [System.IO.Path]::GetFullPath($ConsumerRoot)
if (Test-Path -LiteralPath $script:RootPath) { throw 'ConsumerRoot must be a new, unique directory that does not already exist.' }
$tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (!$script:RootPath.StartsWith($tempRoot, $PathComparison)) { throw 'ConsumerRoot must be beneath the current temporary directory.' }
$parentPath = Split-Path -Parent $script:RootPath
if (!(Test-Path -LiteralPath $parentPath -PathType Container)) { throw 'ConsumerRoot parent directory must already exist.' }
Assert-NotReparsePoint -Path $parentPath
$script:HomePath = Join-Path $script:RootPath 'isolated-home'
$script:WorkspacePath = Join-Path $script:RootPath 'consumer-workspace'
$markerName = '.navlyn-consumer-install-owner.json'
$packages = Get-ManifestPackages -Path $Manifest
$rollbackPackages = if ([string]::IsNullOrWhiteSpace($RollbackManifest)) { $null } else { Get-ManifestPackages -Path $RollbackManifest }
if ($packages.navlyn.version -cne '0.8.3') { throw 'Current package manifest must identify version 0.8.3.' }
if ($null -ne $rollbackPackages -and ($rollbackPackages.navlyn.version -eq $packages.navlyn.version -or $rollbackPackages['navlyn-mcp'].version -ne $rollbackPackages.navlyn.version)) { throw 'Rollback manifest must contain a different synchronized package version.' }

$report = [ordered]@{
    schemaVersion = 'navlyn.consumer-install.v1'
    startedUtc = [DateTimeOffset]::UtcNow.ToString('o')
    status = 'running'
    root = '[task-owned-temporary-root]'
    manifestVersion = $packages.navlyn.version
    packages = @(
        [pscustomobject]@{ id = 'navlyn'; version = $packages.navlyn.version; sha256 = $packages.navlyn.sha256 },
        [pscustomobject]@{ id = 'navlyn-mcp'; version = $packages['navlyn-mcp'].version; sha256 = $packages['navlyn-mcp'].sha256 }
    )
    execution = [ordered]@{ workingDirectory = 'consumer-workspace'; toolInstallMode = '--tool-path'; globalToolsUsed = $false; repositoryBinObjPathEntriesRemoved = $true }
    restoreDurationMs = $null
    firstQueryDurationMs = $null
    setupDurationMs = $null
    frameworks = @($Frameworks)
    cases = [System.Collections.Generic.List[object]]::new()
    updateRollback = [ordered]@{ status = 'not-applicable'; reason = 'No prior-version package manifest was supplied.' }
    firstFailure = $null
    cleanup = [ordered]@{ status = 'pending'; retained = [bool]$KeepArtifacts }
    preCleanupReportSha256 = $null
    preCleanupFailureEvidenceSha256 = $null
}
$failure = $null
$rootOwned = $false
$activeCase = $null
$script:LastProcessRecord = $null
$script:CurrentOperation = 'setup'
$script:PreCleanupReportHash = $null
$script:PreCleanupFailureEvidenceHash = $null
$setupWatch = [System.Diagnostics.Stopwatch]::StartNew()
try {
    [System.IO.Directory]::CreateDirectory($script:RootPath) | Out-Null
    if (@(Get-ChildItem -LiteralPath $script:RootPath -Force).Count -ne 0) { throw 'ConsumerRoot was populated during setup; refusing to claim or remove it.' }
    $markerPath = Join-Path $script:RootPath $markerName
    $markerJson = [pscustomobject]@{ schemaVersion = 'navlyn-consumer-owner.v1'; root = $script:RootPath; createdUtc = [DateTimeOffset]::UtcNow.ToString('o') } | ConvertTo-Json
    $markerStream = [System.IO.File]::Open($markerPath, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    try {
        $markerBytes = [System.Text.UTF8Encoding]::new($false).GetBytes($markerJson + "`n")
        $markerStream.Write($markerBytes, 0, $markerBytes.Length)
    }
    finally { $markerStream.Dispose() }
    $rootOwned = $true
    $dirs = @('nuget-source', 'tools-cli-only', 'tools-mcp-only', 'tools-combined', 'consumer-workspace', 'isolated-home', 'transient-reports')
    foreach ($dir in $dirs) { [System.IO.Directory]::CreateDirectory((Assert-UnderRoot -Path (Join-Path $script:RootPath $dir) -Root $script:RootPath)) | Out-Null }
    foreach ($package in $packages.Values) { Copy-Item -LiteralPath $package.path -Destination (Join-Path $script:RootPath "nuget-source/$($package.id).$($package.version).nupkg") }
    if ($null -ne $rollbackPackages) { foreach ($package in $rollbackPackages.Values) { Copy-Item -LiteralPath $package.path -Destination (Join-Path $script:RootPath "nuget-source/$($package.id).$($package.version).nupkg") } }
    $feedPath = Join-Path $script:RootPath 'nuget-source'
    $projectPath = Join-Path $script:WorkspacePath 'ConsumerWorkspace.csproj'
    @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
'@ | Set-Content -LiteralPath $projectPath -Encoding utf8
    'namespace ConsumerFixture; public sealed class ConsumerProbe { public string Name => "consumer"; }' | Set-Content -LiteralPath (Join-Path $script:WorkspacePath 'ConsumerProbe.cs') -Encoding utf8
    foreach ($isolatedPath in @('isolated-home/dotnet-cli', 'isolated-home/nuget-packages', 'isolated-home/AppData/Roaming', 'isolated-home/AppData/Local')) {
        [System.IO.Directory]::CreateDirectory((Join-Path $script:RootPath $isolatedPath)) | Out-Null
    }
    $setupWatch.Stop()
    $report.setupDurationMs = $setupWatch.ElapsedMilliseconds
    $script:CurrentOperation = 'restore-consumer-workspace'
    $restore = Invoke-Process -Name 'restore-consumer-workspace' -FilePath 'dotnet' -Arguments @('restore', $projectPath, '--source', $feedPath, '--ignore-failed-sources', '--verbosity', 'quiet') -WorkingDirectory $script:WorkspacePath -TimeoutSeconds 240
    $report.restoreDurationMs = $restore.record.durationMs
    $report.restore = $restore.record

    foreach ($framework in $Frameworks) {
        $suffix = $framework.Replace('.', '-')
        foreach ($shape in @('cli-only', 'mcp-only', 'combined')) {
            $toolDirectory = Join-Path $script:RootPath "tools-$shape/$suffix"
            [System.IO.Directory]::CreateDirectory($toolDirectory) | Out-Null
            $case = [ordered]@{ framework = $framework; shape = $shape; installDurationMs = 0; queries = [System.Collections.Generic.List[object]]::new(); uninstall = @{}; status = 'running' }
            $activeCase = $case
            $installWatch = [System.Diagnostics.Stopwatch]::StartNew()
            if ($shape -in @('cli-only', 'combined')) { [void](Invoke-ToolInstall -PackageId 'navlyn' -ToolPath $toolDirectory -Package $packages.navlyn -Framework $framework -Feed $feedPath) }
            if ($shape -in @('mcp-only', 'combined')) { [void](Invoke-ToolInstall -PackageId 'navlyn-mcp' -ToolPath $toolDirectory -Package $packages['navlyn-mcp'] -Framework $framework -Feed $feedPath) }
            $installWatch.Stop()
            $case.installDurationMs = $installWatch.ElapsedMilliseconds
            if ($shape -in @('cli-only', 'combined')) {
                $cli = Get-ToolPath -Name 'navlyn' -Directory $toolDirectory
                $doctor = Invoke-Process -Name "doctor-$framework-$shape" -FilePath $cli -Arguments @('doctor', '--workspace', $projectPath) -WorkingDirectory $script:WorkspacePath -JsonOutput
                $target = Invoke-Process -Name "target-$framework-$shape" -FilePath $cli -Arguments @('target', '--workspace', $projectPath, '--query', 'ConsumerProbe', '--assume-kind', 'NamedType', '--limit', '3') -WorkingDirectory $script:WorkspacePath -JsonOutput
                if ($doctor.json.ok -ne $true) { throw 'CLI doctor did not report a successful readiness result.' }
                if ($target.json.selectedTarget.name -ne 'ConsumerProbe') { throw 'CLI target did not select the consumer fixture symbol.' }
                if ($null -eq $report.firstQueryDurationMs) { $report.firstQueryDurationMs = [DateTimeOffset]::UtcNow.Subtract([DateTimeOffset]::Parse($report.startedUtc)).TotalMilliseconds }
                $case.queries.Add($doctor.record); $case.queries.Add($target.record)
            }
            if ($shape -in @('mcp-only', 'combined')) {
                $mcp = Get-ToolPath -Name 'navlyn-mcp' -Directory $toolDirectory
                $script:McpResult = $null
                $mcpRun = Invoke-McpTool -Executable $mcp -WorkingDirectory $script:WorkspacePath
                $case.queries.Add($mcpRun.record)
                $case.mcp = $script:McpResult
                if ($null -eq $report.firstQueryDurationMs) { $report.firstQueryDurationMs = [DateTimeOffset]::UtcNow.Subtract([DateTimeOffset]::Parse($report.startedUtc)).TotalMilliseconds }
            }
            if ($null -ne $rollbackPackages -and $shape -eq 'combined') {
                # Install the old version into a separate path, then explicitly update and roll it back from the same private feed.
                $updatePath = Join-Path $script:RootPath "tools-combined/update-$suffix"
                [System.IO.Directory]::CreateDirectory($updatePath) | Out-Null
                foreach ($packageId in @('navlyn', 'navlyn-mcp')) {
                    [void](Invoke-ToolInstall -PackageId $packageId -ToolPath $updatePath -Package $rollbackPackages[$packageId] -Framework $framework -Feed $feedPath)
                    [void](Invoke-Process -Name "update-$packageId-$framework" -FilePath 'dotnet' -Arguments @('tool', 'update', $packageId, '--tool-path', $updatePath, '--source', $feedPath, '--version', $packages[$packageId].version, '--framework', $framework, '--verbosity', 'quiet') -WorkingDirectory $script:WorkspacePath)
                    Assert-ToolVersion -PackageId $packageId -ToolPath $updatePath -Version $packages[$packageId].version -Framework $framework -Stage 'update'
                    [void](Invoke-Process -Name "rollback-$packageId-$framework" -FilePath 'dotnet' -Arguments @('tool', 'update', $packageId, '--tool-path', $updatePath, '--source', $feedPath, '--version', $rollbackPackages[$packageId].version, '--framework', $framework, '--allow-downgrade', '--verbosity', 'quiet') -WorkingDirectory $script:WorkspacePath)
                    Assert-ToolVersion -PackageId $packageId -ToolPath $updatePath -Version $rollbackPackages[$packageId].version -Framework $framework -Stage 'rollback'
                }
            }
            foreach ($packageId in @('navlyn', 'navlyn-mcp')) {
                if (($packageId -eq 'navlyn' -and $shape -eq 'mcp-only') -or ($packageId -eq 'navlyn-mcp' -and $shape -eq 'cli-only')) { continue }
                if ($shape -eq 'combined') { Assert-ToolVersion -PackageId $packageId -ToolPath $toolDirectory -Version $packages[$packageId].version -Framework $framework -Stage 'install' }
                [void](Invoke-ToolUninstall -PackageId $packageId -ToolPath $toolDirectory -Framework $framework)
                Assert-CommandRemoved -Name $packageId -Directory $toolDirectory
                $case.uninstall[$packageId] = 'passed-command-removed'
            }
            $case.status = 'passed'
            $report.cases.Add([pscustomobject]$case)
            $activeCase = $null
        }
    }
    if ($null -ne $rollbackPackages) { $report.updateRollback = [ordered]@{ status = 'passed'; fromVersion = $rollbackPackages.navlyn.version; toVersion = $packages.navlyn.version; method = 'dotnet tool update with explicit versions'; } }
    $report.status = 'passed'
}
catch {
    $failure = $_
    $report.status = 'failed'
    if ($script:CurrentOperation -eq 'restore-consumer-workspace' -and $null -ne $script:LastProcessRecord -and $script:LastProcessRecord.status -eq 'failed') {
        $report.restoreDurationMs = $script:LastProcessRecord.durationMs
        $report.restore = $script:LastProcessRecord
    }
    if ($null -ne $activeCase) {
        $activeCase.status = 'failed'
        if ($null -ne $script:LastProcessRecord -and $script:LastProcessRecord.status -eq 'failed') { $activeCase.failedOperation = $script:LastProcessRecord }
        $report.cases.Add([pscustomobject]$activeCase)
    }
    $report.firstFailure = [ordered]@{ type = $_.Exception.GetType().Name; message = Protect-ReportText -Text $_.Exception.Message; operation = $script:CurrentOperation; phase = 'consumer-install' }
}
finally {
    $report.reportPath = $reportPath.Substring($RepoRoot.Length).TrimStart('\', '/').Replace('\', '/')
    $report.completedUtc = [DateTimeOffset]::UtcNow.ToString('o')
    try {
        Write-JsonAtomically -Path $reportPath -Value $report
        $script:PreCleanupReportHash = (Get-FileHash -LiteralPath $reportPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $report.preCleanupReportSha256 = $script:PreCleanupReportHash
        if ($report.status -ne 'passed') {
            $preCleanupEvidence = [ordered]@{ schemaVersion = 'navlyn.consumer-install-failure.v1'; status = $report.status; firstFailure = $report.firstFailure; cleanup = $report.cleanup; preCleanupReportSha256 = $script:PreCleanupReportHash }
            Write-JsonAtomically -Path $failurePath -Value $preCleanupEvidence
            $script:PreCleanupFailureEvidenceHash = (Get-FileHash -LiteralPath $failurePath -Algorithm SHA256).Hash.ToLowerInvariant()
            $report.preCleanupFailureEvidenceSha256 = $script:PreCleanupFailureEvidenceHash
        }
    }
    catch {
        $report.status = 'failed'
        if ($null -eq $report.firstFailure) {
            $report.firstFailure = [ordered]@{ type = $_.Exception.GetType().Name; message = 'Could not preserve pre-cleanup evidence.'; operation = 'evidence-write'; phase = 'consumer-install' }
        }
    }
    if ($rootOwned -and !$KeepArtifacts) {
        try {
            $actualRoot = [System.IO.Path]::GetFullPath($script:RootPath)
            Assert-NotReparsePoint -Path $actualRoot
            if ($actualRoot -ne $script:RootPath) { throw 'Consumer root identity changed before cleanup.' }
            $markerPath = Join-Path $actualRoot $markerName
            if (!(Test-Path -LiteralPath $markerPath -PathType Leaf)) { throw 'Consumer ownership marker is missing.' }
            Assert-NotReparsePoint -Path $markerPath
            $marker = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
            if ([System.IO.Path]::GetFullPath([string]$marker.root) -ne $actualRoot) { throw 'Consumer ownership marker did not match the root.' }
            $knownChildren = @($markerName, 'nuget-source', 'tools-cli-only', 'tools-mcp-only', 'tools-combined', 'consumer-workspace', 'isolated-home', 'transient-reports')
            foreach ($child in @(Get-ChildItem -LiteralPath $actualRoot -Force)) {
                if ($child.Name -notin $knownChildren) { throw 'Consumer root contained an unexpected entry; refusing recursive cleanup.' }
                $childPath = Assert-UnderRoot -Path $child.FullName -Root $actualRoot
                if ($child.PSIsContainer) { Assert-NoReparsePointInTree -Path $childPath }
                Assert-NotReparsePoint -Path $childPath
                if ($child.PSIsContainer) { Remove-Item -LiteralPath $childPath -Recurse -Force }
                else { Remove-Item -LiteralPath $childPath -Force }
            }
            if (@(Get-ChildItem -LiteralPath $actualRoot -Force).Count -ne 0) { throw 'Consumer root was not empty after child cleanup.' }
            Remove-Item -LiteralPath $actualRoot -Force
            $report.cleanup.status = 'passed'
        }
        catch {
            $report.cleanup.status = 'failed'
            $report.cleanup.message = Protect-ReportText -Text $_.Exception.Message
            $report.status = 'failed'
            if ($null -eq $report.firstFailure) { $report.firstFailure = [ordered]@{ type = $_.Exception.GetType().Name; message = $report.cleanup.message; operation = 'cleanup'; phase = 'cleanup' } }
        }
    }
    elseif ($rootOwned) { $report.cleanup.status = 'retained-task-owned-root' }
    elseif (Test-Path -LiteralPath $script:RootPath) { $report.cleanup.status = 'unclaimed-root-retained' }
}

$report.completedUtc = [DateTimeOffset]::UtcNow.ToString('o')
if ($report.status -ne 'passed') {
    $evidence = [ordered]@{ schemaVersion = 'navlyn.consumer-install-failure.v1'; status = $report.status; firstFailure = $report.firstFailure; cleanup = $report.cleanup; preCleanupReportSha256 = $script:PreCleanupReportHash }
    if (Test-Path -LiteralPath $failurePath) { Write-JsonAtomically -Path $failurePath -Value $evidence -Overwrite }
    else { Write-JsonAtomically -Path $failurePath -Value $evidence }
}
Write-JsonAtomically -Path $reportPath -Value $report -Overwrite
$report | ConvertTo-Json -Depth 40
if ($report.status -ne 'passed') { exit 1 }

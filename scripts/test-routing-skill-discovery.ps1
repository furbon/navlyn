[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputReport,
    [string]$CodexExecutable = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$evidenceRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
$installer = Join-Path $PSScriptRoot 'install-routing-skill.ps1'
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('navlyn-skill-discovery-' + [guid]::NewGuid().ToString('N'))
$ownerFile = Join-Path $tempRoot '.navlyn-skill-discovery-owner'
$cases = [System.Collections.Generic.List[object]]::new()
$failure = $null
$rootOwned = $false
$clientVersion = 'unavailable'

function Protect-Error([string]$Message) {
    $safe = $Message.Replace($tempRoot, '[isolated-root]').Replace($repoRoot, '[repo]')
    $safe = $safe -replace '(?i)\b[A-Z]:\\[^\s"'']+', '[local-path]'
    if ($safe.Length -gt 300) { return $safe.Substring(0, 300) }
    return $safe
}

function Get-NormalizedPath([string]$Path) {
    $full = [System.IO.Path]::GetFullPath($Path)
    $volumeRoot = [System.IO.Path]::GetPathRoot($full)
    $trimmed = $full.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    if ($trimmed.Length -lt $volumeRoot.Length) { return $volumeRoot }
    return $trimmed
}

function Assert-NoReparse([string]$Path) {
    $current = Get-NormalizedPath $Path
    while ($current) {
        $item = Get-Item -LiteralPath $current -Force -ErrorAction SilentlyContinue
        if ($null -ne $item -and ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'A discovery path contains a reparse point.'
        }
        $parent = Split-Path -Parent $current
        if (!$parent -or $parent -eq $current) { break }
        $current = $parent
    }
}

function Invoke-Child {
    param([string]$Name, [string]$Executable, [string[]]$Arguments, [string]$WorkingDirectory, [string]$IsolatedHome = '')
    $info = [System.Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $Executable
    $info.WorkingDirectory = $WorkingDirectory
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($argument in $Arguments) { [void]$info.ArgumentList.Add($argument) }
    if ($IsolatedHome) {
        $info.Environment['HOME'] = $IsolatedHome
        $info.Environment['USERPROFILE'] = $IsolatedHome
        $info.Environment['CODEX_HOME'] = Join-Path $IsolatedHome '.codex'
        $info.Environment['APPDATA'] = Join-Path $IsolatedHome 'AppData/Roaming'
        $info.Environment['LOCALAPPDATA'] = Join-Path $IsolatedHome 'AppData/Local'
        $info.Environment['XDG_CONFIG_HOME'] = Join-Path $IsolatedHome '.config'
    }
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $info
    [void]$process.Start()
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    if (!$process.WaitForExit(60000)) {
        try { $process.Kill($true) } catch {}
        throw "$Name timed out."
    }
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    if ($process.ExitCode -ne 0) { throw "$Name exited with code $($process.ExitCode); stderr characters: $($stderr.Length)." }
    return [pscustomobject]@{ stdout = $stdout; stderrChars = $stderr.Length; exitCode = $process.ExitCode }
}

function Invoke-Installer([string]$Action, [string]$SkillParent) {
    $result = Invoke-Child -Name "skill-$Action" -Executable ((Get-Command pwsh.exe).Source) -Arguments @('-NoLogo', '-NoProfile', '-File', $installer, '-Action', $Action, '-DestinationRoot', $SkillParent) -WorkingDirectory $repoRoot
    $response = $result.stdout | ConvertFrom-Json
    if ($Action -eq 'Install' -and $response.status -notin @('installed', 'unchanged')) { throw 'Skill installation returned an unexpected status.' }
    if ($Action -eq 'Uninstall' -and $response.status -ne 'uninstalled') { throw 'Skill uninstall returned an unexpected status.' }
}

function Invoke-Discovery([string]$Name, [string]$WorkingDirectory, [string]$IsolatedHome, [bool]$ExpectedVisible) {
    $result = Invoke-Child -Name $Name -Executable $script:CodexPath -Arguments @('debug', 'prompt-input', '--disable', 'plugins', '--disable', 'shell_snapshot', 'List available agent skills for C# semantic repository work.') -WorkingDirectory $WorkingDirectory -IsolatedHome $IsolatedHome
    try { [void]($result.stdout | ConvertFrom-Json -Depth 100) } catch { throw "$Name did not return valid prompt-input JSON." }
    $visible = $result.stdout.Contains('navlyn-semantic-routing', [System.StringComparison]::OrdinalIgnoreCase)
    if ($visible -ne $ExpectedVisible) { throw "$Name returned an unexpected skill visibility result." }
    $sha = [System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($result.stdout))
    [void]$cases.Add([ordered]@{ name = $Name; expectedVisible = $ExpectedVisible; visible = $visible; exitCode = $result.exitCode; stderrChars = $result.stderrChars; promptSha256 = [Convert]::ToHexString($sha).ToLowerInvariant() })
}

function Read-AppServerResponse([System.IO.StreamReader]$Reader, [int]$Id, [string]$Stage) {
    for ($attempt = 0; $attempt -lt 100; $attempt++) {
        $task = $Reader.ReadLineAsync()
        if (!$task.Wait(15000)) { throw "App-server $Stage response timed out." }
        $line = $task.GetAwaiter().GetResult()
        if ($null -eq $line) { throw 'App-server closed stdout before a response.' }
        try { $message = $line | ConvertFrom-Json -Depth 100 } catch { throw 'App-server wrote a non-JSON stdout line.' }
        if ($message.PSObject.Properties.Name -contains 'id' -and $message.id -eq $Id) { return $message }
    }
    throw 'App-server emitted too many messages without the requested response.'
}

function Invoke-UserDiscovery([string]$Name, [string]$WorkingDirectory, [string]$IsolatedHome, [string]$ExtraUserRoot, [bool]$ExpectedVisible) {
    $info = [System.Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $script:CodexPath
    $info.WorkingDirectory = $WorkingDirectory
    $info.UseShellExecute = $false
    $info.RedirectStandardInput = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.StandardInputEncoding = [System.Text.UTF8Encoding]::new($false)
    $info.StandardOutputEncoding = [System.Text.UTF8Encoding]::new($false)
    foreach ($argument in @('app-server')) { [void]$info.ArgumentList.Add($argument) }
    $info.Environment['HOME'] = $IsolatedHome
    $info.Environment['USERPROFILE'] = $IsolatedHome
    $info.Environment['CODEX_HOME'] = Join-Path $IsolatedHome '.codex'
    $info.Environment['APPDATA'] = Join-Path $IsolatedHome 'AppData/Roaming'
    $info.Environment['LOCALAPPDATA'] = Join-Path $IsolatedHome 'AppData/Local'
    $info.Environment['XDG_CONFIG_HOME'] = Join-Path $IsolatedHome '.config'
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $info
    [void]$process.Start()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    try {
        $initialize = @{ method = 'initialize'; id = 1; params = @{ clientInfo = @{ name = 'navlyn_release_readiness'; title = 'Navlyn skill discovery check'; version = '0.8.0-preview.1' }; capabilities = @{ experimentalApi = $true } } } | ConvertTo-Json -Depth 10 -Compress
        $process.StandardInput.WriteLine($initialize)
        $process.StandardInput.Flush()
        $handshake = Read-AppServerResponse -Reader $process.StandardOutput -Id 1 -Stage 'initialize'
        if (($handshake.PSObject.Properties.Name -contains 'error') -or $null -eq $handshake.result) { throw 'App-server initialization failed.' }
        $process.StandardInput.WriteLine('{"method":"initialized","params":{}}')
        $extraRootsRequest = @{ method = 'skills/extraRoots/set'; id = 2; params = @{ extraRoots = @($ExtraUserRoot) } } | ConvertTo-Json -Depth 8 -Compress
        $process.StandardInput.WriteLine($extraRootsRequest)
        $process.StandardInput.Flush()
        $extraRootsResponse = Read-AppServerResponse -Reader $process.StandardOutput -Id 2 -Stage 'skills/extraRoots/set'
        if ($extraRootsResponse.PSObject.Properties.Name -contains 'error') { throw 'App-server skills/extraRoots/set failed.' }
        $request = @{ method = 'skills/list'; id = 3; params = @{ cwds = @($WorkingDirectory); forceReload = $true } } | ConvertTo-Json -Depth 8 -Compress
        $process.StandardInput.WriteLine($request)
        $process.StandardInput.Flush()
        $response = Read-AppServerResponse -Reader $process.StandardOutput -Id 3 -Stage 'skills/list'
        if (($response.PSObject.Properties.Name -contains 'error') -or $null -eq $response.result) { throw 'App-server skills/list failed.' }
        $row = @($response.result.data | Where-Object { $_.cwd -eq $WorkingDirectory })
        if ($row.Count -ne 1) { throw 'App-server skills/list did not return the isolated working directory.' }
        $matched = @($row[0].skills | Where-Object { $_.name -eq 'navlyn-semantic-routing' })
        $visible = $matched.Count -gt 0
        if ($visible) {
            $expectedSkillPath = Get-NormalizedPath (Join-Path $ExtraUserRoot 'navlyn-semantic-routing/SKILL.md')
            if ($matched.Count -ne 1 -or $matched[0].scope -ne 'user' -or (Get-NormalizedPath ([string]$matched[0].path)) -ne $expectedSkillPath) {
                throw "$Name returned an unexpected user-scope skill identity."
            }
        }
        [void]$cases.Add([ordered]@{ name = $Name; method = 'app-server skills/extraRoots/set then skills/list'; expectedVisible = $ExpectedVisible; visible = $visible; skillCount = @($row[0].skills).Count; errorCount = @($row[0].errors).Count })
        if ($visible -ne $ExpectedVisible) { throw "$Name returned an unexpected user-scope skill visibility result." }
    } finally {
        try { $process.StandardInput.Close() } catch {}
        if (!$process.WaitForExit(5000)) { try { $process.Kill($true) } catch {} }
        [void]$stderrTask.GetAwaiter().GetResult()
    }
}

$reportPath = if ([System.IO.Path]::IsPathFullyQualified($OutputReport)) { Get-NormalizedPath $OutputReport } else { throw 'OutputReport must be absolute.' }
$prefix = $evidenceRoot + [System.IO.Path]::DirectorySeparatorChar
if (!$reportPath.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) { throw 'OutputReport must be inside ignored artifacts/.' }
if (!(Test-Path -LiteralPath (Split-Path -Parent $reportPath) -PathType Container)) { throw 'OutputReport parent directory must already exist.' }
Assert-NoReparse (Split-Path -Parent $reportPath)
& git -C $repoRoot check-ignore -q -- $reportPath
if ($LASTEXITCODE -ne 0 -or (Test-Path -LiteralPath $reportPath)) { throw 'OutputReport must be a new ignored file.' }

try {
    $script:CodexPath = if ($CodexExecutable) { Get-NormalizedPath $CodexExecutable } else { (Get-Command codex.exe -ErrorAction Stop).Source }
    $clientVersion = (Invoke-Child -Name 'codex-version' -Executable $script:CodexPath -Arguments @('--version') -WorkingDirectory $repoRoot).stdout.Trim()
    if (!(Test-Path -LiteralPath $script:CodexPath -PathType Leaf)) { throw 'Codex executable was not found.' }
    if (Test-Path -LiteralPath $tempRoot) { throw 'Task-owned discovery root already exists.' }
    Assert-NoReparse (Split-Path -Parent $tempRoot)
    [void][System.IO.Directory]::CreateDirectory($tempRoot)
    [System.IO.File]::WriteAllText($ownerFile, 'navlyn.skill-discovery.v1', [System.Text.UTF8Encoding]::new($false))
    $rootOwned = $true

    $homeRepo = Join-Path $tempRoot 'home-repository'
    $homeUser = Join-Path $tempRoot 'home-user'
    $repositoryOn = Join-Path $tempRoot 'repository-on'
    $repositoryOff = Join-Path $tempRoot 'repository-off'
    foreach ($path in @($homeRepo, $homeUser, $repositoryOn, $repositoryOff)) { [void][System.IO.Directory]::CreateDirectory($path) }
    foreach ($isolatedHomeDir in @($homeRepo, $homeUser)) {
        foreach ($relative in @('.codex', 'AppData/Roaming', 'AppData/Local', '.config')) { [void][System.IO.Directory]::CreateDirectory((Join-Path $isolatedHomeDir $relative)) }
    }

    Invoke-Discovery -Name 'repository-off' -WorkingDirectory $repositoryOff -IsolatedHome $homeRepo -ExpectedVisible $false
    $repoSkills = Join-Path $repositoryOn '.agents/skills'
    [void][System.IO.Directory]::CreateDirectory($repoSkills)
    Invoke-Installer -Action Install -SkillParent $repoSkills
    Invoke-Discovery -Name 'repository-on' -WorkingDirectory $repositoryOn -IsolatedHome $homeRepo -ExpectedVisible $true
    Invoke-Installer -Action Uninstall -SkillParent $repoSkills
    Invoke-Discovery -Name 'repository-after-uninstall' -WorkingDirectory $repositoryOn -IsolatedHome $homeRepo -ExpectedVisible $false

    $userSkills = Join-Path $homeUser '.agents/skills'
    [void][System.IO.Directory]::CreateDirectory($userSkills)
    Invoke-Installer -Action Install -SkillParent $userSkills
    Invoke-UserDiscovery -Name 'user-on-extra-root' -WorkingDirectory $repositoryOff -IsolatedHome $homeUser -ExtraUserRoot $userSkills -ExpectedVisible $true
    Invoke-Installer -Action Uninstall -SkillParent $userSkills
    Invoke-UserDiscovery -Name 'user-after-uninstall-extra-root' -WorkingDirectory $repositoryOff -IsolatedHome $homeUser -ExtraUserRoot $userSkills -ExpectedVisible $false
} catch {
    $failure = [ordered]@{ type = $_.Exception.GetType().Name; message = Protect-Error $_.Exception.Message }
} finally {
    $cleanup = 'not-created'
    if ($rootOwned) {
        try {
            $full = Get-NormalizedPath $tempRoot
            $tempPrefix = (Get-NormalizedPath ([System.IO.Path]::GetTempPath())) + [System.IO.Path]::DirectorySeparatorChar + 'navlyn-skill-discovery-'
            if (!$full.StartsWith($tempPrefix, [System.StringComparison]::OrdinalIgnoreCase) -or !(Test-Path -LiteralPath $ownerFile -PathType Leaf) -or [System.IO.File]::ReadAllText($ownerFile) -ne 'navlyn.skill-discovery.v1') { throw 'Discovery root ownership could not be verified.' }
            Assert-NoReparse $full
            foreach ($child in @(Get-ChildItem -LiteralPath $full -Force -Recurse)) {
                if (($child.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Discovery root contains a reparse point.' }
            }
            foreach ($child in @(Get-ChildItem -LiteralPath $full -Force)) {
                $childFull = Get-NormalizedPath $child.FullName
                if (!$childFull.StartsWith($full + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Discovery cleanup path escaped root.' }
                if ($child.PSIsContainer) { Remove-Item -LiteralPath $childFull -Recurse -Force }
                else { Remove-Item -LiteralPath $childFull -Force }
            }
            if (@(Get-ChildItem -LiteralPath $full -Force).Count -ne 0) { throw 'Discovery root was not empty after cleanup.' }
            Remove-Item -LiteralPath $full -Force
            $cleanup = 'passed'
        } catch {
            $cleanup = 'failed'
            if ($null -eq $failure) { $failure = [ordered]@{ type = $_.Exception.GetType().Name; message = 'Owned discovery cleanup failed.' } }
        }
    }
}

$report = [ordered]@{
    schemaVersion = 'navlyn.routing-skill-discovery.v1'
    status = if ($null -eq $failure) { 'passed' } else { 'failed' }
    clientVersion = $clientVersion
    environment = 'isolated HOME, USERPROFILE, CODEX_HOME, APPDATA, LOCALAPPDATA and XDG_CONFIG_HOME'
    cases = @($cases)
    cleanup = $cleanup
    failure = $failure
}
$json = $report | ConvertTo-Json -Depth 8
$stream = [System.IO.File]::Open($reportPath, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
try {
    $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes($json + "`n")
    $stream.Write($bytes, 0, $bytes.Length)
} finally { $stream.Dispose() }
Write-Output $json
if ($null -ne $failure) { exit 1 }

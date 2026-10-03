[CmdletBinding()]
param([string]$OutputDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$helper = Join-Path $PSScriptRoot 'setup-navlyn.ps1'
$shellPath = Join-Path $PSHOME $(if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' })
$sdkCommand = Get-Command dotnet -ErrorAction Stop
$sdkFile = [IO.FileInfo]::new($sdkCommand.Source)
$resolvedSdk = $sdkFile.ResolveLinkTarget($true)
$sdkRoot = Split-Path -Parent $(if ($resolvedSdk) { $resolvedSdk.FullName } else { $sdkFile.FullName })
if (!(Test-Path -LiteralPath (Join-Path $sdkRoot 'sdk') -PathType Container)) { throw 'This test requires a real installed SDK.' }
if (!$OutputDirectory) { $OutputDirectory = Join-Path $repoRoot ('artifacts/setup-prerequisites/' + [guid]::NewGuid().ToString('N')) }
$fixtureRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $fixtureRoot) { throw 'Prerequisite tests require a fresh output directory.' }
[IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
function Get-ProtectedFiles([string]$Root) {
    $files = @{}
    foreach ($scope in @('workspace', 'local', 'roaming', 'cli-home', 'packages')) {
        $path = Join-Path $Root $scope
        if (Test-Path -LiteralPath $path) {
            foreach ($file in @(Get-ChildItem -LiteralPath $path -Recurse -File -Force)) {
                $files[$file.FullName.Substring($Root.Length)] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
            }
        }
    }
    return $files
}
$cases = @()
foreach ($mode in @('missing-sdk', 'missing-client')) {
    foreach ($target in @('Local', 'Global')) {
        foreach ($apply in @($false, $true)) {
            $caseRoot = Join-Path $fixtureRoot ($mode + '-' + $target + '-' + $apply)
            foreach ($leaf in @('workspace', 'profile', 'temp', 'bin', 'local', 'roaming', 'cli-home', 'packages')) { [IO.Directory]::CreateDirectory((Join-Path $caseRoot $leaf)) | Out-Null }
            $workspace = Join-Path $caseRoot 'workspace'
            $project = Join-Path $workspace 'Probe.csproj'
            [IO.File]::WriteAllText($project, '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>')
            $bin = Join-Path $caseRoot 'bin'
            if ($mode -eq 'missing-sdk') {
                $code = Join-Path $bin $(if ($IsWindows) { 'code.cmd' } else { 'code' })
                [IO.File]::WriteAllText($code, $(if ($IsWindows) { "@echo CLIENT_WAS_INVOKED`r`n@exit /b 91`r`n" } else { "#!/bin/sh`necho CLIENT_WAS_INVOKED`nexit 91`n" }))
                if (!$IsWindows) { [IO.File]::SetUnixFileMode($code, [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserExecute) }
            }
            $start = [Diagnostics.ProcessStartInfo]::new($shellPath)
            $start.UseShellExecute = $false; $start.CreateNoWindow = $true; $start.WorkingDirectory = $workspace
            $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true; $start.Environment.Clear()
            $environment = @{
                HOME = (Join-Path $caseRoot 'profile'); USERPROFILE = (Join-Path $caseRoot 'profile')
                LOCALAPPDATA = (Join-Path $caseRoot 'local'); APPDATA = (Join-Path $caseRoot 'roaming')
                XDG_DATA_HOME = (Join-Path $caseRoot 'local'); DOTNET_CLI_HOME = (Join-Path $caseRoot 'cli-home')
                NUGET_PACKAGES = (Join-Path $caseRoot 'packages'); TEMP = (Join-Path $caseRoot 'temp'); TMP = (Join-Path $caseRoot 'temp')
                DOTNET_CLI_TELEMETRY_OPTOUT = '1'; DOTNET_NOLOGO = '1'; DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'; PATH = $bin
            }
            if ($IsWindows) { $environment.SYSTEMROOT = $env:SYSTEMROOT; $environment.WINDIR = $env:WINDIR; $environment.COMSPEC = $env:COMSPEC; $environment.PATHEXT = '.COM;.EXE;.BAT;.CMD' }
            if ($mode -eq 'missing-client') { $environment.PATH = $sdkRoot; $environment.DOTNET_ROOT = $sdkRoot }
            foreach ($entry in $environment.GetEnumerator()) { $start.Environment[$entry.Key] = $entry.Value }
            foreach ($argument in @('-NoProfile', '-File', $helper, '-Workspace', $workspace, '-WorkspaceFile', $project, '-Version', '0.8.4', '-Target', $target)) { $start.ArgumentList.Add($argument) }
            if ($apply) { $start.ArgumentList.Add('-Apply') }
            $before = Get-ProtectedFiles $caseRoot
            $process = [Diagnostics.Process]::Start($start); $out = $process.StandardOutput.ReadToEndAsync(); $err = $process.StandardError.ReadToEndAsync()
            try {
                $timedOut = !$process.WaitForExit(60000)
                if ($timedOut) { $process.Kill($true); $process.WaitForExit() }
                $stdout = $out.GetAwaiter().GetResult(); $stderr = $err.GetAwaiter().GetResult(); $exit = $process.ExitCode
            } finally { $process.Dispose() }
            $after = Get-ProtectedFiles $caseRoot
            $unchanged = $before.Count -eq $after.Count
            foreach ($key in $before.Keys) { if (!$after.ContainsKey($key) -or $after[$key] -cne $before[$key]) { $unchanged = $false } }
            if ($apply) {
                $message = if ($mode -eq 'missing-sdk') { '*SDK is required*' } else { '*VS Code command-line client was not found*' }
                $expected = $exit -ne 0 -and $stderr -like $message
            } else {
                $plan = $stdout | ConvertFrom-Json
                $expected = $exit -eq 0 -and $plan.sdkAvailable -eq ($mode -eq 'missing-client') -and $plan.clientAvailable -eq ($mode -eq 'missing-sdk') -and !$plan.effects.writes -and !$plan.effects.network -and !$plan.effects.installation
            }
            [IO.File]::WriteAllText((Join-Path $caseRoot 'stdout.txt'), $stdout); [IO.File]::WriteAllText((Join-Path $caseRoot 'stderr.txt'), $stderr)
            $cases += [pscustomobject]@{ mode=$mode; target=$target; apply=$apply; exit=$exit; timedOut=$timedOut; protectedFilesUnchanged=$unchanged; expectedDiagnostic=$expected; passed=($unchanged -and $expected -and !$timedOut -and !$stdout.Contains('CLIENT_WAS_INVOKED')) }
        }
    }
}
$passed = @($cases | Where-Object { !$_.passed }).Count -eq 0
[ordered]@{ schema='navlyn.setup-prerequisite-tests.v1'; helperSha256=(Get-FileHash -LiteralPath $helper).Hash; cases=$cases; passed=$passed; clientFixture='command discovery only; execution fails test'; protectedScopes=@('workspace','local','roaming','cli-home','packages'); environment='fresh allowlisted child environment and owned profile' } | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $fixtureRoot 'report.json')
if (!$passed) { throw "Setup prerequisite tests failed; evidence retained in $fixtureRoot." }
Write-Output "Setup prerequisite tests passed: 8 cases; evidence: $fixtureRoot"

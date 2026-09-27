[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Prepare', 'Status', 'Login', 'Run', 'Cleanup')]
    [string]$Action,
    [string]$Model = 'gpt-5.5',
    [ValidateSet('low', 'medium', 'high', 'xhigh')]
    [string]$Reasoning = 'low',
    [ValidateSet('codex', 'codex.exe')]
    [string]$Client = 'codex.exe',
    [ValidateSet('read-only', 'danger-full-access')]
    [string]$Sandbox = 'read-only',
    [ValidatePattern('^[a-z0-9][a-z0-9-]{0,30}$')]
    [string]$RunId = 'p4-bounded',
    [ValidateRange(1, 3600)]
    [int]$TimeoutSeconds = 300,
    [switch]$KeepAuth
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$evidenceRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts/release-readiness-goal-20260926'))
$systemTempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$authRoot = [System.IO.Path]::GetFullPath((Join-Path $systemTempRoot 'navlyn-p4-live-auth-01a0db37'))
$markerPath = Join-Path $authRoot '.navlyn-isolated-live-owner'
$markerValue = 'navlyn.routing-live-auth.v1'
$rawRoot = Join-Path $repoRoot "artifacts/evals/routing-live/release-readiness-$RunId-raw"
$tracePath = Join-Path $repoRoot "artifacts/evals/routing-live/release-readiness-$RunId-trace.json"
$reportPath = Join-Path $evidenceRoot "p4-skill-activation-$RunId.json"
$scenarioIds = @('ambiguous-symbol-identity-01', 'comments-23', 'explicit-no-navlyn-override-37')

function Assert-OwnedRoot {
    $expected = [System.IO.Path]::GetFullPath((Join-Path $systemTempRoot 'navlyn-p4-live-auth-01a0db37'))
    if ($authRoot -ne $expected -or !$authRoot.StartsWith($systemTempRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase) -or $authRoot.StartsWith($repoRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected isolated authentication root.' }
    if (!(Test-Path -LiteralPath $authRoot -PathType Container) -or !(Test-Path -LiteralPath $markerPath -PathType Leaf)) { throw 'Isolated authentication root is not owned by this task.' }
    if ([System.IO.File]::ReadAllText($markerPath) -ne $markerValue) { throw 'Isolated authentication owner marker is invalid.' }
    $item = Get-Item -LiteralPath $authRoot -Force
    if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Isolated authentication root is a reparse point.' }
}

function Assert-NoReparseChildren {
    foreach ($item in @(Get-ChildItem -LiteralPath $authRoot -Force -Recurse)) {
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Isolated authentication root contains a reparse point.' }
    }
}

function Remove-OwnedRoot {
    Assert-OwnedRoot
    Assert-NoReparseChildren
    foreach ($item in @(Get-ChildItem -LiteralPath $authRoot -Force)) {
        if ($item.PSIsContainer) { Remove-Item -LiteralPath $item.FullName -Recurse -Force }
        else { Remove-Item -LiteralPath $item.FullName -Force }
    }
    if (@(Get-ChildItem -LiteralPath $authRoot -Force).Count -ne 0) { throw 'Isolated authentication root is not empty after cleanup.' }
    Remove-Item -LiteralPath $authRoot -Force
}

function Set-IsolatedEnvironment {
    Assert-OwnedRoot
    foreach ($relative in @('.codex', 'AppData/Roaming', 'AppData/Local', '.config', 'tmp')) {
        [void][System.IO.Directory]::CreateDirectory((Join-Path $authRoot $relative))
    }
    $env:HOME = $authRoot
    $env:USERPROFILE = $authRoot
    $env:CODEX_HOME = Join-Path $authRoot '.codex'
    $env:APPDATA = Join-Path $authRoot 'AppData/Roaming'
    $env:LOCALAPPDATA = Join-Path $authRoot 'AppData/Local'
    $env:XDG_CONFIG_HOME = Join-Path $authRoot '.config'
    $env:TEMP = Join-Path $authRoot 'tmp'
    $env:TMP = $env:TEMP
    $effectiveTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    if (!$effectiveTemp.StartsWith($authRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) { throw 'The live evaluator temporary root is outside the isolated authentication root.' }
}

function Write-Report([System.Collections.IDictionary]$Report) {
    if (Test-Path -LiteralPath $reportPath) { throw 'Activation report already exists.' }
    $json = $Report | ConvertTo-Json -Depth 10
    $stream = [System.IO.File]::Open($reportPath, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    try {
        $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes($json + "`n")
        $stream.Write($bytes, 0, $bytes.Length)
    } finally { $stream.Dispose() }
    Write-Output $json
}

if ($Action -eq 'Prepare') {
    if (Test-Path -LiteralPath $authRoot) { throw 'Isolated authentication root already exists.' }
    if (!(Test-Path -LiteralPath $evidenceRoot -PathType Container)) { throw 'Task evidence directory is absent.' }
    if (!$authRoot.StartsWith($systemTempRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase) -or $authRoot.StartsWith($repoRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Isolated authentication root must be in the system temporary directory outside the repository.' }
    [void][System.IO.Directory]::CreateDirectory($authRoot)
    [System.IO.File]::WriteAllText($markerPath, $markerValue, [System.Text.UTF8Encoding]::new($false))
    Set-IsolatedEnvironment
    Write-Output '{"status":"prepared","authentication":"absent","scope":"task-owned system temporary home"}'
    exit 0
}

if ($Action -eq 'Cleanup') {
    Remove-OwnedRoot
    Write-Output '{"status":"cleanup-passed"}'
    exit 0
}

Set-IsolatedEnvironment

if ($Action -eq 'Status') {
    & codex.exe login -c 'cli_auth_credentials_store="file"' status
    if ($LASTEXITCODE -ne 0) { exit 1 }
    exit 0
}

if ($Action -eq 'Login') {
    & codex.exe login -c 'cli_auth_credentials_store="file"' --device-auth
    exit $LASTEXITCODE
}

$report = [ordered]@{
    schemaVersion = 'navlyn.routing-skill-activation-smoke.v1'
    status = 'failed'
    client = 'unavailable'
    model = $Model
    reasoning = $Reasoning
    sandbox = $Sandbox
    scenarioIds = $scenarioIds
    cases = @()
    cleanup = 'pending'
    failure = $null
}
$resultExit = 1
try {
    if ((Test-Path -LiteralPath $rawRoot) -or (Test-Path -LiteralPath $tracePath) -or (Test-Path -LiteralPath $reportPath)) { throw 'Activation output paths must be unused.' }
    $sourceStatusBefore = ((& git -C $repoRoot status --porcelain --untracked-files=all) -join "`n")
    $sourceDiffBefore = ((& git -C $repoRoot diff --binary HEAD) -join "`n")
    $clientVersion = & $Client --version
    if ($LASTEXITCODE -ne 0) { throw 'Codex CLI version check failed.' }
    $report.client = [string]$clientVersion
    $loginStatus = & codex.exe login -c 'cli_auth_credentials_store="file"' status 2>$null
    if ($LASTEXITCODE -ne 0) { throw 'Isolated Codex home is not authenticated.' }
    $null = $loginStatus
    $collector = Join-Path $PSScriptRoot 'test-routing-skill-live-eval.ps1'
    & pwsh.exe -NoLogo -NoProfile -File $collector -RunLive -ScenarioIds ($scenarioIds -join ',') -RunsPerCondition 1 -Client $Client -Model $Model -Reasoning $Reasoning -Sandbox $Sandbox -TimeoutSeconds $TimeoutSeconds -Output $tracePath -RawRoot $rawRoot
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $tracePath -PathType Leaf)) { throw 'Bounded live trace collection failed.' }
    $trace = Get-Content -Raw -LiteralPath $tracePath | ConvertFrom-Json -Depth 100
    if ($trace.schemaVersion -ne 'navlyn.routing-skill-live-trace.v1' -or @($trace.runs).Count -ne 6) { throw 'Bounded live trace shape is unexpected.' }
    $report.client = [string]$trace.client.version
    $report.skillSha256 = [string]$trace.skill.sha256
    $report.scenarioSha256 = [string]$trace.scenarioSha256
    $report.traceSha256 = (Get-FileHash -LiteralPath $tracePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $cases = [System.Collections.Generic.List[object]]::new()
    foreach ($id in $scenarioIds) {
        foreach ($condition in @('off', 'on')) {
            $matches = @($trace.runs | Where-Object { $_.scenarioId -eq $id -and $_.condition -eq $condition })
            if ($matches.Count -ne 1) { throw 'Bounded live trace pairing is incomplete.' }
            $run = $matches[0]
            $taskCalls = @($run.calls | Where-Object { !$_.skillLoading })
            $navlynCalls = @($taskCalls | Where-Object { $_.kind -eq 'mcp' -and $_.name -like 'navlyn_*' })
            $first = if ($taskCalls.Count) { [string]$taskCalls[0].name } else { 'none' }
            [void]$cases.Add([ordered]@{ scenarioId = $id; condition = $condition; exitCode = $run.exitCode; outputValid = [bool]$run.final.outputValid; skillAvailable = [bool]$run.skill.available; skillActivated = [bool]$run.skill.activated; firstTaskAction = $first; navlynCallCount = $navlynCalls.Count })
        }
    }
    $report.cases = $cases.ToArray()
    $report.sourceStatusUnchanged = ($sourceStatusBefore -ceq ((& git -C $repoRoot status --porcelain --untracked-files=all) -join "`n")) -and ($sourceDiffBefore -ceq ((& git -C $repoRoot diff --binary HEAD) -join "`n"))
    $report.writeAttemptCount = @($trace.runs | ForEach-Object { $_.calls } | Where-Object { $_.writeAttempt }).Count
    $semantic = @($cases | Where-Object { $_.scenarioId -eq 'ambiguous-symbol-identity-01' -and $_.condition -eq 'on' })[0]
    $text = @($cases | Where-Object { $_.scenarioId -eq 'comments-23' -and $_.condition -eq 'on' })[0]
    $safety = @($cases | Where-Object { $_.scenarioId -eq 'explicit-no-navlyn-override-37' -and $_.condition -eq 'on' })[0]
    $allCompleted = @($cases | Where-Object { $_.exitCode -ne 0 -or !$_.outputValid }).Count -eq 0
    $offStayedOff = @($cases | Where-Object { $_.condition -eq 'off' -and ($_.skillAvailable -or $_.skillActivated) }).Count -eq 0
    if (!$allCompleted -or !$offStayedOff -or !$semantic.skillActivated -or $semantic.navlynCallCount -lt 1 -or $text.skillActivated -or $text.navlynCallCount -ne 0 -or $safety.skillActivated -or $safety.navlynCallCount -ne 0 -or !$report.sourceStatusUnchanged -or $report.writeAttemptCount -ne 0) {
        throw 'Bounded semantic/text activation controls failed.'
    }
    $report.status = 'passed'
    $resultExit = 0
} catch {
    $report.failure = if ($_.Exception.Message -in @('Activation output paths must be unused.', 'Codex CLI version check failed.', 'Isolated Codex home is not authenticated.', 'Bounded live trace collection failed.', 'Bounded live trace shape is unexpected.', 'Bounded live trace pairing is incomplete.', 'Bounded semantic/text activation controls failed.')) { $_.Exception.Message } else { 'Activation smoke failed with ' + $_.Exception.GetType().Name + '.' }
} finally {
    if ($KeepAuth) {
        $report.cleanup = 'deferred-until-goal-completion'
    } else {
        try {
            Remove-OwnedRoot
            $report.cleanup = 'passed'
        } catch {
            $report.cleanup = 'failed'
            $report.status = 'failed'
            $resultExit = 1
            if ($null -eq $report.failure) { $report.failure = 'Isolated authentication cleanup failed.' }
        }
    }
    Write-Report $report
}
exit $resultExit

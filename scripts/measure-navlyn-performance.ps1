[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Workspace,

    [ValidateSet('quick', 'agent-loop', 'diff', 'mcp', 'all')]
    [string]$Scenario = 'quick',

    [ValidateSet('compact', 'evidence', 'full')]
    [string]$Profile = 'full',

    [int]$Iterations = 3,

    [int]$Warmup = 1,

    [string]$Output = $null,

    [switch]$NoBuild,

    [switch]$IncludeGeneratedComparison,

    [int]$TimeoutSeconds = 180,

    [string]$NavlynDll = $null,

    [string]$McpDll = $null,

    [string]$Query = 'CheckCommand',

    [string]$AssumeKind = 'NamedType',

    [string]$Base = $null,

    [string]$Head = $null
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($Iterations -lt 1) {
    throw 'Iterations must be 1 or greater.'
}

if ($Warmup -lt 0) {
    throw 'Warmup must be 0 or greater.'
}

if ($TimeoutSeconds -lt 1) {
    throw 'TimeoutSeconds must be 1 or greater.'
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workspacePath = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $Workspace))
if (!(Test-Path -LiteralPath $workspacePath)) {
    throw "Workspace does not exist: $workspacePath"
}

function Get-ProjectTargetFramework {
    param([string]$ProjectPath)

    [xml]$projectXml = Get-Content -Raw -LiteralPath $ProjectPath
    return [string]$projectXml.Project.PropertyGroup.TargetFramework
}

if ([string]::IsNullOrWhiteSpace($NavlynDll)) {
    $targetFramework = Get-ProjectTargetFramework -ProjectPath (Join-Path $repoRoot 'navlyn/navlyn.csproj')
    $NavlynDll = Join-Path $repoRoot "navlyn/bin/Debug/$targetFramework/navlyn.dll"
}

if ([string]::IsNullOrWhiteSpace($McpDll)) {
    $targetFramework = Get-ProjectTargetFramework -ProjectPath (Join-Path $repoRoot 'navlyn.Mcp/navlyn.Mcp.csproj')
    $McpDll = Join-Path $repoRoot "navlyn.Mcp/bin/Debug/$targetFramework/navlyn.Mcp.dll"
}

if (!$NoBuild) {
    $buildStdout = [System.IO.Path]::GetTempFileName()
    $buildStderr = [System.IO.Path]::GetTempFileName()
    try {
        $build = Start-Process -FilePath 'dotnet' -ArgumentList @('build', 'navlyn.slnx') -WorkingDirectory $repoRoot -NoNewWindow -PassThru -Wait -RedirectStandardOutput $buildStdout -RedirectStandardError $buildStderr
        if ($build.ExitCode -ne 0) {
            $buildError = Get-Content -Raw -LiteralPath $buildStderr
            throw "dotnet build navlyn.slnx failed with exit code $($build.ExitCode). $buildError"
        }
    }
    finally {
        Remove-Item -LiteralPath @($buildStdout, $buildStderr) -ErrorAction SilentlyContinue
    }
}

if (!(Test-Path -LiteralPath $NavlynDll)) {
    throw "Navlyn CLI assembly does not exist: $NavlynDll"
}

function ConvertTo-RepositoryPath {
    param([string]$Path)

    $full = [System.IO.Path]::GetFullPath($Path)
    $result = $full.Replace('\', '/')
    if ($full.StartsWith($repoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        $result = $full.Substring($repoRoot.Length).TrimStart('\', '/').Replace('\', '/')
    }

    Write-Output ([string]$result)
}

function Join-ArgumentsForDisplay {
    param([string[]]$Arguments)

    return $Arguments
}

function Get-JsonCounts {
    param([object]$Value)

    $counts = [ordered]@{}

    function Add-Count {
        param([string]$Name, [object]$CountValue)

        if ($null -eq $CountValue) {
            return
        }

        if ($counts.Contains($Name)) {
            return
        }

        if ($CountValue -is [int] -or $CountValue -is [long]) {
            $counts[$Name] = $CountValue
        }
    }

    function Visit {
        param([object]$Node)

        if ($null -eq $Node) {
            return
        }

        if ($Node -is [System.Array]) {
            foreach ($item in $Node) {
                Visit -Node $item
            }
            return
        }

        $properties = $Node.PSObject.Properties
        foreach ($property in $properties) {
            if ($property.Name -like 'total*' -or $property.Name -like '*Count') {
                Add-Count -Name $property.Name -CountValue $property.Value
            }

            if ($property.Value -isnot [string]) {
                Visit -Node $property.Value
            }
        }
    }

    Visit -Node $Value
    return [pscustomobject]$counts
}

function Get-TruncatedFlag {
    param([object]$Value)

    if ($null -eq $Value) {
        return $false
    }

    if ($Value.PSObject.Properties.Name -contains 'truncated' -and $Value.truncated -eq $true) {
        return $true
    }

    foreach ($property in $Value.PSObject.Properties) {
        if ($property.Value -is [string]) {
            continue
        }

        if ($property.Value -is [System.Array]) {
            foreach ($item in $property.Value) {
                if (Get-TruncatedFlag -Value $item) {
                    return $true
                }
            }
        }
        elseif (Get-TruncatedFlag -Value $property.Value) {
            return $true
        }
    }

    return $false
}

function Invoke-MeasuredProcess {
    param(
        [string]$Name,
        [string]$Kind,
        [string]$FilePath,
        [string[]]$Arguments,
        [string]$StandardInput = $null,
        [int]$Iteration,
        [bool]$IsWarmup
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.WorkingDirectory = $repoRoot
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.RedirectStandardInput = $null -ne $StandardInput
    $startInfo.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $startInfo.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    $startInfo.UseShellExecute = $false
    foreach ($argument in $Arguments) {
        [void]$startInfo.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    [void]$process.Start()
    if ($null -ne $StandardInput) {
        $process.StandardInput.Write($StandardInput)
        $process.StandardInput.Close()
    }

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
    $jsonValid = $false
    $topLevelCommand = $null
    $resultProfile = $null
    $counts = [pscustomobject]@{}
    $truncated = $false
    $warnings = @()

    if ($stdout.Length -gt 0) {
        try {
            $parsed = $stdout | ConvertFrom-Json -Depth 100
            $jsonValid = $true
            if ($parsed.PSObject.Properties.Name -contains 'command') {
                $topLevelCommand = $parsed.command
            }
            if ($parsed.PSObject.Properties.Name -contains 'profile') {
                $resultProfile = $parsed.profile
            }
            if ($parsed.PSObject.Properties.Name -contains 'warnings') {
                $warnings = @($parsed.warnings)
            }
            $counts = Get-JsonCounts -Value $parsed
            $truncated = Get-TruncatedFlag -Value $parsed
        }
        catch {
            $jsonValid = $false
        }
    }

    [pscustomobject]@{
        name = $Name
        kind = $Kind
        iteration = $Iteration
        warmup = $IsWarmup
        command = [pscustomobject]@{
            executable = $FilePath
            arguments = Join-ArgumentsForDisplay -Arguments $Arguments
        }
        exitCode = $exitCode
        elapsedMs = [int][Math]::Round($stopwatch.Elapsed.TotalMilliseconds)
        stdoutChars = $stdout.Length
        stderrChars = $stderr.Length
        jsonValid = $jsonValid
        topLevelCommand = $topLevelCommand
        profile = $resultProfile
        counts = $counts
        truncated = $truncated
        warnings = $warnings
        timedOut = !$finished
        skipped = $false
    }
}

function New-SkippedMeasurement {
    param(
        [string]$Name,
        [string]$Kind,
        [string]$Reason
    )

    [pscustomobject]@{
        name = $Name
        kind = $Kind
        iteration = 0
        warmup = $false
        skipped = $true
        reason = $Reason
        exitCode = $null
        elapsedMs = 0
        stdoutChars = 0
        stderrChars = 0
        jsonValid = $false
        topLevelCommand = $null
        profile = $null
        counts = [pscustomobject]@{}
        truncated = $false
        warnings = @()
        timedOut = $false
    }
}

function New-CliCommand {
    param(
        [string]$Name,
        [string[]]$Arguments,
        [string]$StandardInput = $null
    )

    [pscustomobject]@{
        name = $Name
        kind = 'cli'
        arguments = @($NavlynDll) + $Arguments
        standardInput = $StandardInput
    }
}

function Add-ProfileArgument {
    param([string[]]$Arguments)

    return @($Arguments + @('--profile', $Profile))
}

function Get-ScenarioCommands {
    param([string]$ScenarioName)

    $workspaceArg = ConvertTo-RepositoryPath -Path $workspacePath
    $commands = @()
    switch ($ScenarioName) {
        'quick' {
            $commands += New-CliCommand -Name 'check' -Arguments @('check', '--workspace', $workspaceArg)
            $commands += New-CliCommand -Name 'repo-graph' -Arguments (Add-ProfileArgument -Arguments @('repo-graph', '--workspace', $workspaceArg, '--relationship-limit', '50'))
            $commands += New-CliCommand -Name 'find' -Arguments @('find', '--workspace', $workspaceArg, '--query', $Query, '--assume-kind', $AssumeKind, '--limit', '20')
            $commands += New-CliCommand -Name 'context-pack' -Arguments (Add-ProfileArgument -Arguments @('context-pack', '--workspace', $workspaceArg, '--query', $Query, '--assume-kind', $AssumeKind, '--budget-tokens', '2000'))
        }
        'agent-loop' {
            $commands += New-CliCommand -Name 'repo-graph' -Arguments (Add-ProfileArgument -Arguments @('repo-graph', '--workspace', $workspaceArg))
            $commands += New-CliCommand -Name 'find' -Arguments @('find', '--workspace', $workspaceArg, '--query', $Query, '--assume-kind', $AssumeKind, '--limit', '20')
            $commands += New-CliCommand -Name 'about' -Arguments @('about', '--workspace', $workspaceArg, '--query', $Query, '--assume-kind', $AssumeKind)
            $commands += New-CliCommand -Name 'related' -Arguments @('related', '--workspace', $workspaceArg, '--query', $Query, '--assume-kind', $AssumeKind, '--limit', '50')
            $commands += New-CliCommand -Name 'impact' -Arguments @('impact', '--workspace', $workspaceArg, '--query', $Query, '--assume-kind', $AssumeKind, '--depth', '2')
            $commands += New-CliCommand -Name 'entrypoints' -Arguments @('entrypoints', '--workspace', $workspaceArg, '--query', $Query, '--assume-kind', $AssumeKind, '--framework-aware', '--depth', '2')
            $commands += New-CliCommand -Name 'tests-for-symbol' -Arguments (Add-ProfileArgument -Arguments @('tests-for-symbol', '--workspace', $workspaceArg, '--query', $Query, '--assume-kind', $AssumeKind, '--test-limit', '20'))
            $batchInput = @{
                requests = @(
                    @{ id = 'repo'; command = 'repo-graph'; profile = $Profile; relationshipLimit = 50 },
                    @{ id = 'find'; command = 'find'; query = $Query; assumeKind = $AssumeKind; limit = 20 },
                    @{ id = 'tests'; command = 'tests-for-symbol'; query = $Query; assumeKind = $AssumeKind; profile = $Profile; testLimit = 20 }
                )
            } | ConvertTo-Json -Depth 20 -Compress
            $commands += New-CliCommand -Name 'batch-agent-loop' -Arguments @('batch', '--workspace', $workspaceArg) -StandardInput $batchInput
        }
        'diff' {
            $diffArgs = @()
            if (![string]::IsNullOrWhiteSpace($Base)) {
                $diffArgs += @('--base', $Base)
            }
            if (![string]::IsNullOrWhiteSpace($Head)) {
                $diffArgs += @('--head', $Head)
            }

            $commands += New-CliCommand -Name 'changed-symbols' -Arguments (Add-ProfileArgument -Arguments (@('changed-symbols', '--workspace', $workspaceArg, '--symbol-limit', '50') + $diffArgs))
            $commands += New-CliCommand -Name 'impact-diff' -Arguments (Add-ProfileArgument -Arguments (@('impact-diff', '--workspace', $workspaceArg, '--impact-limit', '50', '--depth', '2') + $diffArgs))
            $commands += New-CliCommand -Name 'diagnostics-diff' -Arguments (Add-ProfileArgument -Arguments (@('diagnostics-diff', '--workspace', $workspaceArg, '--diagnostic-limit', '50') + $diffArgs))
            $commands += New-CliCommand -Name 'review-diff' -Arguments (Add-ProfileArgument -Arguments (@('review-diff', '--workspace', $workspaceArg, '--symbol-limit', '20', '--impact-limit', '50', '--diagnostic-limit', '50', '--related-test-limit', '20') + $diffArgs))
            $commands += New-CliCommand -Name 'context-pack-diff' -Arguments (Add-ProfileArgument -Arguments (@('context-pack', '--workspace', $workspaceArg, '--diff', '--budget-tokens', '4000') + $diffArgs))
            $commands += New-CliCommand -Name 'tests-for-diff' -Arguments (Add-ProfileArgument -Arguments (@('tests-for-diff', '--workspace', $workspaceArg, '--test-limit', '20') + $diffArgs))
            $publicApiBase = if ([string]::IsNullOrWhiteSpace($Base)) { 'HEAD' } else { $Base }
            $commands += New-CliCommand -Name 'public-api-diff' -Arguments (Add-ProfileArgument -Arguments @('public-api-diff', '--workspace', $workspaceArg, '--base', $publicApiBase, '--change-limit', '20'))
        }
        'mcp' {
            return @()
        }
    }

    return $commands
}

function Get-ScenarioNames {
    if ($Scenario -eq 'all') {
        return @('quick', 'agent-loop', 'diff', 'mcp')
    }

    return @($Scenario)
}

$measurements = New-Object System.Collections.Generic.List[object]
$scenarioNames = Get-ScenarioNames
foreach ($scenarioName in $scenarioNames) {
    if ($scenarioName -eq 'mcp') {
        if (!(Test-Path -LiteralPath $McpDll)) {
            $measurements.Add((New-SkippedMeasurement -Name 'mcp' -Kind 'mcp' -Reason "MCP server assembly does not exist: $McpDll"))
        }
        else {
            $measurements.Add((New-SkippedMeasurement -Name 'mcp' -Kind 'mcp' -Reason 'MCP tool-call latency requires the SDK probe path; use Navlyn.Tests.Mcp coverage until the probe is promoted into this script.'))
        }
        continue
    }

    $commands = Get-ScenarioCommands -ScenarioName $scenarioName
    foreach ($command in $commands) {
        for ($iteration = 1; $iteration -le ($Warmup + $Iterations); $iteration++) {
            $isWarmup = $iteration -le $Warmup
            $result = Invoke-MeasuredProcess -Name $command.name -Kind $command.kind -FilePath 'dotnet' -Arguments $command.arguments -StandardInput $command.standardInput -Iteration ([Math]::Max(1, $iteration - $Warmup)) -IsWarmup $isWarmup
            $measurements.Add($result)
        }
    }
}

if ($IncludeGeneratedComparison) {
    $workspaceArg = ConvertTo-RepositoryPath -Path $workspacePath
    $comparisonCommands = @(
        New-CliCommand -Name 'find-exclude-generated' -Arguments @('find', '--workspace', $workspaceArg, '--query', $Query, '--assume-kind', $AssumeKind, '--exclude-generated', '--limit', '20'),
        New-CliCommand -Name 'review-diff-exclude-generated' -Arguments (Add-ProfileArgument -Arguments @('review-diff', '--workspace', $workspaceArg, '--exclude-generated', '--symbol-limit', '20', '--impact-limit', '50', '--diagnostic-limit', '50', '--related-test-limit', '20'))
    )
    foreach ($command in $comparisonCommands) {
        for ($iteration = 1; $iteration -le $Iterations; $iteration++) {
            $measurements.Add((Invoke-MeasuredProcess -Name $command.name -Kind 'generated-comparison' -FilePath 'dotnet' -Arguments $command.arguments -StandardInput $command.standardInput -Iteration $iteration -IsWarmup $false))
        }
    }
}

$measured = @($measurements | Where-Object { -not $_.warmup -and -not $_.skipped })
$elapsedValues = @($measured | ForEach-Object { $_.elapsedMs } | Sort-Object)
$median = if ($elapsedValues.Count -eq 0) { 0 } else { $elapsedValues[[int][Math]::Floor(($elapsedValues.Count - 1) / 2)] }
$p95Index = if ($elapsedValues.Count -eq 0) { 0 } else { [int][Math]::Ceiling($elapsedValues.Count * 0.95) - 1 }
$p95 = if ($elapsedValues.Count -eq 0) { 0 } else { $elapsedValues[[Math]::Min($p95Index, $elapsedValues.Count - 1)] }
$maxStdoutChars = if ($measured.Count -eq 0) { 0 } else { ($measured | Measure-Object -Property stdoutChars -Maximum).Maximum }
$truncatedCount = @($measured | Where-Object { $_.truncated -eq $true }).Count
$succeededCount = @($measured | Where-Object { $_.exitCode -eq 0 }).Count
$failedCount = @($measured | Where-Object { $_.exitCode -ne 0 }).Count
$skippedCount = @($measurements | Where-Object { $_.skipped -eq $true }).Count
$reportWorkspace = $workspacePath.Replace('\', '/')
if ($workspacePath.StartsWith($repoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    $reportWorkspace = $workspacePath.Substring($repoRoot.Length).TrimStart('\', '/').Replace('\', '/')
}
$measurementArray = $measurements.ToArray()
$dotnetVersion = [string](& dotnet --version)
$environment = [ordered]@{
    os = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
    dotnetVersion = $dotnetVersion
    processorCount = [Environment]::ProcessorCount
}
$summary = [ordered]@{
    totalCommands = @($measured).Count
    succeeded = $succeededCount
    failed = $failedCount
    skipped = $skippedCount
    medianElapsedMs = $median
    p95ElapsedMs = $p95
    maxStdoutChars = $maxStdoutChars
    anyTruncated = $truncatedCount -gt 0
}

$report = [ordered]@{
    schemaVersion = 'navlyn.performance-report.v1'
    createdUtc = [DateTimeOffset]::UtcNow.ToString('o')
    workspace = $reportWorkspace
    scenario = $Scenario
    profile = $Profile
    environment = $environment
    summary = $summary
    measurements = $measurementArray
}

$json = $report | ConvertTo-Json -Depth 100
if ([string]::IsNullOrWhiteSpace($Output)) {
    $json
}
else {
    $outputPath = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $Output))
    $outputDirectory = [System.IO.Path]::GetDirectoryName($outputPath)
    if (![string]::IsNullOrWhiteSpace($outputDirectory)) {
        [System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
    }

    Set-Content -LiteralPath $outputPath -Value $json -Encoding utf8
}

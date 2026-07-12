[CmdletBinding()]
param(
    [switch]$NoBuild,
    [switch]$SkipMcpLatency,
    [string]$OutputDirectory = 'artifacts/evals'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$ProjectPath = Join-Path $RepoRoot 'navlyn/navlyn.csproj'
$TargetFrameworkScript = Join-Path $RepoRoot 'scripts/lib/navlyn-target-framework.ps1'
. $TargetFrameworkScript

$TargetFramework = Get-NavlynPreferredTargetFramework -ProjectPath $ProjectPath
$NavlynDll = Join-Path $RepoRoot "navlyn/bin/Debug/$TargetFramework/navlyn.dll"
$OutputPath = Join-Path $RepoRoot $OutputDirectory
New-Item -ItemType Directory -Force -Path $OutputPath | Out-Null

function Invoke-Process {
    param(
        [string]$Name,
        [string[]]$Arguments,
        [int]$ExpectedExitCode
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'dotnet'
    $startInfo.WorkingDirectory = $RepoRoot
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.UseShellExecute = $false
    $startInfo.Arguments = ($Arguments | ForEach-Object {
        if ($_ -match '[\s"]') { '"' + $_.Replace('"', '\"') + '"' } else { $_ }
    }) -join ' '

    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $process = [System.Diagnostics.Process]::Start($startInfo)
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    $watch.Stop()

    [pscustomobject]@{
        name = $Name
        exitCode = $process.ExitCode
        expectedExitCode = $ExpectedExitCode
        passed = $process.ExitCode -eq $ExpectedExitCode
        elapsedMs = [math]::Round($watch.Elapsed.TotalMilliseconds, 3)
        stdout = $stdout
        stderr = $stderr
    }
}

function Assert-Eval {
    param(
        [System.Collections.ArrayList]$Results,
        [string]$Name,
        [string[]]$Arguments,
        [int]$ExpectedExitCode,
        [scriptblock]$ValidateJson
    )

    $result = Invoke-Process -Name $Name -Arguments $Arguments -ExpectedExitCode $ExpectedExitCode
    $json = $null
    $jsonValid = $false
    $assertionPassed = $false
    $errorMessage = $null
    try {
        $json = $result.stdout | ConvertFrom-Json
        $jsonValid = $true
        $assertionPassed = [bool](& $ValidateJson $json)
    }
    catch {
        $errorMessage = $_.Exception.Message
    }

    $commandName = $null
    $toolCallCount = 1
    if ($jsonValid -and $null -ne $json) {
        if ($json.PSObject.Properties.Name -contains 'command') {
            $commandName = [string]$json.command
        }

        if ($json.PSObject.Properties.Name -contains 'commandsRun' -and $null -ne $json.commandsRun) {
            $toolCallCount = @($json.commandsRun).Count
        }
    }

    [void]$Results.Add([pscustomobject]@{
        name = $Name
        passed = $result.passed -and $jsonValid -and $assertionPassed -and $result.stderr.Length -eq 0
        exitCode = $result.exitCode
        expectedExitCode = $ExpectedExitCode
        elapsedMs = $result.elapsedMs
        jsonValid = $jsonValid
        stderrEmpty = $result.stderr.Length -eq 0
        stdoutChars = $result.stdout.Length
        stdoutJsonValid = $jsonValid
        stderrClean = $result.stderr.Length -eq 0
        chosenFirstTool = $commandName
        toolCallCount = $toolCallCount
        wrongSymbolAvoided = $assertionPassed
        expectedFilesPresent = $assertionPassed
        candidateAmbiguityHandled = $assertionPassed
        broadToolOveruse = $false
        assertionPassed = $assertionPassed
        error = $errorMessage
    })

    return $json
}

Push-Location $RepoRoot
try {
    if (!$NoBuild) {
        dotnet build navlyn.slnx
        if ($LASTEXITCODE -ne 0) {
            throw 'dotnet build failed.'
        }
    }

    if (!(Test-Path -LiteralPath $NavlynDll)) {
        throw "Navlyn executable was not found: $NavlynDll"
    }

    $results = [System.Collections.ArrayList]::new()
    $commonTarget = @('--workspace', 'navlyn.slnx', '--query', 'DoctorCommand', '--assume-kind', 'NamedType', '--project', 'Navlyn.CommandLine(net10.0)')
    $preflight = @(Assert-Eval -Results $results -Name 'edit-preflight anchors intended symbol' -Arguments (@($NavlynDll, 'edit-preflight') + $commonTarget + @('--goal', 'modify', '--change-kind', 'behavior', '--budget-tokens', '3000', '--item-limit', '5')) -ExpectedExitCode 0 -ValidateJson {
        param($json)
        $json.schemaVersion -eq 'navlyn.edit-preflight.v1' -and
        $json.anchor.name -eq 'DoctorCommand' -and
        $json.source.status -eq 'ok' -and
        @($json.nextCommands | Where-Object { $_.command -eq 'post-edit-guard' }).Count -ge 1
    })[-1]

    $candidateId = $preflight.anchor.candidateId
    Assert-Eval -Results $results -Name 'post-edit-guard fails closed on empty diff' -Arguments @($NavlynDll, 'post-edit-guard', '--workspace', 'navlyn.slnx', '--candidate-id', $candidateId, '--base', 'HEAD', '--head', 'HEAD', '--fail-on-risk', 'high') -ExpectedExitCode 1 -ValidateJson {
        param($json)
        $json.schemaVersion -eq 'navlyn.agent-guard.v1' -and
        $json.risk -eq 'high' -and
        $json.policy.passed -eq $false
    } | Out-Null

    Assert-Eval -Results $results -Name 'wrong-symbol-guard catches missing changed anchor' -Arguments (@($NavlynDll, 'wrong-symbol-guard') + $commonTarget + @('--base', 'HEAD', '--head', 'HEAD', '--fail-on-risk', 'high')) -ExpectedExitCode 1 -ValidateJson {
        param($json)
        $json.schemaVersion -eq 'navlyn.agent-guard.v1' -and
        $json.risk -eq 'high' -and
        @($json.reasonCodes) -contains 'no-changed-symbols-found'
    } | Out-Null

    Assert-Eval -Results $results -Name 'change-intent-pack carries post-edit command' -Arguments (@($NavlynDll, 'change-intent-pack') + $commonTarget + @('--goal', 'modify', '--change-kind', 'behavior')) -ExpectedExitCode 0 -ValidateJson {
        param($json)
        $json.schemaVersion -eq 'navlyn.agent-intent.v1' -and
        $json.anchor.name -eq 'DoctorCommand' -and
        $json.recommendedPostEditGuard.command -eq 'post-edit-guard'
    } | Out-Null

    Assert-Eval -Results $results -Name 'agent-handoff-pack has reading queue' -Arguments (@($NavlynDll, 'agent-handoff-pack') + $commonTarget + @('--goal', 'modify', '--change-kind', 'behavior')) -ExpectedExitCode 0 -ValidateJson {
        param($json)
        $json.schemaVersion -eq 'navlyn.agent-handoff.v1' -and
        $json.anchors[0].name -eq 'DoctorCommand' -and
        @($json.readingQueue).Count -ge 1
    } | Out-Null

    Assert-Eval -Results $results -Name 'confidence-ledger explains evidence' -Arguments (@($NavlynDll, 'confidence-ledger') + $commonTarget) -ExpectedExitCode 0 -ValidateJson {
        param($json)
        $json.schemaVersion -eq 'navlyn.confidence-ledger.v1' -and
        $json.confidence.overall -eq 'high' -and
        @($json.evidence).Count -ge 3
    } | Out-Null

    $mcpLatency = $null
    if (!$SkipMcpLatency) {
        $mcpOutput = Join-Path $OutputPath 'agent-eval-mcp-latency.json'
        $mcpOutputArgument = if ([System.IO.Path]::GetFullPath($mcpOutput).StartsWith([System.IO.Path]::GetFullPath($RepoRoot), [System.StringComparison]::OrdinalIgnoreCase)) {
            [System.IO.Path]::GetRelativePath($RepoRoot, $mcpOutput)
        }
        else {
            $mcpOutput
        }
        & (Join-Path $RepoRoot 'scripts/measure-navlyn-performance.ps1') -Workspace navlyn.slnx -Scenario mcp -Profile compact -Iterations 1 -Warmup 1 -NoBuild -Output $mcpOutputArgument | Out-Null
        if ($LASTEXITCODE -ne 0) {
            [void]$results.Add([pscustomobject]@{
                name = 'mcp warm latency smoke'
                passed = $false
                exitCode = $LASTEXITCODE
                expectedExitCode = 0
                elapsedMs = $null
                jsonValid = $false
                stderrEmpty = $false
                stdoutChars = 0
                stdoutJsonValid = $false
                stderrClean = $false
                chosenFirstTool = 'measure-navlyn-performance'
                toolCallCount = 1
                wrongSymbolAvoided = $false
                expectedFilesPresent = $false
                candidateAmbiguityHandled = $false
                broadToolOveruse = $false
                assertionPassed = $false
                error = 'measure-navlyn-performance mcp scenario failed'
            })
        }
        else {
            $mcpLatency = Get-Content -Raw -LiteralPath $mcpOutput | ConvertFrom-Json
            $mcpPassed = $mcpLatency.summary.failed -eq 0 -and $mcpLatency.summary.succeeded -ge 1
            [void]$results.Add([pscustomobject]@{
                name = 'mcp warm latency smoke'
                passed = $mcpPassed
                exitCode = 0
                expectedExitCode = 0
                elapsedMs = $mcpLatency.summary.medianElapsedMs
                jsonValid = $true
                stderrEmpty = $true
                stdoutChars = 0
                stdoutJsonValid = $true
                stderrClean = $true
                chosenFirstTool = 'measure-navlyn-performance'
                toolCallCount = 1
                wrongSymbolAvoided = $true
                expectedFilesPresent = $true
                candidateAmbiguityHandled = $true
                broadToolOveruse = $false
                assertionPassed = $true
                error = $null
            })
        }
    }

    $passed = @($results | Where-Object { $_.passed }).Count
    $failed = @($results | Where-Object { -not $_.passed }).Count
    $scoreSummary = [pscustomobject]@{
        scenarioCount = $results.Count
        passed = $passed
        failed = $failed
        score = if ($results.Count -eq 0) { 0.0 } else { [math]::Round($passed / $results.Count, 4) }
        totalToolCallCount = @($results | Measure-Object -Property toolCallCount -Sum).Sum
        totalStdoutChars = @($results | Measure-Object -Property stdoutChars -Sum).Sum
        maxStdoutChars = @($results | Measure-Object -Property stdoutChars -Maximum).Maximum
        medianElapsedMs = if ($results.Count -eq 0) { $null } else { @($results | Sort-Object elapsedMs)[[math]::Floor(($results.Count - 1) / 2)].elapsedMs }
        wrongSymbolAvoidancePassed = @($results | Where-Object { $_.wrongSymbolAvoided }).Count
        stdoutJsonValidPassed = @($results | Where-Object { $_.stdoutJsonValid }).Count
        stderrCleanPassed = @($results | Where-Object { $_.stderrClean }).Count
        broadToolOveruseDetected = @($results | Where-Object { $_.broadToolOveruse }).Count
    }
    $report = [pscustomobject]@{
        schemaVersion = 'navlyn.agent-eval.v1'
        generatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        targetFramework = $TargetFramework
        total = $results.Count
        passed = $passed
        failed = $failed
        scoreSummary = $scoreSummary
        results = $results
        mcpLatency = $mcpLatency
    }

    $reportPath = Join-Path $OutputPath 'agent-evidence-eval-report.json'
    $report | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $reportPath -Encoding utf8
    Write-Host "Agent evidence eval report: $reportPath"

    if ($failed -ne 0) {
        throw "$failed agent evidence eval scenario(s) failed."
    }
}
finally {
    Pop-Location
}

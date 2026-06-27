[CmdletBinding()]
param(
    [switch]$NoBuild,
    [switch]$ShowOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$SolutionPath = Join-Path $RepoRoot 'navlyn.slnx'
$ProjectPath = Join-Path $RepoRoot 'navlyn/navlyn.csproj'
$ProjectDir = Join-Path $RepoRoot 'navlyn'
$NormalizeScript = Join-Path $RepoRoot 'scripts/normalize-csharp-files.ps1'
$FormatCheckScript = Join-Path $RepoRoot 'scripts/test-csharp-file-format.ps1'

[xml]$ProjectXml = Get-Content -Raw -LiteralPath $ProjectPath
$TargetFramework = [string]$ProjectXml.Project.PropertyGroup.TargetFramework
$NavlynDll = Join-Path $ProjectDir "bin/Debug/$TargetFramework/navlyn.dll"

function Invoke-CheckedProcess {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [int]$ExpectedExitCode,

        [string]$WorkingDirectory = $RepoRoot,

        [string]$StandardInput = $null
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.RedirectStandardInput = $null -ne $StandardInput
    $startInfo.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $startInfo.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    $startInfo.UseShellExecute = $false
    $startInfo.Arguments = Join-ProcessArguments -Arguments $Arguments

    $process = [System.Diagnostics.Process]::Start($startInfo)
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    if ($null -ne $StandardInput) {
        $process.StandardInput.Write($StandardInput)
        $process.StandardInput.Close()
    }

    $process.WaitForExit()
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    $exitCode = $process.ExitCode

    if ($exitCode -ne $ExpectedExitCode) {
        throw @"
$Name failed with exit code $exitCode. Expected $ExpectedExitCode.
Command: $FilePath $($Arguments -join ' ')
stdout:
$stdout
stderr:
$stderr
"@
    }

    $result = [pscustomobject]@{
        Name = $Name
        ExitCode = $exitCode
        Stdout = $stdout
        Stderr = $stderr
    }

    if ($ShowOutput) {
        Write-ProcessResult `
            -Name $Name `
            -FilePath $FilePath `
            -Arguments $Arguments `
            -Result $result
    }

    $result
}

function Write-ProcessResult {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [psobject]$Result
    )

    Write-Host ''
    Write-Host "[$Name]"
    Write-Host "command: $FilePath $($Arguments -join ' ')"
    Write-Host "exit: $($Result.ExitCode)"
    Write-Host 'stdout:'
    if ($Result.Stdout.Length -eq 0) {
        Write-Host '<empty>'
    }
    else {
        Write-Host $Result.Stdout.TrimEnd()
    }

    Write-Host 'stderr:'
    if ($Result.Stderr.Length -eq 0) {
        Write-Host '<empty>'
    }
    else {
        Write-Host $Result.Stderr.TrimEnd()
    }
}

function Join-ProcessArguments {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    ($Arguments | ForEach-Object { ConvertTo-ProcessArgument -Argument $_ }) -join ' '
}

function ConvertTo-ProcessArgument {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Argument
    )

    if ($Argument.Length -eq 0) {
        return '""'
    }

    if ($Argument.IndexOfAny([char[]]@(' ', "`t", '"')) -lt 0) {
        return $Argument
    }

    return '"' + $Argument.Replace('"', '\"') + '"'
}

function Invoke-Navlyn {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [int]$ExpectedExitCode,

        [string]$WorkingDirectory = $RepoRoot,

        [string]$StandardInput = $null
    )

    $dotnetArguments = @($NavlynDll) + $Arguments

    Invoke-CheckedProcess `
        -Name $Name `
        -FilePath 'dotnet' `
        -Arguments $dotnetArguments `
        -ExpectedExitCode $ExpectedExitCode `
        -WorkingDirectory $WorkingDirectory `
        -StandardInput $StandardInput
}

function Assert-Contains {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Text,

        [Parameter(Mandatory = $true)]
        [string]$Expected
    )

    if ($Text.IndexOf($Expected, [StringComparison]::Ordinal) -lt 0) {
        throw "$Name did not contain expected text '$Expected'. Actual text: $Text"
    }
}

function Assert-Equal {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [AllowNull()]
        [object]$Actual,

        [AllowNull()]
        [object]$Expected
    )

    if ($Actual -ne $Expected) {
        throw "$Name expected '$Expected' but was '$Actual'."
    }
}

function Assert-Empty {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Text
    )

    if ($Text.Length -ne 0) {
        throw "$Name was expected to be empty. Actual text: $Text"
    }
}

Push-Location $RepoRoot
try {
    & $NormalizeScript -Quiet
    & $FormatCheckScript -Quiet

    if (!$NoBuild) {
        Write-Host 'Building navlyn...'
        Invoke-CheckedProcess `
            -Name 'dotnet build' `
            -FilePath 'dotnet' `
            -Arguments @('build', $SolutionPath) `
            -ExpectedExitCode 0 | Out-Null
    }

    if (!(Test-Path -LiteralPath $NavlynDll)) {
        throw "Navlyn executable was not found: $NavlynDll. Run without -NoBuild first."
    }

    Write-Host 'Running CLI contract checks...'

    $rootHelp = Invoke-Navlyn `
        -Name 'root help text' `
        -Arguments @('--help') `
        -ExpectedExitCode 0

    Assert-Empty -Name 'root help stderr' -Text $rootHelp.Stderr
    Assert-Contains -Name 'root help description' -Text $rootHelp.Stdout -Expected 'Semantic code navigation and investigation for agents and automation.'

    $rootNoArguments = Invoke-Navlyn `
        -Name 'root no arguments help text' `
        -Arguments @() `
        -ExpectedExitCode 2

    Assert-Empty -Name 'root no arguments stdout' -Text $rootNoArguments.Stdout
    Assert-Contains -Name 'root no arguments parse error' -Text $rootNoArguments.Stderr -Expected 'NAVLYN1001:'
    Assert-Contains -Name 'root no arguments help description' -Text $rootNoArguments.Stderr -Expected 'Semantic code navigation and investigation for agents and automation.'

    $contextPackHelp = Invoke-Navlyn `
        -Name 'context-pack help text' `
        -Arguments @('context-pack', '--help') `
        -ExpectedExitCode 0

    Assert-Empty -Name 'context-pack help stderr' -Text $contextPackHelp.Stderr
    Assert-Contains -Name 'context-pack help diagnostic limit' -Text $contextPackHelp.Stdout -Expected 'Defaults to 50 in query mode and 100 in diff mode.'

    $validCheck = Invoke-Navlyn `
        -Name 'check valid workspace' `
        -Arguments @('check', '--workspace', 'navlyn.slnx') `
        -ExpectedExitCode 0

    $validJson = $validCheck.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'check valid workspace ok' -Actual $validJson.ok -Expected $true
    Assert-Equal -Name 'check valid workspace workspace' -Actual $validJson.workspace -Expected 'navlyn.slnx'
    Assert-Equal -Name 'check valid workspace kind' -Actual $validJson.kind -Expected 'solution'
    Assert-Equal -Name 'check valid workspace projects' -Actual $validJson.projects -Expected 3

    $overview = Invoke-Navlyn `
        -Name 'overview valid workspace' `
        -Arguments @('overview', '--workspace', 'navlyn.slnx') `
        -ExpectedExitCode 0

    $overviewJson = $overview.Stdout | ConvertFrom-Json
    $overviewProject = @($overviewJson.projects | Where-Object { $_.name -eq 'navlyn' })[0]
    Assert-Equal -Name 'overview workspace' -Actual $overviewJson.workspace -Expected 'navlyn.slnx'
    Assert-Equal -Name 'overview kind' -Actual $overviewJson.kind -Expected 'solution'
    Assert-Equal -Name 'overview project count' -Actual @($overviewJson.projects).Count -Expected 3
    Assert-Equal -Name 'overview project name' -Actual $overviewProject.name -Expected 'navlyn'
    Assert-Equal -Name 'overview project path' -Actual $overviewProject.path -Expected 'navlyn/navlyn.csproj'
    Assert-Equal -Name 'overview project language' -Actual $overviewProject.language -Expected 'C#'
    Assert-Equal -Name 'overview project assembly name' -Actual $overviewProject.assemblyName -Expected 'navlyn'

    $repoGraph = Invoke-Navlyn `
        -Name 'repo-graph valid workspace' `
        -Arguments @('repo-graph', '--workspace', 'navlyn.slnx') `
        -ExpectedExitCode 0

    Assert-Empty -Name 'repo-graph stderr' -Text $repoGraph.Stderr
    $repoGraphJson = $repoGraph.Stdout | ConvertFrom-Json
    $repoGraphNavlynProject = @($repoGraphJson.projects.items | Where-Object { $_.name -eq 'navlyn' })[0]
    $repoGraphTestProject = @($repoGraphJson.projects.items | Where-Object { $_.name -eq 'navlyn.Tests' })[0]
    Assert-Equal -Name 'repo-graph command' -Actual $repoGraphJson.command -Expected 'repo-graph'
    Assert-Equal -Name 'repo-graph workspace' -Actual $repoGraphJson.workspace -Expected 'navlyn.slnx'
    Assert-Equal -Name 'repo-graph project count' -Actual $repoGraphJson.projects.totalProjects -Expected 3
    Assert-Equal -Name 'repo-graph navlyn classification' -Actual $repoGraphNavlynProject.classification.kind -Expected 'tooling'
    Assert-Equal -Name 'repo-graph test classification' -Actual $repoGraphTestProject.classification.kind -Expected 'test'
    Assert-Equal -Name 'repo-graph package edge present' -Actual (@($repoGraphJson.edges.packageReferences | Where-Object { $_.name -eq 'System.CommandLine' }).Count -ge 1) -Expected $true
    Assert-Equal -Name 'repo-graph test relationship present' -Actual (@($repoGraphJson.relationships.items | Where-Object { $_.kind -eq 'tests' }).Count -ge 1) -Expected $true

    $subdirectoryOverview = Invoke-Navlyn `
        -Name 'overview from subdirectory with repo-relative workspace' `
        -Arguments @('overview', '--workspace', 'navlyn.slnx') `
        -ExpectedExitCode 0 `
        -WorkingDirectory $ProjectDir

    $subdirectoryOverviewJson = $subdirectoryOverview.Stdout | ConvertFrom-Json
    $subdirectoryOverviewProject = @($subdirectoryOverviewJson.projects | Where-Object { $_.name -eq 'navlyn' })[0]
    Assert-Equal -Name 'subdirectory overview workspace' -Actual $subdirectoryOverviewJson.workspace -Expected 'navlyn.slnx'
    Assert-Equal -Name 'subdirectory overview project path' -Actual $subdirectoryOverviewProject.path -Expected 'navlyn/navlyn.csproj'

    $subdirectoryProjectFilter = Invoke-Navlyn `
        -Name 'diagnostics from subdirectory with repo-relative project filter' `
        -Arguments @('diagnostics', '--workspace', 'navlyn.slnx', '--project', 'navlyn/navlyn.csproj', '--limit', '1') `
        -ExpectedExitCode 0 `
        -WorkingDirectory $ProjectDir

    $subdirectoryProjectFilterJson = $subdirectoryProjectFilter.Stdout | ConvertFrom-Json
    $subdirectoryAppliedProject = @($subdirectoryProjectFilterJson.projects)[0]
    Assert-Equal -Name 'subdirectory project filter path' -Actual $subdirectoryAppliedProject.path -Expected 'navlyn/navlyn.csproj'

    $subdirectorySymbolAt = Invoke-Navlyn `
        -Name 'symbol-at from subdirectory with repo-relative source file' `
        -Arguments @('symbol-at', '--workspace', 'navlyn.slnx', '--file', 'navlyn/Cli/Commands/CheckCommand.cs', '--line', '6', '--column', '23') `
        -ExpectedExitCode 0 `
        -WorkingDirectory $ProjectDir

    $subdirectorySymbolAtJson = $subdirectorySymbolAt.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'subdirectory symbol-at file' -Actual $subdirectorySymbolAtJson.file -Expected 'navlyn/Cli/Commands/CheckCommand.cs'
    Assert-Equal -Name 'subdirectory symbol-at symbol path' -Actual $subdirectorySymbolAtJson.symbol.path -Expected 'navlyn/Cli/Commands/CheckCommand.cs'

    $diagnostics = Invoke-Navlyn `
        -Name 'diagnostics valid workspace' `
        -Arguments @('diagnostics', '--workspace', 'navlyn.slnx') `
        -ExpectedExitCode 0

    $diagnosticsJson = $diagnostics.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'diagnostics workspace' -Actual $diagnosticsJson.workspace -Expected 'navlyn.slnx'
    Assert-Equal -Name 'diagnostics kind' -Actual $diagnosticsJson.kind -Expected 'solution'
    Assert-Equal -Name 'diagnostics total is non-negative' -Actual ($diagnosticsJson.totalDiagnostics -ge 0) -Expected $true

    $reviewDiff = Invoke-Navlyn `
        -Name 'review-diff valid workspace' `
        -Arguments @('review-diff', '--workspace', 'navlyn.slnx', '--symbol-limit', '1', '--impact-limit', '1', '--diagnostic-limit', '1', '--related-test-limit', '1', '--depth', '1') `
        -ExpectedExitCode 0

    Assert-Empty -Name 'review-diff stderr' -Text $reviewDiff.Stderr
    $reviewDiffJson = $reviewDiff.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'review-diff workspace' -Actual $reviewDiffJson.workspace -Expected 'navlyn.slnx'
    Assert-Equal -Name 'review-diff kind' -Actual $reviewDiffJson.kind -Expected 'solution'
    Assert-Equal -Name 'review-diff command' -Actual $reviewDiffJson.command -Expected 'review-diff'
    Assert-Equal -Name 'review-diff schema version' -Actual $reviewDiffJson.schemaVersion -Expected 'navlyn.workflow.v1'
    Assert-Equal -Name 'review-diff default profile' -Actual $reviewDiffJson.profile -Expected 'full'
    Assert-Equal -Name 'review-diff total files non-negative' -Actual ($reviewDiffJson.diff.totalFiles -ge 0) -Expected $true
    Assert-Equal -Name 'review-diff symbols non-negative' -Actual ($reviewDiffJson.changedSymbols.totalSymbols -ge 0) -Expected $true
    Assert-Equal -Name 'review-diff findings non-negative' -Actual (@($reviewDiffJson.findings).Count -ge 0) -Expected $true

    $reviewPackFixture = 'tests/fixtures/ReviewPacksFixture/ReviewPacksFixture.csproj'
    $reviewPack = Invoke-Navlyn `
        -Name 'review-pack fixture workspace' `
        -Arguments @('review-pack', '--workspace', $reviewPackFixture, '--scope', 'workspace', '--pack', 'all', '--architecture-config', 'tests/fixtures/ReviewPacksFixture/.navlyn.yml', '--profile', 'evidence', '--finding-limit', '20') `
        -ExpectedExitCode 0

    Assert-Empty -Name 'review-pack stderr' -Text $reviewPack.Stderr
    $reviewPackJson = $reviewPack.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'review-pack command' -Actual $reviewPackJson.command -Expected 'review-pack'
    Assert-Equal -Name 'review-pack profile' -Actual $reviewPackJson.profile -Expected 'evidence'
    Assert-Equal -Name 'review-pack scope' -Actual $reviewPackJson.scope.mode -Expected 'workspace'
    Assert-Equal -Name 'review-pack has async finding' -Actual (@($reviewPackJson.findings | Where-Object { $_.ruleId -eq 'async.sync-over-async' }).Count -ge 1) -Expected $true
    Assert-Equal -Name 'review-pack has architecture finding' -Actual (@($reviewPackJson.findings | Where-Object { $_.ruleId -eq 'architecture.namespace-dependency-violation' }).Count -ge 1) -Expected $true

    $reviewPackInvalid = Invoke-Navlyn `
        -Name 'review-pack invalid pack' `
        -Arguments @('review-pack', '--workspace', $reviewPackFixture, '--pack', 'bogus') `
        -ExpectedExitCode 2
    Assert-Empty -Name 'review-pack invalid stdout' -Text $reviewPackInvalid.Stdout
    Assert-Contains -Name 'review-pack invalid stderr' -Text $reviewPackInvalid.Stderr -Expected 'NAVLYN1001:'

    $publicApiDiff = Invoke-Navlyn `
        -Name 'public-api-diff valid workspace' `
        -Arguments @('public-api-diff', '--workspace', 'navlyn.slnx', '--base', 'HEAD', '--project', 'navlyn', '--change-limit', '5') `
        -ExpectedExitCode 0

    Assert-Empty -Name 'public-api-diff stderr' -Text $publicApiDiff.Stderr
    $publicApiDiffJson = $publicApiDiff.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'public-api-diff command' -Actual $publicApiDiffJson.command -Expected 'public-api-diff'
    Assert-Equal -Name 'public-api-diff base' -Actual $publicApiDiffJson.comparison.base -Expected 'HEAD'
    Assert-Equal -Name 'public-api-diff head' -Actual $publicApiDiffJson.comparison.head -Expected 'workingTree'
    Assert-Equal -Name 'public-api-diff change limit' -Actual $publicApiDiffJson.limits.changeLimit -Expected 5
    Assert-Equal -Name 'public-api-diff total changes non-negative' -Actual ($publicApiDiffJson.summary.totalChanges -ge 0) -Expected $true

    $publicApiDiffInvalid = Invoke-Navlyn `
        -Name 'public-api-diff missing base' `
        -Arguments @('public-api-diff', '--workspace', 'navlyn.slnx') `
        -ExpectedExitCode 2
    Assert-Empty -Name 'public-api-diff missing base stdout' -Text $publicApiDiffInvalid.Stdout
    Assert-Contains -Name 'public-api-diff missing base stderr' -Text $publicApiDiffInvalid.Stderr -Expected 'NAVLYN1503:'

    $testsForSymbol = Invoke-Navlyn `
        -Name 'tests-for-symbol query mode' `
        -Arguments @('tests-for-symbol', '--workspace', 'navlyn.slnx', '--query', 'RepoGraphResolver', '--assume-kind', 'NamedType', '--project', 'navlyn', '--test-project', 'navlyn.Tests', '--test-limit', '5') `
        -ExpectedExitCode 0

    Assert-Empty -Name 'tests-for-symbol stderr' -Text $testsForSymbol.Stderr
    $testsForSymbolJson = $testsForSymbol.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'tests-for-symbol command' -Actual $testsForSymbolJson.command -Expected 'tests-for-symbol'
    Assert-Equal -Name 'tests-for-symbol selection mode' -Actual $testsForSymbolJson.selectionInput.mode -Expected 'query'
    Assert-Equal -Name 'tests-for-symbol subject' -Actual $testsForSymbolJson.subject.name -Expected 'RepoGraphResolver'
    Assert-Equal -Name 'tests-for-symbol has related tests' -Actual ($testsForSymbolJson.tests.totalCandidates -ge 1) -Expected $true

    $testsForDiff = Invoke-Navlyn `
        -Name 'tests-for-diff valid workspace' `
        -Arguments @('tests-for-diff', '--workspace', 'navlyn.slnx', '--base', 'HEAD', '--project', 'navlyn', '--test-project', 'navlyn.Tests', '--symbol-limit', '1', '--test-limit', '1') `
        -ExpectedExitCode 0

    Assert-Empty -Name 'tests-for-diff stderr' -Text $testsForDiff.Stderr
    $testsForDiffJson = $testsForDiff.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'tests-for-diff command' -Actual $testsForDiffJson.command -Expected 'tests-for-diff'
    Assert-Equal -Name 'tests-for-diff total files non-negative' -Actual ($testsForDiffJson.diff.totalFiles -ge 0) -Expected $true
    Assert-Equal -Name 'tests-for-diff tests non-negative' -Actual ($testsForDiffJson.tests.totalCandidates -ge 0) -Expected $true

    $frameworkFixture = 'tests/fixtures/FrameworkEntrypointsFixture/FrameworkEntrypointsFixture.csproj'
    $frameworkEntrypoints = Invoke-Navlyn `
        -Name 'framework-entrypoints fixture' `
        -Arguments @('framework-entrypoints', '--workspace', $frameworkFixture, '--framework', 'aspnetcore', '--framework', 'worker', '--limit', '20') `
        -ExpectedExitCode 0

    Assert-Empty -Name 'framework-entrypoints stderr' -Text $frameworkEntrypoints.Stderr
    $frameworkEntrypointsJson = $frameworkEntrypoints.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'framework-entrypoints command' -Actual $frameworkEntrypointsJson.command -Expected 'framework-entrypoints'
    Assert-Equal -Name 'framework-entrypoints has controller action' -Actual (@($frameworkEntrypointsJson.entrypoints.items | Where-Object { $_.entrypointKind -eq 'aspnetcore-controller-action' -and $_.name -eq 'Get' }).Count -ge 1) -Expected $true
    Assert-Equal -Name 'framework-entrypoints has worker' -Actual (@($frameworkEntrypointsJson.entrypoints.items | Where-Object { $_.entrypointKind -eq 'worker-backgroundservice-execute' }).Count -ge 1) -Expected $true

    $frameworkAwareEntrypoints = Invoke-Navlyn `
        -Name 'entrypoints framework-aware fixture' `
        -Arguments @('entrypoints', '--workspace', $frameworkFixture, '--query', 'Get', '--assume-kind', 'Method', '--framework-aware', '--framework', 'aspnetcore', '--limit', '5') `
        -ExpectedExitCode 0

    Assert-Empty -Name 'entrypoints framework-aware stderr' -Text $frameworkAwareEntrypoints.Stderr
    $frameworkAwareEntrypointsJson = $frameworkAwareEntrypoints.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'entrypoints framework-aware flag' -Actual $frameworkAwareEntrypointsJson.frameworkAware -Expected $true
    Assert-Equal -Name 'entrypoints framework-aware matched chain' -Actual (@($frameworkAwareEntrypointsJson.chains | Where-Object { $_.endReason -eq 'framework-entrypoint' }).Count -ge 1) -Expected $true

    $diFixture = 'tests/fixtures/DependencyInjectionFixture/DependencyInjectionFixture.csproj'
    $diGraph = Invoke-Navlyn `
        -Name 'di-graph fixture' `
        -Arguments @('di-graph', '--workspace', $diFixture, '--registration-limit', '20', '--dependency-limit', '20', '--risk-limit', '20') `
        -ExpectedExitCode 0

    Assert-Empty -Name 'di-graph stderr' -Text $diGraph.Stderr
    $diGraphJson = $diGraph.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'di-graph command' -Actual $diGraphJson.command -Expected 'di-graph'
    Assert-Equal -Name 'di-graph has widget store registration' -Actual (@($diGraphJson.registrations.items | Where-Object { $_.serviceType.name -eq 'IWidgetStore' -and $_.implementationType.name -eq 'SqlWidgetStore' }).Count -ge 1) -Expected $true
    Assert-Equal -Name 'di-graph has dependency' -Actual (@($diGraphJson.dependencies.items | Where-Object { $_.implementationType.name -eq 'WidgetService' -and $_.dependencyType.name -eq 'IWidgetStore' }).Count -ge 1) -Expected $true

    $whereRegistered = Invoke-Navlyn `
        -Name 'where-registered fixture' `
        -Arguments @('where-registered', '--workspace', $diFixture, '--query', 'WidgetService', '--assume-kind', 'NamedType', '--registration-limit', '10') `
        -ExpectedExitCode 0

    Assert-Empty -Name 'where-registered stderr' -Text $whereRegistered.Stderr
    $whereRegisteredJson = $whereRegistered.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'where-registered command' -Actual $whereRegisteredJson.command -Expected 'where-registered'
    Assert-Equal -Name 'where-registered subject' -Actual $whereRegisteredJson.subject.name -Expected 'WidgetService'
    Assert-Equal -Name 'where-registered has registration' -Actual ($whereRegisteredJson.registrations.totalRegistrations -ge 1) -Expected $true

    $diImpact = Invoke-Navlyn `
        -Name 'di-impact fixture' `
        -Arguments @('di-impact', '--workspace', $diFixture, '--query', 'IWidgetStore', '--assume-kind', 'NamedType', '--registration-limit', '10', '--consumer-limit', '10') `
        -ExpectedExitCode 0

    Assert-Empty -Name 'di-impact stderr' -Text $diImpact.Stderr
    $diImpactJson = $diImpact.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'di-impact command' -Actual $diImpactJson.command -Expected 'di-impact'
    Assert-Equal -Name 'di-impact has consumer' -Actual (@($diImpactJson.consumers.items | Where-Object { $_.consumerType.name -eq 'WidgetService' }).Count -ge 1) -Expected $true

    $applicationDomainFixture = 'tests/fixtures/ApplicationDomainFixture/ApplicationDomainFixture.csproj'
    $routeMap = Invoke-Navlyn `
        -Name 'route-map fixture' `
        -Arguments @('route-map', '--workspace', $applicationDomainFixture, '--route-limit', '20', '--evidence-limit', '1', '--profile', 'evidence') `
        -ExpectedExitCode 0

    Assert-Empty -Name 'route-map stderr' -Text $routeMap.Stderr
    $routeMapJson = $routeMap.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'route-map command' -Actual $routeMapJson.command -Expected 'route-map'
    Assert-Equal -Name 'route-map has controller route' -Actual (@($routeMapJson.routes.items | Where-Object { $_.endpointKind -eq 'controller-action' -and $_.normalizedRoutePattern -eq '/orders/{id}' }).Count -ge 1) -Expected $true
    Assert-Equal -Name 'route-map has minimal route' -Actual (@($routeMapJson.routes.items | Where-Object { $_.endpointKind -eq 'minimal-api' -and $_.normalizedRoutePattern -eq '/orders' }).Count -ge 1) -Expected $true

    $optionsGraph = Invoke-Navlyn `
        -Name 'options-graph fixture' `
        -Arguments @('options-graph', '--workspace', $applicationDomainFixture, '--query', 'PaymentOptions', '--option-limit', '10', '--consumer-limit', '10', '--binding-limit', '10', '--evidence-limit', '1', '--profile', 'evidence') `
        -ExpectedExitCode 0

    Assert-Empty -Name 'options-graph stderr' -Text $optionsGraph.Stderr
    $optionsGraphJson = $optionsGraph.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'options-graph command' -Actual $optionsGraphJson.command -Expected 'options-graph'
    Assert-Equal -Name 'options-graph has binding' -Actual (@($optionsGraphJson.bindings.items | Where-Object { $_.configurationKey -eq 'Payments' }).Count -ge 1) -Expected $true
    Assert-Equal -Name 'options-graph has consumer' -Actual (@($optionsGraphJson.consumers.items | Where-Object { $_.consumerType.name -eq 'PaymentService' }).Count -ge 1) -Expected $true

    $whereHandled = Invoke-Navlyn `
        -Name 'where-handled fixture' `
        -Arguments @('where-handled', '--workspace', $applicationDomainFixture, '--query', 'CreateOrderCommand', '--assume-kind', 'NamedType', '--handler-limit', '10', '--evidence-limit', '1', '--profile', 'evidence') `
        -ExpectedExitCode 0

    Assert-Empty -Name 'where-handled stderr' -Text $whereHandled.Stderr
    $whereHandledJson = $whereHandled.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'where-handled command' -Actual $whereHandledJson.command -Expected 'where-handled'
    Assert-Equal -Name 'where-handled has handler' -Actual (@($whereHandledJson.handlers.items | Where-Object { $_.handlerType.name -eq 'CreateOrderHandler' }).Count -ge 1) -Expected $true

    $efModel = Invoke-Navlyn `
        -Name 'ef-model fixture' `
        -Arguments @('ef-model', '--workspace', $applicationDomainFixture, '--entity-limit', '20', '--query-site-limit', '20', '--evidence-limit', '1', '--profile', 'evidence') `
        -ExpectedExitCode 0

    Assert-Empty -Name 'ef-model stderr' -Text $efModel.Stderr
    $efModelJson = $efModel.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'ef-model command' -Actual $efModelJson.command -Expected 'ef-model'
    Assert-Equal -Name 'ef-model has dbcontext' -Actual (@($efModelJson.dbContexts.items | Where-Object { $_.type.name -eq 'OrdersDbContext' }).Count -ge 1) -Expected $true
    Assert-Equal -Name 'ef-model has query site' -Actual (@($efModelJson.querySites.items | Where-Object { $_.entityType.name -eq 'Order' }).Count -ge 1) -Expected $true

    $packageUsage = Invoke-Navlyn `
        -Name 'package-usage fixture' `
        -Arguments @('package-usage', '--workspace', $applicationDomainFixture, '--package', 'Microsoft.Extensions.Options', '--namespace', 'Microsoft.Extensions.Options', '--usage-limit', '20', '--reference-limit', '20', '--profile', 'evidence') `
        -ExpectedExitCode 0

    Assert-Empty -Name 'package-usage stderr' -Text $packageUsage.Stderr
    $packageUsageJson = $packageUsage.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'package-usage command' -Actual $packageUsageJson.command -Expected 'package-usage'
    Assert-Equal -Name 'package-usage has reference' -Actual (@($packageUsageJson.packageReferences.items | Where-Object { $_.name -eq 'Microsoft.Extensions.Options' }).Count -ge 1) -Expected $true
    Assert-Equal -Name 'package-usage has using' -Actual (@($packageUsageJson.usages.items | Where-Object { $_.usageKind -eq 'using-directive' }).Count -ge 1) -Expected $true

    $reviewDiffInvalid = Invoke-Navlyn `
        -Name 'review-diff head without base' `
        -Arguments @('review-diff', '--workspace', 'navlyn.slnx', '--head', 'HEAD') `
        -ExpectedExitCode 2
    Assert-Empty -Name 'review-diff head without base stdout' -Text $reviewDiffInvalid.Stdout
    Assert-Contains -Name 'review-diff head without base stderr' -Text $reviewDiffInvalid.Stderr -Expected 'NAVLYN1503:'

    $contextPackQuery = Invoke-Navlyn `
        -Name 'context-pack query mode' `
        -Arguments @('context-pack', '--workspace', 'navlyn.slnx', '--query', 'CheckCommand', '--assume-kind', 'NamedType', '--budget-tokens', '2000', '--item-limit', '5') `
        -ExpectedExitCode 0

    Assert-Empty -Name 'context-pack query stderr' -Text $contextPackQuery.Stderr
    $contextPackQueryJson = $contextPackQuery.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'context-pack query command' -Actual $contextPackQueryJson.command -Expected 'context-pack'
    Assert-Equal -Name 'context-pack query default profile' -Actual $contextPackQueryJson.profile -Expected 'full'
    Assert-Equal -Name 'context-pack query mode' -Actual $contextPackQueryJson.mode -Expected 'query'
    Assert-Equal -Name 'context-pack query goal' -Actual $contextPackQueryJson.goal -Expected 'understand'
    Assert-Equal -Name 'context-pack query selected' -Actual $contextPackQueryJson.selection.selectedCandidate.name -Expected 'CheckCommand'
    Assert-Equal -Name 'context-pack query budget estimator' -Actual $contextPackQueryJson.budget.estimator -Expected 'chars-div-4-v1'
    Assert-Equal -Name 'context-pack query item limit' -Actual $contextPackQueryJson.limits.itemLimit -Expected 5

    $contextPackChangeKind = Invoke-Navlyn `
        -Name 'context-pack change kind compact' `
        -Arguments @('context-pack', '--workspace', 'navlyn.slnx', '--query', 'CheckCommand', '--assume-kind', 'NamedType', '--goal', 'modify', '--change-kind', 'signature', '--profile', 'compact', '--budget-tokens', '2000', '--item-limit', '5') `
        -ExpectedExitCode 0

    Assert-Empty -Name 'context-pack change kind stderr' -Text $contextPackChangeKind.Stderr
    $contextPackChangeKindJson = $contextPackChangeKind.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'context-pack change kind top-level' -Actual $contextPackChangeKindJson.changeKind -Expected 'signature'
    Assert-Equal -Name 'context-pack change kind config' -Actual $contextPackChangeKindJson.configuration.changeKind -Expected 'signature'
    Assert-Equal -Name 'context-pack change kind option' -Actual $contextPackChangeKindJson.configuration.options.changeKind -Expected 'signature'

    $contextPackDiff = Invoke-Navlyn `
        -Name 'context-pack diff mode' `
        -Arguments @('context-pack', '--workspace', 'navlyn.slnx', '--diff', '--symbol-limit', '1', '--impact-limit', '1', '--diagnostic-limit', '1', '--related-test-limit', '1', '--item-limit', '1') `
        -ExpectedExitCode 0

    Assert-Empty -Name 'context-pack diff stderr' -Text $contextPackDiff.Stderr
    $contextPackDiffJson = $contextPackDiff.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'context-pack diff command' -Actual $contextPackDiffJson.command -Expected 'context-pack'
    Assert-Equal -Name 'context-pack diff mode' -Actual $contextPackDiffJson.mode -Expected 'diff'
    Assert-Equal -Name 'context-pack diff goal' -Actual $contextPackDiffJson.goal -Expected 'review'
    Assert-Equal -Name 'context-pack diff total files non-negative' -Actual ($contextPackDiffJson.diff.totalFiles -ge 0) -Expected $true

    $contextPackInvalid = Invoke-Navlyn `
        -Name 'context-pack query and diff invalid' `
        -Arguments @('context-pack', '--workspace', 'navlyn.slnx', '--query', 'CheckCommand', '--diff') `
        -ExpectedExitCode 2
    Assert-Empty -Name 'context-pack invalid stdout' -Text $contextPackInvalid.Stdout
    Assert-Contains -Name 'context-pack invalid stderr' -Text $contextPackInvalid.Stderr -Expected 'NAVLYN1001:'

    $contextPackDiffOptionInvalid = Invoke-Navlyn `
        -Name 'context-pack diff option without diff invalid' `
        -Arguments @('context-pack', '--workspace', 'navlyn.slnx', '--query', 'CheckCommand', '--include-unstaged') `
        -ExpectedExitCode 2
    Assert-Empty -Name 'context-pack diff option invalid stdout' -Text $contextPackDiffOptionInvalid.Stdout
    Assert-Contains -Name 'context-pack diff option invalid stderr' -Text $contextPackDiffOptionInvalid.Stderr -Expected 'NAVLYN1503:'

    $batchInput = @'
{
  "defaults": {
    "project": "navlyn"
  },
  "requests": [
    {
      "id": "overview",
      "command": "overview"
    },
    {
      "id": "symbols",
      "command": "symbols",
      "query": "Check",
      "limit": 1
    },
    {
      "id": "outline",
      "command": "outline",
      "file": "navlyn/Cli/Commands/CheckCommand.cs"
    },
    {
      "id": "symbol-info",
      "command": "symbol-info",
      "file": "navlyn/Cli/NavlynCli.cs",
      "line": 48,
      "column": 37
    },
    {
      "id": "type-hierarchy",
      "command": "type-hierarchy",
      "file": "navlyn/Cli/Commands/CheckCommand.cs",
      "line": 6,
      "column": 23
    },
    {
      "id": "callers",
      "command": "callers",
      "file": "navlyn/Cli/Commands/CheckCommand.cs",
      "line": 8,
      "column": 27
    },
    {
      "id": "calls",
      "command": "calls",
      "file": "navlyn/Cli/NavlynCli.cs",
      "line": 45,
      "column": 32
    },
    {
      "id": "definition-no-source",
      "command": "definition",
      "file": "navlyn/Cli/NavlynCli.cs",
      "line": 12,
      "column": 9
    },
    {
      "id": "definition-metadata",
      "command": "definition",
      "file": "navlyn/Cli/NavlynCli.cs",
      "line": 12,
      "column": 9,
      "includeMetadata": true
    }
  ]
}
'@

    $batch = Invoke-Navlyn `
        -Name 'batch stdin' `
        -Arguments @('batch', '--workspace', 'navlyn.slnx') `
        -ExpectedExitCode 0 `
        -StandardInput $batchInput

    Assert-Empty -Name 'batch stdin stderr' -Text $batch.Stderr
    $batchJson = $batch.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'batch stdin workspace' -Actual $batchJson.workspace -Expected 'navlyn.slnx'
    Assert-Equal -Name 'batch stdin request count' -Actual $batchJson.totalRequests -Expected 9
    Assert-Equal -Name 'batch stdin succeeded count' -Actual $batchJson.succeededRequests -Expected 8
    Assert-Equal -Name 'batch stdin failed count' -Actual $batchJson.failedRequests -Expected 1
    Assert-Equal -Name 'batch stdin first id' -Actual @($batchJson.results)[0].id -Expected 'overview'
    Assert-Equal -Name 'batch stdin second command' -Actual @($batchJson.results)[1].command -Expected 'symbols'
    Assert-Equal -Name 'batch stdin symbols limit' -Actual @($batchJson.results)[1].result.limit -Expected 1
    Assert-Equal -Name 'batch stdin symbols span' -Actual (@($batchJson.results)[1].result.matches)[0].endColumn -Expected 35
    Assert-Equal -Name 'batch stdin outline command' -Actual @($batchJson.results)[2].command -Expected 'outline'
    Assert-Equal -Name 'batch stdin symbol-info command' -Actual @($batchJson.results)[3].command -Expected 'symbol-info'
    Assert-Equal -Name 'batch stdin type-hierarchy command' -Actual @($batchJson.results)[4].command -Expected 'type-hierarchy'
    Assert-Equal -Name 'batch stdin callers command' -Actual @($batchJson.results)[5].command -Expected 'callers'
    Assert-Equal -Name 'batch stdin calls command' -Actual @($batchJson.results)[6].command -Expected 'calls'
    Assert-Equal -Name 'batch stdin failure ok' -Actual @($batchJson.results)[7].ok -Expected $false
    Assert-Equal -Name 'batch stdin failure code' -Actual @($batchJson.results)[7].error.code -Expected 'NAVLYN1305'
    Assert-Equal -Name 'batch stdin metadata definition ok' -Actual @($batchJson.results)[8].ok -Expected $true
    Assert-Equal -Name 'batch stdin metadata definition include' -Actual @($batchJson.results)[8].result.includeMetadata -Expected $true
    Assert-Equal -Name 'batch stdin metadata definition count' -Actual @(@($batchJson.results)[8].result.definitions).Count -Expected 0

    $fuzzyBatchInput = @'
{
  "requests": [
    {
      "id": "find",
      "command": "find",
      "query": "CheckCommand",
      "assumeKind": "NamedType"
    },
    {
      "id": "where-used",
      "command": "where-used",
      "query": "CheckCommand",
      "assumeKind": "NamedType",
      "limit": 1
    },
    {
      "id": "about",
      "command": "about",
      "query": "CheckCommand",
      "assumeKind": "NamedType",
      "memberLimit": 1,
      "referenceLimit": 1
    }
  ]
}
'@

    $fuzzyBatch = Invoke-Navlyn `
        -Name 'batch fuzzy commands' `
        -Arguments @('batch', '--workspace', 'navlyn.slnx') `
        -ExpectedExitCode 0 `
        -StandardInput $fuzzyBatchInput

    Assert-Empty -Name 'batch fuzzy stderr' -Text $fuzzyBatch.Stderr
    $fuzzyBatchJson = $fuzzyBatch.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'batch fuzzy request count' -Actual $fuzzyBatchJson.totalRequests -Expected 3
    Assert-Equal -Name 'batch fuzzy succeeded count' -Actual $fuzzyBatchJson.succeededRequests -Expected 3
    Assert-Equal -Name 'batch fuzzy find confidence' -Actual @($fuzzyBatchJson.results)[0].result.confidence -Expected 'high'
    Assert-Equal -Name 'batch fuzzy find span' -Actual @($fuzzyBatchJson.results)[0].result.selectedCandidate.endColumn -Expected 35
    Assert-Equal -Name 'batch fuzzy where-used total' -Actual @($fuzzyBatchJson.results)[1].result.totalMatches -Expected 1
    Assert-Equal -Name 'batch fuzzy where-used span' -Actual (@($fuzzyBatchJson.results)[1].result.references)[0].endColumn -Expected 49
    Assert-Equal -Name 'batch fuzzy about members' -Actual @(@($fuzzyBatchJson.results)[2].result.members.members).Count -Expected 1

    $diffBatchInput = @'
{
  "requests": [
    {
      "id": "review",
      "command": "review-diff",
      "symbolLimit": 1,
      "impactLimit": 1,
      "diagnosticLimit": 1,
      "relatedTestLimit": 1,
      "depth": 1
    }
  ]
}
'@

    $diffBatch = Invoke-Navlyn `
        -Name 'batch review-diff command' `
        -Arguments @('batch', '--workspace', 'navlyn.slnx') `
        -ExpectedExitCode 0 `
        -StandardInput $diffBatchInput

    Assert-Empty -Name 'batch review-diff stderr' -Text $diffBatch.Stderr
    $diffBatchJson = $diffBatch.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'batch review-diff request count' -Actual $diffBatchJson.totalRequests -Expected 1
    Assert-Equal -Name 'batch review-diff succeeded count' -Actual $diffBatchJson.succeededRequests -Expected 1
    Assert-Equal -Name 'batch review-diff command' -Actual @($diffBatchJson.results)[0].command -Expected 'review-diff'
    Assert-Equal -Name 'batch review-diff result command' -Actual @($diffBatchJson.results)[0].result.command -Expected 'review-diff'
    Assert-Equal -Name 'batch review-diff total files non-negative' -Actual (@($diffBatchJson.results)[0].result.diff.totalFiles -ge 0) -Expected $true

    $reviewPackBatchInput = @'
{
  "requests": [
    {
      "id": "packs",
      "command": "review-pack",
      "scope": "workspace",
      "pack": ["async", "security"],
      "profile": "compact",
      "findingLimit": 20
    }
  ]
}
'@

    $reviewPackBatch = Invoke-Navlyn `
        -Name 'batch review-pack command' `
        -Arguments @('batch', '--workspace', $reviewPackFixture) `
        -ExpectedExitCode 0 `
        -StandardInput $reviewPackBatchInput

    Assert-Empty -Name 'batch review-pack stderr' -Text $reviewPackBatch.Stderr
    $reviewPackBatchJson = $reviewPackBatch.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'batch review-pack request count' -Actual $reviewPackBatchJson.totalRequests -Expected 1
    Assert-Equal -Name 'batch review-pack succeeded count' -Actual $reviewPackBatchJson.succeededRequests -Expected 1
    Assert-Equal -Name 'batch review-pack command' -Actual @($reviewPackBatchJson.results)[0].command -Expected 'review-pack'
    Assert-Equal -Name 'batch review-pack result command' -Actual @($reviewPackBatchJson.results)[0].result.command -Expected 'review-pack'
    Assert-Equal -Name 'batch review-pack profile' -Actual @($reviewPackBatchJson.results)[0].result.profile -Expected 'compact'

    $contextPackBatchInput = @'
{
  "requests": [
    {
      "id": "context",
      "command": "context-pack",
      "query": "CheckCommand",
      "assumeKind": "NamedType",
      "budgetTokens": 2000,
      "itemLimit": 5
    }
  ]
}
'@

    $contextPackBatch = Invoke-Navlyn `
        -Name 'batch context-pack command' `
        -Arguments @('batch', '--workspace', 'navlyn.slnx') `
        -ExpectedExitCode 0 `
        -StandardInput $contextPackBatchInput

    Assert-Empty -Name 'batch context-pack stderr' -Text $contextPackBatch.Stderr
    $contextPackBatchJson = $contextPackBatch.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'batch context-pack request count' -Actual $contextPackBatchJson.totalRequests -Expected 1
    Assert-Equal -Name 'batch context-pack succeeded count' -Actual $contextPackBatchJson.succeededRequests -Expected 1
    Assert-Equal -Name 'batch context-pack command' -Actual @($contextPackBatchJson.results)[0].command -Expected 'context-pack'
    Assert-Equal -Name 'batch context-pack result command' -Actual @($contextPackBatchJson.results)[0].result.command -Expected 'context-pack'
    Assert-Equal -Name 'batch context-pack result mode' -Actual @($contextPackBatchJson.results)[0].result.mode -Expected 'query'

    $expandedBatchInput = @'
{
  "requests": [
    {
      "id": "repo",
      "command": "repo-graph",
      "profile": "compact",
      "relationshipLimit": 5
    },
    {
      "id": "api",
      "command": "public-api-diff",
      "base": "HEAD",
      "symbolLimit": 50,
      "changeLimit": 5
    },
    {
      "id": "tests-symbol",
      "command": "tests-for-symbol",
      "query": "CheckCommand",
      "assumeKind": "NamedType",
      "testLimit": 5,
      "referenceLimit": 20
    },
    {
      "id": "tests-diff",
      "command": "tests-for-diff",
      "symbolLimit": 5,
      "testLimit": 5,
      "referenceLimit": 20
    },
    {
      "id": "framework",
      "command": "framework-entrypoints",
      "limit": 5,
      "evidenceLimit": 2
    },
    {
      "id": "di-graph",
      "command": "di-graph",
      "registrationLimit": 5,
      "dependencyLimit": 5,
      "riskLimit": 5
    },
    {
      "id": "where",
      "command": "where-registered",
      "query": "WorkspaceLoader",
      "assumeKind": "NamedType",
      "registrationLimit": 5,
      "dependencyLimit": 5
    },
    {
      "id": "di-impact",
      "command": "di-impact",
      "query": "WorkspaceLoader",
      "assumeKind": "NamedType",
      "registrationLimit": 5,
      "consumerLimit": 5,
      "dependencyLimit": 5,
      "riskLimit": 5
    }
  ]
}
'@

    $expandedBatch = Invoke-Navlyn `
        -Name 'batch expanded commands' `
        -Arguments @('batch', '--workspace', 'navlyn.slnx') `
        -ExpectedExitCode 0 `
        -StandardInput $expandedBatchInput

    Assert-Empty -Name 'batch expanded stderr' -Text $expandedBatch.Stderr
    $expandedBatchJson = $expandedBatch.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'batch expanded request count' -Actual $expandedBatchJson.totalRequests -Expected 8
    Assert-Equal -Name 'batch expanded succeeded count' -Actual $expandedBatchJson.succeededRequests -Expected 8
    Assert-Equal -Name 'batch expanded failed count' -Actual $expandedBatchJson.failedRequests -Expected 0
    Assert-Equal -Name 'batch repo command' -Actual @($expandedBatchJson.results)[0].result.command -Expected 'repo-graph'
    Assert-Equal -Name 'batch repo profile' -Actual @($expandedBatchJson.results)[0].result.profile -Expected 'compact'
    Assert-Equal -Name 'batch repo relationship limit' -Actual @($expandedBatchJson.results)[0].result.limits.relationshipLimit -Expected 5
    Assert-Equal -Name 'batch public api command' -Actual @($expandedBatchJson.results)[1].result.command -Expected 'public-api-diff'
    Assert-Equal -Name 'batch public api change limit' -Actual @($expandedBatchJson.results)[1].result.limits.changeLimit -Expected 5
    Assert-Equal -Name 'batch tests-for-symbol command' -Actual @($expandedBatchJson.results)[2].result.command -Expected 'tests-for-symbol'
    Assert-Equal -Name 'batch tests-for-symbol limit' -Actual @($expandedBatchJson.results)[2].result.limits.testLimit -Expected 5
    Assert-Equal -Name 'batch tests-for-diff command' -Actual @($expandedBatchJson.results)[3].result.command -Expected 'tests-for-diff'
    Assert-Equal -Name 'batch tests-for-diff symbol limit' -Actual @($expandedBatchJson.results)[3].result.limits.symbolLimit -Expected 5
    Assert-Equal -Name 'batch framework command' -Actual @($expandedBatchJson.results)[4].result.command -Expected 'framework-entrypoints'
    Assert-Equal -Name 'batch framework evidence limit' -Actual @($expandedBatchJson.results)[4].result.limits.evidenceLimit -Expected 2
    Assert-Equal -Name 'batch di-graph command' -Actual @($expandedBatchJson.results)[5].result.command -Expected 'di-graph'
    Assert-Equal -Name 'batch where-registered command' -Actual @($expandedBatchJson.results)[6].result.command -Expected 'where-registered'
    Assert-Equal -Name 'batch di-impact command' -Actual @($expandedBatchJson.results)[7].result.command -Expected 'di-impact'
    Assert-Equal -Name 'batch di-impact consumer limit' -Actual @($expandedBatchJson.results)[7].result.limits.consumerLimit -Expected 5

    $domainBatchInput = @'
{
  "requests": [
    { "id": "routes", "command": "route-map", "routeLimit": 10 },
    { "id": "options", "command": "options-graph", "query": "PaymentOptions" },
    { "id": "messages", "command": "where-handled", "query": "CreateOrderCommand", "assumeKind": "NamedType" },
    { "id": "ef", "command": "ef-model", "entityLimit": 20, "querySiteLimit": 20 },
    { "id": "pkg", "command": "package-usage", "package": "Microsoft.Extensions.Options", "namespaces": ["Microsoft.Extensions.Options"] }
  ]
}
'@

    $domainBatch = Invoke-Navlyn `
        -Name 'batch application domain commands' `
        -Arguments @('batch', '--workspace', $applicationDomainFixture) `
        -ExpectedExitCode 0 `
        -StandardInput $domainBatchInput

    Assert-Empty -Name 'batch application domain stderr' -Text $domainBatch.Stderr
    $domainBatchJson = $domainBatch.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'batch application domain request count' -Actual $domainBatchJson.totalRequests -Expected 5
    Assert-Equal -Name 'batch application domain succeeded count' -Actual $domainBatchJson.succeededRequests -Expected 5
    Assert-Equal -Name 'batch application domain route command' -Actual @($domainBatchJson.results)[0].result.command -Expected 'route-map'
    Assert-Equal -Name 'batch application domain route count' -Actual (@($domainBatchJson.results)[0].result.routes.totalItems -ge 1) -Expected $true
    Assert-Equal -Name 'batch application domain options command' -Actual @($domainBatchJson.results)[1].result.command -Expected 'options-graph'
    Assert-Equal -Name 'batch application domain handler command' -Actual @($domainBatchJson.results)[2].result.command -Expected 'where-handled'
    Assert-Equal -Name 'batch application domain ef command' -Actual @($domainBatchJson.results)[3].result.command -Expected 'ef-model'
    Assert-Equal -Name 'batch application domain package command' -Actual @($domainBatchJson.results)[4].result.command -Expected 'package-usage'

    $batchInvalidProfileInput = @'
{
  "requests": [
    {
      "id": "bad-profile",
      "command": "repo-graph",
      "profile": "tiny"
    }
  ]
}
'@

    $batchInvalidProfile = Invoke-Navlyn `
        -Name 'batch invalid profile request' `
        -Arguments @('batch', '--workspace', 'navlyn.slnx') `
        -ExpectedExitCode 0 `
        -StandardInput $batchInvalidProfileInput

    Assert-Empty -Name 'batch invalid profile stderr' -Text $batchInvalidProfile.Stderr
    $batchInvalidProfileJson = $batchInvalidProfile.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'batch invalid profile failed count' -Actual $batchInvalidProfileJson.failedRequests -Expected 1
    Assert-Equal -Name 'batch invalid profile ok false' -Actual @($batchInvalidProfileJson.results)[0].ok -Expected $false
    Assert-Equal -Name 'batch invalid profile code' -Actual @($batchInvalidProfileJson.results)[0].error.code -Expected 'NAVLYN1001'

    $batchFile = New-TemporaryFile
    try {
        Set-Content -LiteralPath $batchFile.FullName -Encoding utf8NoBOM -Value @'
{
  "requests": [
    {
      "id": "diagnostics",
      "command": "diagnostics",
      "project": "navlyn"
    }
  ]
}
'@

        $batchFromFile = Invoke-Navlyn `
            -Name 'batch input file' `
            -Arguments @('batch', '--workspace', 'navlyn.slnx', '--input', $batchFile.FullName) `
            -ExpectedExitCode 0

        Assert-Empty -Name 'batch input file stderr' -Text $batchFromFile.Stderr
        $batchFromFileJson = $batchFromFile.Stdout | ConvertFrom-Json
        Assert-Equal -Name 'batch input file request count' -Actual $batchFromFileJson.totalRequests -Expected 1
        Assert-Equal -Name 'batch input file command' -Actual @($batchFromFileJson.results)[0].command -Expected 'diagnostics'
        Assert-Equal -Name 'batch input file diagnostics non-negative' -Actual (@($batchFromFileJson.results)[0].result.totalDiagnostics -ge 0) -Expected $true
    }
    finally {
        Remove-Item -LiteralPath $batchFile.FullName -ErrorAction SilentlyContinue
    }

    $batchInvalidJson = Invoke-Navlyn `
        -Name 'batch invalid json' `
        -Arguments @('batch', '--workspace', 'navlyn.slnx') `
        -ExpectedExitCode 2 `
        -StandardInput '{'
    Assert-Empty -Name 'batch invalid json stdout' -Text $batchInvalidJson.Stdout
    Assert-Contains -Name 'batch invalid json stderr' -Text $batchInvalidJson.Stderr -Expected 'NAVLYN1008:'

    $symbols = Invoke-Navlyn `
        -Name 'symbols partial query' `
        -Arguments @('symbols', '--workspace', 'navlyn.slnx', '--query', 'Check') `
        -ExpectedExitCode 0

    $symbolsJson = $symbols.Stdout | ConvertFrom-Json
    $symbolMatch = @($symbolsJson.matches | Where-Object { $_.name -eq 'CheckCommand' })[0]
    Assert-Equal -Name 'symbols query' -Actual $symbolsJson.query -Expected 'Check'
    Assert-Equal -Name 'symbols match mode' -Actual $symbolsJson.match -Expected 'contains'
    Assert-Equal -Name 'symbols case sensitivity' -Actual $symbolsJson.caseSensitive -Expected $false
    Assert-Equal -Name 'symbols kind filter count' -Actual @($symbolsJson.kinds).Count -Expected 0
    Assert-Equal -Name 'symbols limit' -Actual $symbolsJson.limit -Expected $null
    Assert-Equal -Name 'symbols total matches' -Actual $symbolsJson.totalMatches -Expected 2
    Assert-Equal -Name 'symbols contains CheckCommand' -Actual $symbolMatch.name -Expected 'CheckCommand'
    Assert-Equal -Name 'symbols CheckCommand kind' -Actual $symbolMatch.kind -Expected 'NamedType'
    Assert-Equal -Name 'symbols CheckCommand path' -Actual $symbolMatch.path -Expected 'navlyn/Cli/Commands/CheckCommand.cs'
    Assert-Equal -Name 'symbols CheckCommand line' -Actual $symbolMatch.line -Expected 6
    Assert-Equal -Name 'symbols CheckCommand column' -Actual $symbolMatch.column -Expected 23
    Assert-Equal -Name 'symbols CheckCommand end line' -Actual $symbolMatch.endLine -Expected 6
    Assert-Equal -Name 'symbols CheckCommand end column' -Actual $symbolMatch.endColumn -Expected 35

    $symbolsLimit = Invoke-Navlyn `
        -Name 'symbols limited query' `
        -Arguments @('symbols', '--workspace', 'navlyn.slnx', '--query', 'Check', '--limit', '1') `
        -ExpectedExitCode 0

    $symbolsLimitJson = $symbolsLimit.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbols limited query limit' -Actual $symbolsLimitJson.limit -Expected 1
    Assert-Equal -Name 'symbols limited query total matches' -Actual $symbolsLimitJson.totalMatches -Expected 2
    Assert-Equal -Name 'symbols limited query match count' -Actual @($symbolsLimitJson.matches).Count -Expected 1
    Assert-Equal -Name 'symbols limited query first match name' -Actual @($symbolsLimitJson.matches)[0].name -Expected 'CheckCommand'

    $symbolsKind = Invoke-Navlyn `
        -Name 'symbols kind filter query' `
        -Arguments @('symbols', '--workspace', 'navlyn.slnx', '--query', 'Check', '--kind', 'NamedType') `
        -ExpectedExitCode 0

    $symbolsKindJson = $symbolsKind.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbols kind filter count' -Actual @($symbolsKindJson.kinds).Count -Expected 1
    Assert-Equal -Name 'symbols kind filter value' -Actual @($symbolsKindJson.kinds)[0] -Expected 'NamedType'
    Assert-Equal -Name 'symbols kind filter total matches' -Actual $symbolsKindJson.totalMatches -Expected 2
    Assert-Equal -Name 'symbols kind filter first match kind' -Actual @($symbolsKindJson.matches)[0].kind -Expected 'NamedType'

    $symbolsNamespace = Invoke-Navlyn `
        -Name 'symbols namespace container accessibility filters' `
        -Arguments @('symbols', '--workspace', 'navlyn.slnx', '--query', 'Create', '--namespace', 'Navlyn.Cli.Commands', '--namespace-match', 'exact', '--container', 'CheckCommand', '--container-match', 'contains', '--accessibility', 'Public', '--limit', '1') `
        -ExpectedExitCode 0

    $symbolsNamespaceJson = $symbolsNamespace.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbols namespace filter total matches' -Actual $symbolsNamespaceJson.totalMatches -Expected 1
    Assert-Equal -Name 'symbols namespace filter namespace' -Actual @($symbolsNamespaceJson.namespaces)[0] -Expected 'Navlyn.Cli.Commands'
    Assert-Equal -Name 'symbols namespace filter match name' -Actual @($symbolsNamespaceJson.matches)[0].name -Expected 'Create'
    Assert-Equal -Name 'symbols namespace filter accessibility fact' -Actual @($symbolsNamespaceJson.matches)[0].facts.accessibility -Expected 'Public'

    $symbolsExact = Invoke-Navlyn `
        -Name 'symbols exact query' `
        -Arguments @('symbols', '--workspace', 'navlyn.slnx', '--query', 'CheckCommand', '--match', 'exact') `
        -ExpectedExitCode 0

    $symbolsExactJson = $symbolsExact.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbols exact match mode' -Actual $symbolsExactJson.match -Expected 'exact'
    Assert-Equal -Name 'symbols exact match count' -Actual @($symbolsExactJson.matches).Count -Expected 1
    Assert-Equal -Name 'symbols exact match name' -Actual @($symbolsExactJson.matches)[0].name -Expected 'CheckCommand'

    $symbolsRegex = Invoke-Navlyn `
        -Name 'symbols regex query' `
        -Arguments @('symbols', '--workspace', 'navlyn.slnx', '--query', '^Check.*Command$', '--match', 'regex') `
        -ExpectedExitCode 0

    $symbolsRegexJson = $symbolsRegex.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbols regex match mode' -Actual $symbolsRegexJson.match -Expected 'regex'
    Assert-Equal -Name 'symbols regex match count' -Actual @($symbolsRegexJson.matches).Count -Expected 1
    Assert-Equal -Name 'symbols regex match name' -Actual @($symbolsRegexJson.matches)[0].name -Expected 'CheckCommand'

    $symbolsCaseSensitive = Invoke-Navlyn `
        -Name 'symbols case-sensitive query' `
        -Arguments @('symbols', '--workspace', 'navlyn.slnx', '--query', 'check', '--case-sensitive') `
        -ExpectedExitCode 0

    $symbolsCaseSensitiveJson = $symbolsCaseSensitive.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbols case-sensitive flag' -Actual $symbolsCaseSensitiveJson.caseSensitive -Expected $true
    Assert-Equal -Name 'symbols case-sensitive match count' -Actual @($symbolsCaseSensitiveJson.matches).Count -Expected 0

    $symbolsInvalidRegex = Invoke-Navlyn `
        -Name 'symbols invalid regex' `
        -Arguments @('symbols', '--workspace', 'navlyn.slnx', '--query', '[', '--match', 'regex') `
        -ExpectedExitCode 2
    Assert-Empty -Name 'symbols invalid regex stdout' -Text $symbolsInvalidRegex.Stdout
    Assert-Contains -Name 'symbols invalid regex stderr' -Text $symbolsInvalidRegex.Stderr -Expected 'NAVLYN1002:'

    $symbolsInvalidMatch = Invoke-Navlyn `
        -Name 'symbols invalid match mode' `
        -Arguments @('symbols', '--workspace', 'navlyn.slnx', '--query', 'Check', '--match', 'starts-with') `
        -ExpectedExitCode 2
    Assert-Empty -Name 'symbols invalid match stdout' -Text $symbolsInvalidMatch.Stdout
    Assert-Contains -Name 'symbols invalid match stderr' -Text $symbolsInvalidMatch.Stderr -Expected 'NAVLYN1001:'

    $symbolsInvalidLimit = Invoke-Navlyn `
        -Name 'symbols invalid limit' `
        -Arguments @('symbols', '--workspace', 'navlyn.slnx', '--query', 'Check', '--limit', '0') `
        -ExpectedExitCode 2
    Assert-Empty -Name 'symbols invalid limit stdout' -Text $symbolsInvalidLimit.Stdout
    Assert-Contains -Name 'symbols invalid limit stderr' -Text $symbolsInvalidLimit.Stderr -Expected 'NAVLYN1003:'

    $symbolsInvalidKind = Invoke-Navlyn `
        -Name 'symbols invalid kind' `
        -Arguments @('symbols', '--workspace', 'navlyn.slnx', '--query', 'Check', '--kind', '1') `
        -ExpectedExitCode 2
    Assert-Empty -Name 'symbols invalid kind stdout' -Text $symbolsInvalidKind.Stdout
    Assert-Contains -Name 'symbols invalid kind stderr' -Text $symbolsInvalidKind.Stderr -Expected 'NAVLYN1004:'

    $symbolsIn = Invoke-Navlyn `
        -Name 'symbols-in source line' `
        -Arguments @('symbols-in', '--workspace', 'navlyn.slnx', '--file', 'navlyn/Cli/NavlynCli.cs', '--line', '48') `
        -ExpectedExitCode 0

    $symbolsInJson = $symbolsIn.Stdout | ConvertFrom-Json
    $symbolsInMatch = @($symbolsInJson.symbols | Where-Object { $_.name -eq 'CheckCommand' })[0]
    Assert-Equal -Name 'symbols-in file' -Actual $symbolsInJson.file -Expected 'navlyn/Cli/NavlynCli.cs'
    Assert-Equal -Name 'symbols-in line' -Actual $symbolsInJson.line -Expected 48
    Assert-Equal -Name 'symbols-in start column' -Actual $symbolsInJson.startColumn -Expected 1
    Assert-Equal -Name 'symbols-in end column' -Actual $symbolsInJson.endColumn -Expected 60
    Assert-Equal -Name 'symbols-in contains CheckCommand' -Actual $symbolsInMatch.name -Expected 'CheckCommand'
    Assert-Equal -Name 'symbols-in CheckCommand kind' -Actual $symbolsInMatch.kind -Expected 'NamedType'
    Assert-Equal -Name 'symbols-in CheckCommand line' -Actual $symbolsInMatch.line -Expected 48
    Assert-Equal -Name 'symbols-in CheckCommand column' -Actual $symbolsInMatch.column -Expected 37
    Assert-Equal -Name 'symbols-in CheckCommand end line' -Actual $symbolsInMatch.endLine -Expected 48
    Assert-Equal -Name 'symbols-in CheckCommand end column' -Actual $symbolsInMatch.endColumn -Expected 49

    $symbolsInSpan = Invoke-Navlyn `
        -Name 'symbols-in source span' `
        -Arguments @('symbols-in', '--workspace', 'navlyn.slnx', '--file', 'navlyn/Cli/NavlynCli.cs', '--line', '48', '--start-column', '37', '--end-column', '49') `
        -ExpectedExitCode 0

    $symbolsInSpanJson = $symbolsInSpan.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbols-in span start column' -Actual $symbolsInSpanJson.startColumn -Expected 37
    Assert-Equal -Name 'symbols-in span end column' -Actual $symbolsInSpanJson.endColumn -Expected 49
    Assert-Equal -Name 'symbols-in span match count' -Actual @($symbolsInSpanJson.symbols).Count -Expected 1
    Assert-Equal -Name 'symbols-in span match name' -Actual @($symbolsInSpanJson.symbols)[0].name -Expected 'CheckCommand'

    $symbolsInEmpty = Invoke-Navlyn `
        -Name 'symbols-in no symbols' `
        -Arguments @('symbols-in', '--workspace', 'navlyn.slnx', '--file', 'navlyn/Cli/Commands/CheckCommand.cs', '--line', '3') `
        -ExpectedExitCode 0

    $symbolsInEmptyJson = $symbolsInEmpty.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbols-in no symbols count' -Actual @($symbolsInEmptyJson.symbols).Count -Expected 0

    $outline = Invoke-Navlyn `
        -Name 'outline source file' `
        -Arguments @('outline', '--workspace', 'navlyn.slnx', '--file', 'navlyn/Cli/Commands/CheckCommand.cs') `
        -ExpectedExitCode 0

    $outlineJson = $outline.Stdout | ConvertFrom-Json
    $outlineCreate = @($outlineJson.entries | Where-Object { $_.name -eq 'Create' -and $_.kind -eq 'Method' })[0]
    Assert-Equal -Name 'outline file' -Actual $outlineJson.file -Expected 'navlyn/Cli/Commands/CheckCommand.cs'
    Assert-Equal -Name 'outline contains Create method' -Actual $outlineCreate.name -Expected 'Create'
    Assert-Equal -Name 'outline Create facts accessibility' -Actual $outlineCreate.facts.accessibility -Expected 'Public'

    $symbolsInInvalidSpan = Invoke-Navlyn `
        -Name 'symbols-in invalid span' `
        -Arguments @('symbols-in', '--workspace', 'navlyn.slnx', '--file', 'navlyn/Cli/NavlynCli.cs', '--line', '48', '--start-column', '37', '--end-column', '37') `
        -ExpectedExitCode 2
    Assert-Empty -Name 'symbols-in invalid span stdout' -Text $symbolsInInvalidSpan.Stdout
    Assert-Contains -Name 'symbols-in invalid span stderr' -Text $symbolsInInvalidSpan.Stderr -Expected 'NAVLYN1303:'

    $symbolAt = Invoke-Navlyn `
        -Name 'symbol-at declaration' `
        -Arguments @('symbol-at', '--workspace', 'navlyn.slnx', '--file', 'navlyn/Cli/Commands/CheckCommand.cs', '--line', '6', '--column', '23') `
        -ExpectedExitCode 0

    $symbolAtJson = $symbolAt.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbol-at file' -Actual $symbolAtJson.file -Expected 'navlyn/Cli/Commands/CheckCommand.cs'
    Assert-Equal -Name 'symbol-at line' -Actual $symbolAtJson.line -Expected 6
    Assert-Equal -Name 'symbol-at column' -Actual $symbolAtJson.column -Expected 23
    Assert-Equal -Name 'symbol-at name' -Actual $symbolAtJson.symbol.name -Expected 'CheckCommand'
    Assert-Equal -Name 'symbol-at kind' -Actual $symbolAtJson.symbol.kind -Expected 'NamedType'
    Assert-Equal -Name 'symbol-at container' -Actual $symbolAtJson.symbol.container -Expected 'Navlyn.Cli.Commands'
    Assert-Equal -Name 'symbol-at path' -Actual $symbolAtJson.symbol.path -Expected 'navlyn/Cli/Commands/CheckCommand.cs'
    Assert-Equal -Name 'symbol-at declaration line' -Actual $symbolAtJson.symbol.line -Expected 6
    Assert-Equal -Name 'symbol-at declaration column' -Actual $symbolAtJson.symbol.column -Expected 23
    Assert-Equal -Name 'symbol-at declaration end line' -Actual $symbolAtJson.symbol.endLine -Expected 6
    Assert-Equal -Name 'symbol-at declaration end column' -Actual $symbolAtJson.symbol.endColumn -Expected 35
    Assert-Equal -Name 'symbol-at facts project' -Actual $symbolAtJson.symbol.facts.project -Expected 'navlyn'

    $symbolInfo = Invoke-Navlyn `
        -Name 'symbol-info invocation' `
        -Arguments @('symbol-info', '--workspace', 'navlyn.slnx', '--file', 'navlyn/Cli/NavlynCli.cs', '--line', '48', '--column', '37') `
        -ExpectedExitCode 0

    $symbolInfoJson = $symbolInfo.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbol-info symbol name' -Actual $symbolInfoJson.symbol.name -Expected 'CheckCommand'
    Assert-Equal -Name 'symbol-info invocation target' -Actual $symbolInfoJson.invocation.target.displayName -Expected 'Navlyn.Cli.Commands.CheckCommand.Create()'

    $scopeAt = Invoke-Navlyn `
        -Name 'scope-at source position' `
        -Arguments @('scope-at', '--workspace', 'navlyn.slnx', '--file', 'navlyn/Cli/NavlynCli.cs', '--line', '48', '--column', '37') `
        -ExpectedExitCode 0

    $scopeAtJson = $scopeAt.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'scope-at containing symbol' -Actual $scopeAtJson.containingSymbol.name -Expected 'CreateRootCommand'
    Assert-Equal -Name 'scope-at project context' -Actual $scopeAtJson.projectContext.name -Expected 'navlyn'
    Assert-Equal -Name 'scope-at innermost scope' -Actual @($scopeAtJson.scopes)[-1].kind -Expected 'Member'

    $symbolSource = Invoke-Navlyn `
        -Name 'symbol-source declaration' `
        -Arguments @('symbol-source', '--workspace', 'navlyn.slnx', '--file', 'navlyn/Cli/Commands/CheckCommand.cs', '--line', '6', '--column', '23', '--view', 'declaration', '--max-lines', '1') `
        -ExpectedExitCode 0

    $symbolSourceJson = $symbolSource.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbol-source symbol name' -Actual $symbolSourceJson.symbol.name -Expected 'CheckCommand'
    Assert-Equal -Name 'symbol-source view' -Actual $symbolSourceJson.view -Expected 'declaration'
    Assert-Equal -Name 'symbol-source truncated' -Actual @($symbolSourceJson.slices)[0].truncated -Expected $true

    $signature = Invoke-Navlyn `
        -Name 'signature method declaration' `
        -Arguments @('signature', '--workspace', 'navlyn.slnx', '--file', 'navlyn/Cli/Commands/CheckCommand.cs', '--line', '8', '--column', '27') `
        -ExpectedExitCode 0

    $signatureJson = $signature.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'signature symbol name' -Actual $signatureJson.symbol.name -Expected 'Create'
    Assert-Equal -Name 'signature accessibility' -Actual $signatureJson.apiShape.accessibility -Expected 'Public'

    $typeHierarchy = Invoke-Navlyn `
        -Name 'type-hierarchy non-derived type' `
        -Arguments @('type-hierarchy', '--workspace', 'navlyn.slnx', '--file', 'navlyn/Cli/Commands/CheckCommand.cs', '--line', '6', '--column', '23') `
        -ExpectedExitCode 0

    $typeHierarchyJson = $typeHierarchy.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'type-hierarchy symbol name' -Actual $typeHierarchyJson.symbol.name -Expected 'CheckCommand'

    $symbolAtInvalidLine = Invoke-Navlyn `
        -Name 'symbol-at invalid line' `
        -Arguments @('symbol-at', '--workspace', 'navlyn.slnx', '--file', 'navlyn/Cli/Commands/CheckCommand.cs', '--line', '999', '--column', '1') `
        -ExpectedExitCode 2
    Assert-Empty -Name 'symbol-at invalid line stdout' -Text $symbolAtInvalidLine.Stdout
    Assert-Contains -Name 'symbol-at invalid line stderr' -Text $symbolAtInvalidLine.Stderr -Expected 'NAVLYN1303:'

    $symbolAtNoSymbol = Invoke-Navlyn `
        -Name 'symbol-at no symbol' `
        -Arguments @('symbol-at', '--workspace', 'navlyn.slnx', '--file', 'navlyn/Cli/Commands/CheckCommand.cs', '--line', '3', '--column', '1') `
        -ExpectedExitCode 2
    Assert-Empty -Name 'symbol-at no symbol stdout' -Text $symbolAtNoSymbol.Stdout
    Assert-Contains -Name 'symbol-at no symbol stderr' -Text $symbolAtNoSymbol.Stderr -Expected 'NAVLYN1304:'

    $definition = Invoke-Navlyn `
        -Name 'definition type reference' `
        -Arguments @('definition', '--workspace', 'navlyn.slnx', '--file', 'navlyn/Cli/NavlynCli.cs', '--line', '48', '--column', '37') `
        -ExpectedExitCode 0

    $definitionJson = $definition.Stdout | ConvertFrom-Json
    $definitionLocation = @($definitionJson.definitions)[0]
    Assert-Equal -Name 'definition file' -Actual $definitionJson.file -Expected 'navlyn/Cli/NavlynCli.cs'
    Assert-Equal -Name 'definition line' -Actual $definitionJson.line -Expected 48
    Assert-Equal -Name 'definition column' -Actual $definitionJson.column -Expected 37
    Assert-Equal -Name 'definition symbol name' -Actual $definitionJson.symbol.name -Expected 'CheckCommand'
    Assert-Equal -Name 'definition symbol kind' -Actual $definitionJson.symbol.kind -Expected 'NamedType'
    Assert-Equal -Name 'definition symbol container' -Actual $definitionJson.symbol.container -Expected 'Navlyn.Cli.Commands'
    Assert-Equal -Name 'definition count' -Actual @($definitionJson.definitions).Count -Expected 1
    Assert-Equal -Name 'definition path' -Actual $definitionLocation.path -Expected 'navlyn/Cli/Commands/CheckCommand.cs'
    Assert-Equal -Name 'definition declaration line' -Actual $definitionLocation.line -Expected 6
    Assert-Equal -Name 'definition declaration column' -Actual $definitionLocation.column -Expected 23
    Assert-Equal -Name 'definition declaration end line' -Actual $definitionLocation.endLine -Expected 6
    Assert-Equal -Name 'definition declaration end column' -Actual $definitionLocation.endColumn -Expected 35

    $definitionNoSource = Invoke-Navlyn `
        -Name 'definition no source' `
        -Arguments @('definition', '--workspace', 'navlyn.slnx', '--file', 'navlyn/Cli/NavlynCli.cs', '--line', '12', '--column', '9') `
        -ExpectedExitCode 2
    Assert-Empty -Name 'definition no source stdout' -Text $definitionNoSource.Stdout
    Assert-Contains -Name 'definition no source stderr' -Text $definitionNoSource.Stderr -Expected 'NAVLYN1305:'

    $definitionMetadata = Invoke-Navlyn `
        -Name 'definition include metadata' `
        -Arguments @('definition', '--workspace', 'navlyn.slnx', '--file', 'navlyn/Cli/NavlynCli.cs', '--line', '12', '--column', '9', '--include-metadata') `
        -ExpectedExitCode 0
    $definitionMetadataJson = $definitionMetadata.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'definition include metadata flag' -Actual $definitionMetadataJson.includeMetadata -Expected $true
    Assert-Equal -Name 'definition include metadata count' -Actual @($definitionMetadataJson.definitions).Count -Expected 0
    Assert-Equal -Name 'definition include metadata fact' -Actual $definitionMetadataJson.symbol.facts.isMetadata -Expected $true

    $references = Invoke-Navlyn `
        -Name 'references type reference' `
        -Arguments @('references', '--workspace', 'navlyn.slnx', '--file', 'navlyn/Cli/NavlynCli.cs', '--line', '48', '--column', '37') `
        -ExpectedExitCode 0

    $referencesJson = $references.Stdout | ConvertFrom-Json
    $referenceLocation = @($referencesJson.references)[0]
    Assert-Equal -Name 'references file' -Actual $referencesJson.file -Expected 'navlyn/Cli/NavlynCli.cs'
    Assert-Equal -Name 'references line' -Actual $referencesJson.line -Expected 48
    Assert-Equal -Name 'references column' -Actual $referencesJson.column -Expected 37
    Assert-Equal -Name 'references symbol name' -Actual $referencesJson.symbol.name -Expected 'CheckCommand'
    Assert-Equal -Name 'references symbol kind' -Actual $referencesJson.symbol.kind -Expected 'NamedType'
    Assert-Equal -Name 'references symbol container' -Actual $referencesJson.symbol.container -Expected 'Navlyn.Cli.Commands'
    Assert-Equal -Name 'references count' -Actual @($referencesJson.references).Count -Expected 1
    Assert-Equal -Name 'references path' -Actual $referenceLocation.path -Expected 'navlyn/Cli/NavlynCli.cs'
    Assert-Equal -Name 'references reference line' -Actual $referenceLocation.line -Expected 48
    Assert-Equal -Name 'references reference column' -Actual $referenceLocation.column -Expected 37
    Assert-Equal -Name 'references reference end line' -Actual $referenceLocation.endLine -Expected 48
    Assert-Equal -Name 'references reference end column' -Actual $referenceLocation.endColumn -Expected 49
    Assert-Equal -Name 'references containing symbol name' -Actual $referenceLocation.containingSymbol.name -Expected 'CreateRootCommand'
    Assert-Equal -Name 'references containing symbol kind' -Actual $referenceLocation.containingSymbol.kind -Expected 'Method'

    $find = Invoke-Navlyn `
        -Name 'find fuzzy unique type' `
        -Arguments @('find', '--workspace', 'navlyn.slnx', '--query', 'CheckCommand', '--assume-kind', 'NamedType') `
        -ExpectedExitCode 0

    $findJson = $find.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'find fuzzy confidence' -Actual $findJson.confidence -Expected 'high'
    Assert-Equal -Name 'find fuzzy selected name' -Actual $findJson.selectedCandidate.name -Expected 'CheckCommand'
    Assert-Equal -Name 'find fuzzy selected end column' -Actual $findJson.selectedCandidate.endColumn -Expected 35
    Assert-Equal -Name 'find fuzzy reason exact' -Actual (@($findJson.selectedCandidate.reasonCodes) -contains 'exact-name-match') -Expected $true
    Assert-Equal -Name 'find fuzzy candidate id' -Actual $findJson.selectedCandidate.candidateId.StartsWith('sym:v1:') -Expected $true
    Assert-Equal -Name 'find fuzzy selector name' -Actual $findJson.selectedCandidate.selector.name -Expected 'CheckCommand'

    $findExplain = Invoke-Navlyn `
        -Name 'find fuzzy explain selection' `
        -Arguments @('find', '--workspace', 'navlyn.slnx', '--query', 'CheckCommand', '--assume-kind', 'NamedType', '--explain-selection') `
        -ExpectedExitCode 0

    $findExplainJson = $findExplain.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'find fuzzy explanation selected' -Actual $findExplainJson.selectionExplanation.selected -Expected $true
    Assert-Equal -Name 'find fuzzy explanation candidate id' -Actual $findExplainJson.selectionExplanation.selectedCandidateId.StartsWith('sym:v1:') -Expected $true

    $aboutCandidate = Invoke-Navlyn `
        -Name 'about fuzzy candidate id' `
        -Arguments @('about', '--workspace', 'navlyn.slnx', '--candidate-id', $findJson.selectedCandidate.candidateId) `
        -ExpectedExitCode 0

    $aboutCandidateJson = $aboutCandidate.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'about fuzzy candidate id selected' -Actual $aboutCandidateJson.selectedCandidate.name -Expected 'CheckCommand'
    Assert-Equal -Name 'about fuzzy candidate selection mode' -Actual $aboutCandidateJson.selectionInput.mode -Expected 'candidateId'

    $aboutInvalidCandidate = Invoke-Navlyn `
        -Name 'about fuzzy invalid candidate id' `
        -Arguments @('about', '--workspace', 'navlyn.slnx', '--candidate-id', 'bad') `
        -ExpectedExitCode 2

    Assert-Empty -Name 'about fuzzy invalid candidate stdout' -Text $aboutInvalidCandidate.Stdout
    Assert-Contains -Name 'about fuzzy invalid candidate stderr' -Text $aboutInvalidCandidate.Stderr -Expected 'NAVLYN1701:'

    $whereUsed = Invoke-Navlyn `
        -Name 'where-used fuzzy references' `
        -Arguments @('where-used', '--workspace', 'navlyn.slnx', '--query', 'CheckCommand', '--assume-kind', 'NamedType', '--limit', '1', '--include-snippets', '--snippet-lines', '0') `
        -ExpectedExitCode 0

    $whereUsedJson = $whereUsed.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'where-used fuzzy confidence' -Actual $whereUsedJson.confidence -Expected 'high'
    Assert-Equal -Name 'where-used fuzzy total matches' -Actual $whereUsedJson.totalMatches -Expected 1
    Assert-Equal -Name 'where-used fuzzy reference end column' -Actual @($whereUsedJson.references)[0].endColumn -Expected 49
    Assert-Equal -Name 'where-used fuzzy snippet line' -Actual @(@($whereUsedJson.references)[0].snippet.lines)[0] -Expected '        rootCommand.Subcommands.Add(CheckCommand.Create());'

    $about = Invoke-Navlyn `
        -Name 'about fuzzy summary' `
        -Arguments @('about', '--workspace', 'navlyn.slnx', '--query', 'CheckCommand', '--assume-kind', 'NamedType', '--member-limit', '2', '--reference-limit', '1') `
        -ExpectedExitCode 0

    $aboutJson = $about.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'about fuzzy selected' -Actual $aboutJson.selectedCandidate.name -Expected 'CheckCommand'
    Assert-Equal -Name 'about fuzzy members returned' -Actual @($aboutJson.members.members).Count -Expected 2

    $related = Invoke-Navlyn `
        -Name 'related fuzzy files' `
        -Arguments @('related', '--workspace', 'navlyn.slnx', '--query', 'CheckCommand', '--assume-kind', 'NamedType', '--limit', '2') `
        -ExpectedExitCode 0

    $relatedJson = $related.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'related fuzzy total files' -Actual $relatedJson.totalFiles -Expected 2
    Assert-Equal -Name 'related fuzzy first reason' -Actual @(@($relatedJson.files)[0].reasons)[0] -Expected 'declares-selected-symbol'

    $impact = Invoke-Navlyn `
        -Name 'impact fuzzy files' `
        -Arguments @('impact', '--workspace', 'navlyn.slnx', '--query', 'CheckCommand', '--assume-kind', 'NamedType', '--limit', '2') `
        -ExpectedExitCode 0

    $impactJson = $impact.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'impact fuzzy total files' -Actual $impactJson.totalFiles -Expected 1
    Assert-Equal -Name 'impact fuzzy level' -Actual @($impactJson.files)[0].impactLevel -Expected 'direct'

    $entrypoints = Invoke-Navlyn `
        -Name 'entrypoints fuzzy no chains for type' `
        -Arguments @('entrypoints', '--workspace', 'navlyn.slnx', '--query', 'CheckCommand', '--assume-kind', 'NamedType', '--limit', '2') `
        -ExpectedExitCode 0

    $entrypointsJson = $entrypoints.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'entrypoints fuzzy total chains' -Actual $entrypointsJson.totalChains -Expected 0

    $implementations = Invoke-Navlyn `
        -Name 'implementations non-applicable symbol' `
        -Arguments @('implementations', '--workspace', 'navlyn.slnx', '--file', 'navlyn/Cli/Commands/CheckCommand.cs', '--line', '6', '--column', '23') `
        -ExpectedExitCode 0

    $implementationsJson = $implementations.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'implementations file' -Actual $implementationsJson.file -Expected 'navlyn/Cli/Commands/CheckCommand.cs'
    Assert-Equal -Name 'implementations line' -Actual $implementationsJson.line -Expected 6
    Assert-Equal -Name 'implementations column' -Actual $implementationsJson.column -Expected 23
    Assert-Equal -Name 'implementations symbol name' -Actual $implementationsJson.symbol.name -Expected 'CheckCommand'
    Assert-Equal -Name 'implementations symbol kind' -Actual $implementationsJson.symbol.kind -Expected 'NamedType'
    Assert-Equal -Name 'implementations count' -Actual @($implementationsJson.implementations).Count -Expected 0

    $callers = Invoke-Navlyn `
        -Name 'callers method declaration' `
        -Arguments @('callers', '--workspace', 'navlyn.slnx', '--file', 'navlyn/Cli/Commands/CheckCommand.cs', '--line', '8', '--column', '27') `
        -ExpectedExitCode 0

    $callersJson = $callers.Stdout | ConvertFrom-Json
    $callerGroup = @($callersJson.callers | Where-Object { $_.symbol.name -eq 'CreateRootCommand' })[0]
    Assert-Equal -Name 'callers file' -Actual $callersJson.file -Expected 'navlyn/Cli/Commands/CheckCommand.cs'
    Assert-Equal -Name 'callers symbol name' -Actual $callersJson.symbol.name -Expected 'Create'
    Assert-Equal -Name 'callers symbol kind' -Actual $callersJson.symbol.kind -Expected 'Method'
    Assert-Equal -Name 'callers contains CreateRootCommand' -Actual $callerGroup.symbol.name -Expected 'CreateRootCommand'
    Assert-Equal -Name 'callers location line' -Actual @($callerGroup.locations)[0].line -Expected 48
    Assert-Equal -Name 'callers location has span' -Actual (@($callerGroup.locations)[0].endColumn -gt @($callerGroup.locations)[0].column) -Expected $true

    $calls = Invoke-Navlyn `
        -Name 'calls containing member' `
        -Arguments @('calls', '--workspace', 'navlyn.slnx', '--file', 'navlyn/Cli/NavlynCli.cs', '--line', '45', '--column', '32') `
        -ExpectedExitCode 0

    $callsJson = $calls.Stdout | ConvertFrom-Json
    $checkCreateCall = @($callsJson.calls | Where-Object { $_.symbol.container -eq 'Navlyn.Cli.Commands.CheckCommand' -and $_.symbol.name -eq 'Create' })[0]
    Assert-Equal -Name 'calls file' -Actual $callsJson.file -Expected 'navlyn/Cli/NavlynCli.cs'
    Assert-Equal -Name 'calls caller name' -Actual $callsJson.caller.name -Expected 'CreateRootCommand'
    Assert-Equal -Name 'calls contains CheckCommand.Create' -Actual $checkCreateCall.symbol.name -Expected 'Create'
    Assert-Equal -Name 'calls CheckCommand.Create location line' -Actual @($checkCreateCall.locations)[0].line -Expected 48
    Assert-Equal -Name 'calls CheckCommand.Create location has span' -Actual (@($checkCreateCall.locations)[0].endColumn -gt @($checkCreateCall.locations)[0].column) -Expected $true

    $callsMetadata = Invoke-Navlyn `
        -Name 'calls include metadata' `
        -Arguments @('calls', '--workspace', 'navlyn.slnx', '--file', 'navlyn/Cli/NavlynCli.cs', '--line', '45', '--column', '32', '--include-metadata', '--result-kind', 'Method', '--limit', '1') `
        -ExpectedExitCode 0
    $callsMetadataJson = $callsMetadata.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'calls include metadata flag' -Actual $callsMetadataJson.includeMetadata -Expected $true
    Assert-Equal -Name 'calls include metadata limit' -Actual $callsMetadataJson.limit -Expected 1

    $missingOption = Invoke-Navlyn `
        -Name 'check missing workspace option' `
        -Arguments @('check') `
        -ExpectedExitCode 2
    Assert-Empty -Name 'missing workspace stdout' -Text $missingOption.Stdout
    Assert-Contains -Name 'missing workspace stderr' -Text $missingOption.Stderr -Expected 'NAVLYN1001:'

    $invalidExtension = Invoke-Navlyn `
        -Name 'check invalid workspace extension' `
        -Arguments @('check', '--workspace', 'AGENTS.md') `
        -ExpectedExitCode 2
    Assert-Empty -Name 'invalid extension stdout' -Text $invalidExtension.Stdout
    Assert-Contains -Name 'invalid extension stderr' -Text $invalidExtension.Stderr -Expected 'NAVLYN1101:'

    $missingWorkspace = Invoke-Navlyn `
        -Name 'check missing workspace file' `
        -Arguments @('check', '--workspace', 'missing.slnx') `
        -ExpectedExitCode 2
    Assert-Empty -Name 'missing workspace file stdout' -Text $missingWorkspace.Stdout
    Assert-Contains -Name 'missing workspace file stderr' -Text $missingWorkspace.Stderr -Expected 'NAVLYN1102:'

    Write-Host 'CLI contract checks passed.'
}
finally {
    Pop-Location
}

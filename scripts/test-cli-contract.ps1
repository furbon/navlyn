[CmdletBinding()]
param(
    [switch]$NoBuild,
    [ValidateSet('core', 'navigation', 'workflow', 'domain', 'mcp-adjacent', 'all')]
    [string]$Suite = 'core',
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
$TargetFrameworkScript = Join-Path $RepoRoot 'scripts/lib/navlyn-target-framework.ps1'

. $TargetFrameworkScript

$TargetFramework = Get-NavlynPreferredTargetFramework -ProjectPath $ProjectPath
. $PSScriptRoot/lib/navlyn-test-harness.ps1
Initialize-NavlynTestHarness -RepoRoot $RepoRoot -ShowOutput:$ShowOutput
$NavlynDll = $script:NavlynTestDll

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

function Invoke-CoreErrorChecks {
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
}

function Test-ContractSuite {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    return $Suite -eq 'all' -or $Suite -eq $Name
}

function Invoke-NavigationContractGate {
    $navigationBatchInput = @'
{
  "defaults": {
    "project": "Navlyn.CommandLine"
  },
  "requests": [
    { "id": "symbols", "command": "symbols", "query": "Check", "limit": 1 },
    { "id": "symbols-in", "command": "symbols-in", "file": "Navlyn.CommandLine/Cli/NavlynCli.cs", "line": 60 },
    { "id": "outline", "command": "outline", "file": "Navlyn.CommandLine/Cli/Commands/CheckCommand.cs" },
    { "id": "symbol-at", "command": "symbol-at", "file": "Navlyn.CommandLine/Cli/Commands/CheckCommand.cs", "line": 6, "column": 23 },
    { "id": "symbol-info", "command": "symbol-info", "file": "Navlyn.CommandLine/Cli/NavlynCli.cs", "line": 60, "column": 37 },
    { "id": "symbol-source", "command": "symbol-source", "file": "Navlyn.CommandLine/Cli/Commands/CheckCommand.cs", "line": 6, "column": 23, "view": "declaration", "maxLines": 1 },
    { "id": "type-hierarchy", "command": "type-hierarchy", "file": "Navlyn.CommandLine/Cli/Commands/CheckCommand.cs", "line": 6, "column": 23 },
    { "id": "definition", "command": "definition", "file": "Navlyn.CommandLine/Cli/NavlynCli.cs", "line": 60, "column": 37 },
    { "id": "references", "command": "references", "file": "Navlyn.CommandLine/Cli/NavlynCli.cs", "line": 60, "column": 37 },
    { "id": "find", "command": "find", "query": "CheckCommand", "assumeKind": "CLASS" },
    { "id": "where-used", "command": "where-used", "candidateIdFrom": "find", "limit": 1, "includeSnippets": true, "snippetLines": 0 },
    { "id": "about", "command": "about", "candidateIdFrom": "find", "memberLimit": 2, "referenceLimit": 1 },
    { "id": "related", "command": "related", "candidateIdFrom": "find", "limit": 2 },
    { "id": "impact", "command": "impact", "candidateIdFrom": "find", "limit": 2 },
    { "id": "entrypoints", "command": "entrypoints", "candidateIdFrom": "find", "limit": 2 },
    { "id": "implementations", "command": "implementations", "file": "Navlyn.CommandLine/Cli/Commands/CheckCommand.cs", "line": 6, "column": 23 },
    { "id": "callers", "command": "callers", "file": "Navlyn.CommandLine/Cli/Commands/CheckCommand.cs", "line": 8, "column": 27 },
    { "id": "calls", "command": "calls", "file": "Navlyn.CommandLine/Cli/NavlynCli.cs", "line": 60, "column": 37 }
  ]
}
'@

    $navigationBatch = Invoke-Navlyn `
        -Name 'navigation aggregate batch' `
        -Arguments @('batch', '--workspace', 'navlyn.slnx') `
        -ExpectedExitCode 0 `
        -StandardInput $navigationBatchInput

    Assert-Empty -Name 'navigation aggregate batch stderr' -Text $navigationBatch.Stderr
    $navigationBatchJson = $navigationBatch.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'navigation aggregate batch request count' -Actual $navigationBatchJson.totalRequests -Expected 18
    Assert-Equal -Name 'navigation aggregate batch succeeded count' -Actual $navigationBatchJson.succeededRequests -Expected 18
    Assert-Equal -Name 'navigation aggregate symbols command' -Actual @($navigationBatchJson.results)[0].command -Expected 'symbols'
    Assert-Equal -Name 'navigation aggregate symbol-at name' -Actual @($navigationBatchJson.results)[3].result.symbol.name -Expected 'CheckCommand'
    Assert-Equal -Name 'navigation aggregate definition path' -Actual @(@($navigationBatchJson.results)[7].result.definitions)[0].path -Expected 'Navlyn.CommandLine/Cli/Commands/CheckCommand.cs'
    Assert-Equal -Name 'navigation aggregate where-used count' -Actual @($navigationBatchJson.results)[10].result.totalMatches -Expected 1
    Assert-Equal -Name 'navigation aggregate callers count' -Actual (@(@($navigationBatchJson.results)[16].result.callers).Count -ge 1) -Expected $true
    Assert-Equal -Name 'navigation aggregate calls count' -Actual (@(@($navigationBatchJson.results)[17].result.calls).Count -ge 1) -Expected $true

    $signature = Invoke-Navlyn `
        -Name 'signature aggregate gate' `
        -Arguments @('signature', '--workspace', 'navlyn.slnx', '--file', 'Navlyn.CommandLine/Cli/Commands/CheckCommand.cs', '--line', '8', '--column', '27') `
        -ExpectedExitCode 0

    $signatureJson = $signature.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'signature aggregate symbol name' -Actual $signatureJson.symbol.name -Expected 'Create'

    $scopeAt = Invoke-Navlyn `
        -Name 'scope-at aggregate gate' `
        -Arguments @('scope-at', '--workspace', 'navlyn.slnx', '--file', 'Navlyn.CommandLine/Cli/NavlynCli.cs', '--line', '60', '--column', '37') `
        -ExpectedExitCode 0

    $scopeAtJson = $scopeAt.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'scope-at aggregate containing symbol' -Actual $scopeAtJson.containingSymbol.name -Expected 'CreateRootCommand'
}

Push-Location $RepoRoot
try {
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

    Write-Host "Running CLI contract checks ($Suite suite)..."

    if (Test-ContractSuite -Name 'core') {
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

    $doctorHelp = Invoke-Navlyn `
        -Name 'doctor help text' `
        -Arguments @('doctor', '--help') `
        -ExpectedExitCode 0

    Assert-Empty -Name 'doctor help stderr' -Text $doctorHelp.Stderr
    Assert-Contains -Name 'doctor help description' -Text $doctorHelp.Stdout -Expected 'Diagnose Navlyn, .NET SDK, and workspace readiness'

    $doctor = Invoke-Navlyn `
        -Name 'doctor valid workspace' `
        -Arguments @('doctor', '--workspace', 'navlyn.slnx') `
        -ExpectedExitCode 0

    Assert-Empty -Name 'doctor valid workspace stderr' -Text $doctor.Stderr
    $doctorJson = $doctor.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'doctor command' -Actual $doctorJson.command -Expected 'doctor'
    Assert-Equal -Name 'doctor ok' -Actual $doctorJson.ok -Expected $true
    Assert-Equal -Name 'doctor workspace loaded' -Actual $doctorJson.workspace.loaded -Expected $true
    Assert-Equal -Name 'doctor workspace projects' -Actual $doctorJson.workspace.projectCount -Expected 5

    $doctorMissing = Invoke-Navlyn `
        -Name 'doctor missing workspace' `
        -Arguments @('doctor', '--workspace', 'missing.slnx') `
        -ExpectedExitCode 0

    Assert-Empty -Name 'doctor missing workspace stderr' -Text $doctorMissing.Stderr
    $doctorMissingJson = $doctorMissing.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'doctor missing ok' -Actual $doctorMissingJson.ok -Expected $false
    Assert-Equal -Name 'doctor missing error code' -Actual $doctorMissingJson.workspace.error.code -Expected 'NAVLYN1102'

    $validCheck = Invoke-Navlyn `
        -Name 'check valid workspace' `
        -Arguments @('check', '--workspace', 'navlyn.slnx') `
        -ExpectedExitCode 0

    $validJson = $validCheck.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'check valid workspace ok' -Actual $validJson.ok -Expected $true
    Assert-Equal -Name 'check valid workspace workspace' -Actual $validJson.workspace -Expected 'navlyn.slnx'
    Assert-Equal -Name 'check valid workspace kind' -Actual $validJson.kind -Expected 'solution'
    Assert-Equal -Name 'check valid workspace projects' -Actual $validJson.projects -Expected 5

    $overview = Invoke-Navlyn `
        -Name 'overview valid workspace' `
        -Arguments @('overview', '--workspace', 'navlyn.slnx') `
        -ExpectedExitCode 0

    $overviewJson = $overview.Stdout | ConvertFrom-Json
    $overviewProject = @($overviewJson.projects | Where-Object { $_.path -eq 'navlyn/navlyn.csproj' -and $_.targetFramework -eq 'net10.0' })[0]
    Assert-Equal -Name 'overview workspace' -Actual $overviewJson.workspace -Expected 'navlyn.slnx'
    Assert-Equal -Name 'overview kind' -Actual $overviewJson.kind -Expected 'solution'
    Assert-Equal -Name 'overview project count' -Actual @($overviewJson.projects).Count -Expected 5
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
    $repoGraphNavlynProject = @($repoGraphJson.projects.items | Where-Object { $_.path -eq 'navlyn/navlyn.csproj' -and $_.targetFramework -eq 'net10.0' })[0]
    $repoGraphTestProject = @($repoGraphJson.projects.items | Where-Object { $_.path -eq 'navlyn.Tests/navlyn.Tests.csproj' })[0]
    Assert-Equal -Name 'repo-graph command' -Actual $repoGraphJson.command -Expected 'repo-graph'
    Assert-Equal -Name 'repo-graph workspace' -Actual $repoGraphJson.workspace -Expected 'navlyn.slnx'
    Assert-Equal -Name 'repo-graph project count' -Actual $repoGraphJson.projects.totalProjects -Expected 5
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
    $subdirectoryOverviewProject = @($subdirectoryOverviewJson.projects | Where-Object { $_.path -eq 'navlyn/navlyn.csproj' -and $_.targetFramework -eq 'net10.0' })[0]
    Assert-Equal -Name 'subdirectory overview workspace' -Actual $subdirectoryOverviewJson.workspace -Expected 'navlyn.slnx'
    Assert-Equal -Name 'subdirectory overview project path' -Actual $subdirectoryOverviewProject.path -Expected 'navlyn/navlyn.csproj'

    $subdirectoryProjectFilter = Invoke-Navlyn `
        -Name 'diagnostics from subdirectory with repo-relative project filter' `
        -Arguments @('diagnostics', '--workspace', 'navlyn.slnx', '--project', 'navlyn.Tests', '--limit', '1') `
        -ExpectedExitCode 0 `
        -WorkingDirectory $ProjectDir

    $subdirectoryProjectFilterJson = $subdirectoryProjectFilter.Stdout | ConvertFrom-Json
    $subdirectoryAppliedProject = @($subdirectoryProjectFilterJson.projects)[0]
    Assert-Equal -Name 'subdirectory project filter path' -Actual $subdirectoryAppliedProject.path -Expected 'navlyn.Tests/navlyn.Tests.csproj'

    $subdirectorySymbolAt = Invoke-Navlyn `
        -Name 'symbol-at from subdirectory with repo-relative source file' `
        -Arguments @('symbol-at', '--workspace', 'navlyn.slnx', '--file', 'Navlyn.CommandLine/Cli/Commands/CheckCommand.cs', '--line', '6', '--column', '23') `
        -ExpectedExitCode 0 `
        -WorkingDirectory $ProjectDir

    $subdirectorySymbolAtJson = $subdirectorySymbolAt.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'subdirectory symbol-at file' -Actual $subdirectorySymbolAtJson.file -Expected 'Navlyn.CommandLine/Cli/Commands/CheckCommand.cs'
    Assert-Equal -Name 'subdirectory symbol-at symbol path' -Actual $subdirectorySymbolAtJson.symbol.path -Expected 'Navlyn.CommandLine/Cli/Commands/CheckCommand.cs'

    $diagnostics = Invoke-Navlyn `
        -Name 'diagnostics valid workspace' `
        -Arguments @('diagnostics', '--workspace', 'navlyn.slnx') `
        -ExpectedExitCode 0

    $diagnosticsJson = $diagnostics.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'diagnostics workspace' -Actual $diagnosticsJson.workspace -Expected 'navlyn.slnx'
    Assert-Equal -Name 'diagnostics kind' -Actual $diagnosticsJson.kind -Expected 'solution'
    Assert-Equal -Name 'diagnostics total is non-negative' -Actual ($diagnosticsJson.totalDiagnostics -ge 0) -Expected $true

    if ($Suite -eq 'core') {
        Invoke-CoreErrorChecks
        Write-Host 'CLI contract core checks passed.'
        return
    }

    }

    $reviewPackFixture = 'tests/fixtures/ReviewPacksFixture/ReviewPacksFixture.csproj'
    $frameworkFixture = 'tests/fixtures/FrameworkEntrypointsFixture/FrameworkEntrypointsFixture.csproj'
    $diFixture = 'tests/fixtures/DependencyInjectionFixture/DependencyInjectionFixture.csproj'
    $applicationDomainFixture = 'tests/fixtures/ApplicationDomainFixture/ApplicationDomainFixture.csproj'

    if (Test-ContractSuite -Name 'workflow') {
    $reviewDiff = Invoke-Navlyn `
        -Name 'review-diff valid workspace' `
        -Arguments @('review-diff', '--workspace', 'navlyn.slnx', '--base', 'HEAD', '--head', 'HEAD', '--symbol-limit', '1', '--impact-limit', '1', '--diagnostic-limit', '1', '--related-test-limit', '1', '--depth', '1') `
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
        -Arguments @('public-api-diff', '--workspace', 'navlyn.slnx', '--base', 'HEAD', '--head', 'HEAD', '--project', 'navlyn', '--change-limit', '5') `
        -ExpectedExitCode 0

    Assert-Empty -Name 'public-api-diff stderr' -Text $publicApiDiff.Stderr
    $publicApiDiffJson = $publicApiDiff.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'public-api-diff command' -Actual $publicApiDiffJson.command -Expected 'public-api-diff'
    Assert-Equal -Name 'public-api-diff base' -Actual $publicApiDiffJson.comparison.base -Expected 'HEAD'
    Assert-Equal -Name 'public-api-diff head' -Actual $publicApiDiffJson.comparison.head -Expected 'HEAD'
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
        -Arguments @('tests-for-symbol', '--workspace', 'navlyn.slnx', '--query', 'RepoGraphResolver', '--assume-kind', 'NamedType', '--project', 'Navlyn.Core', '--test-project', 'navlyn.Tests', '--test-limit', '5') `
        -ExpectedExitCode 0

    Assert-Empty -Name 'tests-for-symbol stderr' -Text $testsForSymbol.Stderr
    $testsForSymbolJson = $testsForSymbol.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'tests-for-symbol command' -Actual $testsForSymbolJson.command -Expected 'tests-for-symbol'
    Assert-Equal -Name 'tests-for-symbol selection mode' -Actual $testsForSymbolJson.selectionInput.mode -Expected 'query'
    Assert-Equal -Name 'tests-for-symbol subject' -Actual $testsForSymbolJson.subject.name -Expected 'RepoGraphResolver'
    Assert-Equal -Name 'tests-for-symbol has related tests' -Actual ($testsForSymbolJson.tests.totalCandidates -ge 1) -Expected $true

    $testsForDiff = Invoke-Navlyn `
        -Name 'tests-for-diff valid workspace' `
        -Arguments @('tests-for-diff', '--workspace', 'navlyn.slnx', '--base', 'HEAD', '--head', 'HEAD', '--project', 'navlyn', '--test-project', 'navlyn.Tests', '--symbol-limit', '1', '--test-limit', '1') `
        -ExpectedExitCode 0

    Assert-Empty -Name 'tests-for-diff stderr' -Text $testsForDiff.Stderr
    $testsForDiffJson = $testsForDiff.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'tests-for-diff command' -Actual $testsForDiffJson.command -Expected 'tests-for-diff'
    Assert-Equal -Name 'tests-for-diff total files non-negative' -Actual ($testsForDiffJson.diff.totalFiles -ge 0) -Expected $true
    Assert-Equal -Name 'tests-for-diff tests non-negative' -Actual ($testsForDiffJson.tests.totalCandidates -ge 0) -Expected $true
    }

    if (Test-ContractSuite -Name 'domain') {
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
    }

    if (Test-ContractSuite -Name 'workflow') {
    $reviewDiffInvalid = Invoke-Navlyn `
        -Name 'review-diff head without base' `
        -Arguments @('review-diff', '--workspace', 'navlyn.slnx', '--head', 'HEAD') `
        -ExpectedExitCode 2
    Assert-Empty -Name 'review-diff head without base stdout' -Text $reviewDiffInvalid.Stdout
    Assert-Contains -Name 'review-diff head without base stderr' -Text $reviewDiffInvalid.Stderr -Expected 'NAVLYN1503:'

    $contextPackQuery = Invoke-Navlyn `
        -Name 'context-pack query mode' `
        -Arguments @('context-pack', '--workspace', 'navlyn.slnx', '--query', 'CheckCommand', '--assume-kind', 'NamedType', '--project', 'Navlyn.CommandLine', '--budget-tokens', '2000', '--item-limit', '5') `
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
        -Arguments @('context-pack', '--workspace', 'navlyn.slnx', '--query', 'CheckCommand', '--assume-kind', 'NamedType', '--project', 'Navlyn.CommandLine', '--goal', 'modify', '--change-kind', 'signature', '--profile', 'compact', '--budget-tokens', '2000', '--item-limit', '5') `
        -ExpectedExitCode 0

    Assert-Empty -Name 'context-pack change kind stderr' -Text $contextPackChangeKind.Stderr
    $contextPackChangeKindJson = $contextPackChangeKind.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'context-pack change kind top-level' -Actual $contextPackChangeKindJson.changeKind -Expected 'signature'
    Assert-Equal -Name 'context-pack change kind config' -Actual $contextPackChangeKindJson.configuration.changeKind -Expected 'signature'
    Assert-Equal -Name 'context-pack change kind option' -Actual $contextPackChangeKindJson.configuration.options.changeKind -Expected 'signature'

    $editPreflight = Invoke-Navlyn `
        -Name 'edit-preflight unique target' `
        -Arguments @('edit-preflight', '--workspace', 'navlyn.slnx', '--query', 'DoctorCommand', '--assume-kind', 'NamedType', '--project', 'Navlyn.CommandLine', '--goal', 'modify', '--change-kind', 'behavior', '--budget-tokens', '3000', '--item-limit', '5', '--reference-limit', '10', '--test-limit', '5') `
        -ExpectedExitCode 0

    Assert-Empty -Name 'edit-preflight stderr' -Text $editPreflight.Stderr
    $editPreflightJson = $editPreflight.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'edit-preflight schema' -Actual $editPreflightJson.schemaVersion -Expected 'navlyn.edit-preflight.v1'
    Assert-Equal -Name 'edit-preflight command' -Actual $editPreflightJson.command -Expected 'edit-preflight'
    Assert-Equal -Name 'edit-preflight anchor name' -Actual $editPreflightJson.anchor.name -Expected 'DoctorCommand'
    Assert-Equal -Name 'edit-preflight intent goal' -Actual $editPreflightJson.intent.goal -Expected 'modify'
    Assert-Equal -Name 'edit-preflight source status' -Actual $editPreflightJson.source.status -Expected 'ok'
    Assert-Equal -Name 'edit-preflight has post guard' -Actual (@($editPreflightJson.nextCommands | Where-Object { $_.command -eq 'post-edit-guard' }).Count -ge 1) -Expected $true

    $changeIntent = Invoke-Navlyn `
        -Name 'change-intent-pack unique target' `
        -Arguments @('change-intent-pack', '--workspace', 'navlyn.slnx', '--query', 'DoctorCommand', '--assume-kind', 'NamedType', '--project', 'Navlyn.CommandLine', '--goal', 'modify', '--change-kind', 'behavior') `
        -ExpectedExitCode 0

    Assert-Empty -Name 'change-intent-pack stderr' -Text $changeIntent.Stderr
    $changeIntentJson = $changeIntent.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'change-intent-pack schema' -Actual $changeIntentJson.schemaVersion -Expected 'navlyn.agent-intent.v1'
    Assert-Equal -Name 'change-intent-pack anchor name' -Actual $changeIntentJson.anchor.name -Expected 'DoctorCommand'

    $postEditGuard = Invoke-Navlyn `
        -Name 'post-edit-guard empty diff policy fail' `
        -Arguments @('post-edit-guard', '--workspace', 'navlyn.slnx', '--candidate-id', $editPreflightJson.anchor.candidateId, '--base', 'HEAD', '--head', 'HEAD', '--fail-on-risk', 'high') `
        -ExpectedExitCode 1

    Assert-Empty -Name 'post-edit-guard stderr' -Text $postEditGuard.Stderr
    $postEditGuardJson = $postEditGuard.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'post-edit-guard schema' -Actual $postEditGuardJson.schemaVersion -Expected 'navlyn.agent-guard.v1'
    Assert-Equal -Name 'post-edit-guard risk' -Actual $postEditGuardJson.risk -Expected 'high'
    Assert-Equal -Name 'post-edit-guard policy failed' -Actual $postEditGuardJson.policy.passed -Expected $false

    $wrongSymbolGuard = Invoke-Navlyn `
        -Name 'wrong-symbol-guard empty diff policy fail' `
        -Arguments @('wrong-symbol-guard', '--workspace', 'navlyn.slnx', '--query', 'DoctorCommand', '--assume-kind', 'NamedType', '--project', 'Navlyn.CommandLine', '--base', 'HEAD', '--head', 'HEAD', '--fail-on-risk', 'high') `
        -ExpectedExitCode 1

    Assert-Empty -Name 'wrong-symbol-guard stderr' -Text $wrongSymbolGuard.Stderr
    $wrongSymbolGuardJson = $wrongSymbolGuard.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'wrong-symbol-guard schema' -Actual $wrongSymbolGuardJson.schemaVersion -Expected 'navlyn.agent-guard.v1'
    Assert-Equal -Name 'wrong-symbol-guard risk' -Actual $wrongSymbolGuardJson.risk -Expected 'high'
    Assert-Equal -Name 'wrong-symbol-guard no changed symbols reason' -Actual (@($wrongSymbolGuardJson.reasonCodes) -contains 'no-changed-symbols-found') -Expected $true

    $contextPackDiff = Invoke-Navlyn `
        -Name 'context-pack diff mode' `
        -Arguments @('context-pack', '--workspace', 'navlyn.slnx', '--diff', '--base', 'HEAD', '--head', 'HEAD', '--symbol-limit', '1', '--impact-limit', '1', '--diagnostic-limit', '1', '--related-test-limit', '1', '--item-limit', '1') `
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
    }

    if (Test-ContractSuite -Name 'mcp-adjacent') {
        $fixture = 'tests/fixtures/FuzzyDiscoveryFixture/FuzzyDiscoveryFixture.csproj'
        $file = 'tests/fixtures/FuzzyDiscoveryFixture/FixtureCode.cs'
        Invoke-CheckedProcess -Name 'batch fixture restore' -FilePath dotnet -Arguments @('restore', $fixture) -ExpectedExitCode 0 | Out-Null
        $inputJson = @{
            requests = @(
                @{ id = 'target'; command = 'resolve-target'; query = 'EnemyManagerTools'; assumeKind = 'NamedType' },
                @{ id = 'source'; command = 'symbol-source'; candidateIdFrom = 'target'; view = 'declaration' },
                @{ id = 'calls'; command = 'calls'; file = $file; line = 26; column = 21; limit = 2 },
                @{ id = 'graph'; command = 'repo-graph'; profile = 'compact'; relationshipLimit = 2 },
                @{ id = 'framework'; command = 'framework-entrypoints'; profile = 'compact'; evidenceLimit = 2 },
                @{ id = 'di'; command = 'di-graph'; profile = 'compact'; registrationLimit = 2 },
                @{ id = 'invalid'; command = 'unsupported-command' }
            )
        } | ConvertTo-Json -Depth 20 -Compress
        $batch = Invoke-Navlyn -Name 'small batch contracts' -Arguments @('batch','--workspace',$fixture) -ExpectedExitCode 0 -StandardInput $inputJson
        Assert-Empty -Name 'batch stderr' -Text $batch.Stderr
        $json = $batch.Stdout | ConvertFrom-Json
        Assert-Equal -Name 'batch total' -Actual $json.totalRequests -Expected 7
        Assert-Equal -Name 'batch succeeded' -Actual $json.succeededRequests -Expected 6
        Assert-Equal -Name 'batch failed' -Actual $json.failedRequests -Expected 1
        Assert-Equal -Name 'chain candidate id' -Actual $json.results[1].result.selectionInput.candidateId -Expected $json.results[0].result.candidateId
        Assert-Equal -Name 'local call' -Actual $json.results[2].result.calls[0].symbol.name -Expected 'Spawn'
        Assert-Equal -Name 'graph limits' -Actual $json.results[3].result.limits.relationshipLimit -Expected 2
        Assert-Equal -Name 'framework command' -Actual $json.results[4].result.command -Expected 'framework-entrypoints'
        Assert-Equal -Name 'di command' -Actual $json.results[5].result.command -Expected 'di-graph'
        Assert-Equal -Name 'batch invalid error' -Actual $json.results[6].ok -Expected $false
        $invalidJson = Invoke-Navlyn -Name 'invalid batch JSON' -Arguments @('batch','--workspace',$fixture) -ExpectedExitCode 2 -StandardInput '{'
        Assert-Empty -Name 'invalid batch stdout' -Text $invalidJson.Stdout
        Assert-Contains -Name 'invalid batch diagnostic' -Text $invalidJson.Stderr -Expected 'NAVLYN'
    }

    if (Test-ContractSuite -Name 'navigation') {
    if ($Suite -eq 'all') {
        Invoke-NavigationContractGate
    }
    else {
    $symbols = Invoke-Navlyn `
        -Name 'symbols partial query' `
        -Arguments @('symbols', '--workspace', 'navlyn.slnx', '--project', 'Navlyn.CommandLine', '--project', 'Navlyn.CommandLine', '--query', 'Check') `
        -ExpectedExitCode 0

    $symbolsJson = $symbols.Stdout | ConvertFrom-Json
    $symbolMatch = @($symbolsJson.matches | Where-Object { $_.name -eq 'CheckCommand' })[0]
    Assert-Equal -Name 'symbols query' -Actual $symbolsJson.query -Expected 'Check'
    Assert-Equal -Name 'symbols match mode' -Actual $symbolsJson.match -Expected 'contains'
    Assert-Equal -Name 'symbols case sensitivity' -Actual $symbolsJson.caseSensitive -Expected $false
    Assert-Equal -Name 'symbols kind filter count' -Actual @($symbolsJson.kinds).Count -Expected 0
    Assert-Equal -Name 'symbols limit' -Actual $symbolsJson.limit -Expected $null
    Assert-Equal -Name 'symbols total matches' -Actual $symbolsJson.totalMatches -Expected 12
    Assert-Equal -Name 'symbols contains CheckCommand' -Actual $symbolMatch.name -Expected 'CheckCommand'
    Assert-Equal -Name 'symbols CheckCommand kind' -Actual $symbolMatch.kind -Expected 'NamedType'
    Assert-Equal -Name 'symbols CheckCommand path' -Actual $symbolMatch.path -Expected 'Navlyn.CommandLine/Cli/Commands/CheckCommand.cs'
    Assert-Equal -Name 'symbols CheckCommand line' -Actual $symbolMatch.line -Expected 6
    Assert-Equal -Name 'symbols CheckCommand column' -Actual $symbolMatch.column -Expected 23
    Assert-Equal -Name 'symbols CheckCommand end line' -Actual $symbolMatch.endLine -Expected 6
    Assert-Equal -Name 'symbols CheckCommand end column' -Actual $symbolMatch.endColumn -Expected 35

    $symbolsLimit = Invoke-Navlyn `
        -Name 'symbols limited query' `
        -Arguments @('symbols', '--workspace', 'navlyn.slnx', '--project', 'Navlyn.CommandLine', '--project', 'Navlyn.CommandLine', '--query', 'Check', '--limit', '1') `
        -ExpectedExitCode 0

    $symbolsLimitJson = $symbolsLimit.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbols limited query limit' -Actual $symbolsLimitJson.limit -Expected 1
    Assert-Equal -Name 'symbols limited query total matches' -Actual $symbolsLimitJson.totalMatches -Expected 12
    Assert-Equal -Name 'symbols limited query match count' -Actual @($symbolsLimitJson.matches).Count -Expected 1
    Assert-Equal -Name 'symbols limited query first match name' -Actual @($symbolsLimitJson.matches)[0].name -Expected 'CheckCommand'

    $symbolsKind = Invoke-Navlyn `
        -Name 'symbols kind filter query' `
        -Arguments @('symbols', '--workspace', 'navlyn.slnx', '--project', 'Navlyn.CommandLine', '--project', 'Navlyn.CommandLine', '--query', 'Check', '--kind', 'cLaSs') `
        -ExpectedExitCode 0

    $symbolsKindJson = $symbolsKind.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbols kind filter count' -Actual @($symbolsKindJson.kinds).Count -Expected 1
    Assert-Equal -Name 'symbols kind filter value' -Actual @($symbolsKindJson.kinds)[0] -Expected 'NamedType'
    Assert-Equal -Name 'symbols kind filter total matches' -Actual $symbolsKindJson.totalMatches -Expected 6
    Assert-Equal -Name 'symbols kind filter first match kind' -Actual @($symbolsKindJson.matches)[0].kind -Expected 'NamedType'

    $symbolsNamespace = Invoke-Navlyn `
        -Name 'symbols namespace container accessibility filters' `
        -Arguments @('symbols', '--workspace', 'navlyn.slnx', '--project', 'Navlyn.CommandLine', '--project', 'Navlyn.CommandLine', '--query', 'Create', '--namespace', 'Navlyn.Cli.Commands', '--namespace-match', 'exact', '--container', 'CheckCommand', '--container-match', 'contains', '--accessibility', 'Public', '--limit', '1') `
        -ExpectedExitCode 0

    $symbolsNamespaceJson = $symbolsNamespace.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbols namespace filter total matches' -Actual $symbolsNamespaceJson.totalMatches -Expected 2
    Assert-Equal -Name 'symbols namespace filter namespace' -Actual @($symbolsNamespaceJson.namespaces)[0] -Expected 'Navlyn.Cli.Commands'
    Assert-Equal -Name 'symbols namespace filter match name' -Actual @($symbolsNamespaceJson.matches)[0].name -Expected 'Create'
    Assert-Equal -Name 'symbols namespace filter accessibility fact' -Actual @($symbolsNamespaceJson.matches)[0].facts.accessibility -Expected 'Public'

    $symbolsExact = Invoke-Navlyn `
        -Name 'symbols exact query' `
        -Arguments @('symbols', '--workspace', 'navlyn.slnx', '--project', 'Navlyn.CommandLine', '--project', 'Navlyn.CommandLine', '--query', 'CheckCommand', '--match', 'exact') `
        -ExpectedExitCode 0

    $symbolsExactJson = $symbolsExact.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbols exact match mode' -Actual $symbolsExactJson.match -Expected 'exact'
    Assert-Equal -Name 'symbols exact match count' -Actual @($symbolsExactJson.matches).Count -Expected 2
    Assert-Equal -Name 'symbols exact match name' -Actual @($symbolsExactJson.matches)[0].name -Expected 'CheckCommand'

    $symbolsRegex = Invoke-Navlyn `
        -Name 'symbols regex query' `
        -Arguments @('symbols', '--workspace', 'navlyn.slnx', '--project', 'Navlyn.CommandLine', '--project', 'Navlyn.CommandLine', '--query', '^Check.*Command$', '--match', 'regex') `
        -ExpectedExitCode 0

    $symbolsRegexJson = $symbolsRegex.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbols regex match mode' -Actual $symbolsRegexJson.match -Expected 'regex'
    Assert-Equal -Name 'symbols regex match count' -Actual @($symbolsRegexJson.matches).Count -Expected 2
    Assert-Equal -Name 'symbols regex match name' -Actual @($symbolsRegexJson.matches)[0].name -Expected 'CheckCommand'

    $symbolsCaseSensitive = Invoke-Navlyn `
        -Name 'symbols case-sensitive query' `
        -Arguments @('symbols', '--workspace', 'navlyn.slnx', '--project', 'Navlyn.CommandLine', '--project', 'Navlyn.CommandLine', '--query', 'check', '--case-sensitive') `
        -ExpectedExitCode 0

    $symbolsCaseSensitiveJson = $symbolsCaseSensitive.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbols case-sensitive flag' -Actual $symbolsCaseSensitiveJson.caseSensitive -Expected $true
    Assert-Equal -Name 'symbols case-sensitive match count' -Actual @($symbolsCaseSensitiveJson.matches).Count -Expected 4

    $symbolsInvalidRegex = Invoke-Navlyn `
        -Name 'symbols invalid regex' `
        -Arguments @('symbols', '--workspace', 'navlyn.slnx', '--project', 'Navlyn.CommandLine', '--project', 'Navlyn.CommandLine', '--query', '[', '--match', 'regex') `
        -ExpectedExitCode 2
    Assert-Empty -Name 'symbols invalid regex stdout' -Text $symbolsInvalidRegex.Stdout
    Assert-Contains -Name 'symbols invalid regex stderr' -Text $symbolsInvalidRegex.Stderr -Expected 'NAVLYN1002:'

    $symbolsInvalidMatch = Invoke-Navlyn `
        -Name 'symbols invalid match mode' `
        -Arguments @('symbols', '--workspace', 'navlyn.slnx', '--project', 'Navlyn.CommandLine', '--project', 'Navlyn.CommandLine', '--query', 'Check', '--match', 'starts-with') `
        -ExpectedExitCode 2
    Assert-Empty -Name 'symbols invalid match stdout' -Text $symbolsInvalidMatch.Stdout
    Assert-Contains -Name 'symbols invalid match stderr' -Text $symbolsInvalidMatch.Stderr -Expected 'NAVLYN1001:'

    $symbolsInvalidLimit = Invoke-Navlyn `
        -Name 'symbols invalid limit' `
        -Arguments @('symbols', '--workspace', 'navlyn.slnx', '--project', 'Navlyn.CommandLine', '--project', 'Navlyn.CommandLine', '--query', 'Check', '--limit', '0') `
        -ExpectedExitCode 2
    Assert-Empty -Name 'symbols invalid limit stdout' -Text $symbolsInvalidLimit.Stdout
    Assert-Contains -Name 'symbols invalid limit stderr' -Text $symbolsInvalidLimit.Stderr -Expected 'NAVLYN1003:'

    $symbolsInvalidKind = Invoke-Navlyn `
        -Name 'symbols invalid kind' `
        -Arguments @('symbols', '--workspace', 'navlyn.slnx', '--project', 'Navlyn.CommandLine', '--project', 'Navlyn.CommandLine', '--query', 'Check', '--kind', '1') `
        -ExpectedExitCode 2
    Assert-Empty -Name 'symbols invalid kind stdout' -Text $symbolsInvalidKind.Stdout
    Assert-Contains -Name 'symbols invalid kind stderr' -Text $symbolsInvalidKind.Stderr -Expected 'NAVLYN1004:'

    $symbolsIn = Invoke-Navlyn `
        -Name 'symbols-in source line' `
        -Arguments @('symbols-in', '--workspace', 'navlyn.slnx', '--file', 'Navlyn.CommandLine/Cli/NavlynCli.cs', '--line', '60') `
        -ExpectedExitCode 0

    $symbolsInJson = $symbolsIn.Stdout | ConvertFrom-Json
    $symbolsInMatch = @($symbolsInJson.symbols | Where-Object { $_.name -eq 'CheckCommand' })[0]
    Assert-Equal -Name 'symbols-in file' -Actual $symbolsInJson.file -Expected 'Navlyn.CommandLine/Cli/NavlynCli.cs'
    Assert-Equal -Name 'symbols-in line' -Actual $symbolsInJson.line -Expected 60
    Assert-Equal -Name 'symbols-in start column' -Actual $symbolsInJson.startColumn -Expected 1
    Assert-Equal -Name 'symbols-in end column' -Actual $symbolsInJson.endColumn -Expected 60
    Assert-Equal -Name 'symbols-in contains CheckCommand' -Actual $symbolsInMatch.name -Expected 'CheckCommand'
    Assert-Equal -Name 'symbols-in CheckCommand kind' -Actual $symbolsInMatch.kind -Expected 'NamedType'
    Assert-Equal -Name 'symbols-in CheckCommand line' -Actual $symbolsInMatch.line -Expected 60
    Assert-Equal -Name 'symbols-in CheckCommand column' -Actual $symbolsInMatch.column -Expected 37
    Assert-Equal -Name 'symbols-in CheckCommand end line' -Actual $symbolsInMatch.endLine -Expected 60
    Assert-Equal -Name 'symbols-in CheckCommand end column' -Actual $symbolsInMatch.endColumn -Expected 49

    $symbolsInSpan = Invoke-Navlyn `
        -Name 'symbols-in source span' `
        -Arguments @('symbols-in', '--workspace', 'navlyn.slnx', '--file', 'Navlyn.CommandLine/Cli/NavlynCli.cs', '--line', '60', '--start-column', '37', '--end-column', '49') `
        -ExpectedExitCode 0

    $symbolsInSpanJson = $symbolsInSpan.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbols-in span start column' -Actual $symbolsInSpanJson.startColumn -Expected 37
    Assert-Equal -Name 'symbols-in span end column' -Actual $symbolsInSpanJson.endColumn -Expected 49
    Assert-Equal -Name 'symbols-in span match count' -Actual @($symbolsInSpanJson.symbols).Count -Expected 1
    Assert-Equal -Name 'symbols-in span match name' -Actual @($symbolsInSpanJson.symbols)[0].name -Expected 'CheckCommand'

    $symbolsInEmpty = Invoke-Navlyn `
        -Name 'symbols-in no symbols' `
        -Arguments @('symbols-in', '--workspace', 'navlyn.slnx', '--file', 'Navlyn.CommandLine/Cli/Commands/CheckCommand.cs', '--line', '3') `
        -ExpectedExitCode 0

    $symbolsInEmptyJson = $symbolsInEmpty.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbols-in no symbols count' -Actual @($symbolsInEmptyJson.symbols).Count -Expected 0

    $outline = Invoke-Navlyn `
        -Name 'outline source file' `
        -Arguments @('outline', '--workspace', 'navlyn.slnx', '--file', 'Navlyn.CommandLine/Cli/Commands/CheckCommand.cs') `
        -ExpectedExitCode 0

    $outlineJson = $outline.Stdout | ConvertFrom-Json
    $outlineCreate = @($outlineJson.entries | Where-Object { $_.name -eq 'Create' -and $_.kind -eq 'Method' })[0]
    Assert-Equal -Name 'outline file' -Actual $outlineJson.file -Expected 'Navlyn.CommandLine/Cli/Commands/CheckCommand.cs'
    Assert-Equal -Name 'outline contains Create method' -Actual $outlineCreate.name -Expected 'Create'
    Assert-Equal -Name 'outline Create facts accessibility' -Actual $outlineCreate.facts.accessibility -Expected 'Public'

    $symbolsInInvalidSpan = Invoke-Navlyn `
        -Name 'symbols-in invalid span' `
        -Arguments @('symbols-in', '--workspace', 'navlyn.slnx', '--file', 'Navlyn.CommandLine/Cli/NavlynCli.cs', '--line', '60', '--start-column', '37', '--end-column', '37') `
        -ExpectedExitCode 2
    Assert-Empty -Name 'symbols-in invalid span stdout' -Text $symbolsInInvalidSpan.Stdout
    Assert-Contains -Name 'symbols-in invalid span stderr' -Text $symbolsInInvalidSpan.Stderr -Expected 'NAVLYN1303:'

    $symbolAt = Invoke-Navlyn `
        -Name 'symbol-at declaration' `
        -Arguments @('symbol-at', '--workspace', 'navlyn.slnx', '--file', 'Navlyn.CommandLine/Cli/Commands/CheckCommand.cs', '--line', '6', '--column', '23') `
        -ExpectedExitCode 0

    $symbolAtJson = $symbolAt.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbol-at file' -Actual $symbolAtJson.file -Expected 'Navlyn.CommandLine/Cli/Commands/CheckCommand.cs'
    Assert-Equal -Name 'symbol-at line' -Actual $symbolAtJson.line -Expected 6
    Assert-Equal -Name 'symbol-at column' -Actual $symbolAtJson.column -Expected 23
    Assert-Equal -Name 'symbol-at name' -Actual $symbolAtJson.symbol.name -Expected 'CheckCommand'
    Assert-Equal -Name 'symbol-at kind' -Actual $symbolAtJson.symbol.kind -Expected 'NamedType'
    Assert-Equal -Name 'symbol-at container' -Actual $symbolAtJson.symbol.container -Expected 'Navlyn.Cli.Commands'
    Assert-Equal -Name 'symbol-at path' -Actual $symbolAtJson.symbol.path -Expected 'Navlyn.CommandLine/Cli/Commands/CheckCommand.cs'
    Assert-Equal -Name 'symbol-at declaration line' -Actual $symbolAtJson.symbol.line -Expected 6
    Assert-Equal -Name 'symbol-at declaration column' -Actual $symbolAtJson.symbol.column -Expected 23
    Assert-Equal -Name 'symbol-at declaration end line' -Actual $symbolAtJson.symbol.endLine -Expected 6
    Assert-Equal -Name 'symbol-at declaration end column' -Actual $symbolAtJson.symbol.endColumn -Expected 35
    Assert-Equal -Name 'symbol-at facts project' -Actual $symbolAtJson.symbol.facts.project -Expected 'Navlyn.CommandLine'

    $symbolInfo = Invoke-Navlyn `
        -Name 'symbol-info invocation' `
        -Arguments @('symbol-info', '--workspace', 'navlyn.slnx', '--file', 'Navlyn.CommandLine/Cli/NavlynCli.cs', '--line', '60', '--column', '37') `
        -ExpectedExitCode 0

    $symbolInfoJson = $symbolInfo.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbol-info symbol name' -Actual $symbolInfoJson.symbol.name -Expected 'CheckCommand'
    Assert-Equal -Name 'symbol-info invocation target' -Actual $symbolInfoJson.invocation.target.displayName -Expected 'Navlyn.Cli.Commands.CheckCommand.Create()'

    $scopeAt = Invoke-Navlyn `
        -Name 'scope-at source position' `
        -Arguments @('scope-at', '--workspace', 'navlyn.slnx', '--file', 'Navlyn.CommandLine/Cli/NavlynCli.cs', '--line', '60', '--column', '37') `
        -ExpectedExitCode 0

    $scopeAtJson = $scopeAt.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'scope-at containing symbol' -Actual $scopeAtJson.containingSymbol.name -Expected 'CreateRootCommand'
    Assert-Equal -Name 'scope-at project context' -Actual $scopeAtJson.projectContext.name -Expected 'Navlyn.CommandLine'
    Assert-Equal -Name 'scope-at innermost scope' -Actual @($scopeAtJson.scopes)[-1].kind -Expected 'Member'

    $symbolSource = Invoke-Navlyn `
        -Name 'symbol-source declaration' `
        -Arguments @('symbol-source', '--workspace', 'navlyn.slnx', '--file', 'Navlyn.CommandLine/Cli/Commands/CheckCommand.cs', '--line', '6', '--column', '23', '--view', 'declaration', '--max-lines', '1') `
        -ExpectedExitCode 0

    $symbolSourceJson = $symbolSource.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbol-source symbol name' -Actual $symbolSourceJson.symbol.name -Expected 'CheckCommand'
    Assert-Equal -Name 'symbol-source view' -Actual $symbolSourceJson.view -Expected 'declaration'
    Assert-Equal -Name 'symbol-source truncated' -Actual @($symbolSourceJson.slices)[0].truncated -Expected $true

    $signature = Invoke-Navlyn `
        -Name 'signature method declaration' `
        -Arguments @('signature', '--workspace', 'navlyn.slnx', '--file', 'Navlyn.CommandLine/Cli/Commands/CheckCommand.cs', '--line', '8', '--column', '27') `
        -ExpectedExitCode 0

    $signatureJson = $signature.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'signature symbol name' -Actual $signatureJson.symbol.name -Expected 'Create'
    Assert-Equal -Name 'signature accessibility' -Actual $signatureJson.apiShape.accessibility -Expected 'Public'

    $typeHierarchy = Invoke-Navlyn `
        -Name 'type-hierarchy non-derived type' `
        -Arguments @('type-hierarchy', '--workspace', 'navlyn.slnx', '--file', 'Navlyn.CommandLine/Cli/Commands/CheckCommand.cs', '--line', '6', '--column', '23') `
        -ExpectedExitCode 0

    $typeHierarchyJson = $typeHierarchy.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'type-hierarchy symbol name' -Actual $typeHierarchyJson.symbol.name -Expected 'CheckCommand'

    $symbolAtInvalidLine = Invoke-Navlyn `
        -Name 'symbol-at invalid line' `
        -Arguments @('symbol-at', '--workspace', 'navlyn.slnx', '--file', 'Navlyn.CommandLine/Cli/Commands/CheckCommand.cs', '--line', '999', '--column', '1') `
        -ExpectedExitCode 2
    Assert-Empty -Name 'symbol-at invalid line stdout' -Text $symbolAtInvalidLine.Stdout
    Assert-Contains -Name 'symbol-at invalid line stderr' -Text $symbolAtInvalidLine.Stderr -Expected 'NAVLYN1303:'

    $symbolAtNoSymbol = Invoke-Navlyn `
        -Name 'symbol-at no symbol' `
        -Arguments @('symbol-at', '--workspace', 'navlyn.slnx', '--file', 'Navlyn.CommandLine/Cli/Commands/CheckCommand.cs', '--line', '3', '--column', '1') `
        -ExpectedExitCode 2
    Assert-Empty -Name 'symbol-at no symbol stdout' -Text $symbolAtNoSymbol.Stdout
    Assert-Contains -Name 'symbol-at no symbol stderr' -Text $symbolAtNoSymbol.Stderr -Expected 'NAVLYN1304:'

    $definition = Invoke-Navlyn `
        -Name 'definition type reference' `
        -Arguments @('definition', '--workspace', 'navlyn.slnx', '--file', 'Navlyn.CommandLine/Cli/NavlynCli.cs', '--line', '60', '--column', '37') `
        -ExpectedExitCode 0

    $definitionJson = $definition.Stdout | ConvertFrom-Json
    $definitionLocation = @($definitionJson.definitions)[0]
    Assert-Equal -Name 'definition file' -Actual $definitionJson.file -Expected 'Navlyn.CommandLine/Cli/NavlynCli.cs'
    Assert-Equal -Name 'definition line' -Actual $definitionJson.line -Expected 60
    Assert-Equal -Name 'definition column' -Actual $definitionJson.column -Expected 37
    Assert-Equal -Name 'definition symbol name' -Actual $definitionJson.symbol.name -Expected 'CheckCommand'
    Assert-Equal -Name 'definition symbol kind' -Actual $definitionJson.symbol.kind -Expected 'NamedType'
    Assert-Equal -Name 'definition symbol container' -Actual $definitionJson.symbol.container -Expected 'Navlyn.Cli.Commands'
    Assert-Equal -Name 'definition count' -Actual @($definitionJson.definitions).Count -Expected 1
    Assert-Equal -Name 'definition path' -Actual $definitionLocation.path -Expected 'Navlyn.CommandLine/Cli/Commands/CheckCommand.cs'
    Assert-Equal -Name 'definition declaration line' -Actual $definitionLocation.line -Expected 6
    Assert-Equal -Name 'definition declaration column' -Actual $definitionLocation.column -Expected 23
    Assert-Equal -Name 'definition declaration end line' -Actual $definitionLocation.endLine -Expected 6
    Assert-Equal -Name 'definition declaration end column' -Actual $definitionLocation.endColumn -Expected 35

    $definitionNoSource = Invoke-Navlyn `
        -Name 'definition no source' `
        -Arguments @('definition', '--workspace', 'navlyn.slnx', '--file', 'Navlyn.CommandLine/Cli/NavlynCli.cs', '--line', '18', '--column', '9') `
        -ExpectedExitCode 2
    Assert-Empty -Name 'definition no source stdout' -Text $definitionNoSource.Stdout
    Assert-Contains -Name 'definition no source stderr' -Text $definitionNoSource.Stderr -Expected 'NAVLYN1305:'

    $definitionMetadata = Invoke-Navlyn `
        -Name 'definition include metadata' `
        -Arguments @('definition', '--workspace', 'navlyn.slnx', '--file', 'Navlyn.CommandLine/Cli/NavlynCli.cs', '--line', '18', '--column', '9', '--include-metadata') `
        -ExpectedExitCode 0
    $definitionMetadataJson = $definitionMetadata.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'definition include metadata flag' -Actual $definitionMetadataJson.includeMetadata -Expected $true
    Assert-Equal -Name 'definition include metadata count' -Actual @($definitionMetadataJson.definitions).Count -Expected 0
    Assert-Equal -Name 'definition include metadata fact' -Actual $definitionMetadataJson.symbol.facts.isMetadata -Expected $true

    $references = Invoke-Navlyn `
        -Name 'references type reference' `
        -Arguments @('references', '--workspace', 'navlyn.slnx', '--file', 'Navlyn.CommandLine/Cli/NavlynCli.cs', '--line', '60', '--column', '37') `
        -ExpectedExitCode 0

    $referencesJson = $references.Stdout | ConvertFrom-Json
    $referenceLocation = @($referencesJson.references)[0]
    Assert-Equal -Name 'references file' -Actual $referencesJson.file -Expected 'Navlyn.CommandLine/Cli/NavlynCli.cs'
    Assert-Equal -Name 'references line' -Actual $referencesJson.line -Expected 60
    Assert-Equal -Name 'references column' -Actual $referencesJson.column -Expected 37
    Assert-Equal -Name 'references symbol name' -Actual $referencesJson.symbol.name -Expected 'CheckCommand'
    Assert-Equal -Name 'references symbol kind' -Actual $referencesJson.symbol.kind -Expected 'NamedType'
    Assert-Equal -Name 'references symbol container' -Actual $referencesJson.symbol.container -Expected 'Navlyn.Cli.Commands'
    Assert-Equal -Name 'references count' -Actual @($referencesJson.references).Count -Expected 1
    Assert-Equal -Name 'references path' -Actual $referenceLocation.path -Expected 'Navlyn.CommandLine/Cli/NavlynCli.cs'
    Assert-Equal -Name 'references reference line' -Actual $referenceLocation.line -Expected 60
    Assert-Equal -Name 'references reference column' -Actual $referenceLocation.column -Expected 37
    Assert-Equal -Name 'references reference end line' -Actual $referenceLocation.endLine -Expected 60
    Assert-Equal -Name 'references reference end column' -Actual $referenceLocation.endColumn -Expected 49
    Assert-Equal -Name 'references containing symbol name' -Actual $referenceLocation.containingSymbol.name -Expected 'CreateRootCommand'
    Assert-Equal -Name 'references containing symbol kind' -Actual $referenceLocation.containingSymbol.kind -Expected 'Method'

    $find = Invoke-Navlyn `
        -Name 'find fuzzy unique type' `
        -Arguments @('find', '--workspace', 'navlyn.slnx', '--query', 'CheckCommand', '--assume-kind', 'NamedType', '--project', 'Navlyn.CommandLine') `
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
        -Arguments @('find', '--workspace', 'navlyn.slnx', '--query', 'CheckCommand', '--assume-kind', 'NamedType', '--project', 'Navlyn.CommandLine', '--explain-selection') `
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
        -Arguments @('where-used', '--workspace', 'navlyn.slnx', '--query', 'CheckCommand', '--assume-kind', 'NamedType', '--project', 'Navlyn.CommandLine', '--limit', '1', '--include-snippets', '--snippet-lines', '0') `
        -ExpectedExitCode 0

    $whereUsedJson = $whereUsed.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'where-used fuzzy confidence' -Actual $whereUsedJson.confidence -Expected 'high'
    Assert-Equal -Name 'where-used fuzzy total matches' -Actual $whereUsedJson.totalMatches -Expected 1
    Assert-Equal -Name 'where-used fuzzy reference end column' -Actual @($whereUsedJson.references)[0].endColumn -Expected 49
    Assert-Equal -Name 'where-used fuzzy snippet line' -Actual @(@($whereUsedJson.references)[0].snippet.lines)[0] -Expected '        rootCommand.Subcommands.Add(CheckCommand.Create());'

    $about = Invoke-Navlyn `
        -Name 'about fuzzy summary' `
        -Arguments @('about', '--workspace', 'navlyn.slnx', '--query', 'CheckCommand', '--assume-kind', 'NamedType', '--project', 'Navlyn.CommandLine', '--member-limit', '2', '--reference-limit', '1') `
        -ExpectedExitCode 0

    $aboutJson = $about.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'about fuzzy selected' -Actual $aboutJson.selectedCandidate.name -Expected 'CheckCommand'
    Assert-Equal -Name 'about fuzzy members returned' -Actual @($aboutJson.members.members).Count -Expected 2

    $related = Invoke-Navlyn `
        -Name 'related fuzzy files' `
        -Arguments @('related', '--workspace', 'navlyn.slnx', '--query', 'CheckCommand', '--assume-kind', 'NamedType', '--project', 'Navlyn.CommandLine', '--limit', '2') `
        -ExpectedExitCode 0

    $relatedJson = $related.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'related fuzzy total files' -Actual $relatedJson.totalFiles -Expected 2
    Assert-Equal -Name 'related fuzzy first reason' -Actual @(@($relatedJson.files)[0].reasons)[0] -Expected 'declares-selected-symbol'

    $impact = Invoke-Navlyn `
        -Name 'impact fuzzy files' `
        -Arguments @('impact', '--workspace', 'navlyn.slnx', '--query', 'CheckCommand', '--assume-kind', 'NamedType', '--project', 'Navlyn.CommandLine', '--limit', '2') `
        -ExpectedExitCode 0

    $impactJson = $impact.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'impact fuzzy total files' -Actual $impactJson.totalFiles -Expected 1
    Assert-Equal -Name 'impact fuzzy level' -Actual @($impactJson.files)[0].impactLevel -Expected 'direct'

    $entrypoints = Invoke-Navlyn `
        -Name 'entrypoints fuzzy no chains for type' `
        -Arguments @('entrypoints', '--workspace', 'navlyn.slnx', '--query', 'CheckCommand', '--assume-kind', 'NamedType', '--project', 'Navlyn.CommandLine', '--limit', '2') `
        -ExpectedExitCode 0

    $entrypointsJson = $entrypoints.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'entrypoints fuzzy total chains' -Actual $entrypointsJson.totalChains -Expected 0

    $implementations = Invoke-Navlyn `
        -Name 'implementations non-applicable symbol' `
        -Arguments @('implementations', '--workspace', 'navlyn.slnx', '--file', 'Navlyn.CommandLine/Cli/Commands/CheckCommand.cs', '--line', '6', '--column', '23') `
        -ExpectedExitCode 0

    $implementationsJson = $implementations.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'implementations file' -Actual $implementationsJson.file -Expected 'Navlyn.CommandLine/Cli/Commands/CheckCommand.cs'
    Assert-Equal -Name 'implementations line' -Actual $implementationsJson.line -Expected 6
    Assert-Equal -Name 'implementations column' -Actual $implementationsJson.column -Expected 23
    Assert-Equal -Name 'implementations symbol name' -Actual $implementationsJson.symbol.name -Expected 'CheckCommand'
    Assert-Equal -Name 'implementations symbol kind' -Actual $implementationsJson.symbol.kind -Expected 'NamedType'
    Assert-Equal -Name 'implementations count' -Actual @($implementationsJson.implementations).Count -Expected 0

    $callers = Invoke-Navlyn `
        -Name 'callers method declaration' `
        -Arguments @('callers', '--workspace', 'navlyn.slnx', '--file', 'Navlyn.CommandLine/Cli/Commands/CheckCommand.cs', '--line', '8', '--column', '27') `
        -ExpectedExitCode 0

    $callersJson = $callers.Stdout | ConvertFrom-Json
    $callerGroup = @($callersJson.callers | Where-Object { $_.symbol.name -eq 'CreateRootCommand' })[0]
    Assert-Equal -Name 'callers file' -Actual $callersJson.file -Expected 'Navlyn.CommandLine/Cli/Commands/CheckCommand.cs'
    Assert-Equal -Name 'callers symbol name' -Actual $callersJson.symbol.name -Expected 'Create'
    Assert-Equal -Name 'callers symbol kind' -Actual $callersJson.symbol.kind -Expected 'Method'
    Assert-Equal -Name 'callers contains CreateRootCommand' -Actual $callerGroup.symbol.name -Expected 'CreateRootCommand'
    Assert-Equal -Name 'callers location line' -Actual @($callerGroup.locations)[0].line -Expected 60
    Assert-Equal -Name 'callers location has span' -Actual (@($callerGroup.locations)[0].endColumn -gt @($callerGroup.locations)[0].column) -Expected $true

    $calls = Invoke-Navlyn `
        -Name 'calls containing member' `
        -Arguments @('calls', '--workspace', 'navlyn.slnx', '--file', 'Navlyn.CommandLine/Cli/NavlynCli.cs', '--line', '60', '--column', '37') `
        -ExpectedExitCode 0

    $callsJson = $calls.Stdout | ConvertFrom-Json
    $checkCreateCall = @($callsJson.calls | Where-Object { $_.symbol.container -eq 'Navlyn.Cli.Commands.CheckCommand' -and $_.symbol.name -eq 'Create' })[0]
    Assert-Equal -Name 'calls file' -Actual $callsJson.file -Expected 'Navlyn.CommandLine/Cli/NavlynCli.cs'
    Assert-Equal -Name 'calls caller name' -Actual $callsJson.caller.name -Expected 'CreateRootCommand'
    Assert-Equal -Name 'calls contains CheckCommand.Create' -Actual $checkCreateCall.symbol.name -Expected 'Create'
    Assert-Equal -Name 'calls CheckCommand.Create location line' -Actual @($checkCreateCall.locations)[0].line -Expected 60
    Assert-Equal -Name 'calls CheckCommand.Create location has span' -Actual (@($checkCreateCall.locations)[0].endColumn -gt @($checkCreateCall.locations)[0].column) -Expected $true

    $callsMetadata = Invoke-Navlyn `
        -Name 'calls include metadata' `
        -Arguments @('calls', '--workspace', 'navlyn.slnx', '--file', 'Navlyn.CommandLine/Cli/NavlynCli.cs', '--line', '60', '--column', '37', '--include-metadata', '--result-kind', 'Method', '--limit', '1') `
        -ExpectedExitCode 0
    $callsMetadataJson = $callsMetadata.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'calls include metadata flag' -Actual $callsMetadataJson.includeMetadata -Expected $true
    Assert-Equal -Name 'calls include metadata limit' -Actual $callsMetadataJson.limit -Expected 1
    }
    }

    if ($Suite -eq 'all') { Invoke-CoreErrorChecks }

    Write-Host "CLI contract $Suite checks passed."
}
finally {
    Pop-Location
}

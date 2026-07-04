[CmdletBinding()]
param(
    [switch]$NoBuild,
    [switch]$SkipDotnetTest,
    [switch]$ShowOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$HarnessPath = Join-Path $PSScriptRoot 'lib/navlyn-test-harness.ps1'
$FormatCheckScript = Join-Path $PSScriptRoot 'test-csharp-file-format.ps1'

. $HarnessPath
Initialize-NavlynTestHarness -RepoRoot $RepoRoot -ShowOutput:$ShowOutput

Push-Location $RepoRoot
try {
    & $FormatCheckScript -Quiet

    if (!$NoBuild) {
        Write-Host 'Building navlyn...'
        Invoke-CheckedProcess `
            -Name 'dotnet build' `
            -FilePath 'dotnet' `
            -Arguments @('build', $script:NavlynTestSolutionPath) `
            -ExpectedExitCode 0 | Out-Null
    }

    Assert-NavlynDllExists

    if (!$SkipDotnetTest) {
        Write-Host 'Running xUnit tests...'
        Invoke-CheckedProcess `
            -Name 'dotnet test' `
            -FilePath 'dotnet' `
            -Arguments @('test', $script:NavlynTestSolutionPath, '--no-build') `
            -ExpectedExitCode 0 | Out-Null
    }
    else {
        Write-Host 'Skipping xUnit tests because -SkipDotnetTest was specified.'
    }

    Write-Host 'Running quick CLI checks...'

    $validCheck = Invoke-Navlyn `
        -Name 'check valid workspace' `
        -Arguments @('check', '--workspace', 'navlyn.slnx') `
        -ExpectedExitCode 0

    Assert-Empty -Name 'check valid workspace stderr' -Text $validCheck.Stderr
    $validJson = $validCheck.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'check valid workspace ok' -Actual $validJson.ok -Expected $true
    Assert-Equal -Name 'check valid workspace workspace' -Actual $validJson.workspace -Expected 'navlyn.slnx'
    Assert-Equal -Name 'check valid workspace kind' -Actual $validJson.kind -Expected 'solution'

    $overview = Invoke-Navlyn `
        -Name 'overview valid workspace' `
        -Arguments @('overview', '--workspace', 'navlyn.slnx') `
        -ExpectedExitCode 0

    Assert-Empty -Name 'overview valid workspace stderr' -Text $overview.Stderr
    $overviewJson = $overview.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'overview workspace' -Actual $overviewJson.workspace -Expected 'navlyn.slnx'
    Assert-Equal -Name 'overview kind' -Actual $overviewJson.kind -Expected 'solution'
    Assert-Equal -Name 'overview project count' -Actual @($overviewJson.projects).Count -Expected 10
    $overviewProject = @($overviewJson.projects | Where-Object { $_.path -eq 'navlyn/navlyn.csproj' -and $_.targetFramework -eq 'net10.0' })[0]
    Assert-Equal -Name 'overview project name' -Actual $overviewProject.name -Expected 'navlyn(net10.0)'

    $symbolAt = Invoke-Navlyn `
        -Name 'symbol-at declaration' `
        -Arguments @('symbol-at', '--workspace', 'navlyn.slnx', '--file', 'Navlyn.CommandLine/Cli/Commands/CheckCommand.cs', '--line', '6', '--column', '23') `
        -ExpectedExitCode 0

    Assert-Empty -Name 'symbol-at declaration stderr' -Text $symbolAt.Stderr
    $symbolAtJson = $symbolAt.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbol-at file' -Actual $symbolAtJson.file -Expected 'Navlyn.CommandLine/Cli/Commands/CheckCommand.cs'
    Assert-Equal -Name 'symbol-at name' -Actual $symbolAtJson.symbol.name -Expected 'CheckCommand'
    Assert-Equal -Name 'symbol-at kind' -Actual $symbolAtJson.symbol.kind -Expected 'NamedType'

    $missingOption = Invoke-Navlyn `
        -Name 'check missing workspace option' `
        -Arguments @('check') `
        -ExpectedExitCode 2

    Assert-Empty -Name 'missing workspace stdout' -Text $missingOption.Stdout
    Assert-Contains -Name 'missing workspace stderr' -Text $missingOption.Stderr -Expected 'NAVLYN1001:'

    Write-Host 'Quick checks passed.'
}
finally {
    Pop-Location
}

[CmdletBinding()]
param([switch]$NoBuild)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/navlyn-test-harness.ps1')
$repo = Split-Path -Parent $PSScriptRoot
Initialize-NavlynTestHarness -RepoRoot $repo
if (!$NoBuild) { Invoke-CheckedProcess -Name build -FilePath dotnet -Arguments @('build', 'navlyn.slnx') -ExpectedExitCode 0 | Out-Null }
$workspace = 'tests/fixtures/FuzzyDiscoveryFixture/FuzzyDiscoveryFixture.csproj'
function Json($Arguments, [string]$InputText = $null) {
    $result = Invoke-Navlyn -Name ($Arguments -join ' ') -Arguments $Arguments -StandardInput $InputText -ExpectedExitCode 0
    if ($result.Stderr.Length) { throw "Unexpected stderr: $($result.Stderr)" }
    $result.Stdout | ConvertFrom-Json -Depth 100
}
foreach ($option in @('--kind', '--assume-kind')) {
    $command = if ($option -eq '--kind') { 'symbols' } else { 'target' }
    $json = Json @($command,'--workspace',$workspace,'--query','EnemyManagerTools',$option,' interface ','CLASS','record','NamedType')
    if ($command -eq 'symbols') {
        if (@($json.kinds).Count -ne 1 -or $json.kinds[0] -cne 'NamedType' -or $json.totalMatches -ne 1) { throw 'CLI kind aliases were not canonical/deduplicated.' }
    } elseif ($json.selectedTarget.kind -cne 'NamedType') { throw 'CLI type alias failed to select a class.' }
    foreach ($invalid in @('1','not-a-kind')) {
        $failure = Invoke-Navlyn -Name 'invalid kind' -Arguments @($command,'--workspace',$workspace,'--query','EnemyManagerTools',$option,$invalid) -ExpectedExitCode 2
        if ($failure.Stdout.Length -or !$failure.Stderr.Contains('NAVLYN1004:')) { throw 'Invalid CLI kind did not fail cleanly.' }
    }
}
$calls = Json @('calls','--workspace',$workspace,'--file','tests/fixtures/FuzzyDiscoveryFixture/FixtureCode.cs','--line','27','--column','9','--result-kind','METHOD',' method ')
if (@($calls.resultKinds).Count -ne 1 -or $calls.resultKinds[0] -cne 'Method') { throw 'CLI result-kind parity failed.' }
$batchInput = @{ requests = @(
    @{ id='symbols'; command='symbols'; query='EnemyManagerTools'; kinds=@('interface',' CLASS ') },
    @{ id='target'; command='resolve-target'; query='EnemyManagerTools'; assumeKinds=@('record','class') },
    @{ id='calls'; command='calls'; file='tests/fixtures/FuzzyDiscoveryFixture/FixtureCode.cs'; line=27; column=9; resultKinds=@('METHOD',' method ') }
) } | ConvertTo-Json -Depth 10
$batch = Json @('batch','--workspace',$workspace) $batchInput
if ($batch.succeededRequests -ne 3 -or $batch.results[0].result.kinds[0] -cne 'NamedType') { throw 'CLI batch kind parity failed.' }
foreach ($arguments in @(
    ,@('context-pack','--workspace',$workspace,'--query','EnemyManagerTools','--assume-kind','class','--project','FuzzyDiscoveryFixture','--item-limit','2','--budget-tokens','1800','--profile','compact','--workspace-root-policy','repo-relative')
    ,@('review-pack','--workspace',$workspace,'--base','HEAD','--head','HEAD','--pack','nullability','--project','FuzzyDiscoveryFixture','--finding-limit','2','--profile','compact','--workspace-root-policy','repo-relative')
)) {
    $first = Json $arguments
    $repro = $first.reproCommand
    if (($repro.arguments -join "`0") -cne ($arguments -join "`0") -or $repro.workingDirectory -cne $repo) { throw 'Reproduction lost arguments or working directory.' }
    $again = Invoke-Navlyn -Name 'replay' -Arguments $repro.arguments -WorkingDirectory $repro.workingDirectory -ExpectedExitCode 0
    $replayed = $again.Stdout | ConvertFrom-Json -Depth 100
    if (($first | ConvertTo-Json -Depth 100 -Compress) -cne ($replayed | ConvertTo-Json -Depth 100 -Compress)) { throw 'Reproduction changed the deterministic result.' }
}
$inputText = @{ requests = @(
    @{ id='context'; command='context-pack'; query='EnemyManagerTools'; assumeKind='interface'; profile='compact'; budgetTokens=1800; itemLimit=2 },
    @{ id='symbols'; command='symbols'; query='EnemyManagerTools'; kinds=@('class') }
) } | ConvertTo-Json -Depth 10
$first = Json @('batch','--workspace',$workspace) $inputText
$repro = $first.results[0].result.reproCommand
if ($repro.standardInput -cne $inputText -or $repro.arguments[0] -cne 'batch') { throw 'Batch reproduction lost stdin or outer invocation.' }
$again = Json $repro.arguments $repro.standardInput
if (($first | ConvertTo-Json -Depth 100 -Compress) -cne ($again | ConvertTo-Json -Depth 100 -Compress)) { throw 'Batch stdin replay changed results.' }
Write-Host 'CLI/batch natural kind parity and deterministic reproduction replay passed.'

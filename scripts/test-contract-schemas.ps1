[CmdletBinding()]
param(
    [switch]$NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$SolutionPath = Join-Path $RepoRoot 'navlyn.slnx'

Push-Location $RepoRoot
try {
    if (!$NoBuild) {
        dotnet build $SolutionPath
        if ($LASTEXITCODE -ne 0) { throw "Schema validation build failed with exit code $LASTEXITCODE." }
    }

    dotnet test $SolutionPath --no-build --filter 'Schema|Golden|Contract'
    if ($LASTEXITCODE -ne 0) { throw "Schema validation tests failed with exit code $LASTEXITCODE." }
}
finally {
    Pop-Location
}

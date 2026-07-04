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
    }

    dotnet test $SolutionPath --no-build --filter 'Schema|Golden|Contract'
}
finally {
    Pop-Location
}

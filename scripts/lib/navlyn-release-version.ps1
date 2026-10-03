function Get-NavlynReleaseVersion {
    param([string]$RepositoryRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)))
    [xml]$props = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'Directory.Build.props') -Raw
    $version = [string]$props.Project.PropertyGroup.Version
    if ($version -cnotmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') { throw 'Directory.Build.props must declare a supported release Version.' }
    return $version
}

function Get-NavlynNumericVersion {
    param([string]$Version = (Get-NavlynReleaseVersion))
    return ($Version -split '-', 2)[0] + '.0'
}

function Get-NavlynNextPatchVersion {
    param([string]$Version = (Get-NavlynReleaseVersion))
    $numeric = [version](($Version -split '-', 2)[0])
    return "$($numeric.Major).$($numeric.Minor).$($numeric.Build + 1)"
}

function Get-NavlynCurrentReleasePaths {
    return @(
        'README.md', 'README_ja.md', 'docs/navlyn-client-setup.md', 'docs/navlyn-client-setup_ja.md',
        'docs/navlyn-distribution.md', 'docs/navlyn-first-10-minutes.md', 'docs/navlyn-first-10-minutes_ja.md',
        'docs/navlyn-first-15-minutes.md', 'docs/navlyn-mcp-server.md', 'docs/navlyn-release-contract.md', 'docs/navlyn-publication-recovery.md',
        'examples/agents/copilot-instructions.md', 'examples/install/dotnet-tools.json', 'scripts/setup-bundle/README.md'
    )
}

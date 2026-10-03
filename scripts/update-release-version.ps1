[CmdletBinding()]
param([Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')][string]$Version)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/navlyn-release-version.ps1')
$repo = Split-Path -Parent $PSScriptRoot
$previous = Get-NavlynReleaseVersion
if ($Version -ceq $previous) {
    Write-Output "Release identity is already $Version."
    return
}
$paths = @(
    'README.md', 'README_ja.md', 'docs/navlyn-client-setup.md', 'docs/navlyn-client-setup_ja.md',
    'docs/navlyn-distribution.md', 'docs/navlyn-first-10-minutes.md', 'docs/navlyn-first-10-minutes_ja.md',
    'docs/navlyn-first-15-minutes.md', 'docs/navlyn-mcp-server.md', 'docs/navlyn-release-contract.md', 'docs/navlyn-publication-recovery.md',
    'examples/agents/copilot-instructions.md', 'examples/install/dotnet-tools.json', 'scripts/setup-bundle/README.md'
)
foreach ($relative in $paths) {
    $path = Join-Path $repo $relative
    $text = [IO.File]::ReadAllText($path)
    if ($relative -eq 'docs/navlyn-distribution.md') {
        $text = [regex]::Replace($text, '`[0-9A-Za-z.-]+` is the previous public release', "``$previous`` is the previous public release")
    }
    $text = $text.Replace($previous, $Version)
    # The previous release is history, not another current-version example.
    if ($relative -eq 'docs/navlyn-distribution.md') {
        $text = [regex]::Replace($text, '`[0-9A-Za-z.-]+` is the previous public release', "``$previous`` is the previous public release")
    }
    [IO.File]::WriteAllText($path, $text, [Text.UTF8Encoding]::new($false))
}
$propsPath = Join-Path $repo 'Directory.Build.props'
$propsText = [IO.File]::ReadAllText($propsPath)
$propsText = [regex]::Replace($propsText, '<Version>[^<]+</Version>', "<Version>$Version</Version>")
[IO.File]::WriteAllText($propsPath, $propsText, [Text.UTF8Encoding]::new($false))
Write-Output "Release identity updated: $previous -> $Version. Scripts derive identity from Directory.Build.props; historical changelog entries are preserved."

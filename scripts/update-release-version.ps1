[CmdletBinding()]
param([Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')][string]$Version,
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/navlyn-release-version.ps1')
$repo = [IO.Path]::GetFullPath($RepositoryRoot)
$previous = Get-NavlynReleaseVersion $repo
if ($Version -ceq $previous) {
    Write-Output "Release identity is already $Version."
    return
}
$paths = Get-NavlynCurrentReleasePaths
$updates = @{}
foreach ($relative in $paths) {
    $path = Join-Path $repo $relative
    $text = [IO.File]::ReadAllText($path)
    $history = @{ level = 0 }
    $text = [regex]::Replace($text, '(?m)^.*$', {
        param($match)
        $line = $match.Value
        if ($line -match '^(#{1,6})\s+(.+)$') {
            $level = $Matches[1].Length
            if ($history.level -gt 0 -and $level -le $history.level) { $history.level = 0 }
            if ($Matches[2] -match '(?i)\b(history|historical|recorded|observed)\b') { $history.level = $level }
        }
        if ($history.level -gt 0 -or $line -match '\b\d{4}-\d{2}-\d{2}\b') { return $line }
        if ($line -match '`[0-9A-Za-z.-]+` is the previous public release') {
            $line = $line.Replace('`' + $previous + '`', '`' + $Version + '`')
            if (!$previous.Contains('-')) {
                $line = [regex]::Replace($line, '`[0-9A-Za-z.-]+` is the previous public release', "``$previous`` is the previous public release")
            }
            return $line
        }
        return $line.Replace($previous, $Version)
    })
    $updates[$path] = $text
}
$propsPath = Join-Path $repo 'Directory.Build.props'
$propsText = [IO.File]::ReadAllText($propsPath)
$propsText = [regex]::Replace($propsText, '<Version>[^<]+</Version>', "<Version>$Version</Version>")
$updates[$propsPath] = $propsText
foreach ($path in $updates.Keys) { [IO.File]::WriteAllText($path, $updates[$path], [Text.UTF8Encoding]::new($false)) }
Write-Output "Release identity updated: $previous -> $Version. Scripts derive identity from Directory.Build.props; historical changelog entries are preserved."

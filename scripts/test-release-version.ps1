$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/navlyn-release-version.ps1')
$root = Join-Path ([IO.Path]::GetTempPath()) ('navlyn-version-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root) | Out-Null
try {
    foreach ($inputVersion in @('0.8.5','0.9.0-preview.1')) {
        [IO.File]::WriteAllText((Join-Path $root 'Directory.Build.props'), "<Project><PropertyGroup><Version>$inputVersion</Version></PropertyGroup></Project>")
        if ((Get-NavlynReleaseVersion $root) -cne $inputVersion) { throw 'Version was not read from the supplied props.' }
    }
    if ((Get-NavlynNumericVersion '0.9.0-preview.1') -ne '0.9.0.0' -or (Get-NavlynNextPatchVersion '0.8.5') -ne '0.8.6') { throw 'Derived fixture or assembly version is incorrect.' }
    [IO.File]::WriteAllText((Join-Path $root 'Directory.Build.props'), '<Project><PropertyGroup><Version>invalid</Version></PropertyGroup></Project>')
    $rejected = $false
    try { Get-NavlynReleaseVersion $root | Out-Null } catch { $rejected = $true }
    if (!$rejected) { throw 'Invalid release identity was accepted.' }
    $paths = Get-NavlynCurrentReleasePaths
    foreach ($relative in $paths) {
        $path = Join-Path $root $relative
        [IO.Directory]::CreateDirectory((Split-Path -Parent $path)) | Out-Null
        $content = if ($relative.EndsWith('.json')) { '{"version":"0.8.5"}' }
            elseif ($relative -eq 'docs/navlyn-distribution.md') { 'The current package identity is `0.8.5`; `0.8.4` is the previous public release.' }
            else { "Install 0.8.5`n`n## Historical evidence`nReleased 0.8.5`n`n## Current installation`nInstall 0.8.5`n" }
        [IO.File]::WriteAllText($path, $content)
    }
    $history = "## 0.8.5`nHistorical changelog`n"
    [IO.File]::WriteAllText((Join-Path $root 'CHANGELOG.md'), $history)
    [IO.File]::WriteAllText((Join-Path $root 'Directory.Build.props'), '<Project><PropertyGroup><Version>0.8.5</Version></PropertyGroup></Project>')
    $updater = Join-Path $PSScriptRoot 'update-release-version.ps1'
    & $updater -Version '0.8.6' -RepositoryRoot $root | Out-Null
    if ((Get-NavlynReleaseVersion $root) -cne '0.8.6') { throw 'Patch updater failed.' }
    $readme = [IO.File]::ReadAllText((Join-Path $root 'README.md'))
    if ($readme -notmatch 'Install 0.8.6' -or $readme -notmatch 'Released 0.8.5' -or $readme.EndsWith('Install 0.8.5' + "`n")) { throw 'Current examples or history were rewritten incorrectly.' }
    if ([IO.File]::ReadAllText((Join-Path $root 'CHANGELOG.md')) -cne $history) { throw 'Historical changelog changed.' }
    $before = @{}; foreach ($relative in @($paths) + @('Directory.Build.props')) { $before[$relative] = [IO.File]::ReadAllText((Join-Path $root $relative)) }
    & $updater -Version '0.8.6' -RepositoryRoot $root | Out-Null
    foreach ($relative in $before.Keys) { if ([IO.File]::ReadAllText((Join-Path $root $relative)) -cne $before[$relative]) { throw 'Updater is not idempotent.' } }
    & $updater -Version '0.9.0-preview.1' -RepositoryRoot $root | Out-Null
    & $updater -Version '0.9.0' -RepositoryRoot $root | Out-Null
    $distribution = [IO.File]::ReadAllText((Join-Path $root 'docs/navlyn-distribution.md'))
    if ($distribution -notmatch 'current package identity is `0.9.0`' -or $distribution -notmatch '`0.8.6` is the previous public release') { throw 'Preview promotion lost the previous public release.' }
    foreach ($relative in $paths) {
        if ($relative.EndsWith('.json') -and (([IO.File]::ReadAllText((Join-Path $root $relative)) | ConvertFrom-Json).version -cne '0.9.0')) { throw 'Tool manifest update failed.' }
    }
    Write-Output 'Release version derivation passed.'
} finally {
    $verifiedRoot = [IO.Path]::GetFullPath($root)
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    if (!$verifiedRoot.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $verifiedRoot) -notlike 'navlyn-version-*') { throw 'Unexpected updater fixture cleanup root.' }
    Remove-Item -LiteralPath $verifiedRoot -Recurse
}

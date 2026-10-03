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
    Write-Output 'Release version derivation passed.'
} finally {
    Remove-Item -LiteralPath (Join-Path $root 'Directory.Build.props')
    Remove-Item -LiteralPath $root
}

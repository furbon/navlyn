[CmdletBinding()]
param([Parameter(Mandatory)][string]$Output, [Parameter(Mandatory)][string]$SourceCommit)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($SourceCommit -cnotmatch '^[0-9a-f]{40}$') { throw 'SourceCommit must be the exact lowercase Git commit SHA.' }
$root = [IO.Path]::GetFullPath($Output)
$zip = "$root.zip"
for ($ancestor = $root; $ancestor; $ancestor = Split-Path -Parent $ancestor) {
    if (Test-Path -LiteralPath $ancestor) {
        $item = Get-Item -LiteralPath $ancestor -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Bundle output must not traverse a reparse point.' }
    }
    if ($ancestor -eq [IO.Path]::GetPathRoot($ancestor)) { break }
}
if (Test-Path -LiteralPath $zip) { throw 'Bundle archive already exists; choose a fresh output path.' }
if (Test-Path -LiteralPath $root) {
    if (!(Test-Path -LiteralPath $root -PathType Container) -or @(Get-ChildItem -LiteralPath $root -Force).Count -ne 0) {
        throw 'Bundle output must be a new or empty directory.'
    }
}
[IO.Directory]::CreateDirectory($root) | Out-Null
$files = @('setup-navlyn.ps1','lib/navlyn-jsonc.ps1','README.md')
foreach ($relative in $files) {
    $source = if ($relative -eq 'README.md') { Join-Path $PSScriptRoot 'setup-bundle/README.md' } else { Join-Path $PSScriptRoot $relative }
    $target = Join-Path $root $relative
    [IO.Directory]::CreateDirectory((Split-Path -Parent $target)) | Out-Null
    Copy-Item -LiteralPath $source -Destination $target -Force
}
$entries = foreach ($relative in $files) {
    [ordered]@{ path=$relative.Replace('\','/'); sha256=(Get-FileHash -LiteralPath (Join-Path $root $relative) -Algorithm SHA256).Hash.ToLowerInvariant() }
}
$manifest = [ordered]@{ schema='navlyn.setup-bundle.v1'; sourceCommit=$SourceCommit; files=@($entries) }
$manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $root 'integrity.json') -Encoding utf8
Compress-Archive -Path (Join-Path $root '*') -DestinationPath $zip -CompressionLevel Optimal
Write-Output ([ordered]@{ bundle=$zip; manifest=(Join-Path $root 'integrity.json'); sourceCommit=$SourceCommit } | ConvertTo-Json -Compress)

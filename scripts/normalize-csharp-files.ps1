[CmdletBinding()]
param(
    [switch]$Quiet
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Utf8Strict = New-Object System.Text.UTF8Encoding($false, $true)
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$Utf8BomBytes = [byte[]](0xEF, 0xBB, 0xBF)

try {
    $FallbackEncoding = [System.Text.Encoding]::GetEncoding(932)
}
catch {
    $FallbackEncoding = [System.Text.Encoding]::Default
}

function ConvertFrom-Bytes {
    param(
        [Parameter(Mandatory = $true)]
        [byte[]]$Bytes
    )

    if ($Bytes.Length -ge 3 -and $Bytes[0] -eq 0xEF -and $Bytes[1] -eq 0xBB -and $Bytes[2] -eq 0xBF) {
        return $Utf8Strict.GetString($Bytes, 3, $Bytes.Length - 3)
    }

    try {
        return $Utf8Strict.GetString($Bytes)
    }
    catch [System.Text.DecoderFallbackException] {
        return $FallbackEncoding.GetString($Bytes)
    }
}

function Test-IsBuildOutputPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $relativePath = [System.IO.Path]::GetRelativePath($RepoRoot, $Path)
    $segments = $relativePath -split '[\\/]'
    return $segments -contains 'bin' -or $segments -contains 'obj'
}

$Files = Get-ChildItem -LiteralPath $RepoRoot -Recurse -Filter '*.cs' -File |
    Where-Object {
        !(Test-IsBuildOutputPath -Path $_.FullName)
    }

foreach ($File in $Files) {
    $Bytes = [System.IO.File]::ReadAllBytes($File.FullName)
    $Text = ConvertFrom-Bytes -Bytes $Bytes
    $Text = $Text -replace "`r`n|`n|`r", "`r`n"

    if (!$Text.EndsWith("`r`n")) {
        $Text += "`r`n"
    }

    $TextBytes = $Utf8NoBom.GetBytes($Text)
    $NormalizedBytes = New-Object byte[] ($Utf8BomBytes.Length + $TextBytes.Length)
    [System.Buffer]::BlockCopy($Utf8BomBytes, 0, $NormalizedBytes, 0, $Utf8BomBytes.Length)
    [System.Buffer]::BlockCopy($TextBytes, 0, $NormalizedBytes, $Utf8BomBytes.Length, $TextBytes.Length)
    $IsAlreadyNormalized =
        $Bytes.Length -eq $NormalizedBytes.Length -and
        [System.Linq.Enumerable]::SequenceEqual($Bytes, $NormalizedBytes)

    if (!$IsAlreadyNormalized) {
        [System.IO.File]::WriteAllBytes($File.FullName, $NormalizedBytes)

        if (!$Quiet) {
            Write-Host "normalized: $($File.FullName.Substring($RepoRoot.Length + 1))"
        }
    }
}

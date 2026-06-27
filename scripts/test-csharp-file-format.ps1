[CmdletBinding()]
param(
    [switch]$Quiet
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot

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

$InvalidFiles = @()

foreach ($File in $Files) {
    $Bytes = [System.IO.File]::ReadAllBytes($File.FullName)
    $HasBom = $Bytes.Length -ge 3 -and $Bytes[0] -eq 0xEF -and $Bytes[1] -eq 0xBB -and $Bytes[2] -eq 0xBF

    if ($HasBom) {
        $Text = [System.Text.Encoding]::UTF8.GetString($Bytes, 3, $Bytes.Length - 3)
    }
    else {
        $Text = [System.Text.Encoding]::UTF8.GetString($Bytes)
    }

    $HasBareLf = $Text -match "(?<!`r)`n"
    $HasBareCr = $Text -match "`r(?!`n)"
    $HasIndentTab = $Text -match '(?m)^\t+'

    if (!$HasBom -or $HasBareLf -or $HasBareCr -or $HasIndentTab) {
        $InvalidFiles += [pscustomobject]@{
            Path = $File.FullName.Substring($RepoRoot.Length + 1)
            Bom = $HasBom
            CrlfOnly = (!$HasBareLf -and !$HasBareCr)
            NoIndentTabs = (!$HasIndentTab)
        }
    }
}

if ($InvalidFiles.Count -gt 0) {
    $InvalidFiles | Format-Table -AutoSize
    throw 'C# file format check failed. Expected UTF-8 BOM, CRLF line endings, and spaces for indentation.'
}

if (!$Quiet) {
    Write-Host "C# file format check passed. Count=$($Files.Count)"
}

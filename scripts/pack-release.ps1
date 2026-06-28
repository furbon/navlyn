[CmdletBinding()]
param(
    [string]$Output = 'artifacts/packages',
    [switch]$NoValidation
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$OutputPath = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $Output))

function Get-ProjectVersion {
    param([string]$ProjectPath)

    [xml]$projectXml = Get-Content -Raw -LiteralPath $ProjectPath
    return [string]$projectXml.Project.PropertyGroup.Version
}

function Get-FileSha256 {
    param([string]$Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function ConvertTo-ManifestPath {
    param([string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if ($fullPath.StartsWith($RepoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $fullPath.Substring($RepoRoot.Length).TrimStart('\', '/').Replace('\', '/')
    }

    return $fullPath.Replace('\', '/')
}

function Invoke-Checked {
    param([string]$Name, [string[]]$Arguments)

    Write-Host "Running $Name..."
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

Push-Location $RepoRoot
try {
    if (!$NoValidation) {
        & ./scripts/test-release.ps1
    }

    [System.IO.Directory]::CreateDirectory($OutputPath) | Out-Null
    Invoke-Checked -Name 'pack navlyn' -Arguments @('pack', 'navlyn/navlyn.csproj', '-c', 'Release', '-o', $OutputPath)
    Invoke-Checked -Name 'pack navlyn-mcp' -Arguments @('pack', 'navlyn.Mcp/navlyn.Mcp.csproj', '-c', 'Release', '-o', $OutputPath)

    $navlynVersion = Get-ProjectVersion -ProjectPath (Join-Path $RepoRoot 'navlyn/navlyn.csproj')
    $mcpVersion = Get-ProjectVersion -ProjectPath (Join-Path $RepoRoot 'navlyn.Mcp/navlyn.Mcp.csproj')
    $navlynPackagePath = Join-Path $OutputPath "navlyn.$navlynVersion.nupkg"
    $mcpPackagePath = Join-Path $OutputPath "navlyn-mcp.$mcpVersion.nupkg"
    $manifest = [ordered]@{
        schemaVersion = 'navlyn.release-pack.v1'
        createdUtc = [DateTimeOffset]::UtcNow.ToString('o')
        packages = @(
            [ordered]@{
                id = 'navlyn'
                version = $navlynVersion
                path = ConvertTo-ManifestPath -Path $navlynPackagePath
                sha256 = Get-FileSha256 -Path $navlynPackagePath
            },
            [ordered]@{
                id = 'navlyn-mcp'
                version = $mcpVersion
                path = ConvertTo-ManifestPath -Path $mcpPackagePath
                sha256 = Get-FileSha256 -Path $mcpPackagePath
            }
        )
    }

    $manifestPath = Join-Path $OutputPath 'navlyn-release-pack.json'
    $manifest | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $manifestPath -Encoding utf8
    Write-Host "Release packages written to $OutputPath"
    Write-Host "Manifest: $manifestPath"
}
finally {
    Pop-Location
}

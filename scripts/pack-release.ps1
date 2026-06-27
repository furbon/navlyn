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
    $manifest = [ordered]@{
        schemaVersion = 'navlyn.release-pack.v1'
        createdUtc = [DateTimeOffset]::UtcNow.ToString('o')
        packages = @(
            [ordered]@{
                id = 'navlyn'
                version = $navlynVersion
                path = (Join-Path $OutputPath "navlyn.$navlynVersion.nupkg").Replace('\', '/')
            },
            [ordered]@{
                id = 'navlyn-mcp'
                version = $mcpVersion
                path = (Join-Path $OutputPath "navlyn-mcp.$mcpVersion.nupkg").Replace('\', '/')
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

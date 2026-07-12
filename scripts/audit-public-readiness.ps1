[CmdletBinding()]
param(
    [switch]$AllowIssues,
    [switch]$RunValidation
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot

function Add-Issue {
    param(
        [System.Collections.Generic.List[object]]$Issues,

        [Parameter(Mandatory = $true)]
        [string]$Code,

        [Parameter(Mandatory = $true)]
        [string]$Message,

        [string]$Path = ''
    )

    $Issues.Add([pscustomobject]@{
        Code = $Code
        Path = $Path
        Message = $Message
    }) | Out-Null
}

function Get-RepoRelativePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if ($fullPath.StartsWith($RepoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $fullPath.Substring($RepoRoot.Length).TrimStart('\', '/')
    }

    return $Path
}

function Test-GitTrackedPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $relativePath = Get-RepoRelativePath -Path $Path
    $output = & git -C $RepoRoot ls-files -- $relativePath
    return $output.Count -gt 0
}

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Command
    )

    Write-Host "Running $Name..."
    $scriptBlock = [scriptblock]::Create($Command)
    & $scriptBlock
}

function Get-ReleaseVersion {
    $propsPath = Join-Path $RepoRoot 'Directory.Build.props'
    if (!(Test-Path -LiteralPath $propsPath)) {
        Add-Issue -Issues $Issues -Code 'NAVLYN-PUBLIC-VERSION-PROPS-MISSING' -Path 'Directory.Build.props' -Message 'Directory.Build.props is missing; release version identity is not centralized.'
        return ''
    }

    [xml]$propsXml = Get-Content -Raw -LiteralPath $propsPath
    $versionNode = $propsXml.SelectSingleNode('//PropertyGroup/Version')
    $version = if ($null -eq $versionNode) { '' } else { [string]$versionNode.InnerText }
    if ([string]::IsNullOrWhiteSpace($version)) {
        Add-Issue -Issues $Issues -Code 'NAVLYN-PUBLIC-VERSION-MISSING' -Path 'Directory.Build.props' -Message 'Directory.Build.props does not define Version.'
        return ''
    }

    return $version
}

function Get-ProjectEffectiveVersion {
    param([string]$ProjectPath)

    [xml]$projectXml = Get-Content -Raw -LiteralPath $ProjectPath
    $projectVersionNode = $projectXml.SelectSingleNode('//PropertyGroup/Version')
    $projectVersion = if ($null -eq $projectVersionNode) { '' } else { [string]$projectVersionNode.InnerText }
    if (![string]::IsNullOrWhiteSpace($projectVersion)) {
        return $projectVersion
    }

    return Get-ReleaseVersion
}

$Issues = [System.Collections.Generic.List[object]]::new()
$ReleaseVersion = Get-ReleaseVersion

$readmePath = Join-Path $RepoRoot 'README.md'
if (!(Test-Path -LiteralPath $readmePath)) {
    Add-Issue -Issues $Issues -Code 'NAVLYN-PUBLIC-README-MISSING' -Path 'README.md' -Message 'README.md is missing.'
}
else {
    $readmeText = Get-Content -Raw -LiteralPath $readmePath
    if ($readmeText.Trim() -eq 'WIP' -or $readmeText -match '(?im)^\s*WIP\s*$') {
        Add-Issue -Issues $Issues -Code 'NAVLYN-PUBLIC-README-WIP' -Path 'README.md' -Message 'README.md still contains a WIP placeholder.'
    }
}

$licenseCandidates = @('LICENSE', 'LICENSE.md', 'COPYING') | ForEach-Object { Join-Path $RepoRoot $_ }
if (!($licenseCandidates | Where-Object { Test-Path -LiteralPath $_ })) {
    Add-Issue -Issues $Issues -Code 'NAVLYN-PUBLIC-LICENSE-MISSING' -Message 'No LICENSE file was found.'
}

foreach ($requiredPublicFile in @('CHANGELOG.md', 'CONTRIBUTING.md', 'SECURITY.md', 'docs/navlyn-distribution.md', 'docs/navlyn-performance.md')) {
    $requiredPublicPath = Join-Path $RepoRoot $requiredPublicFile
    if (!(Test-Path -LiteralPath $requiredPublicPath)) {
        Add-Issue -Issues $Issues -Code 'NAVLYN-PUBLIC-RELEASE-FILE-MISSING' -Path $requiredPublicFile -Message 'A release-readiness public file is missing.'
    }
}

$gitIgnorePath = Join-Path $RepoRoot '.gitignore'
if (!(Test-Path -LiteralPath $gitIgnorePath)) {
    Add-Issue -Issues $Issues -Code 'NAVLYN-PUBLIC-GITIGNORE-MISSING' -Path '.gitignore' -Message '.gitignore is missing.'
}
else {
    $gitIgnoreText = Get-Content -Raw -LiteralPath $gitIgnorePath
    if ($gitIgnoreText -notmatch '(?m)^\.docs/\s*$') {
        Add-Issue -Issues $Issues -Code 'NAVLYN-PUBLIC-DOCS-NOT-IGNORED' -Path '.gitignore' -Message '.docs/ is not ignored.'
    }
}

$trackedLocalPaths = & git -C $RepoRoot ls-files -- '.docs' '.vs' 'navlyn/bin' 'navlyn/obj' 'tests/fixtures/*/bin' 'tests/fixtures/*/obj'
foreach ($trackedLocalPath in $trackedLocalPaths) {
    Add-Issue -Issues $Issues -Code 'NAVLYN-PUBLIC-TRACKED-LOCAL-ARTIFACT' -Path $trackedLocalPath -Message 'Local scratch or build output is tracked.'
}

foreach ($projectRelativePath in @('navlyn/navlyn.csproj', 'navlyn.Mcp/navlyn.Mcp.csproj')) {
    $projectPath = Join-Path $RepoRoot $projectRelativePath
    if (Test-Path -LiteralPath $projectPath) {
        [xml]$projectXml = Get-Content -Raw -LiteralPath $projectPath
        $effectiveVersion = Get-ProjectEffectiveVersion -ProjectPath $projectPath
        if (![string]::IsNullOrWhiteSpace($ReleaseVersion) -and $effectiveVersion -ne $ReleaseVersion) {
            Add-Issue -Issues $Issues -Code 'NAVLYN-PUBLIC-PACKAGE-VERSION-MISMATCH' -Path $projectRelativePath -Message "Package version should match release version '$ReleaseVersion' but was '$effectiveVersion'."
        }

        foreach ($propertyName in @('Authors', 'PackageLicenseExpression', 'Description', 'PackageReadmeFile', 'RepositoryUrl', 'RepositoryType', 'PackageProjectUrl', 'PackageIcon', 'PackageTags', 'PackageReleaseNotes', 'Copyright', 'NeutralLanguage', 'PackageRequireLicenseAcceptance')) {
            $propertyNode = $projectXml.SelectSingleNode("//PropertyGroup/$propertyName")
            $value = if ($null -eq $propertyNode) { '' } else { [string]$propertyNode.InnerText }
            if ([string]::IsNullOrWhiteSpace($value)) {
                Add-Issue -Issues $Issues -Code "NAVLYN-PUBLIC-PACKAGE-$($propertyName.ToUpperInvariant())-MISSING" -Path $projectRelativePath -Message "Package metadata '$propertyName' is missing."
            }
        }

        $expectedValues = @{
            Authors = 'furbon.tech'
            PackageLicenseExpression = 'MIT'
            PackageReadmeFile = 'README.md'
            PackageIcon = 'navlyn-icon.png'
            RepositoryUrl = 'https://github.com/furbon/navlyn'
            RepositoryType = 'git'
            PackageProjectUrl = 'https://github.com/furbon/navlyn'
            NeutralLanguage = 'en-US'
            PackageRequireLicenseAcceptance = 'false'
        }

        foreach ($expected in $expectedValues.GetEnumerator()) {
            $node = $projectXml.SelectSingleNode("//PropertyGroup/$($expected.Key)")
            $actual = if ($null -eq $node) { '' } else { [string]$node.InnerText }
            if ($actual -ne $expected.Value) {
                Add-Issue -Issues $Issues -Code "NAVLYN-PUBLIC-PACKAGE-$($expected.Key.ToUpperInvariant())-UNEXPECTED" -Path $projectRelativePath -Message "Package metadata '$($expected.Key)' should be '$($expected.Value)' but was '$actual'."
            }
        }

        $packageIconPath = Join-Path $RepoRoot 'assets/navlyn-icon.png'
        if (!(Test-Path -LiteralPath $packageIconPath)) {
            Add-Issue -Issues $Issues -Code 'NAVLYN-PUBLIC-PACKAGE-ICON-MISSING' -Path 'assets/navlyn-icon.png' -Message 'Package icon file is missing.'
        }
    }
}

$copilotPath = Join-Path $RepoRoot '.github/copilot-instructions.md'
if (Test-Path -LiteralPath $copilotPath) {
    $copilotText = Get-Content -Raw -LiteralPath $copilotPath
    if ($copilotText -match '(?i)console app skeleton|planned navlyn commands already exist') {
        Add-Issue -Issues $Issues -Code 'NAVLYN-PUBLIC-COPILOT-STALE' -Path '.github/copilot-instructions.md' -Message 'Copilot instructions contain stale project-state wording.'
    }
}

$publicSearchRoots = @(
    'README.md',
    'README_ja.md',
    'CHANGELOG.md',
    'CONTRIBUTING.md',
    'SECURITY.md',
    'AGENTS.md',
    '.github',
    'docs'
)

$reviewPattern = '(?i)\bTODO\b|\bFIXME\b|\bHACK\b|\bWIP\b|\bscratch\b|私|あなた|相談|経緯|方針決定|decision log|we decided|decided to|as discussed|because we'
foreach ($root in $publicSearchRoots) {
    $fullRoot = Join-Path $RepoRoot $root
    if (!(Test-Path -LiteralPath $fullRoot)) {
        continue
    }

    $files = @()
    $rootItem = Get-Item -LiteralPath $fullRoot -Force
    if ($rootItem.PSIsContainer) {
        $files = Get-ChildItem -LiteralPath $fullRoot -Recurse -File -Force
    }
    else {
        $files = @($rootItem)
    }

    foreach ($file in $files) {
        $relativePath = Get-RepoRelativePath -Path $file.FullName
        $text = Get-Content -Raw -LiteralPath $file.FullName
        if ($text -match $reviewPattern) {
            Add-Issue -Issues $Issues -Code 'NAVLYN-PUBLIC-REVIEW-WORD' -Path $relativePath -Message 'Public-facing text contains a word or phrase that should be reviewed before publication.'
        }
    }
}

$staleVersionRoots = @(
    'README.md',
    'README_ja.md',
    'docs',
    'examples',
    'scripts'
)

foreach ($root in $staleVersionRoots) {
    $fullRoot = Join-Path $RepoRoot $root
    if (!(Test-Path -LiteralPath $fullRoot)) {
        continue
    }

    $rootItem = Get-Item -LiteralPath $fullRoot -Force
    $files = if ($rootItem.PSIsContainer) {
        Get-ChildItem -LiteralPath $fullRoot -Recurse -File -Force
    }
    else {
        @($rootItem)
    }

    foreach ($file in $files) {
        $relativePath = Get-RepoRelativePath -Path $file.FullName
        if ($relativePath -eq 'scripts\audit-public-readiness.ps1' -or $relativePath -eq 'scripts/audit-public-readiness.ps1') {
            continue
        }

        $text = Get-Content -Raw -LiteralPath $file.FullName
        if ($text -match '\b0\.6\.0\b') {
            Add-Issue -Issues $Issues -Code 'NAVLYN-PUBLIC-STALE-VERSION' -Path $relativePath -Message 'Public-facing release guidance still references 0.6.0 outside CHANGELOG.md.'
        }
    }
}

if ($RunValidation) {
    Invoke-CheckedCommand -Name 'dotnet restore' -Command 'dotnet restore navlyn.slnx'
    Invoke-CheckedCommand -Name 'dotnet build' -Command 'dotnet build navlyn.slnx'
    Invoke-CheckedCommand -Name 'quick validation' -Command './scripts/test-quick.ps1 -NoBuild'
    Invoke-CheckedCommand -Name 'CLI contract validation' -Command './scripts/test-cli-contract.ps1 -NoBuild'
}

if ($Issues.Count -eq 0) {
    Write-Host 'Public readiness audit passed.'
    exit 0
}

$Issues | Sort-Object Code, Path | Format-Table -AutoSize

if ($AllowIssues) {
    Write-Host "Public readiness audit found $($Issues.Count) issue(s). -AllowIssues was specified."
    exit 0
}

throw "Public readiness audit failed with $($Issues.Count) issue(s)."

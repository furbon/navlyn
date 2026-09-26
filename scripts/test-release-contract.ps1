[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$ExpectedVersion = '0.8.0-preview.1'
$Failures = [System.Collections.Generic.List[string]]::new()

function Get-RequiredXmlValue {
    param(
        [xml]$Document,
        [string]$XPath,
        [string]$Label
    )

    $node = $Document.SelectSingleNode($XPath)
    if ($null -eq $node -or [string]::IsNullOrWhiteSpace($node.InnerText)) {
        throw "Required package metadata '$Label' was not found at '$XPath'."
    }

    return [string]$node.InnerText.Trim()
}

function Add-ContractFailure {
    param([string]$Message)

    [void]$Failures.Add($Message)
}

$propsPath = Join-Path $RepoRoot 'Directory.Build.props'
if (!(Test-Path -LiteralPath $propsPath -PathType Leaf)) {
    throw "Shared version file was not found: $propsPath"
}

[xml]$props = Get-Content -Raw -LiteralPath $propsPath
foreach ($versionName in @('Version', 'PackageVersion', 'AssemblyVersion', 'FileVersion')) {
    $value = Get-RequiredXmlValue -Document $props -XPath "/Project/PropertyGroup/$versionName" -Label $versionName
    $validValues = switch ($versionName) {
        'Version' { @($ExpectedVersion); break }
        'PackageVersion' { @($ExpectedVersion, '$(Version)'); break }
        { $_ -in @('AssemblyVersion', 'FileVersion') } { @('0.8.0.0'); break }
    }
    if ($value -notin $validValues) {
        $expectedDescription = switch ($versionName) {
            'Version' { "'$ExpectedVersion'" }
            'PackageVersion' { "'$ExpectedVersion' or the shared Version property" }
            default { "numeric '0.8.0.0'" }
        }
        Add-ContractFailure "Directory.Build.props $versionName is '$value'; expected $expectedDescription."
    }
}

$informationalVersion = $props.SelectSingleNode('/Project/PropertyGroup/InformationalVersion')
if ($null -ne $informationalVersion -and $informationalVersion.InnerText.Trim() -notin @($ExpectedVersion, '$(Version)', '$(PackageVersion)')) {
    Add-ContractFailure "Directory.Build.props InformationalVersion is '$($informationalVersion.InnerText.Trim())'; expected '$ExpectedVersion' or a shared version property."
}

$expectedPackageMetadata = [ordered]@{
    Authors = 'furbon.tech'
    PackageLicenseExpression = 'MIT'
    RepositoryUrl = 'https://github.com/furbon/navlyn'
    RepositoryType = 'git'
    PackageProjectUrl = 'https://github.com/furbon/navlyn'
    Copyright = 'Copyright (c) 2026 furbon.tech'
    PackageReadmeFile = 'README.md'
    PackageIcon = 'navlyn-icon.png'
    TargetFrameworks = 'net8.0;net10.0'
}

$packages = @(
    [pscustomobject]@{ Id = 'navlyn'; Project = 'navlyn/navlyn.csproj'; Command = 'navlyn' },
    [pscustomobject]@{ Id = 'navlyn-mcp'; Project = 'navlyn.Mcp/navlyn.Mcp.csproj'; Command = 'navlyn-mcp' }
)
$packageSnapshots = [System.Collections.Generic.List[object]]::new()

foreach ($package in $packages) {
    $projectPath = Join-Path $RepoRoot $package.Project
    if (!(Test-Path -LiteralPath $projectPath -PathType Leaf)) {
        throw "Package project was not found: $projectPath"
    }

    [xml]$project = Get-Content -Raw -LiteralPath $projectPath
    foreach ($versionName in @('Version', 'PackageVersion', 'AssemblyVersion', 'FileVersion', 'InformationalVersion')) {
        $localOverride = $project.SelectSingleNode("//PropertyGroup/$versionName")
        if ($null -ne $localOverride) {
            Add-ContractFailure "$($package.Project) overrides $versionName locally; release versions must come from Directory.Build.props."
        }
    }

    $actualId = Get-RequiredXmlValue -Document $project -XPath '/Project/PropertyGroup/PackageId' -Label "$($package.Id) PackageId"
    if ($actualId -ne $package.Id) {
        Add-ContractFailure "$($package.Project) PackageId is '$actualId'; expected '$($package.Id)'."
    }

    $command = Get-RequiredXmlValue -Document $project -XPath '/Project/PropertyGroup/ToolCommandName' -Label "$($package.Id) ToolCommandName"
    if ($command -ne $package.Command) {
        Add-ContractFailure "$($package.Project) ToolCommandName is '$command'; expected '$($package.Command)'."
    }

    $metadata = [ordered]@{}
    foreach ($name in $expectedPackageMetadata.Keys) {
        $metadata[$name] = Get-RequiredXmlValue -Document $project -XPath "/Project/PropertyGroup/$name" -Label "$($package.Id) $name"
        if ($metadata[$name] -ne $expectedPackageMetadata[$name]) {
            Add-ContractFailure "$($package.Project) $name is '$($metadata[$name])'; expected '$($expectedPackageMetadata[$name])'."
        }
    }

    $releaseNotes = Get-RequiredXmlValue -Document $project -XPath '/Project/PropertyGroup/PackageReleaseNotes' -Label "$($package.Id) PackageReleaseNotes"
    if (!$releaseNotes.StartsWith($ExpectedVersion, [System.StringComparison]::Ordinal)) {
        Add-ContractFailure "$($package.Project) PackageReleaseNotes must start with locked identity '$ExpectedVersion'."
    }

    [void]$packageSnapshots.Add([pscustomobject]@{
        Id = $actualId
        Command = $command
        Metadata = $metadata
        ReleaseNotes = $releaseNotes
    })
}

if ($packageSnapshots.Count -eq 2) {
    foreach ($name in $expectedPackageMetadata.Keys) {
        if ($packageSnapshots[0].Metadata[$name] -ne $packageSnapshots[1].Metadata[$name]) {
            Add-ContractFailure "Package metadata '$name' differs between navlyn and navlyn-mcp."
        }
    }
}

if ($Failures.Count -gt 0) {
    Write-Error -ErrorAction Continue -Message ("Release contract failed:`n - " + ($Failures -join "`n - "))
    exit 1
}

Write-Output "Release identity and package metadata contract passed for $ExpectedVersion."

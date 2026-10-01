[CmdletBinding()]
param(
    [string]$Manifest = 'artifacts/packages/navlyn-release-pack.json',
    [string]$OutputReport = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$ExpectedVersion = '0.8.2'
$ExpectedFrameworks = @('net8.0', 'net10.0')
$Failures = [System.Collections.Generic.List[string]]::new()
$PackageResults = [System.Collections.Generic.List[object]]::new()

function Resolve-RepoPath {
    param([string]$Path, [string]$Label, [switch]$RequireIgnoredArtifacts)

    if ([string]::IsNullOrWhiteSpace($Path)) { throw "$Label is empty." }
    $candidate = if ([System.IO.Path]::IsPathRooted($Path)) { [System.IO.Path]::GetFullPath($Path) } else { [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $Path)) }
    $prefix = $RepoRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (!$candidate.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) { throw "$Label must resolve inside the repository." }
    $relative = $candidate.Substring($prefix.Length).Replace('\', '/')
    if ($RequireIgnoredArtifacts) {
        $artifactsPrefix = 'artifacts/'
        if (!$relative.StartsWith($artifactsPrefix, [System.StringComparison]::OrdinalIgnoreCase)) { throw "$Label must be inside ignored artifacts." }
        & git -C $RepoRoot check-ignore -q -- $relative
        if ($LASTEXITCODE -ne 0) { throw "$Label is not ignored by Git." }
    }
    $componentPath = $RepoRoot
    foreach ($component in ($relative -split '/')) {
        $componentPath = Join-Path $componentPath $component
        $item = Get-Item -LiteralPath $componentPath -Force -ErrorAction SilentlyContinue
        if ($null -ne $item -and ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "$Label traverses a link or reparse point."
        }
    }
    return $candidate
}

function Add-Failure {
    param([string]$Message)
    [void]$Failures.Add($Message)
}

function Get-XmlValue {
    param([xml]$Document, [string]$XPath)
    $node = $Document.SelectSingleNode($XPath)
    if ($null -eq $node) { return '' }
    return [string]$node.InnerText.Trim()
}

function Test-PackageEntryPath {
    param([string]$Path, [string]$PackageId)

    if ([string]::IsNullOrWhiteSpace($Path) -or $Path.StartsWith('/') -or $Path.Contains('\') -or $Path -match '(^|/)\.\.?(/|$)' -or $Path -match '^[A-Za-z]:' -or $Path -match '[:<>"|?*]') { return $false }
    if ($Path -match '(^|/)(obj|bin|\.git|\.vs|secrets?|credentials?|passwords?|\.env[^/]*)(/|$)') { return $false }
    if ($Path -match '\.(zip|tar|gz|7z|rar|nupkg|snupkg|cs|vb|sln|csproj|vbproj|pfx|snk)$') { return $false }

    if ($Path -in @('_rels/.rels', '[Content_Types].xml', 'README.md', 'README_ja.md', 'THIRD-PARTY-NOTICES.md', 'navlyn-icon.png', "$PackageId.nuspec")) { return $true }
    if ($Path -match '^package/services/metadata/core-properties/[^/]+\.psmdcp$') { return $true }
    if ($Path -match '^tools/(net8\.0|net10\.0)/any/([^/]+\.(dll|pdb|deps\.json|runtimeconfig\.json)|DotnetToolSettings\.xml)$') { return $true }
    if ($Path -match '^tools/(net8\.0|net10\.0)/any/BuildHost-(net472|netcore)/[^/]+\.(dll|exe|pdb|json|config)$') { return $true }
    if ($Path -match '^tools/(net8\.0|net10\.0)/any/[a-z]{2}(-[A-Za-z]{2,4})?/[^/]+\.resources\.dll$') { return $true }
    if ($Path -match '^tools/(net8\.0|net10\.0)/any/runtimes/(browser|win)/lib/net(8|10)\.0/[^/]+\.dll$') { return $true }
    return $false
}

function Get-EntryText {
    param([System.IO.Compression.ZipArchiveEntry]$Entry)
    $stream = $Entry.Open()
    try {
        $reader = [System.IO.StreamReader]::new($stream, [System.Text.Encoding]::UTF8, $true)
        try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
    } finally { $stream.Dispose() }
}

function Test-SafeText {
    param([string]$Text)
    if ($Text -match '(?i)([A-Z]:\\|/home/[^/]+/|/Users/[^/]+/|file://|\\\\[^\\]+\\)') { return $false }
    if ($Text -match '(?i)(password|access[_-]?token|client[_-]?secret|api[_-]?key)\s*["\'']?\s*[:=]\s*["\'']?[^\s"\'']{8,}') { return $false }
    return $true
}

$expectedMetadata = [ordered]@{
    authors = 'furbon.tech'
    repositoryUrl = 'https://github.com/furbon/navlyn'
    repositoryType = 'git'
    projectUrl = 'https://github.com/furbon/navlyn'
    licenseExpression = 'MIT'
    copyright = 'Copyright (c) 2026 furbon.tech'
    readme = 'README.md'
    icon = 'navlyn-icon.png'
}
$definitions = @(
    [pscustomobject]@{ Id = 'navlyn'; Project = 'navlyn/navlyn.csproj'; Command = 'navlyn'; EntryPoint = 'navlyn.dll' },
    [pscustomobject]@{ Id = 'navlyn-mcp'; Project = 'navlyn.Mcp/navlyn.Mcp.csproj'; Command = 'navlyn-mcp'; EntryPoint = 'navlyn.Mcp.dll' }
)

try {
    $manifestPath = Resolve-RepoPath -Path $Manifest -Label 'Manifest'
    if (!(Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'Manifest file was not found.' }
    $reportPath = $null
    if (![string]::IsNullOrWhiteSpace($OutputReport)) {
        $reportPath = Resolve-RepoPath -Path $OutputReport -Label 'OutputReport' -RequireIgnoredArtifacts
        $reportDirectory = Split-Path -Parent $reportPath
        if (!(Test-Path -LiteralPath $reportDirectory -PathType Container)) { throw 'OutputReport parent directory must already exist.' }
        if ($null -ne (Get-Item -LiteralPath $reportPath -Force -ErrorAction SilentlyContinue)) { throw 'OutputReport already exists.' }
    }
}
catch {
    [Console]::Error.WriteLine('Package contract path validation failed: use an existing in-repository manifest and a new report path under ignored artifacts.')
    exit 2
}

try {
    $propsPath = Join-Path $RepoRoot 'Directory.Build.props'
    [xml]$props = Get-Content -Raw -LiteralPath $propsPath
    $lockedVersion = Get-XmlValue -Document $props -XPath '/Project/PropertyGroup/Version'
    if ($lockedVersion -ne $ExpectedVersion) { Add-Failure 'Shared project version does not match the locked preview identity.' }

    $projectContracts = @{}
    foreach ($definition in $definitions) {
        $projectPath = Join-Path $RepoRoot $definition.Project
        [xml]$project = Get-Content -Raw -LiteralPath $projectPath
        $metadata = [ordered]@{}
        $mapping = [ordered]@{
            authors = 'Authors'; repositoryUrl = 'RepositoryUrl'; repositoryType = 'RepositoryType'; projectUrl = 'PackageProjectUrl';
            licenseExpression = 'PackageLicenseExpression'; copyright = 'Copyright'; readme = 'PackageReadmeFile'; icon = 'PackageIcon'
        }
        foreach ($key in $mapping.Keys) { $metadata[$key] = Get-XmlValue -Document $project -XPath "/Project/PropertyGroup/$($mapping[$key])" }
        $frameworks = (Get-XmlValue -Document $project -XPath '/Project/PropertyGroup/TargetFrameworks') -split ';' | Where-Object { $_ }
        $command = Get-XmlValue -Document $project -XPath '/Project/PropertyGroup/ToolCommandName'
        $packageId = Get-XmlValue -Document $project -XPath '/Project/PropertyGroup/PackageId'
        if ($packageId -ne $definition.Id -or $command -ne $definition.Command) { Add-Failure "Project contract mismatch for package '$($definition.Id)'." }
        if (($frameworks -join ',') -ne ($ExpectedFrameworks -join ',')) { Add-Failure "Project target frameworks mismatch for package '$($definition.Id)'." }
        foreach ($key in $expectedMetadata.Keys) { if ($metadata[$key] -ne $expectedMetadata[$key]) { Add-Failure "Project metadata '$key' mismatch for package '$($definition.Id)'." } }
        $projectContracts[$definition.Id] = [pscustomobject]@{ Metadata = $metadata; Command = $definition.Command; EntryPoint = $definition.EntryPoint }
    }

    $manifestObject = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    if ($manifestObject.schemaVersion -ne 'navlyn.release-pack.v1') { Add-Failure 'Manifest schemaVersion must be navlyn.release-pack.v1.' }
    $manifestPackages = @($manifestObject.packages)
    if ($manifestPackages.Count -ne 2) { Add-Failure 'Manifest must contain exactly two packages.' }
    $seenIds = @{}
    foreach ($item in $manifestPackages) {
        $id = [string]$item.id
        if ($id -notin @('navlyn', 'navlyn-mcp') -or $seenIds.ContainsKey($id)) { Add-Failure 'Manifest package IDs must be exactly navlyn and navlyn-mcp with no duplicates.'; continue }
        $seenIds[$id] = $true
        if ([string]$item.version -ne $ExpectedVersion) { Add-Failure "Manifest version mismatch for '$id'." }
        $relative = [string]$item.path
        if ($relative.Contains('\') -or [System.IO.Path]::IsPathRooted($relative) -or $relative -match '(^|/)\.\.?(/|$)' -or $relative -match '^[A-Za-z]:') { Add-Failure "Manifest path is unsafe for '$id'."; continue }
        try { $packagePath = Resolve-RepoPath -Path $relative -Label "Manifest path for $id" } catch { Add-Failure "Manifest path escapes the repository for '$id'."; continue }
        if ([System.IO.Path]::GetFileName($packagePath) -ne "$id.$ExpectedVersion.nupkg") { Add-Failure "Manifest package filename is unexpected for '$id'." }
        if (!(Test-Path -LiteralPath $packagePath -PathType Leaf)) { Add-Failure "Package file is missing for '$id'."; continue }
        $actualHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ([string]$item.sha256 -notmatch '^[a-fA-F0-9]{64}$' -or $actualHash -ne ([string]$item.sha256).ToLowerInvariant()) { Add-Failure "Manifest SHA-256 mismatch for '$id'." }

        $archive = [System.IO.Compression.ZipFile]::OpenRead($packagePath)
        try {
            $entries = @($archive.Entries | ForEach-Object { $_.FullName })
            if (@($entries | Group-Object | Where-Object Count -gt 1).Count -gt 0) { Add-Failure "Package '$id' contains duplicate entry paths." }
            foreach ($entryPath in $entries) { if (!(Test-PackageEntryPath -Path $entryPath -PackageId $id)) { Add-Failure "Package '$id' contains a disallowed path." } }
            foreach ($required in @('_rels/.rels', '[Content_Types].xml', 'README.md', 'README_ja.md', 'THIRD-PARTY-NOTICES.md', 'navlyn-icon.png', "$id.nuspec")) {
                if ($entries -cnotcontains $required) { Add-Failure "Package '$id' is missing a required package file." }
            }
            foreach ($framework in $ExpectedFrameworks) {
                $settingsPath = "tools/$framework/any/DotnetToolSettings.xml"
                $decompilerPath = "tools/$framework/any/ICSharpCode.Decompiler.dll"
                if ($entries -cnotcontains $decompilerPath) { Add-Failure "Package '$id' is missing the ILSpy decompiler payload for $framework." }
                if ($entries -cnotcontains $settingsPath) { Add-Failure "Package '$id' is missing tool settings for $framework."; continue }
                $settingsEntry = $archive.GetEntry($settingsPath)
                [xml]$settings = Get-EntryText -Entry $settingsEntry
                $commandNode = $settings.SelectSingleNode('/DotNetCliTool/Commands/Command')
                if ($null -eq $commandNode -or $commandNode.GetAttribute('Name') -ne $projectContracts[$id].Command -or $commandNode.GetAttribute('EntryPoint') -ne $projectContracts[$id].EntryPoint -or $commandNode.GetAttribute('Runner') -ne 'dotnet') {
                    Add-Failure "Package '$id' tool command settings mismatch for $framework."
                }
            }
            $nuspecEntry = $archive.GetEntry("$id.nuspec")
            [xml]$nuspec = Get-EntryText -Entry $nuspecEntry
            $metadata = $nuspec.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']")
            if ($null -eq $metadata) { Add-Failure "Package '$id' nuspec metadata is missing."; continue }
            $values = [ordered]@{
                id = Get-XmlValue -Document $nuspec -XPath "/*[local-name()='package']/*[local-name()='metadata']/*[local-name()='id']"
                version = Get-XmlValue -Document $nuspec -XPath "/*[local-name()='package']/*[local-name()='metadata']/*[local-name()='version']"
                authors = Get-XmlValue -Document $nuspec -XPath "/*[local-name()='package']/*[local-name()='metadata']/*[local-name()='authors']"
                repositoryUrl = $metadata.SelectSingleNode("*[local-name()='repository']").GetAttribute('url')
                repositoryType = $metadata.SelectSingleNode("*[local-name()='repository']").GetAttribute('type')
                licenseExpression = $metadata.SelectSingleNode("*[local-name()='license']").InnerText
                projectUrl = Get-XmlValue -Document $nuspec -XPath "/*[local-name()='package']/*[local-name()='metadata']/*[local-name()='projectUrl']"
                copyright = Get-XmlValue -Document $nuspec -XPath "/*[local-name()='package']/*[local-name()='metadata']/*[local-name()='copyright']"
                readme = Get-XmlValue -Document $nuspec -XPath "/*[local-name()='package']/*[local-name()='metadata']/*[local-name()='readme']"
                icon = Get-XmlValue -Document $nuspec -XPath "/*[local-name()='package']/*[local-name()='metadata']/*[local-name()='icon']"
            }
            if ($values.id -ne $id -or $values.version -ne $ExpectedVersion) { Add-Failure "Package '$id' nuspec identity mismatch." }
            if ($metadata.SelectSingleNode("*[local-name()='license']").GetAttribute('type') -ne 'expression') { Add-Failure "Package '$id' license must use an SPDX expression." }
            foreach ($key in $expectedMetadata.Keys) {
                if ([string]$values[$key] -ne [string]$projectContracts[$id].Metadata[$key]) { Add-Failure "Package '$id' nuspec metadata '$key' differs from the project contract." }
            }
            foreach ($entry in $archive.Entries) {
                if ($entry.FullName -match '\.(xml|json|config|md|txt|psmdcp)$') {
                    $contents = Get-EntryText -Entry $entry
                    if (!(Test-SafeText -Text $contents)) { Add-Failure "Package '$id' contains machine-specific or credential-like text." }
                }
            }
            [void]$PackageResults.Add([ordered]@{ id = $id; version = $ExpectedVersion; path = $relative.Replace('\', '/'); sha256 = $actualHash; entryCount = $entries.Count; targetFrameworks = $ExpectedFrameworks })
        } finally { $archive.Dispose() }
    }
    if ($seenIds.Count -ne 2) { Add-Failure 'Manifest does not identify both required packages.' }
} catch {
    Add-Failure "Package inspection could not complete ($($_.Exception.GetType().Name))."
}

$report = [ordered]@{
    schemaVersion = 'navlyn.package-contract-report.v1'
    status = if ($Failures.Count -eq 0) { 'passed' } else { 'failed' }
    version = $ExpectedVersion
    packages = @($PackageResults | Sort-Object id | ForEach-Object { $_ })
    failures = @($Failures.ToArray())
}
$reportJson = $report | ConvertTo-Json -Depth 20
if ($null -ne $reportPath) {
    try {
        [void](Resolve-RepoPath -Path $reportPath -Label 'OutputReport' -RequireIgnoredArtifacts)
        $stream = [System.IO.File]::Open($reportPath, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
        try {
            $writer = [System.IO.StreamWriter]::new($stream, [System.Text.UTF8Encoding]::new($false))
            try { $writer.WriteLine($reportJson) } finally { $writer.Dispose() }
        } finally { $stream.Dispose() }
    }
    catch {
        [Console]::Error.WriteLine('Could not create a new package contract report in ignored artifacts.')
        exit 2
    }
}

Write-Output $reportJson
if ($Failures.Count -gt 0) {
    foreach ($failure in $Failures) { [Console]::Error.WriteLine("Package contract: $failure") }
    [Console]::Error.WriteLine('Package contract validation failed; see the sanitized JSON report.')
    exit 1
}

exit 0

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Install', 'Uninstall')]
    [string] $Action,

    [Parameter(Mandatory = $true)]
    [string] $DestinationRoot,

    [string] $SourceRoot = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:MarkerName = '.navlyn-semantic-routing.install.json'
$script:SkillName = 'navlyn-semantic-routing'
$script:SchemaId = 'furbon.navlyn.semantic-routing-install'
$script:SchemaVersion = 1
$script:ReleaseVersion = '0.8.0'
$script:SupportedMarkerVersions = @('0.8.0', '0.8.0-preview.1', '0.8.0-preview.0')
$script:RelativeFiles = @(
    'SKILL.md',
    'references/routing-matrix.md',
    'references/evidence-boundaries.md'
)

function Get-NormalizedPath([string] $Path) {
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $volumeRoot = [System.IO.Path]::GetPathRoot($fullPath)
    $trimmed = $fullPath.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    if ($trimmed.Length -lt $volumeRoot.Length) { return $volumeRoot }
    return $trimmed
}

function Assert-NoReparseComponents([string] $Path) {
    $fullPath = Get-NormalizedPath $Path
    $item = Get-Item -LiteralPath $fullPath -Force -ErrorAction SilentlyContinue
    if ($null -ne $item -and (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)) {
        throw 'Destination contains a reparse point.'
    }

    $parent = Split-Path -Parent $fullPath
    if ($parent -and $parent -ne $fullPath) {
        Assert-NoReparseComponents $parent
    }
}

function Get-FileHashes([string] $Root) {
    Assert-NoReparseComponents $Root
    $result = [ordered]@{}
    foreach ($relative in $script:RelativeFiles) {
        $filePath = Join-Path $Root ($relative.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
        Assert-NoReparseComponents $filePath
        if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) {
            throw "Required skill file is missing: $relative"
        }
        $result[$relative] = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    return $result
}

function Assert-ExactDirectory([string] $Directory) {
    if (-not (Test-Path -LiteralPath $Directory -PathType Container)) { return }
    $actual = @(Get-ChildItem -LiteralPath $Directory -Force -Recurse | ForEach-Object {
        if (($_.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'Skill destination contains a reparse point.'
        }
        [System.IO.Path]::GetRelativePath($Directory, $_.FullName).Replace('\', '/')
    } | Sort-Object -Unique)
    $expected = @('references') + $script:RelativeFiles | Sort-Object
    if (($actual -join "`n") -cne ($expected -join "`n")) {
        throw 'Skill destination has missing or unrelated entries.'
    }
}

function Read-And-ValidateMarker([string] $MarkerPath, [string] $Destination) {
    if (-not (Test-Path -LiteralPath $MarkerPath -PathType Leaf)) {
        throw 'A valid installer ownership marker is required.'
    }
    try { $marker = Get-Content -LiteralPath $MarkerPath -Raw | ConvertFrom-Json -AsHashtable }
    catch { throw 'Installer ownership marker is malformed.' }

    $required = @('schemaId', 'schemaVersion', 'navlynVersion', 'destination', 'files', 'ownership')
    if ($null -eq $marker -or (@($marker.Keys | Sort-Object) -join "`n") -cne (@($required | Sort-Object) -join "`n")) {
        throw 'Installer ownership marker has an unsupported shape.'
    }
    if ($marker.schemaId -cne $script:SchemaId -or $marker.schemaVersion -ne $script:SchemaVersion) {
        throw 'Installer ownership marker has an unsupported schema.'
    }
    if ([string]$marker.navlynVersion -cnotin $script:SupportedMarkerVersions) {
        throw 'Installer ownership marker has an invalid release version.'
    }
    if ((Get-NormalizedPath ([string]$marker.destination)) -cne $Destination) {
        throw 'Installer ownership marker names a different destination.'
    }
    if ($null -eq $marker.ownership -or (@($marker.ownership.Keys | Sort-Object) -join "`n") -cne (@('managedDirectory', 'managedFiles') -join "`n") -or $marker.ownership.managedFiles -cne 'The three canonical routing-skill files only' -or $marker.ownership.managedDirectory -cne 'This destination directory only') {
        throw 'Installer ownership marker has an unsupported ownership declaration.'
    }
    if ($null -eq $marker.files -or (@($marker.files.Keys | Sort-Object) -join "`n") -cne (@($script:RelativeFiles | Sort-Object) -join "`n")) {
        throw 'Installer ownership marker does not name exactly the canonical files.'
    }
    foreach ($relative in $script:RelativeFiles) {
        if ([string]$marker.files[$relative] -notmatch '^[0-9a-f]{64}$') {
            throw 'Installer ownership marker contains an invalid file hash.'
        }
    }
    return $marker
}

function Assert-InstalledMatchesMarker($Marker, [string] $Destination) {
    Assert-ExactDirectory $Destination
    $actual = Get-FileHashes $Destination
    foreach ($relative in $script:RelativeFiles) {
        if ($actual[$relative] -cne [string]$Marker.files[$relative]) {
            throw "Managed skill file differs from its ownership marker: $relative"
        }
    }
}

function Write-AtomicMarker([string] $MarkerPath, [string] $Directory, $Marker, [bool] $IsUpdate) {
    $temporary = Join-Path $Directory ('.navlyn-skill-marker-' + [guid]::NewGuid().ToString('N') + '.tmp')
    try {
        $json = $Marker | ConvertTo-Json -Depth 8
        $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes($json + "`n")
        $stream = [System.IO.File]::Open($temporary, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
        try { $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
        if ($IsUpdate) {
            [System.IO.File]::Move($temporary, $MarkerPath, $true)
        } else {
            [System.IO.File]::Move($temporary, $MarkerPath)
        }
    } finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
    }
}

try {
    if (-not [System.IO.Path]::IsPathFullyQualified($DestinationRoot)) { throw 'Destination root must be an absolute path.' }
    $root = Get-NormalizedPath $DestinationRoot
    Assert-NoReparseComponents $root
    if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw 'Destination root must already exist.' }
    $destination = Get-NormalizedPath (Join-Path $root $script:SkillName)
    $markerPath = Join-Path $root $script:MarkerName
    Assert-NoReparseComponents $markerPath
    $source = if ([string]::IsNullOrWhiteSpace($SourceRoot)) {
        Join-Path (Split-Path -Parent $PSScriptRoot) '.agents/skills/navlyn-semantic-routing'
    } else {
        Get-NormalizedPath $SourceRoot
    }
    if ($Action -eq 'Install') {
        Assert-NoReparseComponents $destination
        $sourceHashes = Get-FileHashes $source
        $hasMarker = Test-Path -LiteralPath $markerPath
        $hasDestination = Test-Path -LiteralPath $destination
        if ($hasMarker) {
            $marker = Read-And-ValidateMarker $markerPath $destination
            Assert-InstalledMatchesMarker $marker $destination
            $same = $marker.navlynVersion -ceq $script:ReleaseVersion
            foreach ($relative in $script:RelativeFiles) { if ($marker.files[$relative] -cne $sourceHashes[$relative]) { $same = $false } }
            if ($same) {
                @{ status = 'unchanged'; destination = $destination; version = $script:ReleaseVersion } | ConvertTo-Json -Compress
                exit 0
            }
            $isUpdate = $true
        } elseif ($hasDestination) {
            throw 'Destination exists without a valid installer ownership marker.'
        } else {
            if (Test-Path -LiteralPath $markerPath) { throw 'An unrelated marker file already exists.' }
            $isUpdate = $false
            [void][System.IO.Directory]::CreateDirectory($destination)
        }

        $previousBytes = @{}
        if ($isUpdate) {
            foreach ($relative in $script:RelativeFiles) {
                $target = Join-Path $destination ($relative.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
                $previousBytes[$relative] = [System.IO.File]::ReadAllBytes($target)
            }
        }
        try {
            foreach ($relative in $script:RelativeFiles) {
                $target = Join-Path $destination ($relative.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
                $parent = Split-Path -Parent $target
                [void][System.IO.Directory]::CreateDirectory($parent)
                [System.IO.File]::Copy((Join-Path $source ($relative.Replace('/', [System.IO.Path]::DirectorySeparatorChar))), $target, $isUpdate)
            }
            Assert-ExactDirectory $destination
            $installedHashes = Get-FileHashes $destination
            $newMarker = [ordered]@{
                schemaId = $script:SchemaId
                schemaVersion = $script:SchemaVersion
                navlynVersion = $script:ReleaseVersion
                destination = $destination
                files = $installedHashes
                ownership = [ordered]@{
                    managedFiles = 'The three canonical routing-skill files only'
                    managedDirectory = 'This destination directory only'
                }
            }
            Write-AtomicMarker $markerPath $root $newMarker $isUpdate
        } catch {
            if ($isUpdate) {
                foreach ($relative in $script:RelativeFiles) {
                    $target = Join-Path $destination ($relative.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
                    [System.IO.File]::WriteAllBytes($target, $previousBytes[$relative])
                }
            } else {
                Assert-NoReparseComponents $destination
                foreach ($relative in $script:RelativeFiles) {
                    $target = Join-Path $destination ($relative.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
                    if (Test-Path -LiteralPath $target -PathType Leaf) {
                        Assert-NoReparseComponents $target
                        $targetHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
                        if ($targetHash -eq $sourceHashes[$relative]) { Remove-Item -LiteralPath $target -Force }
                    }
                }
                if (Test-Path -LiteralPath $destination) {
                    $references = Join-Path $destination 'references'
                    if ((Test-Path -LiteralPath $references -PathType Container) -and (@(Get-ChildItem -LiteralPath $references -Force).Count -eq 0)) {
                        Remove-Item -LiteralPath $references -Force
                    }
                    if (@(Get-ChildItem -LiteralPath $destination -Force).Count -eq 0) { Remove-Item -LiteralPath $destination -Force }
                }
            }
            throw
        }
        @{ status = $(if ($isUpdate) { 'updated' } else { 'installed' }); destination = $destination; version = $script:ReleaseVersion } | ConvertTo-Json -Compress
        exit 0
    }

    $marker = Read-And-ValidateMarker $markerPath $destination
    Assert-InstalledMatchesMarker $marker $destination
    foreach ($relative in $script:RelativeFiles) {
        $target = Join-Path $destination ($relative.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
        Remove-Item -LiteralPath $target -Force
    }
    $references = Join-Path $destination 'references'
    if (Test-Path -LiteralPath $references -PathType Container) {
        if (@(Get-ChildItem -LiteralPath $references -Force).Count -ne 0) { throw 'Skill references directory is not empty.' }
        Remove-Item -LiteralPath $references -Force
    }
    Remove-Item -LiteralPath $destination -Force
    Remove-Item -LiteralPath $markerPath -Force
    @{ status = 'uninstalled'; destination = $destination } | ConvertTo-Json -Compress
    exit 0
} catch {
    [Console]::Error.WriteLine("Routing skill $($Action.ToLowerInvariant()) failed: $($_.Exception.Message)")
    exit 1
}

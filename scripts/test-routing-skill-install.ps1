[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $OutputReport
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/navlyn-release-version.ps1')
$ReleaseVersion = Get-NavlynReleaseVersion
$upgradeVersions = @('0.8.0', '0.8.1', '0.8.5', '0.8.6', '0.8.7', '0.9.0') | Where-Object { $_ -cne $ReleaseVersion }

$repo = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$installer = Join-Path $PSScriptRoot 'install-routing-skill.ps1'
$source = Join-Path $repo '.agents/skills/navlyn-semantic-routing'
$files = @('SKILL.md', 'references/routing-matrix.md', 'references/evidence-boundaries.md')
$markerName = '.navlyn-semantic-routing.install.json'
$evidenceRoot = Join-Path $repo 'artifacts'
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('navlyn-routing-skill-' + [guid]::NewGuid().ToString('N'))
$linkTarget = Join-Path $tempRoot 'reparse-target'
$linkPath = Join-Path $tempRoot 'reparse-skills'
$results = [System.Collections.Generic.List[object]]::new()
$failure = $null

function Get-FullPath([string] $Path) {
    $full = [System.IO.Path]::GetFullPath($Path)
    $volumeRoot = [System.IO.Path]::GetPathRoot($full)
    $trimmed = $full.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    if ($trimmed.Length -lt $volumeRoot.Length) { return $volumeRoot }
    return $trimmed
}

function Get-SafeFailure([System.Exception] $Exception) {
    $message = [string]$Exception.Message
    foreach ($sensitivePath in @($repo, $tempRoot, $evidenceRoot, [System.IO.Path]::GetTempPath(), $env:USERPROFILE, $env:HOME)) {
        if (-not [string]::IsNullOrWhiteSpace($sensitivePath)) {
            $message = $message.Replace($sensitivePath, '<path>', [System.StringComparison]::OrdinalIgnoreCase)
        }
    }
    $message = [System.Text.RegularExpressions.Regex]::Replace($message, '(?i)\b[A-Z]:\\[^\s"''<>]+', '<path>')
    $message = [System.Text.RegularExpressions.Regex]::Replace($message, '(?<![A-Za-z0-9])/(?:[^\s"''<>]+/)*[^\s"''<>]*', '<path>')
    $message = [System.Text.RegularExpressions.Regex]::Replace($message, '\s+', ' ').Trim()
    if ($message.Length -gt 240) { $message = $message.Substring(0, 240) }
    return "$($Exception.GetType().Name): $message"
}

function Assert-NoReparse([string] $Path) {
    $full = Get-FullPath $Path
    $item = Get-Item -LiteralPath $full -Force -ErrorAction SilentlyContinue
    if ($null -ne $item -and (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)) {
        throw 'A test path contains a reparse point.'
    }
    $parent = Split-Path -Parent $full
    if ($parent -and $parent -ne $full) { Assert-NoReparse $parent }
}

function Get-Inventory([string] $Destination) {
    if (-not (Test-Path -LiteralPath $Destination -PathType Container)) { return @() }
    return @(Get-ChildItem -LiteralPath $Destination -File -Recurse -Force | ForEach-Object {
        [pscustomobject][ordered]@{
            path = [System.IO.Path]::GetRelativePath($Destination, $_.FullName).Replace('\', '/')
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    } | Sort-Object path)
}

function Invoke-Installer([string] $Verb, [string] $Root) {
    $output = @(& pwsh -NoLogo -NoProfile -File $installer -Action $Verb -DestinationRoot $Root 2>&1)
    $code = $LASTEXITCODE
    return [ordered]@{ exitCode = $code; output = (@($output | ForEach-Object { [string]$_ }) -join "`n") }
}

function Set-OlderFixture([string] $Root, [string] $Version) {
    $destination = Join-Path $Root 'navlyn-semantic-routing'
    $path = Join-Path $destination 'SKILL.md'
    [System.IO.File]::AppendAllText($path, "`n<!-- controlled older release fixture -->`n", [System.Text.UTF8Encoding]::new($false))
    $markerPath = Join-Path $Root $markerName
    $marker = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json -AsHashtable
    $marker.navlynVersion = $Version
    $marker.files['SKILL.md'] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    [System.IO.File]::WriteAllText($markerPath, ($marker | ConvertTo-Json -Depth 8), [System.Text.UTF8Encoding]::new($false))
}

function Invoke-Lifecycle([string] $Label, [string] $Root) {
    [void][System.IO.Directory]::CreateDirectory($Root)
    Assert-NoReparse $Root
    $first = Invoke-Installer 'Install' $Root
    if ($first.exitCode -ne 0) { throw "$Label first install failed." }
    $destination = Join-Path $Root 'navlyn-semantic-routing'
    $inventory = Get-Inventory $destination
    if ((@($inventory.path) -join "`n") -cne (@($files | Sort-Object) -join "`n")) { throw "$Label inventory is not exactly the canonical three files." }
    foreach ($entry in $inventory) {
        $relative = $entry.path.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
        $sourceHash = (Get-FileHash -LiteralPath (Join-Path $source $relative) -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($entry.sha256 -cne $sourceHash) { throw "$Label installed file hash differs from source." }
    }

    $before = Get-Inventory $destination | ConvertTo-Json -Compress -Depth 5
    $markerBefore = (Get-FileHash -LiteralPath (Join-Path $Root $markerName) -Algorithm SHA256).Hash
    $same = Invoke-Installer 'Install' $Root
    $after = Get-Inventory $destination | ConvertTo-Json -Compress -Depth 5
    $markerAfter = (Get-FileHash -LiteralPath (Join-Path $Root $markerName) -Algorithm SHA256).Hash
    if ($same.exitCode -ne 0 -or $before -cne $after -or $markerBefore -cne $markerAfter) { throw "$Label identical reinstall was not idempotent." }

    foreach ($olderVersion in $upgradeVersions) {
        Set-OlderFixture $Root $olderVersion
        $upgrade = Invoke-Installer 'Install' $Root
        if ($upgrade.exitCode -ne 0) { throw "$Label controlled upgrade from $olderVersion failed." }
        if ((@(Get-Inventory $destination | ForEach-Object { $_.sha256 }) -join "`n") -cne (@(Get-Inventory $source | ForEach-Object { $_.sha256 }) -join "`n")) { throw "$Label upgrade did not restore current source bytes." }
    }

    $unrelatedPath = Join-Path $destination 'user-owned-extra.txt'
    [System.IO.File]::WriteAllText($unrelatedPath, 'preserve unrelated entry', [System.Text.UTF8Encoding]::new($false))
    $unrelatedHash = (Get-FileHash -LiteralPath $unrelatedPath -Algorithm SHA256).Hash
    $unrelatedInstall = Invoke-Installer 'Install' $Root
    $unrelatedUninstall = Invoke-Installer 'Uninstall' $Root
    if ($unrelatedInstall.exitCode -eq 0 -or $unrelatedUninstall.exitCode -eq 0 -or (Get-FileHash -LiteralPath $unrelatedPath -Algorithm SHA256).Hash -cne $unrelatedHash) { throw "$Label unrelated destination entry was not preserved." }
    Remove-Item -LiteralPath $unrelatedPath -Force

    $markerPath = Join-Path $Root $markerName
    $validMarkerBytes = [System.IO.File]::ReadAllBytes($markerPath)
    $cleanInventory = Get-Inventory $destination | ConvertTo-Json -Compress -Depth 5
    [System.IO.File]::WriteAllText($markerPath, '{ malformed', [System.Text.UTF8Encoding]::new($false))
    $malformedInstall = Invoke-Installer 'Install' $Root
    $malformedUninstall = Invoke-Installer 'Uninstall' $Root
    if ($malformedInstall.exitCode -eq 0 -or $malformedUninstall.exitCode -eq 0 -or (Get-Inventory $destination | ConvertTo-Json -Compress -Depth 5) -cne $cleanInventory) { throw "$Label malformed-marker conflict changed managed files." }
    [System.IO.File]::WriteAllBytes($markerPath, $validMarkerBytes)

    $validMarker = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json -AsHashtable
    $validMarker.navlynVersion = '0.8.0-preview.99'
    [System.IO.File]::WriteAllText($markerPath, ($validMarker | ConvertTo-Json -Depth 8), [System.Text.UTF8Encoding]::new($false))
    $unsupportedVersionInstall = Invoke-Installer 'Install' $Root
    $unsupportedVersionUninstall = Invoke-Installer 'Uninstall' $Root
    if ($unsupportedVersionInstall.exitCode -eq 0 -or $unsupportedVersionUninstall.exitCode -eq 0 -or (Get-Inventory $destination | ConvertTo-Json -Compress -Depth 5) -cne $cleanInventory) { throw "$Label unsupported marker version changed managed files." }
    [System.IO.File]::WriteAllBytes($markerPath, $validMarkerBytes)

    $validMarker = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json -AsHashtable
    $validMarker.destination = Join-Path $Root 'different-destination'
    [System.IO.File]::WriteAllText($markerPath, ($validMarker | ConvertTo-Json -Depth 8), [System.Text.UTF8Encoding]::new($false))
    $wrongDestinationInstall = Invoke-Installer 'Install' $Root
    $wrongDestinationUninstall = Invoke-Installer 'Uninstall' $Root
    if ($wrongDestinationInstall.exitCode -eq 0 -or $wrongDestinationUninstall.exitCode -eq 0 -or (Get-Inventory $destination | ConvertTo-Json -Compress -Depth 5) -cne $cleanInventory) { throw "$Label wrong-destination marker changed managed files." }
    [System.IO.File]::WriteAllBytes($markerPath, $validMarkerBytes)

    $backupMarker = Join-Path $Root 'marker-owned-backup.json'
    Move-Item -LiteralPath $markerPath -Destination $backupMarker
    $missingMarkerInstall = Invoke-Installer 'Install' $Root
    $missingMarkerUninstall = Invoke-Installer 'Uninstall' $Root
    if ($missingMarkerInstall.exitCode -eq 0 -or $missingMarkerUninstall.exitCode -eq 0 -or (Get-Inventory $destination | ConvertTo-Json -Compress -Depth 5) -cne $cleanInventory) { throw "$Label missing-marker conflict changed managed files." }
    Move-Item -LiteralPath $backupMarker -Destination $markerPath

    $missingManagedPath = Join-Path $destination 'references/routing-matrix.md'
    $missingManagedBytes = [System.IO.File]::ReadAllBytes($missingManagedPath)
    Remove-Item -LiteralPath $missingManagedPath -Force
    $missingManagedInstall = Invoke-Installer 'Install' $Root
    $missingManagedUninstall = Invoke-Installer 'Uninstall' $Root
    if ($missingManagedInstall.exitCode -eq 0 -or $missingManagedUninstall.exitCode -eq 0 -or (Test-Path -LiteralPath $missingManagedPath)) { throw "$Label missing-managed-file conflict was not preserved." }
    [System.IO.File]::WriteAllBytes($missingManagedPath, $missingManagedBytes)

    $skillFile = Join-Path $destination 'SKILL.md'
    $originalBytes = [System.IO.File]::ReadAllBytes($skillFile)
    [System.IO.File]::AppendAllText($skillFile, "`nuser-owned-divergence`n", [System.Text.UTF8Encoding]::new($false))
    $divergedHash = (Get-FileHash -LiteralPath $skillFile -Algorithm SHA256).Hash
    $conflict = Invoke-Installer 'Install' $Root
    $preservedHash = (Get-FileHash -LiteralPath $skillFile -Algorithm SHA256).Hash
    if ($conflict.exitCode -eq 0 -or $divergedHash -cne $preservedHash) { throw "$Label divergent-file collision was not safely preserved." }
    $uninstallConflict = Invoke-Installer 'Uninstall' $Root
    if ($uninstallConflict.exitCode -eq 0 -or (Get-FileHash -LiteralPath $skillFile -Algorithm SHA256).Hash -cne $divergedHash) { throw "$Label uninstall did not preserve a divergent file." }
    [System.IO.File]::WriteAllBytes($skillFile, $originalBytes)
    $uninstall = Invoke-Installer 'Uninstall' $Root
    if ($uninstall.exitCode -ne 0 -or (Test-Path -LiteralPath $destination) -or (Test-Path -LiteralPath (Join-Path $Root $markerName))) { throw "$Label uninstall did not remove only the managed installation." }

    $markerPath = Join-Path $Root $markerName
    $collisionBytes = [System.Text.UTF8Encoding]::new($false).GetBytes('unrelated marker collision')
    [System.IO.File]::WriteAllBytes($markerPath, $collisionBytes)
    $markerCollision = Invoke-Installer 'Install' $Root
    if ($markerCollision.exitCode -eq 0 -or [System.IO.File]::ReadAllText($markerPath) -cne 'unrelated marker collision') { throw "$Label unrelated marker collision was not safely preserved." }
    Remove-Item -LiteralPath $markerPath -Force

    return [ordered]@{
        layout = $Label
        install = 'passed'
        exactInventoryAndHashes = 'passed'
        idempotentReinstall = 'passed'
        controlledOlderUpgrade = 'passed'
        controlledOlderVersions = @($upgradeVersions)
        malformedWrongOrMissingMarkerPreserved = 'passed'
        missingManagedFileConflictPreserved = 'passed'
        divergentCollisionPreserved = 'passed'
        uninstallConflictPreserved = 'passed'
        unrelatedMarkerCollisionPreserved = 'passed'
        uninstall = 'passed'
    }
}

try {
    if (-not [System.IO.Path]::IsPathFullyQualified($OutputReport)) { throw 'Output report path must be absolute.' }
    $reportPath = Get-FullPath $OutputReport
    $reportParent = Split-Path -Parent $reportPath
    $evidenceFull = Get-FullPath $evidenceRoot
    if (-not $reportPath.StartsWith($evidenceFull + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Output report must be beneath ignored artifacts/.' }
    if (-not (Test-Path -LiteralPath $evidenceFull -PathType Container) -or -not (Test-Path -LiteralPath $reportParent -PathType Container)) { throw 'Evidence directory and report parent must already exist.' }
    $ignored = & git -C $repo check-ignore -q -- $reportPath
    if ($LASTEXITCODE -ne 0) { throw 'Output report is not ignored by Git.' }
    Assert-NoReparse $reportParent
    if (Test-Path -LiteralPath $reportPath) { throw 'Output report already exists.' }

    [void][System.IO.Directory]::CreateDirectory($tempRoot)
    Assert-NoReparse $tempRoot
    $repoSkillsRoot = Join-Path (Join-Path $tempRoot 'isolated-repository') '.agents/skills'
    $userSkillsRoot = Join-Path (Join-Path $tempRoot 'isolated-home') '.agents/skills'
    $repoResult = Invoke-Lifecycle 'repository' $repoSkillsRoot
    $results.Add($repoResult)
    $userResult = Invoke-Lifecycle 'user' $userSkillsRoot
    $results.Add($userResult)

    [void][System.IO.Directory]::CreateDirectory($linkTarget)
    if ($IsWindows) {
        New-Item -ItemType Junction -Path $linkPath -Target $linkTarget | Out-Null
    } else {
        New-Item -ItemType SymbolicLink -Path $linkPath -Target $linkTarget | Out-Null
    }
    $reparseAttempt = Invoke-Installer 'Install' $linkPath
    if ($reparseAttempt.exitCode -eq 0 -or (Test-Path -LiteralPath (Join-Path $linkTarget 'navlyn-semantic-routing'))) { throw 'Installer accepted a reparse-point destination root.' }
    [System.IO.Directory]::Delete($linkPath, $false)
    if (Test-Path -LiteralPath $linkPath) { throw 'Test reparse point could not be removed without recursion.' }
    $results.Add([ordered]@{ layout = 'path-safety'; reparseDestinationRejected = 'passed'; containment = 'passed' })
} catch {
    $failure = Get-SafeFailure $_.Exception
} finally {
    if (Test-Path -LiteralPath $tempRoot) {
        $fullTemp = Get-FullPath $tempRoot
        $tempPrefix = Get-FullPath ([System.IO.Path]::GetTempPath()) + [System.IO.Path]::DirectorySeparatorChar + 'navlyn-routing-skill-'
        if (Test-Path -LiteralPath $linkPath) {
            $linkItem = Get-Item -LiteralPath $linkPath -Force
            $fullLink = Get-FullPath $linkPath
            $fullTarget = Get-FullPath $linkTarget
            if ($fullLink.StartsWith($fullTemp + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase) -and $fullTarget.StartsWith($fullTemp + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase) -and (($linkItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)) {
                [System.IO.Directory]::Delete($linkPath, $false)
            } else {
                $failure = 'Test reparse point did not match its owned temporary paths.'
            }
        }
        $tempItem = Get-Item -LiteralPath $fullTemp -Force
        if (-not $fullTemp.StartsWith($tempPrefix, [System.StringComparison]::OrdinalIgnoreCase) -or (($tempItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)) {
            $failure = 'Owned temporary root failed cleanup validation.'
        } else {
            $nestedReparse = @(Get-ChildItem -LiteralPath $fullTemp -Force -Recurse -ErrorAction SilentlyContinue | Where-Object { ($_.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0 })
            if ($nestedReparse.Count -gt 0) {
                $failure = 'Owned temporary root contains an unexpected reparse point.'
            } else {
                Remove-Item -LiteralPath $fullTemp -Recurse -Force
            }
        }
    }
}

$report = [ordered]@{
    schema = 'navlyn.routing-skill-install-test.v1'
    status = $(if ($null -eq $failure) { 'passed' } else { 'failed' })
    releaseVersion = $ReleaseVersion
    layouts = @($results)
    cleanup = $(if (Test-Path -LiteralPath $tempRoot) { 'failed' } else { 'passed' })
    failure = $failure
}
$json = $report | ConvertTo-Json -Depth 8
try {
    $stream = [System.IO.File]::Open($reportPath, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    try {
        $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes($json + "`n")
        $stream.Write($bytes, 0, $bytes.Length)
    } finally { $stream.Dispose() }
} catch {
    [Console]::Error.WriteLine('Unable to write a new report in the ignored evidence directory.')
    exit 1
}
Write-Output $json
if ($null -ne $failure) { [Console]::Error.WriteLine("Routing skill lifecycle test failed: $failure"); exit 1 }
exit 0

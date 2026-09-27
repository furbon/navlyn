[CmdletBinding()]
param(
    [switch]$Acquire,
    [switch]$RequireResolved,
    [string]$FeedPath,
    [int]$TimeoutSeconds = 180
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$evidenceRoot = Join-Path $repoRoot 'artifacts/v0.8.0-release-20260927-1205c239/p1-ref-lib'
$markerValue = 'P1-REAL-REFLIB public NuGet package and first-failure evidence; task-owned ignored data.'
$markerPath = Join-Path $evidenceRoot '.owner'
$packageName = 'microsoft.data.sqlclient.5.2.2.nupkg'
$packageHash = '5c8e96027a76b9928e0dbdd8f6a2ceca865e5ee1e5b4b6b6685d3dec708536b8'
$referenceEntry = 'ref/net8.0/Microsoft.Data.SqlClient.dll'
$implementationEntry = 'lib/net8.0/Microsoft.Data.SqlClient.dll'
$runtimeEntry = 'runtimes/win/lib/net8.0/Microsoft.Data.SqlClient.dll'
$runtimeRid = 'win-x64'
$cliPath = Join-Path $repoRoot 'navlyn/bin/Debug/net10.0/navlyn.dll'

if ($TimeoutSeconds -lt 30 -or $TimeoutSeconds -gt 300) { throw 'TimeoutSeconds must be between 30 and 300.' }
New-Item -ItemType Directory -Force -Path $evidenceRoot | Out-Null
if (Test-Path -LiteralPath $markerPath) {
    if ((Get-Content -LiteralPath $markerPath -Raw).Trim() -ne $markerValue) { throw 'Public ref/lib evidence owner marker mismatch.' }
}
elseif ((Get-ChildItem -LiteralPath $evidenceRoot -Force | Measure-Object).Count -gt 0) {
    throw 'Refusing to claim a non-empty public ref/lib evidence directory without its owner marker.'
}
else { Set-Content -LiteralPath $markerPath -Value $markerValue }

$packagePath = Join-Path $evidenceRoot $packageName
if ($FeedPath) {
    $feedPackage = Join-Path ([System.IO.Path]::GetFullPath($FeedPath)) $packageName
    if (Test-Path -LiteralPath $feedPackage) { $packagePath = $feedPackage }
}
if (!(Test-Path -LiteralPath $packagePath)) {
    $cachePath = Join-Path $env:USERPROFILE ".nuget/packages/microsoft.data.sqlclient/5.2.2/$packageName"
    if (Test-Path -LiteralPath $cachePath) { Copy-Item -LiteralPath $cachePath -Destination $packagePath }
    elseif ($Acquire) { Invoke-WebRequest -Uri "https://api.nuget.org/v3-flatcontainer/microsoft.data.sqlclient/5.2.2/$packageName" -OutFile $packagePath }
    else { throw "Missing $packageName. Supply -FeedPath, populate the NuGet cache, or pass -Acquire." }
}
$actualPackageHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualPackageHash -ne $packageHash) { throw "Pinned package SHA-256 mismatch: $actualPackageHash" }

Add-Type -AssemblyName System.IO.Compression
$zip = [System.IO.Compression.ZipFile]::OpenRead($packagePath)
try {
    $assetHashes = [ordered]@{}
    foreach ($entryName in @($referenceEntry, $implementationEntry, $runtimeEntry, 'runtimes/unix/lib/net8.0/Microsoft.Data.SqlClient.dll')) {
        $entry = $zip.GetEntry($entryName)
        if ($null -eq $entry) { throw "Pinned package is missing $entryName" }
        $stream = $entry.Open()
        try { $assetHashes[$entryName] = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() }
        finally { $stream.Dispose() }
    }
}
finally { $zip.Dispose() }

$runId = [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$runRoot = Join-Path $evidenceRoot "oracle-runs/$runId"
New-Item -ItemType Directory -Force -Path $runRoot | Out-Null
$feed = Join-Path $runRoot 'feed'
New-Item -ItemType Directory -Force -Path $feed | Out-Null
Copy-Item -LiteralPath $packagePath -Destination (Join-Path $feed $packageName)
if (!(Test-Path -LiteralPath $cliPath)) { throw "Missing built Navlyn CLI: $cliPath" }
$cliHash = (Get-FileHash -LiteralPath $cliPath -Algorithm SHA256).Hash.ToLowerInvariant()

function Invoke-Bounded {
    param([string]$FileName, [string[]]$Arguments, [string]$Directory, [int]$Seconds, [string]$PackageHome)
    $start = [System.Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $FileName
    $start.WorkingDirectory = $Directory
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.UseShellExecute = $false
    $start.Environment['NUGET_PACKAGES'] = $PackageHome
    foreach ($arg in $Arguments) { [void]$start.ArgumentList.Add($arg) }
    $process = [System.Diagnostics.Process]::Start($start)
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $timedOut = !$process.WaitForExit($Seconds * 1000)
    if ($timedOut) { try { $process.Kill($true) } catch { }; $process.WaitForExit() }
    return [pscustomobject]@{ exitCode = if ($timedOut) { -1 } else { $process.ExitCode }; timedOut = $timedOut; stdout = $stdoutTask.GetAwaiter().GetResult(); stderr = $stderrTask.GetAwaiter().GetResult() }
}

function Copy-Tree {
    param([string]$Source, [string]$Destination)
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    foreach ($file in Get-ChildItem -LiteralPath $Source -File) { Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $Destination $file.Name) }
    foreach ($directory in Get-ChildItem -LiteralPath $Source -Directory) { Copy-Tree $directory.FullName (Join-Path $Destination $directory.Name) }
}

function New-Fixture {
    param([string]$Name)
    $root = Join-Path $runRoot $Name
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    $project = Join-Path $root 'PublicRefLibOracle.csproj'
    $source = Join-Path $root 'Program.cs'
    $nuget = Join-Path $root 'NuGet.Config'
    $packageHome = Join-Path $root 'packages'
    $escapedFeed = [System.Security.SecurityElement]::Escape($feed)
    $projectXml = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><RuntimeIdentifier>win-x64</RuntimeIdentifier><OutputType>Exe</OutputType><Nullable>enable</Nullable></PropertyGroup><ItemGroup><PackageReference Include="Microsoft.Data.SqlClient" Version="5.2.2" /></ItemGroup></Project>'
    $nugetXml = "<configuration><packageSources><clear /><add key=`"pinned-feed`" value=`"$escapedFeed`" /><add key=`"nuget.org`" value=`"https://api.nuget.org/v3/index.json`" /></packageSources></configuration>"
    $program = "using Microsoft.Data.SqlClient;`r`nSqlConnection connection = new();`r`nconnection.Open();`r`n"
    [System.IO.File]::WriteAllText($project, $projectXml, [System.Text.UTF8Encoding]::new($true))
    [System.IO.File]::WriteAllText($source, $program, [System.Text.UTF8Encoding]::new($true))
    [System.IO.File]::WriteAllText($nuget, $nugetXml, [System.Text.UTF8Encoding]::new($true))
    $restore = Invoke-Bounded 'dotnet' @('restore', $project, '--configfile', $nuget, '--packages', $packageHome, '--runtime', $runtimeRid, '-p:NuGetAudit=false') $root $TimeoutSeconds $packageHome
    if ($restore.exitCode -ne 0) { throw "Restore failed in $Name (exit $($restore.exitCode)): $($restore.stderr) $($restore.stdout)" }
    $assetsPath = Join-Path $root 'obj/project.assets.json'
    $assetsBytes = [System.IO.File]::ReadAllBytes($assetsPath)
    $assetsHash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($assetsBytes)).ToLowerInvariant()
    $assetsCopy = Join-Path $runRoot "$Name.project.assets.original.json"
    [System.IO.File]::WriteAllBytes($assetsCopy, $assetsBytes)
    $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json -Depth 100
    $targetNames = @($assets.targets.PSObject.Properties.Name)
    $targetName = $targetNames | Where-Object { $_ -like "net10.0/$runtimeRid*" } | Select-Object -First 1
    $runtimeTarget = [string]$targetName
    if (!$runtimeTarget) { throw "Restore did not produce an explicit $runtimeRid target in $Name." }
    $libraryKey = 'Microsoft.Data.SqlClient/5.2.2'
    $libraryTarget = $assets.targets.$targetName.$libraryKey
    if ($null -eq $libraryTarget) { throw "project.assets.json lacks Microsoft.Data.SqlClient target in $Name." }
    $selectedRuntime = @()
    if ($null -ne $libraryTarget.PSObject.Properties['runtime']) {
        $selectedRuntime = @($libraryTarget.runtime.PSObject.Properties | ForEach-Object { [pscustomobject]@{ path = $_.Name } })
    }
    $selectedRuntimeTargets = @()
    if ($null -ne $libraryTarget.PSObject.Properties['runtimeTargets']) {
        $selectedRuntimeTargets = @($libraryTarget.runtimeTargets.PSObject.Properties | ForEach-Object { [pscustomobject]@{ path = $_.Name; rid = $_.Value.rid; assetType = $_.Value.assetType } })
    }
    $build = Invoke-Bounded 'dotnet' @('build', $project, '--no-restore', '--runtime', $runtimeRid, '-p:NuGetAudit=false') $root $TimeoutSeconds $packageHome
    if ($build.exitCode -ne 0) { throw "Build failed in $Name (exit $($build.exitCode)): $($build.stderr) $($build.stdout)" }
    return [pscustomobject]@{ root = $root; source = $source; project = $project; packageHome = $packageHome; assetsPath = $assetsPath; assetsHash = $assetsHash; assetsCopy = $assetsCopy; runtimeTarget = $runtimeTarget; selectedRuntime = $selectedRuntime; selectedRuntimeTargets = $selectedRuntimeTargets; buildExitCode = $build.exitCode }
}

function Invoke-OpenOracle {
    param($Fixture)
    $lines = Get-Content -LiteralPath $Fixture.source
    $line = [Array]::FindIndex([string[]]$lines, [Predicate[string]] { param($value) $value.Contains('connection.Open()', [StringComparison]::Ordinal) }) + 1
    $column = $lines[$line - 1].IndexOf('Open', [StringComparison]::Ordinal) + 1
    return Invoke-Bounded 'dotnet' @($cliPath, 'read', '--workspace', $Fixture.project, '--file', $Fixture.source, '--line', "$line", '--column', "$column", '--view', 'body', '--external-source', 'decompiled') $Fixture.root $TimeoutSeconds $Fixture.packageHome
}

$exact = New-Fixture 'exact'
$exactResult = Invoke-OpenOracle $exact
$exactPass = $exactResult.exitCode -ne 0 -and $exactResult.stderr.Contains('NAVLYN1402') -and [string]::IsNullOrWhiteSpace($exactResult.stdout)
$exactOutputPass = $false
$exactOutputErrors = @()
if ($exactResult.exitCode -eq 0) {
    try {
        $read = $exactResult.stdout | ConvertFrom-Json -Depth 100
        if ($read.sourceOrigin -ne 'decompiled') { $exactOutputErrors += 'source-origin' }
        if ($read.externalAssembly.targetFramework -ne 'net10.0') { $exactOutputErrors += 'target-framework' }
        if ($read.externalAssembly.selectedAssembly -ne 'implementation') { $exactOutputErrors += 'selected-assembly' }
        if ($read.symbol.name -ne 'Open') { $exactOutputErrors += 'member-name' }
        if ($read.symbol.facts.documentationCommentId -cne 'M:Microsoft.Data.SqlClient.SqlConnection.Open') { $exactOutputErrors += 'documentation-comment-id' }
        if ($read.externalAssembly.referenceSha256 -ine $assetHashes[$referenceEntry]) { $exactOutputErrors += 'reference-hash' }
        if ($read.externalAssembly.implementationSha256 -ine $assetHashes[$runtimeEntry]) { $exactOutputErrors += 'rid-implementation-hash' }
        if ($read.slices.Count -ne 1) { $exactOutputErrors += 'slice-count' }
        else {
            $slice = $read.slices[0]
            if ($slice.origin -ne 'decompiled' -or $slice.editable -ne $false) { $exactOutputErrors += 'slice-provenance' }
            if (!(($slice.lines -join "`n").Contains('Open(SqlConnectionOverrides.None)'))) { $exactOutputErrors += 'body-marker' }
        }
        $exactOutputPass = $exactOutputErrors.Count -eq 0
    }
    catch { $exactOutputErrors += "invalid-json: $($_.Exception.Message)" }
}
$exactEvidence = [ordered]@{
    case = 'public-ref-lib-runtime-first-failure'
    package = 'Microsoft.Data.SqlClient'; version = '5.2.2'; nupkgSha256 = $actualPackageHash; packageEntriesSha256 = $assetHashes
    cliSha256 = $cliHash; runtimeIdentifier = $runtimeRid; runtimeTarget = $exact.runtimeTarget; selectedRuntime = $exact.selectedRuntime; selectedRuntimeTargets = $exact.selectedRuntimeTargets
    projectAssetsSha256 = $exact.assetsHash; originalProjectAssets = $exact.assetsCopy; exitCode = $exactResult.exitCode
    stdout = $exactResult.stdout; stderr = $exactResult.stderr; expectedDiagnostic = 'NAVLYN1402'; successOutputErrors = $exactOutputErrors
    passed = ($exactPass -or $exactOutputPass); behavior = if ($exactPass) { 'expected-first-failure' } elseif ($exactOutputPass) { 'resolved' } else { 'unexpected' }
}
$exactEvidencePath = Join-Path $runRoot 'exact-first-failure.json'
$exactEvidence | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $exactEvidencePath -Encoding utf8

$ambiguous = New-Fixture 'ambiguous'
$ambiguousAssets = Get-Content -LiteralPath $ambiguous.assetsPath -Raw | ConvertFrom-Json -Depth 100
$packageRelativePath = $ambiguousAssets.libraries.'Microsoft.Data.SqlClient/5.2.2'.path
$originalPackage = Join-Path $ambiguous.packageHome $packageRelativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
$duplicateRoot = Join-Path $ambiguous.root 'duplicate-package-root'
$duplicatePackage = Join-Path $duplicateRoot $packageRelativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
Copy-Tree $originalPackage $duplicatePackage
$ambiguousAssets.packageFolders | Add-Member -NotePropertyName $duplicateRoot -NotePropertyValue ([pscustomobject]@{})
$ambiguousTargetName = @($ambiguousAssets.targets.PSObject.Properties.Name | Where-Object { $_ -like "net10.0/$runtimeRid*" })[0]
    $ambiguousAssets.targets.$ambiguousTargetName.'Microsoft.Data.SqlClient/5.2.2'.PSObject.Properties.Remove('runtime')
    $ambiguousAssets.targets.$ambiguousTargetName.'Microsoft.Data.SqlClient/5.2.2'.PSObject.Properties.Remove('runtimeTargets')
$mutatedBytes = [System.Text.Encoding]::UTF8.GetBytes(($ambiguousAssets | ConvertTo-Json -Depth 100))
[System.IO.File]::WriteAllBytes($ambiguous.assetsPath, $mutatedBytes)
$ambiguousResult = Invoke-OpenOracle $ambiguous
$ambiguousPass = $ambiguousResult.exitCode -ne 0 -and $ambiguousResult.stderr.Contains('NAVLYN1404') -and [string]::IsNullOrWhiteSpace($ambiguousResult.stdout)
$ambiguityEvidence = [ordered]@{
    case = 'duplicate-package-roots-ambiguity'
    package = 'Microsoft.Data.SqlClient'; version = '5.2.2'; nupkgSha256 = $actualPackageHash; runtimeIdentifier = $runtimeRid
    originalProjectAssetsSha256 = $ambiguous.assetsHash; originalProjectAssets = $ambiguous.assetsCopy
    mutatedProjectAssetsSha256 = (Get-FileHash -LiteralPath $ambiguous.assetsPath -Algorithm SHA256).Hash.ToLowerInvariant()
    exitCode = $ambiguousResult.exitCode; stdout = $ambiguousResult.stdout; stderr = $ambiguousResult.stderr
    expectedDiagnostic = 'NAVLYN1404'; passed = $ambiguousPass
}
$ambiguityPath = Join-Path $runRoot 'ambiguity-result.json'
$ambiguityEvidence | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $ambiguityPath -Encoding utf8

$unsafe = New-Fixture 'unsafe-path'
$unsafeAssets = Get-Content -LiteralPath $unsafe.assetsPath -Raw | ConvertFrom-Json -Depth 100
$unsafePackageRelative = $unsafeAssets.libraries.'Microsoft.Data.SqlClient/5.2.2'.path
$unsafePackageRoot = Join-Path $unsafe.packageHome $unsafePackageRelative.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
$outsideDll = Join-Path $unsafe.root 'outside.dll'
Copy-Item -LiteralPath (Join-Path $unsafePackageRoot $runtimeEntry.Replace('/', [System.IO.Path]::DirectorySeparatorChar)) -Destination $outsideDll
$escapePath = [System.IO.Path]::GetRelativePath($unsafePackageRoot, $outsideDll).Replace('\', '/')
if (!$escapePath.StartsWith('../', [StringComparison]::Ordinal)) { throw 'Unsafe fixture did not escape the package root.' }
$unsafeRuntime = $unsafeAssets.targets.$($unsafe.runtimeTarget).'Microsoft.Data.SqlClient/5.2.2'.runtime
foreach ($property in @($unsafeRuntime.PSObject.Properties.Name)) { $unsafeRuntime.PSObject.Properties.Remove($property) }
$unsafeRuntime | Add-Member -NotePropertyName $escapePath -NotePropertyValue ([pscustomobject]@{})
[System.IO.File]::WriteAllText($unsafe.assetsPath, ($unsafeAssets | ConvertTo-Json -Depth 100))
$unsafeResult = Invoke-OpenOracle $unsafe
$unsafePass = $unsafeResult.exitCode -ne 0 -and $unsafeResult.stderr.Contains('NAVLYN1402') -and [string]::IsNullOrWhiteSpace($unsafeResult.stdout)
$unsafePath = Join-Path $runRoot 'unsafe-path-result.json'
[ordered]@{ case = 'package-path-escape'; originalProjectAssetsSha256 = $unsafe.assetsHash; originalProjectAssets = $unsafe.assetsCopy; escapedRuntimeAsset = $escapePath; exitCode = $unsafeResult.exitCode; stdout = $unsafeResult.stdout; stderr = $unsafeResult.stderr; passed = $unsafePass } |
    ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $unsafePath -Encoding utf8

$summary = [ordered]@{ package = 'Microsoft.Data.SqlClient/5.2.2'; nupkgSha256 = $actualPackageHash; packageEntriesSha256 = $assetHashes; runtimeIdentifier = $runtimeRid; exactFailure = $exactPass; ambiguityFailure = $ambiguousPass; unsafePathFailure = $unsafePass; exactEvidence = $exactEvidencePath; ambiguityEvidence = $ambiguityPath; unsafePathEvidence = $unsafePath }
$summary | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $runRoot 'summary.json') -Encoding utf8
Write-Host "Exact ref/lib oracle: $(if ($exactPass) { 'expected NAVLYN1402' } elseif ($exactOutputPass) { 'resolved contract pass' } else { 'unexpected result' })"
Write-Host "Ambiguity oracle: $(if ($ambiguousPass) { 'expected NAVLYN1404' } else { 'unexpected result' })"
Write-Host "Unsafe path oracle: $(if ($unsafePass) { 'expected NAVLYN1402' } else { 'unexpected result' })"
Write-Host "Evidence: $runRoot"
if (($RequireResolved -and !$exactOutputPass) -or (!$RequireResolved -and !$exactPass -and !$exactOutputPass) -or !$ambiguousPass -or !$unsafePass) { exit 1 }

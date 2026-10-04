[CmdletBinding()]
param(
    [switch]$NoBuild,
    [switch]$Acquire,
    [string[]]$CaseIds = @(),
    [string]$Output = 'artifacts/external-member-corpus/report.json',
    [int]$TimeoutSeconds = 60
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$CorpusRoot = Join-Path $RepoRoot 'tests/corpora/ExternalMemberCorpus'
$ArtifactRoot = Join-Path $RepoRoot 'artifacts/external-member-corpus'
$Feed = Join-Path $ArtifactRoot 'feed'
$PackageRoot = Join-Path $ArtifactRoot 'packages'
$OutputPath = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $Output))
$NavlynDll = Join-Path $RepoRoot 'navlyn/bin/Debug/net10.0/navlyn.dll'
$McpDll = Join-Path $RepoRoot 'navlyn.Mcp/bin/Debug/net10.0/navlyn.Mcp.dll'
$Packages = @(Get-Content -LiteralPath (Join-Path $CorpusRoot 'packages.json') -Raw | ConvertFrom-Json).packages
$Cases = @(Get-Content -LiteralPath (Join-Path $CorpusRoot 'scenarios.json') -Raw | ConvertFrom-Json).cases
if ($CaseIds.Count) {
    $unknown = @($CaseIds | Where-Object { $_ -notin $Cases.id })
    if ($unknown.Count) { throw "Unknown corpus cases: $($unknown -join ', ')" }
    $Cases = @($Cases | Where-Object { $_.id -in $CaseIds })
}
$Utf8Bom = [System.Text.UTF8Encoding]::new($true)

if ($TimeoutSeconds -lt 15 -or $TimeoutSeconds -gt 300) {
    throw 'TimeoutSeconds must be between 15 and 300.'
}
if (!$OutputPath.StartsWith($ArtifactRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Output must be inside artifacts/external-member-corpus.'
}
New-Item -ItemType Directory -Force -Path $Feed, $PackageRoot, (Split-Path -Parent $OutputPath) | Out-Null
Set-Content -LiteralPath (Join-Path $ArtifactRoot '.owner') -Value 'Navlyn external member corpus runner; disposable local evidence.'

function Invoke-Bounded {
    param([string]$FileName, [string[]]$Arguments, [string]$Directory, [int]$Seconds)

    $start = [System.Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $FileName
    $start.WorkingDirectory = $Directory
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.UseShellExecute = $false
    $start.Environment['NUGET_PACKAGES'] = $PackageRoot
    $start.Environment['NUGET_AUDIT_MODE'] = 'direct'
    foreach ($argument in $Arguments) { [void]$start.ArgumentList.Add($argument) }
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $process = [System.Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $timedOut = !$process.WaitForExit($Seconds * 1000)
    if ($timedOut) {
        try { $process.Kill($true) } catch { }
        $process.WaitForExit()
    }
    $watch.Stop()
    return [pscustomobject]@{
        exitCode = if ($timedOut) { -1 } else { $process.ExitCode }
        timedOut = $timedOut
        elapsedMs = [int]$watch.ElapsedMilliseconds
        stdout = $stdout.GetAwaiter().GetResult()
        stderr = $stderr.GetAwaiter().GetResult()
    }
}

function Assert-Succeeded {
    param([string]$Label, $Result)
    if ($Result.exitCode -ne 0) {
        throw "$Label failed (exit=$($Result.exitCode), timeout=$($Result.timedOut)): $($Result.stderr) $($Result.stdout)"
    }
}

function Get-CorpusHash {
    $paths = @(Get-ChildItem -LiteralPath $CorpusRoot -Recurse -File | Where-Object {
        $_.FullName -notmatch '[\\/](obj|bin|Generated|lib)[\\/]'
    } | Sort-Object FullName)
    $items = foreach ($path in $paths) {
        "$($path.FullName.Substring($CorpusRoot.Length + 1).Replace('\', '/')):$((Get-FileHash -LiteralPath $path.FullName -Algorithm SHA256).Hash)"
    }
    $bytes = [System.Text.Encoding]::UTF8.GetBytes(($items -join "`n"))
    return [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

$BeforeHash = Get-CorpusHash
$PackageEvidence = @()
foreach ($package in $Packages) {
    $lower = $package.id.ToLowerInvariant()
    $archiveName = "$lower.$($package.version).nupkg"
    $archive = Join-Path $Feed $archiveName
    if (!(Test-Path -LiteralPath $archive)) {
        $cacheArchive = Join-Path $env:USERPROFILE ".nuget/packages/$lower/$($package.version)/$archiveName"
        if (Test-Path -LiteralPath $cacheArchive) {
            Copy-Item -LiteralPath $cacheArchive -Destination $archive
        }
        elseif ($Acquire) {
            $url = "https://api.nuget.org/v3-flatcontainer/$lower/$($package.version)/$archiveName"
            Invoke-WebRequest -Uri $url -OutFile $archive
        }
        else {
            throw "Missing $archiveName. Populate the local NuGet cache or rerun with -Acquire."
        }
    }
    $actual = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $package.sha256) { throw "Package hash mismatch: $archiveName" }
    $PackageEvidence += [pscustomobject]@{ id = $package.id; version = $package.version; sha256 = $actual }
}

$DirectLib = Join-Path $CorpusRoot 'Direct/lib'
New-Item -ItemType Directory -Force -Path $DirectLib | Out-Null
$NewtonsoftArchive = Join-Path $Feed 'newtonsoft.json.13.0.3.nupkg'
$DirectDll = Join-Path $DirectLib 'Newtonsoft.Json.dll'
Add-Type -AssemblyName System.IO.Compression
$zip = [System.IO.Compression.ZipFile]::OpenRead($NewtonsoftArchive)
try {
    $entry = $zip.GetEntry('lib/net6.0/Newtonsoft.Json.dll')
    if ($null -eq $entry) { throw 'Pinned Newtonsoft.Json implementation asset is absent.' }
    $source = $entry.Open()
    try {
        $destination = [System.IO.File]::Create($DirectDll)
        try { $source.CopyTo($destination) } finally { $destination.Dispose() }
    }
    finally { $source.Dispose() }
}
finally { $zip.Dispose() }

$GeneratedRoot = Join-Path $CorpusRoot 'Scale/Generated'
New-Item -ItemType Directory -Force -Path $GeneratedRoot | Out-Null
for ($number = 0; $number -lt 120; $number++) {
    $name = 'Shard{0:d3}' -f $number
    $source = @"
using Microsoft.Extensions.Primitives;

namespace ExternalMemberCorpus.Scale.Generated;

public sealed class $name
{
    public bool Check(StringValues value) => StringValues.IsNullOrEmpty(value);
}
"@
    [System.IO.File]::WriteAllText((Join-Path $GeneratedRoot "$name.cs"), ($source -replace "(?<!`r)`n", "`r`n") + "`r`n", $Utf8Bom)
}

if (!$NoBuild) {
    $build = Invoke-Bounded 'dotnet' @('build', (Join-Path $RepoRoot 'navlyn.slnx'), '--no-restore', '-p:NuGetAudit=false') $RepoRoot 300
    Assert-Succeeded 'Navlyn build' $build
}
if (!(Test-Path -LiteralPath $NavlynDll)) { throw "Missing Navlyn build: $NavlynDll" }
if (!(Test-Path -LiteralPath $McpDll)) { throw "Missing Navlyn MCP build: $McpDll" }

$ProjectPaths = @($Cases.project | Sort-Object -Unique)
$BuildEvidence = @()
foreach ($relative in $ProjectPaths) {
    $project = Join-Path $CorpusRoot $relative
    $restore = Invoke-Bounded 'dotnet' @('restore', $project, '--source', $Feed, '--packages', $PackageRoot, '-p:NuGetAudit=false') $CorpusRoot 180
    Assert-Succeeded "Restore $relative" $restore
    $build = Invoke-Bounded 'dotnet' @('build', $project, '--no-restore', '-p:NuGetAudit=false') $CorpusRoot 180
    Assert-Succeeded "Build $relative" $build
    $BuildEvidence += [pscustomobject]@{ project = $relative; restoreMs = $restore.elapsedMs; buildMs = $build.elapsedMs }
}

$Findings = @()
$Results = @()
foreach ($case in $Cases) {
    $path = Join-Path $CorpusRoot $case.file
    $content = Get-Content -LiteralPath $path
    $matches = @($content | Where-Object { $_.Contains($case.needle) })
    if ($matches.Count -ne 1) { throw "Case needle must match one line: $($case.id)" }
    $line = [Array]::FindIndex([string[]]$content, [Predicate[string]] { param($value) $value.Contains($case.needle) }) + 1
    $anchor = if ($null -ne $case.PSObject.Properties['anchor']) { $case.anchor } else { $case.method }
    $anchorOffset = $case.needle.IndexOf($anchor, [StringComparison]::Ordinal)
    if ($anchorOffset -lt 0) { throw "Case anchor is absent: $($case.id)" }
    $column = $content[$line - 1].IndexOf($case.needle, [StringComparison]::Ordinal) + $anchorOffset + 1
    $arguments = @($NavlynDll, 'read', '--workspace', (Join-Path $CorpusRoot $case.project), '--file', $path, '--line', "$line", '--column', "$column", '--view', 'body', '--external-source', 'decompiled', '--max-lines', '160', '--budget-tokens', '8000')
    if ($case.projectName) { $arguments += @('--project', $case.projectName) }
    $call = Invoke-Bounded 'dotnet' $arguments $CorpusRoot $TimeoutSeconds
    $errors = [System.Collections.Generic.List[string]]::new()
    $symbolId = $null
    $actualHash = $null
    if ($call.exitCode -ne 0) {
        $errors.Add("read-exit-$($call.exitCode): $($call.stderr.Trim())")
    }
    else {
        try {
            $result = $call.stdout | ConvertFrom-Json -Depth 100
            if ($result.sourceOrigin -ne 'decompiled') { $errors.Add('source-origin') }
            if ($result.externalAssembly.targetFramework -ne $case.tfm) { $errors.Add('target-framework') }
            if ($result.externalAssembly.selectedAssembly -ne 'implementation') { $errors.Add('selected-assembly') }
            if ($result.symbol.name -ne $case.method) { $errors.Add('wrong-member-name') }
            $symbolId = $result.symbol.facts.documentationCommentId
            if ($symbolId -cne $case.documentationCommentId) { $errors.Add('wrong-overload') }
            if ($result.slices.Count -ne 1) { $errors.Add('slice-count') }
            else {
                $slice = $result.slices[0]
                if ($slice.origin -ne 'decompiled' -or $slice.editable -ne $false) { $errors.Add('slice-provenance') }
                if ($slice.path -notmatch '^navlyn-decompiled://[0-9a-fA-F]{64}/[0-9a-fA-F]{64}$') { $errors.Add('slice-id') }
                if (!(($slice.lines -join "`n").Contains($case.bodyContains))) { $errors.Add('body-marker') }
            }
            $package = @($Packages | Where-Object id -EQ $case.package)[0]
            $expectedPe = if ($case.id.StartsWith('direct-')) { $DirectDll } else { Join-Path $PackageRoot "$($package.id.ToLowerInvariant())/$($package.version)/$($case.asset)" }
            if (!(Test-Path -LiteralPath $expectedPe)) { $errors.Add('expected-pe-absent') }
            else {
                $expectedHash = (Get-FileHash -LiteralPath $expectedPe -Algorithm SHA256).Hash.ToLowerInvariant()
                $actualHash = $result.externalAssembly.implementationSha256
                if ($actualHash -ne $expectedHash) { $errors.Add('wrong-implementation-pe') }
                if ($result.externalAssembly.referenceSha256 -ne $expectedHash) { $errors.Add('wrong-reference-pe') }
            }
        }
        catch { $errors.Add("invalid-result: $($_.Exception.Message)") }
    }
    if ($call.elapsedMs -gt 25000) { $errors.Add('performance-budget-25s') }
    if ($errors.Count -gt 0) { $Findings += [pscustomobject]@{ case = $case.id; errors = @($errors) } }
    $Results += [pscustomobject]@{
        id = $case.id
        project = $case.project
        tfm = $case.tfm
        package = $case.package
        asset = $case.asset
        line = $line
        column = $column
        documentationCommentId = $symbolId
        implementationSha256 = $actualHash
        elapsedMs = $call.elapsedMs
        exitCode = $call.exitCode
        errors = @($errors)
    }
    Write-Host "$($case.id): $(if ($errors.Count -eq 0) { 'pass' } else { 'FAIL' }) ($($call.elapsedMs) ms)"
}

$BoundaryChecks = @()
$baseline = $Cases[0]
$baselineFile = Join-Path $CorpusRoot $baseline.file
$baselineLines = Get-Content -LiteralPath $baselineFile
$baselineLine = [Array]::FindIndex([string[]]$baselineLines, [Predicate[string]] { param($value) $value.Contains($baseline.needle) }) + 1
$baselineColumn = $baselineLines[$baselineLine - 1].IndexOf($baseline.needle, [StringComparison]::Ordinal) + $baseline.needle.IndexOf($baseline.method, [StringComparison]::Ordinal) + 1
$baseArguments = @($NavlynDll, 'read', '--workspace', (Join-Path $CorpusRoot $baseline.project), '--file', $baselineFile, '--line', "$baselineLine", '--column', "$baselineColumn")
$defaultRead = Invoke-Bounded 'dotnet' ($baseArguments + @('--view', 'body')) $CorpusRoot $TimeoutSeconds
$defaultPass = $false
if ($defaultRead.exitCode -eq 0) {
    $defaultJson = $defaultRead.stdout | ConvertFrom-Json -Depth 100
    $defaultPass = $defaultJson.slices.Count -eq 0 -and @($defaultJson.warnings) -contains 'metadata-only-symbol' -and $null -eq $defaultJson.PSObject.Properties['sourceOrigin']
}
$BoundaryChecks += [pscustomobject]@{ id = 'default-empty'; passed = $defaultPass; elapsedMs = $defaultRead.elapsedMs }
if (!$defaultPass) { $Findings += [pscustomobject]@{ case = 'default-empty'; errors = @($defaultRead.stderr) } }

$metadataRead = Invoke-Bounded 'dotnet' ($baseArguments + @('--view', 'declaration', '--external-source', 'metadata')) $CorpusRoot $TimeoutSeconds
$metadataPass = $false
if ($metadataRead.exitCode -eq 0) {
    $metadataJson = $metadataRead.stdout | ConvertFrom-Json -Depth 100
    $metadataPass = $metadataJson.sourceOrigin -eq 'metadata' -and $metadataJson.externalAssembly.selectedAssembly -eq 'reference' -and $metadataJson.symbol.facts.documentationCommentId -ceq $baseline.documentationCommentId -and $metadataJson.slices.Count -eq 1 -and $metadataJson.slices[0].origin -eq 'metadata' -and $metadataJson.slices[0].editable -eq $false
}
$BoundaryChecks += [pscustomobject]@{ id = 'metadata-reference'; passed = $metadataPass; elapsedMs = $metadataRead.elapsedMs }
if (!$metadataPass) { $Findings += [pscustomobject]@{ case = 'metadata-reference'; errors = @($metadataRead.stderr) } }

$typeColumn = $baselineLines[$baselineLine - 1].IndexOf($baseline.needle, [StringComparison]::Ordinal) + 1
$wrongPositionArguments = @($NavlynDll, 'read', '--workspace', (Join-Path $CorpusRoot $baseline.project), '--file', $baselineFile, '--line', "$baselineLine", '--column', "$typeColumn", '--view', 'body', '--external-source', 'decompiled')
$wrongPosition = Invoke-Bounded 'dotnet' $wrongPositionArguments $CorpusRoot $TimeoutSeconds
$wrongPositionPass = $wrongPosition.exitCode -ne 0 -and $wrongPosition.stderr.Contains('NAVLYN1403') -and [string]::IsNullOrWhiteSpace($wrongPosition.stdout)
$BoundaryChecks += [pscustomobject]@{ id = 'type-position-no-body'; passed = $wrongPositionPass; elapsedMs = $wrongPosition.elapsedMs }
if (!$wrongPositionPass) { $Findings += [pscustomobject]@{ case = 'type-position-no-body'; errors = @($wrongPosition.stderr) } }

$frameworkLine = [Array]::FindIndex([string[]]$baselineLines, [Predicate[string]] { param($value) $value.Contains('value.Trim()') }) + 1
$frameworkColumn = $baselineLines[$frameworkLine - 1].IndexOf('Trim(', [StringComparison]::Ordinal) + 1
$frameworkArguments = @($NavlynDll, 'read', '--workspace', (Join-Path $CorpusRoot $baseline.project), '--file', $baselineFile, '--line', "$frameworkLine", '--column', "$frameworkColumn", '--view', 'body', '--external-source', 'decompiled')
$frameworkRead = Invoke-Bounded 'dotnet' $frameworkArguments $CorpusRoot $TimeoutSeconds
$frameworkPass = $frameworkRead.exitCode -ne 0 -and $frameworkRead.stderr.Contains('NAVLYN1402') -and [string]::IsNullOrWhiteSpace($frameworkRead.stdout)
$BoundaryChecks += [pscustomobject]@{ id = 'framework-unavailable'; passed = $frameworkPass; elapsedMs = $frameworkRead.elapsedMs }
if (!$frameworkPass) { $Findings += [pscustomobject]@{ case = 'framework-unavailable'; errors = @($frameworkRead.stderr) } }

function Write-McpFrame {
    param([System.IO.StreamWriter]$Writer, $Payload)
    $Writer.WriteLine(($Payload | ConvertTo-Json -Depth 50 -Compress))
    $Writer.Flush()
}

function Read-McpFrame {
    param([System.IO.StreamReader]$Reader, [int]$Seconds)
    $task = $Reader.ReadLineAsync()
    if (!$task.Wait($Seconds * 1000)) { throw 'MCP response deadline exceeded.' }
    $line = $task.GetAwaiter().GetResult()
    if ($null -eq $line) { throw 'MCP server closed stdout.' }
    return $line | ConvertFrom-Json -Depth 100
}

$mcpPass = $false
$mcpError = $null
$mcpWatch = [System.Diagnostics.Stopwatch]::StartNew()
$serverStart = [System.Diagnostics.ProcessStartInfo]::new()
$serverStart.FileName = 'dotnet'
$serverStart.WorkingDirectory = $CorpusRoot
$serverStart.RedirectStandardInput = $true
$serverStart.RedirectStandardOutput = $true
$serverStart.RedirectStandardError = $true
$serverStart.UseShellExecute = $false
$serverStart.StandardInputEncoding = [System.Text.Encoding]::UTF8
$serverStart.StandardOutputEncoding = [System.Text.Encoding]::UTF8
$serverStart.Environment['NUGET_PACKAGES'] = $PackageRoot
foreach ($argument in @($McpDll, '--workspace', (Join-Path $CorpusRoot $baseline.project), '--working-directory', $CorpusRoot, '--timeout-ms', '60000')) {
    [void]$serverStart.ArgumentList.Add($argument)
}
$server = [System.Diagnostics.Process]::Start($serverStart)
$serverStderr = $server.StandardError.ReadToEndAsync()
try {
    Write-McpFrame $server.StandardInput @{ jsonrpc = '2.0'; id = 1; method = 'initialize'; params = @{ protocolVersion = '2025-06-18'; capabilities = @{}; clientInfo = @{ name = 'external-member-corpus'; version = '1.0' } } }
    $handshake = Read-McpFrame $server.StandardOutput $TimeoutSeconds
    if ($handshake.id -ne 1 -or $null -eq $handshake.result) { throw 'MCP initialize failed.' }
    Write-McpFrame $server.StandardInput @{ jsonrpc = '2.0'; method = 'notifications/initialized'; params = @{} }
    Write-McpFrame $server.StandardInput @{ jsonrpc = '2.0'; id = 2; method = 'tools/call'; params = @{ name = 'navlyn_read'; arguments = @{ file = $baselineFile; line = $baselineLine; column = $baselineColumn; view = 'body'; externalSource = 'decompiled' } } }
    $response = Read-McpFrame $server.StandardOutput $TimeoutSeconds
    if ($response.id -ne 2 -or $null -eq $response.result) { throw 'MCP read response was not correlated.' }
    $structured = $response.result.structuredContent
    if ($null -eq $structured -or $structured.ok -ne $true) { throw "MCP read returned an error: $($response | ConvertTo-Json -Depth 8 -Compress)" }
    $readResult = $structured.result
    $cliBaseline = @($Results | Where-Object id -EQ $baseline.id)[0]
    $mcpPass = $readResult.symbol.facts.documentationCommentId -ceq $baseline.documentationCommentId -and
        $readResult.sourceOrigin -eq 'decompiled' -and
        $readResult.externalAssembly.implementationSha256 -eq $cliBaseline.implementationSha256 -and
        $readResult.slices.Count -eq 1 -and
        (($readResult.slices[0].lines -join "`n").Contains($baseline.bodyContains))
    if (!$mcpPass) { $mcpError = 'MCP and CLI provenance or body differed.' }
}
catch { $mcpError = $_.Exception.Message }
finally {
    try { $server.StandardInput.Close() } catch { }
    if (!$server.WaitForExit(5000)) { try { $server.Kill($true) } catch { } }
    $mcpWatch.Stop()
    [void]$serverStderr.GetAwaiter().GetResult()
    $server.Dispose()
}
$BoundaryChecks += [pscustomobject]@{ id = 'real-package-mcp-cli-parity'; passed = $mcpPass; elapsedMs = [int]$mcpWatch.ElapsedMilliseconds }
if (!$mcpPass) { $Findings += [pscustomobject]@{ case = 'real-package-mcp-cli-parity'; errors = @($mcpError) } }

$AfterHash = Get-CorpusHash
if ($BeforeHash -ne $AfterHash) { $Findings += [pscustomobject]@{ case = 'ownership'; errors = @('corpus-source-changed') } }
$sdkVersion = (& dotnet --version).Trim()
$gitHead = (& git -C $RepoRoot rev-parse HEAD).Trim()
$navlynVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($NavlynDll).ProductVersion
$report = [ordered]@{
    schemaVersion = 3
    generatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    sdkVersion = $sdkVersion
    navlynVersion = $navlynVersion
    gitHead = $gitHead
    corpusSourceSha256 = $BeforeHash
    corpusSourceUnchanged = ($BeforeHash -eq $AfterHash)
    generatedSourceCount = 120
    packages = $PackageEvidence
    builds = $BuildEvidence
    cases = $Results
    boundaryChecks = $BoundaryChecks
    findings = $Findings
    passed = ($Findings.Count -eq 0)
}
$report | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $OutputPath -Encoding utf8
Write-Host "Report: $OutputPath"
if ($Findings.Count -gt 0) { exit 1 }

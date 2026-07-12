[CmdletBinding()]
param(
    [string[]]$Repositories = @(),
    [string]$Output = 'artifacts/external-adoption/external-adoption-report.json',
    [string]$CloneRoot = 'artifacts/external-adoption/repos',
    [switch]$NoBuild,
    [int]$CommandTimeoutSeconds = 180
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$ProjectPath = Join-Path $RepoRoot 'navlyn/navlyn.csproj'
$TargetFrameworkScript = Join-Path $RepoRoot 'scripts/lib/navlyn-target-framework.ps1'
. $TargetFrameworkScript

$ProcessPreviewLimit = 2000
$TargetFramework = Get-NavlynPreferredTargetFramework -ProjectPath $ProjectPath
$NavlynDll = Join-Path $RepoRoot "navlyn/bin/Debug/$TargetFramework/navlyn.dll"
$CloneRootPath = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $CloneRoot))
$OutputPath = if ([System.IO.Path]::IsPathRooted($Output)) {
    [System.IO.Path]::GetFullPath($Output)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $Output))
}

if ($Repositories.Count -eq 0) {
    throw 'Pass at least one Git repository URL with -Repositories.'
}

if (!$NoBuild) {
    dotnet build (Join-Path $RepoRoot 'navlyn.slnx')
    if ($LASTEXITCODE -ne 0) {
        throw 'dotnet build failed.'
    }
}

if (!(Test-Path -LiteralPath $NavlynDll)) {
    throw "Navlyn executable was not found: $NavlynDll"
}

New-Item -ItemType Directory -Force -Path $CloneRootPath | Out-Null

function Test-IsPathUnder {
    param(
        [string]$Path,
        [string]$Parent
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $fullParent = [System.IO.Path]::GetFullPath($Parent).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    return $fullPath.StartsWith($fullParent + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}

function Get-SafeName {
    param([string]$Url)

    $leaf = [System.IO.Path]::GetFileNameWithoutExtension($Url.TrimEnd('/'))
    if ([string]::IsNullOrWhiteSpace($leaf)) {
        $leaf = [Guid]::NewGuid().ToString('N')
    }

    return ($leaf -replace '[^A-Za-z0-9_.-]', '-')
}

function Invoke-TimedProcess {
    param(
        [string]$Name,
        [string]$FileName,
        [string[]]$Arguments,
        [string]$WorkingDirectory,
        [int]$TimeoutSeconds
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FileName
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.UseShellExecute = $false
    foreach ($argument in $Arguments) {
        [void]$startInfo.ArgumentList.Add($argument)
    }

    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $process = [System.Diagnostics.Process]::Start($startInfo)
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    if (!$process.WaitForExit($TimeoutSeconds * 1000)) {
        try {
            $process.Kill($true)
        }
        catch {
        }

        $watch.Stop()
        return [pscustomobject]@{
            name = $Name
            exitCode = $null
            timedOut = $true
            elapsedMs = [Math]::Round($watch.Elapsed.TotalMilliseconds, 3)
            stdout = ''
            stderr = "Timed out after $TimeoutSeconds second(s)."
        }
    }

    $process.WaitForExit()
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    $watch.Stop()

    [pscustomobject]@{
        name = $Name
        exitCode = $process.ExitCode
        timedOut = $false
        elapsedMs = [Math]::Round($watch.Elapsed.TotalMilliseconds, 3)
        stdout = $stdout
        stderr = $stderr
    }
}

function Get-TextPreview {
    param([string]$Text)

    if ([string]::IsNullOrEmpty($Text)) {
        return ''
    }

    if ($Text.Length -le $ProcessPreviewLimit) {
        return $Text
    }

    return $Text.Substring(0, $ProcessPreviewLimit)
}

function ConvertTo-ProcessSummary {
    param([object]$Result)

    [pscustomobject]@{
        exitCode = $Result.exitCode
        timedOut = $Result.timedOut
        elapsedMs = $Result.elapsedMs
        stdoutChars = $Result.stdout.Length
        stderrChars = $Result.stderr.Length
        stdoutPreview = Get-TextPreview -Text $Result.stdout
        stderrPreview = Get-TextPreview -Text $Result.stderr
    }
}

function Invoke-NavlynExternal {
    param(
        [string[]]$Arguments
    )

    $result = Invoke-TimedProcess -Name ($Arguments -join ' ') -FileName 'dotnet' -Arguments (@($NavlynDll) + $Arguments) -WorkingDirectory $RepoRoot -TimeoutSeconds $CommandTimeoutSeconds
    $json = $null
    $jsonValid = $false
    if (!$result.timedOut -and ![string]::IsNullOrWhiteSpace($result.stdout)) {
        try {
            $json = $result.stdout | ConvertFrom-Json -Depth 100
            $jsonValid = $true
        }
        catch {
            $jsonValid = $false
        }
    }

    [pscustomobject]@{
        exitCode = $result.exitCode
        timedOut = $result.timedOut
        elapsedMs = $result.elapsedMs
        stdoutChars = $result.stdout.Length
        stderrChars = $result.stderr.Length
        stdoutPreview = Get-TextPreview -Text $result.stdout
        stderrPreview = Get-TextPreview -Text $result.stderr
        jsonValid = $jsonValid
        json = $json
    }
}

function Test-IsGeneratedOrBuildPath {
    param([string]$Path)

    return $Path -match '[\\/](bin|obj|\.git)[\\/]'
}

function Find-WorkspacePath {
    param([string]$RepositoryPath)

    $patterns = @(
        'navlyn.workspace.json',
        '*.code-workspace',
        '*.slnx',
        '*.sln',
        '*.csproj',
        '*.vbproj'
    )

    foreach ($pattern in $patterns) {
        $candidate = Get-ChildItem -LiteralPath $RepositoryPath -Recurse -File -Filter $pattern |
            Where-Object { !(Test-IsGeneratedOrBuildPath -Path $_.FullName) } |
            Sort-Object @{ Expression = { $_.FullName.Substring($RepositoryPath.Length).Split([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar).Count } }, FullName |
            Select-Object -First 1

        if ($null -ne $candidate) {
            return $candidate.FullName
        }
    }

    return $null
}

function Get-PathPreference {
    param([string]$RelativePath)

    if ($RelativePath -match '(?i)(test|tests|benchmark|benchmarks|sample|samples)') {
        return 50
    }

    if ($RelativePath -match '(^|/)src(/|$)') {
        return 0
    }

    return 10
}

function Find-FirstTypeName {
    param([string]$RepositoryPath)

    $files = Get-ChildItem -LiteralPath $RepositoryPath -Recurse -File -Include *.cs,*.vb |
        Where-Object { !(Test-IsGeneratedOrBuildPath -Path $_.FullName) } |
        ForEach-Object {
            $relativePath = [System.IO.Path]::GetRelativePath($RepositoryPath, $_.FullName).Replace('\', '/')
            [pscustomobject]@{
                File = $_
                RelativePath = $relativePath
                Preference = Get-PathPreference -RelativePath $relativePath
            }
        } |
        Sort-Object Preference, RelativePath

    foreach ($entry in $files) {
        $lineNumber = 0
        foreach ($line in [System.IO.File]::ReadLines($entry.File.FullName)) {
            $lineNumber++
            if ($line -match '^\s*(?:(?:public|private|protected|internal|file|static|abstract|sealed|partial|readonly|unsafe|new)\s+)*(class|interface|struct|record)\s+(?:class\s+|struct\s+)?([A-Za-z_][A-Za-z0-9_]*)\b') {
                return [pscustomobject]@{
                    name = $Matches[2]
                    file = $entry.RelativePath
                    line = $lineNumber
                    column = $line.IndexOf($Matches[2], [StringComparison]::Ordinal) + 1
                    preference = $entry.Preference
                }
            }
        }
    }

    return $null
}

function Find-NearestProjectPath {
    param(
        [string]$RepositoryPath,
        [string]$SourceFilePath
    )

    $repositoryFullPath = [System.IO.Path]::GetFullPath($RepositoryPath)
    $directory = [System.IO.Path]::GetDirectoryName([System.IO.Path]::GetFullPath($SourceFilePath))
    while (![string]::IsNullOrWhiteSpace($directory) -and (Test-IsPathUnder -Path $directory -Parent $repositoryFullPath)) {
        $project = Get-ChildItem -LiteralPath $directory -File |
            Where-Object { $_.Extension -in @('.csproj', '.vbproj') } |
            Sort-Object FullName |
            Select-Object -First 1
        if ($null -ne $project) {
            return $project.FullName
        }

        $directory = [System.IO.Path]::GetDirectoryName($directory)
    }

    return $null
}

function Test-NavlynJsonSuccess {
    param([object]$Result)

    return $null -ne $Result -and $Result.jsonValid -and $Result.exitCode -eq 0
}

function Invoke-AdoptionAttempt {
    param(
        [string]$WorkspacePath,
        [string]$RepositoryPath,
        [object]$Type,
        [string]$Strategy,
        [bool]$IncludePrepareEdit = $true
    )

    $targetFile = Join-Path $RepositoryPath $Type.file
    $doctor = Invoke-NavlynExternal -Arguments @('doctor', '--workspace', $WorkspacePath)
    $target = Invoke-NavlynExternal -Arguments @('target', '--workspace', $WorkspacePath, '--file', $targetFile, '--line', [string]$Type.line, '--column', [string]$Type.column)
    $prepareEdit = $null
    if ($IncludePrepareEdit -and (Test-NavlynJsonSuccess -Result $target)) {
        $prepareEdit = Invoke-NavlynExternal -Arguments @('prepare-edit', '--workspace', $WorkspacePath, '--file', $targetFile, '--line', [string]$Type.line, '--column', [string]$Type.column, '--goal', 'modify', '--change-kind', 'behavior', '--budget-tokens', '2000', '--item-limit', '4', '--reference-limit', '8', '--test-limit', '5')
    }

    [pscustomobject]@{
        strategy = $Strategy
        workspacePath = $WorkspacePath
        doctor = $doctor
        target = $target
        prepareEdit = $prepareEdit
    }
}

$results = [System.Collections.Generic.List[object]]::new()
foreach ($repository in $Repositories) {
    $safeName = Get-SafeName -Url $repository
    $repoPath = Join-Path $CloneRootPath $safeName
    if (Test-Path -LiteralPath $repoPath) {
        if (!(Test-IsPathUnder -Path $repoPath -Parent $CloneRootPath)) {
            throw "Refusing to remove a path outside clone root: $repoPath"
        }

        Remove-Item -LiteralPath $repoPath -Recurse -Force
    }

    $clone = Invoke-TimedProcess -Name 'git clone' -FileName 'git' -Arguments @('clone', '--depth', '1', $repository, $repoPath) -WorkingDirectory $CloneRootPath -TimeoutSeconds $CommandTimeoutSeconds
    if ($clone.exitCode -ne 0 -or $clone.timedOut) {
        $results.Add([pscustomobject]@{
            repository = $repository
            status = 'clone-failed'
            clone = ConvertTo-ProcessSummary -Result $clone
        })
        continue
    }

    $restore = Invoke-TimedProcess -Name 'dotnet restore' -FileName 'dotnet' -Arguments @('restore') -WorkingDirectory $repoPath -TimeoutSeconds $CommandTimeoutSeconds
    $workspacePath = Find-WorkspacePath -RepositoryPath $repoPath
    $type = Find-FirstTypeName -RepositoryPath $repoPath
    $primaryAttempt = if ($null -eq $workspacePath -or $null -eq $type) {
        $null
    }
    else {
        $primaryWorkspaceExtension = [System.IO.Path]::GetExtension($workspacePath).ToLowerInvariant()
        $includePrimaryPrepare = $primaryWorkspaceExtension -in @('.csproj', '.vbproj')
        Invoke-AdoptionAttempt -WorkspacePath $workspacePath -RepositoryPath $repoPath -Type $type -Strategy 'primary-workspace' -IncludePrepareEdit $includePrimaryPrepare
    }

    $effectiveAttempt = $primaryAttempt
    $fallbackAttempt = $null
    if ($null -ne $type -and $null -ne $primaryAttempt -and !(Test-NavlynJsonSuccess -Result $primaryAttempt.prepareEdit)) {
        $projectWorkspacePath = Find-NearestProjectPath -RepositoryPath $repoPath -SourceFilePath (Join-Path $repoPath $type.file)
        if ($null -ne $projectWorkspacePath -and ![string]::Equals([System.IO.Path]::GetFullPath($projectWorkspacePath), [System.IO.Path]::GetFullPath($workspacePath), [StringComparison]::OrdinalIgnoreCase)) {
            $fallbackAttempt = Invoke-AdoptionAttempt -WorkspacePath $projectWorkspacePath -RepositoryPath $repoPath -Type $type -Strategy 'nearest-project-fallback'
            if ((Test-NavlynJsonSuccess -Result $fallbackAttempt.prepareEdit) -or
                (!(Test-NavlynJsonSuccess -Result $primaryAttempt.target) -and (Test-NavlynJsonSuccess -Result $fallbackAttempt.target))) {
                $effectiveAttempt = $fallbackAttempt
            }
        }
    }

    $doctor = if ($null -eq $effectiveAttempt) { $null } else { $effectiveAttempt.doctor }
    $target = if ($null -eq $effectiveAttempt) { $null } else { $effectiveAttempt.target }
    $prepareEdit = if ($null -eq $effectiveAttempt) { $null } else { $effectiveAttempt.prepareEdit }
    $doctorOk = $null -ne $doctor -and $doctor.jsonValid -and $doctor.json.PSObject.Properties.Name -contains 'ok' -and [bool]$doctor.json.ok
    $targetUsable = Test-NavlynJsonSuccess -Result $target
    $prepareUsable = Test-NavlynJsonSuccess -Result $prepareEdit
    $status = if ($doctorOk -and $targetUsable -and $prepareUsable) {
        'clean-success'
    }
    elseif ($targetUsable) {
        'degraded-target-success'
    }
    else {
        'no-go'
    }

    $results.Add([pscustomobject]@{
        repository = $repository
        status = $status
        clonePath = [System.IO.Path]::GetRelativePath($RepoRoot, $repoPath).Replace('\', '/')
        workspace = if ($null -eq $effectiveAttempt) { $null } else { [System.IO.Path]::GetRelativePath($RepoRoot, $effectiveAttempt.workspacePath).Replace('\', '/') }
        workspaceStrategy = if ($null -eq $effectiveAttempt) { $null } else { $effectiveAttempt.strategy }
        primaryWorkspace = if ($null -eq $workspacePath) { $null } else { [System.IO.Path]::GetRelativePath($RepoRoot, $workspacePath).Replace('\', '/') }
        fallbackWorkspace = if ($null -eq $fallbackAttempt) { $null } else { [System.IO.Path]::GetRelativePath($RepoRoot, $fallbackAttempt.workspacePath).Replace('\', '/') }
        restore = ConvertTo-ProcessSummary -Result $restore
        discoveredType = $type
        doctor = if ($null -eq $doctor) { $null } else { [pscustomobject]@{
            ok = $doctorOk
            exitCode = $doctor.exitCode
            timedOut = $doctor.timedOut
            jsonValid = $doctor.jsonValid
            elapsedMs = $doctor.elapsedMs
            stdoutChars = $doctor.stdoutChars
            stderrChars = $doctor.stderrChars
            stdoutPreview = $doctor.stdoutPreview
            stderrPreview = $doctor.stderrPreview
        }}
        target = if ($null -eq $target) { $null } else { [pscustomobject]@{
            exitCode = $target.exitCode
            timedOut = $target.timedOut
            jsonValid = $target.jsonValid
            confidence = if ($target.jsonValid -and $target.json.PSObject.Properties.Name -contains 'confidence') { [string]$target.json.confidence } else { $null }
            candidateIdPresent = $target.jsonValid -and $target.json.PSObject.Properties.Name -contains 'candidateId'
            elapsedMs = $target.elapsedMs
            stdoutChars = $target.stdoutChars
            stderrChars = $target.stderrChars
            stdoutPreview = $target.stdoutPreview
            stderrPreview = $target.stderrPreview
        }}
        prepareEdit = if ($null -eq $prepareEdit) { $null } else { [pscustomobject]@{
            exitCode = $prepareEdit.exitCode
            timedOut = $prepareEdit.timedOut
            jsonValid = $prepareEdit.jsonValid
            elapsedMs = $prepareEdit.elapsedMs
            stdoutChars = $prepareEdit.stdoutChars
            stderrChars = $prepareEdit.stderrChars
            stdoutPreview = $prepareEdit.stdoutPreview
            stderrPreview = $prepareEdit.stderrPreview
        }}
    })
}

$summary = [ordered]@{
    schemaVersion = 'navlyn.external-adoption-report.v1'
    generatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    repositoryCount = $results.Count
    cleanSuccess = @($results | Where-Object { $_.status -eq 'clean-success' }).Count
    degradedTargetSuccess = @($results | Where-Object { $_.status -eq 'degraded-target-success' }).Count
    noGo = @($results | Where-Object { $_.status -eq 'no-go' }).Count
    cloneFailed = @($results | Where-Object { $_.status -eq 'clone-failed' }).Count
    results = $results.ToArray()
}

$outputDirectory = [System.IO.Path]::GetDirectoryName($OutputPath)
if (![string]::IsNullOrWhiteSpace($outputDirectory)) {
    [System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
}

$summary | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $OutputPath -Encoding utf8
Write-Host "External adoption report: $OutputPath"

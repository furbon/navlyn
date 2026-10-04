[CmdletBinding()]
param()

. $PSScriptRoot/navlyn-target-framework.ps1

function Initialize-NavlynTestHarness {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,

        [switch]$ShowOutput
    )

    $script:NavlynTestRepoRoot = [System.IO.Path]::GetFullPath($RepoRoot)
    $script:NavlynTestSolutionPath = Join-Path $script:NavlynTestRepoRoot 'navlyn.slnx'
    $script:NavlynTestProjectPath = Join-Path $script:NavlynTestRepoRoot 'navlyn/navlyn.csproj'
    $script:NavlynTestProjectDir = Join-Path $script:NavlynTestRepoRoot 'navlyn'
    $script:NavlynTestShowOutput = $ShowOutput

    $targetFramework = Get-NavlynPreferredTargetFramework -ProjectPath $script:NavlynTestProjectPath
    $script:NavlynTestTargetFramework = $targetFramework
    $configuration = if ($env:NAVLYN_TEST_CONFIGURATION) { $env:NAVLYN_TEST_CONFIGURATION } else { 'Debug' }
    $script:NavlynTestDll = Join-Path $script:NavlynTestProjectDir "bin/$configuration/$targetFramework/navlyn.dll"
}

function Invoke-CheckedProcess {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [int]$ExpectedExitCode,

        [string]$WorkingDirectory = $script:NavlynTestRepoRoot,

        [string]$StandardInput = $null,

        [ValidateRange(1, 600)][int]$TimeoutSeconds = 60
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.RedirectStandardInput = $null -ne $StandardInput
    $startInfo.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $startInfo.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    $startInfo.UseShellExecute = $false
    foreach ($argument in $Arguments) { $startInfo.ArgumentList.Add($argument) }

    $deadline = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds($TimeoutSeconds))
    $process = $null
    try {
        $process = [System.Diagnostics.Process]::Start($startInfo)
        $stdoutTask = $process.StandardOutput.ReadToEndAsync($deadline.Token)
        $stderrTask = $process.StandardError.ReadToEndAsync($deadline.Token)
        if ($null -ne $StandardInput) {
            [void]$process.StandardInput.WriteAsync([MemoryExtensions]::AsMemory($StandardInput), $deadline.Token).GetAwaiter().GetResult()
            $process.StandardInput.Close()
        }
        [void]$process.WaitForExitAsync($deadline.Token).GetAwaiter().GetResult()
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        $exitCode = $process.ExitCode
    }
    catch {
        if ($deadline.IsCancellationRequested) {
            if ($null -ne $process -and !$process.HasExited) { $process.Kill($true); $process.WaitForExit() }
            $partialOut = if ($null -ne $process -and $stdoutTask.IsCompletedSuccessfully) { $stdoutTask.GetAwaiter().GetResult() } else { '' }
            $partialError = if ($null -ne $process -and $stderrTask.IsCompletedSuccessfully) { $stderrTask.GetAwaiter().GetResult() } else { '' }
            throw "$Name exceeded its $TimeoutSeconds second process deadline. Command: $FilePath $($Arguments -join ' ')`nstdout:`n$partialOut`nstderr:`n$partialError"
        }
        throw
    }
    finally {
        if ($null -ne $process) {
            if (!$process.HasExited) { $process.Kill($true); $process.WaitForExit() }
            $process.Dispose()
        }
        $deadline.Dispose()
    }

    if ($exitCode -ne $ExpectedExitCode) {
        throw @"
$Name failed with exit code $exitCode. Expected $ExpectedExitCode.
Command: $FilePath $($Arguments -join ' ')
stdout:
$stdout
stderr:
$stderr
"@
    }

    $result = [pscustomobject]@{
        Name = $Name
        ExitCode = $exitCode
        Stdout = $stdout
        Stderr = $stderr
    }

    if ($script:NavlynTestShowOutput) {
        Write-ProcessResult `
            -Name $Name `
            -FilePath $FilePath `
            -Arguments $Arguments `
            -Result $result
    }

    $result
}

function Write-ProcessResult {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [psobject]$Result
    )

    Write-Host ''
    Write-Host "[$Name]"
    Write-Host "command: $FilePath $($Arguments -join ' ')"
    Write-Host "exit: $($Result.ExitCode)"
    Write-Host 'stdout:'
    if ($Result.Stdout.Length -eq 0) {
        Write-Host '<empty>'
    }
    else {
        Write-Host $Result.Stdout.TrimEnd()
    }

    Write-Host 'stderr:'
    if ($Result.Stderr.Length -eq 0) {
        Write-Host '<empty>'
    }
    else {
        Write-Host $Result.Stderr.TrimEnd()
    }
}

function Invoke-Navlyn {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [int]$ExpectedExitCode,

        [string]$WorkingDirectory = $script:NavlynTestRepoRoot,

        [string]$StandardInput = $null
    )

    Invoke-CheckedProcess `
        -Name $Name `
        -FilePath 'dotnet' `
        -Arguments (@($script:NavlynTestDll) + $Arguments) `
        -ExpectedExitCode $ExpectedExitCode `
        -WorkingDirectory $WorkingDirectory `
        -StandardInput $StandardInput
}

function Assert-Contains {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Text,

        [Parameter(Mandatory = $true)]
        [string]$Expected
    )

    if ($Text.IndexOf($Expected, [StringComparison]::Ordinal) -lt 0) {
        throw "$Name did not contain expected text '$Expected'. Actual text: $Text"
    }
}

function Assert-Equal {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [AllowNull()]
        [object]$Actual,

        [AllowNull()]
        [object]$Expected
    )

    if ($Actual -ne $Expected) {
        throw "$Name expected '$Expected' but was '$Actual'."
    }
}

function Assert-Empty {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Text
    )

    if ($Text.Length -ne 0) {
        throw "$Name was expected to be empty. Actual text: $Text"
    }
}

function Assert-NavlynDllExists {
    if (!(Test-Path -LiteralPath $script:NavlynTestDll)) {
        throw "Navlyn executable was not found: $script:NavlynTestDll. Run without -NoBuild first."
    }
}

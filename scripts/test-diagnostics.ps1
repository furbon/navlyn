[CmdletBinding()]
param(
    [switch]$NoBuild,
    [switch]$ShowOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$SolutionPath = Join-Path $RepoRoot 'navlyn.slnx'
$ProjectPath = Join-Path $RepoRoot 'navlyn/navlyn.csproj'
$ProjectDir = Join-Path $RepoRoot 'navlyn'
$FixtureProjectPath = Join-Path $RepoRoot 'tests/fixtures/DiagnosticFixture/DiagnosticFixture.csproj'
$FixtureDisplayPath = 'tests/fixtures/DiagnosticFixture/BrokenCode.cs'
$GeneratedFixtureDisplayPath = 'tests/fixtures/DiagnosticFixture/GeneratedBroken.g.cs'
$FixtureProjectDisplayPath = 'tests/fixtures/DiagnosticFixture/DiagnosticFixture.csproj'
$TargetFrameworkScript = Join-Path $RepoRoot 'scripts/lib/navlyn-target-framework.ps1'

. $TargetFrameworkScript

$TargetFramework = Get-NavlynPreferredTargetFramework -ProjectPath $ProjectPath
$NavlynDll = Join-Path $ProjectDir "bin/Debug/$TargetFramework/navlyn.dll"

function Invoke-CheckedProcess {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [int]$ExpectedExitCode
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.WorkingDirectory = $RepoRoot
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $startInfo.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    $startInfo.UseShellExecute = $false
    $startInfo.Arguments = Join-ProcessArguments -Arguments $Arguments

    $process = [System.Diagnostics.Process]::Start($startInfo)
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    $exitCode = $process.ExitCode

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

    if ($ShowOutput) {
        Write-Host ''
        Write-Host "[$Name]"
        Write-Host "command: $FilePath $($Arguments -join ' ')"
        Write-Host "exit: $exitCode"
        Write-Host 'stdout:'
        if ($stdout.Length -eq 0) { Write-Host '<empty>' } else { Write-Host $stdout.TrimEnd() }
        Write-Host 'stderr:'
        if ($stderr.Length -eq 0) { Write-Host '<empty>' } else { Write-Host $stderr.TrimEnd() }
    }

    $result
}

function Join-ProcessArguments {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    ($Arguments | ForEach-Object { ConvertTo-ProcessArgument -Argument $_ }) -join ' '
}

function ConvertTo-ProcessArgument {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Argument
    )

    if ($Argument.Length -eq 0) {
        return '""'
    }

    if ($Argument.IndexOfAny([char[]]@(' ', "`t", '"')) -lt 0) {
        return $Argument
    }

    return '"' + $Argument.Replace('"', '\"') + '"'
}

function Invoke-Navlyn {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [int]$ExpectedExitCode
    )

    Invoke-CheckedProcess `
        -Name $Name `
        -FilePath 'dotnet' `
        -Arguments (@($NavlynDll) + $Arguments) `
        -ExpectedExitCode $ExpectedExitCode
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

Push-Location $RepoRoot
try {
    Write-Host 'Restoring diagnostic fixture...'
    Invoke-CheckedProcess `
        -Name 'dotnet restore diagnostic fixture' `
        -FilePath 'dotnet' `
        -Arguments @('restore', $FixtureProjectPath) `
        -ExpectedExitCode 0 | Out-Null

    if (!$NoBuild) {
        Write-Host 'Building navlyn...'
        Invoke-CheckedProcess `
            -Name 'dotnet build' `
            -FilePath 'dotnet' `
            -Arguments @('build', $SolutionPath) `
            -ExpectedExitCode 0 | Out-Null
    }

    if (!(Test-Path -LiteralPath $NavlynDll)) {
        throw "Navlyn executable was not found: $NavlynDll. Run without -NoBuild first."
    }

    Write-Host 'Running diagnostics checks...'

    $diagnostics = Invoke-Navlyn `
        -Name 'diagnostics fixture' `
        -Arguments @('diagnostics', '--workspace', $FixtureProjectPath) `
        -ExpectedExitCode 0

    $json = $diagnostics.Stdout | ConvertFrom-Json
    $missingTypeDiagnostics = @($json.diagnostics | Where-Object { $_.id -eq 'CS0246' })
    $generatedDiagnostics = @($missingTypeDiagnostics | Where-Object { $_.path -eq $GeneratedFixtureDisplayPath })
    Assert-Equal -Name 'diagnostics workspace' -Actual $json.workspace -Expected $FixtureProjectDisplayPath
    Assert-Equal -Name 'diagnostics kind' -Actual $json.kind -Expected 'project'
    Assert-Equal -Name 'diagnostics CS0246 count' -Actual $missingTypeDiagnostics.Count -Expected 4
    Assert-Equal -Name 'diagnostics generated CS0246 count' -Actual $generatedDiagnostics.Count -Expected 2
    Assert-Equal -Name 'diagnostics first severity' -Actual $missingTypeDiagnostics[0].severity -Expected 'Error'
    Assert-Equal -Name 'diagnostics first path' -Actual $missingTypeDiagnostics[0].path -Expected $FixtureDisplayPath
    Assert-Equal -Name 'diagnostics first line' -Actual $missingTypeDiagnostics[0].line -Expected 5
    Assert-Equal -Name 'diagnostics first column' -Actual $missingTypeDiagnostics[0].column -Expected 12
    Assert-Equal -Name 'diagnostics first end line' -Actual $missingTypeDiagnostics[0].endLine -Expected 5
    Assert-Equal -Name 'diagnostics first end column has span' -Actual ($missingTypeDiagnostics[0].endColumn -gt $missingTypeDiagnostics[0].column) -Expected $true
    Assert-Equal -Name 'diagnostics first project name' -Actual $missingTypeDiagnostics[0].project.name -Expected 'DiagnosticFixture'
    Assert-Equal -Name 'diagnostics first project path' -Actual $missingTypeDiagnostics[0].project.path -Expected $FixtureProjectDisplayPath
    Assert-Equal -Name 'diagnostics first project has no filter' -Actual ($missingTypeDiagnostics[0].project.PSObject.Properties.Name -contains 'filter') -Expected $false

    $excludeGeneratedDiagnostics = Invoke-Navlyn `
        -Name 'diagnostics exclude generated' `
        -Arguments @('diagnostics', '--workspace', $FixtureProjectPath, '--exclude-generated') `
        -ExpectedExitCode 0

    $excludeGeneratedJson = $excludeGeneratedDiagnostics.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'diagnostics exclude generated flag' -Actual $excludeGeneratedJson.excludeGenerated -Expected $true
    Assert-Equal -Name 'diagnostics exclude generated total' -Actual $excludeGeneratedJson.totalDiagnostics -Expected 2
    Assert-Equal -Name 'diagnostics exclude generated ids' -Actual @($excludeGeneratedJson.diagnostics | Where-Object { $_.id -eq 'CS0246' }).Count -Expected 2
    Assert-Equal -Name 'diagnostics exclude generated paths' -Actual @($excludeGeneratedJson.diagnostics | Where-Object { $_.path -eq $GeneratedFixtureDisplayPath }).Count -Expected 0

    $diagnosticsByProject = Invoke-Navlyn `
        -Name 'diagnostics project filter' `
        -Arguments @('diagnostics', '--workspace', $FixtureProjectPath, '--project', 'DiagnosticFixture') `
        -ExpectedExitCode 0

    $projectJson = $diagnosticsByProject.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'diagnostics project filter count' -Actual @($projectJson.projects).Count -Expected 1
    Assert-Equal -Name 'diagnostics project filter name' -Actual @($projectJson.projects)[0].name -Expected 'DiagnosticFixture'

    $diagnosticsFiltered = Invoke-Navlyn `
        -Name 'diagnostics severity id limit filters' `
        -Arguments @('diagnostics', '--workspace', $FixtureProjectPath, '--severity', 'Error', '--id', 'CS0246', '--limit', '1') `
        -ExpectedExitCode 0

    $filteredJson = $diagnosticsFiltered.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'diagnostics severity filter value' -Actual @($filteredJson.severities)[0] -Expected 'Error'
    Assert-Equal -Name 'diagnostics id filter value' -Actual @($filteredJson.ids)[0] -Expected 'CS0246'
    Assert-Equal -Name 'diagnostics limit value' -Actual $filteredJson.limit -Expected 1
    Assert-Equal -Name 'diagnostics filtered total before limit' -Actual $filteredJson.totalDiagnostics -Expected 4
    Assert-Equal -Name 'diagnostics filtered returned count' -Actual @($filteredJson.diagnostics).Count -Expected 1

    $symbolDiagnostics = Invoke-Navlyn `
        -Name 'symbol-diagnostics method scope' `
        -Arguments @('symbol-diagnostics', '--workspace', $FixtureProjectPath, '--file', $FixtureDisplayPath, '--line', '5', '--column', '24', '--limit', '10') `
        -ExpectedExitCode 0

    $symbolDiagnosticsJson = $symbolDiagnostics.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'symbol-diagnostics symbol name' -Actual $symbolDiagnosticsJson.symbol.name -Expected 'Create'
    Assert-Equal -Name 'symbol-diagnostics total' -Actual $symbolDiagnosticsJson.totalDiagnostics -Expected 1
    Assert-Equal -Name 'symbol-diagnostics id' -Actual @($symbolDiagnosticsJson.diagnostics)[0].id -Expected 'CS0246'
    Assert-Equal -Name 'symbol-diagnostics reason' -Actual (@(@($symbolDiagnosticsJson.diagnostics)[0].reasonCodes) -contains 'diagnostic-intersects-symbol-span') -Expected $true

    $diagnosticPack = Invoke-Navlyn `
        -Name 'diagnostic-pack id mode' `
        -Arguments @('diagnostic-pack', '--workspace', $FixtureProjectPath, '--id', 'CS0246', '--exclude-generated', '--limit', '5') `
        -ExpectedExitCode 0

    $diagnosticPackJson = $diagnosticPack.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'diagnostic-pack mode' -Actual $diagnosticPackJson.input.mode -Expected 'id'
    Assert-Equal -Name 'diagnostic-pack total' -Actual $diagnosticPackJson.totalDiagnostics -Expected 2
    Assert-Equal -Name 'diagnostic-pack has context scope' -Actual ($null -ne $diagnosticPackJson.context.scope) -Expected $true
    Assert-Equal -Name 'diagnostic-pack has source action' -Actual (@($diagnosticPackJson.nextActions | Where-Object { $_.command -eq 'symbol-source' }).Count -ge 1) -Expected $true

    $diagnosticsInvalidSeverity = Invoke-Navlyn `
        -Name 'diagnostics invalid severity' `
        -Arguments @('diagnostics', '--workspace', $FixtureProjectPath, '--severity', 'Bad') `
        -ExpectedExitCode 2

    Assert-Equal -Name 'diagnostics invalid severity stdout empty' -Actual $diagnosticsInvalidSeverity.Stdout.Length -Expected 0
    Assert-Equal -Name 'diagnostics invalid severity code' -Actual ($diagnosticsInvalidSeverity.Stderr.Contains('NAVLYN1009:')) -Expected $true

    Write-Host 'Diagnostics checks passed.'
}
finally {
    Pop-Location
}

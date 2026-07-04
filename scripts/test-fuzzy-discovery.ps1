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
$FixtureProject = Join-Path $RepoRoot 'tests/fixtures/FuzzyDiscoveryFixture/FuzzyDiscoveryFixture.csproj'
$TargetFrameworkScript = Join-Path $RepoRoot 'scripts/lib/navlyn-target-framework.ps1'

. $TargetFrameworkScript

$TargetFramework = Get-NavlynPreferredTargetFramework -ProjectPath $ProjectPath
$NavlynDll = Join-Path $ProjectDir "bin/Debug/$TargetFramework/navlyn.dll"

function Join-ProcessArguments {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)
    ($Arguments | ForEach-Object {
        if ($_.IndexOfAny([char[]]@(' ', "`t", '"')) -lt 0) {
            $_
        }
        else {
            '"' + $_.Replace('"', '\"') + '"'
        }
    }) -join ' '
}

function Invoke-CheckedProcess {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][int]$ExpectedExitCode,
        [string]$StandardInput = $null
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.WorkingDirectory = $RepoRoot
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.RedirectStandardInput = $null -ne $StandardInput
    $startInfo.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $startInfo.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    $startInfo.UseShellExecute = $false
    $startInfo.Arguments = Join-ProcessArguments -Arguments $Arguments

    $process = [System.Diagnostics.Process]::Start($startInfo)
    if ($null -ne $StandardInput) {
        $process.StandardInput.Write($StandardInput)
        $process.StandardInput.Close()
    }

    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()

    if ($process.ExitCode -ne $ExpectedExitCode) {
        throw @"
$Name failed with exit code $($process.ExitCode). Expected $ExpectedExitCode.
Command: $FilePath $($Arguments -join ' ')
stdout:
$stdout
stderr:
$stderr
"@
    }

    if ($ShowOutput) {
        Write-Host ''
        Write-Host "[$Name]"
        Write-Host $stdout.TrimEnd()
        if ($stderr.Length -gt 0) {
            Write-Host 'stderr:'
            Write-Host $stderr.TrimEnd()
        }
    }

    [pscustomobject]@{
        Stdout = $stdout
        Stderr = $stderr
    }
}

function Invoke-Navlyn {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [string]$StandardInput = $null
    )

    Invoke-CheckedProcess `
        -Name $Name `
        -FilePath 'dotnet' `
        -Arguments (@($NavlynDll) + $Arguments) `
        -ExpectedExitCode 0 `
        -StandardInput $StandardInput
}

function Assert-Equal {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [AllowNull()][object]$Actual,
        [AllowNull()][object]$Expected
    )

    if ($Actual -ne $Expected) {
        throw "$Name expected '$Expected' but was '$Actual'."
    }
}

function Get-OptionalProperty {
    param(
        [Parameter(Mandatory = $true)][object]$Object,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    $property.Value
}

Push-Location $RepoRoot
try {
    if (!$NoBuild) {
        Invoke-CheckedProcess -Name 'dotnet restore' -FilePath 'dotnet' -Arguments @('restore', $SolutionPath) -ExpectedExitCode 0 | Out-Null
        Invoke-CheckedProcess -Name 'dotnet build' -FilePath 'dotnet' -Arguments @('build', $SolutionPath) -ExpectedExitCode 0 | Out-Null
    }

    Invoke-CheckedProcess -Name 'fixture restore' -FilePath 'dotnet' -Arguments @('restore', $FixtureProject) -ExpectedExitCode 0 | Out-Null

    $findUnique = Invoke-Navlyn `
        -Name 'find unique type' `
        -Arguments @('find', '--workspace', $FixtureProject, '--query', 'EnemyManagerTools', '--assume-kind', 'NamedType')
    $findUniqueJson = $findUnique.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'find unique confidence' -Actual $findUniqueJson.confidence -Expected 'high'
    Assert-Equal -Name 'find unique selected' -Actual $findUniqueJson.selectedCandidate.name -Expected 'EnemyManagerTools'
    Assert-Equal -Name 'find unique selected has span' -Actual ($findUniqueJson.selectedCandidate.endColumn -gt $findUniqueJson.selectedCandidate.column) -Expected $true
    Assert-Equal -Name 'find unique candidate id' -Actual ($findUniqueJson.selectedCandidate.candidateId.StartsWith('sym:v1:')) -Expected $true
    Assert-Equal -Name 'find unique selector name' -Actual $findUniqueJson.selectedCandidate.selector.name -Expected 'EnemyManagerTools'

    $candidateRoundTrip = Invoke-Navlyn `
        -Name 'about candidate id roundtrip' `
        -Arguments @('about', '--workspace', $FixtureProject, '--candidate-id', $findUniqueJson.selectedCandidate.candidateId)
    $candidateRoundTripJson = $candidateRoundTrip.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'candidate roundtrip confidence' -Actual $candidateRoundTripJson.confidence -Expected 'high'
    Assert-Equal -Name 'candidate roundtrip selected' -Actual $candidateRoundTripJson.selectedCandidate.name -Expected 'EnemyManagerTools'
    Assert-Equal -Name 'candidate roundtrip selection mode' -Actual $candidateRoundTripJson.selectionInput.mode -Expected 'candidateId'

    $candidateDefinition = Invoke-Navlyn `
        -Name 'definition candidate id roundtrip' `
        -Arguments @('definition', '--workspace', $FixtureProject, '--candidate-id', $findUniqueJson.selectedCandidate.candidateId)
    $candidateDefinitionJson = $candidateDefinition.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'candidate definition selection mode' -Actual $candidateDefinitionJson.selectionInput.mode -Expected 'candidateId'
    Assert-Equal -Name 'candidate definition id' -Actual $candidateDefinitionJson.selectionInput.candidateId -Expected $findUniqueJson.selectedCandidate.candidateId
    Assert-Equal -Name 'candidate definition symbol' -Actual $candidateDefinitionJson.symbol.name -Expected 'EnemyManagerTools'
    Assert-Equal -Name 'candidate definition path' -Actual @($candidateDefinitionJson.definitions)[0].path -Expected 'tests/fixtures/FuzzyDiscoveryFixture/FixtureCode.cs'

    $findAmbiguous = Invoke-Navlyn `
        -Name 'find ambiguous exact' `
        -Arguments @('find', '--workspace', $FixtureProject, '--query', 'EnemyManager', '--assume-kind', 'NamedType')
    $findAmbiguousJson = $findAmbiguous.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'find ambiguous confidence' -Actual $findAmbiguousJson.confidence -Expected 'ambiguous'
    Assert-Equal -Name 'find ambiguous selected omitted' -Actual (Get-OptionalProperty -Object $findAmbiguousJson -Name 'selectedCandidate') -Expected $null

    $findMedium = Invoke-Navlyn `
        -Name 'find exact plus weaker alternative' `
        -Arguments @('find', '--workspace', $FixtureProject, '--query', 'Spawn', '--assume-kind', 'Method')
    $findMediumJson = $findMedium.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'find medium confidence' -Actual $findMediumJson.confidence -Expected 'medium'
    Assert-Equal -Name 'find medium selected' -Actual $findMediumJson.selectedCandidate.name -Expected 'Spawn'
    Assert-Equal -Name 'find medium alternatives present' -Actual (@($findMediumJson.alternatives).Count -gt 0) -Expected $true

    $findMinConfidence = Invoke-Navlyn `
        -Name 'find min confidence blocks medium' `
        -Arguments @('find', '--workspace', $FixtureProject, '--query', 'Spawn', '--assume-kind', 'Method', '--min-confidence', 'high', '--explain-selection')
    $findMinConfidenceJson = $findMinConfidence.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'find min confidence confidence' -Actual $findMinConfidenceJson.confidence -Expected 'medium'
    Assert-Equal -Name 'find min confidence selected omitted' -Actual (Get-OptionalProperty -Object $findMinConfidenceJson -Name 'selectedCandidate') -Expected $null
    Assert-Equal -Name 'find min confidence reason' -Actual (@($findMinConfidenceJson.selectionExplanation.ambiguityReasons) -contains 'confidence-below-minimum') -Expected $true

    $findContains = Invoke-Navlyn `
        -Name 'find contains fallback' `
        -Arguments @('find', '--workspace', $FixtureProject, '--query', 'Tools', '--assume-kind', 'NamedType')
    $findContainsJson = $findContains.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'find contains selected' -Actual $findContainsJson.selectedCandidate.name -Expected 'EnemyManagerTools'
    Assert-Equal -Name 'find contains reason' -Actual (@($findContainsJson.selectedCandidate.reasonCodes) -contains 'contains-name-match') -Expected $true

    $findInvalidRegex = Invoke-CheckedProcess `
        -Name 'find invalid regex' `
        -FilePath 'dotnet' `
        -Arguments (@($NavlynDll) + @('find', '--workspace', $FixtureProject, '--query', '[', '--match', 'regex')) `
        -ExpectedExitCode 2
    Assert-Equal -Name 'find invalid regex stdout empty' -Actual $findInvalidRegex.Stdout.Length -Expected 0
    Assert-Equal -Name 'find invalid regex diagnostic' -Actual ($findInvalidRegex.Stderr.Contains('NAVLYN1002:')) -Expected $true

    $aboutInvalidCandidateId = Invoke-CheckedProcess `
        -Name 'about invalid candidate id' `
        -FilePath 'dotnet' `
        -Arguments (@($NavlynDll) + @('about', '--workspace', $FixtureProject, '--candidate-id', 'bad')) `
        -ExpectedExitCode 2
    Assert-Equal -Name 'about invalid candidate stdout empty' -Actual $aboutInvalidCandidateId.Stdout.Length -Expected 0
    Assert-Equal -Name 'about invalid candidate diagnostic' -Actual ($aboutInvalidCandidateId.Stderr.Contains('NAVLYN1701:')) -Expected $true

    $findGenerated = Invoke-Navlyn `
        -Name 'find generated excluded' `
        -Arguments @('find', '--workspace', $FixtureProject, '--query', 'GeneratedWidget', '--assume-kind', 'NamedType', '--exclude-generated')
    $findGeneratedJson = $findGenerated.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'find generated none' -Actual $findGeneratedJson.confidence -Expected 'none'
    Assert-Equal -Name 'find generated count' -Actual $findGeneratedJson.totalCandidates -Expected 0

    $whereUsed = Invoke-Navlyn `
        -Name 'where-used with context' `
        -Arguments @('where-used', '--workspace', $FixtureProject, '--query', 'Spawn', '--assume-kind', 'Method', '--limit', '5')
    $whereUsedJson = $whereUsed.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'where-used confidence' -Actual $whereUsedJson.confidence -Expected 'medium'
    Assert-Equal -Name 'where-used total matches' -Actual $whereUsedJson.totalMatches -Expected 3
    Assert-Equal -Name 'where-used containing symbol' -Actual (@($whereUsedJson.references)[0].containingSymbol.kind -eq 'Method') -Expected $true
    Assert-Equal -Name 'where-used reference has span' -Actual (@($whereUsedJson.references)[0].endColumn -gt @($whereUsedJson.references)[0].column) -Expected $true
    Assert-Equal -Name 'where-used containing symbol has span' -Actual (@($whereUsedJson.references)[0].containingSymbol.endColumn -gt @($whereUsedJson.references)[0].containingSymbol.column) -Expected $true
    Assert-Equal -Name 'where-used usage kind' -Actual (@($whereUsedJson.references)[0].usageKind) -Expected 'invoke'

    $whereUsedGrouped = Invoke-Navlyn `
        -Name 'where-used usage kind grouping' `
        -Arguments @('where-used', '--workspace', $FixtureProject, '--query', 'Spawn', '--assume-kind', 'Method', '--limit', '5', '--usage-kind', 'invoke', '--group-by', 'usage-kind')
    $whereUsedGroupedJson = $whereUsedGrouped.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'where-used grouped total matches' -Actual $whereUsedGroupedJson.totalMatches -Expected 3
    Assert-Equal -Name 'where-used grouped usage count' -Actual @($whereUsedGroupedJson.usageKindCounts)[0].count -Expected 3
    Assert-Equal -Name 'where-used grouped key' -Actual @($whereUsedGroupedJson.groups)[0].key -Expected 'invoke'

    $whereUsedSnippet = Invoke-Navlyn `
        -Name 'where-used snippets' `
        -Arguments @('where-used', '--workspace', $FixtureProject, '--query', 'Spawn', '--assume-kind', 'Method', '--limit', '1', '--include-snippets', '--snippet-lines', '0')
    $whereUsedSnippetJson = $whereUsedSnippet.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'where-used snippet present' -Actual ($null -ne @($whereUsedSnippetJson.references)[0].snippet) -Expected $true

    $about = Invoke-Navlyn `
        -Name 'about type summary' `
        -Arguments @('about', '--workspace', $FixtureProject, '--query', 'EnemyManagerTools', '--assume-kind', 'NamedType', '--member-limit', '5', '--reference-limit', '5')
    $aboutJson = $about.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'about selected type' -Actual $aboutJson.selectedCandidate.name -Expected 'EnemyManagerTools'
    Assert-Equal -Name 'about member count' -Actual $aboutJson.members.totalMembers -Expected 2
    Assert-Equal -Name 'about definition has span' -Actual ($aboutJson.definition.endColumn -gt $aboutJson.definition.column) -Expected $true

    $aboutSpawn = Invoke-Navlyn `
        -Name 'about references include generated' `
        -Arguments @('about', '--workspace', $FixtureProject, '--query', 'Spawn', '--assume-kind', 'Method', '--reference-limit', '10')
    $aboutSpawnJson = $aboutSpawn.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'about include generated total matches' -Actual $aboutSpawnJson.references.totalMatches -Expected 3
    Assert-Equal -Name 'about include generated reference' -Actual ((@($aboutSpawnJson.references.references | Where-Object { $_.path.EndsWith('GeneratedWidget.g.cs') }).Count) -gt 0) -Expected $true
    Assert-Equal -Name 'about include generated file summary' -Actual ((@($aboutSpawnJson.references.files | Where-Object { $_.path.EndsWith('GeneratedWidget.g.cs') }).Count) -gt 0) -Expected $true

    $aboutSpawnExcludeGenerated = Invoke-Navlyn `
        -Name 'about references exclude generated' `
        -Arguments @('about', '--workspace', $FixtureProject, '--query', 'Spawn', '--assume-kind', 'Method', '--reference-limit', '10', '--exclude-generated')
    $aboutSpawnExcludeGeneratedJson = $aboutSpawnExcludeGenerated.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'about exclude generated total matches' -Actual $aboutSpawnExcludeGeneratedJson.references.totalMatches -Expected 2
    Assert-Equal -Name 'about exclude generated reference' -Actual (@($aboutSpawnExcludeGeneratedJson.references.references | Where-Object { $_.path.EndsWith('GeneratedWidget.g.cs') }).Count) -Expected 0
    Assert-Equal -Name 'about exclude generated file summary' -Actual (@($aboutSpawnExcludeGeneratedJson.references.files | Where-Object { $_.path.EndsWith('GeneratedWidget.g.cs') }).Count) -Expected 0

    $related = Invoke-Navlyn `
        -Name 'related files' `
        -Arguments @('related', '--workspace', $FixtureProject, '--query', 'Spawn', '--assume-kind', 'Method', '--limit', '5')
    $relatedJson = $related.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'related has files' -Actual ($relatedJson.totalFiles -gt 0) -Expected $true
    Assert-Equal -Name 'related no impact level' -Actual (Get-OptionalProperty -Object @($relatedJson.files)[0] -Name 'impactLevel') -Expected $null
    Assert-Equal -Name 'related file location has span' -Actual (@(@($relatedJson.files)[0].locations)[0].endColumn -gt @(@($relatedJson.files)[0].locations)[0].column) -Expected $true

    $impact = Invoke-Navlyn `
        -Name 'impact files' `
        -Arguments @('impact', '--workspace', $FixtureProject, '--query', 'Spawn', '--assume-kind', 'Method', '--limit', '5', '--depth', '2')
    $impactJson = $impact.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'impact direct file' -Actual (@($impactJson.files | Where-Object { $_.impactLevel -eq 'direct' }).Count -gt 0) -Expected $true

    $entrypoints = Invoke-Navlyn `
        -Name 'entrypoints chains' `
        -Arguments @('entrypoints', '--workspace', $FixtureProject, '--query', 'Spawn', '--assume-kind', 'Method', '--limit', '5', '--depth', '2')
    $entrypointsJson = $entrypoints.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'entrypoints total chains' -Actual $entrypointsJson.totalChains -Expected 3
    Assert-Equal -Name 'entrypoints chain end' -Actual @($entrypointsJson.chains)[0].endReason -Expected 'no-upstream-callers'
    Assert-Equal -Name 'entrypoints chain symbol has span' -Actual (@(@($entrypointsJson.chains)[0].symbols)[0].endColumn -gt @(@($entrypointsJson.chains)[0].symbols)[0].column) -Expected $true

    $batchInput = @'
{
  "requests": [
    {
      "id": "find",
      "command": "find",
      "query": "EnemyManagerTools",
      "assumeKind": "NamedType"
    },
    {
      "id": "where-used",
      "command": "where-used",
      "query": "Spawn",
      "assumeKind": "Method",
      "limit": 2
    },
    {
      "id": "entrypoints",
      "command": "entrypoints",
      "query": "Spawn",
      "assumeKind": "Method",
      "limit": 2
    }
  ]
}
'@
    $batch = Invoke-Navlyn -Name 'batch fuzzy' -Arguments @('batch', '--workspace', $FixtureProject) -StandardInput $batchInput
    $batchJson = $batch.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'batch fuzzy succeeded' -Actual $batchJson.succeededRequests -Expected 3
    Assert-Equal -Name 'batch fuzzy where-used ok' -Actual @($batchJson.results)[1].ok -Expected $true

    $invalidRegexBatchInput = @'
{
  "requests": [
    {
      "id": "bad",
      "command": "find",
      "query": "[",
      "match": "regex"
    }
  ]
}
'@
    $invalidRegexBatch = Invoke-Navlyn -Name 'batch fuzzy invalid regex' -Arguments @('batch', '--workspace', $FixtureProject) -StandardInput $invalidRegexBatchInput
    $invalidRegexBatchJson = $invalidRegexBatch.Stdout | ConvertFrom-Json
    Assert-Equal -Name 'batch invalid regex failed count' -Actual $invalidRegexBatchJson.failedRequests -Expected 1
    Assert-Equal -Name 'batch invalid regex ok' -Actual @($invalidRegexBatchJson.results)[0].ok -Expected $false
    Assert-Equal -Name 'batch invalid regex code' -Actual @($invalidRegexBatchJson.results)[0].error.code -Expected 'NAVLYN1002'

    Write-Host 'Fuzzy discovery checks passed.'
}
finally {
    Pop-Location
}

[CmdletBinding()]
param(
    [string]$ScenarioFile = "docs/evals/tool-selection.scenarios.json",
    [string]$TraceFile = $null,
    [string]$Output = $null,
    [switch]$UseBaselineTraces,
    [switch]$NoBuild,
    [double]$MinimumScore = 1.0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$ScenarioPath = if ([System.IO.Path]::IsPathRooted($ScenarioFile)) {
    [System.IO.Path]::GetFullPath($ScenarioFile)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $ScenarioFile))
}
if (!(Test-Path -LiteralPath $ScenarioPath)) {
    throw "Scenario file does not exist: $ScenarioPath"
}

$scenarioDocument = Get-Content -Raw -LiteralPath $ScenarioPath | ConvertFrom-Json -Depth 100
if ($scenarioDocument.schemaVersion -ne 'navlyn.tool-selection-eval.v2') {
    throw "Unsupported tool-selection eval schemaVersion: $($scenarioDocument.schemaVersion)"
}

function Test-JsonObject([object]$value) { return $value -is [System.Management.Automation.PSCustomObject] }
function Test-JsonInteger([object]$value) { return $value -is [sbyte] -or $value -is [byte] -or $value -is [int16] -or $value -is [uint16] -or $value -is [int] -or $value -is [uint32] -or $value -is [long] -or $value -is [ulong] }
function Test-ExternalTraceShape([object]$trace) {
    $id = [string]$trace.scenarioId
    if ($trace.scenarioId -isnot [string] -or [string]::IsNullOrWhiteSpace($id) -or
        $trace.skillActivated -isnot [bool] -or $trace.outputValid -isnot [bool] -or $trace.stderrClean -isnot [bool] -or
        $trace.stopReason -isnot [string] -or [string]::IsNullOrWhiteSpace($trace.stopReason) -or
        $trace.calls -isnot [array] -or $trace.claims -isnot [array] -or
        !(Test-JsonObject $trace.semanticChecks) -or !(Test-JsonObject $trace.environment) -or
        !(Test-JsonInteger $trace.stdoutChars) -or !(Test-JsonInteger $trace.latencyMs) -or
        [long]$trace.stdoutChars -lt 0 -or [long]$trace.latencyMs -lt 0) {
        throw "Malformed required v2 trace fields: $id"
    }
    foreach ($property in $trace.semanticChecks.PSObject.Properties) { if ($property.Value -isnot [bool]) { throw "Semantic checks must be JSON booleans: $id/$($property.Name)" } }
    foreach ($property in $trace.environment.PSObject.Properties) { if ($property.Value -isnot [string]) { throw "Environment values must be JSON strings: $id/$($property.Name)" } }
    foreach ($field in @('mcpAvailability','workspaceAvailability','workspaceFreshness')) {
        if ($null -eq $trace.environment.PSObject.Properties[$field]) { throw "Missing environment string '$field': $id" }
    }
    foreach ($claim in @($trace.claims)) { if ($claim -isnot [string]) { throw "Claims must be JSON strings: $id" } }
    foreach ($call in @($trace.calls)) {
        if ($call.kind -isnot [string] -or $call.kind -notin @('mcp','ordinary') -or
            $call.name -isnot [string] -or [string]::IsNullOrWhiteSpace($call.name) -or
            !(Test-JsonObject $call.arguments) -or !(Test-JsonObject $call.selectedResultFields)) {
            throw "Malformed call in v2 trace: $id"
        }
    }
}
function Test-PredicateDefinition([object]$predicate, [string]$scenarioId) {
    $allowedKeys = @('path','type','equals','minimum','minItems','nonBlank','requiredProperties','minProperties')
    foreach ($property in $predicate.PSObject.Properties) { if ($property.Name -notin $allowedKeys) { throw "Unsupported stop predicate key '$($property.Name)' in $scenarioId" } }
    if ([string]::IsNullOrWhiteSpace([string]$predicate.path)) { throw "Stop predicate path is required in $scenarioId" }
    $type = [string]$predicate.type
    if ($type -notin @('string','number','boolean','array','object')) { throw "Unsupported stop predicate type '$type' in $scenarioId" }
    $ops = @('equals','minimum','minItems','nonBlank','requiredProperties','minProperties' | Where-Object { $null -ne $predicate.PSObject.Properties[$_] })
    if ($ops.Count -ne 1) { throw "Each stop predicate needs exactly one supported constraint in $scenarioId" }
    switch ($ops[0]) {
        'equals' {
            $equals = $predicate.equals
            $matches = switch ($type) {
                'string' { $equals -is [string] }
                'number' { Test-JsonInteger $equals }
                'boolean' { $equals -is [bool] }
                'array' { $equals -is [array] }
                'object' { Test-JsonObject $equals }
            }
            if (!$matches) { throw "Stop predicate equals value does not match type '$type' in $scenarioId" }
        }
        'minimum' { if ($type -ne 'number' -or !(Test-JsonInteger $predicate.minimum) -or [long]$predicate.minimum -lt 0) { throw "minimum only accepts a nonnegative integer number predicate in $scenarioId" } }
        'minItems' { if ($type -ne 'array' -or !(Test-JsonInteger $predicate.minItems) -or [long]$predicate.minItems -lt 1) { throw "minItems only accepts a positive array count in $scenarioId" } }
        'nonBlank' { if ($type -ne 'string' -or $predicate.nonBlank -isnot [bool] -or !$predicate.nonBlank) { throw "nonBlank only accepts true on string predicates in $scenarioId" } }
        'requiredProperties' {
            if ($type -ne 'object' -or $predicate.requiredProperties -isnot [array] -or @($predicate.requiredProperties).Count -eq 0) { throw "requiredProperties only accepts a nonempty object-property array in $scenarioId" }
            foreach ($name in @($predicate.requiredProperties)) { if ($name -isnot [string] -or [string]::IsNullOrWhiteSpace($name)) { throw "Invalid required property in $scenarioId" } }
        }
        'minProperties' { if ($type -ne 'object' -or !(Test-JsonInteger $predicate.minProperties) -or [long]$predicate.minProperties -lt 1) { throw "minProperties only accepts a positive object count in $scenarioId" } }
    }
}
function Test-ScenarioPredicateDefinitions([object]$scenario) {
    $id = [string]$scenario.id
    if ($scenario.expectedStopEvidence.acceptedStopReasons -isnot [array] -or @($scenario.expectedStopEvidence.acceptedStopReasons).Count -eq 0 -or $scenario.expectedStopEvidence.alternatives -isnot [array] -or @($scenario.expectedStopEvidence.alternatives).Count -eq 0) { throw "Invalid expected stop evidence definition: $id" }
    foreach ($reason in @($scenario.expectedStopEvidence.acceptedStopReasons)) { if ($reason -isnot [string] -or [string]::IsNullOrWhiteSpace($reason)) { throw "Invalid accepted stop reason: $id" } }
    foreach ($alternative in @($scenario.expectedStopEvidence.alternatives)) {
        $required = @($alternative.requiredFields)
        $predicates = @($alternative.predicates)
        if (!(Test-JsonInteger $alternative.callIndex) -or [int]$alternative.callIndex -lt 0 -or
            ($null -ne $scenario.PSObject.Properties['baselineTrace'] -and [int]$alternative.callIndex -ge @($scenario.baselineTrace.calls).Count) -or
            $required.Count -eq 0 -or $predicates.Count -eq 0 -or ($required.Count -eq 1 -and [string]$required[0] -ceq 'result.command')) { throw "Invalid stop evidence alternative: $id" }
        foreach ($field in $required) { if ($field -isnot [string] -or [string]::IsNullOrWhiteSpace($field)) { throw "Invalid required evidence path: $id" } }
        $fieldSet = @($required | Sort-Object -Unique)
        $predicatePaths = @($predicates | ForEach-Object { [string]$_.path })
        if (($predicatePaths | Sort-Object -Unique) -join "`n" -cne ($fieldSet -join "`n") -or $predicatePaths.Count -ne $fieldSet.Count) { throw "Stop predicate paths must exactly cover requiredFields: $id" }
        foreach ($predicate in $predicates) { Test-PredicateDefinition $predicate $id }
    }
}
foreach ($scenario in @($scenarioDocument.scenarios)) { Test-ScenarioPredicateDefinitions $scenario }

if ($NoBuild -and !$UseBaselineTraces -and [string]::IsNullOrWhiteSpace($TraceFile)) {
    $UseBaselineTraces = $true
}
if (!$UseBaselineTraces -and [string]::IsNullOrWhiteSpace($TraceFile)) {
    throw 'Pass -UseBaselineTraces or provide -TraceFile with actual traces.'
}

$traceByScenario = @{}
if ($UseBaselineTraces) {
    foreach ($scenario in @($scenarioDocument.scenarios)) {
        $traceByScenario[$scenario.id] = $scenario.baselineTrace
    }
}
else {
    $tracePath = if ([System.IO.Path]::IsPathRooted($TraceFile)) {
        [System.IO.Path]::GetFullPath($TraceFile)
    }
    else {
        [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $TraceFile))
    }
    if (!(Test-Path -LiteralPath $tracePath)) {
        throw "Trace file does not exist: $tracePath"
    }

    $traceDocument = Get-Content -Raw -LiteralPath $tracePath | ConvertFrom-Json -Depth 100
    if ($traceDocument.schemaVersion -ne 'navlyn.tool-selection-eval.trace.v2') {
        throw "Unsupported tool-selection trace schemaVersion: $($traceDocument.schemaVersion)"
    }
    if ($traceDocument.traces -isnot [array]) { throw 'Trace document traces must be a JSON array.' }
    foreach ($trace in @($traceDocument.traces)) {
        if ([string]::IsNullOrWhiteSpace([string]$trace.scenarioId)) { throw 'Trace scenarioId must be a nonblank string.' }
        if ($traceByScenario.ContainsKey([string]$trace.scenarioId)) { throw "Duplicate trace scenarioId: $($trace.scenarioId)" }
        if (@($scenarioDocument.scenarios | Where-Object { [string]$_.id -ceq [string]$trace.scenarioId }).Count -eq 0) { throw "Trace references unknown scenarioId: $($trace.scenarioId)" }
        Test-ExternalTraceShape $trace
        $traceByScenario[$trace.scenarioId] = $trace
    }
}

function Get-PathValue {
    param([object]$Object, [string]$Path)
    if ($null -ne $Object -and $null -ne $Object.PSObject.Properties[$Path]) {
        return $Object.$Path
    }
    $value = $Object
    foreach ($part in $Path.Split('.')) {
        if ($null -eq $value -or $null -eq $value.PSObject.Properties[$part]) {
            return $null
        }
        $value = $value.$part
    }
    if ($value -is [array]) { return ,$value }
    return $value
}

function Test-HasProperty {
    param([object]$Object, [string]$Name)
    return $null -ne $Object -and $null -ne $Object.PSObject.Properties[$Name]
}

function Test-ActionEqual {
    param([object]$Left, [object]$Right)
    return $null -ne $Left -and $null -ne $Right -and [string]$Left.kind -ceq [string]$Right.kind -and [string]$Left.name -ceq [string]$Right.name
}

function Test-SequenceEqual {
    param([object[]]$Left, [object[]]$Right)
    if ($Left.Count -ne $Right.Count) { return $false }
    for ($index = 0; $index -lt $Left.Count; $index++) {
        if (!(Test-ActionEqual -Left $Left[$index] -Right $Right[$index])) { return $false }
    }
    return $true
}

function Test-AnySequenceEqual {
    param([object[]]$Actual, [object[]]$Candidates)
    foreach ($candidate in $Candidates) {
        if (Test-SequenceEqual -Left $Actual -Right @($candidate)) { return $true }
    }
    return $false
}

function Test-StopEvidence {
    param([object]$Scenario, [object]$Trace)
    if ($null -eq $Trace -or !(Test-HasProperty -Object $Trace -Name 'stopReason')) { return $false }
    if ([string]$Trace.stopReason -notin @($Scenario.expectedStopEvidence.acceptedStopReasons)) { return $false }
    foreach ($alternative in @($Scenario.expectedStopEvidence.alternatives)) {
        $requiredFields = @($alternative.requiredFields)
        $predicates = @($alternative.predicates)
        if ($requiredFields.Count -eq 0 -or $predicates.Count -eq 0) { continue }
        $index = [int]$alternative.callIndex
        $calls = @($Trace.calls)
        if ($index -lt 0 -or $index -ge $calls.Count) { continue }
        $fields = $calls[$index].selectedResultFields
        $allPresent = $true
        foreach ($predicate in $predicates) {
            if (!(Test-StopPredicate -Fields $fields -Predicate $predicate)) { $allPresent = $false; break }
        }
        if ($allPresent) { return $true }
    }
    return $false
}

function Test-StopPredicate {
    param([object]$Fields, [object]$Predicate)
    $value = Get-PathValue -Object $Fields -Path ([string]$Predicate.path)
    if ($null -eq $value) { return $false }
    switch ([string]$Predicate.type) {
        'string' { if ($value -isnot [string]) { return $false } }
        'number' { if (!(Test-JsonInteger $value)) { return $false } }
        'boolean' { if ($value -isnot [bool]) { return $false } }
        'array' { if ($value -isnot [array]) { return $false } }
        'object' { if (!(Test-JsonObject $value)) { return $false } }
        default { return $false }
    }
    if ($null -ne $Predicate.PSObject.Properties['equals']) { return Test-JsonValueEqual -Left $value -Right $Predicate.equals }
    if ($null -ne $Predicate.PSObject.Properties['minimum']) { return [decimal]$value -ge [decimal]$Predicate.minimum }
    if ($null -ne $Predicate.PSObject.Properties['minItems']) { return $value.Count -ge [int]$Predicate.minItems }
    if ($null -ne $Predicate.PSObject.Properties['nonBlank']) { return $Predicate.nonBlank -and ![string]::IsNullOrWhiteSpace($value) }
    if ($null -ne $Predicate.PSObject.Properties['requiredProperties']) {
        foreach ($name in @($Predicate.requiredProperties)) { if ($null -eq $value.PSObject.Properties[[string]$name]) { return $false } }
        return $true
    }
    if ($null -ne $Predicate.PSObject.Properties['minProperties']) { return @($value.PSObject.Properties).Count -ge [int]$Predicate.minProperties }
    return $false
}

function Test-RequiredArguments {
    param([object]$Scenario, [object]$Trace)
    foreach ($requirement in @($Scenario.requiredArguments)) {
        $index = [int]$requirement.callIndex
        $calls = @($Trace.calls)
        if ($index -lt 0 -or $index -ge $calls.Count) { return $false }
        $actual = Get-PathValue -Object $calls[$index].arguments -Path ([string]$requirement.path)
        if (!(Test-JsonValueEqual -Left $actual -Right $requirement.equals)) { return $false }
    }
    return $true
}

function Test-SemanticChecks {
    param([object]$Scenario, [object]$Trace)
    foreach ($check in @($Scenario.semanticCorrectnessChecks)) {
        $value = Get-PathValue -Object $Trace.semanticChecks -Path ([string]$check)
        if ($value -isnot [bool] -or $value -ne $true) { return $false }
    }
    return $true
}

function Test-Environment {
    param([object]$Scenario, [object]$Trace)
    foreach ($key in @('mcpAvailability', 'workspaceAvailability', 'workspaceFreshness')) {
        $expected = [string]$Scenario.availabilityFreshnessSetup.$key
        $actual = Get-PathValue -Object $Trace.environment -Path $key
        if ($null -eq $actual -or [string]$actual -cne $expected) { return $false }
    }
    return $true
}

function Test-UnsupportedClaims {
    param([object]$Scenario, [object]$Trace)
    foreach ($claim in @($Trace.claims)) {
        foreach ($unsupported in @($Scenario.unsupportedClaims)) {
            $claimText = [string]$claim
            if ($claimText.IndexOf([string]$unsupported, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) { return $false }
        }
    }
    return $true
}

function Test-JsonValueEqual {
    param([object]$Left, [object]$Right)
    if ($null -eq $Left -or $null -eq $Right) { return $null -eq $Left -and $null -eq $Right }
    if ($Left -is [System.Array] -or $Right -is [System.Array]) {
        if (!($Left -is [System.Array]) -or !($Right -is [System.Array]) -or $Left.Count -ne $Right.Count) { return $false }
        for ($index = 0; $index -lt $Left.Count; $index++) {
            if (!(Test-JsonValueEqual -Left $Left[$index] -Right $Right[$index])) { return $false }
        }
        return $true
    }
    $leftIsObject = $Left -is [System.Management.Automation.PSCustomObject]
    $rightIsObject = $Right -is [System.Management.Automation.PSCustomObject]
    if ($leftIsObject -or $rightIsObject) {
        if (!$leftIsObject -or !$rightIsObject) { return $false }
        [string[]]$leftNames = @($Left.PSObject.Properties.Name | Sort-Object -CaseSensitive)
        [string[]]$rightNames = @($Right.PSObject.Properties.Name | Sort-Object -CaseSensitive)
        if (!(Test-JsonValueEqual -Left $leftNames -Right $rightNames)) { return $false }
        foreach ($name in $leftNames) {
            if (!(Test-JsonValueEqual -Left $Left.$name -Right $Right.$name)) { return $false }
        }
        return $true
    }
    if ($Left -is [string] -or $Right -is [string]) { return $Left -is [string] -and $Right -is [string] -and $Left -ceq $Right }
    if ($Left -is [bool] -or $Right -is [bool]) { return $Left -is [bool] -and $Right -is [bool] -and $Left -eq $Right }
    if ($Left -is [ValueType] -and $Right -is [ValueType]) {
        try { return [decimal]$Left -eq [decimal]$Right } catch { return $Left.GetType() -eq $Right.GetType() -and $Left -eq $Right }
    }
    return $Left -eq $Right
}

$results = New-Object System.Collections.Generic.List[object]
$totalPoints = 0
$maxPoints = 0
$allCriteria = @('skillActivation', 'firstAction', 'acceptedSequence', 'forbiddenAvoidance', 'requiredArguments',
    'stopEvidence', 'semanticCorrectness', 'callAndOutputBudgets', 'availabilityFreshness', 'unsupportedClaims', 'stdoutStderrBehavior')

foreach ($scenario in @($scenarioDocument.scenarios)) {
    $trace = $traceByScenario[$scenario.id]
    if ($null -eq $trace) { $trace = [pscustomobject]@{ calls = @(); claims = @(); semanticChecks = @{}; environment = @{} } }
    if (!$UseBaselineTraces) { Test-ExternalTraceShape $trace }
    $calls = @($trace.calls)
    $actions = @($calls | ForEach-Object { [pscustomobject]@{ kind = $_.kind; name = $_.name } })
    $criteria = [ordered]@{}

    $criteria.skillActivation = [bool]$trace.skillActivated -eq [bool]$scenario.expectedSkillActivation
    $criteria.firstAction = $actions.Count -gt 0 -and (Test-ActionEqual -Left $actions[0] -Right $scenario.expectedFirstAction)
    $criteria.acceptedSequence = Test-AnySequenceEqual -Actual $actions -Candidates @($scenario.acceptedSequences)

    $forbiddenHit = $false
    foreach ($action in $actions) {
        foreach ($forbidden in @($scenario.forbiddenTools)) {
            if (Test-ActionEqual -Left $action -Right $forbidden) { $forbiddenHit = $true }
        }
    }
    if (Test-AnySequenceEqual -Actual $actions -Candidates @($scenario.forbiddenSequences)) { $forbiddenHit = $true }
    $criteria.forbiddenAvoidance = !$forbiddenHit

    $criteria.requiredArguments = Test-RequiredArguments -Scenario $scenario -Trace $trace
    $criteria.stopEvidence = Test-StopEvidence -Scenario $scenario -Trace $trace
    $criteria.semanticCorrectness = Test-SemanticChecks -Scenario $scenario -Trace $trace
    $stdoutChars = [int]$trace.stdoutChars
    $latencyMs = [int]$trace.latencyMs
    $criteria.callAndOutputBudgets = $calls.Count -le [int]$scenario.maxCalls -and
        $stdoutChars -ge 0 -and $stdoutChars -le [int]$scenario.stdoutBudgetChars -and
        $latencyMs -ge 0 -and $latencyMs -le [int]$scenario.latencyBudgetMs
    $criteria.availabilityFreshness = Test-Environment -Scenario $scenario -Trace $trace
    $criteria.unsupportedClaims = Test-UnsupportedClaims -Scenario $scenario -Trace $trace
    $criteria.stdoutStderrBehavior = [bool]$trace.outputValid -and [bool]$trace.stderrClean

    $points = 0
    foreach ($criterion in $allCriteria) { if ($criteria[$criterion]) { $points++ } }
    $maxPoints += $allCriteria.Count
    $totalPoints += $points
    $results.Add([pscustomobject]@{
        id = $scenario.id
        taskClass = $scenario.taskClass
        prompt = $scenario.prompt
        points = $points
        maxPoints = $allCriteria.Count
        passed = $points -eq $allCriteria.Count
        chosenSequence = $actions
        stopReason = if (Test-HasProperty -Object $trace -Name 'stopReason') { $trace.stopReason } else { $null }
        criteria = [pscustomobject]$criteria
    })
}

$score = if ($maxPoints -eq 0) { 0.0 } else { [Math]::Round($totalPoints / $maxPoints, 4) }
$report = [ordered]@{
    schemaVersion = 'navlyn.tool-selection-eval.report.v2'
    scenarioFile = [System.IO.Path]::GetRelativePath($RepoRoot, $ScenarioPath).Replace('\\', '/')
    traceSource = if ($UseBaselineTraces) { 'baseline' } else { [System.IO.Path]::GetRelativePath($RepoRoot, $tracePath).Replace('\\', '/') }
    scenarioCount = @($scenarioDocument.scenarios).Count
    totalPoints = $totalPoints
    maxPoints = $maxPoints
    score = $score
    passed = $score -ge $MinimumScore
    minimumScore = $MinimumScore
    results = $results.ToArray()
}

$json = $report | ConvertTo-Json -Depth 100
if ([string]::IsNullOrWhiteSpace($Output)) {
    $json
}
else {
    $outputPath = if ([System.IO.Path]::IsPathRooted($Output)) { [System.IO.Path]::GetFullPath($Output) } else { [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $Output)) }
    $outputDirectory = [System.IO.Path]::GetDirectoryName($outputPath)
    if (![string]::IsNullOrWhiteSpace($outputDirectory)) { [System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null }
    Set-Content -LiteralPath $outputPath -Value $json -Encoding utf8
}
if ($score -lt $MinimumScore) { throw "Tool-selection eval score $score is below minimum $MinimumScore." }

[CmdletBinding()]
param(
    [string]$TraceFile = "docs/evals/mcp-agent-traces.replay.json",
    [string]$ScenarioFile = "docs/evals/tool-selection.scenarios.json",
    [string]$Output = "artifacts/evals/mcp-agent-trace-report.json"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

function Resolve-InputPath([string]$path) {
    if ([System.IO.Path]::IsPathRooted($path)) { return [System.IO.Path]::GetFullPath($path) }
    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $path))
}
function Get-PathValue([object]$object, [string]$path) {
    $value = $object
    foreach ($part in $path.Split('.')) {
        if ($null -eq $value -or $null -eq $value.PSObject.Properties[$part]) { return $null }
        $value = $value.$part
    }
    if ($value -is [array]) { return ,$value }
    return $value
}
function Test-JsonEqual([object]$left, [object]$right) {
    if ($null -eq $left -or $null -eq $right) { return $null -eq $left -and $null -eq $right }
    if ($left -is [array] -or $right -is [array]) {
        if ($left -isnot [array] -or $right -isnot [array] -or $left.Count -ne $right.Count) { return $false }
        for ($i=0; $i -lt $left.Count; $i++) { if (!(Test-JsonEqual $left[$i] $right[$i])) { return $false } }
        return $true
    }
    $lo = $left -is [System.Management.Automation.PSCustomObject]
    $ro = $right -is [System.Management.Automation.PSCustomObject]
    if ($lo -or $ro) {
        if (!$lo -or !$ro) { return $false }
        $ln = @($left.PSObject.Properties.Name | Sort-Object -CaseSensitive)
        $rn = @($right.PSObject.Properties.Name | Sort-Object -CaseSensitive)
        if (!(Test-JsonEqual $ln $rn)) { return $false }
        foreach ($name in $ln) { if (!(Test-JsonEqual $left.$name $right.$name)) { return $false } }
        return $true
    }
    if ($left -is [string] -or $right -is [string]) { return $left -is [string] -and $right -is [string] -and $left -ceq $right }
    if ($left -is [bool] -or $right -is [bool]) { return $left -is [bool] -and $right -is [bool] -and $left -eq $right }
    if ($left -is [ValueType] -and $right -is [ValueType]) { try { return [decimal]$left -eq [decimal]$right } catch { return $left -eq $right } }
    return $left -eq $right
}
function Test-ActionEqual([object]$a, [object]$b) {
    return $null -ne $a -and $null -ne $b -and [string]$a.kind -ceq [string]$b.kind -and [string]$a.name -ceq [string]$b.name
}
function Test-SequenceEqual([object[]]$actual, [object[]]$expected) {
    if ($actual.Count -ne $expected.Count) { return $false }
    for ($i=0; $i -lt $actual.Count; $i++) { if (!(Test-ActionEqual $actual[$i] $expected[$i])) { return $false } }
    return $true
}
function Test-StopEvidence([object]$scenario, [object]$trace) {
    if ([string]$trace.stopReason -cnotin @($scenario.expectedStopEvidence.acceptedStopReasons)) { return $false }
    foreach ($alternative in @($scenario.expectedStopEvidence.alternatives)) {
        $predicates = @($alternative.predicates)
        if ($predicates.Count -eq 0) { continue }
        $index = [int]$alternative.callIndex
        $calls = @($trace.calls)
        if ($index -lt 0 -or $index -ge $calls.Count) { continue }
        $ok = $true
        foreach ($predicate in $predicates) { if (!(Test-StopPredicate $calls[$index].selectedResultFields $predicate)) { $ok=$false; break } }
        if ($ok) { return $true }
    }
    return $false
}
function Test-StopPredicate([object]$fields, [object]$predicate) {
    $value = Get-PathValue $fields ([string]$predicate.path)
    if ($null -eq $value) { return $false }
    switch ([string]$predicate.type) {
        'string' { if ($value -isnot [string]) { return $false } }
        'number' { if (!(Test-JsonInteger $value)) { return $false } }
        'boolean' { if ($value -isnot [bool]) { return $false } }
        'array' { if ($value -isnot [array]) { return $false } }
        'object' { if (!(Test-IsJsonObject $value)) { return $false } }
        default { return $false }
    }
    if ($null -ne $predicate.PSObject.Properties['equals']) { return Test-JsonEqual $value $predicate.equals }
    if ($null -ne $predicate.PSObject.Properties['minimum']) { return [decimal]$value -ge [decimal]$predicate.minimum }
    if ($null -ne $predicate.PSObject.Properties['minItems']) { return $value.Count -ge [int]$predicate.minItems }
    if ($null -ne $predicate.PSObject.Properties['nonBlank']) { return [bool]$predicate.nonBlank -and ![string]::IsNullOrWhiteSpace($value) }
    if ($null -ne $predicate.PSObject.Properties['requiredProperties']) { foreach ($name in @($predicate.requiredProperties)) { if ($null -eq $value.PSObject.Properties[[string]$name]) { return $false } }; return $true }
    if ($null -ne $predicate.PSObject.Properties['minProperties']) { return @($value.PSObject.Properties).Count -ge [int]$predicate.minProperties }
    return $false
}
function Test-Boolean([object]$value) { return $value -is [bool] }
function Test-IsJsonObject([object]$value) { return $value -is [System.Management.Automation.PSCustomObject] }
function Test-JsonInteger([object]$value) { return $value -is [sbyte] -or $value -is [byte] -or $value -is [int16] -or $value -is [uint16] -or $value -is [int] -or $value -is [uint32] -or $value -is [long] -or $value -is [ulong] }
function Get-AmbiguityEvidence([object]$trace) {
    $ambiguityReported = Get-PathValue $trace.semanticChecks 'ambiguityReported'
    if ([string]$trace.anchorState -ceq 'ambiguous' -or $ambiguityReported -eq $true) { return $true }
    foreach ($call in @($trace.calls)) {
        $confidence = Get-PathValue $call.selectedResultFields 'result.confidence'
        if ([string]$confidence -ceq 'ambiguous') { return $true }
    }
    return $false
}
function Test-PredicateDefinition([object]$predicate, [string]$scenarioId) {
    $allowedKeys = @('path','type','equals','minimum','minItems','nonBlank','requiredProperties','minProperties')
    foreach ($property in $predicate.PSObject.Properties) { if ($property.Name -notin $allowedKeys) { throw "Unsupported stop predicate key '$($property.Name)' in $scenarioId" } }
    $type = [string]$predicate.type
    if ([string]::IsNullOrWhiteSpace([string]$predicate.path) -or $type -notin @('string','number','boolean','array','object')) { throw "Invalid stop predicate path/type in $scenarioId" }
    $ops = @('equals','minimum','minItems','nonBlank','requiredProperties','minProperties' | Where-Object { $null -ne $predicate.PSObject.Properties[$_] })
    if ($ops.Count -ne 1) { throw "Each stop predicate needs exactly one supported constraint in $scenarioId" }
    switch ($ops[0]) {
        'equals' {
            $equals = $predicate.equals
            $matches = switch ($type) { 'string' { $equals -is [string] } 'number' { Test-JsonInteger $equals } 'boolean' { $equals -is [bool] } 'array' { $equals -is [array] } 'object' { Test-IsJsonObject $equals } }
            if (!$matches) { throw "Stop predicate equals value does not match '$type' in $scenarioId" }
        }
        'minimum' { if ($type -ne 'number' -or !(Test-JsonInteger $predicate.minimum) -or [long]$predicate.minimum -lt 0) { throw "Invalid minimum predicate in $scenarioId" } }
        'minItems' { if ($type -ne 'array' -or !(Test-JsonInteger $predicate.minItems) -or [long]$predicate.minItems -lt 1) { throw "Invalid minItems predicate in $scenarioId" } }
        'nonBlank' { if ($type -ne 'string' -or $predicate.nonBlank -isnot [bool] -or !$predicate.nonBlank) { throw "Invalid nonBlank predicate in $scenarioId" } }
        'requiredProperties' {
            if ($type -ne 'object' -or $predicate.requiredProperties -isnot [array] -or @($predicate.requiredProperties).Count -eq 0) { throw "Invalid requiredProperties predicate in $scenarioId" }
            foreach ($name in @($predicate.requiredProperties)) { if ($name -isnot [string] -or [string]::IsNullOrWhiteSpace($name)) { throw "Invalid required property in $scenarioId" } }
        }
        'minProperties' { if ($type -ne 'object' -or !(Test-JsonInteger $predicate.minProperties) -or [long]$predicate.minProperties -lt 1) { throw "Invalid minProperties predicate in $scenarioId" } }
    }
}
function Test-ScenarioPredicates([object]$scenario) {
    $id = [string]$scenario.id
    if ($scenario.expectedStopEvidence.acceptedStopReasons -isnot [array] -or @($scenario.expectedStopEvidence.acceptedStopReasons).Count -eq 0 -or $scenario.expectedStopEvidence.alternatives -isnot [array] -or @($scenario.expectedStopEvidence.alternatives).Count -eq 0) { throw "Invalid expected stop evidence definition: $id" }
    foreach ($reason in @($scenario.expectedStopEvidence.acceptedStopReasons)) { if ($reason -isnot [string] -or [string]::IsNullOrWhiteSpace($reason)) { throw "Invalid accepted stop reason: $id" } }
    foreach ($alternative in @($scenario.expectedStopEvidence.alternatives)) {
        $required = @($alternative.requiredFields); $predicates = @($alternative.predicates)
        if (!(Test-JsonInteger $alternative.callIndex) -or [int]$alternative.callIndex -lt 0 -or [int]$alternative.callIndex -ge @($scenario.baselineTrace.calls).Count -or $required.Count -eq 0 -or $predicates.Count -eq 0 -or ($required.Count -eq 1 -and [string]$required[0] -ceq 'result.command')) { throw "Stop alternative has no meaningful predicate: $id" }
        foreach ($field in $required) { if ($field -isnot [string] -or [string]::IsNullOrWhiteSpace($field)) { throw "Invalid required evidence path: $id" } }
        $expectedPaths = @($required | Sort-Object -Unique)
        $paths = @($predicates | ForEach-Object { [string]$_.path })
        if (($paths | Sort-Object -Unique) -join "`n" -cne ($expectedPaths -join "`n") -or $paths.Count -ne $expectedPaths.Count) { throw "Stop predicate paths do not exactly cover requiredFields: $id" }
        foreach ($predicate in $predicates) { Test-PredicateDefinition $predicate $id }
    }
}
function Test-StrictReplayTrace([object]$trace) {
    $id = [string]$trace.scenarioId
    if ($trace.schemaVersion -ne 'navlyn.mcp-agent-trace.v2' -or $trace.scenarioId -isnot [string] -or [string]::IsNullOrWhiteSpace($id) -or
        $trace.skillActivated -isnot [bool] -or $trace.broadChecklist -isnot [bool] -or $trace.outputValid -isnot [bool] -or $trace.stderrClean -isnot [bool] -or
        $trace.stopReason -isnot [string] -or [string]::IsNullOrWhiteSpace($trace.stopReason) -or $trace.anchorState -isnot [string] -or [string]::IsNullOrWhiteSpace($trace.anchorState) -or
        $trace.calls -isnot [array] -or $trace.claims -isnot [array] -or !(Test-IsJsonObject $trace.semanticChecks) -or !(Test-IsJsonObject $trace.environment) -or
        !(Test-IsJsonObject $trace.editTiming) -or $trace.editTiming.editAttempted -isnot [bool] -or $trace.editTiming.anchorBeforeEdit -isnot [bool] -or
        !(Test-JsonInteger $trace.stdoutChars) -or !(Test-JsonInteger $trace.latencyMs) -or [long]$trace.stdoutChars -lt 0 -or [long]$trace.latencyMs -lt 0) {
        throw "Malformed required v2 replay trace fields: $id"
    }
    foreach ($property in $trace.semanticChecks.PSObject.Properties) { if ($property.Value -isnot [bool]) { throw "Semantic checks must be JSON booleans: $id/$($property.Name)" } }
    foreach ($property in $trace.environment.PSObject.Properties) { if ($property.Value -isnot [string]) { throw "Environment values must be JSON strings: $id/$($property.Name)" } }
    foreach ($field in @('mcpAvailability','workspaceAvailability','workspaceFreshness')) { if ($null -eq $trace.environment.PSObject.Properties[$field]) { throw "Missing environment string '$field': $id" } }
    foreach ($claim in @($trace.claims)) { if ($claim -isnot [string]) { throw "Claims must be JSON strings: $id" } }
    foreach ($call in @($trace.calls)) {
        if ($call.kind -isnot [string] -or $call.kind -notin @('mcp','ordinary') -or $call.name -isnot [string] -or [string]::IsNullOrWhiteSpace($call.name) -or !(Test-IsJsonObject $call.arguments) -or !(Test-IsJsonObject $call.selectedResultFields)) { throw "Malformed v2 replay call: $id" }
    }
}

$scenarioPath = Resolve-InputPath $ScenarioFile
$tracePath = Resolve-InputPath $TraceFile
if (!(Test-Path -LiteralPath $scenarioPath)) { throw "Scenario file does not exist: $scenarioPath" }
if (!(Test-Path -LiteralPath $tracePath)) { throw "Trace file does not exist: $tracePath" }
$scenarioDoc = Get-Content -Raw -LiteralPath $scenarioPath | ConvertFrom-Json -Depth 100
if ($scenarioDoc.schemaVersion -ne 'navlyn.tool-selection-eval.v2') { throw "Unsupported scenario schemaVersion: $($scenarioDoc.schemaVersion)" }
$traceDoc = Get-Content -Raw -LiteralPath $tracePath | ConvertFrom-Json -Depth 100
if ($traceDoc.schemaVersion -ne 'navlyn.mcp-agent-trace-eval.v2') { throw "Unsupported MCP trace eval schemaVersion: $($traceDoc.schemaVersion)" }

$mcpTools = @('navlyn_target','navlyn_read','navlyn_file_outline','navlyn_navigate','navlyn_prepare_edit','navlyn_verify_edit','navlyn_review','navlyn_workspace_summary','navlyn_workspace_status','navlyn_workspace_refresh','navlyn_doctor','navlyn_impact','navlyn_context_pack','navlyn_entrypoints','navlyn_tests_for_symbol','navlyn_tests_for_diff','navlyn_diagnostics','navlyn_di','navlyn_public_api_diff','navlyn_routes','navlyn_options','navlyn_messages','navlyn_ef','navlyn_packages','navlyn_batch')
$ordinaryNames = @('file-read','rg','git','build','test')
$scenarios = @{}
foreach ($scenario in @($scenarioDoc.scenarios)) { if ($scenarios.ContainsKey([string]$scenario.id)) { throw "Duplicate scenario id: $($scenario.id)" }; Test-ScenarioPredicates $scenario; $scenarios[[string]$scenario.id] = $scenario }
if ($traceDoc.traces -isnot [array]) { throw 'Replay traces must be a JSON array.' }
$seen = @{}
$results = [System.Collections.Generic.List[object]]::new()
$legacyMcp = 0
$mcpCalls = 0
$textOnlyCount = 0
$textOnlyFalsePositives = 0
$semanticDen = 0; $semanticNum = 0
$firstDen = 0; $firstNum = 0
$coreDen = 0; $coreNum = 0
$stopDen = 0; $stopNum = 0
$budgetDen = 0; $budgetNum = 0
$anchorDen = 0; $anchorNum = 0
$ambiguousEdits = 0; $staleSilent = 0; $unsupportedCount = 0; $broadCount = 0
$canonicalStdout = [System.Collections.Generic.List[int]]::new()
$allContractPass = $true
$textOnlyClasses = @('comments','strings','markdown','configuration','generated-artifact-text','arbitrary-text-search','simple-file-read','explicit-no-navlyn-override')
$coreTools = @('navlyn_target','navlyn_read','navlyn_file_outline','navlyn_navigate','navlyn_prepare_edit','navlyn_verify_edit','navlyn_review')

foreach ($trace in @($traceDoc.traces)) {
    $id = [string]$trace.scenarioId
    if ($seen.ContainsKey($id)) { throw "Duplicate trace scenarioId: $id" }
    $seen[$id] = $true
    if (!$scenarios.ContainsKey($id)) { throw "Trace references unknown scenarioId: $id" }
    Test-StrictReplayTrace $trace
    $scenario = $scenarios[$id]
    $calls = @($trace.calls)
    $actions = @($calls | ForEach-Object { [pscustomobject]@{ kind=$_.kind; name=$_.name } })
    $criteria = [ordered]@{}
    $criteria.skillActivation = [bool]$trace.skillActivated -eq [bool]$scenario.expectedSkillActivation
    $criteria.validActionNames = $true
    foreach ($call in $calls) {
        if ([string]$call.kind -ceq 'mcp') {
            $mcpCalls++
            if ([string]$call.name -notin $mcpTools) { $criteria.validActionNames = $false; $legacyMcp++ }
        } elseif ([string]$call.kind -ceq 'ordinary') {
            if ([string]$call.name -notin $ordinaryNames) { $criteria.validActionNames = $false }
        } else { $criteria.validActionNames = $false }
    }
    $expectedFirst = $scenario.expectedFirstAction
    $criteria.firstAction = $actions.Count -gt 0 -and (Test-ActionEqual $actions[0] $expectedFirst)
    $firstDen++; if ($criteria.firstAction) { $firstNum++ }
    if ([string]$expectedFirst.kind -ceq 'mcp') { $semanticDen++; if ($criteria.firstAction) { $semanticNum++ } }
    if ([string]$expectedFirst.kind -ceq 'mcp' -and [string]$expectedFirst.name -in $coreTools) { $coreDen++; if ($criteria.firstAction) { $coreNum++ }; $canonicalStdout.Add([int]$trace.stdoutChars) }
    $criteria.acceptedSequence = $false
    foreach ($seq in @($scenario.acceptedSequences)) { if (Test-SequenceEqual $actions @($seq)) { $criteria.acceptedSequence = $true; break } }
    $forbidden = $false
    foreach ($action in $actions) { foreach ($bad in @($scenario.forbiddenTools)) { if (Test-ActionEqual $action $bad) { $forbidden=$true } } }
    foreach ($seq in @($scenario.forbiddenSequences)) { if (Test-SequenceEqual $actions @($seq)) { $forbidden=$true } }
    $criteria.forbiddenAvoidance = !$forbidden
    $criteria.requiredArguments = $true
    foreach ($requirement in @($scenario.requiredArguments)) {
        $index = [int]$requirement.callIndex
        if ($index -lt 0 -or $index -ge $calls.Count) { $criteria.requiredArguments=$false; break }
        $actual = Get-PathValue $calls[$index].arguments ([string]$requirement.path)
        if (!(Test-JsonEqual $actual $requirement.equals)) { $criteria.requiredArguments=$false; break }
    }
    $criteria.stopEvidence = Test-StopEvidence $scenario $trace
    $stopDen++; if ($criteria.stopEvidence) { $stopNum++ }
    $criteria.semanticCorrectness = $true
    foreach ($check in @($scenario.semanticCorrectnessChecks)) { $v=Get-PathValue $trace.semanticChecks ([string]$check); if ($v -isnot [bool] -or $v -ne $true) { $criteria.semanticCorrectness=$false; break } }
    $criteria.environment = $true
    foreach ($field in @('mcpAvailability','workspaceAvailability','workspaceFreshness')) {
        if ([string](Get-PathValue $trace.environment $field) -cne [string]$scenario.availabilityFreshnessSetup.$field) { $criteria.environment=$false }
    }
    $criteria.unsupportedClaims = $true
    foreach ($claim in @($trace.claims)) { foreach ($unsupported in @($scenario.unsupportedClaims)) { if (([string]$claim).IndexOf([string]$unsupported, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) { $criteria.unsupportedClaims=$false; $unsupportedCount++ } } }
    $stdout = [int]$trace.stdoutChars; $latency = [int]$trace.latencyMs
    $criteria.callBudget = $calls.Count -le [int]$scenario.maxCalls
    $criteria.outputAndLatencyBudgets = $stdout -ge 0 -and $stdout -le [int]$scenario.stdoutBudgetChars -and $latency -ge 0 -and $latency -le [int]$scenario.latencyBudgetMs
    $criteria.callAndOutputBudgets = $criteria.callBudget -and $criteria.outputAndLatencyBudgets
    $budgetDen++; if ($criteria.callAndOutputBudgets) { $budgetNum++ }
    $criteria.outputValidity = [bool]$trace.outputValid -and [bool]$trace.stderrClean
    $criteria.editTiming = $trace.editTiming -and $trace.editTiming.PSObject.Properties['editAttempted'] -and $trace.editTiming.PSObject.Properties['anchorBeforeEdit'] -and $trace.PSObject.Properties['anchorState']
    $criteria.broadChecklistAvoided = $trace.PSObject.Properties['broadChecklist'] -and !( [bool]$trace.broadChecklist )
    if ([bool]$trace.broadChecklist) { $broadCount++ }
    $taskClass = [string]$scenario.taskClass
    if ($taskClass -in $textOnlyClasses) { $textOnlyCount++; if (@($calls | Where-Object kind -eq 'mcp').Count -gt 0) { $textOnlyFalsePositives++ } }
    $identityCriticalEdit = $trace.editTiming.editAttempted -and [string]$scenario.expectedFirstAction.kind -ceq 'mcp'
    if ($identityCriticalEdit) { $anchorDen++; if ($trace.editTiming.anchorBeforeEdit) { $anchorNum++ } }
    $criteria.identityCriticalEditAnchoredBeforeEdit = !$identityCriticalEdit -or [bool]$trace.editTiming.anchorBeforeEdit
    $ambiguousAndEdited = (Get-AmbiguityEvidence $trace) -and [bool]$trace.editTiming.editAttempted
    $criteria.ambiguousTargetFollowedByEdit = !$ambiguousAndEdited
    if ($ambiguousAndEdited) { $ambiguousEdits++ }
    $staleUnsafe = $taskClass -eq 'stale-candidate' -and ([bool]$trace.editTiming.editAttempted -or !$trace.semanticChecks.staleReported -or !$criteria.stopEvidence)
    $criteria.staleCandidateSilentReuse = !$staleUnsafe
    if ($staleUnsafe) { $staleSilent++ }
    $failedCriteria = @($criteria.Keys | Where-Object { !$criteria[$_] })
    $passed = $failedCriteria.Count -eq 0
    if (!$passed) { $allContractPass=$false }
    $results.Add([pscustomobject]@{scenarioId=$id;taskClass=$taskClass;points=(@($criteria.Values | Where-Object { $_ }).Count);maxPoints=$criteria.Count;passed=$passed;criteria=[pscustomobject]$criteria;actualCalls=$calls;stopReason=$trace.stopReason})
}

$p95 = 0; $maxStdout = 0
if ($canonicalStdout.Count -gt 0) { $sorted=@($canonicalStdout | Sort-Object); $p95=$sorted[[Math]::Ceiling($sorted.Count*0.95)-1]; $maxStdout=$sorted[-1] }
function Rate([int]$num,[int]$den,[double]$threshold,[string]$direction='min') {
    $value = if ($den -eq 0) { 0.0 } else { [double]$num / $den }
    $ok = if ($direction -eq 'max') { $value -le $threshold } else { $value -ge $threshold }
    return [pscustomobject]@{numerator=$num;denominator=$den;rate=[Math]::Round($value,4);threshold=$threshold;passed=($den -gt 0 -and $ok)}
}
$metrics = [ordered]@{
    semanticNavlynRecall = Rate $semanticNum $semanticDen 0.95
    identityCriticalEditAnchoredBeforeEdit = Rate $anchorNum $anchorDen 1.0
    textOnlyFalsePositiveRate = Rate $textOnlyFalsePositives $textOnlyCount 0.05 'max'
    correctFirstActionOverall = Rate $firstNum $firstDen 0.92
    correctFirstActionCoreSeven = Rate $coreNum $coreDen 0.95
    legacyMcpUse = Rate $legacyMcp $mcpCalls 0.0 'max'
    ambiguousTargetFollowedByEdit = [pscustomobject]@{cases=$ambiguousEdits;threshold=0;passed=($ambiguousEdits -eq 0)}
    staleCandidateSilentReuse = [pscustomobject]@{cases=$staleSilent;threshold=0;passed=($staleSilent -eq 0)}
    unsupportedClaims = [pscustomobject]@{cases=$unsupportedCount;threshold=0;passed=($unsupportedCount -eq 0)}
    evidenceBackedStop = Rate $stopNum $stopDen 0.95
    broadChecklistRate = Rate $broadCount @($results).Count 0.05 'max'
    withinCallBudget = Rate $budgetNum $budgetDen 0.95
    canonicalLoopStdout = [pscustomobject]@{sampleCount=$canonicalStdout.Count;p95Chars=$p95;maxChars=$maxStdout;p95Threshold=20000;maxThreshold=40000;passed=($canonicalStdout.Count -gt 0 -and $p95 -le 20000 -and $maxStdout -le 40000)}
}
$metricsPass = $true
foreach ($metric in $metrics.Values) { if (!$metric.passed) { $metricsPass=$false } }
$report = [ordered]@{
    schemaVersion='navlyn.mcp-agent-trace-eval.report.v2'
    traceSchemaVersion=$traceDoc.schemaVersion
    scenarioFile=[System.IO.Path]::GetRelativePath($repoRoot,$scenarioPath).Replace('\','/')
    traceFile=[System.IO.Path]::GetRelativePath($repoRoot,$tracePath).Replace('\','/')
    traceCount=$results.Count
    legacyMcpCallCount=$legacyMcp
    metrics=$metrics
    notApplicable=[ordered]@{
        skillOnLatencyRegressionVsSkillOff=[pscustomobject]@{status='not-applicable-live-only';reason='Synthetic replay fixtures are not paired live runs.'}
        toolsListJsonSizeVsV07=[pscustomobject]@{status='not-applicable';reason='This replay does not measure live tools/list payloads.'}
        newFocusedToolInvalidInputCoverage=[pscustomobject]@{status='not-applicable';reason='Focused builder and stdio contract tests establish invalid-input coverage.'}
        cliContractRegressions=[pscustomobject]@{status='not-applicable';reason='Replay traces do not establish CLI regression coverage.'}
    }
    passed=($allContractPass -and $metricsPass)
    results=$results.ToArray()
}
$json = $report | ConvertTo-Json -Depth 100
$outputPath = Resolve-InputPath $Output
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($outputPath)) | Out-Null
Set-Content -LiteralPath $outputPath -Value $json -Encoding utf8
Write-Output $json
if (!$report.passed) { throw 'MCP agent trace eval failed one or more per-trace or aggregate gates.' }

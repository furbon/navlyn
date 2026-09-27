[CmdletBinding()]
param(
    [switch]$Describe,
    [switch]$RunLive,
    [string]$TraceFile,
    [string]$ScenarioFile = 'docs/evals/tool-selection.scenarios.json',
    [string[]]$ScenarioIds,
    [int]$RunsPerCondition = 5,
    [string]$Client = 'codex.exe',
    [string]$Model = 'gpt-5.5',
    [ValidateSet('low','medium','high','xhigh')][string]$Reasoning = 'low',
    [ValidateSet('read-only','danger-full-access')][string]$Sandbox = 'read-only',
    [ValidateRange(1,3600)][int]$TimeoutSeconds = 300,
    [ValidateRange(1,4)][int]$MaxParallelism = 1,
    [string]$Output,
    [string]$RawRoot = 'artifacts/evals/routing-live/raw'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$defaultIds = @(
    'ambiguous-symbol-identity-01','exact-position-02','overload-03','partial-declaration-04','references-callers-05',
    'known-file-outline-06','multi-project-07','multi-target-08','linked-file-09','generated-code-avoidance-10',
    'comments-23','strings-24','markdown-25','configuration-26','arbitrary-text-search-28',
    'ambiguity-31','stale-candidate-32','stale-workspace-33','unavailable-mcp-35','explicit-no-navlyn-override-37'
)
$semanticIds = @($defaultIds[0..9])
$textIds = @($defaultIds[10..14])
$safetyIds = @($defaultIds[15..19])
$currentTools = @('navlyn_target','navlyn_read','navlyn_file_outline','navlyn_navigate','navlyn_prepare_edit','navlyn_verify_edit','navlyn_review','navlyn_workspace_summary','navlyn_workspace_status','navlyn_workspace_refresh','navlyn_doctor','navlyn_impact','navlyn_context_pack','navlyn_entrypoints','navlyn_tests_for_symbol','navlyn_tests_for_diff','navlyn_diagnostics','navlyn_di','navlyn_public_api_diff','navlyn_routes','navlyn_options','navlyn_messages','navlyn_ef','navlyn_packages','navlyn_batch')
$coreTools = @('navlyn_target','navlyn_read','navlyn_file_outline','navlyn_navigate','navlyn_prepare_edit','navlyn_verify_edit','navlyn_review')
$ordinaryTools = @('file-read','rg','git','build','test')

function Fail([string]$message) { throw $message }
function Read-Json([string]$path) { return Get-Content -Raw -LiteralPath $path | ConvertFrom-Json -Depth 100 }
function Resolve-Input([string]$path) { if ([IO.Path]::IsPathRooted($path)) { return [IO.Path]::GetFullPath($path) }; return [IO.Path]::GetFullPath((Join-Path $repoRoot $path)) }
function Get-Group([string]$id) { if ($id -in $semanticIds) { return 'semantic' }; if ($id -in $textIds) { return 'text' }; if ($id -in $safetyIds) { return 'safety' }; return $null }
function Get-Scenarios {
    $path = Resolve-Input $ScenarioFile
    if (!(Test-Path -LiteralPath $path)) { Fail "Scenario file not found: $path" }
    $doc = Read-Json $path
    if ($doc.schemaVersion -ne 'navlyn.tool-selection-eval.v2') { Fail 'Unsupported authoritative P6A scenario version.' }
    $available = @{}; foreach ($item in @($doc.scenarios)) { $available[$item.id] = $item }
    $ids = if ($ScenarioIds -and $ScenarioIds.Count) { if($ScenarioIds.Count -eq 1 -and $ScenarioIds[0].Contains(',')){@($ScenarioIds[0].Split(',')|ForEach-Object {$_.Trim()}|Where-Object {$_})}else{@($ScenarioIds)} } else { $defaultIds }
    if (@($ids | Sort-Object -Unique).Count -ne $ids.Count) { Fail 'Scenario subset contains duplicates.' }
    foreach ($id in $ids) { if ($id -notin $defaultIds -or !$available.ContainsKey($id)) { Fail "Unknown or out-of-subset scenario id: $id" } }
    $groups = @($ids | ForEach-Object { Get-Group $_ })
    if ($groups -contains $null) { Fail 'Every selected scenario must belong to the fixed semantic/text/safety groups.' }
    if (@($groups | Sort-Object -Unique).Count -ne 3) { Fail 'Selected scenarios must include at least one semantic, text, and safety case.' }
    if (!$ScenarioIds -and (($ids.Count -ne 20) -or (@($ids | Where-Object { $_ -in $semanticIds }).Count -ne 10) -or (@($ids | Where-Object { $_ -in $textIds }).Count -ne 5) -or (@($ids | Where-Object { $_ -in $safetyIds }).Count -ne 5))) { Fail 'Default live set must be balanced 10 semantic / 5 text / 5 safety.' }
    return @($ids | ForEach-Object { $available[$_] })
}
function Test-JsonObject($value) { return $null -ne $value -and $value -is [Management.Automation.PSCustomObject] }
function Test-JsonInteger($value) { return $value -is [sbyte] -or $value -is [byte] -or $value -is [int16] -or $value -is [uint16] -or $value -is [int] -or $value -is [uint32] -or $value -is [long] -or $value -is [ulong] }
function Has-JsonProperty($obj,[string]$name) { return (Test-JsonObject $obj) -and $null -ne $obj.PSObject.Properties[$name] }
function Get-OrdinaryArguments([string]$kind,[string]$command) {
    $tokens=@([regex]::Matches($command,'"([^"\\]|\\.)*"|''[^'']*''|\S+')|ForEach-Object { $_.Value.Trim([char]'"',[char]39) })
    $commandIndex=[array]::IndexOf($tokens,'-Command')
    if($commandIndex -ge 0 -and $commandIndex+1 -lt $tokens.Count){
        $innerCommand=$tokens[$commandIndex+1].Replace('\"','"')
        $tokens=@([regex]::Matches($innerCommand,'"([^"\\]|\\.)*"|''[^'']*''|\S+')|ForEach-Object { $_.Value.Trim([char]'"',[char]39) })
    }
    $result=[ordered]@{command=$command}
    if($kind -eq 'rg') {
        $rgIndex=-1;for($i=0;$i -lt $tokens.Count;$i++){if($tokens[$i] -match '(^|[/\\])rg(\.exe)?$'){$rgIndex=$i;break}}
        $patternIndex=-1
        for($i=$rgIndex+1;$i -lt $tokens.Count;$i++){if($tokens[$i] -eq '--'){if($i+1 -lt $tokens.Count){$patternIndex=$i+1};break};if($tokens[$i] -notmatch '^-'){$patternIndex=$i;break}}
        if($patternIndex -ge 0){$result.pattern=$tokens[$patternIndex]}
        for($i=0;$i -lt $tokens.Count-1;$i++){if($tokens[$i] -eq '--glob'){$result.glob=$tokens[$i+1]}elseif($tokens[$i] -eq '--type'){$result.type=$tokens[$i+1]}}
        $paths=@($tokens|Where-Object {$_ -notmatch '^-'}|Select-Object -Skip 2);if($paths.Count -gt 0){$result.path=$paths[-1]}
    } elseif($kind -eq 'file-read') {
        for($i=0;$i -lt $tokens.Count-1;$i++){if($tokens[$i] -in @('-LiteralPath','-Path')){$result.path=$tokens[$i+1];break}}
        if(!$result.Contains('path') -and $tokens.Count -gt 1){$result.path=$tokens[-1]}
    }
    return [pscustomobject]$result
}
function Get-EventOffset([object]$event,[DateTimeOffset]$runStarted) {
    foreach($field in @('timestamp','created_at','time')){
        if(Has-JsonProperty $event $field){try{$time=[DateTimeOffset]::Parse([string]$event.$field);return [Math]::Max(0,[int]($time-$runStarted).TotalMilliseconds)}catch{}}
    }
    return 0
}
function Get-UsageObject([object]$event) {
    $usage=if(Has-JsonProperty $event 'usage'){$event.usage}elseif((Has-JsonProperty $event 'item') -and (Has-JsonProperty $event.item 'usage')){$event.item.usage}else{$null}
    if(!(Test-JsonObject $usage)){return $null}
    $inputTokens=if(Has-JsonProperty $usage 'input_tokens'){$usage.input_tokens}elseif(Has-JsonProperty $usage 'inputTokens'){$usage.inputTokens}else{$null}
    $outputTokens=if(Has-JsonProperty $usage 'output_tokens'){$usage.output_tokens}elseif(Has-JsonProperty $usage 'outputTokens'){$usage.outputTokens}else{$null}
    $totalTokens=if(Has-JsonProperty $usage 'total_tokens'){$usage.total_tokens}elseif(Has-JsonProperty $usage 'totalTokens'){$usage.totalTokens}else{$null}
    return [pscustomobject]@{inputTokens=$inputTokens;outputTokens=$outputTokens;totalTokens=$totalTokens}
}
function Set-CodexWindowsShellPath([Diagnostics.ProcessStartInfo]$startInfo) {
    foreach ($key in @($startInfo.Environment.Keys)) {
        if ($key -like 'CODEX_*' -and $key -ne 'CODEX_HOME') { [void]$startInfo.Environment.Remove($key) }
    }
    if (!$IsWindows) { return }
    $windowsPowerShell = Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe'
    if (!(Test-Path -LiteralPath $windowsPowerShell -PathType Leaf)) { Fail 'Windows PowerShell executable is unavailable for the isolated Codex shell.' }
    $entries = @($startInfo.Environment['PATH'] -split ';' | Where-Object { $_ -and $_ -notmatch '(?i)WindowsApps' })
    if ($entries.Count -eq 0) { Fail 'Isolated Codex shell PATH is empty.' }
    $startInfo.Environment['PATH'] = ($entries -join ';')
}
function Test-PromptInputSkillVisibility([string]$conditionRoot,[string]$condition,[string]$artifactDirectory) {
    $psi=[Diagnostics.ProcessStartInfo]::new($Client);$psi.WorkingDirectory=$conditionRoot;$psi.UseShellExecute=$false;$psi.RedirectStandardOutput=$true;$psi.RedirectStandardError=$true
    if ($Sandbox -eq 'read-only') { Set-CodexWindowsShellPath $psi }
    foreach($arg in @('debug','prompt-input','--disable','plugins','--disable','shell_snapshot','Inspect which repository-local agent skills are available from this working root for C# semantic navigation. Report names and paths only.')){$psi.ArgumentList.Add($arg)}
    $process=[Diagnostics.Process]::new();$process.StartInfo=$psi;$process.Start()|Out-Null
    $outTask=$process.StandardOutput.ReadToEndAsync();$errTask=$process.StandardError.ReadToEndAsync();$finished=$process.WaitForExit(60000)
    if(!$finished){try{$process.Kill($true)}catch{};Fail "Codex prompt-input skill discovery timed out for $condition."}
    $process.WaitForExit();$output=$outTask.GetAwaiter().GetResult();$errorText=$errTask.GetAwaiter().GetResult()
    $path=Join-Path $artifactDirectory "discovery-$condition.prompt-input.json";Set-Content -LiteralPath $path -Value $output -Encoding utf8
    if($process.ExitCode -ne 0){Fail "Codex prompt-input skill discovery failed for ${condition}: $errorText"}
    $visible=$output -match '(?i)navlyn-semantic-routing'
    return [pscustomobject]@{visible=$visible;exitCode=$process.ExitCode;artifact=[IO.Path]::GetFileName($path);stderrChars=$errorText.Length;matchedPath=if($visible){'navlyn-semantic-routing'}else{$null}}
}
function Get-AtPath($obj,[string]$path) { $value=$obj; foreach($part in $path.Split('.')) { if (!(Test-JsonObject $value) -or $null -eq $value.PSObject.Properties[$part]) { return @{found=$false;value=$null} }; $value=$value.$part }; return @{found=$true;value=$value} }
function Test-Predicate($root,$predicate) {
    $hit=Get-AtPath $root ([string]$predicate.path); if(!$hit.found){return $false}; $v=$hit.value
    switch ([string]$predicate.type) {
        'string' { if($v -isnot [string]){return $false}; if((Has-JsonProperty $predicate 'nonBlank') -and $predicate.nonBlank -and [string]::IsNullOrWhiteSpace($v)){return $false}; if((Has-JsonProperty $predicate 'equals') -and $v -cne [string]$predicate.equals){return $false}; return $true }
        'number' { if($v -isnot [ValueType] -or $v -is [bool]){return $false}; if((Has-JsonProperty $predicate 'equals') -and [double]$v -ne [double]$predicate.equals){return $false}; if((Has-JsonProperty $predicate 'minimum') -and [double]$v -lt [double]$predicate.minimum){return $false}; return $true }
        'boolean' { return $v -is [bool] -and $v -eq [bool]$predicate.equals }
        'array' { if($v -isnot [array]){return $false}; if((Has-JsonProperty $predicate 'minItems') -and $v.Count -lt [int]$predicate.minItems){return $false}; return $true }
        'object' { if(!(Test-JsonObject $v)){return $false}; if(Has-JsonProperty $predicate 'requiredProperties'){foreach($p in @($predicate.requiredProperties)){if($null -eq $v.PSObject.Properties[[string]$p]){return $false}}}; if((Has-JsonProperty $predicate 'minProperties') -and @($v.PSObject.Properties).Count -lt [int]$predicate.minProperties){return $false}; return $true }
    }
    return $false
}
function Test-StopEvidence($scenario,$run) {
    if($run.final.stopReason -cnotin @($scenario.expectedStopEvidence.acceptedStopReasons)){return $false}
    $taskCalls=@($run.calls|Where-Object {!$_.skillLoading})
    foreach($alt in @($scenario.expectedStopEvidence.alternatives)) {
        $index=[int]$alt.callIndex; if($index -ge $taskCalls.Count){continue}
        $all=$true; foreach($pred in @($alt.predicates)){if(!(Test-Predicate $taskCalls[$index].selectedResultFields $pred)){$all=$false;break}}
        if($all){return $true}
    }
    return $false
}
function Test-StructuredFinal($run) {
    $structured=$run.final.structuredResponse
    if(!(Test-JsonObject $structured) -or $run.final.outputValid -ne $true){return $false}
    foreach($name in @('answer','stopReason','claims','editAttempted','anchorBeforeEdit','ambiguityReported','staleReported','partialResult')){if(!(Has-JsonProperty $structured $name)){return $false}}
    if($structured.answer -isnot [string] -or [string]::IsNullOrWhiteSpace([string]$structured.answer) -or $structured.stopReason -isnot [string] -or [string]::IsNullOrWhiteSpace([string]$structured.stopReason) -or $structured.claims -isnot [array]){return $false}
    foreach($name in @('editAttempted','anchorBeforeEdit','ambiguityReported','staleReported','partialResult')){if($structured.$name -isnot [bool]){return $false}}
    foreach($name in @('answer','stopReason','claims','editAttempted','anchorBeforeEdit','ambiguityReported','staleReported','partialResult')){if(($structured.$name|ConvertTo-Json -Compress -Depth 30) -cne ($run.final.$name|ConvertTo-Json -Compress -Depth 30)){return $false}}
    return $true
}
function Test-Call($call) {
    if($call.kind -eq 'mcp'){return $call.name -notmatch '^navlyn_' -or $call.name -in $currentTools}
    if($call.kind -eq 'ordinary'){return $call.name -in $ordinaryTools}
    return $false
}
function Test-SequenceAt($actual,[object[]]$expected,[int]$start) {
    if($start+$expected.Count -gt $actual.Count){return $false}
    for($i=0;$i -lt $expected.Count;$i++){if($actual[$start+$i].kind -ne $expected[$i].kind -or $actual[$start+$i].name -ne $expected[$i].name){return $false}}
    return $true
}
function Test-UnsupportedClaims($scenario,$run) {
    foreach($text in @(@($run.final.claims) + @([string]$run.final.answer))){foreach($unsupported in @($scenario.unsupportedClaims)){
        $value=[string]$text
        $index=$value.IndexOf([string]$unsupported,[StringComparison]::OrdinalIgnoreCase)
        while($index -ge 0){
            $prefix=$value.Substring([Math]::Max(0,$index-48),[Math]::Min(48,$index))
            $nextStart=$index+([string]$unsupported).Length
            $suffix=$value.Substring($nextStart)
            $sentenceBoundary=[regex]::Match($suffix,'[.!?\r\n]')
            $sentenceSuffix=if($sentenceBoundary.Success){$suffix.Substring(0,$sentenceBoundary.Index)}else{$suffix}
            $afterSentence=if($sentenceBoundary.Success){$suffix.Substring($sentenceBoundary.Index+$sentenceBoundary.Length)}else{''}
            $directPrefix=$prefix -match '(?i)\b(does not|doesn''t|cannot|can''t)\s+(prove|establish|guarantee|verify|execute|know)\s*$' -or
                $prefix -match '(?i)\b(no|without|not)\s+(evidence|proof)(\s+(of|for))?\s*$'
            $negativeSuffixPattern='(?i)^[\s,:;-]{0,4}(and\s+[^,.;]{1,40}\s+as\s+)?(is|are|was|were|has|have|can)?\s*(not|never)\s+(proven|established|guaranteed|verified|executed|known)\b'
            $negativeSuffixMatch=[regex]::Match($sentenceSuffix,$negativeSuffixPattern)
            $prefixBound=$directPrefix -and $sentenceSuffix -match '^[\s,:;-]*$'
            $suffixBound=$false
            if($negativeSuffixMatch.Success){
                $negativeTail=$sentenceSuffix.Substring($negativeSuffixMatch.Length)
                $suffixBound=$negativeTail -match '^[\s,:;-]*$' -or
                    $negativeTail -match '(?i)^\s+because\s+(this|it)\s+was\s+static(\s+[a-z-]+){0,4}\s+evidence\s+only\s*$'
            }
            $negated=($prefixBound -or $suffixBound) -and $afterSentence -notmatch '\S'
            if(!$negated){return $false}
            $index=if($nextStart -lt $value.Length){$value.IndexOf([string]$unsupported,$nextStart,[StringComparison]::OrdinalIgnoreCase)}else{-1}
        }
    }}
    return $true
}
function Get-NormalizedComparisonValue($value,[string]$path) {
    if($value -is [string] -and $path.Split('.')[-1] -in @('file','path','project','workspace','fixture')){
        $normalized=([string]$value).Replace('\','/')
        $normalizedRoot=$repoRoot.Replace('\','/').TrimEnd('/')+'/'
        if($normalized.StartsWith($normalizedRoot,[StringComparison]::OrdinalIgnoreCase)){$normalized=$normalized.Substring($normalizedRoot.Length)}
        return $normalized.TrimStart('./')
    }
    return $value
}
function Get-BooleanRate($items,[string]$property) {
    $list=@($items);$good=@($list|Where-Object { [bool]$_.PSObject.Properties[$property].Value }).Count
    return [ordered]@{numerator=$good;denominator=$list.Count;rate=if($list.Count){[Math]::Round($good/$list.Count,4)}else{$null}}
}
function Test-Run($scenario,$run) {
    $calls=@($run.calls); $task=@($calls | Where-Object { !$_.skillLoading })
    $validNames=@($calls | ForEach-Object { Test-Call $_ } | Where-Object { !$_ }).Count -eq 0
    $first=$task.Count -gt 0 -and $task[0].kind -eq $scenario.expectedFirstAction.kind -and $task[0].name -eq $scenario.expectedFirstAction.name
    if(!$first -and $task.Count -gt 0){foreach($seq in @($scenario.acceptedSequences)){$items=@($seq);if($items.Count -gt 0 -and $task[0].kind -eq $items[0].kind -and $task[0].name -eq $items[0].name){$first=$true;break}}}
    $accepted=$false; foreach($seq in @($scenario.acceptedSequences)){if($seq.Count -eq $task.Count){$same=$true;for($i=0;$i -lt $seq.Count;$i++){if($task[$i].kind -ne $seq[$i].kind -or $task[$i].name -ne $seq[$i].name){$same=$false}};if($same){$accepted=$true}}}
    $forbidden=$false; foreach($c in $task){foreach($f in @($scenario.forbiddenTools)){if($c.kind -eq $f.kind -and $c.name -eq $f.name){$forbidden=$true}}}
    foreach($sequence in @($scenario.forbiddenSequences)){$sequenceItems=@($sequence);for($start=0;$start -le ($task.Count-$sequenceItems.Count);$start++){if(Test-SequenceAt $task $sequenceItems $start){$forbidden=$true}}}
    $required=$true; foreach($req in @($scenario.requiredArguments)){if([int]$req.callIndex -ge $task.Count){$required=$false;break};$hit=Get-AtPath $task[[int]$req.callIndex].arguments ([string]$req.path);$actual=Get-NormalizedComparisonValue $hit.value ([string]$req.path);$expected=Get-NormalizedComparisonValue $req.equals ([string]$req.path);if(!$hit.found -or ($actual | ConvertTo-Json -Compress -Depth 30) -cne ($expected | ConvertTo-Json -Compress -Depth 30)){$required=$false;break}}
    $stop=Test-StopEvidence $scenario $run
    $mcp=@($task | Where-Object kind -eq 'mcp')
    $semantic=if((Get-Group $scenario.id) -eq 'semantic'){$mcp.Count -gt 0 -and $stop -and $accepted -and $required -and !$forbidden}else{$true}
    $expectedActivation=($run.condition -eq 'on' -and [bool]$scenario.expectedSkillActivation)
    $activation=([bool]$run.skill.activated -eq $expectedActivation)
    $envOk=($run.environment.mcpAvailability -eq $scenario.availabilityFreshnessSetup.mcpAvailability -and $run.environment.workspaceAvailability -eq $scenario.availabilityFreshnessSetup.workspaceAvailability -and $run.environment.workspaceFreshness -eq $scenario.availabilityFreshnessSetup.workspaceFreshness)
    $actualWrites=@($calls | Where-Object { $_.writeAttempt -eq $true })
    $editIntent=($scenario.taskClass -eq 'partial-declaration' -or $run.final.editAttempted -or $actualWrites.Count -gt 0)
    $anchorIndex=-1
    for($i=0;$i -lt $calls.Count;$i++) {
        $call=$calls[$i]
        if($call.kind -eq 'mcp' -and $call.name -eq 'navlyn_prepare_edit') {
            $anchor=Get-AtPath $call.selectedResultFields 'result.anchor.path'
            $confidence=Get-AtPath $call.selectedResultFields 'result.confidence.overall'
            if($anchor.found -and ![string]::IsNullOrWhiteSpace([string]$anchor.value) -and $confidence.found){$anchorIndex=$i;break}
        }
    }
    $writeIndex=if($actualWrites.Count){[array]::IndexOf($calls,$actualWrites[0])}else{[int]::MaxValue}
    $anchored=(!$editIntent) -or ($anchorIndex -ge 0 -and $anchorIndex -lt $writeIndex)
    $isAmbiguous=$false
    foreach($call in $calls){$confidence=Get-AtPath $call.selectedResultFields 'result.confidence';$count=Get-AtPath $call.selectedResultFields 'result.candidateCount';if(($confidence.found -and $confidence.value -eq 'ambiguous') -or ($count.found -and [int]$count.value -gt 1)){$isAmbiguous=$true}}
    $attempted=$run.final.editAttempted -or $actualWrites.Count -gt 0
    $ambiguousEditSafe=!($isAmbiguous -and $attempted)
    $staleSilentSafe=!(($scenario.taskClass -eq 'stale-candidate') -and ($attempted -or !$stop))
    $editSafe=$anchored -and $ambiguousEditSafe -and $staleSilentSafe
    $navlynMcp=@($task | Where-Object { $_.kind -eq 'mcp' -and $_.name -match '^navlyn_' })
    $mcpFalsePositive=($navlynMcp.Count -gt 0 -and (Get-Group $scenario.id) -eq 'text')
    $broad=($task.Count -gt [int]$scenario.maxCalls -or @($task|Where-Object { $_.kind -eq 'mcp' -and $_.name -eq 'navlyn_batch' }).Count -gt 0)
    $exitSucceeded=(Has-JsonProperty $run 'exitCode') -and $null -ne $run.exitCode -and [int]$run.exitCode -eq 0
    $outputValid=Test-StructuredFinal $run
    $stderrClean=(Has-JsonProperty $run 'diagnosticStderrChars') -and [int]$run.diagnosticStderrChars -eq 0
    return [pscustomobject]@{
        validActionNames=$validNames; skillActivation=$activation; firstAction=$first; acceptedSequence=$accepted; forbiddenAvoidance=!$forbidden; requiredArguments=$required; evidenceBackedStop=$stop; stopReasonMatchesContract=($run.final.stopReason -cin @($scenario.expectedStopEvidence.acceptedStopReasons)); semanticCorrectness=$semantic; availabilityFreshness=$envOk; unsupportedClaims=(Test-UnsupportedClaims $scenario $run); callBudget=($task.Count -le [int]$scenario.maxCalls); budgets=($task.Count -le [int]$scenario.maxCalls -and $run.canonicalOutputChars -ge 0 -and $run.canonicalOutputChars -le [int]$scenario.stdoutBudgetChars -and $run.durationMs -le [int]$scenario.latencyBudgetMs); clientCompletionAndStructuredOutput=($outputValid -and $exitSucceeded); diagnosticStderrClean=$stderrClean; editSafety=$editSafe; editIntent=$editIntent; ambiguousEditAvoidance=$ambiguousEditSafe; staleReuseAvoidance=$staleSilentSafe; textNoMcp=(!$mcpFalsePositive -and !([bool]$run.skill.activated)); broadChecklist=!$broad; callCount=$task.Count; mcpCallCount=$mcp.Count; durationMs=$run.durationMs; stdoutChars=$run.canonicalOutputChars
    }
}
function Get-PairKey($run) { return "$($run.scenarioId)|$($run.repetition)" }
function Score-Trace($trace,$scenarios) {
    if($trace.schemaVersion -ne 'navlyn.routing-skill-live-trace.v1'){Fail 'Unsupported live trace schemaVersion.'}
    if(!(Test-JsonInteger $trace.runsPerCondition) -or $trace.runsPerCondition -lt 1 -or $trace.runs -isnot [array] -or $trace.subset -isnot [array]){Fail 'Malformed live trace root repetition/subset/runs fields.'}
    if(@($trace.subset).Count -ne @($scenarios).Count -or (@($trace.subset) -join "`n") -cne (@($scenarios | ForEach-Object id) -join "`n")){Fail 'Trace subset does not match the authoritative selected scenario order.'}
    foreach($field in @('repository','client','configuration','skill','discovery','groupCounts')){if(!(Has-JsonProperty $trace $field) -or !(Test-JsonObject $trace.$field)){Fail "Malformed required trace metadata object: $field"}}
    if($trace.scenarioFile -cne 'docs/evals/tool-selection.scenarios.json' -or $trace.scenarioSha256 -cne (Get-Hash (Resolve-Input $ScenarioFile))){Fail 'Trace source scenario path/hash does not match current authoritative P6A input.'}
    if($trace.client.name -cne 'codex' -or [string]::IsNullOrWhiteSpace([string]$trace.client.version) -or [string]::IsNullOrWhiteSpace([string]$trace.client.model) -or [string]::IsNullOrWhiteSpace([string]$trace.client.reasoning)){Fail 'Trace client identity is incomplete.'}
    if($trace.skill.name -cne 'navlyn-semantic-routing' -or [string]::IsNullOrWhiteSpace([string]$trace.skill.path) -or [string]$trace.skill.sha256 -notmatch '^[A-Fa-f0-9]{64}$'){Fail 'Trace skill identity/hash is invalid.'}
    if($trace.discovery.offSkillVisible -isnot [bool] -or $trace.discovery.onSkillVisible -isnot [bool] -or $trace.discovery.offSkillVisible -or !$trace.discovery.onSkillVisible){Fail 'Skill visibility discovery evidence must prove local skill is visible only in on.'}
    $scenarioMap=@{}; foreach($s in $scenarios){$scenarioMap[$s.id]=$s}
    $seen=@{}; foreach($run in @($trace.runs)) {
        if(!$scenarioMap.ContainsKey([string]$run.scenarioId)){Fail "Unknown scenario in trace: $($run.scenarioId)"}
        $key="$(Get-PairKey $run)|$($run.condition)"; if($seen.ContainsKey($key)){Fail "Duplicate run: $key"}; $seen[$key]=$run
        if($run.condition -notin @('off','on') -or $run.group -ne (Get-Group $run.scenarioId) -or $run.taskClass -ne $scenarioMap[$run.scenarioId].taskClass){Fail "Run identity/group mismatch: $key"}
        if($run.final.claims -isnot [array] -or $run.calls -isnot [array] -or $run.rawCallOrder -isnot [array] -or $run.skill.activated -isnot [bool] -or $run.skill.available -isnot [bool] -or $run.final.outputValid -isnot [bool] -or $run.final.stderrClean -isnot [bool] -or $run.final.editAttempted -isnot [bool] -or $run.final.anchorBeforeEdit -isnot [bool] -or $run.final.ambiguityReported -isnot [bool] -or $run.final.staleReported -isnot [bool] -or $run.final.partialResult -isnot [bool] -or $run.final.stopReason -isnot [string] -or [string]::IsNullOrWhiteSpace($run.final.stopReason) -or $run.final.answer -isnot [string]) {Fail "Malformed trace booleans/arrays/final response: $key"}
        foreach($metadataObject in @('prompt','environment','configuration','skill','final')){if(!(Test-JsonObject $run.$metadataObject)){Fail "Malformed per-run object '$metadataObject': $key"}}
        if($run.prompt.text -isnot [string] -or $run.prompt.scenarioPrompt -isnot [string] -or $run.prompt.workspace -isnot [string] -or $run.prompt.fixture -isnot [string] -or ($run.startedAtUtc -isnot [string] -and $run.startedAtUtc -isnot [DateTime]) -or $run.rawJsonl -isnot [string]){Fail "Malformed prompt/time/path strings: $key"}
        if($run.configuration -isnot [Management.Automation.PSCustomObject]){Fail "Missing per-run configuration: $key"}
        if(($null -ne $run.exitCode -and !(Test-JsonInteger $run.exitCode)) -or !(Test-JsonInteger $run.durationMs) -or !(Test-JsonInteger $run.stdoutChars) -or !(Test-JsonInteger $run.canonicalOutputChars) -or !(Test-JsonInteger $run.stderrChars) -or !(Test-JsonInteger $run.diagnosticStderrChars) -or [long]$run.durationMs -lt 0 -or [long]$run.stdoutChars -lt 0 -or [long]$run.canonicalOutputChars -lt 0 -or [long]$run.stderrChars -lt 0 -or [long]$run.diagnosticStderrChars -lt 0){Fail "Malformed exit/time/output type: $key"}
        if(!(Test-JsonInteger $run.repetition) -or $run.repetition -lt 1 -or [string]::IsNullOrWhiteSpace([string]$run.runId) -or ($null -ne $run.tokenUsage -and !(Test-JsonObject $run.tokenUsage)) -or $run.group -notin @('semantic','text','safety')){Fail "Malformed repetition/id/group/usage: $key"}
        if($trace.groupCounts.semantic -ne @($scenarios|Where-Object {(Get-Group $_.id) -eq 'semantic'}).Count -or $trace.groupCounts.text -ne @($scenarios|Where-Object {(Get-Group $_.id) -eq 'text'}).Count -or $trace.groupCounts.safety -ne @($scenarios|Where-Object {(Get-Group $_.id) -eq 'safety'}).Count){Fail 'Trace group counts do not match selected scenarios.'}
        foreach($call in @($run.calls)){if($call.kind -notin @('mcp','ordinary','other') -or [string]::IsNullOrWhiteSpace([string]$call.name) -or !(Test-JsonObject $call.arguments) -or !(Test-JsonObject $call.selectedResultFields) -or !(Test-JsonInteger $call.startOffsetMs) -or !(Test-JsonInteger $call.durationMs) -or !(Test-JsonInteger $call.outputChars) -or $call.startOffsetMs -lt 0 -or $call.durationMs -lt 0 -or $call.outputChars -lt 0 -or $call.skillLoading -isnot [bool] -or $call.writeAttempt -isnot [bool] -or $call.status -isnot [string]){Fail "Malformed call: $key"}}
        if($null -ne $trace.configuration){}
    }
    $expected=@($scenarios).Count*[int]$trace.runsPerCondition*2
    $complete=@($trace.runs).Count -eq $expected -and [int]$trace.runsPerCondition -ge 5 -and @($scenarios).Count -eq 20
    $pairOk=$true
    foreach($s in $scenarios){for($rep=1;$rep -le [int]$trace.runsPerCondition;$rep++){$offKey="$($s.id)|$rep|off";$onKey="$($s.id)|$rep|on";if(!$seen.ContainsKey($offKey)-or !$seen.ContainsKey($onKey)){$pairOk=$false;continue};$off=$seen[$offKey];$on=$seen[$onKey];if($off.prompt.text -cne $on.prompt.text -or ($off.environment|ConvertTo-Json -Compress -Depth 20) -cne ($on.environment|ConvertTo-Json -Compress -Depth 20) -or ($off.configuration|ConvertTo-Json -Compress -Depth 20) -cne ($on.configuration|ConvertTo-Json -Compress -Depth 20)){$pairOk=$false}}}
    $rows=New-Object Collections.Generic.List[object]
    foreach($run in @($trace.runs)){ $scenario=$scenarioMap[$run.scenarioId]; $criteria=Test-Run $scenario $run; $criteria | Add-Member -NotePropertyName runId -NotePropertyValue $run.runId; $criteria | Add-Member -NotePropertyName scenarioId -NotePropertyValue $run.scenarioId; $criteria | Add-Member -NotePropertyName condition -NotePropertyValue $run.condition; $rows.Add($criteria) }
    $runOn=@($rows | Where-Object condition -eq on);$runOff=@($rows | Where-Object condition -eq off)
    $semanticOn=@($runOn | Where-Object { (Get-Group $_.scenarioId) -eq 'semantic' });$semanticOff=@($runOff | Where-Object { (Get-Group $_.scenarioId) -eq 'semantic' })
    $textOn=@($runOn | Where-Object { (Get-Group $_.scenarioId) -eq 'text' })
    $firstOverall=@($runOn | Where-Object firstAction).Count;$coreScenarioIds=@($scenarios|Where-Object {$_.expectedFirstAction.kind -eq 'mcp' -and $_.expectedFirstAction.name -in $coreTools}|ForEach-Object id);$firstCore=@($runOn | Where-Object { $_.scenarioId -in $coreScenarioIds -and $_.firstAction }).Count;$coreDenominator=@($runOn|Where-Object {$_.scenarioId -in $coreScenarioIds}).Count
    $identityRows=@($runOn|Where-Object {$_.editIntent -or $_.scenarioId -eq 'partial-declaration-04' -or $_.scenarioId -eq 'pre-edit-11'})
    $latencies=@($runOn | ForEach-Object {[int]$_.durationMs});$sorted=@($latencies | Sort-Object);$latencyP95=if($sorted.Count){$sorted[[Math]::Ceiling(.95*$sorted.Count)-1]}else{0};$latencyMax=if($sorted.Count){($sorted|Measure-Object -Maximum).Maximum}else{0}
    $offLatencies=@($runOff|ForEach-Object {[int]$_.durationMs}|Sort-Object);$offLatencyP95=if($offLatencies.Count){$offLatencies[[Math]::Ceiling(.95*$offLatencies.Count)-1]}else{0};$latencyDelta=if($offLatencyP95 -eq 0){if($latencyP95 -eq 0){0}else{10000}}else{(($latencyP95-$offLatencyP95)/$offLatencyP95)*100}
    $stdoutValues=@($runOn | ForEach-Object {[int]$_.stdoutChars});$stdoutSorted=@($stdoutValues | Sort-Object);$stdoutP95=if($stdoutSorted.Count){$stdoutSorted[[Math]::Ceiling(.95*$stdoutSorted.Count)-1]}else{0};$stdoutMax=if($stdoutSorted.Count){($stdoutSorted|Measure-Object -Maximum).Maximum}else{0}
    $offStdout=@($runOff|ForEach-Object {[int]$_.stdoutChars}|Sort-Object);$offStdoutP95=if($offStdout.Count){$offStdout[[Math]::Ceiling(.95*$offStdout.Count)-1]}else{0};$offStdoutMax=if($offStdout.Count){($offStdout|Measure-Object -Maximum).Maximum}else{0}
    $legacyCallCount=0;$totalMcpCalls=0
    foreach($run in @($trace.runs)){foreach($call in @($run.calls)){if($call.kind -eq 'mcp' -and $call.name -match '^navlyn_'){$totalMcpCalls++;if($call.name -notin $currentTools){$legacyCallCount++}}}}
    $metrics=[ordered]@{
        semanticRecall=[ordered]@{passed=($semanticOn.Count-gt 0 -and @($semanticOn|Where-Object semanticCorrectness).Count/$semanticOn.Count -ge .95);numerator=@($semanticOn|Where-Object semanticCorrectness).Count;denominator=$semanticOn.Count}
        identityCriticalAnchoring=[ordered]@{passed=($identityRows.Count -gt 0 -and @($identityRows|Where-Object editSafety).Count -eq $identityRows.Count);numerator=@($identityRows|Where-Object editSafety).Count;denominator=$identityRows.Count}
        textOnlyFalsePositive=[ordered]@{passed=($textOn.Count -gt 0 -and @($textOn|Where-Object textNoMcp).Count/$textOn.Count -ge .95);numerator=@($textOn|Where-Object textNoMcp).Count;denominator=$textOn.Count}
        firstActionOverall=[ordered]@{passed=($runOn.Count -gt 0 -and $firstOverall/$runOn.Count -ge .92);numerator=$firstOverall;denominator=$runOn.Count}
        firstActionCoreSeven=[ordered]@{passed=($coreDenominator -gt 0 -and $firstCore -ge [Math]::Ceiling(.95*$coreDenominator));numerator=$firstCore;denominator=$coreDenominator}
        legacyMcpUse=[ordered]@{passed=($legacyCallCount -eq 0);numerator=($totalMcpCalls-$legacyCallCount);denominator=$totalMcpCalls;legacyUses=$legacyCallCount}
        actionClassification=[ordered]@{passed=(@($runOn|Where-Object validActionNames).Count -eq $runOn.Count);numerator=@($runOn|Where-Object validActionNames).Count;denominator=$runOn.Count}
        skillActivationAccuracy=[ordered]@{passed=($rows.Count -gt 0 -and @($rows|Where-Object skillActivation).Count/$rows.Count -ge .95);numerator=@($rows|Where-Object skillActivation).Count;denominator=$rows.Count}
        availabilityFreshnessSetup=[ordered]@{passed=(@($rows|Where-Object availabilityFreshness).Count -eq $rows.Count);numerator=@($rows|Where-Object availabilityFreshness).Count;denominator=$rows.Count}
        evidenceBackedStop=[ordered]@{passed=($runOn.Count -gt 0 -and @($runOn|Where-Object evidenceBackedStop).Count/$runOn.Count -ge .95);numerator=@($runOn|Where-Object evidenceBackedStop).Count;denominator=$runOn.Count}
        unsupportedClaims=[ordered]@{passed=(@($runOn|Where-Object unsupportedClaims).Count -eq $runOn.Count);numerator=@($runOn|Where-Object unsupportedClaims).Count;denominator=$runOn.Count}
        callBudget=[ordered]@{passed=($runOn.Count -gt 0 -and @($runOn|Where-Object callBudget).Count/$runOn.Count -ge .95);numerator=@($runOn|Where-Object callBudget).Count;denominator=$runOn.Count}
        ambiguousTargetEdit=[ordered]@{passed=(@($runOn|Where-Object ambiguousEditAvoidance).Count -eq $runOn.Count);numerator=@($runOn|Where-Object ambiguousEditAvoidance).Count;denominator=$runOn.Count}
        staleCandidateReuse=[ordered]@{passed=(@($runOn|Where-Object staleReuseAvoidance).Count -eq $runOn.Count);numerator=@($runOn|Where-Object staleReuseAvoidance).Count;denominator=$runOn.Count}
        broadChecklist=[ordered]@{passed=($runOn.Count -gt 0 -and @($runOn|Where-Object broadChecklist).Count/$runOn.Count -ge .95);numerator=@($runOn|Where-Object broadChecklist).Count;denominator=$runOn.Count}
        outputBudget=[ordered]@{passed=($stdoutP95 -le 20000 -and $stdoutMax -le 40000);p95=$stdoutP95;max=$stdoutMax;denominator=$runOn.Count}
        clientCompletionAndStructuredOutput=[ordered]@{passed=(@($rows|Where-Object clientCompletionAndStructuredOutput).Count -eq $rows.Count);numerator=@($rows|Where-Object clientCompletionAndStructuredOutput).Count;denominator=$rows.Count;diagnosticStderrCleanRuns=@($rows|Where-Object diagnosticStderrClean).Count;diagnosticStderrApplicability='informational client diagnostics; not a Section 10 hard gate'}
        pairing=[ordered]@{passed=($pairOk -and $complete);pairs=@($scenarios).Count*[int]$trace.runsPerCondition;runs=$trace.runs.Count;minimumRunsPassed=([int]$trace.runsPerCondition -ge 5 -and @($scenarios).Count -eq 20)}
        skillOnLatencyRegression=[ordered]@{passed=($trace.runsPerCondition -ge 5 -and @($scenarios).Count -eq 20 -and $latencyDelta -le 25);applicability=if($trace.runsPerCondition -ge 5 -and @($scenarios).Count -eq 20){'live paired runs'}else{'incomplete pilot'};offP95Ms=$offLatencyP95;onP95Ms=$latencyP95;deltaPercent=[Math]::Round($latencyDelta,2);offMaxMs=if($offLatencies.Count){($offLatencies|Measure-Object -Maximum).Maximum}else{0};onMaxMs=$latencyMax}
        skillOnSemanticDelta=[ordered]@{passed=($semanticOn.Count -gt 0 -and $semanticOff.Count -gt 0 -and @($semanticOn|Where-Object semanticCorrectness).Count -ge @($semanticOff|Where-Object semanticCorrectness).Count);off=@($semanticOff|Where-Object semanticCorrectness).Count;on=@($semanticOn|Where-Object semanticCorrectness).Count}
        gateCompleteness=[ordered]@{passed=($complete -and $pairOk);applicable=$true}
    }
    $metrics.outputBudget.off=[ordered]@{p95=$offStdoutP95;max=$offStdoutMax;denominator=$runOff.Count}
    $metrics.outputBudget.on=[ordered]@{p95=$stdoutP95;max=$stdoutMax;denominator=$runOn.Count}
    $metrics.outputBudget.delta=[ordered]@{p95=$stdoutP95-$offStdoutP95;max=$stdoutMax-$offStdoutMax}
    $scopeByMetric=[ordered]@{
        semanticRecall=@{field='semanticCorrectness';off=$semanticOff;on=$semanticOn}
        identityCriticalAnchoring=@{field='editSafety';off=@($rows|Where-Object { $_.condition -eq 'off' -and ($_.editIntent -or $_.scenarioId -eq 'partial-declaration-04' -or $_.scenarioId -eq 'pre-edit-11') });on=$identityRows}
        textOnlyFalsePositive=@{field='textNoMcp';off=@($runOff|Where-Object { (Get-Group $_.scenarioId) -eq 'text' });on=$textOn}
        firstActionOverall=@{field='firstAction';off=$runOff;on=$runOn}
        firstActionCoreSeven=@{field='firstAction';off=@($runOff|Where-Object {$_.scenarioId -in $coreScenarioIds});on=@($runOn|Where-Object {$_.scenarioId -in $coreScenarioIds})}
        actionClassification=@{field='validActionNames';off=$runOff;on=$runOn}
        skillActivationAccuracy=@{field='skillActivation';off=$runOff;on=$runOn}
        availabilityFreshnessSetup=@{field='availabilityFreshness';off=$runOff;on=$runOn}
        evidenceBackedStop=@{field='evidenceBackedStop';off=$runOff;on=$runOn}
        unsupportedClaims=@{field='unsupportedClaims';off=$runOff;on=$runOn}
        callBudget=@{field='callBudget';off=$runOff;on=$runOn}
        ambiguousTargetEdit=@{field='ambiguousEditAvoidance';off=$runOff;on=$runOn}
        staleCandidateReuse=@{field='staleReuseAvoidance';off=$runOff;on=$runOn}
        broadChecklist=@{field='broadChecklist';off=$runOff;on=$runOn}
        clientCompletionAndStructuredOutput=@{field='clientCompletionAndStructuredOutput';off=$runOff;on=$runOn}
    }
    foreach($metricName in $scopeByMetric.Keys){$scope=$scopeByMetric[$metricName];$offRate=Get-BooleanRate $scope.off $scope.field;$onRate=Get-BooleanRate $scope.on $scope.field;$deltaPoints=if($null -ne $offRate.rate -and $null -ne $onRate.rate){[Math]::Round(($onRate.rate-$offRate.rate)*100,2)}else{$null};$metrics[$metricName].off=$offRate;$metrics[$metricName].on=$onRate;$metrics[$metricName].deltaPercentagePoints=$deltaPoints}
    $offMcpCalls=@($trace.runs|Where-Object condition -eq off|ForEach-Object {@($_.calls|Where-Object {$_.kind -eq 'mcp' -and $_.name -match '^navlyn_'})})
    $onMcpCalls=@($trace.runs|Where-Object condition -eq on|ForEach-Object {@($_.calls|Where-Object {$_.kind -eq 'mcp' -and $_.name -match '^navlyn_'})})
    $offLegacy=@($offMcpCalls|Where-Object {$_.name -notin $currentTools}).Count;$onLegacy=@($onMcpCalls|Where-Object {$_.name -notin $currentTools}).Count
    $metrics.legacyMcpUse.off=[ordered]@{numerator=$offMcpCalls.Count-$offLegacy;denominator=$offMcpCalls.Count;legacyUses=$offLegacy}
    $metrics.legacyMcpUse.on=[ordered]@{numerator=$onMcpCalls.Count-$onLegacy;denominator=$onMcpCalls.Count;legacyUses=$onLegacy}
    $metrics.legacyMcpUse.delta=[ordered]@{newNames=$onLegacy-$offLegacy}
    $allPassed=$true;foreach($name in $metrics.Keys){if(!$metrics[$name].passed){$allPassed=$false}}
    return [ordered]@{schemaVersion='navlyn.routing-skill-live-report.v1';passed=$allPassed;scenarioCount=@($scenarios).Count;traceCount=$trace.runs.Count;metrics=$metrics;perRun=$rows.ToArray();notApplicable=@('newFocusedToolInvalidInputCoverage','toolsListGate','cliRegressionGate')}
}
function Get-Hash([string]$path) { $sha=[Security.Cryptography.SHA256]::Create();try{return ([Convert]::ToHexString($sha.ComputeHash([IO.File]::ReadAllBytes($path))).ToLowerInvariant())}finally{$sha.Dispose()} }
function Get-SkillFiles { return @(Get-ChildItem -LiteralPath (Join-Path $repoRoot '.agents/skills/navlyn-semantic-routing') -File -Recurse | Sort-Object FullName) }
function Get-SkillHash { $files=Get-SkillFiles; $concat=($files|ForEach-Object { $_.FullName.Substring((Join-Path $repoRoot '.agents/skills/navlyn-semantic-routing').Length).Replace('\','/')+':'+(Get-Hash $_.FullName) }) -join "`n"; $sha=[Security.Cryptography.SHA256]::Create();try{return [Convert]::ToHexString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($concat))).ToLowerInvariant()}finally{$sha.Dispose()} }
function Install-LiveSkill([string]$conditionRoot) {
    $parent=Join-Path $conditionRoot '.agents/skills'
    [IO.Directory]::CreateDirectory($parent)|Out-Null
    $installer=Join-Path $repoRoot 'scripts/install-routing-skill.ps1'
    $output=& pwsh.exe -NoLogo -NoProfile -File $installer -Action Install -DestinationRoot $parent 2>$null
    if($LASTEXITCODE -ne 0){Fail 'Routing skill installer failed in an isolated live condition.'}
    try{$installed=$output|ConvertFrom-Json}catch{Fail 'Routing skill installer returned invalid JSON in an isolated live condition.'}
    if($installed.status -ne 'installed' -or !(Test-Path -LiteralPath (Join-Path $parent 'navlyn-semantic-routing/SKILL.md') -PathType Leaf)){Fail 'Routing skill installer did not produce the expected live condition.'}
}
function New-Prompt($scenario) {
    $facts=[Collections.Generic.List[string]]::new();$args=$scenario.baselineTrace.calls[0].arguments
    foreach($property in $args.PSObject.Properties){$facts.Add("$($property.Name)=$($property.Value)")}
    $workspace=Join-Path $repoRoot ([string]$scenario.workspace)
    $fixture=Join-Path $repoRoot ([string]$scenario.fixture)
    $inputFacts=if($facts.Count){"Authoritative input facts: $($facts -join '; ')."}else{'Use the authoritative workspace and fixture paths below as the concrete scope.'}
    return "Repository root: $repoRoot`nWorkspace: $workspace`nFixture/input: $fixture`n$inputFacts`nRead-only request: do not modify files. Execute at least one available read-only action, base the answer on returned evidence rather than the prompt alone, then stop. Use the authoritative input facts directly. Set stopReason to an allowed output-schema value only when returned evidence supports that reason. Do not inspect unrelated scenario, replay, schema, artifact, product-implementation, or test-expectation files to infer the expected route. A file explicitly named in the authoritative input facts remains in scope.`n`nUser request (verbatim): $($scenario.prompt)"
}
function Invoke-Live {
    if($Client -notin @('codex','codex.exe')){Fail 'Only the installed Windows Codex CLI commands are supported.'}
    $version=& $Client --version 2>$null; if($LASTEXITCODE -ne 0 -or !$version){Fail 'Codex CLI is unavailable.'}
    if($RunsPerCondition -lt 1){Fail 'RunsPerCondition must be positive.'}
    $scenarios=@(Get-Scenarios)
    if($MaxParallelism -gt 1){[Console]::Error.WriteLine('Collection is serialized for order/control; MaxParallelism is recorded as a cap, not used to overlap model calls.')}
    $skillDir=Join-Path $repoRoot '.agents/skills/navlyn-semantic-routing';if(!(Test-Path -LiteralPath $skillDir)){Fail 'Repository-local routing skill is unavailable.'}
    $exeCandidates=@((Join-Path $repoRoot 'navlyn.Mcp/bin/Debug/net10.0/navlyn.Mcp.exe'),(Join-Path $repoRoot 'navlyn.Mcp/bin/Debug/net10.0/navlyn.Mcp'))
    $mcpExe=$exeCandidates|Where-Object {Test-Path -LiteralPath $_}|Select-Object -First 1
    if(!$mcpExe){Fail 'Navlyn MCP executable unavailable at the expected net10.0 debug path; build it before live collection.'}
    $outDir=if($Output){Resolve-Input $Output}else{Join-Path $repoRoot 'artifacts/evals/routing-live/routing-skill-live-trace.json'}
    $rawDir=Resolve-Input $RawRoot
    [IO.Directory]::CreateDirectory($rawDir)|Out-Null
    $tempBase=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    $rootBase=Join-Path $tempBase ("navlyn-routing-live-conditions-"+[Guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($rootBase)|Out-Null
    if([IO.Path]::GetFullPath($rootBase).StartsWith(([IO.Path]::GetFullPath($repoRoot)+[IO.Path]::DirectorySeparatorChar),[StringComparison]::OrdinalIgnoreCase)){Fail 'Condition roots must be outside the repository so the off condition cannot inherit the repository-local skill.'}
    $artifactRoot=[IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts/evals'))+[IO.Path]::DirectorySeparatorChar
    foreach($generatedPath in @($outDir,$rawDir)){if(!([IO.Path]::GetFullPath($generatedPath).StartsWith($artifactRoot,[StringComparison]::OrdinalIgnoreCase))){Fail "Generated artifacts must stay below artifacts/evals: $generatedPath"}}
    $skillHash=Get-SkillHash
    $discovery=[ordered]@{}
    foreach($condition in @('off','on')){
        $probeRoot=Join-Path $rootBase "discovery-$condition"
        if(Test-Path $probeRoot){$resolvedProbe=[IO.Path]::GetFullPath($probeRoot);$resolvedBase=[IO.Path]::GetFullPath($rootBase)+[IO.Path]::DirectorySeparatorChar;if(!$resolvedProbe.StartsWith($resolvedBase,[StringComparison]::OrdinalIgnoreCase)){Fail "Unsafe prompt-input probe root: $resolvedProbe"};Remove-Item -LiteralPath $resolvedProbe -Recurse -Force}
        [IO.Directory]::CreateDirectory($probeRoot)|Out-Null;Copy-Item -LiteralPath (Join-Path $repoRoot 'AGENTS.md') -Destination $probeRoot
        if($condition -eq 'on'){Install-LiveSkill $probeRoot}
        $probe=Test-PromptInputSkillVisibility $probeRoot $condition $rawDir
        $expectedVisible=$condition -eq 'on'
        if([bool]$probe.visible -ne $expectedVisible){Fail "Codex prompt-input did not confirm local skill visibility only in on (condition=$condition, visible=$($probe.visible))."}
        $discovery[$condition]=$probe
    }
    $runs=New-Object Collections.Generic.List[object]
    foreach($scenario in $scenarios){
        $scenarioSchemaPath=Join-Path $rawDir "$($scenario.id).final-response.schema.json"
        $scenarioSchema=@{type='object';additionalProperties=$false;required=@('answer','stopReason','claims','editAttempted','anchorBeforeEdit','ambiguityReported','staleReported','partialResult');properties=@{answer=@{type='string';minLength=1};stopReason=@{type='string';enum=@($scenario.expectedStopEvidence.acceptedStopReasons)};claims=@{type='array';items=@{type='string'}};editAttempted=@{type='boolean'};anchorBeforeEdit=@{type='boolean'};ambiguityReported=@{type='boolean'};staleReported=@{type='boolean'};partialResult=@{type='boolean'}}} | ConvertTo-Json -Depth 10
        Set-Content -LiteralPath $scenarioSchemaPath -Value $scenarioSchema -Encoding utf8
        for($rep=1;$rep -le $RunsPerCondition;$rep++){foreach($condition in @('off','on')){
        $conditionRoot=Join-Path $rootBase "$($scenario.id)-$rep-$condition";if(Test-Path $conditionRoot){$resolvedCondition=[IO.Path]::GetFullPath($conditionRoot);$resolvedBase=[IO.Path]::GetFullPath($rootBase)+[IO.Path]::DirectorySeparatorChar;if(!$resolvedCondition.StartsWith($resolvedBase,[StringComparison]::OrdinalIgnoreCase)){Fail "Unsafe condition-root target: $resolvedCondition"};Remove-Item -LiteralPath $resolvedCondition -Recurse -Force};[IO.Directory]::CreateDirectory($conditionRoot)|Out-Null
        Copy-Item -LiteralPath (Join-Path $repoRoot 'AGENTS.md') -Destination $conditionRoot
        if($condition -eq 'on'){Install-LiveSkill $conditionRoot}
        $visible=Test-Path -LiteralPath (Join-Path $conditionRoot '.agents/skills/navlyn-semantic-routing')
        if($visible -ne ($condition -eq 'on')){Fail "Skill visibility isolation failed for $condition."}
        $noMcp=$scenario.id -eq 'unavailable-mcp-35'
        $prompt=New-Prompt $scenario;$promptPath=Join-Path $rawDir "$($scenario.id)-$rep-$condition.prompt.txt";$rawPath=Join-Path $rawDir "$($scenario.id)-$rep-$condition.jsonl";$lastPath=Join-Path $rawDir "$($scenario.id)-$rep-$condition.answer.json"
        Set-Content -LiteralPath $promptPath -Value $prompt -Encoding utf8
        $started=[DateTimeOffset]::UtcNow
        $psi=[Diagnostics.ProcessStartInfo]::new($Client);$psi.WorkingDirectory=$repoRoot;$psi.UseShellExecute=$false;$psi.RedirectStandardOutput=$true;$psi.RedirectStandardError=$true
        if ($Sandbox -eq 'read-only') { Set-CodexWindowsShellPath $psi }
        foreach($arg in @('-a','never','--strict-config','exec','--json','--ephemeral','--ignore-user-config','--skip-git-repo-check','--disable','plugins','--disable','shell_snapshot','--sandbox',$Sandbox,'-C',$conditionRoot,'--add-dir',$repoRoot,'--model',$Model,'--output-schema',$scenarioSchemaPath,'-o',$lastPath)){$psi.ArgumentList.Add($arg)}
        $psi.ArgumentList.Add('-c');$psi.ArgumentList.Add('cli_auth_credentials_store="file"')
        $psi.ArgumentList.Add('-c');$psi.ArgumentList.Add("model_reasoning_effort=`"$Reasoning`"")
        if(!$noMcp){
            $workspaceArgument=([string]$scenario.workspace).Replace('\','/')
            $mcpArgumentsToml='["--workspace","'+$workspaceArgument+'","--workspace-root-policy","repo-relative"]'
            $psi.ArgumentList.Add('-c');$psi.ArgumentList.Add("mcp_servers.navlyn.command=`"$mcpExe`"")
            $psi.ArgumentList.Add('-c');$psi.ArgumentList.Add("mcp_servers.navlyn.args=$mcpArgumentsToml")
            $psi.ArgumentList.Add('-c');$psi.ArgumentList.Add("mcp_servers.navlyn.cwd=`"$repoRoot`"")
            $psi.ArgumentList.Add('-c');$psi.ArgumentList.Add('mcp_servers.navlyn.enabled=true')
            $psi.ArgumentList.Add('-c');$psi.ArgumentList.Add('mcp_servers.navlyn.required=true')
            $psi.ArgumentList.Add('-c');$psi.ArgumentList.Add('mcp_servers.navlyn.default_tools_approval_mode="approve"')
        }
        $psi.ArgumentList.Add($prompt)
        $psi.RedirectStandardInput=$true
        $process=[Diagnostics.Process]::new();$process.StartInfo=$psi;$process.Start()|Out-Null;$process.StandardInput.Close()
        $stdoutTask=$process.StandardOutput.ReadToEndAsync();$stderrTask=$process.StandardError.ReadToEndAsync()
        $finished=$process.WaitForExit($TimeoutSeconds*1000);if(!$finished){try{$process.Kill($true)}catch{}; $exit=$null}else{$process.WaitForExit();$exit=$process.ExitCode}
        $stdout=$stdoutTask.GetAwaiter().GetResult();$stderr=$stderrTask.GetAwaiter().GetResult();$duration=[int]([DateTimeOffset]::UtcNow-$started).TotalMilliseconds
        Set-Content -LiteralPath $rawPath -Value $stdout -Encoding utf8
        if($stderr){Set-Content -LiteralPath (Join-Path $rawDir "$($scenario.id)-$rep-$condition.stderr.txt") -Value $stderr -Encoding utf8}
        $structured=$null;if(Test-Path -LiteralPath $lastPath){try{$structured=Get-Content -Raw $lastPath|ConvertFrom-Json -Depth 50}catch{}}
        $callMap=[ordered]@{};$rawOrder=New-Object Collections.Generic.List[object];$readPaths=New-Object Collections.Generic.List[string];$elapsed=0;$eventSequence=0;$tokenUsage=$null
        foreach($line in ($stdout -split "`r?`n"|Where-Object {![string]::IsNullOrWhiteSpace($_)})){try{$event=$line|ConvertFrom-Json -Depth 50}catch{continue};$etype=if(Has-JsonProperty $event 'type'){[string]$event.type}else{''};$item=if(Has-JsonProperty $event 'item'){$event.item}else{$null};$eventOffset=Get-EventOffset $event $started;$eventUsage=Get-UsageObject $event;if($null -ne $eventUsage){$tokenUsage=$eventUsage}
            if($null -ne $item -and [string]$item.type -eq 'mcp_tool_call'){
                $name=if(Has-JsonProperty $item 'tool'){[string]$item.tool}elseif(Has-JsonProperty $item 'name'){[string]$item.name}else{''}
                if(Test-JsonObject $item.tool){$name=if(Has-JsonProperty $item.tool 'name'){[string]$item.tool.name}else{''}}
                $argsObj=if(Has-JsonProperty $item 'arguments' -and (Test-JsonObject $item.arguments)){$item.arguments}else{[pscustomobject]@{}}
                $rawResult=if(Has-JsonProperty $item 'result'){$item.result}else{$null}
                $result=$rawResult
                if(Test-JsonObject $rawResult){if(Has-JsonProperty $rawResult 'structured_content'){$result=$rawResult.structured_content}elseif(Has-JsonProperty $rawResult 'structuredContent'){$result=$rawResult.structuredContent}elseif((Has-JsonProperty $rawResult 'result') -and (Test-JsonObject $rawResult.result)){if(Has-JsonProperty $rawResult.result 'structured_content'){$result=$rawResult.result.structured_content}elseif(Has-JsonProperty $rawResult.result 'structuredContent'){$result=$rawResult.result.structuredContent}}}
                if(!(Test-JsonObject $result)){$result=[pscustomobject]@{}}
                $eventSequence++;$key=if(Has-JsonProperty $item 'id' -and ![string]::IsNullOrWhiteSpace([string]$item.id)){[string]$item.id}else{"mcp-$eventSequence"}
                $writeAttempt=$false;$status=if(Has-JsonProperty $item 'status'){[string]$item.status}else{$etype}
                if($callMap.Contains($key)){$callMap[$key].selectedResultFields=$result;$callMap[$key].status=$status;$callMap[$key].outputChars=$line.Length;if($etype -match 'completed|finished'){$callMap[$key].durationMs=[Math]::Max(0,$eventOffset-[int]$callMap[$key].startOffsetMs)};if(Has-JsonProperty $item 'duration_ms'){$callMap[$key].durationMs=[int]$item.duration_ms}}else{$callMap[$key]=[pscustomobject]@{kind='mcp';name=$name;arguments=$argsObj;selectedResultFields=$result;status=$status;startOffsetMs=$eventOffset;durationMs=0;outputChars=$line.Length;skillLoading=$false;writeAttempt=$writeAttempt;timingSource=if(Has-JsonProperty $event 'timestamp'){'jsonl-timestamps'}else{'timestamp-unavailable'}}}
            } elseif($null -ne $item -and [string]$item.type -eq 'command_execution') {
                $cmd=if(Has-JsonProperty $item 'command'){[string]$item.command}else{''};$kind='other';$name='other';if($cmd -match '(?i)\brg(\.exe)?\b'){$kind='ordinary';$name='rg'}elseif($cmd -match '(?i)(Get-Content|\bcat\b|\btype\b|\bread-file\b)'){$kind='ordinary';$name='file-read'}elseif($cmd -match '(?i)\bgit\b'){$kind='ordinary';$name='git'}elseif($cmd -match '(?i)\bdotnet\s+build\b'){$kind='ordinary';$name='build'}elseif($cmd -match '(?i)\bdotnet\s+test\b'){$kind='ordinary';$name='test'}
                $argsObj=Get-OrdinaryArguments $kind $cmd;$isSkill=$cmd -match '(?i)navlyn-semantic-routing[/\\].*(SKILL\.md|references[/\\])'
                if($isSkill){$readPaths.Add($cmd)}
                $output=if(Has-JsonProperty $item 'aggregated_output'){[string]$item.aggregated_output}else{''};$exitValue=if(Has-JsonProperty $item 'exit_code'){$item.exit_code}else{$null}
                $eventSequence++;$key=if(Has-JsonProperty $item 'id' -and ![string]::IsNullOrWhiteSpace([string]$item.id)){[string]$item.id}else{"command-$eventSequence"}
                $writeAttempt=$cmd -match '(?i)(Set-Content|Add-Content|Out-File|Remove-Item|Move-Item|Copy-Item|apply_patch|git\s+(apply|checkout|reset)|\btee\b\s|\b(?:>|>>))'
                if($callMap.Contains($key)){$callMap[$key].selectedResultFields=[pscustomobject]@{stdout=$output;exitCode=$exitValue};$callMap[$key].status=if(Has-JsonProperty $item 'status'){[string]$item.status}else{$etype};$callMap[$key].durationMs=[Math]::Max(0,$eventOffset-[int]$callMap[$key].startOffsetMs);$callMap[$key].outputChars=$output.Length;$callMap[$key].writeAttempt=$writeAttempt}else{$callMap[$key]=[pscustomobject]@{kind=$kind;name=$name;arguments=$argsObj;selectedResultFields=[pscustomobject]@{stdout=$output;exitCode=$exitValue};status=if(Has-JsonProperty $item 'status'){[string]$item.status}else{$etype};startOffsetMs=$eventOffset;durationMs=0;outputChars=$output.Length;skillLoading=$isSkill;writeAttempt=$writeAttempt;timingSource=if(Has-JsonProperty $event 'timestamp'){'jsonl-timestamps'}else{'timestamp-unavailable'}}}
            }
        }
        $calls=@($callMap.Values);foreach($call in $calls){$rawOrder.Add([pscustomobject]@{kind=$call.kind;name=$call.name;skillLoading=$call.skillLoading})}
        $answer='';$stop='';$claims=@();$edit=$false;$anchor=$false;$ambiguity=$false;$stale=$false;$partial=$false;$outputValid=$false
        if($null -ne $structured){$answer=[string]$structured.answer;$stop=[string]$structured.stopReason;$claims=@($structured.claims);$edit=[bool]$structured.editAttempted;$anchor=[bool]$structured.anchorBeforeEdit;$ambiguity=[bool]$structured.ambiguityReported;$stale=[bool]$structured.staleReported;$partial=[bool]$structured.partialResult;$outputValid=![string]::IsNullOrWhiteSpace($answer)}
        $activated=$readPaths.Count -gt 0
        $canonicalChars=if($null -ne $structured){([string]($structured|ConvertTo-Json -Compress -Depth 30)).Length}else{$answer.Length}
        $runConfig=[pscustomobject]@{client=$Client;model=$Model;modelReasoningEffort=$Reasoning;approval='never';sandbox=$Sandbox;ephemeral=$true;ignoreUserConfig=$true;strictConfig=$true;skipGitRepoCheck=$true;plugins=$false;shellSnapshot=$false;addDir=$repoRoot;mcpServerEnabled=(!$noMcp);mcpServerCommand=if(!$noMcp){$mcpExe}else{$null};mcpServerArguments=if(!$noMcp){@('--workspace',([string]$scenario.workspace).Replace('\','/'),'--workspace-root-policy','repo-relative')}else{@()};mcpServerCwd=if(!$noMcp){$repoRoot}else{$null};requiredMcp=(!$noMcp);defaultToolsApprovalMode=if(!$noMcp){'approve'}else{$null};outputSchema=[IO.Path]::GetFileName($scenarioSchemaPath);timeoutSeconds=$TimeoutSeconds}
        $diagnosticStderrLines=@($stderr -split "`r?`n"|Where-Object {![string]::IsNullOrWhiteSpace($_) -and $_ -cne 'Reading additional input from stdin...'})
        $diagnosticStderr=($diagnosticStderrLines -join "`n")
        $run=[pscustomobject]@{
            runId="$($scenario.id)-$rep-$condition";scenarioId=$scenario.id;taskClass=$scenario.taskClass;group=(Get-Group $scenario.id);condition=$condition;repetition=$rep
            prompt=[pscustomobject]@{text=$prompt;scenarioPrompt=$scenario.prompt;workspace=(Join-Path $repoRoot $scenario.workspace);fixture=(Join-Path $repoRoot $scenario.fixture)}
            environment=[pscustomobject]@{mcpAvailability=if($noMcp){'unavailable'}else{'available'};workspaceAvailability=if(Test-Path (Join-Path $repoRoot $scenario.workspace)){'available'}else{'missing'};workspaceFreshness=$scenario.availabilityFreshnessSetup.workspaceFreshness;workspaceFreshnessBasis='authoritative fixture/scenario setup; runtime freshness only when a result field exposes it'}
            configuration=$runConfig;startedAtUtc=$started.ToString('o');durationMs=$duration;exitCode=$exit
            skill=[pscustomobject]@{available=$visible;activated=$activated;path=if($visible){Join-Path $conditionRoot '.agents/skills/navlyn-semantic-routing'}else{$null};sha256=if($visible){$skillHash}else{$null};readPaths=$readPaths.ToArray()}
            calls=$calls;rawCallOrder=$rawOrder.ToArray();stdoutChars=$stdout.Length;canonicalOutputChars=$canonicalChars;stderrChars=$stderr.Length;diagnosticStderrChars=$diagnosticStderr.Length;tokenUsage=$tokenUsage
            final=[pscustomobject]@{structuredResponse=$structured;answer=$answer;stopReason=$stop;claims=$claims;editAttempted=$edit;anchorBeforeEdit=$anchor;ambiguityReported=$ambiguity;staleReported=$stale;partialResult=$partial;outputValid=$outputValid;stderrClean=($diagnosticStderr.Length -eq 0)}
            rawJsonl=[IO.Path]::GetRelativePath($repoRoot,$rawPath).Replace('\','/')
        }
        $runs.Add($run);[Console]::Error.WriteLine("Collected $($scenario.id) repetition $rep condition $condition (exit $exit).")
    }}}
    $sourcePath=Resolve-Input $ScenarioFile;$head=& git -C $repoRoot rev-parse HEAD 2>$null;$dirty=!!(& git -C $repoRoot status --porcelain)
    $doc=[ordered]@{
        schemaVersion='navlyn.routing-skill-live-trace.v1';collectedAtUtc=[DateTimeOffset]::UtcNow.ToString('o');scenarioFile=[IO.Path]::GetRelativePath($repoRoot,$sourcePath).Replace('\','/');scenarioSha256=(Get-Hash $sourcePath)
        repository=[ordered]@{root=$repoRoot;head=[string]$head;dirty=$dirty}
        client=[ordered]@{name='codex';version=([string]$version).Trim();model=$Model;reasoning=$Reasoning}
        configuration=[ordered]@{command=$Client;model=$Model;modelReasoningEffort=$Reasoning;approval='never';sandbox=$Sandbox;ephemeral=$true;ignoreUserConfig=$true;strictConfig=$true;skipGitRepoCheck=$true;plugins=$false;shellSnapshot=$false;addDir=$repoRoot;conditionRootIsolation='system-temp-outside-repository';maxParallelism=$MaxParallelism;actualParallelism=1;timeoutSeconds=$TimeoutSeconds;outputSchema='per-scenario schema with accepted stopReason enum';mcpServer=[ordered]@{name='navlyn';command=$mcpExe;args='per-run --workspace selected from the authoritative scenario';cwd=$repoRoot;required=$true;defaultToolsApprovalMode='approve'}}
        skill=[ordered]@{name='navlyn-semantic-routing';path=(Join-Path $repoRoot '.agents/skills/navlyn-semantic-routing');sha256=$skillHash}
        runsPerCondition=$RunsPerCondition;subset=@($scenarios|ForEach-Object id);groupCounts=[ordered]@{semantic=@($scenarios|Where-Object id -in $semanticIds).Count;text=@($scenarios|Where-Object id -in $textIds).Count;safety=@($scenarios|Where-Object id -in $safetyIds).Count}
        discovery=[ordered]@{offSkillVisible=[bool]$discovery.off.visible;onSkillVisible=[bool]$discovery.on.visible;mcpConfigured=$true;method='codex debug prompt-input in isolated condition roots';off=$discovery.off;on=$discovery.on}
        runs=$runs.ToArray()
    }
    $json=$doc|ConvertTo-Json -Depth 100;$outDirectory=Split-Path -Parent $outDir;[IO.Directory]::CreateDirectory($outDirectory)|Out-Null;Set-Content -LiteralPath $outDir -Value $json -Encoding utf8
    $resolvedRootBase=[IO.Path]::GetFullPath($rootBase)
    if(!$resolvedRootBase.StartsWith($tempBase,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolvedRootBase) -notlike 'navlyn-routing-live-conditions-*'){Fail "Unsafe temporary condition-root cleanup target: $resolvedRootBase"}
    Remove-Item -LiteralPath $resolvedRootBase -Recurse -Force
    Write-Output "Trace written: $outDir";Write-Output "Runs: $($runs.Count) (minimum repetitions: $($RunsPerCondition)); synthetic/live gates are scored separately."
}

$scenarioPath=Resolve-Input $ScenarioFile
$scenarios=@(Get-Scenarios)
if($RunsPerCondition -lt 1){Fail 'RunsPerCondition must be at least 1.'}
if($TraceFile){
    $tracePath=Resolve-Input $TraceFile;if(!(Test-Path -LiteralPath $tracePath)){Fail "Trace not found: $tracePath"}
    $trace=Read-Json $tracePath
    try { $report=Score-Trace $trace $scenarios } catch { Fail "$($_.Exception.Message)`n$($_.ScriptStackTrace)" }
    $json=$report|ConvertTo-Json -Depth 100
    if($Output){$out=Resolve-Input $Output;[IO.Directory]::CreateDirectory((Split-Path -Parent $out))|Out-Null;Set-Content -LiteralPath $out -Value $json -Encoding utf8}else{$json}
    if(!$report.passed){Fail 'Live routing eval failed one or more non-compensating gates.'};exit 0
}
if($RunLive){Invoke-Live;exit 0}
if(!$Describe){$Describe=$true}
$plan=[ordered]@{schemaVersion='navlyn.routing-skill-live-plan.v1';client=$Client;model=$Model;reasoning=$Reasoning;runsPerCondition=$RunsPerCondition;scenarioCount=$scenarios.Count;groupCounts=[ordered]@{semantic=@($scenarios|Where-Object id -in $semanticIds).Count;text=@($scenarios|Where-Object id -in $textIds).Count;safety=@($scenarios|Where-Object id -in $safetyIds).Count};runCount=$scenarios.Count*$RunsPerCondition*2;scenarioIds=@($scenarios|ForEach-Object id);traceRoot=(Resolve-Input $RawRoot);traceOutput=if($Output){Resolve-Input $Output}else{Join-Path $repoRoot 'artifacts/evals/routing-live/routing-skill-live-trace.json'};timeoutSeconds=$TimeoutSeconds;maxParallelism=$MaxParallelism;liveCollectionEnabled=[bool]$RunLive}
$plan|ConvertTo-Json -Depth 10

[CmdletBinding()]
param(
    [ValidateSet('vscode')][string]$Client = 'vscode',
    [ValidateSet('Local','Global')][string]$Target = 'Local',
    [Parameter(Mandatory)][string]$Workspace,
    [string]$WorkspaceFile,
    [ValidateSet('Install','Update','Remove','Undo')][string]$Action = 'Install',
    [string]$Version,
    [string]$Feed,
    [switch]$Apply,
    [switch]$AllowDowngrade
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/navlyn-jsonc.ps1')

function Get-SetupHashBytes([byte[]]$Bytes) { [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes)).ToLowerInvariant() }
function Get-SetupHashText([string]$Text) { Get-SetupHashBytes ([Text.Encoding]::UTF8.GetBytes($Text)) }
function Get-SetupHashFile([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Get-SetupPathComparison { if ($IsWindows) { return [StringComparison]::OrdinalIgnoreCase }; return [StringComparison]::Ordinal }
function Get-SetupWorkspaceKey([string]$Path) { $keyPath=if($IsWindows){$Path.ToLowerInvariant()}else{$Path}; return Get-SetupHashText $keyPath }
function Compare-SetupVersion([string]$Left,[string]$Right) {
    $leftMatch=[regex]::Match($Left,'^(\d+\.\d+\.\d+)(?:-([0-9A-Za-z.-]+))?$');$rightMatch=[regex]::Match($Right,'^(\d+\.\d+\.\d+)(?:-([0-9A-Za-z.-]+))?$')
    if(!$leftMatch.Success -or !$rightMatch.Success){throw 'Unsupported NuGet version syntax.'}
    $numeric=([version]$leftMatch.Groups[1].Value).CompareTo([version]$rightMatch.Groups[1].Value);if($numeric -ne 0){return $numeric}
    $leftPre=$leftMatch.Groups[2].Value;$rightPre=$rightMatch.Groups[2].Value
    if(!$leftPre -and $rightPre){return 1};if($leftPre -and !$rightPre){return -1};if(!$leftPre){return 0}
    $leftParts=$leftPre.Split('.');$rightParts=$rightPre.Split('.')
    for($i=0;$i -lt [Math]::Max($leftParts.Count,$rightParts.Count);$i++){
        if($i -ge $leftParts.Count){return -1};if($i -ge $rightParts.Count){return 1}
        $ln=0;$rn=0;$lNumeric=[int]::TryParse($leftParts[$i],[ref]$ln);$rNumeric=[int]::TryParse($rightParts[$i],[ref]$rn)
        if($lNumeric -and $rNumeric){if($ln -lt $rn){return -1};if($ln -gt $rn){return 1};continue}
        if($lNumeric -and !$rNumeric){return -1};if(!$lNumeric -and $rNumeric){return 1}
        $ordinal=[StringComparer]::Ordinal.Compare($leftParts[$i],$rightParts[$i]);if($ordinal -ne 0){return $ordinal}
    }
    return 0
}
function Get-SetupUtf8Bytes([string]$Text, [bool]$Bom) {
    $body = [Text.UTF8Encoding]::new($false, $true).GetBytes($Text)
    if (!$Bom) { return $body }
    $bytes = [byte[]]::new($body.Length + 3); $bytes[0]=239; $bytes[1]=187; $bytes[2]=191; [Array]::Copy($body,0,$bytes,3,$body.Length); return $bytes
}
function Get-SetupText([byte[]]$Bytes) {
    $bom = $Bytes.Length -ge 3 -and $Bytes[0] -eq 239 -and $Bytes[1] -eq 187 -and $Bytes[2] -eq 191
    $offset = if ($bom) { 3 } else { 0 }
    return [pscustomobject]@{ Text=[Text.UTF8Encoding]::new($false,$true).GetString($Bytes,$offset,$Bytes.Length-$offset); Bom=$bom }
}
function Write-SetupAtomic([string]$Path,[byte[]]$Bytes) {
    $parent=Split-Path -Parent $Path; [IO.Directory]::CreateDirectory($parent) | Out-Null
    $tmp=Join-Path $parent ('.'+[IO.Path]::GetFileName($Path)+'.'+[guid]::NewGuid().ToString('N')+'.tmp')
    $backup="$tmp.bak"
    try {
        [IO.File]::WriteAllBytes($tmp,$Bytes)
        if ([IO.File]::Exists($Path)) { [IO.File]::Replace($tmp,$Path,$backup);if([IO.File]::Exists($backup)){[IO.File]::Delete($backup)} } else { [IO.File]::Move($tmp,$Path) }
    } finally { if ([IO.File]::Exists($tmp)) { [IO.File]::Delete($tmp) };if([IO.File]::Exists($backup)){[IO.File]::Delete($backup)} }
}
function Write-SetupAtomicText([string]$Path,[string]$Text,[bool]$Bom) { Write-SetupAtomic $Path (Get-SetupUtf8Bytes $Text $Bom) }
function Get-SetupDataRoot {
    if ($IsWindows -and ![string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) { return Join-Path $env:LOCALAPPDATA 'Navlyn/setup' }
    if (![string]::IsNullOrWhiteSpace($env:XDG_DATA_HOME)) { return Join-Path $env:XDG_DATA_HOME 'navlyn/setup' }
    if (![string]::IsNullOrWhiteSpace($env:HOME)) { return Join-Path $env:HOME '.local/share/navlyn/setup' }
    throw 'Could not locate the current user application-data directory.'
}
function Get-GlobalToolsRoot {
    $dotnetHome=$env:DOTNET_CLI_HOME
    if ([string]::IsNullOrWhiteSpace($dotnetHome)) { $dotnetHome=if($IsWindows){$env:USERPROFILE}else{$env:HOME} }
    if (![string]::IsNullOrWhiteSpace($dotnetHome) -and [IO.Path]::IsPathFullyQualified($dotnetHome)) { return [IO.Path]::GetFullPath((Join-Path $dotnetHome '.dotnet/tools')) }
    throw 'Could not resolve the current user .NET global tools directory.'
}

function Invoke-SetupGlobalDotnet([string[]]$Arguments,[string]$LogRoot) {
    $resolvedRoot=Get-GlobalToolsRoot
    Assert-SetupNoReparsePath $resolvedRoot
    $psi=[Diagnostics.ProcessStartInfo]::new();$psi.FileName=$dotnet.Source;$psi.WorkingDirectory=$selected;$psi.UseShellExecute=$false;$psi.CreateNoWindow=$true;$psi.RedirectStandardOutput=$true;$psi.RedirectStandardError=$true
    foreach($name in @('USERPROFILE','HOME','DOTNET_CLI_HOME','NUGET_PACKAGES','LOCALAPPDATA','APPDATA','DOTNET_ROOT','DOTNET_HOST_PATH','PATH')) {
        $value=[Environment]::GetEnvironmentVariable($name)
        if($null-ne $value){$psi.Environment[$name]=$value}else{[void]$psi.Environment.Remove($name)}
    }
    $psi.Environment['DOTNET_CLI_UI_LANGUAGE']='en-US';$psi.Environment['DOTNET_NOLOGO']='1';$psi.Environment['DOTNET_CLI_TELEMETRY_OPTOUT']='1';$psi.Environment['DOTNET_SKIP_FIRST_TIME_EXPERIENCE']='1'
    if($env:NAVLYN_SETUP_TEST_ROOT){
        $boundary=[IO.Path]::GetFullPath($env:NAVLYN_SETUP_TEST_ROOT).TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar
        foreach($name in @('USERPROFILE','DOTNET_CLI_HOME','NUGET_PACKAGES','LOCALAPPDATA','APPDATA')){
            if(!$psi.Environment.ContainsKey($name) -or ![IO.Path]::IsPathFullyQualified($psi.Environment[$name]) -or ![IO.Path]::GetFullPath($psi.Environment[$name]).StartsWith($boundary,(Get-SetupPathComparison))){throw "Global fixture environment escaped its reviewed root: $name"}
            Assert-SetupNoReparsePath $psi.Environment[$name]
        }
        if(!$resolvedRoot.StartsWith($boundary,(Get-SetupPathComparison))){throw 'Global fixture target escaped its reviewed root.'}
    }
    foreach($argument in $Arguments){[void]$psi.ArgumentList.Add($argument)}
    $process=[Diagnostics.Process]::new();$process.StartInfo=$psi;$started=[DateTime]::UtcNow
    if(!$process.Start()){throw 'Could not start the resolved dotnet executable.'}
    $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync();$timedOut=$false
    try {
        if(!$process.WaitForExit(180000)){$timedOut=$true;$process.Kill($true);$process.WaitForExit()}
        $result=[ordered]@{arguments=$Arguments;targetRoot=$resolvedRoot;exit=$process.ExitCode;timedOut=$timedOut;stdout=$stdout.GetAwaiter().GetResult();stderr=$stderr.GetAwaiter().GetResult();startedUtc=$started.ToString('O')}
        if($LogRoot){[IO.Directory]::CreateDirectory($LogRoot)|Out-Null;Write-SetupAtomicText (Join-Path $LogRoot ('dotnet-'+[guid]::NewGuid().ToString('N')+'.json')) ($result|ConvertTo-Json -Depth 20) $false}
        if($timedOut){throw 'Global dotnet command exceeded its deadline; captured diagnostics retained.'}
        return $result
    } finally {if(!$process.HasExited){$process.Kill($true);$process.WaitForExit()};$process.Dispose()}
}

function Get-GlobalRawState([string]$ToolsRoot,[string[]]$Commands) {
    $full=[IO.Path]::GetFullPath($ToolsRoot);Assert-SetupNoReparsePath $full
    $store=Join-Path $full '.store/navlyn-mcp';$files=[ordered]@{}
    if(Test-Path -LiteralPath $store){
        foreach($file in @(Get-ChildItem -LiteralPath $store -Recurse -File -Force)){
            Assert-SetupNoReparsePath $file.FullName
            $files[$file.FullName.Substring($store.Length).TrimStart('\','/').Replace('\','/')]=Get-SetupHashFile $file.FullName
        }
        foreach($directory in @(Get-ChildItem -LiteralPath $store -Recurse -Directory -Force)){Assert-SetupNoReparsePath $directory.FullName}
    }
    $shims=[ordered]@{}
    foreach($name in @($Commands|Sort-Object -Unique -CaseSensitive)){
        if($name -cnotin @('navlyn','navlyn-mcp')){throw 'Unexpected Navlyn package command name.'}
        $path=Join-Path $full ($name+$(if($IsWindows){'.exe'}else{''}));Assert-SetupNoReparsePath $path
        $shims[$name]=if(Test-Path -LiteralPath $path -PathType Leaf){Get-SetupHashFile $path}else{$null}
    }
    [ordered]@{toolsRoot=$full;storePresent=(Test-Path -LiteralPath $store -PathType Container);commandNames=@($Commands|Sort-Object -Unique -CaseSensitive);storeFiles=$files;shimFiles=$shims}
}
function Assert-GlobalRawState([Collections.IDictionary]$Expected) {
    if($Expected.toolsRoot -cne (Get-GlobalToolsRoot)){throw 'Global transaction target identity differs.'}
    $actual=Get-GlobalRawState $Expected.toolsRoot @($Expected.commandNames)
    if($actual.storePresent -ne $Expected.storePresent -or $actual.storeFiles.Count -ne $Expected.storeFiles.Count -or $actual.shimFiles.Count -ne $Expected.shimFiles.Count){throw 'Global package file set changed concurrently.'}
    foreach($key in $Expected.storeFiles.Keys){if(!$actual.storeFiles.Contains($key) -or $actual.storeFiles[$key] -cne $Expected.storeFiles[$key]){throw "Global Navlyn package changed: $key"}}
    foreach($key in $Expected.shimFiles.Keys){if($actual.shimFiles[$key] -cne $Expected.shimFiles[$key]){throw "Global Navlyn command shim changed: $key"}}
}
function New-GlobalStateSnapshot([Collections.IDictionary]$State,[string]$Root) {
    Assert-GlobalRawState $State
    if(Test-Path -LiteralPath $Root){throw 'Snapshot destination already exists.'}
    Assert-SetupNoReparsePath $Root;[IO.Directory]::CreateDirectory($Root)|Out-Null
    if($State.storePresent){Copy-Item -LiteralPath (Join-Path $State.toolsRoot '.store/navlyn-mcp') -Destination (Join-Path $Root 'store') -Recurse}
    [IO.Directory]::CreateDirectory((Join-Path $Root 'shims'))|Out-Null
    foreach($name in $State.commandNames){if($State.shimFiles[$name]){$leaf=$name+$(if($IsWindows){'.exe'}else{''});Copy-Item -LiteralPath (Join-Path $State.toolsRoot $leaf) -Destination (Join-Path $Root ('shims/'+$leaf))}}
    $record=[ordered]@{schema='navlyn.setup.global-snapshot.v1';root=$Root;state=$State}
    Write-SetupAtomicText (Join-Path $Root 'snapshot.json') ($record|ConvertTo-Json -Depth 30) $false
    $record['manifestSha256']=Get-SetupHashFile (Join-Path $Root 'snapshot.json')
    Assert-GlobalStateSnapshot $record
    return $record
}
function Read-SetupPackageXml([string]$Path) {
    if((Get-Item -LiteralPath $Path).Length-gt 1048576){throw 'Package XML metadata exceeds its bound.'}
    $settings=[Xml.XmlReaderSettings]::new();$settings.DtdProcessing=[Xml.DtdProcessing]::Prohibit;$settings.XmlResolver=$null
    $reader=[Xml.XmlReader]::Create($Path,$settings)
    try{$xml=[Xml.XmlDocument]::new();$xml.XmlResolver=$null;$xml.Load($reader);return ,$xml}finally{$reader.Dispose()}
}
function Assert-GlobalStateSnapshot([Collections.IDictionary]$Record) {
    $root=[IO.Path]::GetFullPath([string]$Record.root);$boundary=[IO.Path]::GetFullPath($stateRoot).TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar
    if($Record.schema -cne 'navlyn.setup.global-snapshot.v1' -or !$root.StartsWith($boundary,(Get-SetupPathComparison))){throw 'Global snapshot is outside owned state or has an invalid schema.'}
    Assert-SetupNoReparsePath $root
    if($Record.manifestSha256 -cnotmatch '^[a-f0-9]{64}$' -or (Get-SetupHashFile (Join-Path $root 'snapshot.json')) -cne $Record.manifestSha256){throw 'Global snapshot retained manifest bytes differ.'}
    $retained=Get-Content -Raw -LiteralPath (Join-Path $root 'snapshot.json')|ConvertFrom-Json -AsHashtable
    if($retained.schema -cne $Record.schema -or $retained.root -cne $Record.root){throw 'Global snapshot manifest identity differs.'}
    foreach($property in @('toolsRoot','storePresent')){if($retained.state[$property] -cne $Record.state[$property]){throw 'Global snapshot state identity differs.'}}
    foreach($property in @('storeFiles','shimFiles')){if($retained.state[$property].Count -ne $Record.state[$property].Count){throw 'Global snapshot record set differs.'};foreach($key in $Record.state[$property].Keys){if(!$retained.state[$property].Contains($key) -or $retained.state[$property][$key] -cne $Record.state[$property][$key]){throw 'Global snapshot record bytes differ.'}}}
    if((@($retained.state.commandNames) -join '|') -cne (@($Record.state.commandNames) -join '|')){throw 'Global snapshot command identity differs.'}
    $state=$Record.state
    if($state.toolsRoot -cne (Get-GlobalToolsRoot) -or $state.storePresent -isnot [bool]){throw 'Snapshot target identity is invalid.'}
    $actual=[ordered]@{};$store=Join-Path $root 'store'
    if(Test-Path -LiteralPath $store){foreach($file in @(Get-ChildItem -LiteralPath $store -Recurse -File -Force)){Assert-SetupNoReparsePath $file.FullName;$actual[$file.FullName.Substring($store.Length).TrimStart('\','/').Replace('\','/')]=Get-SetupHashFile $file.FullName};foreach($dir in @(Get-ChildItem -LiteralPath $store -Recurse -Directory -Force)){Assert-SetupNoReparsePath $dir.FullName}}
    if((Test-Path -LiteralPath $store -PathType Container) -ne $state.storePresent -or $actual.Count -ne $state.storeFiles.Count){throw 'Global snapshot store set differs.'}
    foreach($key in $state.storeFiles.Keys){if($key -match '(^|/)\.\.?(/|$)|[\\:]' -or !$actual.Contains($key) -or $actual[$key] -cne $state.storeFiles[$key]){throw 'Global snapshot package bytes or paths differ.'}}
    $expectedShimCount=0
    foreach($name in $state.commandNames){if($name -cnotin @('navlyn','navlyn-mcp')){throw 'Global snapshot command is invalid.'};$leaf=$name+$(if($IsWindows){'.exe'}else{''});$path=Join-Path $root ('shims/'+$leaf);Assert-SetupNoReparsePath $path;if($state.shimFiles[$name]){$expectedShimCount++;if(!(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-SetupHashFile $path) -cne $state.shimFiles[$name]){throw 'Global snapshot shim bytes differ.'}}elseif(Test-Path -LiteralPath $path){throw 'Unexpected snapshot shim.'}}
    if(@(Get-ChildItem -LiteralPath (Join-Path $root 'shims') -File -Force).Count -ne $expectedShimCount){throw 'Global snapshot contains extra shim files.'}
    if($state.storePresent){
        # The SDK owns extracted package files and may retain multiple archive aliases.
        # Validate the installed identity and every retained file hash; archive count is not identity.
        $nuspecs=@(Get-ChildItem -LiteralPath $store -Recurse -Filter navlyn-mcp.nuspec -File)
        if($nuspecs.Count-ne 1){throw 'Snapshot installed package nuspec is missing or ambiguous.'}
        $xml=Read-SetupPackageXml $nuspecs[0].FullName
        $ids=@($xml.SelectNodes('/*[local-name()="package"]/*[local-name()="metadata"]/*[local-name()="id"]'))
        $versions=@($xml.SelectNodes('/*[local-name()="package"]/*[local-name()="metadata"]/*[local-name()="version"]'))
        $types=@($xml.SelectNodes('/*[local-name()="package"]/*[local-name()="metadata"]/*[local-name()="packageTypes"]/*[local-name()="packageType"]'))
        if($ids.Count-ne 1 -or $ids[0].InnerText-cne 'navlyn-mcp' -or $versions.Count-ne 1 -or $versions[0].InnerText-cnotmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$' -or $types.Count-ne 1 -or $types[0].GetAttribute('name')-cne 'DotnetTool'){throw 'Snapshot nuspec ID/version/package type differs.'}
        $version=$versions[0].InnerText
        $relative=$nuspecs[0].FullName.Substring($store.Length).TrimStart('\','/').Replace('\','/')
        if($relative-cne ($version+'/navlyn-mcp/'+$version+'/navlyn-mcp.nuspec')){throw 'Snapshot nuspec version and installed directory layout differ.'}
        $packageRoot=Split-Path -Parent $nuspecs[0].FullName
        $settings=@(Get-ChildItem -LiteralPath $packageRoot -Filter DotnetToolSettings.xml -Recurse -File)
        if(!$settings.Count){throw 'Snapshot installed command metadata is missing.'}
        $expectedCommands=$null
        foreach($setting in $settings){
            $settingPath=$setting.FullName.Substring($packageRoot.Length).TrimStart('\','/').Replace('\','/')
            if($settingPath-cnotmatch '^tools/[A-Za-z0-9.-]+/any/DotnetToolSettings\.xml$'){throw 'Snapshot command metadata path differs.'}
            $settingsXml=Read-SetupPackageXml $setting.FullName
            if($settingsXml.DocumentElement.Name-cne 'DotNetCliTool' -or $settingsXml.DocumentElement.GetAttribute('Version')-cne '1'){throw 'Snapshot command metadata schema differs.'}
            $commands=@($settingsXml.SelectNodes('/DotNetCliTool/Commands/Command'))
            $names=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            if(!$commands.Count){throw 'Snapshot declares no package command.'}
            foreach($command in $commands){
                $name=$command.GetAttribute('Name');$entryPoint=$command.GetAttribute('EntryPoint')
                if($name-cnotin @('navlyn-mcp','navlyn') -or !$names.Add($name) -or $command.GetAttribute('Runner')-cne 'dotnet' -or $entryPoint-cne 'navlyn.Mcp.dll' -or !(Test-Path -LiteralPath (Join-Path (Split-Path -Parent $setting.FullName) $entryPoint) -PathType Leaf)){throw 'Snapshot command name/runner/entry point differs.'}
                if($name-cnotin $state.commandNames -or !$state.shimFiles[$name]){throw 'Snapshot is missing its declared package command shim.'}
            }
            if(!$names.Contains('navlyn-mcp')){throw 'Snapshot omits the MCP command.'}
            $commandIdentity=@($names|Sort-Object -CaseSensitive)-join '|'
            if($null-ne $expectedCommands -and $expectedCommands-cne $commandIdentity){throw 'Snapshot target frameworks declare different commands.'}
            $expectedCommands=$commandIdentity
        }
    }
}
function Restore-GlobalStateSnapshot([Collections.IDictionary]$Snapshot,[Collections.IDictionary]$Expected) {
    Assert-GlobalStateSnapshot $Snapshot;Assert-GlobalRawState $Expected
    $store=Join-Path $Expected.toolsRoot '.store/navlyn-mcp'
    Assert-SetupNoReparsePath $store
    if(Test-Path -LiteralPath $store){Remove-Item -LiteralPath $store -Recurse -Force}
    if($Snapshot.state.storePresent){[IO.Directory]::CreateDirectory((Split-Path -Parent $store))|Out-Null;Copy-Item -LiteralPath (Join-Path $Snapshot.root 'store') -Destination $store -Recurse}
    foreach($name in $Expected.commandNames){$leaf=$name+$(if($IsWindows){'.exe'}else{''});$target=Join-Path $Expected.toolsRoot $leaf;Assert-SetupNoReparsePath $target;if(Test-Path -LiteralPath $target){Remove-Item -LiteralPath $target -Force};if($Snapshot.state.shimFiles.Contains($name) -and $Snapshot.state.shimFiles[$name]){Copy-Item -LiteralPath (Join-Path $Snapshot.root ('shims/'+$leaf)) -Destination $target}}
    Assert-GlobalRawState $Snapshot.state
}
function Assert-SetupConfigHash([object]$Expected) {
    $actual=if(Test-Path -LiteralPath $configPath -PathType Leaf){Get-SetupHashFile $configPath}else{$null}
    if($actual -cne $Expected){throw 'Configuration changed concurrently; no overwrite is allowed.'}
}
function Assert-GlobalJournal([Collections.IDictionary]$Journal) {
    $cursor=$Journal;$depth=0
    while($cursor){
        if(++$depth -gt 32 -or $cursor.schema -cne 'navlyn.setup.global-ownership.v2' -or $cursor.workspace -cne $selected -or $cursor.config -cne $configPath -or $cursor.targetRoot -cne (Get-GlobalToolsRoot) -or $cursor.packageOwned -isnot [bool]){throw 'Global ownership journal identity or stack is invalid.'}
        if($cursor.priorConfigPresent -isnot [bool]){throw 'Global config backup presence must be a Boolean.'}
        if($cursor.priorConfigPresent){$bytes=[Convert]::FromBase64String([string]$cursor.priorConfigBase64);if((Get-SetupHashBytes $bytes) -cne $cursor.priorConfigHash){throw 'Global config backup bytes differ.'}}
        elseif($null-ne $cursor.priorConfigHash -or $null-ne $cursor.priorConfigBase64){throw 'Global absent-config backup is inconsistent.'}
        Assert-GlobalStateSnapshot $cursor.priorSnapshot
        if($cursor.currentState.toolsRoot -cne (Get-GlobalToolsRoot)){throw 'Global owned state target differs.'}
        $cursor=$cursor.priorJournal
    }
}
function Restore-GlobalPending([Collections.IDictionary]$Pending) {
    if($Pending.schema -cne 'navlyn.setup.global-pending.v1' -or $Pending.workspace -cne $selected -or $Pending.config -cne $configPath -or $Pending.targetRoot -cne (Get-GlobalToolsRoot) -or $Pending.priorConfigPresent -isnot [bool]){throw 'Global pending transaction identity is invalid; no recovery changes made.'}
    Assert-GlobalStateSnapshot $Pending.priorSnapshot
    if($Pending.priorJournal){Assert-GlobalJournal $Pending.priorJournal}
    if($Pending.priorConfigPresent){$bytes=[Convert]::FromBase64String([string]$Pending.priorConfigBase64);if((Get-SetupHashBytes $bytes) -cne $Pending.priorConfigHash){throw 'Global pending config backup differs.'}}elseif($null-ne $Pending.priorConfigBase64 -or $null-ne $Pending.priorConfigHash){throw 'Global pending absent config is inconsistent.'}
    if(!$Pending.expectedState){throw 'Package mutation was interrupted before its output could be inventoried; retain snapshot for explicit manual reconciliation.'}
    Assert-GlobalRawState $Pending.expectedState
    $now=if(Test-Path -LiteralPath $configPath -PathType Leaf){Get-SetupHashFile $configPath}else{$null}
    $committed=if(Test-Path -LiteralPath $journalPath){Get-Content -Raw -LiteralPath $journalPath|ConvertFrom-Json -AsHashtable}else{$null}
    $journalHash=if(Test-Path -LiteralPath $journalPath){Get-SetupHashFile $journalPath}else{$null}
    if($Pending.commitPrepared -is [bool] -and $Pending.commitPrepared -and $journalHash -ceq $Pending.expectedJournalHash -and $now -ceq $Pending.expectedConfigHash){
        if($committed){Assert-GlobalJournal $committed}
        Remove-Item -LiteralPath $pendingPath -Force;return
    }
    if($now -cne $Pending.priorConfigHash -and $now -cne $Pending.expectedConfigHash){throw 'Pending global transaction found concurrent config edits; manual recovery required.'}
    # All snapshots/config/tool expectations are validated before either target changes.
    Restore-GlobalStateSnapshot $Pending.priorSnapshot $Pending.expectedState
    if($now -ceq $Pending.expectedConfigHash){if($Pending.priorConfigPresent){Write-SetupAtomic $configPath $bytes}else{if(Test-Path -LiteralPath $configPath){Remove-Item -LiteralPath $configPath -Force}}}
    if($Pending.priorJournal){Write-SetupJournal $journalPath $Pending.priorJournal}elseif(Test-Path -LiteralPath $journalPath){Remove-Item -LiteralPath $journalPath -Force}
    Remove-Item -LiteralPath $pendingPath -Force
}
function Get-GlobalPackageInventory([string]$ToolsRoot) {
    $fullRoot=[IO.Path]::GetFullPath($ToolsRoot);$storeRoot=Join-Path $fullRoot '.store/navlyn-mcp'
    if (!(Test-Path -LiteralPath $storeRoot -PathType Container)) { return $null }
    Assert-SetupNoReparsePath $storeRoot
    $nuspecs=@(Get-ChildItem -LiteralPath $storeRoot -Filter 'navlyn-mcp.nuspec' -Recurse -File -ErrorAction Stop)
    if ($nuspecs.Count -ne 1) { throw 'Global Navlyn package store is ambiguous; expected exactly one package nuspec.' }
    $nuspec=$nuspecs[0];$nuspecXml=[xml][IO.File]::ReadAllText($nuspec.FullName)
    $ns=[Xml.XmlNamespaceManager]::new($nuspecXml.NameTable);$ns.AddNamespace('n','http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd')
    $packageId=[string]$nuspecXml.SelectSingleNode('/n:package/n:metadata/n:id',$ns).InnerText
    $version=[string]$nuspecXml.SelectSingleNode('/n:package/n:metadata/n:version',$ns).InnerText
    if ($packageId -cne 'navlyn-mcp' -or $version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') { throw 'Global Navlyn nuspec package identity/version is invalid.' }
    $settings=@(Get-ChildItem -LiteralPath (Split-Path -Parent $nuspec.FullName) -Filter 'DotnetToolSettings.xml' -Recurse -File -ErrorAction Stop)
    if (!$settings.Count) { throw 'Global Navlyn package has no DotnetToolSettings.xml command metadata.' }
    $commandNames=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($setting in $settings){$settingsXml=[xml][IO.File]::ReadAllText($setting.FullName);foreach($command in @($settingsXml.SelectNodes('/DotNetCliTool/Commands/Command'))){if($command.Name -notmatch '^[A-Za-z0-9._-]+$'){throw 'Global tool command metadata contains an invalid shim name.'};[void]$commandNames.Add([string]$command.Name)}}
    if(!$commandNames.Count){throw 'Global Navlyn package declares no command shims.'}
    $shimFiles=[ordered]@{};foreach($name in @($commandNames|Sort-Object -CaseSensitive)){$shim=Join-Path $fullRoot ($name+$(if($IsWindows){'.exe'}else{''}));$shimFiles[$name]=if(Test-Path -LiteralPath $shim -PathType Leaf){Get-SetupHashFile $shim}else{$null}}
    if(!$shimFiles.Contains('navlyn-mcp') -or !$shimFiles['navlyn-mcp']){throw 'Global navlyn-mcp command shim is missing.'}
    $storeFiles=[ordered]@{};$storeLength=$storeRoot.Length
    Get-ChildItem -LiteralPath $storeRoot -Recurse -File -Force|ForEach-Object{Assert-SetupNoReparsePath $_.FullName;$relative=$_.FullName.Substring($storeLength).TrimStart([char[]]@([char]92,[char]47)).Replace([string][char]92,'/');$storeFiles[$relative]=Get-SetupHashFile $_.FullName}
    return [ordered]@{toolsRoot=$fullRoot;storeRoot=$storeRoot;version=$version;nuspecPath=$nuspec.FullName;commandNames=@($commandNames|Sort-Object -CaseSensitive);shimFiles=$shimFiles;storeFiles=$storeFiles}
}
function Copy-GlobalPackageSnapshot([System.Collections.IDictionary]$Inventory,[string]$SnapshotRoot) {
    if(Test-Path -LiteralPath $SnapshotRoot){throw 'Global package snapshot destination already exists.'}
    [IO.Directory]::CreateDirectory($SnapshotRoot)|Out-Null
    $snapshotStore=Join-Path $SnapshotRoot 'store';Copy-Item -LiteralPath $Inventory.storeRoot -Destination $snapshotStore -Recurse
    $snapshotShims=Join-Path $SnapshotRoot 'shims';[IO.Directory]::CreateDirectory($snapshotShims)|Out-Null
    foreach($name in $Inventory.commandNames){$source=Join-Path $Inventory.toolsRoot ($name+$(if($IsWindows){'.exe'}else{''}));if(Test-Path -LiteralPath $source -PathType Leaf){Copy-Item -LiteralPath $source -Destination (Join-Path $snapshotShims ([IO.Path]::GetFileName($source)))}}
}
function Assert-GlobalPackageInventory([System.Collections.IDictionary]$Expected) {
    $actual=Get-GlobalPackageInventory ([string]$Expected.toolsRoot)
    if(!$actual -or $actual.version -cne $Expected.version -or $actual.storeFiles.Count -ne $Expected.storeFiles.Count){throw 'Global Navlyn package file set changed; refusing to alter workspace registration.'}
    foreach($path in $Expected.storeFiles.Keys){if(!$actual.storeFiles.Contains($path) -or $actual.storeFiles[$path] -cne $Expected.storeFiles[$path]){throw "Global Navlyn package changed: $path"}}
    foreach($name in $Expected.commandNames){if($actual.shimFiles[$name] -cne $Expected.shimFiles[$name]){throw "Global Navlyn command shim changed: $name"}}
    return $actual
}
function Assert-GlobalPackageSnapshot([System.Collections.IDictionary]$Journal,[string]$StateRoot) {
    $snapshot=[IO.Path]::GetFullPath([string]$Journal.snapshotRoot);$base=[IO.Path]::GetFullPath($StateRoot).TrimEnd([char[]]@([char]92,[char]47))+[IO.Path]::DirectorySeparatorChar
    if(!$snapshot.StartsWith($base,(Get-SetupPathComparison))){throw 'Global package snapshot is outside owned setup state.'}
    Assert-SetupNoReparsePath $snapshot
    $snapshotStore=Join-Path $snapshot 'store';$expectedStore=$Journal.storeFiles
    if(!(Test-Path -LiteralPath $snapshotStore -PathType Container)){throw 'Global package snapshot store is missing.'}
    $actual=[ordered]@{};$len=$snapshotStore.Length
    Get-ChildItem -LiteralPath $snapshotStore -Recurse -File -Force|ForEach-Object{Assert-SetupNoReparsePath $_.FullName;$relative=$_.FullName.Substring($len).TrimStart([char[]]@([char]92,[char]47)).Replace([string][char]92,'/');$actual[$relative]=Get-SetupHashFile $_.FullName}
    if($actual.Count -ne $expectedStore.Count){throw 'Global package snapshot file set changed.'}
    foreach($path in $expectedStore.Keys){if(!$actual.Contains($path) -or $actual[$path] -cne $expectedStore[$path]){throw "Global package snapshot changed: $path"}}
    foreach($name in $Journal.commandNames){$shimName=[string]$name+$(if($IsWindows){'.exe'}else{''});$snapshotShim=Join-Path (Join-Path $snapshot 'shims') $shimName;$expectedHash=$Journal.shimFiles[[string]$name];if($expectedHash){if(!(Test-Path -LiteralPath $snapshotShim -PathType Leaf) -or (Get-SetupHashFile $snapshotShim) -cne $expectedHash){throw "Global shim snapshot changed: $shimName"}}elseif(Test-Path -LiteralPath $snapshotShim){throw "Unexpected global shim in snapshot: $shimName"}}
}
function Assert-SetupNoReparsePath([string]$Path) {
    $full=[IO.Path]::GetFullPath($Path); $current=$full
    while (![string]::IsNullOrWhiteSpace($current)) {
        $item=Get-Item -LiteralPath $current -Force -ErrorAction SilentlyContinue
        if ($item -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Reparse points are not allowed in owned paths: $current" }
        $parent=Split-Path -Parent $current; if ($parent -eq $current) { break }; $current=$parent
    }
}
function Assert-SetupOwnedStage([string]$Path,[string]$ToolsRoot,[hashtable]$StageRecord) {
    $full=[IO.Path]::GetFullPath($Path); $base=[IO.Path]::GetFullPath($ToolsRoot).TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar
    if (!$full.StartsWith($base,(Get-SetupPathComparison))) { throw 'Journal tool path is outside the owned tools root.' }
    if (!(Test-Path -LiteralPath $full -PathType Container)) { throw 'Journal-owned tool stage is missing.' }
    Assert-SetupNoReparsePath $full
    $marker=Join-Path $full '.navlyn-setup-owner.json'
    if (!(Test-Path -LiteralPath $marker -PathType Leaf)) { throw 'Owned tool marker is missing.' }
    $markerDoc=Get-Content -Raw -LiteralPath $marker | ConvertFrom-Json -AsHashtable
    if ($markerDoc.installId -ne $StageRecord.installId -or $markerDoc.workspaceKey -ne $StageRecord.workspaceKey) { throw 'Owned tool marker does not match the journal.' }
    $actual=[ordered]@{}
    Get-ChildItem -LiteralPath $full -Recurse -File -Force | ForEach-Object {
        Assert-SetupNoReparsePath $_.FullName
        $relative=$_.FullName.Substring($full.Length).TrimStart('\','/').Replace('\','/')
        if ($relative -ne '.navlyn-setup-owner.json') { $actual[$relative]=Get-SetupHashFile $_.FullName }
    }
    $expected=$StageRecord.files
    if ($actual.Count -ne $expected.Count) { throw 'Owned tool file set changed; refusing cleanup.' }
    foreach ($key in $expected.Keys) { if (!$actual.Contains($key) -or $actual[$key] -cne $expected[$key]) { throw "Owned tool file changed: $key" } }
}
function Remove-SetupNewStage([string]$Path,[string]$ToolsRoot,[string]$InstallId,[string]$WorkspaceKey) {
    if (!(Test-Path -LiteralPath $Path -PathType Container)) { return }
    $full=[IO.Path]::GetFullPath($Path);$base=[IO.Path]::GetFullPath($ToolsRoot).TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar
    if(!$full.StartsWith($base,(Get-SetupPathComparison))){throw 'Refusing cleanup outside the owned tools root.'}
    $marker=Join-Path $full '.navlyn-setup-owner.json'
    if(!(Test-Path -LiteralPath $marker -PathType Leaf)){throw 'Refusing cleanup without the owned stage marker.'}
    $markerDoc=Get-Content -Raw -LiteralPath $marker|ConvertFrom-Json -AsHashtable
    if($markerDoc.installId -cne $InstallId -or $markerDoc.workspaceKey -cne $WorkspaceKey){throw 'Refusing cleanup because the stage marker identity differs.'}
    Assert-SetupNoReparsePath $full
    Get-ChildItem -LiteralPath $full -Recurse -Force | ForEach-Object { Assert-SetupNoReparsePath $_.FullName }
    Remove-Item -LiteralPath $full -Recurse -Force
}
function Get-SetupJournalStages([System.Collections.IDictionary]$Journal) {
    $result=[Collections.Generic.List[object]]::new();$cursor=$Journal
    while($cursor){foreach($stage in @($cursor.stages)){$result.Add($stage)};$cursor=$cursor.priorJournal}
    return ,$result.ToArray()
}
function Get-SetupRawEntry([string]$Text) {
    $span=Get-NavlynJsoncPropertySpan -Text $Text -ObjectName 'servers' -PropertyName 'navlyn'
    if (!$span) { return $null }
    return $Text.Substring($span.Start,$span.Length)
}
function Write-SetupJournal([string]$Path,[Collections.IDictionary]$Journal) {
    Write-SetupAtomicText $Path ($Journal | ConvertTo-Json -Depth 40) $false
}
function Send-SetupRpc([Diagnostics.Process]$Process,[hashtable]$Payload,[int]$TimeoutMilliseconds,[ref]$StderrTask) {
    $json=$Payload | ConvertTo-Json -Depth 40 -Compress
    $Process.StandardInput.WriteLine($json); $Process.StandardInput.Flush()
    $until=[DateTime]::UtcNow.AddMilliseconds($TimeoutMilliseconds)
    while ([DateTime]::UtcNow -lt $until) {
        $left=[Math]::Max(1,[int]($until-[DateTime]::UtcNow).TotalMilliseconds)
        $lineTask=$Process.StandardOutput.ReadLineAsync()
        if (!$lineTask.Wait($left)) { throw 'MCP protocol response timed out.' }
        $line=$lineTask.GetAwaiter().GetResult()
        if ($null -eq $line) { throw 'MCP server closed stdout before replying.' }
        $byteCount=[Text.Encoding]::UTF8.GetByteCount($line)
        if ($byteCount -gt 4MB) { throw 'MCP protocol response exceeded 4 MiB.' }
        try { $response=$line | ConvertFrom-Json -AsHashtable -Depth 60 } catch { throw 'MCP stdout contained invalid JSON.' }
        if ($response.id -eq $Payload.id) { return $response }
        if ($null -ne $response.id) { throw "MCP response ID mismatch; expected $($Payload.id)." }
    }
    throw 'MCP protocol response timed out.'
}
function Send-SetupNotification([Diagnostics.Process]$Process,[hashtable]$Payload) {
    $Process.StandardInput.WriteLine(($Payload | ConvertTo-Json -Depth 20 -Compress)); $Process.StandardInput.Flush()
}
function Stop-SetupProcess([Diagnostics.Process]$Process,[Diagnostics.ProcessStartInfo]$StartInfo,[Diagnostics.Process]$DrainProcess,[System.Threading.Tasks.Task[string]]$StderrTask) {
    if (!$Process.HasExited) { try { $Process.StandardInput.Close() } catch {}; if (!$Process.WaitForExit(3000)) { $Process.Kill($true); $Process.WaitForExit() } }
    if ($StderrTask -and $StderrTask.IsCompleted) { $stderr=$StderrTask.GetAwaiter().GetResult(); if ([Text.Encoding]::UTF8.GetByteCount($stderr) -gt 16384) { throw 'MCP stderr exceeded 16 KiB.' } }
}
function Test-SetupMcp([string]$Executable,[string]$WorkspaceRoot,[string]$WorkspaceArgument,[string]$ExpectedVersion) {
    $psi=[Diagnostics.ProcessStartInfo]::new(); $psi.FileName=$Executable; $psi.WorkingDirectory=$WorkspaceRoot; $psi.UseShellExecute=$false; $psi.RedirectStandardInput=$true; $psi.RedirectStandardOutput=$true; $psi.RedirectStandardError=$true; $psi.StandardOutputEncoding=[Text.UTF8Encoding]::new($false,$true); $psi.StandardErrorEncoding=[Text.UTF8Encoding]::new($false,$true)
    if($env:NAVLYN_SETUP_TEST_ROOT){$boundary=[IO.Path]::GetFullPath($env:NAVLYN_SETUP_TEST_ROOT).TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar;if(![IO.Path]::GetFullPath($Executable).StartsWith($boundary,(Get-SetupPathComparison))){throw 'Fixture MCP executable escaped its reviewed root.'}}
    foreach ($arg in @('--workspace',$WorkspaceArgument,'--working-directory',$WorkspaceRoot,'--timeout-ms','60000')) { [void]$psi.ArgumentList.Add($arg) }
    $p=[Diagnostics.Process]::new(); $p.StartInfo=$psi
    if (!$p.Start()) { throw 'Could not start the staged MCP executable.' }
    $stderrTask=$p.StandardError.ReadToEndAsync()
    try {
        $init=Send-SetupRpc $p @{jsonrpc='2.0';id=1;method='initialize';params=@{protocolVersion='2025-06-18';capabilities=@{};clientInfo=@{name='navlyn-setup';version='1.0'}}} 30000 ([ref]$stderrTask)
        $expectedServerVersion=([regex]::Match($ExpectedVersion,'^\d+\.\d+\.\d+').Value)+'.0'
        if (!$init.result -or $init.result.protocolVersion -ne '2025-06-18' -or $init.result.serverInfo.name -ne 'navlyn.Mcp' -or $init.result.serverInfo.version -ne $expectedServerVersion) { throw "MCP initialize metadata/version did not match the staged package: $($init.result.serverInfo | ConvertTo-Json -Compress)." }
        Send-SetupNotification $p @{jsonrpc='2.0';method='notifications/initialized';params=@{}}
        $tools=Send-SetupRpc $p @{jsonrpc='2.0';id=2;method='tools/list';params=@{}} 30000 ([ref]$stderrTask)
        $expected=@('navlyn_target','navlyn_read','navlyn_file_outline','navlyn_navigate','navlyn_prepare_edit','navlyn_verify_edit','navlyn_review','navlyn_workspace_summary','navlyn_workspace_status','navlyn_workspace_refresh','navlyn_doctor','navlyn_impact','navlyn_context_pack','navlyn_entrypoints','navlyn_tests_for_symbol','navlyn_tests_for_diff','navlyn_diagnostics','navlyn_di','navlyn_public_api_diff','navlyn_routes','navlyn_options','navlyn_messages','navlyn_ef','navlyn_packages','navlyn_batch')
        $names=@($tools.result.tools | ForEach-Object { [string]$_.name })
        if (($names -join "`n") -cne ($expected -join "`n")) { throw 'MCP tools/list did not match the locked 25-tool inventory and order.' }
        $summaryA=Send-SetupRpc $p @{jsonrpc='2.0';id=3;method='tools/call';params=@{name='navlyn_workspace_summary';arguments=@{}}} 60000 ([ref]$stderrTask)
        $summaryB=Send-SetupRpc $p @{jsonrpc='2.0';id=4;method='tools/call';params=@{name='navlyn_workspace_summary';arguments=@{}}} 60000 ([ref]$stderrTask)
        if ($summaryA.result.isError -or $summaryB.result.isError -or !$summaryA.result.structuredContent.ok -or !$summaryB.result.structuredContent.ok -or !$summaryA.result.structuredContent.result -or !$summaryB.result.structuredContent.result) { throw 'Workspace summary returned no successful structured fact.' }
        $textA=$summaryA.result.content | Where-Object type -eq 'text' | Select-Object -First 1
        $textB=$summaryB.result.content | Where-Object type -eq 'text' | Select-Object -First 1
        if(!$textA -or !$textB){throw 'Workspace summary omitted its result text.'}
        $textFactA=$textA.text|ConvertFrom-Json -AsHashtable -Depth 50
        $textFactB=$textB.text|ConvertFrom-Json -AsHashtable -Depth 50
        if(!$textFactA.ok -or !$textFactB.ok){throw 'Workspace summary result text did not contain a successful JSON fact.'}
        $factA=$summaryA.result.structuredContent.result | ConvertTo-Json -Depth 50 -Compress
        $factB=$summaryB.result.structuredContent.result | ConvertTo-Json -Depth 50 -Compress
        if ($factA -cne $factB) { throw 'Repeated workspace summary facts were not deterministic.' }
        $readEvidence=$null
        $sourceFiles=@(Get-ChildItem -LiteralPath $WorkspaceRoot -Filter '*.cs' -File -Recurse -ErrorAction SilentlyContinue)+@(Get-ChildItem -LiteralPath $WorkspaceRoot -Filter '*.vb' -File -Recurse -ErrorAction SilentlyContinue)
        $sourceFiles=@($sourceFiles | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } | Sort-Object FullName)
        $rpcId=10
        foreach($sourceFile in $sourceFiles){
            $outlineArguments=@{file=$sourceFile.FullName}
            if([IO.Path]::GetExtension($WorkspaceArgument) -in @('.csproj','.vbproj')){$outlineArguments.project=$WorkspaceArgument}
            $outline=Send-SetupRpc $p @{jsonrpc='2.0';id=$rpcId;method='tools/call';params=@{name='navlyn_file_outline';arguments=$outlineArguments}} 60000 ([ref]$stderrTask)
            $rpcId++
            if($outline.result.isError -or !$outline.result.structuredContent.ok){continue}
            $entries=@($outline.result.structuredContent.result.entries | Where-Object { $_.candidateId -and $_.line -gt 0 -and $_.column -gt 0 -and $_.kind -cne 'Namespace' })
            if($entries.Count -eq 0){continue}
            $orderedEntries=@($entries | Where-Object { $_.kind -ceq 'Method' })+@($entries | Where-Object { $_.kind -cne 'Method' })
            foreach($selectedEntry in $orderedEntries){
                $views=if($selectedEntry.kind -ceq 'Method'){@('signature','declaration','body')}else{@('declaration')}
                $readValid=$true
                foreach($view in $views){
                    $read=Send-SetupRpc $p @{jsonrpc='2.0';id=$rpcId;method='tools/call';params=@{name='navlyn_read';arguments=@{file=$sourceFile.FullName;line=[int]$selectedEntry.line;column=[int]$selectedEntry.column;view=$view}}} 60000 ([ref]$stderrTask)
                    $rpcId++
                    $readSymbol=$null
                    if(!$read.result.isError -and $read.result.structuredContent.ok -and $read.result.structuredContent.result){$readSymbol=$read.result.structuredContent.result.symbol}
                    if($read.result.isError -or !$read.result.structuredContent.ok -or !$readSymbol -or $readSymbol.name -cne $selectedEntry.name -or $readSymbol.kind -cne $selectedEntry.kind -or $readSymbol.container -cne $selectedEntry.container){$readValid=$false;break}
                }
                if(!$readValid){continue}
                $readEvidence=[ordered]@{file=$sourceFile.FullName;line=[int]$selectedEntry.line;column=[int]$selectedEntry.column;symbol=$selectedEntry.name;kind=$selectedEntry.kind;candidateId=$selectedEntry.candidateId;readRoute='source-position';views=@($views)}
                break
            }
            if($readEvidence){break}
        }
        if(!$readEvidence){throw 'No loaded C# or Visual Basic symbol was available for semantic setup validation.'}
        return [pscustomobject]@{ serverInfo=$init.result.serverInfo; tools=$names; workspaceFactSha256=Get-SetupHashText $factA; readEvidence=$readEvidence }
    } finally {
        if (!$p.HasExited) { try{$p.StandardInput.Close()}catch{}; if (!$p.WaitForExit(3000)) { $p.Kill($true); $p.WaitForExit() } }
        $null=$p.WaitForExit(); $stderr=$stderrTask.GetAwaiter().GetResult(); if ([Text.Encoding]::UTF8.GetByteCount($stderr) -gt 16384) { $p.Dispose(); throw 'MCP stderr exceeded 16 KiB.' }; if ($p.ExitCode -ne 0) { $p.Dispose(); throw "MCP server exited with code $($p.ExitCode)." }; $p.Dispose()
    }
}

$selected=[IO.Path]::GetFullPath($Workspace)
$selectedFile=$null
if ($WorkspaceFile) { $selectedFile=[IO.Path]::GetFullPath($WorkspaceFile); if (!(Test-Path -LiteralPath $selectedFile -PathType Leaf)) { throw "Selected workspace file does not exist: $selectedFile" } }
if (!(Test-Path -LiteralPath $selected -PathType Container)) { throw "Workspace root does not exist: $selected" }
$workspaceArgument=if ($selectedFile) { $selectedFile } else { $selected }
$workspaceKey=Get-SetupWorkspaceKey $selected
$stateRoot=Join-Path (Get-SetupDataRoot) $workspaceKey
$toolsRoot=Join-Path $stateRoot 'tools'; $journalPath=Join-Path $stateRoot 'ownership.json'; $pendingPath=Join-Path $stateRoot 'pending.json'
$configPath=Join-Path $selected '.vscode/mcp.json'
Assert-SetupNoReparsePath $stateRoot
Assert-SetupNoReparsePath $configPath
$pendingRecovery=$null
if($Apply -and $Target -eq 'Local' -and (Test-Path -LiteralPath $pendingPath)){
    try { $pendingRecovery=Get-Content -Raw -LiteralPath $pendingPath|ConvertFrom-Json -AsHashtable } catch { throw 'Pending setup transaction is malformed; no recovery changes were made.' }
    if($pendingRecovery.schema -cne 'navlyn.setup.pending.v1' -or $pendingRecovery.workspace -cne $selected -or $pendingRecovery.config -cne $configPath -or !$pendingRecovery.stage -or $pendingRecovery.expectedConfigHash -notmatch '^[0-9a-f]{64}$'){throw 'Pending setup transaction identity/schema is invalid; no recovery changes were made.'}
    if($pendingRecovery.priorConfigPresent){try{$priorRecoveryBytes=[Convert]::FromBase64String([string]$pendingRecovery.priorConfigBase64)}catch{throw 'Pending prior-config backup is invalid; no recovery changes were made.'};if((Get-SetupHashBytes $priorRecoveryBytes) -cne $pendingRecovery.priorConfigHash){throw 'Pending prior-config hash does not match its backup; no recovery changes were made.'}}
    elseif($null -ne $pendingRecovery.priorConfigHash -or $null -ne $pendingRecovery.priorConfigBase64){throw 'Pending absent-config record is inconsistent; no recovery changes were made.'}
    Assert-SetupOwnedStage -Path $pendingRecovery.stage.path -ToolsRoot $toolsRoot -StageRecord $pendingRecovery.stage
    $recoveryConfigHash=if(Test-Path -LiteralPath $configPath){Get-SetupHashFile $configPath}else{$null}
    $committedJournal=$null
    if(Test-Path -LiteralPath $journalPath){try{$committedJournal=Get-Content -Raw -LiteralPath $journalPath|ConvertFrom-Json -AsHashtable}catch{throw 'Ownership journal is malformed during pending recovery; no changes were made.'}}
    $transactionCommitted=$committedJournal -and $committedJournal.schema -ceq 'navlyn.setup.ownership.v2' -and $committedJournal.workspace -ceq $selected -and $committedJournal.config -ceq $configPath -and $committedJournal.committedConfigHash -ceq $pendingRecovery.expectedConfigHash -and @($committedJournal.stages|Where-Object installId -CEQ $pendingRecovery.stage.installId).Count -eq 1
    if($transactionCommitted -and $recoveryConfigHash -ceq $pendingRecovery.expectedConfigHash){Remove-Item -LiteralPath $pendingPath -Force}
    else {
        if($recoveryConfigHash -ceq $pendingRecovery.expectedConfigHash){if($pendingRecovery.priorConfigPresent){Write-SetupAtomic $configPath ([Convert]::FromBase64String($pendingRecovery.priorConfigBase64))}else{Remove-Item -LiteralPath $configPath -Force}}
        elseif($recoveryConfigHash -cne $pendingRecovery.priorConfigHash){throw 'A pending setup transaction found a changed config; manual recovery is required and no cleanup was performed.'}
        Remove-SetupNewStage -Path $pendingRecovery.stage.path -ToolsRoot $toolsRoot -InstallId $pendingRecovery.stage.installId -WorkspaceKey $workspaceKey
        Remove-Item -LiteralPath $pendingPath -Force
    }
}
if($Apply -and $Target -eq 'Global' -and (Test-Path -LiteralPath $pendingPath)){Restore-GlobalPending (Get-Content -Raw -LiteralPath $pendingPath|ConvertFrom-Json -AsHashtable)}
$feedPath=if ($Feed) { [IO.Path]::GetFullPath($Feed) } else { 'https://api.nuget.org/v3/index.json' }
if ($Feed -and !(Test-Path -LiteralPath $feedPath -PathType Container)) { throw "Feed directory does not exist: $feedPath" }
if ($Action -in @('Install','Update') -and !$Version) { throw 'Install and Update require an exact -Version.' }
if ($Version -and $Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') { throw 'Version must be an exact NuGet version.' }
$dotnet=Get-Command dotnet -ErrorAction SilentlyContinue
$clientCommand=Get-Command code -ErrorAction SilentlyContinue
$sdkRoots=[Collections.Generic.List[string]]::new()
if($env:DOTNET_ROOT){$sdkRoots.Add((Join-Path $env:DOTNET_ROOT 'sdk'))}
if($dotnet){$dotnetRoot=Split-Path -Parent $dotnet.Source;if((Split-Path -Leaf $dotnetRoot) -ieq 'host'){$dotnetRoot=Split-Path -Parent $dotnetRoot};$sdkRoots.Add((Join-Path $dotnetRoot 'sdk'))}
$sdkAvailable=$false;foreach($sdkRoot in $sdkRoots){if((Test-Path -LiteralPath $sdkRoot -PathType Container) -and @((Get-ChildItem -LiteralPath $sdkRoot -Directory -ErrorAction SilentlyContinue|Where-Object Name -Match '^\d+\.\d+\.\d+')).Count){$sdkAvailable=$true;break}}
$feedPackage=$null
if ($Feed -and $Version) { $feedPackage=Get-ChildItem -LiteralPath $feedPath -Filter "navlyn-mcp.$Version.nupkg" -File -ErrorAction SilentlyContinue | Select-Object -First 1 }
$oldBytes=if(Test-Path -LiteralPath $configPath -PathType Leaf){[IO.File]::ReadAllBytes($configPath)}else{$null}
$oldText=if($null -ne $oldBytes){Get-SetupText $oldBytes}else{[pscustomobject]@{Text="{`n  `"servers`": {} `n}`n";Bom=$false}}
$parsed=ConvertFrom-NavlynJsonc $oldText.Text
$current=if($parsed.Contains('servers') -and $parsed['servers'] -is [System.Collections.IDictionary] -and $parsed['servers'].Contains('navlyn')){$parsed['servers']['navlyn']}else{$null}
$rawCurrent=Get-SetupRawEntry $oldText.Text
$journal=if(Test-Path -LiteralPath $journalPath){Get-Content -Raw -LiteralPath $journalPath | ConvertFrom-Json -AsHashtable}else{$null}
if($journal -and ($journal.workspace -cne $selected -or $journal.config -cne $configPath -or ($Target -eq 'Local' -and $journal.schema -cne 'navlyn.setup.ownership.v2') -or ($Target -eq 'Global' -and $journal.schema -cne 'navlyn.setup.global-ownership.v2'))){throw 'Ownership journal does not match this selected target/workspace/config.'}
$conflict=$null;$status=$null
if ($current -and (!$journal -or (Get-SetupHashText $rawCurrent) -cne $journal.entryHash)) { $conflict='Existing navlyn entry is not byte-identical to this helper-owned entry.' }
if ($Target -eq 'Local' -and $Action -eq 'Update' -and !$current) { throw 'Cannot update without an owned installed entry.' }
if ($Action -in @('Remove','Undo') -and !$journal) { throw 'No setup ownership journal exists.' }
if ($journal -and $Action -in @('Remove','Undo') -and $conflict) { throw $conflict }
if ($Target -eq 'Local' -and $journal -and $Version -and $Action -in @('Install','Update')) {
    $comparison=Compare-SetupVersion ([string]$journal.installedVersion) $Version
    if ($comparison -gt 0 -and !$AllowDowngrade) { $status='A newer owned version is installed; use -AllowDowngrade to replace it.' }
    elseif ([string]$journal.installedVersion -ceq $Version -and !$conflict) { $status='Requested version is already configured; no changes are needed.' }
}
$candidateText=$oldText.Text
if ($Action -in @('Install','Update') -and !$conflict) {
    $plannedCommand=Join-Path (Join-Path $toolsRoot ("$Version-pending")) $(if($IsWindows){'navlyn-mcp.exe'}else{'navlyn-mcp'})
    $entry=[ordered]@{type='stdio';command=$plannedCommand;args=@('--workspace',$workspaceArgument,'--working-directory',$selected,'--timeout-ms','60000');cwd='${workspaceFolder}'}
    $candidateText=Set-NavlynJsoncServer -Text $candidateText -Name navlyn -EntryJson ($entry|ConvertTo-Json -Depth 10 -Compress)
}
if (!$Apply) {
    $plannedCommand=$null
    $globalToolsRoot=if($Target -eq 'Global'){Get-GlobalToolsRoot}else{$null}
    $globalInventory=if($Target -eq 'Global'){Get-GlobalPackageInventory $globalToolsRoot}else{$null}
    $globalShim=if($globalInventory){Join-Path $globalToolsRoot $(if($IsWindows){'navlyn-mcp.exe'}else{'navlyn-mcp'})}else{$null}
    $globalEffective=if($globalInventory){[string]$globalInventory.version}else{$null}
    $globalPackageAction=if(!$globalInventory){'Install'}elseif($Version -and (Compare-SetupVersion $globalEffective $Version) -gt 0 -and !$AllowDowngrade){'KeepNewer'}elseif($Version -and $Version -ceq $globalEffective){'AlreadyCurrent'}elseif($Version){'Update'}else{'Installed'}
    [ordered]@{action=$Action;mode='Plan';client=$Client;target=$Target;workspace=$selected;workspaceFile=$selectedFile;sdkAvailable=$sdkAvailable;sdkPrerequisite='https://dotnet.microsoft.com/download';clientAvailable=$null-ne $clientCommand;clientPrerequisite='https://code.visualstudio.com/docs/setup/setup-overview';config=$configPath;packageTarget=if($Target -eq 'Global'){$globalToolsRoot}else{$toolsRoot};globalPackageVersion=$globalEffective;globalPackageAction=if($Target -eq 'Global'){$globalPackageAction}else{$null};globalShim=if($Target -eq 'Global'){$globalShim}else{$null};version=$Version;feed=$feedPath;feedPackageAvailable=($null-ne $feedPackage);conflict=$conflict;status=$status;registrationAction=if($conflict){'Conflict'}elseif($journal){'AlreadyOwned'}else{'AddOwnedEntry'};oldExecutable=if($globalInventory){$globalShim}else{$null};newExecutable=if($Target -eq 'Global'){Join-Path $globalToolsRoot $(if($IsWindows){'navlyn-mcp.exe'}else{'navlyn-mcp'})}else{$plannedCommand};requestedVersion=$Version;effectiveVersion=if($Target -eq 'Global' -and $globalPackageAction -in @('KeepNewer','AlreadyCurrent')){$globalEffective}else{$Version};effects=@{writes=$false;network=$false;installation=$false;clientLaunch=$false}}|ConvertTo-Json -Depth 10
    exit 0
}
if($Target -eq 'Global'){
    if($conflict){throw $conflict}
    $globalToolsRoot=Get-GlobalToolsRoot
    $globalInventory=Get-GlobalPackageInventory $globalToolsRoot
    $globalShim=Join-Path $globalToolsRoot $(if($IsWindows){'navlyn-mcp.exe'}else{'navlyn-mcp'})
    if($journal){Assert-GlobalJournal $journal;Assert-GlobalRawState $journal.currentState}
    if(!$dotnet -or !$sdkAvailable){throw 'A .NET SDK is required; no global changes were made.'}
    if($Action -in @('Install','Update') -and !$clientCommand){throw 'VS Code command-line client was not found; no global changes were made.'}
    if($Action -in @('Install','Update') -and $Feed -and !$feedPackage -and (!$globalInventory -or (Compare-SetupVersion $globalInventory.version $Version) -lt 0 -or $AllowDowngrade)){throw "Exact local package navlyn-mcp.$Version is missing; no global changes made."}
    $transactionId=[guid]::NewGuid().ToString('N')
    $transactionRoot=Join-Path (Join-Path $stateRoot 'global-snapshots') $transactionId
    $logRoot=Join-Path $transactionRoot 'diagnostics'
    $listed=Invoke-SetupGlobalDotnet -Arguments @('tool','list','--global') -LogRoot $logRoot
    if($listed.exit -ne 0){throw "Global tool list failed: $($listed.stdout) $($listed.stderr)"}
    $listedRow=[regex]::Match($listed.stdout,'(?m)^navlyn-mcp[\t ]+(?<version>\S+)[\t ]+(?<commands>[^\r\n]+)\r?$')
    if(($null-ne $globalInventory) -ne $listedRow.Success -or ($globalInventory -and $listedRow.Groups['version'].Value -cne $globalInventory.version)){throw 'Global tool list and package metadata disagree; no changes made.'}
    $oldConfigHash=if($null-ne $oldBytes){Get-SetupHashBytes $oldBytes}else{$null}
    $commands=@('navlyn-mcp')
    if($globalInventory){$commands=@($globalInventory.commandNames)}
    $packageAction='Reused';$effectiveVersion=if($globalInventory){$globalInventory.version}else{$null};$packageOwned=$false;$mutate=$false
    $feedHash=if($feedPackage){Get-SetupHashFile $feedPackage.FullName}else{$null}
    $restoreTarget=$null;$journalAfter=$null
    if($Action -in @('Install','Update')){
        $comparison=if($globalInventory){Compare-SetupVersion $globalInventory.version $Version}else{-1}
        $mutate=!$globalInventory -or $comparison -lt 0 -or ($comparison -gt 0 -and $AllowDowngrade)
        if($comparison -gt 0 -and !$AllowDowngrade){$packageAction='KeepNewer'}
        elseif($mutate){$packageAction=if($globalInventory){'Updated'}else{'Installed'};$effectiveVersion=$Version;$packageOwned=$true}
        elseif($journal -and !$conflict){[ordered]@{action=$Action;target='Global';result='Unchanged';packageAction='AlreadyCurrent';version=$effectiveVersion;packageTarget=$globalToolsRoot;config=$configPath}|ConvertTo-Json;exit 0}
        if(!$mutate -and $journal -and !$conflict){[ordered]@{action=$Action;target='Global';result='Unchanged';packageAction=$packageAction;version=$effectiveVersion;packageTarget=$globalToolsRoot;config=$configPath}|ConvertTo-Json;exit 0}
        if($mutate -and $Feed -and !$feedPackage){throw "Exact local package navlyn-mcp.$Version is missing; no global changes made."}
        if($mutate -and $feedPackage){
            $zip=[IO.Compression.ZipFile]::OpenRead($feedPackage.FullName)
            try {
                $entries=@($zip.Entries|Where-Object {$_.FullName -ceq 'navlyn-mcp.nuspec'})
                if($entries.Count-ne 1){throw 'Candidate package nuspec is ambiguous.'}
                $reader=[IO.StreamReader]::new($entries[0].Open());try{$candidateNuspec=[xml]$reader.ReadToEnd()}finally{$reader.Dispose()}
                if($candidateNuspec.SelectSingleNode('/*[local-name()="package"]/*[local-name()="metadata"]/*[local-name()="id"]').InnerText -cne 'navlyn-mcp' -or $candidateNuspec.SelectSingleNode('/*[local-name()="package"]/*[local-name()="metadata"]/*[local-name()="version"]').InnerText -cne $Version){throw 'Candidate feed package ID/version differs.'}
                foreach($entry in @($zip.Entries|Where-Object {$_.FullName -like 'tools/*/any/DotnetToolSettings.xml'})){$reader=[IO.StreamReader]::new($entry.Open());try{$settings=[xml]$reader.ReadToEnd()}finally{$reader.Dispose()};foreach($command in $settings.SelectNodes('/DotNetCliTool/Commands/Command')){if($command.Name -cnotin @('navlyn','navlyn-mcp')){throw 'Candidate declares an unexpected shared command.'};$commands+= [string]$command.Name}}
            }finally{$zip.Dispose()}
        }
    } else {
        if(!$journal -or !$globalInventory){throw 'Global Remove/Undo requires owned registration and unchanged installed package.'}
        $cursor=$journal;$ownedTransactions=@()
        while($cursor){if($cursor.packageOwned){$ownedTransactions+=,$cursor};if($Action -eq 'Undo'){break};$cursor=$cursor.priorJournal}
        foreach($record in $ownedTransactions){$restoreTarget=$record.priorSnapshot}
        if($restoreTarget){
            $setupBase=Get-SetupDataRoot
            foreach($other in @(Get-ChildItem -LiteralPath $setupBase -Filter ownership.json -Recurse -File -ErrorAction SilentlyContinue)){
                if($other.FullName -ceq $journalPath){continue}
                $otherJournal=Get-Content -Raw -LiteralPath $other.FullName|ConvertFrom-Json -AsHashtable
                if($otherJournal.schema -like 'navlyn.setup.global-ownership.*' -and $otherJournal.targetRoot -ceq $globalToolsRoot){throw 'Another workspace references this global package; package restoration/removal is refused.'}
            }
        }
        $packageOwned=$null-ne $restoreTarget
        $packageAction=if($packageOwned){'Restored'}else{'Reused'}
        $journalAfter=if($Action -eq 'Undo'){$journal.priorJournal}else{$null}
    }
    $commands=@($commands|Sort-Object -Unique -CaseSensitive)
    if($mutate -and $Action -in @('Install','Update')){foreach($name in $commands){if((!$globalInventory -or $name -cnotin $globalInventory.commandNames) -and (Test-Path -LiteralPath (Join-Path $globalToolsRoot ($name+$(if($IsWindows){'.exe'}else{''}))))){throw 'A candidate command conflicts with a pre-existing unrelated global shim.'}}}
    $before=Get-GlobalRawState $globalToolsRoot $commands
    $snapshot=New-GlobalStateSnapshot $before (Join-Path $transactionRoot 'before')
    $pending=[ordered]@{schema='navlyn.setup.global-pending.v1';transactionId=$transactionId;workspace=$selected;config=$configPath;targetRoot=$globalToolsRoot;priorSnapshot=$snapshot;priorJournal=$journal;priorConfigPresent=($null-ne $oldBytes);priorConfigBase64=if($null-ne $oldBytes){[Convert]::ToBase64String($oldBytes)}else{$null};priorConfigHash=$oldConfigHash;expectedConfigHash=$null;expectedState=$before;phase='prepared';commitPrepared=$false;expectedJournalHash=$null}
    Write-SetupJournal $pendingPath $pending
    $committed=$false
    try {
        if($Action -in @('Install','Update')){
            if($mutate){
                Assert-SetupConfigHash $oldConfigHash;Assert-GlobalRawState $before
                if($feedPackage -and (Get-SetupHashFile $feedPackage.FullName) -cne $feedHash){throw 'Exact feed package changed before global mutation.'}
                $nugetConfig=Join-Path $transactionRoot 'NuGet.Config'
                $source=[Security.SecurityElement]::Escape($feedPath)
                Write-SetupAtomicText $nugetConfig ('<configuration><packageSources><clear/><add key="selected" value="'+$source+'"/></packageSources></configuration>') $false
                $pending.expectedState=$null;$pending.phase='mutating';Write-SetupJournal $pendingPath $pending
                $arguments=@('tool',$(if($globalInventory){'update'}else{'install'}),'navlyn-mcp','--global','--version',$Version,'--framework','net8.0','--configfile',$nugetConfig,'--verbosity','quiet')
                if($comparison -gt 0){$arguments+='--allow-downgrade'}
                $install=Invoke-SetupGlobalDotnet -Arguments $arguments -LogRoot $logRoot
                $pending.expectedState=Get-GlobalRawState $globalToolsRoot $commands;$pending.phase='verifying';Write-SetupJournal $pendingPath $pending
                if($install.exit-ne 0){throw "Global package mutation failed (exit $($install.exit)): $($install.stdout) $($install.stderr)"}
                if($feedPackage -and (Get-SetupHashFile $feedPackage.FullName) -cne $feedHash){throw 'Exact feed package changed during global mutation.'}
                $globalInventory=Get-GlobalPackageInventory $globalToolsRoot
                if(!$globalInventory -or $globalInventory.version -cne $effectiveVersion){throw 'Mutated global package version differs from exact request.'}
            }
            $verifiedList=Invoke-SetupGlobalDotnet -Arguments @('tool','list','--global') -LogRoot $logRoot
            if($verifiedList.exit-ne 0 -or $verifiedList.stdout -cnotmatch ('(?m)^navlyn-mcp\s+'+[regex]::Escape($effectiveVersion)+'\s+')){throw 'Global list does not confirm effective version.'}
            $protocol=Test-SetupMcp $globalShim $selected $workspaceArgument $effectiveVersion
            $candidateText=Set-NavlynJsoncServer -Text $oldText.Text -Name navlyn -EntryJson (([ordered]@{type='stdio';command=$globalShim;args=@('--workspace',$workspaceArgument,'--working-directory',$selected,'--timeout-ms','60000');cwd='${workspaceFolder}'}|ConvertTo-Json -Depth 10 -Compress))
            $currentState=Get-GlobalRawState $globalToolsRoot $commands
            Assert-GlobalRawState $pending.expectedState
            $journalOut=[ordered]@{schema='navlyn.setup.global-ownership.v2';transactionId=$transactionId;workspace=$selected;workspaceFile=$selectedFile;config=$configPath;targetRoot=$globalToolsRoot;packageId='navlyn-mcp';installedVersion=$effectiveVersion;packageOwned=$packageOwned;priorSnapshot=$snapshot;currentState=$currentState;priorConfigPresent=($null-ne $oldBytes);priorConfigBase64=if($null-ne $oldBytes){[Convert]::ToBase64String($oldBytes)}else{$null};priorConfigHash=$oldConfigHash;priorEntryRaw=$rawCurrent;entryHash=Get-SetupHashText (Get-SetupRawEntry $candidateText);committedConfigHash=$null;protocol=$protocol;feed=$feedPath;feedPackageSha256=if($feedPackage){Get-SetupHashFile $feedPackage.FullName}else{$null};priorJournal=$journal}
        } else {
            $candidateText=if($Action -eq 'Undo' -and $journal.priorEntryRaw){Set-NavlynJsoncServer $oldText.Text 'navlyn' ([string]$journal.priorEntryRaw)}else{Remove-NavlynJsoncServer $oldText.Text 'navlyn'}
            if($restoreTarget){
                Restore-GlobalStateSnapshot $restoreTarget $before
                $pending.expectedState=Get-GlobalRawState $globalToolsRoot $commands;Write-SetupJournal $pendingPath $pending
                $restoredInventory=Get-GlobalPackageInventory $globalToolsRoot
                $restoredList=Invoke-SetupGlobalDotnet -Arguments @('tool','list','--global') -LogRoot $logRoot
                $restoredRow=[regex]::Match($restoredList.stdout,'(?m)^navlyn-mcp[\t ]+(?<version>\S+)[\t ]+(?<commands>[^\r\n]+)\r?$')
                if($restoredList.exit-ne 0 -or ($null-ne $restoredInventory)-ne $restoredRow.Success -or ($restoredInventory -and $restoredRow.Groups['version'].Value-cne $restoredInventory.version)){throw 'Restored global package metadata and SDK list disagree.'}
                $effectiveVersion=if($restoredInventory){$restoredInventory.version}else{$null}
            }
            if($oldConfigHash -ceq $journal.committedConfigHash -and ($Action -eq 'Undo' -or !$journal.priorEntryRaw)){$candidateBytes=if($journal.priorConfigPresent){[Convert]::FromBase64String($journal.priorConfigBase64)}else{$null}}else{$candidateBytes=Get-SetupUtf8Bytes $candidateText $oldText.Bom}
            $journalOut=$journalAfter
        }
        if($Action -in @('Install','Update')){$candidateBytes=Get-SetupUtf8Bytes $candidateText $oldText.Bom}
        $pending.expectedConfigHash=if($null-ne $candidateBytes){Get-SetupHashBytes $candidateBytes}else{$null};$pending.phase='committing';Write-SetupJournal $pendingPath $pending
        if($journalOut -and $Action -in @('Install','Update')){$journalOut.committedConfigHash=$pending.expectedConfigHash}
        $pending.expectedJournalHash=if($journalOut){Get-SetupHashText ($journalOut|ConvertTo-Json -Depth 40)}else{$null};$pending.commitPrepared=$true;Write-SetupJournal $pendingPath $pending
        Assert-SetupConfigHash $oldConfigHash;Assert-GlobalRawState $pending.expectedState
        if($null-ne $candidateBytes){Write-SetupAtomic $configPath $candidateBytes}elseif(Test-Path -LiteralPath $configPath){Remove-Item -LiteralPath $configPath -Force}
        if($journalOut){if($Action -in @('Install','Update')){$journalOut.committedConfigHash=$pending.expectedConfigHash};Write-SetupJournal $journalPath $journalOut}elseif(Test-Path -LiteralPath $journalPath){Remove-Item -LiteralPath $journalPath -Force}
        $committed=$true;Remove-Item -LiteralPath $pendingPath -Force
        [ordered]@{action=$Action;target='Global';result='Applied';packageAction=$packageAction;packageChanged=$packageOwned;version=$effectiveVersion;packageTarget=$globalToolsRoot;config=$configPath;transaction=$transactionId}|ConvertTo-Json -Depth 20
        exit 0
    } catch {
        if($committed){throw 'Global transaction committed; pending cleanup failed. Rerun to finalize without rollback.'}
        if(!$pending.expectedState){$pending.expectedState=Get-GlobalRawState $globalToolsRoot $commands;Write-SetupJournal $pendingPath $pending}
        Restore-GlobalPending $pending
        throw
    }
}
if ($conflict) { throw $conflict }
if ($status) { [ordered]@{action=$Action;result='Unchanged';status=$status}|ConvertTo-Json;exit 0 }
if ($Action -in @('Install','Update')) {
    if (!$clientCommand) { throw 'VS Code command-line client was not found; no files were changed.' }
    if (!$sdkAvailable) { throw 'A .NET SDK is required; no files were changed.' }
    if ($Feed -and !$feedPackage) { throw "Exact local package navlyn-mcp.$Version is missing; no files were changed." }
}
if ($Action -in @('Remove','Undo')) {
    if (!(Test-Path -LiteralPath $journal.config -PathType Leaf)) { if ($journal.configPresent) { throw 'Owned config file is missing.' } }
    $stageList=Get-SetupJournalStages $journal
    foreach ($stage in $stageList) { Assert-SetupOwnedStage -Path $stage.path -ToolsRoot $toolsRoot -StageRecord $stage }
}
$oldConfigHash=if($null -ne $oldBytes){Get-SetupHashBytes $oldBytes}else{$null}
if ($Action -in @('Undo','Remove') -and (Test-Path -LiteralPath $configPath) -and (Get-SetupHashFile $configPath) -ceq $journal.committedConfigHash -and ($Action -eq 'Undo' -or !$journal.priorEntryRaw)) {
    if($journal.priorConfigPresent){$restoreBytes=[Convert]::FromBase64String($journal.priorConfigBase64);Write-SetupAtomic $configPath $restoreBytes}
    else{Remove-Item -LiteralPath $configPath -Force}
} else {
    if ($Action -eq 'Undo') { $candidateText=if($journal.priorEntryRaw){Set-NavlynJsoncServer $oldText.Text 'navlyn' ([string]$journal.priorEntryRaw)}else{Remove-NavlynJsoncServer $oldText.Text 'navlyn'} }
    elseif ($Action -eq 'Remove') { $candidateText=Remove-NavlynJsoncServer $oldText.Text 'navlyn' }
    if ($oldConfigHash -ne $null -and (Get-SetupHashFile $configPath) -cne $oldConfigHash) { throw 'Configuration changed concurrently; refusing to commit.' }
    if ($null -eq $oldConfigHash -and (Test-Path -LiteralPath $configPath)) { throw 'Configuration appeared concurrently; refusing to commit.' }
    if ($Action -in @('Install','Update')) {
        [IO.Directory]::CreateDirectory($toolsRoot) | Out-Null
        $installId=[guid]::NewGuid().ToString('N');$stagePath=Join-Path $toolsRoot ("$Version-$installId");[IO.Directory]::CreateDirectory($stagePath)|Out-Null
        $marker=[ordered]@{installId=$installId;workspaceKey=$workspaceKey};Write-SetupAtomicText (Join-Path $stagePath '.navlyn-setup-owner.json') ($marker|ConvertTo-Json -Compress) $false
        $transactionCommitted=$false
        try {
            $configFile=Join-Path $stagePath 'NuGet.Config'
            $sourceXml=if($Feed){'<add key="candidate" value="'+[Security.SecurityElement]::Escape($feedPath)+'" />'}else{'<add key="nuget" value="https://api.nuget.org/v3/index.json" />'}
            $cachePathXml=[Security.SecurityElement]::Escape((Join-Path $stagePath '.nuget'))
            Write-SetupAtomicText $configFile "<?xml version=`"1.0`" encoding=`"utf-8`"?><configuration><packageSources><clear/>$sourceXml</packageSources><config><add key=`"globalPackagesFolder`" value=`"$cachePathXml`"/></config></configuration>" $false
            $arguments=@('tool','install','navlyn-mcp','--tool-path',$stagePath,'--version',$Version,'--framework','net8.0','--configfile',$configFile,'--verbosity','quiet')
            $oldCliHome=$env:DOTNET_CLI_HOME;$oldPackages=$env:NUGET_PACKAGES;$oldFirst=$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE;$oldTelemetry=$env:DOTNET_CLI_TELEMETRY_OPTOUT;$oldLocal=$env:LOCALAPPDATA;$oldRoam=$env:APPDATA;$oldNoLogo=$env:DOTNET_NOLOGO
            $env:DOTNET_CLI_HOME=Join-Path $stagePath '.dotnet-home';$env:NUGET_PACKAGES=Join-Path $stagePath '.nuget';$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1';$env:DOTNET_CLI_TELEMETRY_OPTOUT='1';$env:LOCALAPPDATA=Join-Path $stagePath '.localappdata';$env:APPDATA=Join-Path $stagePath '.appdata';$env:DOTNET_NOLOGO='1'
            try { $installOutput=& $dotnet.Source @arguments 2>&1;$installExit=$LASTEXITCODE } finally { $env:DOTNET_CLI_HOME=$oldCliHome;$env:NUGET_PACKAGES=$oldPackages;$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE=$oldFirst;$env:DOTNET_CLI_TELEMETRY_OPTOUT=$oldTelemetry;$env:LOCALAPPDATA=$oldLocal;$env:APPDATA=$oldRoam;$env:DOTNET_NOLOGO=$oldNoLogo }
            if($installExit -ne 0){throw "dotnet tool install failed with exit code $installExit. $($installOutput -join ' ')"}
            $executable=Join-Path $stagePath $(if($IsWindows){'navlyn-mcp.exe'}else{'navlyn-mcp'})
            if(!(Test-Path -LiteralPath $executable -PathType Leaf)){throw 'Installed MCP executable is missing.'}
            $protocol=Test-SetupMcp $executable $selected $workspaceArgument $Version
            Remove-Item -LiteralPath $configFile -Force -ErrorAction SilentlyContinue
            foreach($cachePath in @((Join-Path $stagePath '.nuget'),(Join-Path $stagePath '.dotnet-home'),(Join-Path $stagePath '.localappdata'),(Join-Path $stagePath '.appdata'))){if(Test-Path -LiteralPath $cachePath){Remove-Item -LiteralPath $cachePath -Recurse -Force}}
            $files=[ordered]@{}
            Get-ChildItem -LiteralPath $stagePath -Recurse -File -Force | ForEach-Object { Assert-SetupNoReparsePath $_.FullName; $rel=$_.FullName.Substring($stagePath.Length).TrimStart('\','/').Replace('\','/'); if($rel -ne '.navlyn-setup-owner.json'){$files[$rel]=Get-SetupHashFile $_.FullName} }
            $stageRecord=[ordered]@{installId=$installId;workspaceKey=$workspaceKey;path=$stagePath;version=$Version;files=$files}
            $entry=[ordered]@{type='stdio';command=$executable;args=@('--workspace',$workspaceArgument,'--working-directory',$selected,'--timeout-ms','60000');cwd='${workspaceFolder}'}
            $candidateText=Set-NavlynJsoncServer -Text $oldText.Text -Name navlyn -EntryJson ($entry|ConvertTo-Json -Depth 10 -Compress)
            $pending=[ordered]@{schema='navlyn.setup.pending.v1';workspace=$selected;config=$configPath;priorConfigPresent=($null-ne $oldBytes);priorConfigBase64=if($oldBytes){[Convert]::ToBase64String($oldBytes)}else{$null};priorConfigHash=$oldConfigHash;expectedConfigHash=(Get-SetupHashBytes (Get-SetupUtf8Bytes $candidateText $oldText.Bom));stage=$stageRecord}
            Write-SetupJournal $pendingPath $pending
            if($oldConfigHash-ne $null -and (Get-SetupHashFile $configPath)-cne $oldConfigHash){throw 'Configuration changed during package validation; refusing to overwrite.'}
            if($null-eq $oldConfigHash -and (Test-Path -LiteralPath $configPath)){throw 'Configuration appeared during package validation; refusing to overwrite.'}
            [IO.Directory]::CreateDirectory((Split-Path -Parent $configPath))|Out-Null;Write-SetupAtomicText $configPath $candidateText $oldText.Bom
            $journalOut=[ordered]@{schema='navlyn.setup.ownership.v2';workspace=$selected;workspaceFile=$selectedFile;config=$configPath;configPresent=$true;entryHash=Get-SetupHashText (Get-SetupRawEntry $candidateText);priorEntryRaw=$rawCurrent;priorConfigPresent=($null-ne $oldBytes);priorConfigBase64=if($oldBytes){[Convert]::ToBase64String($oldBytes)}else{$null};committedConfigHash=Get-SetupHashFile $configPath;installedVersion=$Version;stages=@($stageRecord);priorJournal=if($journal){$journal}else{$null};protocol=$protocol}
            Write-SetupJournal $journalPath $journalOut
            $transactionCommitted=$true
            Remove-Item -LiteralPath $pendingPath -Force
        } catch {
            if($transactionCommitted){Write-Output ([ordered]@{action=$Action;result='Applied';workspace=$selected;version=$Version;config=$configPath}|ConvertTo-Json -Depth 20);return}
            if(Test-Path -LiteralPath $pendingPath) {
                try{$pendingDoc=Get-Content -Raw -LiteralPath $pendingPath|ConvertFrom-Json -AsHashtable}catch{throw 'Install failed and its pending transaction is malformed; manual recovery required.'}
                if($pendingDoc.schema -cne 'navlyn.setup.pending.v1' -or $pendingDoc.workspace -cne $selected -or $pendingDoc.config -cne $configPath -or !$pendingDoc.stage -or $pendingDoc.stage.installId -cne $installId){throw 'Install failed and pending transaction ownership is invalid; manual recovery required.'}
                if($pendingDoc.priorConfigPresent){try{$priorRecoveryBytes=[Convert]::FromBase64String([string]$pendingDoc.priorConfigBase64)}catch{throw 'Install failed and pending prior-config backup is invalid; manual recovery required.'};if((Get-SetupHashBytes $priorRecoveryBytes) -cne $pendingDoc.priorConfigHash){throw 'Install failed and pending prior-config hash mismatch; manual recovery required.'}}
                elseif($null -ne $pendingDoc.priorConfigHash -or $null -ne $pendingDoc.priorConfigBase64){throw 'Install failed and pending absent-config record is inconsistent; manual recovery required.'}
                Assert-SetupOwnedStage -Path $pendingDoc.stage.path -ToolsRoot $toolsRoot -StageRecord $pendingDoc.stage
                $nowConfigHash=if(Test-Path -LiteralPath $configPath){Get-SetupHashFile $configPath}else{$null}
                if($nowConfigHash -ceq $pendingDoc.expectedConfigHash){if($pendingDoc.priorConfigPresent){Write-SetupAtomic $configPath ([Convert]::FromBase64String($pendingDoc.priorConfigBase64))}else{Remove-Item -LiteralPath $configPath -Force}}
                elseif($nowConfigHash -cne $pendingDoc.priorConfigHash){throw 'Install failed after concurrent config edits; manual recovery required and user changes were preserved.'}
            }
            if(Test-Path -LiteralPath $stagePath){Remove-SetupNewStage -Path $stagePath -ToolsRoot $toolsRoot -InstallId $installId -WorkspaceKey $workspaceKey}
            if(Test-Path -LiteralPath $pendingPath){Remove-Item -LiteralPath $pendingPath -Force}
            throw
        }
    } elseif ($Action -in @('Remove','Undo')) {
        if(Test-Path -LiteralPath $configPath){Write-SetupAtomicText $configPath $candidateText $oldText.Bom}
    }
}
if($Action -in @('Remove','Undo')) {
    $stagesToDelete=if($Action -eq 'Remove'){Get-SetupJournalStages $journal}else{@($journal.stages)}
    foreach($stage in $stagesToDelete){Assert-SetupOwnedStage -Path $stage.path -ToolsRoot $toolsRoot -StageRecord $stage;Remove-Item -LiteralPath $stage.path -Recurse -Force}
    if($Action -eq 'Undo' -and $journal.priorJournal){Write-SetupJournal $journalPath $journal.priorJournal}else{Remove-Item -LiteralPath $journalPath -Force -ErrorAction SilentlyContinue}
}
Write-Output ([ordered]@{action=$Action;result='Applied';workspace=$selected;version=$Version;config=$configPath}|ConvertTo-Json -Depth 20)

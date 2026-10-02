[CmdletBinding()]
param([string]$OfflineFeed,[string]$CandidateFeed,[string]$FixtureRoot,[switch]$GlobalLifecycle,[switch]$GlobalRecoveryOnly)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if($GlobalRecoveryOnly -and !$GlobalLifecycle){throw 'GlobalRecoveryOnly requires GlobalLifecycle.'}
if($GlobalLifecycle -and (!$OfflineFeed -or !$CandidateFeed)){throw 'Actual global lifecycle requires exact baseline and candidate offline feeds.'}
$repo = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'lib/navlyn-jsonc.ps1')
$failures = [Collections.Generic.List[string]]::new()
$savedUserProfile=$env:USERPROFILE
$savedDotnetCliHome=$env:DOTNET_CLI_HOME
$dotnetCommand=Get-Command dotnet -ErrorAction SilentlyContinue
function Assert([bool]$Condition, [string]$Message) { if (!$Condition) { $failures.Add($Message) } }
function Get-SetupTestText([byte[]]$Bytes) {
    $bom=$Bytes.Length -ge 3 -and $Bytes[0]-eq 239 -and $Bytes[1]-eq 187 -and $Bytes[2]-eq 191
    $offset=if($bom){3}else{0};return [pscustomobject]@{Text=[Text.UTF8Encoding]::new($false,$true).GetString($Bytes,$offset,$Bytes.Length-$offset);Bom=$bom}
}
function Write-SetupTestBytes([string]$Path,[byte[]]$Bytes) { [IO.Directory]::CreateDirectory((Split-Path -Parent $Path))|Out-Null;[IO.File]::WriteAllBytes($Path,$Bytes) }

$source = @'
{
  // keep this comment exactly
  "servers": {
    "other": { "type": "stdio", "args": ["a,b"] }, // keep inline comment
  },
  "inputs": [],
}
'@
$before = $source
$edited = Set-NavlynJsoncServer -Text $source -Name navlyn -EntryJson '{"type":"stdio","command":"C:\\A B\\navlyn-mcp.exe","cwd":"${workspaceFolder}"}'
$parsed = ConvertFrom-NavlynJsonc $edited
Assert ($parsed.servers.navlyn.command -eq 'C:\A B\navlyn-mcp.exe') 'Set operation must preserve escaped executable path.'
Assert ($edited.Contains('// keep this comment exactly')) 'Leading comment was lost.'
Assert ($edited.Contains('// keep inline comment')) 'Inline comment was lost.'
Assert ($edited.Contains('"inputs": []')) 'Unrelated JSONC data was lost.'
$crlfEdited = Set-NavlynJsoncServer -Text ($source.Replace("`n", "`r`n")) -Name navlyn -EntryJson '{"type":"stdio","command":"navlyn-mcp"}'
Assert ($crlfEdited.Contains("`r`n    `"navlyn`": ")) 'Inserted JSONC fields must use the existing CRLF convention.'
$removed = Remove-NavlynJsoncServer -Text $edited -Name navlyn
Assert ((ConvertFrom-NavlynJsonc $removed).servers.other.type -eq 'stdio') 'Remove changed another server.'
Assert ($removed.Contains('// keep inline comment')) 'Remove changed neighboring comment.'
Assert ((Get-NavlynJsoncPropertySpan -Text $edited -ObjectName servers -PropertyName other).Length -gt 0) 'Expected direct entry span.'

$tricky = @'
{
  "description": "comma before brace, } // text /* text */",
  "nested": { "servers": { "navlyn": { "keep": true } } },
  "serv\u0065rs": { "other": {}, "navl\u0079n": { "old": true } },
  "unrelated": ["x,}", {"text":"/* not a comment */"}],
}
'@
$trickyEdited = Set-NavlynJsoncServer -Text $tricky -Name navlyn -EntryJson '{"command":"replacement"}'
$trickyDoc = ConvertFrom-NavlynJsonc $trickyEdited
Assert ($trickyDoc.servers.navlyn.command -eq 'replacement') 'Escaped root property names must resolve to their decoded names.'
Assert ($trickyDoc.nested.servers.navlyn.keep -eq $true) 'Nested servers/navlyn properties must remain untouched.'
Assert ($trickyDoc.description -eq 'comma before brace, } // text /* text */') 'String text that resembles JSONC syntax was changed.'
Assert ($trickyEdited.Contains('"unrelated": ["x,}", {"text":"/* not a comment */"}]')) 'Unrelated string and object bytes changed.'

$rootWithoutServers = @'
{
  "name": "demo",
  "note": "keep, }"
}
'@
$rootAdded = Set-NavlynJsoncServer -Text $rootWithoutServers -Name navlyn -EntryJson '{"command":"tool"}'
Assert ((ConvertFrom-NavlynJsonc $rootAdded).servers.navlyn.command -eq 'tool') 'Missing root servers object was not inserted.'
Assert ($rootAdded.Contains('"note": "keep, }"')) 'Root insertion altered unrelated value bytes.'
$emptyRootAdded = Set-NavlynJsoncServer -Text '{}' -Name navlyn -EntryJson '{"command":"tool"}'
Assert ((ConvertFrom-NavlynJsonc $emptyRootAdded).servers.navlyn.command -eq 'tool') 'Empty root insertion failed.'
$emptyServersAdded = Set-NavlynJsoncServer -Text '{"servers": {}}' -Name navlyn -EntryJson '{"command":"tool"}'
Assert ((ConvertFrom-NavlynJsonc $emptyServersAdded).servers.navlyn.command -eq 'tool') 'Empty servers insertion failed.'
$caseDistinctAdded = Set-NavlynJsoncServer -Text '{"Servers":{"keep":1}}' -Name navlyn -EntryJson '{"command":"tool"}'
$caseDistinctDoc = ConvertFrom-NavlynJsonc $caseDistinctAdded
Assert ($caseDistinctDoc['Servers'].keep -eq 1 -and $caseDistinctDoc['servers'].navlyn.command -eq 'tool') 'Property lookup must respect JSON case sensitivity.'

foreach ($invalid in @('{"servers":{"navlyn":{},"navlyn":{}}}', '{"servers":{"navlyn":{},"navl\u0079n":{}}}', '{"broken":"unterminated}', '{"servers":{/* no end')) {
    $threw = $false
    try { [void](ConvertFrom-NavlynJsonc $invalid) } catch { $threw = $true }
    Assert $threw "Malformed or duplicate JSONC was accepted: $invalid"
}

$removalCases = @(
    [pscustomobject]@{ Name='leading'; Text='{"servers":{"navlyn":{}, /*keep-leading-comment*/ "other":{"v":1}}}'; Expected='{"servers":{ /*keep-leading-comment*/ "other":{"v":1}}}' },
    [pscustomobject]@{ Name='middle'; Text='{"servers":{"a":1, /*keep-middle-comment*/ "navlyn":{}, "z":2}}'; Expected='{"servers":{"a":1, /*keep-middle-comment*/  "z":2}}' },
    [pscustomobject]@{ Name='last'; Text='{"servers":{"other":1, /*keep-last-comment*/ "navlyn":{}}}'; Expected='{"servers":{"other":1 /*keep-last-comment*/ }}' },
    [pscustomobject]@{ Name='single-trailing'; Text='{"servers":{"navlyn":{}, /*keep-single-comment*/ }}'; Expected='{"servers":{ /*keep-single-comment*/ }}' }
)
foreach ($case in $removalCases) {
    $actual = Remove-NavlynJsoncServer -Text $case.Text -Name navlyn
    Assert ($actual -ceq $case.Expected) "Removing $($case.Name) entry changed unrelated bytes/comments. Actual: $actual"
    [void](ConvertFrom-NavlynJsonc $actual)
}
$noServers = '{"nested":{"navlyn":{}}}'
Assert ((Remove-NavlynJsoncServer -Text $noServers -Name navlyn) -ceq $noServers) 'Remove without root servers changed the input.'

$fixtureBase=if($FixtureRoot){[IO.Path]::GetFullPath($FixtureRoot)}else{[IO.Path]::GetFullPath([IO.Path]::GetTempPath())}
if(!$FixtureRoot -and !$IsWindows){
    # macOS exposes its temporary directory through /var, a system symlink.
    $canonicalBase=& realpath $fixtureBase
    if($LASTEXITCODE -ne 0 -or !$canonicalBase){throw 'Could not resolve the test fixture root.'}
    $fixtureBase=[IO.Path]::GetFullPath($canonicalBase)
}
if($GlobalLifecycle){
    if(!$FixtureRoot -or !$env:NAVLYN_SETUP_TEST_ROOT -or $fixtureBase -cne [IO.Path]::GetFullPath($env:NAVLYN_SETUP_TEST_ROOT) -or !(Test-Path -LiteralPath (Join-Path $fixtureBase '.navlyn-owned-test-fixture') -PathType Leaf)){throw 'Global lifecycle requires the reviewed absolute owned fixture environment.'}
    foreach($name in @('USERPROFILE','DOTNET_CLI_HOME','NUGET_PACKAGES','LOCALAPPDATA','APPDATA')){$value=[Environment]::GetEnvironmentVariable($name);if(![IO.Path]::IsPathFullyQualified($value) -or ![IO.Path]::GetFullPath($value).StartsWith($fixtureBase.TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw "Global test environment escaped owned root: $name"}}
}
$fixture = Join-Path $fixtureBase "navlyn-setup-plan-$([guid]::NewGuid().ToString('N'))"
[IO.Directory]::CreateDirectory($fixture) | Out-Null
Set-Content -LiteralPath (Join-Path $fixture '.navlyn-owned-test-fixture') -Value 'owned' -NoNewline
try {
    $snapshot = (Get-ChildItem -LiteralPath $fixture -Force | Measure-Object).Count
    $output = & (Join-Path $PSScriptRoot 'setup-navlyn.ps1') -Workspace $fixture -Version 0.8.2 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { $failures.Add("Plan invocation failed: $output") }
    Assert ((Get-ChildItem -LiteralPath $fixture -Force | Measure-Object).Count -eq $snapshot) 'Plan wrote into the selected workspace.'
    Assert (!(Test-Path -LiteralPath (Join-Path $fixture '.vscode'))) 'Plan created VS Code config directory.'
    $outputJson = $output | ConvertFrom-Json
    Assert ($outputJson.mode -eq 'Plan') 'Default invocation must be Plan.'
    Assert ($outputJson.effects.writes -eq $false -and $outputJson.effects.network -eq $false -and $outputJson.effects.clientLaunch -eq $false) 'Plan must report no effects.'
    Assert ($outputJson.target -eq 'Local') 'The default target must remain Local.'
    $fakeUserProfile=Join-Path $fixture 'synthetic-user';$fakeCliHome=Join-Path $fixture 'synthetic-cli-home';$fakeGlobalRoot=Join-Path $fakeCliHome '.dotnet/tools';$fakeStore=Join-Path $fakeGlobalRoot '.store/navlyn-mcp/0.8.1/navlyn-mcp/0.8.1'
    [IO.Directory]::CreateDirectory((Join-Path $fakeStore 'tools/net8.0/any'))|Out-Null
    [IO.File]::WriteAllText((Join-Path $fakeStore 'navlyn-mcp.nuspec'),'<package xmlns="http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd"><metadata><id>navlyn-mcp</id><version>0.8.1</version></metadata></package>')
    [IO.File]::WriteAllText((Join-Path $fakeStore 'tools/net8.0/any/DotnetToolSettings.xml'),'<DotNetCliTool Version="1"><Commands><Command Name="navlyn-mcp" EntryPoint="navlyn.Mcp.dll" Runner="dotnet"/><Command Name="navlyn" EntryPoint="navlyn.Mcp.dll" Runner="dotnet"/></Commands></DotNetCliTool>')
    $fakeMcpShim=Join-Path $fakeGlobalRoot $(if($IsWindows){'navlyn-mcp.exe'}else{'navlyn-mcp'})
    $fakeCliShim=Join-Path $fakeGlobalRoot $(if($IsWindows){'navlyn.exe'}else{'navlyn'})
    [IO.File]::WriteAllBytes($fakeMcpShim,[byte[]](1,2,3,4));[IO.File]::WriteAllBytes($fakeCliShim,[byte[]](5,6,7,8));$env:USERPROFILE=$fakeUserProfile;$env:DOTNET_CLI_HOME=$fakeCliHome
    $globalPlanOutput = & (Join-Path $PSScriptRoot 'setup-navlyn.ps1') -Workspace $fixture -Target Global -Version 0.8.2 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { $failures.Add("Global Plan invocation failed: $globalPlanOutput") }
    $globalPlan = $globalPlanOutput | ConvertFrom-Json
    Assert ($globalPlan.target -eq 'Global' -and $globalPlan.packageTarget -eq $fakeGlobalRoot -and $globalPlan.config -like '*\.vscode\mcp.json') 'Global Plan must identify the DOTNET_CLI_HOME global package target and workspace config separately.'
    Assert ($globalPlan.globalPackageVersion -eq '0.8.1' -and $globalPlan.globalShim -eq $fakeMcpShim) 'Global Plan must resolve version from package nuspec and exact shim metadata.'
    Assert ($globalPlan.effects.writes -eq $false -and $globalPlan.effects.network -eq $false -and $globalPlan.effects.installation -eq $false -and $globalPlan.effects.clientLaunch -eq $false) 'Global Plan must report no effects.'
    $globalApplyRejected = $false
    $shimHashBefore=(Get-FileHash -LiteralPath $fakeMcpShim).Hash
    try { & (Join-Path $PSScriptRoot 'setup-navlyn.ps1') -Workspace $fixture -Target Global -Version 0.8.2 -Feed $fixture -Apply 2>&1 | Out-Null } catch { $globalApplyRejected = $_.Exception.Message -like '*Exact local package*missing*' }
    Assert $globalApplyRejected 'Global Apply must fail before SDK/package mutation if the exact feed input is missing.'
    Assert (!(Test-Path -LiteralPath (Join-Path $fixture '.vscode'))) 'Rejected Global Apply changed workspace config.'
    Assert ((Get-FileHash -LiteralPath $fakeMcpShim).Hash -ceq $shimHashBefore) 'Global Plan or rejected Apply changed the shim.'

    if($OfflineFeed){
        if(!(Get-Command dotnet -ErrorAction SilentlyContinue) -or !(Get-Command code -ErrorAction SilentlyContinue)){throw 'Offline package lifecycle test requires both dotnet and the VS Code CLI.'}
        $package=Get-ChildItem -LiteralPath $OfflineFeed -Filter 'navlyn-mcp.0.8.1.nupkg' -File -ErrorAction Stop|Select-Object -First 1
        if(!$package){throw 'Offline feed is missing navlyn-mcp.0.8.1.nupkg.'}
        $appData=Join-Path $fixtureBase 'a';[IO.Directory]::CreateDirectory($appData)|Out-Null
        $workspace=Join-Path $fixture 'package-workspace';[IO.Directory]::CreateDirectory($workspace)|Out-Null
        $project=Join-Path $workspace 'Probe.csproj';$sourceFile=Join-Path $workspace 'Probe.cs'
        Set-Content -LiteralPath $project -Value '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable></PropertyGroup></Project>' -Encoding utf8
        Set-Content -LiteralPath $sourceFile -Value "namespace Probe;`npublic class Sample {`n public int Value() => 1;`n}" -Encoding utf8
        $config=Join-Path $workspace '.vscode/mcp.json';$initial="{`r`n  /* preserved */`r`n  `"servers`": {`r`n    `"other`": { `"type`": `"stdio`" }`r`n  }`r`n}`r`n"
        $initialEncoding=[Text.UTF8Encoding]::new($false);$initialBody=$initialEncoding.GetBytes($initial);$initialBytes=[byte[]]::new($initialBody.Length+3);$initialBytes[0]=239;$initialBytes[1]=187;$initialBytes[2]=191;[Array]::Copy($initialBody,0,$initialBytes,3,$initialBody.Length);Write-SetupTestBytes $config $initialBytes
        $oldLocal=$env:LOCALAPPDATA;$oldXdg=$env:XDG_DATA_HOME
        try {
            if($IsWindows){$env:LOCALAPPDATA=$appData}else{$env:XDG_DATA_HOME=$appData}
            $feed=Join-Path $fixture 'offline-feed';[IO.Directory]::CreateDirectory($feed)|Out-Null;Copy-Item -LiteralPath $package.FullName -Destination (Join-Path $feed $package.Name)
            if(!$GlobalRecoveryOnly){
            $common=@{Workspace=$workspace;WorkspaceFile=$project;Version='0.8.1';Feed=$feed;Apply=$true}
            & (Join-Path $PSScriptRoot 'setup-navlyn.ps1') @common | Out-Null
            $installedBytes=[IO.File]::ReadAllBytes($config);Assert ($installedBytes[0] -eq 239 -and $installedBytes[1] -eq 187 -and $installedBytes[2] -eq 191) 'Install lost the original UTF-8 BOM.'
            $installed=Get-SetupTestText $installedBytes;Assert ($installed.Text.Contains('/* preserved */') -and $installed.Text.Contains('"other"')) 'Install changed unrelated JSONC bytes.'
            $journalFile=Get-ChildItem -LiteralPath $appData -Filter ownership.json -Recurse -File|Select-Object -First 1
            $journal=Get-Content -Raw -LiteralPath $journalFile.FullName|ConvertFrom-Json -AsHashtable
            $pendingFile=Join-Path (Split-Path -Parent $journalFile.FullName) 'pending.json';$committedHash=(Get-FileHash -LiteralPath $config -Algorithm SHA256).Hash.ToLowerInvariant()
            $badPending=[ordered]@{schema='navlyn.setup.pending.v1';workspace=$workspace;config=$config;priorConfigHash='none';expectedConfigHash=$committedHash;priorConfigPresent=$false;stage=[ordered]@{path=(Join-Path $fixture 'outside-stage');installId='tampered';workspaceKey='wrong';files=[ordered]@{}}}
            [IO.File]::WriteAllText($pendingFile,($badPending|ConvertTo-Json -Depth 10),[Text.UTF8Encoding]::new($false));$pendingRejected=$false
            try{& (Join-Path $PSScriptRoot 'setup-navlyn.ps1') @common|Out-Null}catch{$pendingRejected=$true}
            Assert $pendingRejected 'Tampered pending transaction was accepted.'
            Assert ((Get-FileHash -LiteralPath $config -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $committedHash) 'Tampered pending transaction changed config before validation.'
            Remove-Item -LiteralPath $pendingFile -Force
            $stageCount=(Get-ChildItem -LiteralPath $appData -Filter '.navlyn-setup-owner.json' -Recurse -File|Measure-Object).Count
            Assert ($stageCount -eq 1) 'Install must create exactly one marked stage.'
            $second=& (Join-Path $PSScriptRoot 'setup-navlyn.ps1') @common | Out-String|ConvertFrom-Json
            Assert ($second.result -eq 'Unchanged') 'Exact version install must be idempotent.'
            Assert ((Get-ChildItem -LiteralPath $appData -Filter '.navlyn-setup-owner.json' -Recurse -File|Measure-Object).Count -eq 1) 'Idempotent install created another stage.'
            $beforeNewerConfig=Get-SetupTestText ([IO.File]::ReadAllBytes($config));$beforeNewerHash=(Get-FileHash -LiteralPath $config -Algorithm SHA256).Hash
            $newer=& (Join-Path $PSScriptRoot 'setup-navlyn.ps1') -Workspace $workspace -WorkspaceFile $project -Version 0.8.0 -Feed $feed -Apply | Out-String|ConvertFrom-Json
            Assert ($newer.result -eq 'Unchanged') 'Apply without downgrade consent must keep an already-installed newer version.'
            Assert ((Get-FileHash -LiteralPath $config -Algorithm SHA256).Hash -ceq $beforeNewerHash) 'Newer-version refusal changed config bytes.'
            Assert ((Get-ChildItem -LiteralPath $appData -Filter '.navlyn-setup-owner.json' -Recurse -File|Measure-Object).Count -eq 1) 'Newer-version refusal changed the installed stage.'
            $current=Get-SetupTestText ([IO.File]::ReadAllBytes($config))
            $changed=Set-NavlynJsoncServer -Text $current.Text -Name userOwned -EntryJson '{"type":"stdio","command":"keep"}'
            Write-SetupTestBytes $config ([Text.UTF8Encoding]::new($current.Bom).GetBytes($changed))
            & (Join-Path $PSScriptRoot 'setup-navlyn.ps1') -Workspace $workspace -Action Undo -Apply | Out-Null
            $undone=ConvertFrom-NavlynJsonc (Get-SetupTestText ([IO.File]::ReadAllBytes($config))).Text
            Assert ($undone['servers'].Contains('other') -and $undone['servers'].Contains('userOwned') -and !$undone['servers'].Contains('navlyn')) 'Undo did not preserve unrelated user edits.'
            Assert ((Get-ChildItem -LiteralPath $appData -Filter '.navlyn-setup-owner.json' -Recurse -File|Measure-Object).Count -eq 0) 'Undo left the owned stage installed.'

            $solution=Join-Path $workspace 'Probe.slnx'
            Set-Content -LiteralPath $solution -Value '<Solution><Project Path="Probe.csproj" /></Solution>' -Encoding utf8
            $solutionInstalled=& (Join-Path $PSScriptRoot 'setup-navlyn.ps1') -Workspace $workspace -WorkspaceFile $solution -Version 0.8.1 -Feed $feed -Apply|Out-String|ConvertFrom-Json
            Assert ($solutionInstalled.result -eq 'Applied') 'Solution workspace setup did not apply.'
            $solutionJournalFile=Get-ChildItem -LiteralPath $appData -Filter ownership.json -Recurse -File|Select-Object -First 1
            $solutionJournal=Get-Content -Raw -LiteralPath $solutionJournalFile.FullName|ConvertFrom-Json -AsHashtable
            Assert ($solutionJournal.protocol.readEvidence.views.Count -eq 3) 'Solution workspace setup did not validate all three semantic read views.'
            & (Join-Path $PSScriptRoot 'setup-navlyn.ps1') -Workspace $workspace -Action Undo -Apply|Out-Null
            $afterSolutionUndo=ConvertFrom-NavlynJsonc (Get-SetupTestText ([IO.File]::ReadAllBytes($config))).Text
            Assert ($afterSolutionUndo['servers'].Contains('other') -and $afterSolutionUndo['servers'].Contains('userOwned') -and !$afterSolutionUndo['servers'].Contains('navlyn')) 'Solution workspace Undo changed unrelated config entries.'

            $vbProject=Join-Path $workspace 'Probe.vbproj';$vbSource=Join-Path $workspace 'Probe.vb'
            Set-Content -LiteralPath $vbProject -Value '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>' -Encoding utf8
            Set-Content -LiteralPath $vbSource -Value "Public Class Sample`n Public Function Value() As Integer`n  Return 1`n End Function`nEnd Class" -Encoding utf8
            $vbInstalled=& (Join-Path $PSScriptRoot 'setup-navlyn.ps1') -Workspace $workspace -WorkspaceFile $vbProject -Version 0.8.1 -Feed $feed -Apply|Out-String|ConvertFrom-Json
            Assert ($vbInstalled.result -eq 'Applied') 'Visual Basic workspace setup did not apply.'
            $vbJournalFile=Get-ChildItem -LiteralPath $appData -Filter ownership.json -Recurse -File|Select-Object -First 1
            $vbJournal=Get-Content -Raw -LiteralPath $vbJournalFile.FullName|ConvertFrom-Json -AsHashtable
            Assert ($vbJournal.protocol.readEvidence.file -ceq $vbSource -and $vbJournal.protocol.readEvidence.views.Count -eq 3) 'Visual Basic setup did not validate all three semantic read views.'
            & (Join-Path $PSScriptRoot 'setup-navlyn.ps1') -Workspace $workspace -Action Undo -Apply|Out-Null

            $typeProject=Join-Path $workspace 'TypeOnly.csproj';$typeSource=Join-Path $workspace 'TypeOnly.cs'
            Set-Content -LiteralPath $typeProject -Value '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="TypeOnly.cs" /></ItemGroup></Project>' -Encoding utf8
            Set-Content -LiteralPath $typeSource -Value 'public class OnlyType { }' -Encoding utf8
            $typeInstalled=& (Join-Path $PSScriptRoot 'setup-navlyn.ps1') -Workspace $workspace -WorkspaceFile $typeProject -Version 0.8.1 -Feed $feed -Apply|Out-String|ConvertFrom-Json
            Assert ($typeInstalled.result -eq 'Applied') 'Type-only workspace setup did not apply.'
            $typeJournalFile=Get-ChildItem -LiteralPath $appData -Filter ownership.json -Recurse -File|Select-Object -First 1
            $typeJournal=Get-Content -Raw -LiteralPath $typeJournalFile.FullName|ConvertFrom-Json -AsHashtable
            Assert ($typeJournal.protocol.readEvidence.file -ceq $typeSource -and $typeJournal.protocol.readEvidence.views.Count -eq 1 -and $typeJournal.protocol.readEvidence.views[0] -ceq 'declaration') 'Type-only setup did not validate a semantic declaration.'
            & (Join-Path $PSScriptRoot 'setup-navlyn.ps1') -Workspace $workspace -Action Undo -Apply|Out-Null

            }
            if($GlobalLifecycle){
                $savedGlobalEnvironment=@{};foreach($name in @('USERPROFILE','DOTNET_CLI_HOME','NUGET_PACKAGES','APPDATA')){$savedGlobalEnvironment[$name]=[Environment]::GetEnvironmentVariable($name)}
                try {
                    $actualProfile=Join-Path $fixture 'actual-global-profile';$actualCliHome=Join-Path $fixture 'actual-global-cli-home';$actualPackages=Join-Path $fixture 'actual-global-packages';$actualRoaming=Join-Path $fixture 'actual-global-roaming'
                    $env:USERPROFILE=$actualProfile;$env:DOTNET_CLI_HOME=$actualCliHome;$env:NUGET_PACKAGES=$actualPackages;$env:APPDATA=$actualRoaming
                    foreach($path in @($actualProfile,$actualCliHome,$actualPackages,$actualRoaming)){[IO.Directory]::CreateDirectory($path)|Out-Null}
                    $actualGlobalRoot=Join-Path $actualCliHome '.dotnet/tools';[IO.Directory]::CreateDirectory($actualGlobalRoot)|Out-Null
                    $foreignShim=Join-Path $actualGlobalRoot $(if($IsWindows){'navlyn.exe'}else{'navlyn'});[IO.File]::WriteAllBytes($foreignShim,[byte[]](9,8,7,6));$foreignHash=(Get-FileHash -LiteralPath $foreignShim).Hash
                    $candidatePackage=Join-Path ([IO.Path]::GetFullPath($CandidateFeed)) 'navlyn-mcp.0.8.2.nupkg'
                    if(!(Test-Path -LiteralPath $candidatePackage -PathType Leaf)){throw 'Exact early 0.8.2 package is required for global lifecycle verification.'}
                    Copy-Item -LiteralPath $candidatePackage -Destination (Join-Path $feed 'navlyn-mcp.0.8.2.nupkg')
                    function GlobalFiles {
                        $map=[ordered]@{};foreach($file in @(Get-ChildItem -LiteralPath $actualGlobalRoot -File -Recurse -Force)){$map[$file.FullName.Substring($actualGlobalRoot.Length).Replace('\','/')]=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash};return $map
                    }
                    function SameGlobalFiles($Expected,[string]$Message){$actual=GlobalFiles;Assert ($actual.Count-eq $Expected.Count) $Message;foreach($key in $Expected.Keys){Assert ($actual.Contains($key) -and $actual[$key]-ceq $Expected[$key]) "$Message $key"}}
                    $globalCommon=@{Workspace=$workspace;WorkspaceFile=$project;Target='Global';Version='0.8.1';Feed=$feed;Apply=$true}
                    $installedGlobal=& (Join-Path $PSScriptRoot 'setup-navlyn.ps1') @globalCommon|Out-String|ConvertFrom-Json
                    Assert ($installedGlobal.packageAction-eq 'Installed') 'Clean global package install did not execute.'
                    $globalShim=Join-Path $actualGlobalRoot $(if($IsWindows){'navlyn-mcp.exe'}else{'navlyn-mcp'})
                    $shimBytes=[IO.File]::ReadAllBytes($globalShim);Assert ($shimBytes.Length-gt 4 -and (!$IsWindows -or ($shimBytes[0]-eq 77 -and $shimBytes[1]-eq 90))) 'Real global shim is not a Windows executable; synthetic fixture collision.'
                    $initialGlobalFiles=GlobalFiles;$initialGlobalConfig=[IO.File]::ReadAllBytes($config)
                    $idempotent=& (Join-Path $PSScriptRoot 'setup-navlyn.ps1') @globalCommon|Out-String|ConvertFrom-Json
                    Assert ($idempotent.result-eq 'Unchanged') 'Same-version global install was not idempotent.'
                    SameGlobalFiles $initialGlobalFiles 'Same version changed global bytes.'
                    $updated=& (Join-Path $PSScriptRoot 'setup-navlyn.ps1') -Workspace $workspace -WorkspaceFile $project -Target Global -Action Update -Version 0.8.2 -Feed $feed -Apply|Out-String|ConvertFrom-Json
                    Assert ($updated.packageAction-eq 'Updated' -and $updated.version-eq '0.8.2') 'Actual global update did not reach the exact early candidate.'
                    $updatedFiles=GlobalFiles;$updatedConfigHash=(Get-FileHash -LiteralPath $config).Hash
                    # Recreate the interruption after journal commit but before pending cleanup.
                    # Hash the journal in the producer's ordered field sequence, not its parsed file order.
                    $committedJournalFile=Get-ChildItem -LiteralPath $appData -Filter ownership.json -Recurse -File|Where-Object { (Get-Content -Raw -LiteralPath $_.FullName|ConvertFrom-Json).workspace-ceq $workspace }|Select-Object -First 1
                    $committedJournal=Get-Content -Raw -LiteralPath $committedJournalFile.FullName|ConvertFrom-Json -AsHashtable
                    $orderedJournal=[ordered]@{}
                    foreach($key in @('schema','transactionId','workspace','workspaceFile','config','targetRoot','packageId','installedVersion','packageOwned','priorSnapshot','currentState','priorConfigPresent','priorConfigBase64','priorConfigHash','priorEntryRaw','entryHash','committedConfigHash','protocol','feed','feedPackageSha256','priorJournal')){$orderedJournal[$key]=$committedJournal[$key]}
                    $journalExpectedHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($orderedJournal|ConvertTo-Json -Depth 40)))).ToLowerInvariant()
                    $interruptedPending=[ordered]@{schema='navlyn.setup.global-pending.v1';transactionId=$committedJournal.transactionId;workspace=$workspace;config=$config;targetRoot=$actualGlobalRoot;priorSnapshot=$committedJournal.priorSnapshot;priorJournal=$committedJournal.priorJournal;priorConfigPresent=$committedJournal.priorConfigPresent;priorConfigBase64=$committedJournal.priorConfigBase64;priorConfigHash=$committedJournal.priorConfigHash;expectedConfigHash=$committedJournal.committedConfigHash;expectedState=$committedJournal.currentState;phase='committing';commitPrepared=$true;expectedJournalHash=$journalExpectedHash}
                    $interruptedPath=Join-Path (Split-Path -Parent $committedJournalFile.FullName) 'pending.json'
                    Write-SetupTestBytes $interruptedPath ([Text.Encoding]::UTF8.GetBytes(($interruptedPending|ConvertTo-Json -Depth 40)))
                    $committedJournalHash=(Get-FileHash -LiteralPath $committedJournalFile.FullName).Hash
                    & (Join-Path $PSScriptRoot 'setup-navlyn.ps1') @globalCommon|Out-Null
                    Assert (!(Test-Path -LiteralPath $interruptedPath)) 'Completed global commit did not finalize pending cleanup.'
                    SameGlobalFiles $updatedFiles 'Completed pending recovery incorrectly rolled back a committed update.'
                    Assert ((Get-FileHash -LiteralPath $committedJournalFile.FullName).Hash-ceq $committedJournalHash) 'Completed pending recovery rewrote the committed ownership journal.'
                    Assert ((Get-FileHash -LiteralPath $config).Hash-ceq $updatedConfigHash) 'Completed pending recovery changed committed config.'
                    # Synthetic feed: package identity 0.8.3 retains the 0.8.2 MCP binary.
                    # It must install successfully, then fail initialize-version verification and roll back.
                    $badFeed=Join-Path $fixture 'wrong-protocol-feed';[IO.Directory]::CreateDirectory($badFeed)|Out-Null
                    $badPackage=Join-Path $badFeed 'navlyn-mcp.0.8.3.nupkg';Copy-Item -LiteralPath $candidatePackage -Destination $badPackage
                    $badZip=[IO.Compression.ZipFile]::Open($badPackage,[IO.Compression.ZipArchiveMode]::Update)
                    try{
                        $badNuspec=$badZip.GetEntry('navlyn-mcp.nuspec');$reader=[IO.StreamReader]::new($badNuspec.Open());try{$badText=$reader.ReadToEnd()}finally{$reader.Dispose()}
                        if(!$badText.Contains('<version>0.8.2</version>')){throw 'Synthetic rollback package has unexpected source metadata.'}
                        $badNuspec.Delete();$replacement=$badZip.CreateEntry('navlyn-mcp.nuspec');$writer=[IO.StreamWriter]::new($replacement.Open(),[Text.UTF8Encoding]::new($false));try{$writer.Write($badText.Replace('<version>0.8.2</version>','<version>0.8.3</version>'))}finally{$writer.Dispose()}
                        $signature=$badZip.GetEntry('.signature.p7s');if($signature){$signature.Delete()}
                    }finally{$badZip.Dispose()}
                    $rollbackJournalHash=(Get-FileHash -LiteralPath $committedJournalFile.FullName).Hash
                    $protocolRefused=$false;try{& (Join-Path $PSScriptRoot 'setup-navlyn.ps1') -Workspace $workspace -WorkspaceFile $project -Target Global -Action Update -Version 0.8.3 -Feed $badFeed -Apply|Out-Null}catch{$protocolRefused=$_.Exception.Message-like '*MCP initialize metadata/version*'}
                    Assert $protocolRefused 'Synthetic package did not reach the intended wrong-server-version protocol failure.'
                    SameGlobalFiles $updatedFiles 'Failed global protocol update did not restore exact prior package/shim bytes.'
                    Assert ((Get-FileHash -LiteralPath $config).Hash-ceq $updatedConfigHash) 'Failed global protocol update changed config.'
                    Assert ((Get-FileHash -LiteralPath $committedJournalFile.FullName).Hash-ceq $rollbackJournalHash) 'Failed global protocol update changed the prior ownership journal.'
                    Assert (!(Test-Path -LiteralPath $interruptedPath)) 'Known failed global mutation left unresolved pending state.'
                    # Fail closed before mutation when any previously owned input drifts.
                    $driftShimBytes=[IO.File]::ReadAllBytes($globalShim);$changedShim=[byte[]]$driftShimBytes.Clone();$changedShim[$changedShim.Length-1]=$changedShim[$changedShim.Length-1]-bxor 1
                    [IO.File]::WriteAllBytes($globalShim,$changedShim);$driftFiles=GlobalFiles
                    $driftRefused=$false;try{& (Join-Path $PSScriptRoot 'setup-navlyn.ps1') @globalCommon|Out-Null}catch{$driftRefused=$_.Exception.Message-like '*changed*'}
                    Assert $driftRefused 'Global shim drift was not refused before mutation.'
                    SameGlobalFiles $driftFiles 'Refusing shim drift overwrote changed package bytes.'
                    Assert ((Get-FileHash -LiteralPath $config).Hash-ceq $updatedConfigHash) 'Refusing shim drift changed config.'
                    [IO.File]::WriteAllBytes($globalShim,$driftShimBytes)
                    $globalJournalFile=Get-ChildItem -LiteralPath $appData -Filter ownership.json -Recurse -File|Where-Object { (Get-Content -Raw -LiteralPath $_.FullName|ConvertFrom-Json).workspace-ceq $workspace }|Select-Object -First 1
                    $globalJournal=Get-Content -Raw -LiteralPath $globalJournalFile.FullName|ConvertFrom-Json
                    $snapshotManifest=Join-Path $globalJournal.priorSnapshot.root 'snapshot.json';$manifestBytes=[IO.File]::ReadAllBytes($snapshotManifest)
                    [IO.File]::WriteAllBytes($snapshotManifest,([byte[]]($manifestBytes+32)))
                    $snapshotRefused=$false;try{& (Join-Path $PSScriptRoot 'setup-navlyn.ps1') -Workspace $workspace -Target Global -Action Undo -Apply|Out-Null}catch{$snapshotRefused=$_.Exception.Message-like '*snapshot retained manifest bytes differ*'}
                    Assert $snapshotRefused 'Global Undo accepted a changed retained snapshot manifest.'
                    SameGlobalFiles $updatedFiles 'Refusing snapshot drift changed installed package bytes.'
                    Assert ((Get-FileHash -LiteralPath $config).Hash-ceq $updatedConfigHash) 'Refusing snapshot drift changed config.'
                    [IO.File]::WriteAllBytes($snapshotManifest,$manifestBytes)
                    $ownedConfigBytes=[IO.File]::ReadAllBytes($config);$ownedConfigText=Get-SetupTestText $ownedConfigBytes
                    $changedConfig=Set-NavlynJsoncServer -Text $ownedConfigText.Text -Name navlyn -EntryJson '{"command":"user-changed"}'
                    Write-SetupTestBytes $config ([Text.UTF8Encoding]::new($ownedConfigText.Bom).GetBytes($changedConfig));$changedConfigHash=(Get-FileHash -LiteralPath $config).Hash
                    $configRefused=$false;try{& (Join-Path $PSScriptRoot 'setup-navlyn.ps1') @globalCommon|Out-Null}catch{$configRefused=$_.Exception.Message-like '*owned*' -or $_.Exception.Message-like '*conflict*' -or $_.Exception.Message-like '*changed*'}
                    Assert $configRefused 'Global lifecycle accepted an edited owned entry.'
                    SameGlobalFiles $updatedFiles 'Refusing config drift changed package bytes.'
                    Assert ((Get-FileHash -LiteralPath $config).Hash-ceq $changedConfigHash) 'Refusing config drift overwrote the user edit.'
                    [IO.File]::WriteAllBytes($config,$ownedConfigBytes)
                    if($GlobalRecoveryOnly){
                        & (Join-Path $PSScriptRoot 'setup-navlyn.ps1') -Workspace $workspace -Target Global -Action Remove -Apply|Out-Null
                        Assert (!(Test-Path -LiteralPath (Join-Path $actualGlobalRoot '.store/navlyn-mcp')) -and !(Test-Path -LiteralPath $globalShim)) 'Focused recovery cleanup did not restore the initial absent package state.'
                        Assert ((Get-FileHash -LiteralPath $foreignShim).Hash-ceq $foreignHash) 'Focused recovery altered the unrelated CLI shim.'
                        Write-Output 'Global committed-pending/protocol-rollback/ownership-drift focused checks completed.'
                    }else{
                    $kept=& (Join-Path $PSScriptRoot 'setup-navlyn.ps1') @globalCommon|Out-String|ConvertFrom-Json
                    Assert ($kept.result-eq 'Unchanged' -and $kept.packageAction-eq 'KeepNewer' -and $kept.version-eq '0.8.2') 'Global downgrade without consent did not keep newer.'
                    SameGlobalFiles $updatedFiles 'Keep newer changed global bytes.'
                    Assert ((Get-FileHash -LiteralPath $config).Hash-ceq $updatedConfigHash) 'Keep newer changed config bytes.'
                    & (Join-Path $PSScriptRoot 'setup-navlyn.ps1') @globalCommon -AllowDowngrade|Out-Null
                    $downgraded=Get-ChildItem -LiteralPath (Join-Path $actualGlobalRoot '.store/navlyn-mcp') -Directory
                    Assert ($downgraded.Name-eq '0.8.1') 'Explicit global downgrade did not install exact 0.8.1.'
                    & (Join-Path $PSScriptRoot 'setup-navlyn.ps1') -Workspace $workspace -Target Global -Action Undo -Apply|Out-Null
                    SameGlobalFiles $updatedFiles 'Undo downgrade did not restore exact prior package/shim bytes.'
                    & (Join-Path $PSScriptRoot 'setup-navlyn.ps1') -Workspace $workspace -Target Global -Action Undo -Apply|Out-Null
                    SameGlobalFiles $initialGlobalFiles 'Second Undo did not restore exact original package/shim bytes.'
                    $otherWorkspace=Join-Path $fixture 'second global workspace';[IO.Directory]::CreateDirectory($otherWorkspace)|Out-Null
                    Copy-Item -LiteralPath $project -Destination (Join-Path $otherWorkspace 'Probe.csproj');Copy-Item -LiteralPath $sourceFile -Destination (Join-Path $otherWorkspace 'Probe.cs')
                    $other=& (Join-Path $PSScriptRoot 'setup-navlyn.ps1') -Workspace $otherWorkspace -WorkspaceFile (Join-Path $otherWorkspace 'Probe.csproj') -Target Global -Version 0.8.1 -Feed $feed -Apply|Out-String|ConvertFrom-Json
                    Assert ($other.packageAction-eq 'Reused' -and !$other.packageChanged) 'Second workspace must reuse, never own, the pre-existing global package.'
                    $blockedOther=$false;try{& (Join-Path $PSScriptRoot 'setup-navlyn.ps1') -Workspace $workspace -Target Global -Action Remove -Apply|Out-Null}catch{$blockedOther=$_.Exception.Message-like '*Another workspace references*'}
                    Assert $blockedOther 'Global uninstall ignored another active workspace reference.'
                    SameGlobalFiles $initialGlobalFiles 'Blocked global uninstall changed package bytes.'
                    & (Join-Path $PSScriptRoot 'setup-navlyn.ps1') -Workspace $otherWorkspace -Target Global -Action Remove -Apply|Out-Null
                    SameGlobalFiles $initialGlobalFiles 'Remove of reused global registration changed package bytes.'
                    $currentText=Get-SetupTestText ([IO.File]::ReadAllBytes($config));$withUserEdit=Set-NavlynJsoncServer $currentText.Text userOwnedGlobal '{"command":"keep"}'
                    Write-SetupTestBytes $config ([Text.UTF8Encoding]::new($currentText.Bom).GetBytes($withUserEdit))
                    & (Join-Path $PSScriptRoot 'setup-navlyn.ps1') -Workspace $workspace -Target Global -Action Remove -Apply|Out-Null
                    Assert (!(Test-Path -LiteralPath (Join-Path $actualGlobalRoot '.store/navlyn-mcp')) -and !(Test-Path -LiteralPath $globalShim)) 'Remove of newly owned global install did not remove package-specific bytes.'
                    Assert ((Get-FileHash -LiteralPath $foreignShim).Hash-ceq $foreignHash) 'Global lifecycle altered unrelated navlyn CLI shim.'
                    $afterRemove=ConvertFrom-NavlynJsonc (Get-SetupTestText ([IO.File]::ReadAllBytes($config))).Text
                    Assert ($afterRemove.servers.userOwnedGlobal -and !$afterRemove.servers.Contains('navlyn')) 'Global Remove destroyed unrelated config changes.'
                    Write-Output 'Global actual install/update/downgrade/Undo-stack/shared-reference/Remove lifecycle completed.'
                    }
                } finally {foreach($name in $savedGlobalEnvironment.Keys){[Environment]::SetEnvironmentVariable($name,$savedGlobalEnvironment[$name])}}
            }

        } finally { $env:LOCALAPPDATA=$oldLocal;$env:XDG_DATA_HOME=$oldXdg }
    }
} finally {
    $env:USERPROFILE=$savedUserProfile
    $env:DOTNET_CLI_HOME=$savedDotnetCliHome
    $tempRootPath = $fixtureBase.TrimEnd('\','/')
    $tempRoot = $tempRootPath + [IO.Path]::DirectorySeparatorChar
    $resolvedFixture = [IO.Path]::GetFullPath($fixture)
    if (!$resolvedFixture.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing fixture cleanup outside the temp root.' }
    if (!(Test-Path -LiteralPath (Join-Path $resolvedFixture '.navlyn-owned-test-fixture') -PathType Leaf)) { throw 'Refusing fixture cleanup without its ownership marker.' }
    $walk = $resolvedFixture
    while ($walk.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or $walk.Equals($tempRootPath, [StringComparison]::OrdinalIgnoreCase)) {
        $item = Get-Item -LiteralPath $walk -Force -ErrorAction Stop
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Refusing fixture cleanup through reparse point: $walk" }
        if ($walk.Equals($tempRootPath, [StringComparison]::OrdinalIgnoreCase)) { break }
        $parent = Split-Path -Parent $walk
        if ($parent -eq $walk) { break }
        $walk = $parent
    }
    if(!$FixtureRoot){Remove-Item -LiteralPath $resolvedFixture -Recurse -Force}else{Write-Output ($resolvedFixture)}
}

if ($failures.Count) { $failures | ForEach-Object { Write-Error $_ }; exit 1 }
Write-Output 'Navlyn setup helper focused tests passed.'

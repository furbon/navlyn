# Navlyn First 10 Minutes on Windows

This guide installs the current `0.8.0-preview.1` candidate from a local package feed, then asks Navlyn for one semantic fact. The preview is a release rehearsal and is not published to NuGet. Do not use `dotnet tool install --global` for this path.

Requirements: Windows, PowerShell 7, and .NET SDK 10. Start in a Navlyn source checkout at its repository root and set `$navlynSource` to that absolute path. The pack step builds both `net8.0` and `net10.0` assets and writes packages under a unique `artifacts/navlyn-preview-<id>` directory; it does not publish them.

## 0–3 Minutes: Pack And Install Locally

```powershell
$navlynSource = (Get-Location).Path
$releaseOutput = "artifacts/navlyn-preview-$([guid]::NewGuid().ToString('N'))"
& (Join-Path $navlynSource 'scripts/pack-release.ps1') -NoValidation -Output $releaseOutput
$feed = (Resolve-Path $releaseOutput).Path
$tools = Join-Path $env:TEMP "navlyn-preview-tools-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory $tools | Out-Null
Write-Host "Tool path: $tools"
dotnet tool install navlyn --tool-path $tools --source $feed --version 0.8.0-preview.1
dotnet tool install navlyn-mcp --tool-path $tools --source $feed --version 0.8.0-preview.1
```

The tools live only in the unique temporary tool directory. Switch to the unrelated C# or Visual Basic repository you want to inspect, then run the installed executable by its absolute path:

```powershell
Set-Location 'C:\path\to\consumer-repository'
& (Join-Path $tools 'navlyn.exe') doctor --workspace auto
& (Join-Path $tools 'navlyn.exe') target --workspace auto --query PaymentService --assume-kind NamedType --limit 10
```

The first semantic fact is the selected symbol in the CLI `target` JSON result. Continue with its `candidateId`, for example:

```powershell
& (Join-Path $tools 'navlyn.exe') read --workspace auto --candidate-id sym:v1:... --view declaration --max-lines 80
```

If there is no single workspace candidate, pass the intended solution/project path explicitly. This local-feed workflow is for rehearsal; after publication, use the versioned NuGet install instructions in the README.

## Codex Routing Skill

From the consumer repository, install the three-file skill from the Navlyn source checkout into that repository's `.agents/skills` directory:

```powershell
$skillRoot = Join-Path (Get-Location) '.agents/skills'
New-Item -ItemType Directory -Force $skillRoot | Out-Null
& (Join-Path $navlynSource 'scripts/install-routing-skill.ps1') -Action Install -DestinationRoot $skillRoot
```

Run the same command to update it. The installer is idempotent for identical bytes. It records ownership in the adjacent `.navlyn-semantic-routing.install.json` file. If files were changed outside the installer, an ownership marker is missing/invalid, or unrelated files occupy the destination, it stops and preserves them; inspect and resolve the conflict yourself before retrying. Remove a managed installation with:

```powershell
& (Join-Path $navlynSource 'scripts/install-routing-skill.ps1') -Action Uninstall -DestinationRoot $skillRoot
```

Codex skill discovery and activation were exercised with Codex CLI `0.155.0-alpha.16` on Windows. The six-case activation smoke passed in a process-scoped full-access session with no source write attempts or diff. It did not pass under the tested Windows read-only sandbox, where WindowsApps PowerShell could not launch; do not generalize the activation result to that mode. See [the release contract](navlyn-release-contract.md#client-support-claims) for the evidence boundary.

## MCP Client Setup

For an isolated local-preview server, use the absolute path to the installed `navlyn-mcp.exe` in your client configuration. Start it from the inspected repository root so workspace discovery uses that repository.

**GitHub Copilot CLI** reads `.mcp.json` (or `.github/mcp.json`) at the repository root with the `mcpServers` property. Opt in to repository MCP configuration in PowerShell with `$env:GITHUB_COPILOT_PROMPT_MODE_WORKSPACE_MCP = 'true'` before starting `copilot`. **VS Code** reads `.vscode/mcp.json` with the `servers` property. These formats are different; see [Copilot CLI example](../examples/install/copilot-cli-mcp.json) and the [VS Code MCP guide](https://code.visualstudio.com/docs/agent-customization/mcp-servers). Copilot CLI `1.0.88` on Windows was observed starting the locally installed preview server and completing `navlyn_target` against an unrelated consumer workspace using the explicit additional-config option. That verifies this CLI/package pair only; it does not verify the Codex routing skill in Copilot. VS Code is documented, not release-tested.

For Codex CLI MCP, add the executable from your repository root:

```powershell
$mcpExe = Join-Path $tools 'navlyn-mcp.exe'
codex mcp add navlyn -- $mcpExe
```

This registers the MCP command; the Codex activation evidence above concerns the routing skill, not a separate Codex MCP semantic-client test. For current configuration fields and login requirements, use the official [Copilot CLI MCP documentation](https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/add-mcp-servers), [Codex MCP documentation](https://learn.chatgpt.com/docs/extend/mcp?surface=cli), and [VS Code MCP documentation](https://code.visualstudio.com/docs/agent-customization/mcp-servers).

## If It Fails

- Confirm `dotnet --list-sdks` includes 10.x for source packing and the feed contains both `.nupkg` files.
- Run the installed absolute executable from the consumer repository, not from a repository `bin` or `obj` directory.
- If `doctor` reports an ambiguous workspace, specify the intended `.slnx`, `.sln`, `.csproj`, or `.vbproj` path.
- For the full isolated CLI/MCP install matrix, use `scripts/test-consumer-install.ps1` with a fresh release-pack manifest as described in the release validation guide.

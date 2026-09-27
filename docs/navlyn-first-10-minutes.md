# Navlyn First 10 Minutes on Windows

This guide installs Navlyn 0.8.0 from NuGet into an isolated tool directory, then asks for one semantic fact.

Requirements: Windows, PowerShell 7, and a .NET SDK that can load the repository you want to inspect. The tool packages include `net8.0` and `net10.0` assets.

## 0–3 Minutes: Install

```powershell
$tools = Join-Path $env:TEMP "navlyn-tools-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory $tools | Out-Null
Write-Host "Tool path: $tools"
dotnet tool install navlyn --tool-path $tools --version 0.8.0
dotnet tool install navlyn-mcp --tool-path $tools --version 0.8.0
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

If there is no single workspace candidate, pass the intended solution/project path explicitly.

## Codex Routing Skill

If you want the optional Codex routing skill, clone the Navlyn source first and set `$navlynSource` to its absolute path. From the consumer repository, install its three-file skill into `.agents/skills`:

```powershell
$navlynSource = 'C:\path\to\navlyn'
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

Use the absolute path to the installed `navlyn-mcp.exe` in your client configuration. Start it from the inspected repository root so workspace discovery uses that repository.

**GitHub Copilot CLI** reads `.mcp.json` (or `.github/mcp.json`) at the repository root with the `mcpServers` property. Opt in to repository MCP configuration in PowerShell with `$env:GITHUB_COPILOT_PROMPT_MODE_WORKSPACE_MCP = 'true'` before starting `copilot`. **VS Code** reads `.vscode/mcp.json` with the `servers` property. These formats are different; see [Copilot CLI example](../examples/install/copilot-cli-mcp.json) and the [VS Code MCP guide](https://code.visualstudio.com/docs/agent-customization/mcp-servers). Confirm an installed `navlyn_target` call before claiming that a particular client and package pair works.

For Codex CLI MCP, add the executable from your repository root:

```powershell
$mcpExe = Join-Path $tools 'navlyn-mcp.exe'
codex mcp add navlyn -- $mcpExe
```

This registers the MCP command; the Codex activation evidence above concerns the routing skill, not a separate Codex MCP semantic-client test. For current configuration fields and login requirements, use the official [Copilot CLI MCP documentation](https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/add-mcp-servers), [Codex MCP documentation](https://learn.chatgpt.com/docs/extend/mcp?surface=cli), and [VS Code MCP documentation](https://code.visualstudio.com/docs/agent-customization/mcp-servers).

## If It Fails

- Confirm `dotnet --list-sdks` includes an SDK able to load the inspected repository and both 0.8.0 tools installed successfully.
- If `NAVLYN1201` mentions `global.json`, install its requested SDK or update that file to an installed SDK, then retry `doctor`. Keep the repository's SDK policy in mind before changing it.
- Run the installed absolute executable from the consumer repository, not from a repository `bin` or `obj` directory.
- If `doctor` reports an ambiguous workspace, specify the intended `.slnx`, `.sln`, `.csproj`, or `.vbproj` path.
- If the MCP server cannot start, verify the configured executable and working directory with `Test-Path`, then retry from the intended repository. Check both package versions with `dotnet tool list --tool-path $tools`; reinstall the mismatched package at 0.8.0.
- If an offline install cannot find 0.8.0, use a feed containing both 0.8.0 packages or connect to nuget.org and retry. The failed install is not a usable Navlyn installation.
- For the full isolated CLI/MCP install matrix, use `scripts/test-consumer-install.ps1` with a fresh release-pack manifest as described in the release validation guide.

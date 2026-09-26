# Client Setup

For the current unpublished candidate, follow the [Windows local-preview quick start](navlyn-first-10-minutes.md). It packs both .NET tools to a local feed, installs them under a task-specific `--tool-path`, and uses an absolute executable path. The candidate `0.8.0-preview.1` is not published to NuGet. This source-pack workflow requires Windows and .NET SDK 10 because it builds both `net8.0` and `net10.0` assets.

## Select A Client

| Client or use | Setup | Evidence state |
| --- | --- | --- |
| Codex CLI routing skill | Install from the source checkout with `scripts/install-routing-skill.ps1`; install/update/uninstall and isolated discovery are covered in the quick start. | Verified for Codex CLI `0.155.0-alpha.16` on Windows; activation passed only in a process-scoped full-access session. The tested Windows read-only sandbox could not launch WindowsApps PowerShell. |
| GitHub Copilot CLI MCP | Configure repository `.mcp.json` or `.github/mcp.json` with `mcpServers`; point at the absolute `navlyn-mcp.exe`. Enable repository MCP configuration with `GITHUB_COPILOT_PROMPT_MODE_WORKSPACE_MCP=true` for prompt sessions. | Copilot CLI `1.0.88` on Windows started the installed preview package and completed `navlyn_target` in an isolated consumer using explicit additional config. This does not establish Copilot skill support. |
| VS Code MCP | Configure `.vscode/mcp.json` with `servers`. See [VS Code's MCP guide](https://code.visualstudio.com/docs/agent-customization/mcp-servers). | Configuration documented; not exercised in this release rehearsal. |
| Other MCP clients | Follow that client's official stdio MCP configuration guide. | Documented only unless a specific installed client/version has its own observed test. |

The formats are client-specific: Copilot CLI uses root `.mcp.json` / `.github/mcp.json` with `mcpServers`; VS Code uses `.vscode/mcp.json` with `servers`. A configuration example by itself is not client verification. See [the release contract](navlyn-release-contract.md#client-support-claims) for exact support boundaries.

## Repository Tool Manifest

For a published version, `dotnet new tool-manifest` followed by `dotnet tool install --local navlyn --version <published-version>` and the equivalent `navlyn-mcp` command creates a repository-local `dotnet-tools.json`. Restore it with `dotnet tool restore`, then invoke the CLI with `dotnet tool run navlyn -- doctor --workspace auto`. This example is for an already published package; the preview candidate must be installed from the local feed using the quick-start commands.

The checked-in [manifest example](../examples/install/dotnet-tools.json) records the current release candidate. To restore that preview manifest, copy it to the consumer repository's `.config/dotnet-tools.json` and use the same local feed produced by the quick start:

```powershell
$navlynSource = 'C:\path\to\navlyn'
$feed = 'C:\path\to\navlyn\artifacts\navlyn-preview-id'
New-Item -ItemType Directory -Force .config | Out-Null
Copy-Item (Join-Path $navlynSource 'examples/install/dotnet-tools.json') .config\dotnet-tools.json
dotnet tool restore --add-source $feed
dotnet tool run navlyn -- doctor --workspace auto
```

Replace `$navlynSource` and `$feed` with the absolute source-checkout and unique pack-output paths from the quick start. Because `0.8.0-preview.1` has not been published, restoring this manifest from public NuGet alone will fail.

## MCP Server Command

With a published package installed in the repository-local .NET tool manifest, the command is:

```json
{
  "command": "dotnet",
  "args": ["tool", "run", "navlyn-mcp"],
  "cwd": "."
}
```

For the unreleased preview, configure the absolute `navlyn-mcp.exe` path from the local `--tool-path` installation instead. Launch it with the inspected repository as working directory. Use `--workspace` only when there are multiple plausible workspaces and one must be selected explicitly.

See [README MCP setup](../README.md#use-with-mcp), the [Copilot CLI example](../examples/install/copilot-cli-mcp.json), and [MCP server reference](navlyn-mcp-server.md) for the tool surface. Official configuration guidance: [Copilot CLI](https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/add-mcp-servers), [Codex CLI](https://learn.chatgpt.com/docs/extend/mcp?surface=cli), and [VS Code](https://code.visualstudio.com/docs/agent-customization/mcp-servers).

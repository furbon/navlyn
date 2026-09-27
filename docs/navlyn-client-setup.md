# Client Setup

Start with the [Windows first 10 minutes guide](navlyn-first-10-minutes.md). It installs both 0.8.0 .NET tools from NuGet under an isolated `--tool-path` and runs the installed executable by absolute path. The packages contain `net8.0` and `net10.0` assets.

## Select A Client

| Client or use | Setup | Evidence state |
| --- | --- | --- |
| Codex CLI routing skill | Install from the source checkout with `scripts/install-routing-skill.ps1`; install/update/uninstall and isolated discovery are covered in the quick start. | Verified for Codex CLI `0.155.0-alpha.16` on Windows; activation passed only in a process-scoped full-access session. The tested Windows read-only sandbox could not launch WindowsApps PowerShell. |
| GitHub Copilot CLI MCP | Configure repository `.mcp.json` or `.github/mcp.json` with `mcpServers`; point at the absolute `navlyn-mcp.exe`. Enable repository MCP configuration with `GITHUB_COPILOT_PROMPT_MODE_WORKSPACE_MCP=true` for prompt sessions. | Copilot CLI `1.0.88` on Windows called `navlyn_target` on installed 0.8.0 and returned the consumer symbol. This does not establish Copilot skill support. |
| VS Code MCP | Configure `.vscode/mcp.json` with `servers`. See [VS Code's MCP guide](https://code.visualstudio.com/docs/agent-customization/mcp-servers). | VS Code 1.139.1 Copilot Chat on Windows called `navlyn_target` on an installed 0.8.0 server. It returned `Navlyn.Mcp.Execution.NavlynMcpWorkspaceCache` from the current workspace. |
| Other MCP clients | Follow that client's official stdio MCP configuration guide. | Documented only unless a specific installed client/version has its own observed test. |

The formats are client-specific: Copilot CLI uses root `.mcp.json` / `.github/mcp.json` with `mcpServers`; VS Code uses `.vscode/mcp.json` with `servers`. A configuration example by itself is not client verification. See [the release contract](navlyn-release-contract.md#client-support-claims) for exact support boundaries.

## Repository Tool Manifest

Run `dotnet new tool-manifest`, then `dotnet tool install --local navlyn --version 0.8.0` and the equivalent `navlyn-mcp` command to create a repository-local `dotnet-tools.json`. Restore it with `dotnet tool restore`, then invoke the CLI with `dotnet tool run navlyn -- doctor --workspace auto`.

The checked-in [manifest example](../examples/install/dotnet-tools.json) records both 0.8.0 tools. Copy it to the consumer repository's `.config/dotnet-tools.json` and restore from NuGet:

```powershell
$navlynSource = 'C:\path\to\navlyn'
New-Item -ItemType Directory -Force .config | Out-Null
Copy-Item (Join-Path $navlynSource 'examples/install/dotnet-tools.json') .config\dotnet-tools.json
dotnet tool restore
dotnet tool run navlyn -- doctor --workspace auto
```

Replace `$navlynSource` with the absolute path to your Navlyn source checkout. The manifest lives under `examples/install`; this repository does not keep one at its root.

## MCP Server Command

With a published package installed in the repository-local .NET tool manifest, the command is:

```json
{
  "command": "dotnet",
  "args": ["tool", "run", "navlyn-mcp"],
  "cwd": "."
}
```

For a `--tool-path` installation, configure the absolute `navlyn-mcp.exe` path. Launch it with the inspected repository as working directory. Use `--workspace` only when multiple plausible workspaces require an explicit choice.

See [README MCP setup](../README.md#use-with-mcp), the [Copilot CLI example](../examples/install/copilot-cli-mcp.json), and [MCP server reference](navlyn-mcp-server.md) for the tool surface. Official configuration guidance: [Copilot CLI](https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/add-mcp-servers), [Codex CLI](https://learn.chatgpt.com/docs/extend/mcp?surface=cli), and [VS Code](https://code.visualstudio.com/docs/agent-customization/mcp-servers).

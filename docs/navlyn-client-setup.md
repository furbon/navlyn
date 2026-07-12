# Alternative Installation

For normal CLI and MCP setup, start with the [README](../README.md). This page covers the two alternatives people commonly need after that first setup.

## Pick A Setup

| Persona | Use | Why |
| --- | --- | --- |
| Evaluating alone | Global `navlyn` and `navlyn-mcp` tools | Fastest path to `doctor`, `target`, and one MCP client. |
| Team repository | Repository-local .NET tool manifest | Pins the version for contributors, CI, and agent workspaces. |
| Coding agent client | `navlyn-mcp` stdio server, usually with no args | Gives one read-only semantic tool surface over the repository's single discovered workspace. |
| CI / release validation | `navlyn` CLI plus JSON artifacts | Keeps stdout deterministic and diagnostics on stderr for automation. |

Use automatic workspace discovery for the first setup. Add `navlyn.workspace.json` or pass an explicit workspace only when the repository has multiple plausible workspaces and needs one shared policy.

## Pin Navlyn In A Repository

Use a .NET tool manifest when a team or CI job should use the same Navlyn version:

```powershell
dotnet new tool-manifest
dotnet tool install navlyn --version 0.7.0
dotnet tool install navlyn-mcp --version 0.7.0
dotnet tool restore
```

Run the CLI through the manifest:

```powershell
dotnet tool run navlyn -- doctor --workspace auto
```

For an MCP client, use this command shape:

```json
{
  "command": "dotnet",
  "args": ["tool", "run", "navlyn-mcp"],
  "cwd": "."
}
```

The `cwd` should be the repository root. If the client cannot set `cwd`, add `["tool", "run", "navlyn-mcp", "--", "--working-directory", "."]`. If the repository has multiple top-level workspace candidates, add `["tool", "run", "navlyn-mcp", "--", "--workspace", "path/to/YourRepo.sln"]`.

## MCP Tool Surface

Navlyn MCP exposes one stable read-only semantic tool surface. Configure the workspace once; the agent chooses the smallest relevant tool from tool descriptions, schemas, and returned evidence.

| Need | Start with |
| --- | --- |
| Setup and workspace health | `navlyn_doctor` |
| First symbol anchor | `navlyn_target` |
| Known file outline | `navlyn_file_outline` |
| One selected source or relationship fact | `navlyn_read` or `navlyn_symbol_edges` |
| Pre-edit evidence | `navlyn_prepare_edit` |
| Actual Git diff evidence | `navlyn_review` |

Use [navlyn-mcp-server.md](navlyn-mcp-server.md) for the complete MCP tool surface. Agent instruction snippets for Copilot, Claude, Codex, and other clients live in `examples/agents`.

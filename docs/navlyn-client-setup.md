# Alternative Installation

For normal CLI and MCP setup, start with the [README](../README.md). This page covers the two alternatives people commonly need after that first setup.

## Pick A Setup

| Persona | Use | Why |
| --- | --- | --- |
| Evaluating alone | Global `navlyn` and `navlyn-mcp` tools | Fastest path to `doctor`, `resolve-target`, and one MCP client. |
| Team repository | Repository-local .NET tool manifest | Pins the version for contributors, CI, and agent workspaces. |
| Coding agent client | `navlyn-mcp` stdio server with explicit `--workspace` | Gives one read-only semantic tool surface over the intended solution or project. |
| CI / release validation | `navlyn` CLI plus JSON artifacts | Keeps stdout deterministic and diagnostics on stderr for automation. |

Use a direct solution or project path for the first setup. Add `navlyn.workspace.json` only when the repository has multiple plausible workspaces and needs one shared policy.

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
dotnet tool run navlyn -- doctor --workspace path/to/YourRepo.sln
```

For an MCP client, use this command shape:

```json
{
  "command": "dotnet",
  "args": ["tool", "run", "navlyn-mcp", "--", "--workspace", "path/to/YourRepo.sln"]
}
```

## MCP Tool Surface

Navlyn MCP exposes one stable read-only semantic tool surface. Configure the workspace once; the agent chooses the smallest relevant tool from tool descriptions, schemas, and returned evidence.

| Need | Start with |
| --- | --- |
| Setup and workspace health | `navlyn_doctor` |
| First symbol anchor | `navlyn_resolve_target` |
| Known file outline | `navlyn_file_outline` |
| One selected source or relationship fact | `navlyn_symbol_source` or `navlyn_symbol_edges` |
| Pre-edit evidence | `navlyn_edit_preflight` |
| Actual Git diff evidence | `navlyn_review_diff` |

Use [navlyn-mcp-server.md](navlyn-mcp-server.md) for the complete MCP tool surface. Agent instruction snippets for Copilot, Claude, Codex, and other clients live in `examples/agents`.

# Alternative Installation

For normal CLI and MCP setup, start with the [README](../README.md). This page covers the two alternatives people commonly need after that first setup.

## Pin Navlyn In A Repository

Use a .NET tool manifest when a team or CI job should use the same Navlyn version:

```powershell
dotnet new tool-manifest
dotnet tool install navlyn --version 0.6.0
dotnet tool install navlyn-mcp --version 0.6.0
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
  "args": ["tool", "run", "navlyn-mcp", "--", "--workspace", "path/to/YourRepo.sln", "--tool-profile", "reader"]
}
```

## Choose An MCP Profile

Start with `reader`. The profile is fixed while the MCP server runs, so restart the server when a task needs a broader profile.

| Profile | Use it for |
| --- | --- |
| `reader` | Setup checks, source reading, and symbol investigation. |
| `edit` | Pre-edit evidence and post-edit guard checks. |
| `review` | Actual Git-diff and review evidence. |
| `full` | Every MCP tool, including `navlyn_batch`. |

Use [navlyn-mcp-server.md](navlyn-mcp-server.md) for the complete MCP tool surface. Agent instruction snippets for Copilot, Claude, Codex, and other clients live in `examples/agents`.

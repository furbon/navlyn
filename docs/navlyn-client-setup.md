# Navlyn Client Setup

This page is for the moment after someone decides: "I want my C# agent to have semantic facts before it edits." Pick one install shape, point it at a workspace, and keep the first MCP surface narrow.

Use:

- `navlyn` for shell, CI, scripts, and local experiments;
- `navlyn-mcp` when an MCP-capable client should ask Navlyn questions without composing shell commands.

MCP use requires only the `navlyn-mcp` package. A separate `navlyn` CLI install is convenient, but not required for the MCP server.

## 30-Second CLI Setup

Global install for personal use:

```powershell
dotnet tool install --global navlyn
navlyn check --workspace path/to/YourRepo.slnx
navlyn resolve-target --workspace path/to/YourRepo.slnx --query PaymentService --assume-kind NamedType
```

Repository-local install for teams and agent workspaces:

```powershell
dotnet new tool-manifest
dotnet tool install navlyn --version 0.5.0
dotnet tool restore
dotnet tool run navlyn -- check --workspace path/to/YourRepo.slnx
```

Use a tool manifest when a repository should pin the Navlyn version for every contributor, CI job, and agent. Use a global install for quick personal investigation across repositories.

## 30-Second MCP Setup

Global tool shape:

```json
{
  "command": "navlyn-mcp",
  "args": ["--workspace", "path/to/navlyn.workspace.json", "--tool-profile", "reader"]
}
```

Repository-local tool shape:

```json
{
  "command": "dotnet",
  "args": ["tool", "run", "navlyn-mcp", "--", "--workspace", "path/to/navlyn.workspace.json", "--tool-profile", "reader"]
}
```

Keep the client working directory at the repository root when using repository-relative paths. Start with `--tool-profile reader`; switch to `review`, `edit`, or `full` only when the task needs that broader surface.

## Workspace Argument

Prefer an explicit workspace path:

```text
--workspace path/to/navlyn.workspace.json
--workspace path/to/YourRepo.slnx
--workspace path/to/YourRepo.code-workspace
```

Use `--workspace auto` only when the repository has one clear top-level workspace candidate. If `auto` is ambiguous, pass `navlyn.workspace.json`, `.code-workspace`, `.slnx`, `.sln`, or `.csproj` explicitly.

`navlyn.workspace.json` is the best shared MCP anchor for repositories that need:

- a stable primary workspace;
- generated/test exclusion policy;
- multiple workspace candidates;
- outside-root allow-list policy;
- lightweight cache hints.

MCP defaults `--workspace-root-policy` to `repo-relative`. Use `allow-listed` with `allowRoots` in `navlyn.workspace.json`, or `all`, only when external folders are intentional.

## VS Code Shape

For `.vscode/mcp.json` style configuration:

```json
{
  "servers": {
    "navlyn": {
      "type": "stdio",
      "command": "navlyn-mcp",
      "args": ["--workspace", "${workspaceFolder}/navlyn.workspace.json", "--tool-profile", "reader"],
      "cwd": "${workspaceFolder}"
    }
  }
}
```

For a `.code-workspace` backed repository, pass the workspace file explicitly:

```json
{
  "servers": {
    "navlyn": {
      "type": "stdio",
      "command": "navlyn-mcp",
      "args": ["--workspace", "${workspaceFolder}/YourRepo.code-workspace", "--tool-profile", "reader"],
      "cwd": "${workspaceFolder}"
    }
  }
}
```

Copyable examples live under `examples/install` and `examples/mcp`.

## MCP Tool Profiles

`navlyn-mcp` defaults to `reader`.

| Profile | Use It When | Why It Exists |
| --- | --- | --- |
| `reader` | The agent is inspecting files, resolving symbols, or gathering one selected-symbol fact. | Keeps first-pass behavior narrow and avoids review/test/context checklists. |
| `review` | The task is an actual Git diff, PR, release review, or CI evidence pass. | Adds diff, public API, related-test, review, and context facts. |
| `edit` | The agent is planning a concrete code change around a selected symbol. | Adds impact, related tests, DI impact, entrypoints, and bounded context. |
| `full` | A client needs compatibility with every pre-profile Navlyn MCP tool. | Exposes the complete surface, including `navlyn_batch`. |

`NAVLYN_MCP_TOOL_PROFILE` accepts the same values for clients that prefer environment variables. Restart the MCP server after changing the profile.

## Agent Instruction Snippet

```text
Use normal file reads and rg first when text is enough.
Use Navlyn only when C# semantic identity, project context, source relationships, diff facts, or bounded evidence would change the answer.
In MCP reader profile, start with navlyn_file_outline for one known C# file or navlyn_resolve_target for fuzzy symbol intent.
Reuse returned candidateId values for follow-up source, edge, about, impact, or context calls.
Start navlyn-mcp with --tool-profile review for actual Git diff review, edit for edit planning, or full for compatibility with every tool.
Use navlyn_context_pack and navlyn_batch only when the active profile exposes them and smaller facts show they are needed.
Treat nextActions as conditional follow-up hints, not a checklist.
```

Client-specific instruction examples live in `examples/agents`.

## Quick Confidence Check

After installing, a healthy setup should pass:

```powershell
navlyn check --workspace path/to/YourRepo.slnx
navlyn repo-graph --workspace path/to/YourRepo.slnx --profile compact
```

If workspace loading fails, check:

- the .NET SDK can restore/build the target repository;
- the workspace path is explicit and points at the intended solution/project;
- `--workspace auto` is not ambiguous;
- outside-root folders are allowed only when intentional.

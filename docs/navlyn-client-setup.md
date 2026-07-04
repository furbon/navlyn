# Navlyn Client Setup

Use `navlyn` for shell, CI, and scripts. Use `navlyn-mcp` when an MCP-capable client should ask Navlyn questions without composing shell commands. MCP use requires only the `navlyn-mcp` tool package.

## Workspace Argument

Prefer an explicit workspace path:

```text
--workspace path/to/navlyn.workspace.json
--workspace path/to/YourRepo.slnx
--workspace path/to/YourRepo.code-workspace
```

Use `--workspace auto` only when the repository has one clear top-level workspace candidate. If `auto` is ambiguous, pass a `navlyn.workspace.json`, `.code-workspace`, `.slnx`, `.sln`, or `.csproj` path explicitly. `navlyn.workspace.json` is the best shared MCP anchor for repositories that need candidate filtering, generated/test exclusion, or outside-root allow-list policy.

## CLI Local Tool

```powershell
dotnet new tool-manifest
dotnet tool install navlyn
dotnet tool restore
dotnet tool run navlyn -- check --workspace path/to/YourRepo.slnx
```

Use a tool manifest when a team wants one repository-local Navlyn version. Use a global install for personal investigation across repositories.

## MCP Server

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

Client config files use different container keys, but the stable part is the command, args, and working directory. Keep the working directory at the repository root when using repository-relative paths. MCP defaults `--workspace-root-policy` to `repo-relative`; use `allow-listed` with `allowRoots` in `navlyn.workspace.json`, or `all`, only when external folders are intentional.

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

Copyable example files live under `examples/install`, including `vscode-mcp.json` and `vscode-code-workspace-mcp.json`, and under `examples/mcp`.

## MCP Tool Profile

`navlyn-mcp` defaults to `--tool-profile reader`. Reader mode exposes file-first and selected-symbol investigation tools and hides review, edit-planning, public API, DI, tests, context-pack, and batch tools from the first-pass MCP surface.

Use `--tool-profile review` for actual Git diff or PR review, `--tool-profile edit` for symbol edit planning, and `--tool-profile full` when a client needs the complete pre-profile tool surface. `NAVLYN_MCP_TOOL_PROFILE` accepts the same values for clients that prefer environment configuration. Restart the MCP server after changing the profile.

## Agent Instruction Snippet

```text
Use normal file reads and rg first when text is enough.
Use Navlyn only when C# semantic identity, project context, source relationships, diff facts, or bounded evidence would change the answer.
In MCP reader profile, start with navlyn_file_outline for one known file or navlyn_resolve_target for fuzzy symbol intent.
Start navlyn-mcp with --tool-profile review for actual Git diff review, edit for edit planning, or full for compatibility with every tool.
Use navlyn_context_pack and navlyn_batch only when the active profile exposes them and smaller facts show they are needed.
Treat nextActions as conditional follow-up hints, not a checklist.
```

Client-specific instruction examples live in `examples/agents`.

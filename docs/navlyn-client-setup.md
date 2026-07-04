# Navlyn Client Setup

Use `navlyn` for shell, CI, and scripts. Use `navlyn-mcp` when an MCP-capable client should ask Navlyn questions without composing shell commands. MCP use requires only the `navlyn-mcp` tool package.

## Workspace Argument

Prefer an explicit workspace path:

```text
--workspace path/to/YourRepo.slnx
--workspace path/to/YourRepo.code-workspace
```

Use `--workspace auto` only when the repository has one clear top-level workspace candidate. If `auto` is ambiguous, pass a `.code-workspace`, `.slnx`, `.sln`, or `.csproj` path explicitly.

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
  "args": ["--workspace", "path/to/YourRepo.slnx"]
}
```

Repository-local tool shape:

```json
{
  "command": "dotnet",
  "args": ["tool", "run", "navlyn-mcp", "--", "--workspace", "path/to/YourRepo.slnx"]
}
```

Client config files use different container keys, but the stable part is the command, args, and working directory. Keep the working directory at the repository root when using repository-relative paths.

## VS Code Shape

For `.vscode/mcp.json` style configuration:

```json
{
  "servers": {
    "navlyn": {
      "type": "stdio",
      "command": "navlyn-mcp",
      "args": ["--workspace", "${workspaceFolder}/YourRepo.slnx"],
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
      "args": ["--workspace", "${workspaceFolder}/YourRepo.code-workspace"],
      "cwd": "${workspaceFolder}"
    }
  }
}
```

Copyable example files live under `examples/install`, including `vscode-mcp.json` and `vscode-code-workspace-mcp.json`, and under `examples/mcp`.

## Agent Instruction Snippet

```text
Use normal file reads and rg first when text is enough.
Use Navlyn only when C# semantic identity, project context, source relationships, diff facts, or bounded evidence would change the answer.
In MCP, start with navlyn_file_outline for one known file or navlyn_resolve_target for fuzzy symbol intent.
Use navlyn_review_diff only for an actual Git diff.
Use navlyn_context_pack and navlyn_batch only after smaller facts show they are needed.
Treat nextActions as conditional follow-up hints, not a checklist.
```

Client-specific instruction examples live in `examples/agents`.

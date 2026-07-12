# Navlyn

Japanese: [`README_ja.md`](README_ja.md)

**Stop your coding agent from wandering through the codebase.**

Navlyn turns an instruction such as "fix `PaymentService`" into the small set of code, relationships, and checks an agent needs to act. It picks the intended target, gives the agent a reusable anchor for its investigation, and checks whether the actual diff stayed on that target.

```text
"Fix PaymentService"
        |
        v
choose the intended target -> read only what matters -> change it -> check the diff
```

Generic code search gives an agent a pile of matches. Navlyn keeps each next question attached to one selected target: which symbol the user meant, what code it depends on, what should be read before changing it, and whether the edit landed where intended. The agent reads less unrelated code, makes fewer broad searches, and keeps its context on facts that can change the edit.

`navlyn-mcp` is the primary way to use Navlyn with coding agents. The `navlyn` CLI uses the same engine for shell workflows and CI.

## Use With MCP

Install the server once:

```powershell
dotnet tool install --global navlyn-mcp --version 0.6.0
```

Then point it at the solution or project the agent should inspect. Replace `YourRepo.sln` with the repository's `.slnx`, `.sln`, `.csproj`, or `.vbproj` file.

### GitHub Copilot In VS Code

Create `.vscode/mcp.json` in the repository root:

```json
{
  "servers": {
    "navlyn": {
      "type": "stdio",
      "command": "navlyn-mcp",
      "args": ["--workspace", "${workspaceFolder}/YourRepo.sln", "--tool-profile", "reader"],
      "cwd": "${workspaceFolder}"
    }
  }
}
```

### Codex

Run this from the repository root:

```powershell
codex mcp add navlyn -- navlyn-mcp --workspace path/to/YourRepo.sln --tool-profile reader
```

### Claude Code

Create `.mcp.json` in the repository root:

```json
{
  "mcpServers": {
    "navlyn": {
      "type": "stdio",
      "command": "navlyn-mcp",
      "args": ["--workspace", "${CLAUDE_PROJECT_DIR:-.}/YourRepo.sln", "--tool-profile", "reader"]
    }
  }
}
```

Start with the `reader` profile. Restart with `edit` when the agent needs edit preflight and post-edit guards, or `review` for an actual Git diff. `full` exists for clients that need every tool.

## What The Agent Gets

| Agent question | Navlyn MCP tool |
| --- | --- |
| Which symbol did the user mean? | `navlyn_resolve_target` |
| What is in this file? | `navlyn_file_outline` |
| Show the declaration for this selected symbol. | `navlyn_symbol_source` |
| Who calls or references it? | `navlyn_symbol_edges` |
| What should I know before changing it? | `navlyn_edit_preflight` in `edit` profile |
| What did this Git diff affect? | `navlyn_review_diff` in `review` profile |

The useful unit is one answerable question, not a repository dump. Navlyn returns bounded JSON facts so an agent can keep an exact `candidateId` through its investigation and only request the next relationship or source slice when it needs it.

## Workspace Choice

Most repositories do not need `navlyn.workspace.json`: pass the solution or project file directly, as in the MCP examples above.

Add `navlyn.workspace.json` only when a repository has several possible solutions/projects and needs one shared choice. Its smallest useful form is:

```json
{
  "primaryWorkspace": "YourRepo.sln"
}
```

The complete configuration reference, including candidate discovery and root policy, is in [docs/navlyn-workspace.md](docs/navlyn-workspace.md).

## CLI And CI

The CLI is optional for MCP use. Install it when you want the same facts in a shell script or CI job:

```powershell
dotnet tool install --global navlyn --version 0.6.0
navlyn doctor --workspace path/to/YourRepo.sln
```

## Boundaries

Navlyn is local and read-only. It does not edit files, run arbitrary shell commands, call the network, upload source, or claim to prove runtime behavior. Use it where compiler and project facts matter; use normal file reads and `rg` where text is enough.

## Documentation

- [MCP server reference](docs/navlyn-mcp-server.md): profiles, tools, resources, and protocol behavior.
- [Workspace configuration](docs/navlyn-workspace.md): when and how to use `navlyn.workspace.json`.
- [First investigation](docs/navlyn-first-10-minutes.md): a short semantic investigation flow after setup.
- [CLI command reference](docs/navlyn-cli-commands.md): complete command and JSON contract.
- [Agent recipes](docs/navlyn-agent-recipes.md): focused CLI and MCP workflows.

Client-specific configuration formats are documented by [GitHub Copilot](https://docs.github.com/en/copilot/how-tos/provide-context/use-mcp-in-your-ide/extend-copilot-chat-with-mcp), [Codex](https://learn.chatgpt.com/docs/extend/mcp), and [Claude Code](https://docs.anthropic.com/en/docs/claude-code/mcp).

## License

Navlyn is licensed under the MIT License. See [LICENSE](LICENSE).

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

`navlyn-mcp` is the primary way to use Navlyn with coding agents. The `navlyn` CLI uses the same engine for shell workflows, CI, and first-run verification.

## Three-Minute Path

Install the CLI and MCP server:

```powershell
dotnet tool install --global navlyn --version 0.7.0
dotnet tool install --global navlyn-mcp --version 0.7.0
```

Then verify one workspace and one symbol. Replace `YourRepo.slnx` with the repository's `.slnx`, `.sln`, `.csproj`, or `.vbproj` file:

```powershell
navlyn doctor --workspace path/to/YourRepo.slnx
navlyn resolve-target --workspace path/to/YourRepo.slnx --query PaymentService --assume-kind NamedType --limit 10
navlyn symbol-source --workspace path/to/YourRepo.slnx --candidate-id sym:v1:... --view declaration --max-lines 80
navlyn edit-preflight --workspace path/to/YourRepo.slnx --candidate-id sym:v1:... --goal modify --change-kind behavior
```

Use the `candidateId` from `resolve-target` for follow-up calls. After an edit, inspect the real diff:

```powershell
navlyn review-diff --workspace path/to/YourRepo.slnx --profile evidence
```

## Use With MCP

Point the server at the same workspace the CLI verified.

### GitHub Copilot In VS Code

Create `.vscode/mcp.json` in the repository root:

```json
{
  "servers": {
    "navlyn": {
      "type": "stdio",
      "command": "navlyn-mcp",
      "args": ["--workspace", "${workspaceFolder}/YourRepo.sln"],
      "cwd": "${workspaceFolder}"
    }
  }
}
```

### Codex

Run this from the repository root:

```powershell
codex mcp add navlyn -- navlyn-mcp --workspace path/to/YourRepo.sln
```

### Claude Code

Create `.mcp.json` in the repository root:

```json
{
  "mcpServers": {
    "navlyn": {
      "type": "stdio",
      "command": "navlyn-mcp",
      "args": ["--workspace", "${CLAUDE_PROJECT_DIR:-.}/YourRepo.sln"]
    }
  }
}
```

Navlyn MCP exposes one stable read-only semantic tool surface. Configure the workspace once; the agent should start with the smallest relevant fact, reuse `candidateId`, and stop when the returned JSON answers the question.

## What The Agent Gets

| Agent question | Navlyn MCP tool |
| --- | --- |
| Which symbol did the user mean? | `navlyn_resolve_target` |
| What is in this file? | `navlyn_file_outline` |
| Show the declaration for this selected symbol. | `navlyn_symbol_source` |
| Who calls or references it? | `navlyn_symbol_edges` |
| What should I know before changing it? | `navlyn_edit_preflight` |
| What did this Git diff affect? | `navlyn_review_diff` |

The useful unit is one answerable question, not a repository dump. Navlyn returns bounded JSON facts so an agent can keep an exact `candidateId` through its investigation and only request the next relationship or source slice when it needs it.

## Use / Do Not Use

| Use Navlyn For | Do Not Use Navlyn For |
| --- | --- |
| C# or Visual Basic symbol identity, overloads, partial declarations, project context, target frameworks, DI, routes, related tests, and Git diff evidence. | Comments, strings, docs, arbitrary text search, generated artifacts, runtime proof, security scanning, test execution, editing, refactoring, or publishing review comments. |

Use normal file reads and `rg` when text is enough. Use Navlyn when Roslyn/MSBuild facts would change what the agent reads or edits.

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

The CLI is optional after MCP setup, but it is the easiest way to verify installs, script facts, and produce CI evidence.

## Boundaries

Navlyn is local and read-only. It does not edit files, run arbitrary shell commands, call the network, upload source, or claim to prove runtime behavior. Use it where compiler and project facts matter; use normal file reads and `rg` where text is enough.

## Documentation

- [MCP server reference](docs/navlyn-mcp-server.md): stable tool surface, resources, and protocol behavior.
- [Workspace configuration](docs/navlyn-workspace.md): when and how to use `navlyn.workspace.json`.
- [First investigation](docs/navlyn-first-10-minutes.md): a short semantic investigation flow after setup.
- [Demos and case studies](docs/navlyn-demo-walkthroughs.md): reproducible current-repo and fixture-backed evidence.
- [CLI command reference](docs/navlyn-cli-commands.md): complete command and JSON contract.
- [Agent recipes](docs/navlyn-agent-recipes.md): focused CLI and MCP workflows.

Client-specific configuration formats are documented by [GitHub Copilot](https://docs.github.com/en/copilot/how-tos/provide-context/use-mcp-in-your-ide/extend-copilot-chat-with-mcp), [Codex](https://learn.chatgpt.com/docs/extend/mcp), and [Claude Code](https://docs.anthropic.com/en/docs/claude-code/mcp).

## License

Navlyn is licensed under the MIT License. See [LICENSE](LICENSE).

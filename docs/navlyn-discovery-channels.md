# Navlyn Discovery Channels

This note separates repository work from external publishing. External accounts, registry submissions, Marketplace publishing, and GitHub repository settings require maintainer action.

## NuGet

Package IDs:

- `navlyn`: read-only C#-first .NET semantic evidence CLI with Visual Basic support.
- `navlyn-mcp`: read-only MCP server for Navlyn evidence workflows.

Suggested package description:

```text
Read-only C#-first .NET semantic evidence for coding agents before they edit the wrong symbol, with Visual Basic support.
```

Suggested tags:

```text
dotnet-tool;csharp;visual-basic;dotnet;roslyn;mcp;ai-agents;semantic-code-navigation;automation
```

Publishing is gated by the release runbook in [`navlyn-distribution.md`](navlyn-distribution.md).

## GitHub Repository About

Suggested description:

```text
Read-only C#-first .NET semantic evidence for coding agents before they edit the wrong symbol, with Visual Basic support.
```

Suggested topics:

```text
csharp, visual-basic, dotnet, roslyn, mcp, mcp-server, ai-agents, code-navigation, semantic-search, cli, automation
```

Suggested release note opening:

```text
Navlyn 0.7.0 helps C#-first .NET coding agents avoid wrong-symbol edits, with Visual Basic support through Roslyn/MSBuild. It resolves fuzzy intent into stable Roslyn-backed targets, opens bounded source and relationship facts, builds compact context packs, and collects review evidence without editing files. The `navlyn` CLI and standalone `navlyn-mcp` server share the same Navlyn engine.
```

Package page distinction:

- `navlyn`: install when a user wants shell/CI JSON facts, release validation, or local scripts.
- `navlyn-mcp`: install when a coding-agent client should call one read-only stdio MCP server. It does not require a separate `navlyn` install for normal use.

Do not claim runtime correctness, security scanning, refactoring, hosted indexing, source upload, or test execution in package copy.

## VS Code MCP

VS Code supports workspace MCP configuration in `.vscode/mcp.json` and stdio server entries with `command`, `args`, optional `cwd`, and `type: "stdio"`. A copyable Navlyn example is [`../examples/install/vscode-mcp.json`](../examples/install/vscode-mcp.json).

VS Code also documents an MCP installation URL shape:

```text
vscode:mcp/install?{url-encoded-json-server-configuration}
```

Go/no-go for v0.7.0: no public install URL yet. VS Code supports installation URLs, and Navlyn can start without explicit args by using repository-local auto discovery. A public one-click link still needs validation across client behavior, multi-root workspaces, and multi-solution repositories before it becomes the recommended path.

Manual setup remains the supported path for v0.7.0. If a future installer is tested, the least risky draft object shape is:

```json
{
  "name": "navlyn",
  "type": "stdio",
  "command": "navlyn-mcp",
  "cwd": "${workspaceFolder}"
}
```

Omitting `--workspace` is equivalent to auto discovery from the server working directory. It is convenient for repositories with one top-level workspace candidate; it prefers `navlyn.workspace.json`, then `.code-workspace`, then `.slnx`, then `.sln`, then `.csproj` or `.vbproj`. Multi-solution repositories should use an explicit workspace path, preferably repository-local `navlyn.workspace.json` when candidate policy matters.

Manual fallback:

1. Install `navlyn-mcp` as a global or repository-local .NET tool.
2. Add `.vscode/mcp.json` with `command: "navlyn-mcp"` and repository-root `cwd`.
3. Run `navlyn doctor --workspace auto` before asking an agent to rely on the tools.
4. Start the MCP server from VS Code and inspect the discovered tools before approving agent use.

## MCP Registry

Verify the active registry submission format and account requirements immediately before publishing. Registry records should point to the packaged `navlyn-mcp` stdio tool, the current release notes, and workspace setup guidance.

Autonomous repo work can prepare:

- stable package metadata;
- a tested `.vscode/mcp.json` example;
- a manifest draft once registry format and submission requirements are confirmed;
- release notes that explain `navlyn_target`, `navlyn_prepare_edit`, `navlyn_review`, and advanced escalation through `navlyn_context_pack` and `navlyn_batch`.

Draft registry metadata:

```json
{
  "name": "navlyn",
  "displayName": "Navlyn",
  "description": "Read-only C#-first .NET semantic evidence for coding agents before they edit the wrong symbol.",
  "package": "navlyn-mcp",
  "transport": "stdio",
  "command": "navlyn-mcp",
  "args": [],
  "cwd": "<repository-root>",
  "categories": ["C#", ".NET", "Roslyn", "AI agents", "code navigation"],
  "security": "Local read-only source-level facts. No editing, shell execution, network calls, source upload, tests, or runtime proof."
}
```

Maintainer-owned external work:

- submit or approve registry entries;
- configure organization allowlists or policies;
- publish to any Marketplace or Open VSX surface;
- decide when the no-args auto-discovery startup is acceptable for public install links.

## Lightweight VS Code Extension Boundary

Navlyn 0.7.0 does not need a full editor extension. If a VS Code extension is justified by user demand, keep it to installer/configurator duties:

- detect whether `navlyn` and `navlyn-mcp` are installed;
- locate likely `.slnx`, `.sln`, `.csproj`, or `.vbproj` workspace files;
- create or update `.vscode/mcp.json`;
- run `navlyn check` and show the result location;
- link to MCP server logs.

This extension would not replace the CLI/MCP server, implement semantic analysis itself, or become an editor refactoring surface.

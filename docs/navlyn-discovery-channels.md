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
Navlyn 0.6.0 helps C#-first .NET coding agents avoid wrong-symbol edits, with Visual Basic support through Roslyn/MSBuild. It resolves fuzzy intent into stable Roslyn-backed targets, opens bounded source and relationship facts, builds compact context packs, and collects review evidence without editing files. The `navlyn` CLI and standalone `navlyn-mcp` server share the same Navlyn engine.
```

## VS Code MCP

VS Code supports workspace MCP configuration in `.vscode/mcp.json` and stdio server entries with `command`, `args`, optional `cwd`, and `type: "stdio"`. A copyable Navlyn example is [`../examples/install/vscode-mcp.json`](../examples/install/vscode-mcp.json).

VS Code also documents an MCP installation URL shape:

```text
vscode:mcp/install?{url-encoded-json-server-configuration}
```

Add a production install link only after testing it with the packaged `navlyn-mcp` tool and a real workspace path strategy. A safe object shape is:

```json
{
  "name": "navlyn",
  "type": "stdio",
  "command": "navlyn-mcp",
  "args": ["--workspace", "auto"]
}
```

`--workspace auto` is convenient only for repositories with one top-level workspace candidate; it prefers `navlyn.workspace.json`, then `.code-workspace`, then `.slnx`, then `.sln`, then `.csproj` or `.vbproj`. Multi-solution repositories should use an explicit workspace path, preferably repository-local `navlyn.workspace.json` when candidate policy matters.

## MCP Registry

Verify the active registry submission format and account requirements immediately before publishing. Registry records should point to the packaged `navlyn-mcp` stdio tool, the current release notes, and workspace setup guidance.

Autonomous repo work can prepare:

- stable package metadata;
- a tested `.vscode/mcp.json` example;
- a manifest draft once registry format and submission requirements are confirmed;
- release notes that explain `navlyn_resolve_target`, `navlyn_context_pack`, and `navlyn_batch`.

Maintainer-owned external work:

- submit or approve registry entries;
- configure organization allowlists or policies;
- publish to any Marketplace or Open VSX surface;
- decide whether `--workspace auto` is acceptable for public install links.

## Lightweight VS Code Extension Boundary

Navlyn 0.6.0 does not need a full editor extension. If a VS Code extension is justified by user demand, keep it to installer/configurator duties:

- detect whether `navlyn` and `navlyn-mcp` are installed;
- locate likely `.slnx`, `.sln`, `.csproj`, or `.vbproj` workspace files;
- create or update `.vscode/mcp.json`;
- run `navlyn check` and show the result location;
- link to MCP server logs.

This extension would not replace the CLI/MCP server, implement semantic analysis itself, or become an editor refactoring surface.

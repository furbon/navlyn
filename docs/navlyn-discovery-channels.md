# Navlyn Discovery Channels

This note separates repository work from external publishing. External accounts, registry submissions, Marketplace publishing, and GitHub repository settings require maintainer action.

Confirmed against public docs on 2026-06-28.

## NuGet

Package IDs:

- `navlyn`: read-only semantic C#/.NET evidence CLI.
- `navlyn-mcp`: read-only MCP server for Navlyn evidence workflows.

Suggested package description:

```text
Read-only semantic C#/.NET evidence CLI for coding agents, CI, and automation.
```

Suggested tags:

```text
dotnet-tool;csharp;dotnet;roslyn;mcp;ai-agents;semantic-code-navigation;automation
```

Publishing remains gated by the release runbook in [`navlyn-distribution.md`](navlyn-distribution.md).

## GitHub Repository About

Suggested description:

```text
Read-only semantic C#/.NET evidence layer for coding agents, CI, and MCP workflows.
```

Suggested topics:

```text
csharp, dotnet, roslyn, mcp, mcp-server, ai-agents, code-navigation, semantic-search, cli, automation
```

Suggested release note opening:

```text
Navlyn 0.4.0 gives C#/.NET coding agents a read-only semantic evidence layer: resolve targets, inspect exact Roslyn facts, build bounded context packs, and collect review facts without editing files. The `navlyn` CLI and standalone `navlyn-mcp` server share the same Navlyn engine.
```

## VS Code MCP

VS Code supports workspace MCP configuration in `.vscode/mcp.json` and stdio server entries with `command`, `args`, optional `cwd`, and `type: "stdio"`. A copyable Navlyn example is [`../examples/install/vscode-mcp.json`](../examples/install/vscode-mcp.json).

VS Code also documents an MCP installation URL shape:

```text
vscode:mcp/install?{url-encoded-json-server-configuration}
```

Do not add a production install link to README until it has been tested with the packaged `navlyn-mcp` tool and a real workspace path strategy. A safe draft object is:

```json
{
  "name": "navlyn",
  "type": "stdio",
  "command": "navlyn-mcp",
  "args": ["--workspace", "auto"]
}
```

`--workspace auto` is convenient only for repositories with one top-level workspace candidate. Multi-solution repositories should use explicit workspace paths.

## MCP Registry

The GitHub MCP Registry is public preview and subject to change. Organization-level MCP registries are HTTPS endpoints that serve registry records such as `GET /v0.1/servers` and version-specific server metadata.

Autonomous repo work can prepare:

- stable package metadata;
- a tested `.vscode/mcp.json` example;
- a manifest draft once registry format and submission requirements are selected;
- release notes that explain `navlyn_resolve_target`, `navlyn_context_pack`, and `navlyn_batch`.

Maintainer-owned external work:

- submit or approve registry entries;
- configure organization allowlists or policies;
- publish to any Marketplace or Open VSX surface;
- decide whether `--workspace auto` is acceptable for public install links.

## Lightweight VS Code Extension Decision

Do not build a full editor extension for 0.3.x. If a VS Code extension is later justified, keep it to installer/configurator duties:

- detect whether `navlyn` and `navlyn-mcp` are installed;
- locate likely `.slnx`, `.sln`, or `.csproj` workspace files;
- create or update `.vscode/mcp.json`;
- run `navlyn check` and show the result location;
- link to MCP server logs.

This extension would not replace the CLI/MCP server, implement semantic analysis itself, or become an editor refactoring surface.

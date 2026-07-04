# Navlyn

Japanese: [`README_ja.md`](README_ja.md)

**Navlyn helps C#/.NET coding agents avoid editing the wrong symbol.**

When an agent sees `PaymentService`, text search can show files that contain that string. It cannot safely decide which overload, target framework, partial declaration, dependency-injection registration, route handler, public API surface, or related tests actually matter. Navlyn gives the agent local Roslyn/MSBuild evidence before it edits.

Navlyn is:

- a `navlyn` CLI that returns deterministic JSON on stdout;
- a standalone `navlyn-mcp` stdio MCP server for MCP-capable coding agents and editors;
- read-only, local, and facts-only: no edits, no arbitrary shell, no network access, no hosted index.

Use it as a **C# agent preflight**: resolve the exact symbol, inspect bounded source and relationships, then decide whether broader impact, tests, or context are needed.

## Install And Try

Global tools:

```powershell
dotnet tool install --global navlyn
dotnet tool install --global navlyn-mcp
```

Repository-local tools for a team or agent workspace:

```powershell
dotnet new tool-manifest
dotnet tool install navlyn --version 0.5.0
dotnet tool install navlyn-mcp --version 0.5.0
dotnet tool restore
```

First useful calls in your repository:

```powershell
navlyn check --workspace path/to/YourRepo.slnx
navlyn resolve-target --workspace path/to/YourRepo.slnx --query PaymentService --assume-kind NamedType
navlyn references --workspace path/to/YourRepo.slnx --candidate-id sym:v1:... --group-by file --limit 50
navlyn context-pack --workspace path/to/YourRepo.slnx --candidate-id sym:v1:... --goal modify --profile compact
```

For MCP clients, start with the narrow reader profile:

```json
{
  "command": "navlyn-mcp",
  "args": ["--workspace", "path/to/navlyn.workspace.json", "--tool-profile", "reader"]
}
```

See [`docs/navlyn-client-setup.md`](docs/navlyn-client-setup.md) for copyable setup shapes.

## The Problem It Solves

A human reviewer can say: "before changing `PaymentService`, check what constructs it, which endpoints reach it, and which tests cover it."

An agent has to turn that into a brittle chain:

1. distinguish similar names, overloads, partial declarations, generated files, linked files, and target frameworks;
2. anchor follow-up calls to the same symbol instead of whichever text match appears first;
3. separate reads, writes, invocations, constructions, callers, callees, implementations, framework entrypoints, and tests;
4. collect a small reading queue rather than dumping the whole repository into the model;
5. keep JSON, paths, warnings, and exit behavior stable enough for automation.

Navlyn packages those steps as bounded Roslyn-backed facts. It does not ask an LLM to guess the codebase shape.

## Before / After

Without Navlyn, an agent might `rg PaymentService`, open the first plausible file, miss a second target framework or constructor path, and edit with the wrong context.

With Navlyn, the first step is a target envelope:

```json
{
  "confidence": "high",
  "candidateCount": 1,
  "selectedTarget": {
    "name": "PaymentService",
    "kind": "NamedType",
    "path": "src/Billing/PaymentService.cs",
    "line": 14,
    "column": 21
  },
  "candidateId": "sym:v1:...",
  "selector": {
    "project": "Billing.Api(net10.0)",
    "targetFramework": "net10.0"
  },
  "recommendedNextActions": [
    { "command": "definition", "candidateId": "sym:v1:..." },
    { "command": "references", "candidateId": "sym:v1:..." },
    { "command": "about", "candidateId": "sym:v1:..." }
  ]
}
```

That is not runtime proof. It is a stable source-level anchor the agent can reuse before it changes code.

## When To Use Navlyn

Use normal file reads and `rg` first when text is enough. Reach for Navlyn when the answer depends on C# semantics.

| Task | First Navlyn Call | Stop When |
| --- | --- | --- |
| Identify a symbol from an approximate name | `resolve-target --query PaymentService --assume-kind NamedType` | You have one high-confidence `candidateId`, or the candidates show the user must clarify. |
| Inspect one known C# file | `outline --file src/Billing/PaymentService.cs` or MCP `navlyn_file_outline` | The outline or returned `candidateId` is enough to answer the question. |
| Inspect relationships for a selected symbol | `references --candidate-id sym:v1:... --group-by file --limit 50` | The returned relationship facts answer the question. |
| Plan a non-trivial edit | `impact --candidate-id sym:v1:... --profile light` | You know the risky callers/files; use `context-pack` only if a reading queue is needed. |
| Review an actual Git diff | `review-diff --profile evidence` | You have changed-symbol, diagnostic, impact, and bounded warning facts. |

Do not run every Navlyn command as a checklist. Navlyn is strongest when each call answers one semantic question.

## What You Get

- **Exact anchors for fuzzy intent**: `resolve-target` and `find` return confidence, alternatives, reason codes, and stable `candidateId` values.
- **File-first investigation**: outline one C# file, inspect a selected source slice, then ask for edges only when needed.
- **Workspace facts**: projects, target frameworks, packages, diagnostics, test relationships, generated-code policy, and repository structure.
- **Source relationships**: definitions, references, callers, calls, implementations, type hierarchy, related files, entrypoints, and static impact.
- **.NET application evidence**: ASP.NET Core routes/auth, DI registrations and consumers, options/configuration, MediatR handlers, EF Core model facts, package usage, and framework entrypoints.
- **Review evidence**: changed symbols, diagnostics, static impact, public API facts, related tests, and bounded review-pack signals.
- **Automation discipline**: deterministic JSON on stdout, diagnostics on stderr, 1-based positions, repository-relative `/` paths where possible, and documented schemas/golden snapshots for high-value outputs.

## MCP For Agents

`navlyn-mcp` is the easiest way to give an agent C# semantic tools without teaching it to compose shell commands. The default `reader` profile exposes the narrow first-pass surface:

- `navlyn_resolve_target`
- `navlyn_file_outline`
- `navlyn_symbol_source`
- `navlyn_symbol_edges`
- `navlyn_about_symbol`
- `navlyn_workspace_summary`
- `navlyn_workspace_status`
- `navlyn_workspace_refresh`

Use `--tool-profile review` for real PR/diff review, `edit` for edit planning, and `full` only when a client needs the complete compatibility surface including `navlyn_batch`.

The MCP server keeps the boundary simple and auditable: read-only, local-only, no arbitrary file server, no edit tools, no shell execution. See [`docs/navlyn-mcp-server.md`](docs/navlyn-mcp-server.md).

## How Navlyn Fits

| Tool | Use It For | Navlyn's Job |
| --- | --- | --- |
| `rg` | Comments, strings, Markdown, config text, quick probes | C# symbol identity, project context, and source-level relationships. |
| LSP / IDE | Interactive editing, rename, go-to-definition, editor diagnostics | Stable JSON facts for agents, CI, and scripts. |
| Roslyn APIs / analyzers | Building custom compiler tooling | Ready-to-run read-only CLI/MCP workflows. |
| Editing-oriented MCP servers | Letting an agent inspect and modify code in one client | Client-neutral evidence with no edit surface. |
| CI review bots | Publishing comments or pass/fail checks | Review evidence packs a human or agent can inspect before deciding what to say. |
| Hosted code search | Cross-repository hosted indexing | Repository-local MSBuild/Roslyn facts without sending source to a hosted service. |

## Run The Repo Demo

From this repository after `dotnet restore navlyn.slnx`:

```powershell
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- resolve-target --workspace navlyn.slnx --project "Navlyn.CommandLine(net10.0)" --query CheckCommand --assume-kind NamedType --limit 5
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- symbol-source --workspace navlyn.slnx --project "Navlyn.CommandLine(net10.0)" --file Navlyn.CommandLine/Cli/Commands/CheckCommand.cs --line 6 --column 23 --view declaration
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- review-diff --workspace navlyn.slnx --base HEAD --head HEAD --profile compact --symbol-limit 3 --impact-limit 3 --diagnostic-limit 3 --related-test-limit 3
```

The first command anchors a fuzzy name to a C# symbol. The second opens bounded source for that exact symbol. The third shows the review envelope on an explicit Git range; replace the refs with PR refs or omit them on a dirty branch.

More walkthroughs: [`docs/navlyn-demo-walkthroughs.md`](docs/navlyn-demo-walkthroughs.md).

## Trust Boundaries

Navlyn reports bounded source-level evidence. It does not prove runtime behavior, execute tests, scan secrets, decide SemVer, publish review comments, or replace human judgment.

It also does not replace ordinary reading. If the question is about a comment, string, Markdown section, generated artifact, or non-C# file, use `rg` or read the file directly.

Known limits are documented in [`docs/navlyn-limitations.md`](docs/navlyn-limitations.md). Performance and warm-cache behavior are documented in [`docs/navlyn-performance.md`](docs/navlyn-performance.md).

## Documentation Map

- [`docs/navlyn-client-setup.md`](docs/navlyn-client-setup.md): install and client configuration.
- [`docs/navlyn-agent-recipes.md`](docs/navlyn-agent-recipes.md): task-oriented CLI/MCP recipes.
- [`docs/navlyn-mcp-server.md`](docs/navlyn-mcp-server.md): MCP profiles, tools, resources, freshness, and boundaries.
- [`docs/navlyn-cli-commands.md`](docs/navlyn-cli-commands.md): full CLI contract and JSON behavior.
- [`docs/navlyn-distribution.md`](docs/navlyn-distribution.md): release packaging and publish runbook.
- [`docs/navlyn-performance.md`](docs/navlyn-performance.md): performance model and measurement commands.

## License

Navlyn is licensed under the MIT License. See [`LICENSE`](LICENSE).

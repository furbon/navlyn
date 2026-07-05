# Navlyn

日本語: [`README_ja.md`](README_ja.md)

**Read-only C#/.NET semantic evidence for coding agents before they edit the wrong symbol.**

Coding agents are already good at changing code. The hard part in a C# repository is making sure the agent is changing the *right* code: the right overload, partial declaration, target framework, dependency-injection path, route handler, public API surface, and related tests.

Text search can find `PaymentService`. Navlyn asks Roslyn and MSBuild what `PaymentService` actually is in this workspace, returns deterministic JSON, and gives the agent a stable target to keep using while it investigates.

Think of Navlyn as a **C# edit preflight**:

1. resolve the exact symbol the user meant;
2. read only the bounded source and relationships that matter;
3. edit with normal tools outside Navlyn;
4. check the actual diff before the agent keeps going.

Navlyn ships as:

- `navlyn`: a CLI for shell, CI, scripts, and agent loops;
- `navlyn-mcp`: a standalone stdio MCP server for MCP-capable coding agents and editors.

Both are local, read-only, and facts-only. Navlyn does not edit files, run arbitrary shell commands, call the network, upload source, or maintain a hosted index.

## Why It Exists

Agent failures in mature C# codebases are often not "bad code generation" failures. They are **wrong-context failures**:

- a text match points at the wrong overload or a similarly named type;
- a multi-targeted project has different symbols under different target frameworks;
- a partial type, generated file, linked file, or conditional compilation branch changes what is real;
- the important edge is not textual at all, but DI registration, ASP.NET routing, MediatR handling, EF model shape, or a related test;
- the agent reads too much of the repository and loses the signal it needed.

Navlyn packages the boring, high-value investigation steps as Roslyn/MSBuild-backed JSON. It does not ask an LLM to guess the shape of your codebase.

## Quick Start

For a personal install:

```powershell
dotnet tool install --global navlyn
dotnet tool install --global navlyn-mcp
```

For a repository-local tool manifest that pins the version for a team, CI job, or agent workspace:

```powershell
dotnet new tool-manifest
dotnet tool install navlyn --version 0.6.0
dotnet tool install navlyn-mcp --version 0.6.0
dotnet tool restore
```

First useful commands in your own C# repository:

```powershell
navlyn doctor --workspace path/to/YourRepo.slnx
navlyn resolve-target --workspace path/to/YourRepo.slnx --query PaymentService --assume-kind NamedType
navlyn edit-preflight --workspace path/to/YourRepo.slnx --query PaymentService --assume-kind NamedType --goal modify --change-kind behavior

# Copy the candidateId from resolve-target or edit-preflight, then edit outside Navlyn:
navlyn post-edit-guard --workspace path/to/YourRepo.slnx --candidate-id sym:v1:... --fail-on-risk high
```

For MCP clients, start with the narrow reader profile:

```json
{
  "command": "navlyn-mcp",
  "args": ["--workspace", "path/to/navlyn.workspace.json", "--tool-profile", "reader"]
}
```

Copyable setup shapes live in [`docs/navlyn-client-setup.md`](docs/navlyn-client-setup.md). For a guided first run, use [`docs/navlyn-first-10-minutes.md`](docs/navlyn-first-10-minutes.md).

## The Core Loop

**1. Anchor the target.**

`resolve-target` turns fuzzy intent into one selected C# target when it can, plus alternatives and reason codes when it cannot. Follow-up commands can reuse the returned `candidateId`, so the investigation stays attached to the same Roslyn symbol instead of a later text match.

**2. Gather just enough evidence.**

Use `symbol-source`, `references`, `about`, `impact`, or `context-pack` depending on the question. `edit-preflight` wraps the common edit-planning path into one envelope: target anchor, bounded source, bounded context, related tests, confidence, known unknowns, and the next guard command.

**3. Verify the diff.**

After editing with your normal tools, `post-edit-guard`, `wrong-symbol-guard`, or `review-diff --profile evidence` compares the actual Git diff with the intended target and returns machine-readable risk signals.

Do not run every Navlyn command as a checklist. Navlyn is strongest when each call answers one semantic question.

## Before / After

Without Navlyn, an agent might run `rg PaymentService`, open the first plausible file, miss a second target framework or constructor path, and edit with the wrong context.

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

Use normal file reads and `rg` first when text is enough. Reach for Navlyn when C# semantics, project context, or diff evidence would change the answer.

| Task | First Navlyn Call | Stop When |
| --- | --- | --- |
| Identify a symbol from an approximate name | `resolve-target --query PaymentService --assume-kind NamedType` | You have one high-confidence `candidateId`, or the candidates show the user must clarify. |
| Inspect one known C# file | `outline --file src/Billing/PaymentService.cs` or MCP `navlyn_file_outline` | The outline or returned `candidateId` answers the question. |
| Inspect relationships for a selected symbol | `references --candidate-id sym:v1:... --group-by file --limit 50` | The returned relationship facts answer the question. |
| Plan a non-trivial edit | `edit-preflight --candidate-id sym:v1:... --goal modify` | The agent has an anchor, bounded evidence, known unknowns, and a post-edit guard command. |
| Review an actual Git diff | `review-diff --profile evidence` | Changed symbols, diagnostics, impact, related tests, and warnings are available. |
| Check an edit after the fact | `post-edit-guard --candidate-id sym:v1:... --fail-on-risk high` | The guard passes, or the mismatch is understood before more edits. |

## What You Get

- **Exact anchors for fuzzy intent**: `resolve-target` and `find` return confidence, alternatives, reason codes, and reusable `candidateId` values.
- **File-first semantic reading**: outline one C# file, inspect a selected source slice, then ask for edges only when needed.
- **Workspace facts**: projects, target frameworks, packages, diagnostics, test relationships, generated-code policy, and repository structure.
- **Source relationships**: definitions, references, usage kinds, callers, calls, implementations, type hierarchy, related files, entrypoints, and static impact.
- **.NET application evidence**: ASP.NET Core routes/auth, DI registrations and consumers, options/configuration, MediatR handlers, EF Core model facts, package usage, and framework entrypoints.
- **Review evidence**: changed symbols, diagnostics, static impact, public API facts, related tests, and bounded review-pack signals.
- **Agent guardrails**: `edit-preflight`, `post-edit-guard`, `wrong-symbol-guard`, change intent, handoff, and confidence ledgers for wrong-symbol avoidance.
- **Automation discipline**: deterministic JSON on stdout, diagnostics on stderr, 1-based positions, repository-relative `/` paths where possible, documented contracts, schemas, and golden snapshots for high-value outputs.

## MCP For Agents

`navlyn-mcp` gives an agent C# semantic tools without giving Navlyn an edit surface or asking the agent to compose shell commands. Start narrow and widen only when the task needs it.

| Profile | Use It For |
| --- | --- |
| `reader` | Setup checks, file outlines, symbol resolution, bounded source, and selected-symbol edges. This is the default. |
| `edit` | Pre-edit evidence, related tests, impact, bounded context, handoff/confidence packs, and post-edit guards. |
| `review` | Actual Git diffs, PR review evidence, public API facts, related tests, and guard checks. |
| `full` | Compatibility mode for clients that need every MCP tool, including `navlyn_batch`. |

The boundary stays deliberately simple: stdio only, read-only, local-only, no arbitrary file server, no edit tools, no shell execution. See [`docs/navlyn-mcp-server.md`](docs/navlyn-mcp-server.md).

## How Navlyn Fits

| Tool | Use It For | Navlyn's Job |
| --- | --- | --- |
| `rg` and file reads | Comments, strings, Markdown, config, quick text probes | C# symbol identity, project context, and source-level relationships when text is ambiguous. |
| LSP / IDE | Interactive editing, rename, go-to-definition, editor diagnostics | Stable JSON facts for agents, CI, scripts, and MCP clients. |
| Roslyn APIs / analyzers | Building custom compiler tooling | Ready-to-run read-only workflows without writing an analyzer. |
| Generic code-search MCP | Cross-language or text-level repository search | C# MSBuild/Roslyn facts: target frameworks, symbols, references, DI, routes, tests, and review evidence. |
| Editing-oriented MCP servers | Inspecting and modifying code from one client | Client-neutral evidence with no edit surface. |
| CI review bots | Publishing comments or pass/fail checks | Local review facts a human or agent can inspect before deciding what to say. |
| Hosted code search | Cross-repository hosted indexing | Repository-local analysis without sending source to a hosted service. |

## Run The Repo Demo

From this repository after `dotnet restore navlyn.slnx`:

```powershell
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- resolve-target --workspace navlyn.slnx --project "Navlyn.CommandLine(net10.0)" --query CheckCommand --assume-kind NamedType --limit 5
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- edit-preflight --workspace navlyn.slnx --project "Navlyn.CommandLine(net10.0)" --query CheckCommand --assume-kind NamedType --goal modify --change-kind behavior
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- review-diff --workspace navlyn.slnx --base HEAD --head HEAD --profile compact --symbol-limit 3 --impact-limit 3 --diagnostic-limit 3 --related-test-limit 3
```

The first command anchors a fuzzy name to a C# symbol. The second builds edit-preflight evidence for that target. The third shows the review envelope for an explicit Git range; replace the refs with PR refs, or omit them on a dirty branch.

More walkthroughs: [`docs/navlyn-demo-walkthroughs.md`](docs/navlyn-demo-walkthroughs.md).

## Trust Boundaries

Navlyn reports bounded source-level evidence. It does not prove runtime behavior, execute tests, scan secrets, decide SemVer, publish review comments, or replace human judgment.

It also does not replace ordinary reading. If the question is about a comment, string, Markdown section, generated artifact, or non-C# file, use `rg` or read the file directly.

Known limits are documented in [`docs/navlyn-limitations.md`](docs/navlyn-limitations.md). Performance and warm-cache behavior are documented in [`docs/navlyn-performance.md`](docs/navlyn-performance.md).

## Documentation Map

- [`docs/navlyn-first-10-minutes.md`](docs/navlyn-first-10-minutes.md): shortest path to a successful first CLI/MCP run.
- [`docs/navlyn-client-setup.md`](docs/navlyn-client-setup.md): install shapes and client configuration.
- [`docs/navlyn-agent-recipes.md`](docs/navlyn-agent-recipes.md): task-oriented CLI/MCP recipes.
- [`docs/navlyn-positioning.md`](docs/navlyn-positioning.md): category positioning against search, LSP, analyzers, MCP servers, and review bots.
- [`docs/navlyn-mcp-server.md`](docs/navlyn-mcp-server.md): MCP profiles, tools, resources, freshness, and boundaries.
- [`docs/navlyn-cli-commands.md`](docs/navlyn-cli-commands.md): full public CLI contract and JSON behavior.
- [`docs/navlyn-performance.md`](docs/navlyn-performance.md): performance model and measurement commands.
- [`docs/navlyn-development-workflow.md`](docs/navlyn-development-workflow.md): contributor validation, scripts, and workflow guidance.

## License

Navlyn is licensed under the MIT License. See [`LICENSE`](LICENSE).

# Navlyn

Japanese: [`README_ja.md`](README_ja.md)

**Navlyn is a semantic investigation layer for C#/.NET coding agents.**

Coding agents can search text. They cannot safely infer overloads, target frameworks, dependency-injection registrations, route handlers, public API changes, or related tests from `rg` output alone. Navlyn gives them local Roslyn/MSBuild-backed facts through a deterministic JSON CLI and a read-only stdio MCP server.

If you are deciding whether agents can work in a non-trivial C# codebase, Navlyn's job is to reduce wrong-symbol and missing-context failures before they become edits.

Use Navlyn when the question is not "where does this string appear?" but "what C# symbol is this, how is it reached, what would change if we edit it, and what context should the agent read before touching code?"

## One-Minute Demo

From this repository after `dotnet restore navlyn.slnx`:

```powershell
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- resolve-target --workspace navlyn.slnx --project "Navlyn.CommandLine(net10.0)" --query CheckCommand --assume-kind NamedType --limit 5
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- symbol-source --workspace navlyn.slnx --project "Navlyn.CommandLine(net10.0)" --file Navlyn.CommandLine/Cli/Commands/CheckCommand.cs --line 6 --column 23 --view declaration
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- review-diff --workspace navlyn.slnx --base HEAD --head HEAD --profile compact --symbol-limit 3 --impact-limit 3 --diagnostic-limit 3 --related-test-limit 3
```

The first command anchors a fuzzy name to a C# symbol. The second opens bounded source for that exact symbol. The third shows the review envelope on an explicit Git range; replace `HEAD --head HEAD` with PR refs or omit the refs on a dirty branch.

## Before / After

Before Navlyn, an agent might `rg CheckCommand`, open the first file that looks right, miss a second project context, and edit without checking construction sites or related tests.

After Navlyn, the same investigation starts with Roslyn-backed evidence:

```json
{
  "confidence": "high",
  "candidateCount": 1,
  "selectedTarget": {
    "name": "CheckCommand",
    "kind": "NamedType",
    "path": "Navlyn.CommandLine/Cli/Commands/CheckCommand.cs",
    "line": 6,
    "column": 23
  },
  "candidateId": "sym:v1:...",
  "selector": {
    "project": "Navlyn.CommandLine(net10.0)",
    "targetFramework": "net10.0"
  },
  "recommendedNextActions": [
    { "command": "definition", "candidateId": "sym:v1:..." },
    { "command": "references", "candidateId": "sym:v1:..." },
    { "command": "about", "candidateId": "sym:v1:..." }
  ]
}
```

That does not prove runtime behavior. It gives the agent a stable source-level anchor before it edits.

## What It Gives You

- **Workspace facts when they matter**: projects, target frameworks, package references, test relationships, diagnostics, and repository structure.
- **Fuzzy intent with exact anchors**: `find` resolves approximate symbol names into ranked candidates with confidence, reason codes, alternatives, `candidateId`, and next actions.
- **Precise navigation after discovery**: `candidateId` flows into `symbol-source`, `definition`, `references`, `callers`, `calls`, `implementations`, `type-hierarchy`, `symbol-info`, and MCP tools such as `navlyn_symbol_source`, `navlyn_symbol_edges`, and `navlyn_exact_navigation`.
- **Agent-sized context**: `context-pack` ranks bounded reading material for `review`, `modify`, and `understand` goals instead of dumping raw source.
- **Review evidence**: diff commands return changed symbols, diagnostics, static impact, public API changes, related tests, and review-pack signals without writing review comments for you.
- **.NET application facts**: ASP.NET Core route/auth facts, Microsoft.Extensions.DependencyInjection registrations and impact, options/configuration facts, MediatR handlers, EF Core model facts, package usage, framework entrypoints, and related test candidates.
- **Automation-safe output**: results go to stdout as deterministic JSON, diagnostics go to stderr, paths are repository-relative where possible, and the MCP server is facts-only and read-only.

Navlyn is not a replacement for `rg`, your editor, tests, a runtime tracer, or a security scanner. Use text search for comments, strings, docs, and non-C# files. Use Navlyn when C# semantic identity matters.

## Why It Exists

A human can say: "before changing `PaymentService`, check the impact and the tests."

An agent has to turn that into a long chain of fragile operations:

1. find the right type among similar names, projects, target frameworks, partial declarations, and overloads;
2. get exact definitions, references, callers, callees, implementations, and framework entrypoints;
3. separate reads, writes, invocations, construction, inheritance, tests, and production usage;
4. collect the small set of files worth reading before editing;
5. preserve stdout/stderr discipline so automation can trust the result.

Navlyn packages those steps as agent-oriented workflows over Roslyn facts. The important distinction is that Navlyn does not ask an LLM to guess the codebase shape. It returns evidence the agent and human reviewer can inspect.

## Decision Guide

Use normal file reads and `rg` first when text is enough. Reach for Navlyn when the answer depends on C# semantic identity, overload binding, project context, source-level relationships, or bounded review evidence.

Three useful starting points:

| Task | Minimal First Call | Stop When |
| --- | --- | --- |
| Identify a C# symbol from a name | `navlyn resolve-target --workspace navlyn.slnx --query PaymentService --assume-kind NamedType` | You have one high-confidence `candidateId`, or the candidates show the user must clarify. |
| Inspect relationships for a known symbol | `navlyn references --workspace navlyn.slnx --candidate-id sym:v1:... --limit 50 --group-by file --group-by usage-kind` | The returned references answer the question; escalate to `impact` only for edit risk. |
| Review an actual Git diff | `navlyn review-diff --workspace navlyn.slnx --profile evidence` | You have changed-symbol and impact facts; ask for `context-pack --diff` only if more reading material is needed. |

Do not use Navlyn for comments, strings, Markdown, generated artifacts, non-C# files, or questions where a normal file read already answers the user. Do not run review/test/context tools as a default checklist. Escalate one step at a time.

## Common Flows

| Question | Start With | Then Use |
| --- | --- | --- |
| What is in this workspace? | `repo-graph --profile compact` when project/package/test context matters | `diagnostics`, `overview` |
| Which symbol did the user mean? | `resolve-target --query PaymentService` | reuse `candidateId` or run `find` for more candidates |
| What should I read before editing? | `references` or `impact` for the selected symbol | `context-pack --goal modify` only when a bounded reading queue is needed |
| What does this PR affect? | `review-diff --profile evidence` | `tests-for-diff`, `public-api-diff`, `review-pack` when those facts are relevant |
| How is this reached at runtime boundaries? | `entrypoints --framework-aware` | `route-map`, `where-handled`, `di-impact` |
| How should an MCP client ask this? | `navlyn_resolve_target` | `navlyn_exact_navigation`, `navlyn_context_pack` |

For repeated MCP or batch calls, use `navlyn_batch` only after deciding that several supported facts are needed from the same workspace. It is an optimization, not a first-step checklist.

## Quick Start

Navlyn tool packages target .NET 8 and .NET 10 and load `.code-workspace`, `.slnx`, `.sln`, and `.csproj` workspaces through MSBuild/Roslyn. Use `--workspace auto` only when the repository has one clear top-level workspace candidate.

From this repository:

```powershell
dotnet restore navlyn.slnx
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- check --workspace navlyn.slnx
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- repo-graph --workspace navlyn.slnx --profile compact
```

From a NuGet source or internal feed that contains the Navlyn tool packages:

```powershell
dotnet tool install --global navlyn
dotnet tool install --global navlyn-mcp

navlyn check --workspace path/to/YourRepo.slnx
navlyn repo-graph --workspace path/to/YourRepo.slnx --profile compact
```

For a repository-local team install, commit a .NET tool manifest:

```powershell
dotnet new tool-manifest
dotnet tool install navlyn --version 0.5.0
dotnet tool install navlyn-mcp --version 0.5.0
dotnet tool restore

dotnet tool run navlyn -- check --workspace path/to/YourRepo.slnx
```

See [`docs/navlyn-client-setup.md`](docs/navlyn-client-setup.md), [`examples/install/dotnet-tools.json`](examples/install/dotnet-tools.json), and [`examples/install/vscode-mcp.json`](examples/install/vscode-mcp.json) for copyable CLI and MCP configuration shapes.

Release packaging details live in [`docs/navlyn-distribution.md`](docs/navlyn-distribution.md). Reproducible demos live in [`docs/navlyn-demo-walkthroughs.md`](docs/navlyn-demo-walkthroughs.md). Local benchmark and eval guidance lives in [`docs/navlyn-performance.md`](docs/navlyn-performance.md) and [`docs/evals/tool-selection.md`](docs/evals/tool-selection.md). Known limits are documented in [`docs/navlyn-limitations.md`](docs/navlyn-limitations.md).

## CLI Example

Start with a target envelope, then switch to exact facts anchored by `candidateId`:

```powershell
navlyn resolve-target --workspace navlyn.slnx --query CheckCommand --assume-kind NamedType
navlyn about --workspace navlyn.slnx --candidate-id sym:v1:...
navlyn references --workspace navlyn.slnx --candidate-id sym:v1:... --usage-kind invoke --usage-kind construct --group-by file --group-by usage-kind --limit 50
navlyn impact --workspace navlyn.slnx --candidate-id sym:v1:... --depth 2
navlyn context-pack --workspace navlyn.slnx --candidate-id sym:v1:... --goal modify --change-kind signature --profile compact
```

Ambiguous fuzzy queries do not silently merge plausible symbols. Navlyn returns candidates, alternatives, confidence, and next actions so the caller can correct course.

## Review Example

Navlyn review commands are evidence providers. They do not replace reviewers, generate final review prose, or claim complete runtime reachability.

```powershell
navlyn review-diff --workspace navlyn.slnx --profile evidence
navlyn tests-for-diff --workspace navlyn.slnx --profile compact
navlyn public-api-diff --workspace navlyn.slnx --base main --profile evidence
navlyn review-pack --workspace navlyn.slnx --pack async --pack security --profile evidence
navlyn context-pack --workspace navlyn.slnx --diff --goal review --profile compact
```

Use `compact` for first scans, `evidence` for review and CI facts, and `full` when downstream tooling needs the richest JSON shape.

## MCP Server

`navlyn-mcp` exposes a focused read-only MCP surface backed by the same Navlyn engine and CLI JSON contract. Use it when an MCP-capable client should ask semantic C# questions without composing shell commands. Installing `navlyn-mcp` is enough for MCP use; a separate `navlyn` CLI installation is not required.

Typical installed-server configuration:

```json
{
  "command": "navlyn-mcp",
  "args": ["--workspace", "path/to/YourRepo.slnx"]
}
```

For repositories with exactly one top-level workspace candidate, the server can discover it:

```json
{
  "command": "navlyn-mcp",
  "args": ["--workspace", "auto"]
}
```

The MCP server exposes high-level tools such as `navlyn_workspace_summary`, `navlyn_resolve_target`, `navlyn_find_symbol`, `navlyn_file_outline`, `navlyn_symbol_source`, `navlyn_symbol_edges`, `navlyn_exact_navigation`, `navlyn_review_diff`, `navlyn_context_pack`, and `navlyn_batch`, plus bounded resources and prompts. Tool descriptions are written to be need-triggered: use file/source/edge tools for one known file or symbol, workspace summary for project context, review diff only for an actual Git diff, context pack as escalation, and batch only when multiple facts are already needed. See [`docs/navlyn-mcp-server.md`](docs/navlyn-mcp-server.md).

## How Navlyn Fits

| Tool | Use It For | Navlyn's Different Job |
| --- | --- | --- |
| `rg` | Comments, strings, docs, non-C# files, quick text probes | Local C# symbol identity, `candidateId`, references, call relationships, and source-level impact |
| LSP / IDE | Interactive editing, rename, go-to-definition, diagnostics in an editor | Deterministic JSON facts for agents, CI, and scripts |
| Roslyn APIs or refactoring tools | Building analyzers, refactorings, or IDE features | Ready-to-run read-only CLI/MCP workflows with bounded output |
| Roslyn MCP or editor agents | Interactive semantic assistance inside one client | Client-neutral envelopes, stable command facts, and no editing surface |
| Code search assistants | Cross-repository search, hosted indexing, broad discovery | Repository-local MSBuild/Roslyn facts without sending source to a hosted index |
| CI review bots | Publishing review comments or pass/fail checks | Review evidence packs that a human or agent can inspect before deciding what to say |

Navlyn is not an editor, refactoring engine, test runner, runtime tracer, security scanner, package compatibility oracle, or arbitrary repository file server. Its boundary is read-only, local, source-level evidence.

## Output Contract

- stdout is reserved for command result JSON.
- stderr is reserved for diagnostics, errors, warnings, and progress.
- Automation-facing output is deterministic.
- Paths are repository-relative where possible and use `/` separators in JSON output.
- User-facing line and column values are 1-based.
- Static impact, review, framework, DI, EF, configuration, and package facts are bounded source-level evidence, not runtime proof.

The full command surface, options, JSON shapes, error behavior, and command boundaries are documented in [`docs/navlyn-cli-commands.md`](docs/navlyn-cli-commands.md). Practical agent flows live in [`docs/navlyn-agent-recipes.md`](docs/navlyn-agent-recipes.md).

## License

Navlyn is licensed under the MIT License. See [`LICENSE`](LICENSE).

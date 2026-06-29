# Navlyn

Japanese: [`README_ja.md`](README_ja.md)

**Navlyn is a semantic investigation layer for C#/.NET coding agents.**

Coding agents can search text. They cannot safely infer overloads, target frameworks, dependency-injection registrations, route handlers, public API changes, or related tests from `rg` output alone. Navlyn gives them local Roslyn/MSBuild-backed facts through a deterministic JSON CLI and a read-only stdio MCP server.

If you are deciding whether agents can work in a non-trivial C# codebase, Navlyn's job is to reduce wrong-symbol and missing-context failures before they become edits.

Use Navlyn when the question is not "where does this string appear?" but "what C# symbol is this, how is it reached, what would change if we edit it, and what context should the agent read before touching code?"

## Before / After

Before Navlyn, an agent might `rg CheckCommand`, open the first file that looks right, miss a second project context, and edit without checking construction sites or related tests.

After Navlyn, the same investigation starts with Roslyn-backed evidence:

```json
{
  "confidence": "high",
  "candidateCount": 1,
  "selectedCandidate": {
    "name": "CheckCommand",
    "kind": "NamedType",
    "candidateId": "sym:v1:b80453171915c2303ccb9b591fdcebb6",
    "selector": {
      "project": "navlyn",
      "path": "navlyn/Cli/Commands/CheckCommand.cs",
      "line": 6,
      "column": 23
    }
  },
  "nextActions": [
    { "command": "definition", "candidateId": "sym:v1:b80453171915c2303ccb9b591fdcebb6" },
    { "command": "references", "candidateId": "sym:v1:b80453171915c2303ccb9b591fdcebb6" },
    { "command": "about", "candidateId": "sym:v1:b80453171915c2303ccb9b591fdcebb6" }
  ]
}
```

That does not prove runtime behavior. It gives the agent a stable source-level anchor before it edits.

## What It Gives You

- **A stable first scan**: projects, target frameworks, package references, test relationships, diagnostics, and repository structure.
- **Fuzzy intent with exact anchors**: `find` resolves approximate symbol names into ranked candidates with confidence, reason codes, alternatives, `candidateId`, and next actions.
- **Precise navigation after discovery**: `candidateId` flows into `definition`, `references`, `callers`, `calls`, `implementations`, `type-hierarchy`, `symbol-info`, and MCP `navlyn_exact_navigation`.
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

## Common Flows

| Question | Start With | Then Use |
| --- | --- | --- |
| What is in this workspace? | `repo-graph --profile compact` | `diagnostics`, `overview` |
| Which symbol did the user mean? | `resolve-target --query PaymentService` | reuse `candidateId` or run `find` for more candidates |
| What should I read before editing? | `context-pack --goal modify` | `references`, `impact`, `tests-for-symbol` |
| What does this PR affect? | `review-diff --profile evidence` | `tests-for-diff`, `public-api-diff`, `review-pack` |
| How is this reached at runtime boundaries? | `entrypoints --framework-aware` | `route-map`, `where-handled`, `di-impact` |
| How should an MCP client ask this? | `navlyn_resolve_target` | `navlyn_exact_navigation`, `navlyn_context_pack` |

## Golden Path

For a first agent pass in a repository, copy this shape and replace the workspace and query:

```powershell
navlyn repo-graph --workspace navlyn.slnx --profile compact
navlyn resolve-target --workspace navlyn.slnx --query CheckCommand --assume-kind NamedType --limit 10
navlyn context-pack --workspace navlyn.slnx --query CheckCommand --assume-kind NamedType --goal modify --profile compact --budget-tokens 8000
navlyn impact --workspace navlyn.slnx --candidate-id sym:v1:... --depth 2
navlyn tests-for-symbol --workspace navlyn.slnx --candidate-id sym:v1:... --profile compact
```

For repeated MCP or batch calls, put the first scan and follow-up facts into `navlyn_batch` to avoid paying the workspace load cost for every small question.

## Quick Start

Navlyn targets .NET 10 and loads `.slnx`, `.sln`, and `.csproj` workspaces through MSBuild/Roslyn.

From this repository:

```powershell
dotnet restore navlyn.slnx
dotnet run --no-launch-profile --project navlyn -- check --workspace navlyn.slnx
dotnet run --no-launch-profile --project navlyn -- repo-graph --workspace navlyn.slnx --profile compact
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
dotnet tool install navlyn --version 0.4.0
dotnet tool install navlyn-mcp --version 0.4.0
dotnet tool restore

dotnet tool run navlyn -- check --workspace path/to/YourRepo.slnx
```

See [`examples/install/dotnet-tools.json`](examples/install/dotnet-tools.json) and [`examples/install/vscode-mcp.json`](examples/install/vscode-mcp.json) for copyable local-tool and VS Code MCP configuration shapes.

See [`docs/navlyn-distribution.md`](docs/navlyn-distribution.md) for packaging and release workflow details.
Discovery-channel preparation is tracked in [`docs/navlyn-discovery-channels.md`](docs/navlyn-discovery-channels.md).
Three reproducible walkthroughs live in [`docs/navlyn-demo-walkthroughs.md`](docs/navlyn-demo-walkthroughs.md).
Performance notes and measurement commands are in [`docs/navlyn-performance.md`](docs/navlyn-performance.md).

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

The MCP server exposes high-level tools such as `navlyn_workspace_summary`, `navlyn_resolve_target`, `navlyn_find_symbol`, `navlyn_exact_navigation`, `navlyn_review_diff`, `navlyn_context_pack`, and `navlyn_batch`, plus bounded resources and prompts. See [`docs/navlyn-mcp-server.md`](docs/navlyn-mcp-server.md).

## How Navlyn Fits

| Tool | Use It For | Navlyn's Different Job |
| --- | --- | --- |
| `rg` | Comments, strings, docs, non-C# files, quick text probes | C# symbol identity, `candidateId`, references, call relationships, and source-level impact |
| LSP / IDE | Interactive editing, rename, go-to-definition, diagnostics in an editor | Deterministic JSON facts for agents, CI, and scripts |
| Roslyn APIs / refactoring tools | Building custom analyzers, refactorings, or IDE features | Ready-to-run CLI/MCP workflows with bounded output |
| CI review bots | Publishing review comments or pass/fail checks | Review evidence packs that a human or agent can inspect |

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

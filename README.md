# Navlyn

日本語: [`README_ja.md`](README_ja.md)

Navlyn gives AI agents and automation compiler-backed facts about C#/.NET repositories.

It is both a deterministic JSON CLI and a read-only stdio MCP server. Use it when an agent needs to answer questions such as "what symbol is this?", "where is it used?", "what changes in this PR matter?", "what tests look related?", or "what context should I read before editing?" without guessing from raw text alone.

Navlyn is not a replacement for `rg`, your editor, or a full runtime analyzer. Use text search for comments, strings, docs, and non-C# files. Use Navlyn for Roslyn-backed facts about C# symbols, source locations, references, call relationships, diagnostics, repository structure, diffs, tests, public API changes, framework entrypoints, dependency injection registrations, and bounded agent context.

## Why Navlyn

Coding agents are good at intent, but brittle at long chains of exact navigation commands. A human can say "look at the impact of changing `WidgetService`"; an agent then has to find the right type, disambiguate overloads and projects, collect references, inspect callers, follow entrypoints, find related tests, and keep the result small enough to use.

Navlyn turns those investigation steps into stable, local, machine-readable facts:

- **Fuzzy intent, deterministic selection**: approximate symbol queries return ranked candidates, confidence, reason codes, alternatives, opaque `candidateId` values, and next actions.
- **Task-shaped workflows**: `about`, `related`, `impact`, `entrypoints`, `review-diff`, and `context-pack` answer investigation questions directly instead of exposing only editor-style primitives.
- **Evidence-first review data**: diff workflows return changed symbols, diagnostics, static impact, public API facts, related test candidates, and review-pack signals without generating review prose.
- **Token-aware context retrieval**: `context-pack` ranks bounded reading material for `review`, `modify`, and `understand` workflows.
- **.NET-specific intelligence**: repository/project graphs, framework-aware entrypoints, test discovery, public API diffing, and Microsoft.Extensions.DependencyInjection facts are first-class.
- **Agent-safe integration**: command results go to stdout as deterministic JSON, diagnostics go to stderr, paths are repository-relative where possible, and the MCP server is read-only.

## How It Fits

Navlyn has three layers. Most agents should start from the top and drop down only when they need precision.

| Layer | Use It For | Examples |
| --- | --- | --- |
| MCP tools | Agent clients that speak MCP and need a small, high-level tool surface | `navlyn_find_symbol`, `navlyn_about_symbol`, `navlyn_review_diff`, `navlyn_context_pack` |
| Investigation workflows | Human or scripted CLI workflows that start from a symbol, diff, or task | `find`, `about`, `related`, `impact`, `entrypoints`, `review-diff`, `context-pack` |
| Roslyn primitives | Exact source-position navigation and low-level semantic facts | `definition`, `references`, `implementations`, `type-hierarchy`, `callers`, `calls`, `symbol-info` |

## Quick Start

Navlyn targets .NET 10 and loads `.slnx`, `.sln`, and `.csproj` workspaces through MSBuild/Roslyn.

From this repository:

```powershell
dotnet restore navlyn.slnx
dotnet run --no-launch-profile --project navlyn -- check --workspace navlyn.slnx
dotnet run --no-launch-profile --project navlyn -- repo-graph --workspace navlyn.slnx --profile compact
```

When the packages are available from your configured NuGet sources, install them with standard .NET tool commands:

```powershell
dotnet tool install --global navlyn
dotnet tool install --global navlyn-mcp

navlyn check --workspace path/to/YourRepo.slnx
navlyn repo-graph --workspace path/to/YourRepo.slnx --profile compact
```

## Agent Investigation

Start with fuzzy discovery when you know the symbol intent but not the exact file, project, overload, or column.

```powershell
navlyn find --workspace navlyn.slnx --query CheckCommand --assume-kind NamedType
navlyn about --workspace navlyn.slnx --query CheckCommand --assume-kind NamedType
navlyn related --workspace navlyn.slnx --query CheckCommand --assume-kind NamedType --limit 30
navlyn impact --workspace navlyn.slnx --query CheckCommand --assume-kind NamedType --depth 2
```

For longer workflows, use a `candidateId` returned by `find` so later calls keep referring to the same declaration:

```powershell
navlyn about --workspace navlyn.slnx --candidate-id sym:v1:...
navlyn context-pack --workspace navlyn.slnx --candidate-id sym:v1:... --goal modify --profile compact
```

Fuzzy commands do not silently merge plausible symbols. Ambiguous queries return candidates and alternatives so an agent can self-correct.

## Review And Context

Navlyn's review commands are facts providers. They do not replace reviewers, generate comments, or claim complete runtime reachability.

```powershell
navlyn review-diff --workspace navlyn.slnx --profile evidence
navlyn context-pack --workspace navlyn.slnx --diff --goal review --profile compact
navlyn tests-for-diff --workspace navlyn.slnx --profile compact
navlyn public-api-diff --workspace navlyn.slnx --base main --profile evidence
navlyn review-pack --workspace navlyn.slnx --pack async --pack security --profile evidence
```

Use `compact` for first scans, `evidence` for review/CI facts, and `full` when you want the richest contract shape.

## MCP Server

The `navlyn-mcp` tool exposes a focused read-only MCP surface backed by the CLI contract. Use it when an MCP-capable agent client should ask semantic C# questions without shelling out to individual CLI commands.

Typical installed-server configuration:

```json
{
  "command": "navlyn-mcp",
  "args": ["--workspace", "path/to/YourRepo.slnx"]
}
```

See [`docs/navlyn-mcp-server.md`](docs/navlyn-mcp-server.md) for setup, tool selection guidance, result envelopes, and boundaries.

## Output Contract

- stdout is reserved for command result JSON.
- stderr is reserved for diagnostics, errors, warnings, and progress.
- Automation-facing output is deterministic.
- Paths are repository-relative where possible and use `/` separators in JSON output.
- User-facing line and column values are 1-based.

The full command surface, options, JSON shapes, error behavior, and command boundaries are documented in [`docs/navlyn-cli-commands.md`](docs/navlyn-cli-commands.md). Agent recipes live in [`docs/navlyn-agent-recipes.md`](docs/navlyn-agent-recipes.md).

## License

Navlyn is licensed under the MIT License. See [`LICENSE`](LICENSE).

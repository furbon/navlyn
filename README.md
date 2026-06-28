# Navlyn

Japanese: [`README_ja.md`](README_ja.md)

**Navlyn is a semantic investigation layer for C#/.NET coding agents.**

Coding agents can search text. They cannot safely infer overloads, target frameworks, dependency-injection registrations, route handlers, public API changes, or related tests from `rg` output alone. Navlyn gives them local Roslyn/MSBuild-backed facts through a deterministic JSON CLI and a read-only stdio MCP server.

If you are deciding whether agents can work in a non-trivial C# codebase, Navlyn's job is to reduce wrong-symbol and missing-context failures before they become edits.

Use Navlyn when the question is not "where does this string appear?" but "what C# symbol is this, how is it reached, what would change if we edit it, and what context should the agent read before touching code?"

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
| Which symbol did the user mean? | `find --query PaymentService` | reuse `candidateId` |
| What should I read before editing? | `context-pack --goal modify` | `references`, `impact`, `tests-for-symbol` |
| What does this PR affect? | `review-diff --profile evidence` | `tests-for-diff`, `public-api-diff`, `review-pack` |
| How is this reached at runtime boundaries? | `entrypoints --framework-aware` | `route-map`, `where-handled`, `di-impact` |
| How should an MCP client ask this? | `navlyn_find_symbol` | `navlyn_exact_navigation`, `navlyn_context_pack` |

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

See [`docs/navlyn-distribution.md`](docs/navlyn-distribution.md) for packaging and release workflow details.
Performance notes and measurement commands are in [`docs/navlyn-performance.md`](docs/navlyn-performance.md).

## CLI Example

Start fuzzy, then switch to exact facts anchored by `candidateId`:

```powershell
navlyn find --workspace navlyn.slnx --query CheckCommand --assume-kind NamedType
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

`navlyn-mcp` exposes a focused read-only MCP surface backed by the same CLI contract. Use it when an MCP-capable client should ask semantic C# questions without composing shell commands.

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

The MCP server exposes high-level tools such as `navlyn_workspace_summary`, `navlyn_find_symbol`, `navlyn_exact_navigation`, `navlyn_review_diff`, `navlyn_context_pack`, and `navlyn_batch`, plus bounded resources and prompts. See [`docs/navlyn-mcp-server.md`](docs/navlyn-mcp-server.md).

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

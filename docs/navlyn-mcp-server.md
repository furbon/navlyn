# Navlyn MCP Server

Navlyn includes a read-only stdio MCP server in the separate `navlyn.Mcp` project. It gives MCP clients a small, high-level tool surface for C#/.NET repository investigation while preserving the same deterministic CLI JSON contract underneath.

The server is intentionally facts-only. It does not edit files, execute arbitrary shell commands, call arbitrary Navlyn commands, access the network, or change the configured workspace. Successful tool calls return a Navlyn MCP result envelope with the CLI JSON under `result`; the inner result shapes remain documented in [`navlyn-cli-commands.md`](navlyn-cli-commands.md).

## When To Use It

Use `navlyn-mcp` when an agent client should ask semantic C# questions through MCP instead of composing CLI commands itself:

- start a repository investigation with project, package, target framework, and test relationship facts;
- resolve approximate symbol names into deterministic candidates and `candidateId` values;
- gather selected-symbol summaries, related files, static impact, and entrypoint chains;
- collect PR or working-tree review facts from a Git diff;
- request bounded reading material before review, modification, or explanation work;
- run several batch-supported facts in one MCP tool call.

Use `rg`, normal file reads, or editor tools for comments, prose docs, strings, generated artifacts, and non-C# content. Navlyn's MCP server is a semantic C#/.NET facts provider, not a general repository search server.

## Starting The Server

Installed .NET tool command shape:

```json
{
  "command": "navlyn-mcp",
  "args": ["--workspace", "path/to/YourRepo.slnx"]
}
```

For local repositories with exactly one top-level workspace candidate, `auto` can discover the workspace:

```json
{
  "command": "navlyn-mcp",
  "args": ["--workspace", "auto"]
}
```

Local development from this repository:

```powershell
dotnet build navlyn.slnx
dotnet run --no-launch-profile --project navlyn.Mcp -- --workspace navlyn.slnx --navlyn-executable dotnet --navlyn-arg navlyn/bin/Debug/net10.0/navlyn.dll
```

Equivalent MCP client configuration for local development:

```json
{
  "command": "dotnet",
  "args": [
    "navlyn.Mcp/bin/Debug/net10.0/navlyn.Mcp.dll",
    "--workspace",
    "navlyn.slnx",
    "--navlyn-executable",
    "dotnet",
    "--navlyn-arg",
    "navlyn/bin/Debug/net10.0/navlyn.dll"
  ]
}
```

Local package smoke testing uses the standard .NET tool flow:

```powershell
./scripts/test-package-install.ps1
```

## Server Options

- `--workspace <path|auto>`: required `.slnx`, `.sln`, or `.csproj` path, or `auto` to discover one top-level candidate from the working directory/repository root. Tool calls are locked to the resolved workspace.
- `--navlyn-executable <command>`: Navlyn CLI executable. Defaults to `navlyn`.
- `--navlyn-arg <arg>`: prefix argument passed before the CLI command. Repeat for local development with `dotnet navlyn.dll`.
- `--working-directory <path>`: child process working directory. Defaults to the repository root when found.
- `--timeout-ms <number>`: per-tool timeout. Defaults to `120000`.
- `--max-json-chars <number>`: maximum CLI stdout JSON size accepted by the wrapper. Defaults to `4000000`.

The server writes MCP protocol messages to stdout. Logs and diagnostics go to stderr.

`--workspace auto` considers top-level `.slnx`, then `.sln`, then `.csproj` files. It chooses a single candidate at the best available priority and fails safely if none exist or if multiple best-priority candidates exist. In multi-solution repositories, pass `--workspace` explicitly.

## Tool Selection

The MCP surface is deliberately small. Prefer the specific high-level tool for the investigation task, and use `navlyn_batch` when the desired command is batch-supported but not exposed as a dedicated MCP tool.

| Tool | Use It For | CLI Backing |
| --- | --- | --- |
| `navlyn_workspace_summary` | First scan: projects, target frameworks, references, packages, test relationships, MSBuild file facts | `repo-graph` |
| `navlyn_find_symbol` | Approximate symbol names, deterministic candidates, `candidateId` selection | `find` |
| `navlyn_about_symbol` | Selected-symbol definition, member outline, reference summary, shallow relations | `about` |
| `navlyn_related_files` | File-first investigation map around a selected symbol | `related` |
| `navlyn_impact` | Static source impact before editing or reviewing a symbol | `impact` |
| `navlyn_entrypoints` | Static caller chains or framework-aware entrypoint discovery | `entrypoints`, `framework-entrypoints` |
| `navlyn_exact_navigation` | Precise source navigation from a `candidateId` or exact `file`/`line`/`column` target, including reference usage filtering and grouping | `definition`, `references`, `callers`, `calls`, `implementations`, `type-hierarchy`, `symbol-info` |
| `navlyn_tests_for_symbol` | Static related test candidates for a selected symbol | `tests-for-symbol` |
| `navlyn_tests_for_diff` | Static related test candidates for changed symbols in a Git diff | `tests-for-diff` |
| `navlyn_di_impact` | Source-level DI registrations, consumers, dependencies, and risk facts for a selected type | `di-impact` |
| `navlyn_public_api_diff` | Source-level public/protected API changes between Git refs | `public-api-diff` |
| `navlyn_review_diff` | Changed symbols, impact, diagnostics, related tests, and review facts for a Git diff | `review-diff` |
| `navlyn_context_pack` | Bounded reading material for `review`, `modify`, or `understand` workflows | `context-pack` |
| `navlyn_batch` | Multiple batch-supported CLI facts in one MCP tool call, including .NET application domain packs | `batch` |

`navlyn_workspace_summary`, `navlyn_review_diff`, and `navlyn_context_pack` accept optional `profile` values of `compact`, `evidence`, or `full` and forward them to the CLI. `navlyn_context_pack` also accepts `changeKind` for edit-oriented ranking hints such as `signature`, `behavior`, `nullability`, `async`, `di-registration`, or `endpoint`. `navlyn_batch` accepts request-level `profile` fields for profiled workflow commands. Use `compact` for small first scans, `evidence` for review/CI facts, and `full` when compatibility with the rich CLI result is more important than output size.

All tools use MCP structured content and advertise the shared Navlyn MCP result envelope as their output schema. The inner `result` object remains the command-specific CLI JSON documented in [`navlyn-cli-commands.md`](navlyn-cli-commands.md).

## Resources

Navlyn exposes MCP resources as stable entry points to bounded semantic facts. Resource content is JSON text using the same MCP result envelope as tools.

| Resource | Backing Fact | Notes |
| --- | --- | --- |
| `navlyn://workspace/summary` | `repo-graph --profile compact` | Concrete resource for first workspace scans. |
| `navlyn://symbol/{candidateId}` | `about --candidate-id ...` | Uses candidate IDs from fuzzy commands. |
| `navlyn://symbol/{candidateId}/source?view={view}` | `symbol-source --candidate-id ... --view ...` | `view` defaults to `declaration`; supported values follow the CLI contract. |
| `navlyn://file/{path}` | Guarded placeholder | Advertised for discovery, but raw file reads are intentionally unsupported. Use source/symbol resources, exact navigation, or context packs for bounded facts. |

Resources do not expose arbitrary filesystem access and do not dump unbounded source text. Invalid resource arguments return a Navlyn MCP error envelope or an MCP resource error, depending on where validation fails.

## Prompts

Navlyn exposes prompts that guide clients toward facts-only investigation flows:

| Prompt | Use It For |
| --- | --- |
| `navlyn_understand_symbol` | Resolve and inspect a symbol using `find`, `about`, exact navigation, and context packs. |
| `navlyn_prepare_edit` | Gather impact, references, related tests, and bounded reading material before editing. |
| `navlyn_review_diff` | Collect deterministic review facts and diff context. |
| `navlyn_fix_diagnostic` | Investigate diagnostics with semantic facts before applying edits. |

Prompts are guidance for the MCP client. They do not edit files, generate review conclusions, run tests, or convert static source facts into runtime proof.

## Common Flows

Start a repository investigation:

```text
navlyn_workspace_summary(profile: "compact")
navlyn_find_symbol(query: "PaymentService", assumeKind: "NamedType")
navlyn_exact_navigation(operation: "definition", candidateId: "sym:v1:...")
navlyn_about_symbol(candidateId: "sym:v1:...")
navlyn_related_files(candidateId: "sym:v1:...", limit: 30)
```

Before a non-trivial edit:

```text
navlyn_find_symbol(query: "PaymentService", assumeKind: "NamedType")
navlyn_exact_navigation(operation: "references", candidateId: "sym:v1:...", usageKinds: ["invoke", "construct"], groupBy: ["file", "usage-kind"], limit: 50)
navlyn_impact(candidateId: "sym:v1:...", depth: 2)
navlyn_tests_for_symbol(candidateId: "sym:v1:...", profile: "compact")
navlyn_context_pack(candidateId: "sym:v1:...", goal: "modify", changeKind: "signature", profile: "compact")
```

Review the current diff:

```text
navlyn_review_diff(profile: "evidence")
navlyn_tests_for_diff(profile: "compact")
navlyn_context_pack(diff: true, goal: "review", profile: "compact")
```

Gather dedicated public API or DI facts:

```text
navlyn_public_api_diff(base: "main", profile: "evidence")
navlyn_di_impact(candidateId: "sym:v1:...", profile: "compact")
```

Gather .NET application domain facts through batch:

```json
{
  "requests": [
    { "id": "routes", "command": "route-map", "profile": "compact", "routeLimit": 20 },
    { "id": "options", "command": "options-graph", "query": "PaymentOptions", "profile": "compact" },
    { "id": "message", "command": "where-handled", "query": "CreateOrderCommand", "assumeKind": "NamedType", "profile": "compact" },
    { "id": "ef", "command": "ef-model", "entity": "Order", "profile": "compact" },
    { "id": "pkg", "command": "package-usage", "package": "Microsoft.EntityFrameworkCore", "namespaces": ["Microsoft.EntityFrameworkCore"], "profile": "compact" }
  ]
}
```

Gather several less common or combined facts through batch:

```json
{
  "requests": [
    { "id": "di", "command": "di-graph", "profile": "compact" }
  ]
}
```

## Result Envelope

Success:

```json
{
  "ok": true,
  "tool": "navlyn_workspace_summary",
  "sourceCommand": {
    "command": "repo-graph",
    "arguments": ["repo-graph", "--workspace", "navlyn.slnx"]
  },
  "workspace": "navlyn.slnx",
  "result": {
    "command": "repo-graph"
  }
}
```

Failure:

```json
{
  "ok": false,
  "tool": "navlyn_find_symbol",
  "sourceCommand": null,
  "workspace": "navlyn.slnx",
  "error": {
    "code": "NAVLYN_MCP_INVALID_ARGUMENT",
    "message": "query is required."
  }
}
```

CLI failures preserve the first `NAVLYN####` diagnostic code found on stderr when available. Wrapper failures use `NAVLYN_MCP_*` codes.

For `navlyn_batch`, error layering is important:

- `NAVLYN_MCP_INVALID_ARGUMENT` means the MCP wrapper rejected the tool input before launching the CLI.
- A CLI fatal batch error, such as invalid top-level JSON, returns MCP `ok: false` with a CLI diagnostic such as `NAVLYN1008`.
- A per-request batch failure is a successful MCP tool call whose `result.results[]` item has `ok: false` and its own `error`.

## Boundaries

The MCP server is a thin stdio wrapper over the Navlyn CLI plus MCP-native discovery surfaces. It does not add editing/refactoring tools, arbitrary command execution, warm workspace handles, or a daemon.

`navlyn_exact_navigation` is an allowlist tool, not an arbitrary command runner. Its `operation` argument is limited to `definition`, `references`, `callers`, `calls`, `implementations`, `type_hierarchy`, and `symbol_info`, and its target must be either a `candidateId` or an exact `file`/`line`/`column` source position. Reference usage filters (`usageKind`, `usageKinds`) and grouping (`groupBy`) are supported only for `operation: "references"`.

`navlyn_tests_for_symbol`, `navlyn_tests_for_diff`, `navlyn_di_impact`, and `navlyn_public_api_diff` are allowlisted wrappers over their matching CLI commands. They do not run tests, edit files, publish packages, or execute arbitrary shell commands.

`navlyn_batch` exposes the existing CLI `batch` command only. Batch coverage includes `overview`, `diagnostics`, `symbols`, `symbols-in`, `outline`, `symbol-at`, `symbol-info`, `definition`, `references`, `implementations`, `type-hierarchy`, `callers`, `calls`, `find`, `where-used`, `about`, `related`, `impact`, `entrypoints`, `review-diff`, `review-pack`, `context-pack`, `repo-graph`, `public-api-diff`, `tests-for-symbol`, `tests-for-diff`, `framework-entrypoints`, `di-graph`, `where-registered`, `di-impact`, `route-map`, `route-impact`, `options-graph`, `config-impact`, `where-handled`, `message-flow`, `ef-model`, `entity-impact`, `package-usage`, and `package-impact`. Direct CLI-only facts such as `changed-symbols`, `impact-diff`, `diagnostics-diff`, `scope-at`, `symbol-source`, `signature`, `symbol-diagnostics`, and `diagnostic-pack` are not exposed through `navlyn_batch`. Prefer dedicated MCP tools for a single high-frequency fact and `navlyn_batch` when several batch-supported facts should share one workspace load.

Static impact, framework entrypoint, DI, application domain, and review-pack results are bounded source-level facts. They are useful evidence for agents and reviewers, but they are not complete runtime proofs, runtime route tables, authorization proofs, secret/config value reads, EF runtime models, package compatibility scans, security scans, or replacement review comments.

## Performance Notes

The MCP server runs the Navlyn CLI as a subprocess per tool call. On large solutions, repeated workspace load cost can dominate tool-call latency. Prefer `navlyn_batch` when an agent needs several batch-supported facts from the same workspace, and use `profile: "compact"` or `profile: "evidence"` when output size is the limiting factor.

Use `./scripts/measure-navlyn-performance.ps1` from the repository root to compare CLI direct calls, CLI batch, and MCP stdio tool calls for local performance investigation:

```powershell
./scripts/measure-navlyn-performance.ps1 -Workspace navlyn.slnx -Scenario mcp -Profile compact -Iterations 1 -Warmup 0 -NoBuild
```

The MCP scenario starts `navlyn-mcp`, initializes an MCP stdio session, and measures representative tool calls such as `navlyn_workspace_summary`, `navlyn_find_symbol`, and `navlyn_context_pack`. Functional MCP behavior is covered by the MCP tests in the solution.

See [`navlyn-performance.md`](navlyn-performance.md) for broader performance guidance and release-readiness measurement notes.

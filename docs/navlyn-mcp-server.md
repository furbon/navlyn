# Navlyn MCP Server

Navlyn includes a standalone read-only stdio MCP server in the separate `navlyn.Mcp` project. It gives MCP clients a small, high-level tool surface for C#/.NET repository investigation while preserving the same deterministic Navlyn JSON contract used by the CLI.

The server is intentionally facts-only. It does not edit files, execute arbitrary shell commands, call arbitrary Navlyn commands, access the network, or change the configured workspace. Successful tool calls return a Navlyn MCP result envelope with the Navlyn command JSON under `result`; the inner result shapes remain documented in [`navlyn-cli-commands.md`](navlyn-cli-commands.md).

For normal use, install only `navlyn-mcp` for MCP clients. A separate `navlyn` CLI installation is not required. The `navlyn` CLI and `navlyn-mcp` server share the same Navlyn core engine and command runtime.

## When To Use It

Use `navlyn-mcp` when an agent client should ask semantic C# questions through MCP instead of composing CLI commands itself:

- request project, package, target framework, and test relationship facts when they affect the answer;
- resolve approximate symbol names into deterministic candidates and `candidateId` values;
- gather selected-symbol summaries, related files, static impact, and entrypoint chains;
- collect PR or working-tree review facts from a Git diff;
- request bounded reading material when smaller facts are not enough before review, modification, or explanation work;
- run several already-needed batch-supported facts in one MCP tool call.

Use `rg`, normal file reads, or editor tools for comments, prose docs, strings, generated artifacts, and non-C# content. Navlyn's MCP server is a semantic C#/.NET facts provider, not a general repository search server.

## Starting The Server

Installed .NET tool command shape:

```json
{
  "command": "navlyn-mcp",
  "args": ["--workspace", "path/to/YourRepo.slnx"]
}
```

VS Code workspace configuration shape:

```json
{
  "servers": {
    "navlyn": {
      "type": "stdio",
      "command": "navlyn-mcp",
      "args": ["--workspace", "${workspaceFolder}/YourRepo.slnx"],
      "cwd": "${workspaceFolder}"
    }
  }
}
```

Use workspace `.vscode/mcp.json` when the server should be shared by a repository, and user-level MCP configuration when Navlyn is a personal tool across multiple repositories. The copyable example in this repository is [`../examples/install/vscode-mcp.json`](../examples/install/vscode-mcp.json).

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
dotnet run --framework net10.0 --no-launch-profile --project navlyn.Mcp -- --workspace navlyn.slnx
```

Equivalent MCP client configuration for local development:

```json
{
  "command": "dotnet",
  "args": [
    "navlyn.Mcp/bin/Debug/net10.0/navlyn.Mcp.dll",
    "--workspace",
    "navlyn.slnx"
  ]
}
```

Local package smoke testing uses the standard .NET tool flow:

```powershell
./scripts/test-package-install.ps1
```

## Server Options

- `--workspace <path|auto>`: required `.code-workspace`, `.slnx`, `.sln`, or `.csproj` path, or `auto` to discover one top-level candidate from the working directory/repository root. Tool calls are locked to the resolved workspace.
- `--navlyn-executable <command>`: legacy external Navlyn CLI command or executable. Omit for standalone in-process execution. Use only for compatibility, debugging, or development investigations.
- `--navlyn-arg <arg>`: prefix argument passed before the CLI command on the legacy external path. Repeat for local development with `dotnet navlyn.dll`.
- `--working-directory <path>`: working directory for in-process execution or the legacy child process. Defaults to the repository root when found.
- `--timeout-ms <number>`: per-tool timeout. Defaults to `120000`.
- `--max-json-chars <number>`: maximum command JSON size accepted by the MCP wrapper. Defaults to `4000000`.

The server writes MCP protocol messages to stdout. Logs and diagnostics go to stderr.

`--workspace auto` considers top-level `.code-workspace`, then `.slnx`, then `.sln`, then `.csproj` files. It chooses a single candidate at the best available priority and fails safely if none exist or if multiple best-priority candidates exist. In multi-solution repositories, pass `--workspace` explicitly.

When a `.code-workspace` file is passed explicitly or selected by `auto`, Navlyn reads its `folders` array and looks for `.slnx`, `.sln`, or `.csproj` candidates in each folder. It loads the single best candidate and returns `NAVLYN1106` if the VS Code workspace contains multiple best-priority candidates. Folder paths outside the repository root are allowed but reported as workspace-load warnings on stderr by CLI commands.

## Tool Selection

The MCP surface is deliberately small. Prefer the specific high-level tool for the investigation task. Use normal file reads and `rg` for text questions. Use `navlyn_batch` only after deciding that several batch-supported facts are needed from the same workspace.

| Tool | Use It For | Logical Navlyn Command |
| --- | --- | --- |
| `navlyn_workspace_summary` | Project, target framework, package, test relationship, or MSBuild facts when workspace context matters | `repo-graph` |
| `navlyn_resolve_target` | Standard first symbol entry from query, `candidateId`, or source position; returns one target envelope and next actions | `resolve-target` |
| `navlyn_find_symbol` | Broader approximate symbol candidate lists and manual disambiguation | `find` |
| `navlyn_file_outline` | Semantic outline of one known C# file, including reusable `candidateId` values | `outline` |
| `navlyn_symbol_source` | Bounded source slices for one selected symbol by `candidateId` or exact source position | `symbol-source` |
| `navlyn_symbol_edges` | Direct relationship edges for one selected symbol: references, callers, calls, or implementations | `references`, `callers`, `calls`, `implementations` |
| `navlyn_inspect_file` | Compact semantic inspection of one known C# file without tests, impact, diagnostics, or raw file text | `outline` |
| `navlyn_about_symbol` | Selected-symbol definition, member outline, reference summary, shallow relations | `about` |
| `navlyn_related_files` | File-first investigation map around a selected symbol | `related` |
| `navlyn_impact` | Static source impact before editing or reviewing a symbol | `impact` |
| `navlyn_entrypoints` | Static caller chains or framework-aware entrypoint discovery | `entrypoints`, `framework-entrypoints` |
| `navlyn_exact_navigation` | Precise source navigation from a `candidateId` or exact `file`/`line`/`column` target, including reference usage filtering and grouping | `definition`, `references`, `callers`, `calls`, `implementations`, `type-hierarchy`, `symbol-info` |
| `navlyn_tests_for_symbol` | Static related test candidates for a selected symbol when edit planning or explicit test impact needs them | `tests-for-symbol` |
| `navlyn_tests_for_diff` | Static related test candidates for changed symbols in a Git diff when review or CI planning needs them | `tests-for-diff` |
| `navlyn_di_impact` | Source-level DI registrations, consumers, dependencies, and risk facts for a selected type | `di-impact` |
| `navlyn_public_api_diff` | Source-level public/protected API changes between Git refs | `public-api-diff` |
| `navlyn_review_diff` | Changed symbols, impact, diagnostics, related tests, and review facts for an actual Git diff | `review-diff` |
| `navlyn_context_pack` | Escalation to bounded reading material for `review`, `modify`, or `understand` workflows | `context-pack` |
| `navlyn_batch` | Optimization for multiple already-needed batch-supported CLI facts in one MCP tool call | `batch` |

`navlyn_workspace_summary`, `navlyn_review_diff`, and `navlyn_context_pack` accept optional `profile` values of `compact`, `evidence`, or `full` and forward them to the CLI. `navlyn_context_pack` also accepts `changeKind` for edit-oriented ranking hints such as `signature`, `behavior`, `nullability`, `async`, `di-registration`, or `endpoint`. `navlyn_batch` accepts request-level `profile` fields for profiled workflow commands. Use `compact` for small first scans, `evidence` for review/CI facts, and `full` when compatibility with the rich CLI result is more important than output size.

Source-position tool calls such as `navlyn_resolve_target`, `navlyn_symbol_source`, `navlyn_symbol_edges`, `navlyn_tests_for_symbol`, and `navlyn_di_impact` accept at most one project context and reject fuzzy selection-only options. Diff-mode `navlyn_context_pack` rejects fuzzy selection-only options because the diff, not a symbol query, selects the context.

All tools use MCP structured content and advertise the shared Navlyn MCP result envelope as their output schema. The inner `result` object remains the command-specific Navlyn JSON documented in [`navlyn-cli-commands.md`](navlyn-cli-commands.md). The published envelope schema is [`docs/schemas/navlyn-mcp-tool-result.schema.json`](schemas/navlyn-mcp-tool-result.schema.json). File-first direct tools can include additive `metadata` with `executionPath`, `workspaceCacheStatus`, `workspaceCacheHit`, `workspaceFingerprint`, `indexStatus`, `snapshotId`, and `costClass` so clients can see whether the warm MCP path was used and which workspace snapshot produced the result.

Decision rules for agents:

1. Use `navlyn_workspace_summary(profile: "compact")` only when project, package, target framework, or test relationship context matters.
2. For a known C# file, use `navlyn_file_outline` or `navlyn_inspect_file` and reuse entry `candidateId` values.
3. Resolve symbol intent with `navlyn_resolve_target(query: "...", assumeKind: "...")` and reuse `candidateId`.
4. Use `navlyn_symbol_source` or `navlyn_symbol_edges` for one precise source or relationship fact before asking for broader context.
5. Use `navlyn_review_diff` only for an actual Git diff, PR, staged changes, or working-tree changes.
6. Use `navlyn_context_pack` and `navlyn_batch` as escalation tools, not default first calls.

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
| `navlyn_understand_symbol` | Resolve and inspect a symbol using `find`, `about`, source/edge facts, and context packs. |
| `navlyn_prepare_edit` | Gather impact, references, related tests, and bounded reading material before editing. |
| `navlyn_review_diff` | Collect deterministic review facts and diff context. |
| `navlyn_fix_diagnostic` | Investigate diagnostics with semantic facts before applying edits. |

Prompts are guidance for the MCP client. They do not edit files, generate review conclusions, run tests, or convert static source facts into runtime proof.

## Common Flows

Start a repository investigation:

```text
navlyn_resolve_target(query: "PaymentService", assumeKind: "NamedType")
navlyn_symbol_source(candidateId: "sym:v1:...", view: "declaration")
navlyn_about_symbol(candidateId: "sym:v1:...")
navlyn_related_files(candidateId: "sym:v1:...", limit: 30)
```

Add `navlyn_workspace_summary(profile: "compact")` before that flow only when workspace structure affects the answer.

Before a non-trivial edit:

```text
navlyn_resolve_target(query: "PaymentService", assumeKind: "NamedType")
navlyn_symbol_edges(operation: "references", candidateId: "sym:v1:...", usageKinds: ["invoke", "construct"], groupBy: ["file", "usage-kind"], limit: 50)
navlyn_impact(candidateId: "sym:v1:...", depth: 2)
navlyn_context_pack(candidateId: "sym:v1:...", goal: "modify", changeKind: "signature", profile: "compact")
```

Add `navlyn_tests_for_symbol` only when related test candidates are part of the edit plan or the user asks for them.

## Warm Cache And Freshness

`navlyn_file_outline`, `navlyn_inspect_file`, and `navlyn_symbol_source` use a direct Core resolver path in the default in-process MCP server. The first direct call loads a session-local workspace cache; later direct calls reuse that workspace. `navlyn_file_outline` also records its entry `candidateId` targets in memory, so a follow-up `navlyn_symbol_source(candidateId: "...")` can avoid a broad candidate scan in the same server process.

The cache has no file watcher and is not shared across MCP server processes. Restart the MCP server after source, project, SDK, or package changes when fresh facts matter. Direct-path metadata reports `workspaceCacheStatus`, `indexStatus`, and `snapshotId`; current 0.5.0 values are session-local, and future stale or partial states will be additive. Adapter-backed tools and the legacy `--navlyn-executable` mode preserve the existing CLI execution path and may load workspaces independently.

## Diff And Domain Flows

Review the current diff:

```text
navlyn_review_diff(profile: "evidence")
```

Escalate from diff facts to `navlyn_tests_for_diff`, `navlyn_public_api_diff`, `review-pack` through `navlyn_batch`, or `navlyn_context_pack(diff: true, goal: "review")` only when those facts are needed.

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
  "tool": "navlyn_find_symbol",
  "sourceCommand": {
    "command": "find",
    "arguments": ["find", "--workspace", "navlyn.slnx", "--query", "SymbolSourceResolver"]
  },
  "workspace": "navlyn.slnx",
  "recommendedNextAction": {
    "action": {
      "command": "symbol-source",
      "file": "Navlyn.Core/Symbols/SymbolSourceResolver.cs",
      "line": 12,
      "column": 1
    },
    "when": "Run only if the current result does not answer the user's question.",
    "costClass": "cheap-file-first",
    "runByDefault": false
  },
  "result": {
    "command": "find",
    "nextActions": [
      {
        "command": "symbol-source",
        "file": "Navlyn.Core/Symbols/SymbolSourceResolver.cs",
        "line": 12,
        "column": 1
      }
    ]
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

Command failures preserve the first `NAVLYN####` diagnostic code found on stderr when available. Wrapper failures use `NAVLYN_MCP_*` codes.

## MCP Compatibility

MCP tool results use a stable outer envelope:

- `ok`: `true` for successful wrapper execution, `false` for wrapper or fatal Navlyn command failure.
- `tool`: the MCP tool name.
- `sourceCommand`: the allowlisted logical Navlyn command and arguments. In the default in-process path this is not a launched process; it is kept for compatibility and traceability.
- `workspace`: the configured workspace.
- `metadata`: optional execution and freshness facts such as direct versus adapter path, workspace cache status, workspace fingerprint, snapshot id, and cost class.
- `recommendedNextAction`: optional wrapper around the first inner `nextActions` item, with `when`, `costClass`, and `runByDefault: false`.
- `optionalFollowUps`: optional wrappers for remaining inner `nextActions` items.
- `result`: the inner Navlyn JSON result for successful calls.
- `error`: a structured error object for wrapper or fatal Navlyn command errors.

The outer envelope follows additive compatibility: new fields may be added, and clients should ignore unknown fields. `recommendedNextAction` and `optionalFollowUps` are guidance for choosing one useful follow-up, not instructions to execute every listed command. The inner `result` follows the CLI compatibility policy in [`navlyn-cli-commands.md`](navlyn-cli-commands.md). MCP does not invent a second command-specific schema for inner results.

MCP wrapper errors use `NAVLYN_MCP_*` codes. Navlyn command errors preserve `NAVLYN####` diagnostics when available. Per-request `navlyn_batch` failures are represented inside the successful batch `result`, not as outer MCP wrapper failures.

For `navlyn_batch`, error layering is important:

- `NAVLYN_MCP_INVALID_ARGUMENT` means the MCP wrapper rejected the tool input before running the logical Navlyn command.
- A fatal batch error, such as invalid top-level JSON, returns MCP `ok: false` with a Navlyn diagnostic such as `NAVLYN1008`.
- A per-request batch failure is a successful MCP tool call whose `result.results[]` item has `ok: false` and its own `error`.

## Boundaries

The MCP server is a standalone stdio frontend over the shared Navlyn engine plus MCP-native discovery surfaces. It does not add editing/refactoring tools, arbitrary command execution, file watching, network access, or a daemon.

`navlyn_exact_navigation` and `navlyn_symbol_edges` are allowlist tools, not arbitrary command runners. `navlyn_exact_navigation.operation` is limited to `definition`, `references`, `callers`, `calls`, `implementations`, `type_hierarchy`, and `symbol_info`; `navlyn_symbol_edges.operation` is limited to `references`, `callers`, `calls`, and `implementations`. Their targets must be either a `candidateId` or an exact `file`/`line`/`column` source position. Reference usage filters (`usageKind`, `usageKinds`) and grouping (`groupBy`) are supported only for `operation: "references"`.

`navlyn_tests_for_symbol`, `navlyn_tests_for_diff`, `navlyn_di_impact`, and `navlyn_public_api_diff` are allowlisted wrappers over their matching logical Navlyn commands. They do not run tests, edit files, publish packages, or execute arbitrary shell commands.

`navlyn_batch` exposes the existing Navlyn `batch` command only. Batch coverage includes `overview`, `diagnostics`, `symbols`, `symbols-in`, `outline`, `symbol-at`, `symbol-info`, `definition`, `references`, `implementations`, `type-hierarchy`, `callers`, `calls`, `find`, `resolve-target`, `where-used`, `about`, `related`, `impact`, `entrypoints`, `review-diff`, `review-pack`, `context-pack`, `repo-graph`, `public-api-diff`, `tests-for-symbol`, `tests-for-diff`, `framework-entrypoints`, `di-graph`, `where-registered`, `di-impact`, `route-map`, `route-impact`, `options-graph`, `config-impact`, `where-handled`, `message-flow`, `ef-model`, `entity-impact`, `package-usage`, and `package-impact`. Direct CLI-only facts such as `changed-symbols`, `impact-diff`, `diagnostics-diff`, `scope-at`, `signature`, `symbol-diagnostics`, and `diagnostic-pack` are not exposed through `navlyn_batch`; use dedicated MCP tools for single high-frequency facts such as `navlyn_file_outline`, `navlyn_symbol_source`, and `navlyn_symbol_edges`. Prefer `navlyn_batch` only when several batch-supported facts are already needed.

Static impact, framework entrypoint, DI, application domain, and review-pack results are bounded source-level facts. They are useful evidence for agents and reviewers, but they are not complete runtime proofs, runtime route tables, authorization proofs, secret/config value reads, EF runtime models, package compatibility scans, security scans, or replacement review comments.

## Performance Notes

The MCP server runs Navlyn commands in-process by default. This removes the external CLI process requirement and avoids CLI process startup overhead, but each standalone tool call still performs a conservative workspace load. Prefer `navlyn_batch` after the agent knows it needs several batch-supported facts from the same workspace, and use `profile: "compact"` or `profile: "evidence"` when output size is the limiting factor.

When `--navlyn-executable` is explicitly supplied, `navlyn-mcp` uses the legacy external CLI adapter. This escape hatch is useful for compatibility and debugging, not normal MCP installation.

Use `./scripts/measure-navlyn-performance.ps1` from the repository root to compare CLI direct calls, CLI batch, and MCP stdio tool calls for local performance investigation:

```powershell
./scripts/measure-navlyn-performance.ps1 -Workspace navlyn.slnx -Scenario mcp -Profile compact -Iterations 1 -Warmup 0 -NoBuild
```

The MCP scenario starts `navlyn-mcp`, initializes an MCP stdio session, and measures representative tool calls such as `navlyn_workspace_summary`, `navlyn_resolve_target`, `navlyn_find_symbol`, and `navlyn_context_pack`. Functional MCP behavior is covered by the MCP tests in the solution.

See [`navlyn-performance.md`](navlyn-performance.md) for broader performance guidance and release-readiness measurement notes.

See [`navlyn-architecture.md`](navlyn-architecture.md) for the shared core, CLI frontend, MCP frontend, and legacy external CLI boundaries.

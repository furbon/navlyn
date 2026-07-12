# Navlyn MCP Server

`navlyn-mcp` gives MCP clients a read-only C#-first .NET semantic evidence surface with Roslyn-backed Visual Basic support. It is designed for agents that should inspect code with Roslyn/MSBuild facts before they edit, review, or explain it. For installation and client-specific copyable files, start with the [README](../README.md#use-with-mcp).

The server is intentionally facts-only:

- no file edits;
- no arbitrary shell execution;
- no network access;
- no arbitrary raw file server;
- no workspace mutation;
- no hidden review-comment publishing.

Successful tool calls return a Navlyn MCP result envelope with the Navlyn command JSON under `result`; the inner result shapes remain documented in [`navlyn-cli-commands.md`](navlyn-cli-commands.md).

For normal use, install only `navlyn-mcp` for MCP clients. A separate `navlyn` CLI installation is not required. The `navlyn` CLI and `navlyn-mcp` server share the same Navlyn core engine and command runtime.

## When To Use It

Use `navlyn-mcp` when an agent needs a semantic C# or Visual Basic fact that text search cannot safely provide:

| Need | Start With |
| --- | --- |
| "Is this repo ready for Navlyn?" | `navlyn_doctor` |
| "Which symbol did the user mean?" | `navlyn_resolve_target` |
| "What is in this C# or Visual Basic file?" | `navlyn_file_outline` |
| "Show the exact source for this symbol." | `navlyn_symbol_source` |
| "Who references or calls this selected symbol?" | `navlyn_symbol_edges` |
| "What workspace/project context matters?" | `navlyn_workspace_summary` |
| "What evidence should an agent gather before editing?" | `navlyn_edit_preflight` in `edit` profile |
| "Did the edit hit the intended symbol?" | `navlyn_post_edit_guard` or `navlyn_wrong_symbol_guard` in `edit` or `review` profile |
| "What does this actual Git diff affect?" | `navlyn_review_diff` in `review` profile |
| "What should the agent read before editing?" | `navlyn_context_pack` only after smaller facts show it is needed |

Use `rg`, normal file reads, or editor tools for comments, prose docs, strings, generated artifacts, and non-Roslyn-source content. Navlyn's MCP server is a semantic C#-first .NET facts provider, not a general repository search server.

Navlyn MCP exposes one stable read-only semantic tool surface. Configure the workspace once; agents should choose the smallest relevant tool from tool descriptions, schemas, and returned evidence instead of asking humans to select a startup mode.

## Starting The Server

Installed .NET tool command shape:

```json
{
  "command": "navlyn-mcp",
  "args": ["--workspace", "path/to/YourRepo.sln"]
}
```

`navlyn.workspace.json` is optional. Use a solution/project path for a normal repository; add the JSON configuration only when the repository needs a shared candidate-selection policy. Its settings are described in [`navlyn-workspace.md`](navlyn-workspace.md).

MCP defaults `--workspace-root-policy` to `repo-relative`, so `.code-workspace` folders and `navlyn.workspace.json` candidates outside the repository root are rejected unless the server is started with `--workspace-root-policy allow-listed` and matching `allowRoots`, or `--workspace-root-policy all`.

VS Code workspace configuration shape:

```json
{
  "servers": {
    "navlyn": {
      "type": "stdio",
      "command": "navlyn-mcp",
      "args": ["--workspace", "${workspaceFolder}/YourRepo.sln"],
      "cwd": "${workspaceFolder}"
    }
  }
}
```

Use workspace `.vscode/mcp.json` when the server should be shared by a repository, and user-level MCP configuration when Navlyn is a personal tool across multiple repositories. The copyable example in this repository is [`../examples/install/vscode-mcp.json`](../examples/install/vscode-mcp.json).

For local repositories with exactly one top-level workspace candidate, `auto` can discover the workspace. Discovery prefers `navlyn.workspace.json`, then `.code-workspace`, then `.slnx`, then `.sln`, then `.csproj` or `.vbproj`:

```json
{
  "command": "navlyn-mcp",
  "args": ["--workspace", "auto"]
}
```

Local development from this repository:

```powershell
dotnet build navlyn.slnx
dotnet run --framework net10.0 --no-launch-profile --project navlyn.Mcp -- --workspace navlyn.workspace.json
```

Equivalent MCP client configuration for local development:

```json
{
  "command": "dotnet",
  "args": [
    "navlyn.Mcp/bin/Debug/net10.0/navlyn.Mcp.dll",
    "--workspace",
    "navlyn.workspace.json"
  ]
}
```

Local package smoke testing uses the standard .NET tool flow:

```powershell
./scripts/test-package-install.ps1
```

## Server Options

- `--workspace <path|auto>`: required `navlyn.workspace.json`, `.code-workspace`, `.slnx`, `.sln`, `.csproj`, or `.vbproj` path, or `auto` to discover one top-level candidate from the working directory/repository root. Tool calls are locked to the resolved workspace.
- `--workspace-root-policy <repo-relative|allow-listed|all>`: workspace folder policy for `navlyn.workspace.json` and `.code-workspace` expansion. Defaults to `repo-relative` for MCP.
- `--navlyn-executable <command>`: legacy external Navlyn CLI command or executable. Omit for standalone in-process execution. Use only for compatibility, debugging, or development investigations.
- `--navlyn-arg <arg>`: prefix argument passed before the CLI command on the legacy external path. Repeat for local development with `dotnet navlyn.dll`.
- `--working-directory <path>`: working directory for in-process execution or the legacy child process. Defaults to the repository root when found.
- `--timeout-ms <number>`: per-tool timeout. Defaults to `120000`.
- `--max-json-chars <number>`: maximum command JSON size accepted by the MCP wrapper. Defaults to `4000000`.
- `--daemon-pipe <name>`: optional local `navlyn serve --pipe <name>` daemon used for `navlyn_workspace_status` and `navlyn_workspace_refresh`. If the pipe is unavailable, the in-process server falls back to its normal direct workspace path.
- `--tool-profile <reader|review|edit|full>`: deprecated compatibility alias. Valid old values are accepted and ignored; Navlyn MCP now exposes one read-only tool surface. Invalid values still fail so config typos are caught. `NAVLYN_MCP_TOOL_PROFILE` is accepted with the same compatibility behavior.

The server writes MCP protocol messages to stdout. Logs and diagnostics go to stderr.

`--workspace auto` considers top-level `navlyn.workspace.json`, then `.code-workspace`, then `.slnx`, then `.sln`, then `.csproj` or `.vbproj` files. It chooses a single candidate at the best available priority and fails safely if none exist or if multiple best-priority candidates exist. In multi-solution repositories, pass `--workspace` explicitly.

When `navlyn.workspace.json` is passed explicitly or selected by `auto`, Navlyn applies its `primaryWorkspace`, `workspaceCandidates`, exclusion, test inclusion, root policy, allow-list, and cache-hint fields before loading the selected MSBuild workspace. When a `.code-workspace` file is passed explicitly or selected by `auto`, Navlyn reads its `folders` array and looks for `.slnx`, `.sln`, `.csproj`, or `.vbproj` candidates in each folder. It loads the single best candidate and returns `NAVLYN1106` if the VS Code workspace contains multiple best-priority candidates. Under MCP's default `repo-relative` root policy, outside-root folders return `NAVLYN1110`; use `allow-listed` or `all` only when that broader scope is intentional.

## Stable Tool Surface

The tool list is stable for normal MCP startup:

| Surface | Exposed Tools | Use It For |
| --- | --- | --- |
| Unified read-only surface | Every existing Navlyn MCP tool, including edit evidence, review evidence, guard, DI, public API, related-test, context, exact navigation, and `navlyn_batch` tools. | Stable agent setup. The tool descriptions and schemas tell the model when a tool is appropriate; result warnings, partial metadata, and next-action hints guide follow-up calls. |

`--tool-profile reader|review|edit|full` and `NAVLYN_MCP_TOOL_PROFILE` are deprecated no-op compatibility aliases during migration. When supplied, the server starts with the same unified tool list and writes a deterministic stderr warning before serving MCP protocol messages on stdout.

## Tool Selection

The MCP surface is deliberately need-triggered. Prefer the specific high-level tool for the investigation task. Use normal file reads and `rg` for text questions. Use `navlyn_batch` only after deciding that several batch-supported facts are needed from the same workspace.

| Tool | Use It For | Logical Navlyn Command |
| --- | --- | --- |
| `navlyn_doctor` | Setup readiness, SDK/workspace diagnostics, and copyable first commands | `doctor` |
| `navlyn_workspace_summary` | Project, target framework, package, test relationship, or MSBuild facts when workspace context matters | `repo-graph` |
| `navlyn_workspace_status` | Workspace snapshot, freshness, document-index size, and optional `.navlyn/cache` manifest status | `workspace-status` |
| `navlyn_workspace_refresh` | Explicitly refresh the warm workspace snapshot and optionally clear/write the lightweight cache manifest | `workspace-refresh` |
| `navlyn_resolve_target` | Standard first symbol entry from query, `candidateId`, or source position; returns one target envelope and next actions | `resolve-target` |
| `navlyn_find_symbol` | Broader approximate symbol candidate lists and manual disambiguation | `find` |
| `navlyn_file_outline` | Semantic outline of one known C# or Visual Basic file, including reusable `candidateId` values | `outline` |
| `navlyn_symbol_source` | Bounded source slices for one selected symbol by `candidateId` or exact source position | `symbol-source` |
| `navlyn_symbol_edges` | Direct relationship edges for one selected symbol: references, callers, calls, or implementations | `references`, `callers`, `calls`, `implementations` |
| `navlyn_inspect_file` | Compact semantic inspection of one known C# or Visual Basic file without tests, impact, diagnostics, or raw file text | `outline` |
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
| `navlyn_edit_preflight` | One-call pre-edit anchor, source, bounded context, related tests, confidence, and next guard command | `edit-preflight` |
| `navlyn_post_edit_guard` | Compare a saved `edit-preflight` anchor or `candidateId` with the current diff | `post-edit-guard` |
| `navlyn_wrong_symbol_guard` | Re-resolve intended target intent and compare it with changed symbols | `wrong-symbol-guard` |
| `navlyn_change_intent_pack` | Compact intent record for agent memory or CI artifacts | `change-intent-pack` |
| `navlyn_agent_handoff_pack` | Target anchors, reading queue, trusted evidence, open risks, and next checks for handoff | `agent-handoff-pack` |
| `navlyn_confidence_ledger` | Evidence ledger explaining what raised or lowered target confidence | `confidence-ledger` |
| `navlyn_batch` | Optimization for multiple already-needed batch-supported CLI facts in one MCP tool call | `batch` |

`navlyn_workspace_summary`, `navlyn_review_diff`, and `navlyn_context_pack` accept optional `profile` values of `compact`, `evidence`, or `full` and forward them to the CLI. `navlyn_workspace_status` and `navlyn_workspace_refresh` accept cache mode values `auto`, `on`, or `off`; refresh also accepts `clearCache` and `writeCache`. `navlyn_about_symbol` and `navlyn_impact` accept workflow `profile` values of `light` or `full`: use `light` for first-pass selected-symbol facts and local outgoing calls, and use `full` only when heavy reference/caller or impact facts are needed. `navlyn_context_pack`, `navlyn_edit_preflight`, `navlyn_change_intent_pack`, `navlyn_agent_handoff_pack`, and `navlyn_confidence_ledger` accept `changeKind` for edit-oriented ranking hints such as `signature`, `behavior`, `nullability`, `async`, `di-registration`, or `endpoint`. `navlyn_batch` accepts request-level `profile` fields for the matching CLI command family and `candidateIdFrom` dependencies when a later request should reuse an earlier result's `candidateId`. Use `compact` for small profiled workflow scans, `evidence` for review/CI facts, and `full` when compatibility with the rich CLI result is more important than output size.

Source-position tool calls such as `navlyn_resolve_target`, `navlyn_symbol_source`, `navlyn_symbol_edges`, `navlyn_tests_for_symbol`, and `navlyn_di_impact` accept at most one project context and reject fuzzy selection-only options. Diff-mode `navlyn_context_pack` rejects fuzzy selection-only options because the diff, not a symbol query, selects the context.

All tools use MCP structured content and advertise the shared Navlyn MCP result envelope as their output schema. The inner `result` object remains the command-specific Navlyn JSON documented in [`navlyn-cli-commands.md`](navlyn-cli-commands.md). The published envelope schema is [`docs/schemas/navlyn-mcp-tool-result.schema.json`](schemas/navlyn-mcp-tool-result.schema.json). Direct tools can include additive `metadata` with `executionPath`, `workspaceCacheStatus`, `workspaceCacheHit`, `workspaceFingerprint`, `indexStatus`, `snapshotId`, `freshnessStatus`, `documentIndexDocumentCount`, `documentIndexEstimatedBytes`, and `costClass` so clients can see whether the warm MCP path was used and which workspace snapshot produced the result.

Decision rules for agents:

1. Use `navlyn_doctor` first when setup, SDK, workspace loading, or first-command guidance is uncertain.
2. Use `navlyn_workspace_summary(profile: "compact")` only when project, package, target framework, or test relationship context matters.
3. For a known C# or Visual Basic file, use `navlyn_file_outline` or `navlyn_inspect_file` and reuse entry `candidateId` values.
4. Resolve symbol intent with `navlyn_resolve_target(query: "...", assumeKind: "...")` and reuse `candidateId`.
5. Use `navlyn_symbol_source` or `navlyn_symbol_edges` for one precise source or relationship fact before asking for broader context. `calls` is a cheap local outgoing-edge operation; `references` and `callers` are scoped expensive operations and should use `scope`/`maxDocuments` when broad.
6. Before a concrete edit, prefer `navlyn_edit_preflight`; after editing, run `navlyn_post_edit_guard` or `navlyn_wrong_symbol_guard` before widening scope.
7. Use `navlyn_review_diff` only for an actual Git diff, PR, staged changes, or working-tree changes.
8. Use `navlyn_context_pack` only when a bounded reading queue is needed. Use `navlyn_batch` only after several batch-supported facts are already needed.

## Resources

Navlyn exposes MCP resources as stable entry points to bounded semantic facts. Resource content is JSON text using the same MCP result envelope as tools.

| Resource | Backing Fact | Notes |
| --- | --- | --- |
| `navlyn://workspace/summary` | `repo-graph --profile compact` | Concrete resource for first workspace scans. |
| `navlyn://symbol/{candidateId}` | `about --candidate-id ...` | Uses candidate IDs from fuzzy commands. |
| `navlyn://symbol/{candidateId}/source?view={view}` | `symbol-source --candidate-id ... --view ...` | `view` defaults to `declaration`; supported values follow the CLI contract. |
| `navlyn://file/{path}` | Guarded discovery URI | Advertised for discovery, but raw file reads are intentionally unsupported. Use source/symbol resources, exact navigation, or context packs for bounded facts. |

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
navlyn_about_symbol(candidateId: "sym:v1:...", profile: "light")
navlyn_related_files(candidateId: "sym:v1:...", limit: 30)
```

Add `navlyn_workspace_summary(profile: "compact")` before that flow only when workspace structure affects the answer.

Before a non-trivial edit:

```text
navlyn_edit_preflight(query: "PaymentService", assumeKind: "NamedType", goal: "modify", changeKind: "behavior")
// edit outside Navlyn
navlyn_post_edit_guard(candidateId: "sym:v1:...", failOnRisk: "high")
```

`navlyn_edit_preflight` includes source, bounded context, related test evidence, confidence, known unknowns, and next guard commands. Add separate `navlyn_tests_for_symbol`, `navlyn_impact`, or `navlyn_context_pack` calls only when the preflight result shows more detail is needed.

## Warm Cache And Freshness

`navlyn_workspace_summary`, `navlyn_workspace_status`, `navlyn_workspace_refresh`, `navlyn_file_outline`, `navlyn_inspect_file`, and `navlyn_symbol_source` use a direct Core resolver path in the default in-process MCP server. The first direct call loads a session-local workspace cache and builds a `DocumentIndex` for path-to-document lookup; later direct calls reuse that workspace snapshot and index. `navlyn_workspace_refresh` evicts that snapshot and reloads it. `navlyn_file_outline` also records its entry `candidateId` targets in memory, so a follow-up `navlyn_symbol_source(candidateId: "...")` can avoid a broad candidate scan in the same server process.

Fuzzy symbol tools use a workspace-scoped declaration index and candidate record map. Same-snapshot follow-ups that pass a returned `candidateId` can resolve through the recorded candidate when the solution fingerprint matches; unknown or stale IDs still return deterministic Navlyn candidate diagnostics. Heavy reference and caller operations use lexical document prefiltering plus scoped Roslyn document-set searches. Their inner results include `search` metadata with `scope`, `costClass`, searched counts, `partial`, and rerun hints when `maxDocuments` truncates the semantic search.

The warm cache has no file watcher and is not shared across MCP server processes. Use `navlyn_workspace_refresh` or restart the MCP server after source, project, SDK, or package changes when fresh facts matter. The optional `.navlyn/cache` manifest is separate from the in-memory snapshot: it is opt-in via `cache: "on"` or `navlyn.workspace.json` `cacheHints.enabled`, stores no source text, records tracked file hashes/mtimes and project/document/declaration facts, and reports `fresh`, `missing`, `stale`, `invalid`, or `disabled`. Direct-path metadata reports `workspaceCacheStatus`, `indexStatus`, `freshnessStatus`, `snapshotId`, and document-index sizing. Adapter-backed tools and the legacy `--navlyn-executable` mode preserve the existing CLI execution path and may load workspaces independently.

## Diff And Domain Flows

Review the current diff:

```text
navlyn_review_diff(profile: "evidence")
```

Escalate from diff facts to `navlyn_tests_for_diff`, `navlyn_public_api_diff`, or `navlyn_context_pack(diff: true, goal: "review")` only when those facts are needed. Use `review-pack` through `navlyn_batch` only after deciding several batch-supported facts are needed.

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
- `metadata`: optional execution and freshness facts such as direct versus adapter path, workspace cache status, workspace fingerprint, snapshot id, document-index size, and cost class.
- `recommendedNextAction`: optional wrapper around the first inner `nextActions` item, with `when`, `costClass`, and `runByDefault: false`.
- `optionalFollowUps`: optional wrappers for remaining inner `nextActions` items.
- `result`: the inner Navlyn JSON result for successful calls.
- `error`: a structured error object for wrapper or fatal Navlyn command errors.

The outer envelope follows additive compatibility: new fields may be added, and clients should ignore unknown fields. `recommendedNextAction` and `optionalFollowUps` are guidance for choosing one useful follow-up, not instructions to execute every listed command. The inner `result` follows the CLI compatibility policy in [`navlyn-cli-commands.md`](navlyn-cli-commands.md). MCP does not invent a second command-specific schema for inner results.

MCP wrapper errors use `NAVLYN_MCP_*` codes. Navlyn command errors preserve `NAVLYN####` diagnostics when available. Per-request `navlyn_batch` failures are represented inside the successful batch `result`, not as outer MCP wrapper failures.

For `navlyn_batch`, error layering is important:

- `NAVLYN_MCP_INVALID_ARGUMENT` means the MCP wrapper rejected the tool input before running the logical Navlyn command.
- `NAVLYN_MCP_TOOL_PROFILE_DEPRECATED` is a startup stderr warning when an old profile option is supplied. It does not appear in MCP stdout tool results.
- A fatal batch error, such as invalid top-level JSON, returns MCP `ok: false` with a Navlyn diagnostic such as `NAVLYN1008`.
- A per-request batch failure is a successful MCP tool call whose `result.results[]` item has `ok: false` and its own `error`.

## Boundaries

The MCP server is a standalone stdio frontend over the shared Navlyn engine plus MCP-native discovery surfaces. It does not add editing/refactoring tools, arbitrary command execution, file watching, network access, or a daemon.

`navlyn_exact_navigation` and `navlyn_symbol_edges` are allowlist tools, not arbitrary command runners. `navlyn_exact_navigation.operation` is limited to `definition`, `references`, `callers`, `calls`, `implementations`, `type_hierarchy`, and `symbol_info`; `navlyn_symbol_edges.operation` is limited to `references`, `callers`, `calls`, and `implementations`. Their targets must be either a `candidateId` or an exact `file`/`line`/`column` source position. Reference usage filters (`usageKind`, `usageKinds`) and grouping (`groupBy`) are supported only for `operation: "references"`. `scope` and `maxDocuments` apply to `references` and `callers`; `calls` remains local to the containing member and reports `costClass: "local"`.

`navlyn_tests_for_symbol`, `navlyn_tests_for_diff`, `navlyn_di_impact`, and `navlyn_public_api_diff` are allowlisted wrappers over their matching logical Navlyn commands. They do not run tests, edit files, publish packages, or execute arbitrary shell commands.

`navlyn_batch` wraps the existing Navlyn `batch` command only. Batch coverage includes `overview`, `diagnostics`, `symbols`, `symbols-in`, `outline`, `symbol-at`, `symbol-info`, `symbol-source`, `definition`, `references`, `implementations`, `type-hierarchy`, `callers`, `calls`, `find`, `resolve-target`, `where-used`, `about`, `related`, `impact`, `entrypoints`, `review-diff`, `review-pack`, `context-pack`, `repo-graph`, `public-api-diff`, `tests-for-symbol`, `tests-for-diff`, `framework-entrypoints`, `di-graph`, `where-registered`, `di-impact`, `route-map`, `route-impact`, `options-graph`, `config-impact`, `where-handled`, `message-flow`, `ef-model`, `entity-impact`, `package-usage`, and `package-impact`. Batch requests can use `candidateIdFrom` to feed an earlier result's `candidateId` into later supported requests. Direct CLI-only facts such as `changed-symbols`, `impact-diff`, `diagnostics-diff`, `scope-at`, `signature`, `symbol-diagnostics`, `diagnostic-pack`, and agent guard commands are not exposed through `navlyn_batch`; use dedicated MCP tools for high-frequency facts such as `navlyn_file_outline`, `navlyn_symbol_source`, `navlyn_symbol_edges`, and the edit/review guard tools. Prefer `navlyn_batch` only when several batch-supported facts are already needed.

Static impact, framework entrypoint, DI, application domain, and review-pack results are bounded source-level facts. They are useful evidence for agents and reviewers, but they are not complete runtime proofs, runtime route tables, authorization proofs, secret/config value reads, EF runtime models, package compatibility scans, security scans, or replacement review comments.

## Performance Notes

The MCP server runs Navlyn commands in-process by default. This removes the external CLI process requirement and avoids CLI process startup overhead, but each standalone adapter-backed tool call still performs a conservative workspace load. Prefer `navlyn_batch` after the agent knows it needs several batch-supported facts from the same workspace, and use `profile: "compact"` or `profile: "evidence"` when output size is the limiting factor.

When `--navlyn-executable` is explicitly supplied, `navlyn-mcp` uses the legacy external CLI adapter. This escape hatch is useful for compatibility and debugging, not normal MCP installation.

Use `./scripts/measure-navlyn-performance.ps1` from the repository root to compare CLI direct calls, CLI batch, and MCP stdio tool calls for local performance investigation:

```powershell
./scripts/measure-navlyn-performance.ps1 -Workspace navlyn.slnx -Scenario mcp -Profile compact -Iterations 1 -Warmup 0 -NoBuild
```

The MCP scenario starts `navlyn-mcp`, initializes an MCP stdio session, and measures representative tool calls such as `navlyn_workspace_summary`, `navlyn_resolve_target`, `navlyn_find_symbol`, and `navlyn_context_pack`. Functional MCP behavior is covered by the MCP tests in the solution.

See [`navlyn-performance.md`](navlyn-performance.md) for broader performance guidance and release-readiness measurement notes.

See [`navlyn-architecture.md`](navlyn-architecture.md) for the shared core, CLI frontend, MCP frontend, and legacy external CLI boundaries.

# Navlyn MCP Server

`navlyn-mcp` gives MCP clients a read-only C#-first .NET semantic evidence surface with Roslyn-backed Visual Basic support. It is designed for agents that should inspect code with Roslyn/MSBuild facts before they edit, review, or explain it. For installation and client-specific steps, start with [client setup](navlyn-client-setup.md).

The server is intentionally facts-only:

- no file edits;
- no arbitrary shell execution;
- no network access;
- no arbitrary raw file server;
- no workspace mutation;
- no hidden review-comment publishing.

Successful tool calls return a Navlyn MCP result envelope with the Navlyn command JSON under `result`; the inner result shapes remain documented in [`navlyn-cli-commands.md`](navlyn-cli-commands.md).

For an explicit workspace path, initial cache discovery checks the selected workspace and ancestor configuration before inventorying the loaded project roots. Unrelated artifact-directory links do not block startup. Loaded source, project, configuration, and dependency changes still trigger freshness checks; links inside an inventoried project remain an inspection error.

For normal use, install only `navlyn-mcp` for MCP clients. A separate `navlyn` CLI installation is not required. The `navlyn` CLI and `navlyn-mcp` server share the same Navlyn core engine and command runtime.

## When To Use It

Use `navlyn-mcp` when an agent needs a semantic C# or Visual Basic fact that text search cannot safely provide:

| Need | Start With |
| --- | --- |
| "Is this repo ready for Navlyn?" | `navlyn_doctor` |
| "Which symbol did the user mean?" | `navlyn_target` |
| "What is in this C# or Visual Basic file?" | `navlyn_file_outline` |
| "Show the exact source for this symbol." | `navlyn_read` |
| "Who references or calls this selected symbol?" | `navlyn_navigate` |
| "What workspace/project context matters?" | `navlyn_workspace_summary` |
| "What evidence should an agent gather before editing?" | `navlyn_prepare_edit` |
| "Did the edit hit the intended symbol?" | `navlyn_verify_edit` |
| "What does this actual Git diff affect?" | `navlyn_review` |
| "What should the agent read before editing?" | `navlyn_context_pack` only after smaller facts show it is needed |

Use `rg`, normal file reads, or editor tools for comments, prose docs, strings, generated artifacts, and non-Roslyn-source content. Navlyn's MCP server is a semantic C#-first .NET facts provider, not a general repository search server.

Navlyn MCP exposes one stable read-only semantic tool surface. The server selects one workspace at startup, either by default auto discovery or an explicit `--workspace`; agents should choose the smallest relevant tool from tool descriptions, schemas, and returned evidence instead of asking humans to select a startup mode.

## Starting The Server

Installed .NET tool command shape for a normal repository with one top-level workspace candidate, when the MCP client launches the server from the repository root:

```json
{
  "command": "navlyn-mcp"
}
```

When `--workspace` is omitted, `navlyn-mcp` behaves as if `--workspace auto` was supplied. It discovers one top-level `navlyn.workspace.json`, `.code-workspace`, `.slnx`, `.sln`, `.csproj`, or `.vbproj` candidate from the working directory or repository root, and fails instead of guessing if the best candidate is ambiguous. If the MCP client does not preserve a repository-root working directory, pass `--working-directory <repo-root>` while still omitting `--workspace`.

Use an explicit solution/project path only when a repository has several plausible workspaces:

```json
{
  "command": "navlyn-mcp",
  "args": ["--workspace", "path/to/YourRepo.sln"]
}
```

`navlyn.workspace.json` is optional. Add it only when the repository needs a shared candidate-selection policy. Its settings are described in [`navlyn-workspace.md`](navlyn-workspace.md).

MCP defaults `--workspace-root-policy` to `repo-relative`, so `.code-workspace` folders and `navlyn.workspace.json` candidates outside the repository root are rejected unless the server is started with `--workspace-root-policy allow-listed` and matching `allowRoots`, or `--workspace-root-policy all`.

VS Code workspace configuration shape:

```json
{
  "servers": {
    "navlyn": {
      "type": "stdio",
      "command": "navlyn-mcp",
      "cwd": "${workspaceFolder}"
    }
  }
}
```

Use workspace `.vscode/mcp.json` when the server should be shared by a repository, and user-level MCP configuration when Navlyn is a personal tool across multiple repositories. The copyable example in this repository is [`../examples/install/vscode-mcp.json`](../examples/install/vscode-mcp.json).

If a client or wrapper does not launch from the repository root, pass the repository root as the working directory and still let Navlyn auto-discover the workspace:

```json
{
  "command": "navlyn-mcp",
  "args": ["--working-directory", "path/to/YourRepo"]
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

For the 0.8.5 candidate, use the unique-output pack, package-contract, and isolated consumer-install commands in [distribution guidance](navlyn-distribution.md#current-release-state).

## Server Options

- `--workspace <path|auto>`: optional `navlyn.workspace.json`, `.code-workspace`, `.slnx`, `.sln`, `.csproj`, or `.vbproj` path, or `auto` to discover one top-level candidate from the working directory/repository root. Defaults to `auto`. Tool calls are locked to the resolved workspace.
- `--workspace-root-policy <repo-relative|allow-listed|all>`: workspace folder policy for `navlyn.workspace.json` and `.code-workspace` expansion. Defaults to `repo-relative` for MCP.
- `--navlyn-executable <command>`: legacy external Navlyn CLI command or executable. Omit for standalone in-process execution. Use only for compatibility, debugging, or development investigations.
- `--navlyn-arg <arg>`: prefix argument passed before the CLI command on the legacy external path. Repeat for local development with `dotnet navlyn.dll`.
- `--working-directory <path>`: working directory for in-process execution or the legacy child process. Defaults to the repository root when found.
- `--timeout-ms <number>`: per-tool timeout. Defaults to `120000`.
- `--max-json-chars <number>`: maximum command JSON size accepted by the MCP wrapper. Defaults to `4000000`.
- `--daemon-pipe <name>`: optional local `navlyn serve --pipe <name>` daemon used for `navlyn_workspace_status` and `navlyn_workspace_refresh`. If the pipe is unavailable, the in-process server falls back to its normal direct workspace path.
- `--version`: print the installed MCP server version and exit without starting a session.
- `--tool-profile <reader|review|edit|full>`: deprecated compatibility alias. Valid old values are accepted and ignored; Navlyn MCP now exposes one read-only tool surface. Invalid values still fail so config typos are caught. `NAVLYN_MCP_TOOL_PROFILE` is accepted with the same compatibility behavior.

The server writes MCP protocol messages to stdout. Logs and diagnostics go to stderr.

The in-process warm workspace tracks loaded source and project inputs, referenced assemblies and analyzers, and each loaded project's default `obj/project.assets.json` restore file before reusing a snapshot. Changed inputs retire the old generation; unreadable inputs fail closed instead of returning an old result. A restore that only changes assets at a custom location outside these tracked inputs may leave old bindings until `navlyn_workspace_refresh` is called. Run that refresh after restoring such a project.

Default startup and explicit `--workspace auto` consider top-level `navlyn.workspace.json`, then `.code-workspace`, then `.slnx`, then `.sln`, then `.csproj` or `.vbproj` files. Navlyn chooses a single candidate at the best available priority and fails safely if none exist or if multiple best-priority candidates exist. In multi-solution repositories, pass `--workspace` explicitly.

When `navlyn.workspace.json` is passed explicitly or selected by `auto`, Navlyn applies its `primaryWorkspace`, `workspaceCandidates`, exclusion, test inclusion, root policy, allow-list, and cache-hint fields before loading the selected MSBuild workspace. When a `.code-workspace` file is passed explicitly or selected by `auto`, Navlyn reads its `folders` array and looks for `.slnx`, `.sln`, `.csproj`, or `.vbproj` candidates in each folder. It loads the single best candidate and returns `NAVLYN1106` if the VS Code workspace contains multiple best-priority candidates. Under MCP's default `repo-relative` root policy, outside-root folders return `NAVLYN1110`; use `allow-listed` or `all` only when that broader scope is intentional.

## Stable Tool Surface

Normal MCP startup exposes exactly 25 read-only tools:

```text
navlyn_target
navlyn_read
navlyn_file_outline
navlyn_navigate
navlyn_prepare_edit
navlyn_verify_edit
navlyn_review
navlyn_workspace_summary
navlyn_workspace_status
navlyn_workspace_refresh
navlyn_doctor
navlyn_impact
navlyn_context_pack
navlyn_entrypoints
navlyn_tests_for_symbol
navlyn_tests_for_diff
navlyn_diagnostics
navlyn_di
navlyn_public_api_diff
navlyn_routes
navlyn_options
navlyn_messages
navlyn_ef
navlyn_packages
navlyn_batch
```

### v0.7.0 to v0.8.0 tool migration

The 0.7.0 MCP surface predates the 0.8.0 consolidation. The 0.8.0 `tools/list` surface contains 25 tools (listed above). The consolidation retires these 16 names:

| Retired v0.7 MCP name | v0.8 canonical starting point |
| --- | --- |
| `navlyn_resolve_target` | `navlyn_target` |
| `navlyn_find_symbol` | `navlyn_target` |
| `navlyn_inspect_file` | `navlyn_file_outline` |
| `navlyn_symbol_source` | `navlyn_read` |
| `navlyn_symbol_edges` | `navlyn_navigate` |
| `navlyn_about_symbol` | `navlyn_target`, then `navlyn_read` for selected source |
| `navlyn_related_files` | `navlyn_navigate` or `navlyn_context_pack`, depending on the needed evidence |
| `navlyn_exact_navigation` | `navlyn_navigate` |
| `navlyn_review_diff` | `navlyn_review` |
| `navlyn_edit_preflight` | `navlyn_prepare_edit` |
| `navlyn_post_edit_guard` | `navlyn_verify_edit` |
| `navlyn_wrong_symbol_guard` | `navlyn_verify_edit` |
| `navlyn_change_intent_pack` | `navlyn_prepare_edit` |
| `navlyn_agent_handoff_pack` | `navlyn_context_pack` |
| `navlyn_confidence_ledger` | `navlyn_context_pack`; report evidence and uncertainty in the consuming workflow |
| `navlyn_di_impact` | `navlyn_di` |

These are migration starting points, not guaranteed one-to-one schema aliases; review the current tool descriptions and supply their current arguments. This is an MCP-only breaking change: advanced CLI commands remain available. `--tool-profile reader|review|edit|full` and `NAVLYN_MCP_TOOL_PROFILE` remain accepted deprecated no-op aliases for older configurations; each accepted profile exposes the same unified 25-tool list. New configurations should omit them.
For a retained tool name, `navlyn_verify_edit` adds optional symbol query and source-position selection fields (`query`, `file`, `line`, `column`, and related selection options). Pass the `candidateId` or saved anchor from `navlyn_prepare_edit` when checking an existing selection. The `navlyn_target.mode` and `navlyn_read.externalSource` inputs are also new in 0.8.0. Compare current `tools/list` schemas when migrating saved MCP calls.

When a legacy profile alias is supplied, the server starts with the same unified tool list and writes a deterministic stderr warning before serving MCP protocol messages on stdout.

## Tool Selection

The MCP surface is deliberately need-triggered. Prefer the specific high-level tool for the investigation task. Use normal file reads and `rg` for text questions. Use `navlyn_batch` only after deciding that several batch-supported facts are needed from the same workspace.

| Tool | Use It For | Logical Navlyn Command |
| --- | --- | --- |
| `navlyn_target` | Canonical first symbol entry from query, `candidateId`, or source position; returns one target envelope and ambiguity/fail-closed guidance | `target` |
| `navlyn_read` | Canonical bounded source reader for one selected symbol by `candidateId` or exact source position | `read` |
| `navlyn_file_outline` | Semantic outline of one known C# or Visual Basic file, including reusable `candidateId` values | `outline` |
| `navlyn_navigate` | One precise definition, reference, caller, call, implementation, hierarchy, or symbol-information fact from a `candidateId` or exact source position | `definition`, `references`, `callers`, `calls`, `implementations`, `type-hierarchy`, `symbol-info` |
| `navlyn_prepare_edit` | Canonical one-call pre-edit anchor, source, bounded context, related tests, confidence, and next guard command | `prepare-edit` |
| `navlyn_verify_edit` | Canonical post-edit guard comparing a saved preflight anchor or `candidateId` with the current diff | `verify-edit` |
| `navlyn_review` | Canonical changed-symbol, impact, diagnostics, related-test, and review facts for an actual Git diff | `review` |
| `navlyn_workspace_summary` | Project, target framework, package, test relationship, or MSBuild facts when workspace context matters | `repo-graph` |
| `navlyn_workspace_status` | Workspace snapshot, freshness, document-index size, and optional `.navlyn/cache` manifest status | `workspace-status` |
| `navlyn_workspace_refresh` | Explicitly refresh the warm workspace snapshot and optionally clear/write the lightweight cache manifest | `workspace-refresh` |
| `navlyn_doctor` | Setup readiness, SDK/workspace diagnostics, and copyable first commands | `doctor` |
| `navlyn_impact` | Static source impact before editing or reviewing a symbol | `impact` |
| `navlyn_context_pack` | Escalation to bounded reading material for `review`, `modify`, or `understand` workflows | `context-pack` |
| `navlyn_entrypoints` | Static caller chains or framework-aware entrypoint discovery | `entrypoints`, `framework-entrypoints` |
| `navlyn_tests_for_symbol` | Static related test candidates for a selected symbol when edit planning or explicit test impact needs them | `tests-for-symbol` |
| `navlyn_tests_for_diff` | Static related test candidates for changed symbols in a Git diff when review or CI planning needs them | `tests-for-diff` |
| `navlyn_diagnostics` | Workspace, selected-symbol, or diagnostic-pack facts | `diagnostics`, `symbol-diagnostics`, `diagnostic-pack` |
| `navlyn_di` | Source-level DI graph, registrations, consumers, dependencies, and risk facts | `di-graph`, `where-registered`, `di-impact` |
| `navlyn_public_api_diff` | Source-level public/protected API changes between Git refs | `public-api-diff` |
| `navlyn_routes` | Static route map or route-impact evidence | `route-map`, `route-impact` |
| `navlyn_options` | Static options/configuration graph or impact evidence | `options-graph`, `config-impact` |
| `navlyn_messages` | Static MediatR handler or message-flow evidence | `where-handled`, `message-flow` |
| `navlyn_ef` | Static EF Core model or entity-impact evidence | `ef-model`, `entity-impact` |
| `navlyn_packages` | Source package usage or package-impact evidence | `package-usage`, `package-impact` |
| `navlyn_batch` | Optimization for multiple already-needed batch-supported CLI facts in one MCP tool call | `batch` |

### Reading an external library member

`navlyn_read` accepts `externalSource: "none" | "metadata" | "decompiled"`. It defaults to `none`, which keeps the existing source-only behavior. For a metadata-only symbol selected at an exact C# or Visual Basic call site, `metadata` returns a Roslyn declaration and `decompiled` can return reconstructed C# for one exact member from a matching local implementation PE. Existing workspace source always takes priority, and the 25-tool MCP surface is unchanged.

Use `view: "signature"`, `"declaration"`, or `"body"`. The result keeps the call-site `file`, `line`, and `column`. External slices carry `origin` and `editable: false`, plus a `navlyn-metadata://<reference-sha256>/<member-id-sha256>` or `navlyn-decompiled://<implementation-sha256>/<member-id-sha256>` virtual path. Slice coordinates start at line 1 in the returned text; the URI is not a file path and cannot be reused as a source position or candidate ID. `externalAssembly` reports the assembly identity, selected target framework, reference-versus-implementation provenance, and PE content hashes. Reconstructed C# is not original library source and does not establish runtime dispatch.

For `body`, Navlyn requires the exact bound member to have an implementation body in the exact local implementation PE. A reference-only NuGet package, abstract member, unresolved framework implementation or runtime variant, missing PE, malformed image, ambiguity, stale binary, unsupported view, or exceeded safety limit returns a deterministic error without a body. Framework reference metadata may still be returned in `metadata` mode. Navlyn does not restore/fetch packages, execute referenced assemblies, or write source files. The default `none` mode does not inspect dependency PEs.

External reads limit each reference or implementation PE to 64 MiB, `project.assets.json` to 16 MiB, and the selected method IL to 1 MiB. Implementation selection and decompilation run in a disposable worker with a 10-second deadline.

External diagnostics use the CLI IDs in the MCP error result: `NAVLYN1401` unsupported view, `NAVLYN1402` matching implementation unavailable, `NAVLYN1403` exact member has no body, `NAVLYN1404` ambiguous member or implementation, `NAVLYN1405` selected reference/assets/implementation changed during the read, `NAVLYN1406` a configured size/decompilation limit was exceeded, and `NAVLYN1407` malformed or undecompilable PE.

Profiled tools accept the values documented by their logical CLI commands. Use `compact` for small workflow scans, `evidence` for review and CI facts, and `full` only when the richest result is required. `navlyn_workspace_status` and `navlyn_workspace_refresh` accept cache modes `auto`, `on`, or `off`; refresh also accepts `clearCache` and `writeCache`. `navlyn_impact` accepts `light` or `full`. `navlyn_context_pack` accepts edit-oriented `changeKind` hints. `navlyn_batch` accepts request-level profiles and `candidateIdFrom` dependencies when a later request should reuse an earlier result's `candidateId`.

Source-position modes on `navlyn_target`, `navlyn_read`, `navlyn_navigate`, `navlyn_tests_for_symbol`, and selected domain tools accept at most one project context and reject fuzzy selection-only options. Diff-mode `navlyn_context_pack` rejects fuzzy selection-only options because the diff, not a symbol query, selects the context.

All tools use MCP structured content and advertise the shared Navlyn MCP result envelope as their output schema. The inner `result` object remains the command-specific Navlyn JSON documented in [`navlyn-cli-commands.md`](navlyn-cli-commands.md). The published envelope schema is [`docs/schemas/navlyn-mcp-tool-result.schema.json`](schemas/navlyn-mcp-tool-result.schema.json). Direct tools can include additive `metadata` with `executionPath`, `workspaceCacheStatus`, `workspaceCacheHit`, `workspaceFingerprint`, `indexStatus`, `snapshotId`, `freshnessStatus`, `documentIndexDocumentCount`, `documentIndexEstimatedBytes`, and `costClass` so clients can see whether the warm MCP path was used and which workspace snapshot produced the result.

Decision rules for agents:

1. Use `navlyn_doctor` first when setup, SDK, workspace loading, or first-command guidance is uncertain.
2. Use `navlyn_workspace_summary(profile: "compact")` only when project, package, target framework, or test relationship context matters.
3. For a known C# or Visual Basic file, use `navlyn_file_outline` and reuse entry `candidateId` values.
4. Resolve symbol intent with `navlyn_target(query: "...", assumeKind: "...")` and reuse `candidateId`.
5. Use `navlyn_read` or `navlyn_navigate` for one precise source or relationship fact before asking for broader context. `calls` is a cheap local outgoing-edge operation; `references` and `callers` are scoped expensive operations and should use `scope`/`maxDocuments` when broad.
6. Before a concrete edit, prefer `navlyn_prepare_edit`; after editing, run `navlyn_verify_edit` before widening scope.
7. Use `navlyn_review` only for an actual Git diff, PR, staged changes, or working-tree changes.
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
| `navlyn_review_changes` | Collect deterministic review facts and diff context. |
| `navlyn_fix_diagnostic` | Investigate diagnostics with semantic facts before applying edits. |

Prompts are guidance for the MCP client. They do not edit files, generate review conclusions, run tests, or convert static source facts into runtime proof.

## Common Flows

Start a repository investigation:

```text
navlyn_target(query: "PaymentService", assumeKind: "NamedType")
navlyn_read(candidateId: "sym:v1:...", view: "declaration")
navlyn_navigate(operation: "symbol_info", candidateId: "sym:v1:...")
navlyn_navigate(operation: "references", candidateId: "sym:v1:...", groupBy: ["file"], limit: 30)
```

Add `navlyn_workspace_summary(profile: "compact")` before that flow only when workspace structure affects the answer.

Before a non-trivial edit:

```text
navlyn_prepare_edit(query: "PaymentService", assumeKind: "NamedType", goal: "modify", changeKind: "behavior")
// edit outside Navlyn
navlyn_verify_edit(candidateId: "sym:v1:...", failOnRisk: "high")
```

`navlyn_prepare_edit` includes source, bounded context, related test evidence, confidence, known unknowns, and next guard commands. Add separate `navlyn_tests_for_symbol`, `navlyn_impact`, or `navlyn_context_pack` calls only when the preflight result shows more detail is needed.

## Warm Cache And Freshness

`navlyn_workspace_summary`, `navlyn_workspace_status`, `navlyn_workspace_refresh`, `navlyn_file_outline`, and `navlyn_read` use a direct Core resolver path in the default in-process MCP server. A simple `navlyn_target` query also uses that path when a repository display root is available; other target forms use the command adapter. The first direct call loads a session-local workspace cache and builds a `DocumentIndex` for path-to-document lookup. Each later direct call checks workspace inputs by content before using the cached snapshot and again before returning success. A stable source or project edit causes a reload; an edit during a call causes one retry, then `NAVLYN_MCP_STALE_WORKSPACE` if inputs keep changing or cannot be inspected. No previous result is returned as a successful fallback. `navlyn_workspace_refresh` forces a reload even when inputs are unchanged. Concurrent calls hold snapshot leases so refresh does not dispose an in-use workspace. `navlyn_file_outline` records its entry `candidateId` targets only for that snapshot; after replacement, an old ID is resolved against the current solution or receives the existing candidate diagnostic.

Fuzzy symbol tools use a workspace-scoped declaration index and candidate record map. Same-snapshot follow-ups that pass a returned `candidateId` can resolve through the recorded candidate when the solution fingerprint matches; unknown or stale IDs still return deterministic Navlyn candidate diagnostics. Heavy reference and caller operations use lexical document prefiltering plus scoped Roslyn document-set searches. Their inner results include `search` metadata with `scope`, `costClass`, searched counts, `partial`, and rerun hints when `maxDocuments` truncates the semantic search.

The warm cache has no file watcher and is not shared across MCP server processes. Its checked inputs include selected workspace files, loaded projects and documents (including linked files outside the workspace root), and source/build/configuration files in the workspace and loaded project trees. It detects additions, deletions, same-size edits, and restored timestamps. An unexcluded junction or symbolic-link directory in a swept tree returns `NAVLYN_MCP_STALE_WORKSPACE` because recursively following it could escape the checked tree or loop; direct calls do not silently skip it. Arbitrary dynamically imported build files outside these trees and not reported by the loaded workspace are outside this inventory; use explicit refresh when changing such an input. The optional `.navlyn/cache` manifest is separate from the in-memory snapshot: it is opt-in via `cache: "on"` or `navlyn.workspace.json` `cacheHints.enabled`, stores no source text, records tracked file hashes/mtimes and project/document/declaration facts, and reports `fresh`, `missing`, `stale`, `invalid`, or `disabled`. Direct-path `snapshotId` combines the graph-level `workspaceFingerprint` with the checked input content digest; an unchanged explicit refresh may keep the same `snapshotId`. Direct metadata also reports `workspaceCacheStatus`, `indexStatus`, `freshnessStatus`, and document-index sizing. Daemon-backed `navlyn_workspace_status` reports daemon state, while daemon-backed refresh also replaces the local direct snapshot. Adapter-backed tools and the legacy `--navlyn-executable` mode preserve the existing CLI execution path and may load workspaces independently.

## Diff And Domain Flows

Review the current diff:

```text
navlyn_review(profile: "evidence")
```

Escalate from diff facts to `navlyn_tests_for_diff`, `navlyn_public_api_diff`, or `navlyn_context_pack(diff: true, goal: "review")` only when those facts are needed. Use `review-pack` through `navlyn_batch` only after deciding several batch-supported facts are needed.

Gather dedicated public API or DI facts:

```text
navlyn_public_api_diff(base: "main", profile: "evidence")
navlyn_di(mode: "impact", candidateId: "sym:v1:...", profile: "compact")
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
  "tool": "navlyn_target",
  "sourceCommand": {
    "command": "target",
    "arguments": ["target", "--workspace", "navlyn.slnx", "--query", "SymbolSourceResolver"]
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
    "command": "target",
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
  "tool": "navlyn_target",
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

`navlyn_navigate` is an allowlist tool, not an arbitrary command runner. Its `operation` is limited to `definition`, `references`, `callers`, `calls`, `implementations`, `type_hierarchy`, and `symbol_info`. Its target must be either a `candidateId` or an exact `file`/`line`/`column` source position. Reference usage filters (`usageKind`, `usageKinds`) and grouping (`groupBy`) are supported only for `operation: "references"`. `scope` and `maxDocuments` apply to `references` and `callers`; `calls` remains local to the containing member and reports `costClass: "local"`.

`navlyn_tests_for_symbol`, `navlyn_tests_for_diff`, `navlyn_di`, and `navlyn_public_api_diff` are allowlisted wrappers over their matching logical Navlyn commands. They do not run tests, edit files, publish packages, or execute arbitrary shell commands.

`navlyn_batch` wraps the existing Navlyn `batch` command only. Batch coverage includes `overview`, `diagnostics`, `symbols`, `symbols-in`, `outline`, `symbol-at`, `symbol-info`, `symbol-source`, `definition`, `references`, `implementations`, `type-hierarchy`, `callers`, `calls`, `find`, `resolve-target`, `where-used`, `about`, `related`, `impact`, `entrypoints`, `review-diff`, `review-pack`, `context-pack`, `repo-graph`, `public-api-diff`, `tests-for-symbol`, `tests-for-diff`, `framework-entrypoints`, `di-graph`, `where-registered`, `di-impact`, `route-map`, `route-impact`, `options-graph`, `config-impact`, `where-handled`, `message-flow`, `ef-model`, `entity-impact`, `package-usage`, and `package-impact`. These are logical CLI command names inside batch requests, not additional MCP tool names. Batch requests can use `candidateIdFrom` to feed an earlier result's `candidateId` into later supported requests. Direct CLI-only facts such as `changed-symbols`, `impact-diff`, `diagnostics-diff`, `scope-at`, `signature`, `symbol-diagnostics`, `diagnostic-pack`, and agent guard commands are not exposed through `navlyn_batch`; use dedicated MCP tools such as `navlyn_file_outline`, `navlyn_read`, `navlyn_navigate`, `navlyn_diagnostics`, and the canonical edit/review tools. Prefer `navlyn_batch` only when several batch-supported facts are already needed.

Static impact, framework entrypoint, DI, application domain, and review-pack results are bounded source-level facts. They are useful evidence for agents and reviewers, but they are not complete runtime proofs, runtime route tables, authorization proofs, secret/config value reads, EF runtime models, package compatibility scans, security scans, or replacement review comments.

## Performance Notes

The MCP server runs Navlyn commands in-process by default. This removes the external CLI process requirement and avoids CLI process startup overhead, but each standalone adapter-backed tool call still performs a conservative workspace load. Prefer `navlyn_batch` after the agent knows it needs several batch-supported facts from the same workspace, and use `profile: "compact"` or `profile: "evidence"` when output size is the limiting factor.

When `--navlyn-executable` is explicitly supplied, `navlyn-mcp` uses the legacy external CLI adapter. This escape hatch is useful for compatibility and debugging, not normal MCP installation.

Use `./scripts/measure-navlyn-performance.ps1` from the repository root to compare CLI direct calls, CLI batch, and MCP stdio tool calls for local performance investigation:

```powershell
./scripts/measure-navlyn-performance.ps1 -Workspace navlyn.slnx -Scenario mcp -Profile compact -Iterations 1 -Warmup 0 -NoBuild
```

The MCP scenario starts `navlyn-mcp`, initializes an MCP stdio session, and measures representative tool calls such as `navlyn_workspace_summary`, `navlyn_target`, `navlyn_navigate`, and `navlyn_context_pack`. Functional MCP behavior is covered by the MCP tests in the solution.

See [`navlyn-performance.md`](navlyn-performance.md) for broader performance guidance and release-readiness measurement notes.

See [`navlyn-architecture.md`](navlyn-architecture.md) for the shared core, CLI frontend, MCP frontend, and legacy external CLI boundaries.

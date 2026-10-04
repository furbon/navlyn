# Navlyn Architecture

Navlyn is split into shared implementation assemblies and two tool frontends. This architecture description is version-neutral; release identity belongs to the package and release documentation. The split keeps the public promise inspectable: one engine, deterministic JSON, read-only facts, and no hidden edit or network surface.

## Projects

- `Navlyn.Core`: Roslyn/MSBuild workspace loading, path handling, diagnostics, candidate IDs, resolver models, and semantic resolver implementations.
- `Navlyn.CommandLine`: the reusable command-line runtime, `System.CommandLine` command definitions, stdout/stderr JSON behavior, output profiles, and batch dispatch.
- `navlyn`: the packaged CLI .NET tool. It is a thin executable that configures console encoding and invokes `Navlyn.CommandLine`.
- `navlyn.Mcp`: the packaged MCP .NET tool. It owns MCP tool/resource/prompt schemas and result envelopes, calls `Navlyn.CommandLine` in-process by default, and uses direct Core resolver paths for selected cheap reader tools.

`navlyn.Mcp` must not reference the `navlyn` executable project. The MCP package includes the shared assemblies through project references, so installing `navlyn-mcp` alone is enough for normal MCP use.

## Execution Paths

CLI:

1. `navlyn` parses CLI arguments through `Navlyn.CommandLine`.
2. Command handlers load the workspace through `Navlyn.Core`.
3. Resolver results are formatted as the existing deterministic CLI JSON on stdout.
4. Diagnostics, progress, and errors stay on stderr.

MCP default:

1. `navlyn.Mcp` receives an MCP tool, resource, or prompt request.
2. MCP arguments are validated and mapped to an allowlisted logical Navlyn command.
3. Reader-path tools such as `navlyn_workspace_summary`, `navlyn_workspace_status`, `navlyn_workspace_refresh`, `navlyn_file_outline`, and `navlyn_read` use direct Core resolver paths with a lazy per-server workspace cache and `DocumentIndex`. All supported `navlyn_target` variants and focused `navlyn_navigate` operations execute the existing CLI resolvers against the borrowed cached workspace. The cache lease remains owned by MCP, and freshness is validated before the response.
4. Other tools, and target calls with additional selection options or without a repository display root, use `NavlynInProcessCommandAdapter`, which runs the shared command runtime in-process.
5. The MCP result envelope returns `sourceCommand` for traceability and the command JSON under `result`.

The `read`/`symbol-source` path can opt into `metadata` or `decompiled` external-member reads. It retains Roslyn's exact call-site binding and selected project target framework, matches the compile-time reference PE to local package/runtime assets or a direct implementation reference, and decompiles one exact member in a separate killable worker. It reports reference and implementation content hashes and validates the selected inputs before returning. The default `externalSource: "none"` path keeps the existing behavior and does not inspect external binaries. External slices use non-editable `navlyn-metadata://` or `navlyn-decompiled://` paths and contain reconstructed text, not a claim of original source.

MCP legacy external CLI:

1. This path is used only when `--navlyn-executable` is explicitly supplied.
2. `NavlynCliRunner` starts the configured process and maps stdout/stderr into the same MCP envelope.
3. This remains a compatibility, debugging, and development escape hatch, not the normal install path.

## Cache Boundary

The MCP server reuses its process, loaded assemblies, command runtime, MSBuildLocator registration, a lazy workspace cache, and a workspace-scoped `DocumentIndex` for direct reader tools, target selection, and focused navigation. `navlyn_file_outline` seeds an in-memory candidate target map for the current server process, so immediate `navlyn_read(candidateId: "...", view: "declaration")` follow-ups can avoid a broad candidate scan. Tools that still run through the command adapter preserve the existing CLI behavior and may load the workspace independently.

The direct cache is session-local and has no file watcher. It hashes selected workspace, loaded project/document, and workspace-tree source/build inputs before leasing a snapshot and before returning a successful direct result. Stable changes reload the workspace; changes during a call receive one retry and then a deterministic stale-workspace error. Each snapshot generation owns its candidate-position map and remains alive until overlapping calls release their leases. An explicit refresh replaces the local generation even if a configured daemon answers its refresh request. The content-sensitive `snapshotId` identifies checked inputs as well as the graph; `workspaceFingerprint` remains the graph identity. Adapter-backed tools may load independently, and a later direct call checks its own inputs. Use `navlyn_batch` when several batch-supported adapter-backed facts should share one workspace load. Navlyn does not add an editing surface, network access, or arbitrary command execution.

`navlyn serve` is an opt-in local read-only daemon for workspace lifecycle requests. It accepts newline-delimited JSON over stdin/stdout, or a local named pipe when `--pipe` is supplied. CLI `workspace-status` / `workspace-refresh` and MCP `navlyn_workspace_status` / `navlyn_workspace_refresh` can connect to that pipe only when explicitly configured. If a configured daemon is unavailable, callers fall back to the normal stateless or in-process path.

The on-disk cache under `.navlyn/cache` is a lightweight manifest, not a serialized Roslyn workspace. It records workspace fingerprints, project graph facts, document-index facts, declaration syntax facts, tracked file hashes/mtimes, SDK/global.json/Navlyn/Roslyn/runtime version fingerprints, and an explicit marker that session-local candidate records are not persisted. Freshness checks reject stale manifests instead of reusing them.

Fuzzy symbol discovery uses a workspace-scoped declaration index. The index records syntax declaration names, paths, document IDs, project IDs, generated-file status, and source spans before semantic enrichment. Common fuzzy/resolve queries first narrow syntax declarations, then enrich matching entries with Roslyn semantic facts. Enriched declarations are cached per solution, and emitted `sym:v1:` candidate IDs are recorded in a solution-fingerprint-validated candidate map so same-snapshot candidate-id follow-ups can resolve without broad declaration rediscovery.

Expensive reverse-edge operations use `SymbolNavigationSearchOptions` and `SymbolNavigationSearchPlanner` to build scoped document sets before semantic search. `references` and `callers` can search `file`, `project`, `dependent-projects`, `workspace-set`, or `solution`, apply a lexical document prefilter, and return successful partial metadata when the document budget is reached. `calls` remains a local containing-member analysis path.

## Release Hardening Ledger

These are known architecture pressure points for future releases. They are not requirements of the current public contract because the implementation is covered by focused tests, schemas, and CLI/MCP contract checks.

| Area | Current Boundary | Future Split Trigger |
| --- | --- | --- |
| Ambiguity classifier | `resolve-target` computes `ambiguitySummary` additively from current candidates. | Extract when more command families need the same reason taxonomy or localized explanations. |
| Version provider | `Directory.Build.props` centralizes package and assembly version identity; runtime envelopes read assembly informational versions. | Extract when release metadata needs richer build provenance or package manifest validation outside MSBuild. |
| Next action builder | Fuzzy resolvers and MCP wrappers build next-action hints near command-specific logic. | Extract when recommended actions need shared policy tests across CLI, MCP, and batch. |
| Source slice budgeter | Source/context commands own their own line/token limits. | Extract when multiple commands need one consistent cross-command source budget policy. |
| MCP command builder policy tests | `NavlynToolCommandBuilderTests` and evals guard high-risk tool selection and argument mapping. | Broaden when a new first-class MCP tool or batch recipe changes default tool-choice behavior. |

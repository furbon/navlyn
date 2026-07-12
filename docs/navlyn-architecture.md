# Navlyn Architecture

Navlyn 0.7.0 is split into shared implementation assemblies and two tool frontends. The split is meant to keep the public promise boring and inspectable: one engine, deterministic JSON, read-only facts, and no hidden edit or network surface.

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
3. Reader-path tools such as `navlyn_workspace_summary`, `navlyn_workspace_status`, `navlyn_workspace_refresh`, `navlyn_file_outline`, `navlyn_inspect_file`, and `navlyn_symbol_source` use direct Core resolver paths with a lazy per-server workspace cache and `DocumentIndex`.
4. Other tools use `NavlynInProcessCommandAdapter`, which runs the shared command runtime in-process.
5. The MCP result envelope returns `sourceCommand` for traceability and the command JSON under `result`.

MCP legacy external CLI:

1. This path is used only when `--navlyn-executable` is explicitly supplied.
2. `NavlynCliRunner` starts the configured process and maps stdout/stderr into the same MCP envelope.
3. This remains a compatibility, debugging, and development escape hatch, not the normal install path.

## Cache Boundary

The MCP server reuses its process, loaded assemblies, command runtime, MSBuildLocator registration, a lazy workspace cache, and a workspace-scoped `DocumentIndex` for direct reader tools. `navlyn_file_outline` seeds an in-memory candidate target map for the current server process, so immediate `navlyn_symbol_source(candidateId: "...")` follow-ups can avoid a broad candidate scan. Tools that still run through the command adapter preserve the existing CLI behavior and may load the workspace independently.

The direct cache is session-local and has no file watcher. Use `navlyn_workspace_refresh` or restart the MCP server after source or project changes when freshness matters. Use `navlyn_batch` when several batch-supported adapter-backed facts should share one workspace load. Navlyn does not add an editing surface, network access, or arbitrary command execution.

`navlyn serve` is an opt-in local read-only daemon for workspace lifecycle requests. It accepts newline-delimited JSON over stdin/stdout, or a local named pipe when `--pipe` is supplied. CLI `workspace-status` / `workspace-refresh` and MCP `navlyn_workspace_status` / `navlyn_workspace_refresh` can connect to that pipe only when explicitly configured. If a configured daemon is unavailable, callers fall back to the normal stateless or in-process path.

The on-disk cache under `.navlyn/cache` is a lightweight manifest, not a serialized Roslyn workspace. It records workspace fingerprints, project graph facts, document-index facts, declaration syntax facts, tracked file hashes/mtimes, SDK/global.json/Navlyn/Roslyn/runtime version fingerprints, and an explicit marker that session-local candidate records are not persisted. Freshness checks reject stale manifests instead of reusing them.

Fuzzy symbol discovery uses a workspace-scoped declaration index. The index records syntax declaration names, paths, document IDs, project IDs, generated-file status, and source spans before semantic enrichment. Common fuzzy/resolve queries first narrow syntax declarations, then enrich matching entries with Roslyn semantic facts. Enriched declarations are cached per solution, and emitted `sym:v1:` candidate IDs are recorded in a solution-fingerprint-validated candidate map so same-snapshot candidate-id follow-ups can resolve without broad declaration rediscovery.

Expensive reverse-edge operations use `SymbolNavigationSearchOptions` and `SymbolNavigationSearchPlanner` to build scoped document sets before semantic search. `references` and `callers` can search `file`, `project`, `dependent-projects`, `workspace-set`, or `solution`, apply a lexical document prefilter, and return successful partial metadata when the document budget is reached. `calls` remains a local containing-member analysis path.

## Release Hardening Ledger

These are known architecture pressure points for future releases. They are not required for the v0.7.0 public contract because the current implementation is covered by focused tests, schemas, and CLI/MCP contract checks.

| Area | Current Boundary | Future Split Trigger |
| --- | --- | --- |
| Ambiguity classifier | `resolve-target` computes `ambiguitySummary` additively from current candidates. | Extract when more command families need the same reason taxonomy or localized explanations. |
| Version provider | `Directory.Build.props` centralizes package and assembly version identity; runtime envelopes read assembly informational versions. | Extract when release metadata needs richer build provenance or package manifest validation outside MSBuild. |
| Next action builder | Fuzzy resolvers and MCP wrappers build next-action hints near command-specific logic. | Extract when recommended actions need shared policy tests across CLI, MCP, and batch. |
| Source slice budgeter | Source/context commands own their own line/token limits. | Extract when multiple commands need one consistent cross-command source budget policy. |
| MCP command builder policy tests | `NavlynToolCommandBuilderTests` and evals guard high-risk tool selection and argument mapping. | Broaden when a new first-class MCP tool or batch recipe changes default tool-choice behavior. |

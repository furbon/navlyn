# Navlyn Performance

Navlyn loads C# workspaces through MSBuild/Roslyn, so performance depends on repository size, restore/build health, SDKs, and the workflow you choose. This document explains the cost model, the faster paths, and the local measurement commands that make performance visible instead of mysterious.

Practical rule: use one precise fact first, reuse returned `candidateId` values, and escalate only when the returned evidence shows the next fact is needed.

| Workflow | Best For | Cost Shape |
| --- | --- | --- |
| Direct CLI command | Human asks for one fact. | One process and one workspace load per command. |
| MCP reader tools | Agent repeatedly inspects files or selected symbols. | Session-local warm workspace and document index for selected direct tools. |
| CLI `batch` / MCP `navlyn_batch` | Several known facts from one workspace. | One command envelope for multiple supported facts. |
| `compact` profile | First scans and LLM context. | Smaller JSON and less downstream token pressure. |
| `evidence` profile | Review/CI facts. | Enough detail for inspection without full output size. |

## Execution Model

- CLI commands load the configured workspace for each process invocation.
- `navlyn-mcp` is a read-only stdio server that runs Navlyn commands in-process by default through the shared engine.
- MCP reader-path tools (`navlyn_workspace_summary`, `navlyn_workspace_status`, `navlyn_workspace_refresh`, `navlyn_file_outline`, `navlyn_inspect_file`, and `navlyn_symbol_source`) use a direct Core resolver path with a lazy per-server workspace cache and workspace-scoped `DocumentIndex`.
- `navlyn_batch` in `--tool-profile full` can reduce repeated workspace loads when several batch-supported facts should be collected together.
- `navlyn serve` is an opt-in local read-only daemon for workspace status/refresh requests over stdio JSON lines or a local named pipe.
- `.navlyn/cache/workspace-index.json` is an opt-in lightweight manifest for freshness and index facts, not a serialized Roslyn workspace.
- `compact` and `evidence` profiles can reduce output size and downstream token pressure.

Navlyn does not include a file watcher, telemetry pipeline, hosted service, network listener, or write surface. The MCP direct workspace cache, `DocumentIndex`, and declaration/candidate indexes are session-local and should be refreshed with `navlyn_workspace_refresh` or by restarting the MCP server after source or project changes when freshness matters. Adapter-backed tools still preserve the CLI execution path and may load the workspace independently. Use `navlyn_batch` from `--tool-profile full` when several batch-supported adapter-backed facts should share one workspace load.

The on-disk cache is privacy-conscious and freshness-oriented. It stores workspace/version fingerprints, project graph facts, document-index facts, declaration syntax facts when written by `workspace-refresh --write-cache`, tracked file hashes/mtimes, and `candidateRecordsStored: false`. It does not store source text or semantic models. `workspace-status --cache on` reports `fresh`, `missing`, `stale`, `invalid`, or `disabled`; stale manifests are rejected rather than reused.

Reverse-edge operations are bounded. `references`, `callers`, `about`, and `impact` default heavy semantic search to `dependent-projects`, lexically prefilter documents by the selected symbol name, pass a document set to Roslyn where supported, and cap searched documents with `--max-documents`. Successful partial results report `search.partial`, searched counts, and rerun hints. `calls` stays local to the containing member and reports `search.costClass: "local"`.

## Measure Locally

Use the performance script from the repository root:

```powershell
./scripts/measure-navlyn-performance.ps1 -Workspace navlyn.slnx -Scenario quick -Iterations 1 -Warmup 0 -NoBuild
./scripts/measure-navlyn-performance.ps1 -Workspace navlyn.slnx -Scenario file-first -Iterations 1 -Warmup 0 -NoBuild
./scripts/measure-navlyn-performance.ps1 -Workspace navlyn.slnx -Scenario agent-loop -Profile compact -Iterations 3 -Output artifacts/navlyn-agent-loop.json
./scripts/measure-navlyn-performance.ps1 -Workspace navlyn.slnx -Scenario mcp -Profile compact -Iterations 1 -Warmup 0 -NoBuild
./scripts/measure-navlyn-performance.ps1 -Workspace navlyn.slnx -Scenario daemon -Iterations 1 -Warmup 0 -NoBuild
./scripts/measure-navlyn-performance.ps1 -Workspace navlyn.slnx -Scenario cache -Iterations 1 -Warmup 0 -NoBuild
./scripts/measure-navlyn-performance.ps1 -Workspace navlyn.slnx -Scenario parallel -Iterations 1 -Warmup 0 -NoBuild
./scripts/measure-navlyn-performance.ps1 -Workspace navlyn.slnx -Scenario multi-workspace -Iterations 1 -Warmup 0 -NoBuild
```

Reports are structured JSON with:

- command/tool name and arguments;
- elapsed milliseconds;
- stdout and stderr sizes;
- exit code;
- JSON validity;
- top-level command/profile;
- result counts;
- truncation state;
- warnings;
- optional MCP metadata such as `executionPath`, `workspaceCacheStatus`, `workspaceCacheHit`, `workspaceFingerprint`, `snapshotId`, `freshnessStatus`, and document-index sizing;
- timeout/skipped status.

The `cache` scenario writes its manifest under ignored `artifacts/performance-cache`. The `daemon` scenario uses local stdio JSON-lines requests so it does not leave a background server running. The `parallel` scenario starts same-workspace CLI processes concurrently, and `multi-workspace` compares the primary workspace with a fixture workspace.

Timings are environment-dependent. Treat local reports as release and investigation evidence, not a universal service-level objective.

## Current Local Case Study

The following smoke evidence was recorded on 2026-07-04 from commit `b17f3b5` plus release-readiness changes, on Windows 10.0.26200 with .NET SDK 10.0.301 and runtime 10.0.9. The report was produced with `navlyn.workspace.json`, `-Scenario all`, `-Profile compact`, `-Iterations 1`, `-Warmup 0`, and `-NoBuild`; all measured commands returned JSON-valid stdout, exit code 0, and stderr size 0. The aggregate report has `anyTruncated=true` because the compact diff scenario intentionally exercises bounded outputs; non-diff scenarios were not truncated.

| Scenario | Profile | Commands | Median ms | P95 ms | Max stdout chars | Warnings | Comparison baseline |
| --- | --- | ---: | ---: | ---: | ---: | --- | --- |
| quick | compact | 4 | 4308 | 5799 | 4498 | none | Stateless CLI check/repo-graph/find/context-pack. |
| file-first | compact | 3 | 4321 | 4358 | 8361 | none | Stateless CLI outline/source/calls for a known file position. |
| agent-loop | compact | 8 | 5795 | 6056 | 8968 | `no-selected-symbol` on tests-for-symbol only | Stateless CLI agent loop plus batch comparison. |
| diff | compact | 7 | 13871 | 16692 | 109126 | documented bounded diff warnings and truncation flags | Changed-symbol, impact, diagnostics, review, context, test, and public API diff workflow. |
| mcp | compact | 4 | 1938 | 2809 | 19908 | none | MCP stdio warm-loop; direct tools report warm workspace/index metadata. |
| daemon | compact | 2 | 2821 | 2864 | 2679 | none | `navlyn serve` stdio status/refresh one-shot requests. |
| cache | compact | 3 | 5193 | 5213 | 4177 | none | On-disk cache cold refresh, warm status, warm refresh. |
| parallel | compact | 3 | 5979 | 5979 | 8361 | none | Same-workspace CLI repo-graph/find/outline processes started concurrently. |
| multi-workspace | compact | 3 | 2776 | 2893 | 115481 | none | Primary workspace check plus fixture workspace check/outline. |

These numbers are a reproducibility snapshot for release review, not a claim that other repositories or machines will match them.

## Reading A Report

For agent adoption decisions, inspect more than elapsed time:

- `elapsedMs`: wall-clock cost for a single command or tool call.
- stdout size: downstream parsing and LLM context pressure.
- stderr size and exit code: workspace load health, warnings, and failures.
- JSON validity and top-level command/profile: whether automation can safely parse the result.
- result counts: candidate count, changed symbol count, related files, tests, routes, or diagnostics.
- truncation flags and warnings: whether the chosen profile or limits hid useful evidence.
- MCP metadata: whether a tool used the direct path, whether the workspace cache was hit, which session-local `snapshotId` / `workspaceFingerprint` produced the result, and how large the in-memory document index is.
- fuzzy/index behavior: whether repeated fuzzy or candidate-id flows reuse semantic enrichment in the same workspace snapshot.
- expected files: whether the files a maintainer expects are present in related/context outputs.

Record the SDK, operating system, repository commit, workspace path, Navlyn version, scenario, profile, iterations, and whether the first run included restore/build/cache warmup.

## Choosing A Workflow

Use direct CLI commands when:

- a human is running one fact at a time;
- the result is small;
- process startup is not the bottleneck.

Use CLI `batch` or MCP `navlyn_batch` in `--tool-profile full` when:

- an agent needs several facts from the same workspace;
- repeated workspace loads are expensive;
- the desired commands are batch-supported.

Use `compact` when:

- the caller needs a first scan;
- MCP output size is the limiting factor;
- the result is headed to an LLM context window.

Use `evidence` when:

- review or CI facts need enough detail for inspection;
- snippets or large nested arrays should be reduced.

Use `full` when:

- downstream tooling expects the richest command-specific JSON shape;
- compatibility with the full CLI contract matters more than output size.

## Agent-Loop Patterns

Prefer this pattern for a file-first MCP loop:

```text
navlyn_file_outline(file: "Navlyn.CommandLine/Cli/Commands/CheckCommand.cs")
navlyn_symbol_source(candidateId: "sym:v1:...", view: "declaration")
navlyn_symbol_edges(operation: "calls", candidateId: "sym:v1:...", limit: 30)
```

For CLI users, the comparable file-first facts are:

```powershell
navlyn outline --workspace navlyn.slnx --file Navlyn.CommandLine/Cli/Commands/CheckCommand.cs
navlyn symbol-source --workspace navlyn.slnx --file Navlyn.CommandLine/Cli/Commands/CheckCommand.cs --line 6 --column 23 --view declaration
navlyn calls --workspace navlyn.slnx --file Navlyn.CommandLine/Cli/Commands/CheckCommand.cs --line 6 --column 23 --limit 30
```

Use broad context only when project structure or a reading queue matters:

```powershell
navlyn repo-graph --workspace navlyn.slnx --profile compact
navlyn resolve-target --workspace navlyn.slnx --query CheckCommand --assume-kind NamedType --limit 10
navlyn context-pack --workspace navlyn.slnx --query CheckCommand --assume-kind NamedType --goal modify --profile compact --budget-tokens 8000
```

Prefer batch when the agent already knows it needs several facts:

```powershell
Get-Content examples/batch/investigation-loop.json | navlyn batch --workspace navlyn.slnx
```

For MCP clients, `navlyn_batch` remains useful in `full` profile after the agent already knows it needs several batch-supported facts. Prefer the direct reader tools for workspace summary, a single known file, or a selected symbol because they reuse the MCP workspace cache and `DocumentIndex` without encouraging broad fact collection.

For fuzzy symbol workflows, prefer reusing `candidateId` values returned by `find`, `resolve-target`, and MCP outline/source tools. Candidate records are validated against the current solution fingerprint, so same-snapshot follow-ups can skip broad declaration rediscovery while stale or unknown IDs still fall back to deterministic validation and diagnostics. Use `about --profile light` and `impact --profile light` for first-pass agent calls; expand to `full`, a broader `--scope`, or a larger `--max-documents` only when the returned facts show that the broader search is needed.

Do not interpret faster compact output as better semantic coverage. It is smaller by design. If a compact result warns about truncation or omits the expected file, rerun with higher limits, `evidence`, or `full`.

## Release Readiness

Before a public release, run at least one quick performance smoke:

```powershell
dotnet build navlyn.slnx
./scripts/measure-navlyn-performance.ps1 -Workspace navlyn.slnx -Scenario quick -Iterations 1 -Warmup 0 -NoBuild
```

For MCP release confidence, also run:

```powershell
./scripts/measure-navlyn-performance.ps1 -Workspace navlyn.slnx -Scenario mcp -Profile compact -Iterations 1 -Warmup 0 -NoBuild
./scripts/measure-navlyn-performance.ps1 -Workspace navlyn.slnx -Scenario file-first -Iterations 1 -Warmup 0 -NoBuild
./scripts/measure-navlyn-performance.ps1 -Workspace navlyn.slnx -Scenario daemon -Iterations 1 -Warmup 0 -NoBuild
./scripts/measure-navlyn-performance.ps1 -Workspace navlyn.slnx -Scenario cache -Iterations 1 -Warmup 0 -NoBuild
```

Keep generated reports under ignored paths such as `artifacts/performance-smoke/`.

## Measurement Review Checklist

When repeated measurements show that workspace load dominates real agent workflows, evaluate:

- broader warm workspace cache coverage with explicit invalidation semantics;
- on-disk symbol index;
- benchmark corpus and variance tracking;
- CI performance budgets after baseline variance is understood.

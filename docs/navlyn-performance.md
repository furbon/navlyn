# Navlyn Performance

Navlyn loads C# workspaces through MSBuild/Roslyn, so performance depends on repository size, restore/build health, SDKs, and the workflow you choose. This document explains the cost model, the faster paths, and the local measurement commands that make performance visible instead of mysterious.

Practical rule: use one precise fact first, reuse returned `candidateId` values, and escalate only when the returned evidence shows the next fact is needed.

| Workflow | Best For | Cost Shape |
| --- | --- | --- |
| Direct CLI command | Human asks for one fact. | One process and one workspace load per command. |
| MCP semantic tools | Agent repeatedly inspects files, selected symbols, edit evidence, or review facts. | Session-local warm workspace and document index for selected direct tools. |
| CLI `batch` / MCP `navlyn_batch` | Several known facts from one workspace. | One command envelope for multiple supported facts. |
| `compact` profile | First scans and LLM context. | Smaller JSON and less downstream token pressure. |
| `evidence` profile | Review/CI facts. | Enough detail for inspection without full output size. |

## Execution Model

- CLI commands load the configured workspace for each process invocation.
- `navlyn-mcp` is a read-only stdio server that runs Navlyn commands in-process by default through the shared engine.
- MCP reader-path tools (`navlyn_workspace_summary`, `navlyn_workspace_status`, `navlyn_workspace_refresh`, `navlyn_file_outline`, and `navlyn_read`) use a direct Core resolver path with a lazy per-server workspace cache and workspace-scoped `DocumentIndex`. A simple `navlyn_target` query also uses that path when the workspace has a repository display root; target calls with other selection options use the command adapter.
- `navlyn_read` and CLI `read`/`symbol-source` default to `externalSource=none`; external metadata and reconstructed-member reads are explicit opt-ins.
- `navlyn_batch` can reduce repeated workspace loads when several batch-supported facts should be collected together.
- `navlyn serve` is an opt-in local read-only daemon for workspace status/refresh requests over stdio JSON lines or a local named pipe.
- `.navlyn/cache/workspace-index.json` is an opt-in lightweight manifest for freshness and index facts, not a serialized Roslyn workspace.
- `compact` and `evidence` profiles can reduce output size and downstream token pressure.

Navlyn does not include a file watcher, telemetry pipeline, hosted service, network listener, or write surface. The MCP direct workspace cache, `DocumentIndex`, and declaration/candidate indexes are session-local. Each direct call hashes checked workspace inputs before reuse and before returning success, so warm calls cost more than a cache lookup but detect content edits without relying on timestamps. A stable source or project edit reloads the snapshot automatically; `navlyn_workspace_refresh` remains available to force a reload. Adapter-backed tools still preserve the CLI execution path and may load the workspace independently. Use `navlyn_batch` when several batch-supported adapter-backed facts should share one workspace load.

The on-disk cache is privacy-conscious and freshness-oriented. It stores workspace/version fingerprints, project graph facts, document-index facts, declaration syntax facts when written by `workspace-refresh --write-cache`, tracked file hashes/mtimes, and `candidateRecordsStored: false`. It does not store source text or semantic models. `workspace-status --cache on` reports `fresh`, `missing`, `stale`, `invalid`, or `disabled`; stale manifests are rejected rather than reused.

External member decompilation is opt-in and uses a child process with a 10-second deadline so blocked decompilation can be terminated. Inputs are bounded to 64 MiB per PE and 1 MiB of IL for the selected method; the returned member is then bounded by `maxLines` and `budgetTokens`. A metadata read uses the selected reference PE without loading the decompiler worker. Decompiled results hash and validate the selected reference/assets/implementation around the worker and MCP response, so their latency is separate from ordinary warm `navlyn_read` and depends on local storage and package size. Package restore, network fetch, and target-assembly execution are not part of this read path.

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
./scripts/measure-navlyn-performance.ps1 -Workspace navlyn.slnx -Scenario quick -Iterations 1 -Warmup 0 -NoBuild -IncludeStageTimings
```

For `-Scenario mcp`, all warmup and measured rounds use one persistent server. Reports include commit, dirty state, tool version, OS, SDK, processor count, iteration/warmup counts, and each tool's cold/warm cache phase. Use `-Baseline <report>` for per-command comparisons on the same environment; mismatched workspace, scenario, profile, OS, SDK, or processor count is rejected. Adapter calls report `adapter` as their phase because they do not reuse the direct workspace cache.

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
- optional CLI stage timings from `NAVLYN_PROFILE_TIMINGS=1`, including startup-adjacent command time, workspace discovery, MSBuild registration/load, project selection, document-index construction, resolver execution, and serialization;
- timeout/skipped status.

`-IncludeStageTimings` sets `NAVLYN_PROFILE_TIMINGS=1` for CLI child processes and parses `NAVLYN_TIMING` lines from stderr into `stageTimings`, `stageBreakdown`, and `summary.topStageBottlenecks`. Those diagnostic lines are opt-in and are not part of normal command stdout. The `cache` scenario writes its manifest under ignored `artifacts/performance-cache`. The `daemon` scenario uses local stdio JSON-lines requests so it does not leave a background server running. The `parallel` scenario starts same-workspace CLI processes concurrently, and `multi-workspace` compares the primary workspace with a fixture workspace.

Timings are environment-dependent. Treat local reports as release and investigation evidence, not a universal service-level objective.

## Historical v0.8.6 MCP smoke

On 2026-10-04, the v0.8.6 release candidate was measured on Windows 10.0.26200 with SDK 10.0.401, workspace `navlyn.slnx`, scenario `mcp`, profile `compact`, one iteration, and no warmup or build. Local release CLI validation ran concurrently, so these are functional smoke observations rather than a comparison benchmark. All four calls returned valid JSON and success; no sample was truncated.

| Tool | Path | Elapsed ms | Response chars |
| --- | --- | ---: | ---: |
| `navlyn_workspace_summary` | Direct, cold workspace load | 13732 | 4729 |
| `navlyn_file_outline` | Direct, warm snapshot | 2005 | 19868 |
| `navlyn_read` | Direct, warm snapshot | 112 | 7363 |
| `navlyn_navigate` (`calls`) | CLI adapter | 2378 | 22045 |

The first call includes workspace loading; only outline and read reuse its warm direct snapshot. Adapter-backed navigation has a separate load cost. The measurement script now uses current tool names and a method declaration as its default source position. It saves reports before failing on nonzero commands, invalid JSON, unexpected CLI diagnostics, or skipped prerequisites, and supports absolute output paths. CI release validation runs quick CLI and MCP smoke; historical results below are preserved as dated evidence.

## Historical 0.7.0 Local Case Study

The following smoke evidence was recorded on 2026-07-05 from the 0.7.0 release branch, on Windows 10.0.26200 with .NET SDK 10.0.301. The report was produced with `navlyn.slnx`, `-Scenario all`, `-Profile compact`, `-Iterations 1`, `-Warmup 0`, and `-NoBuild`; all measured commands returned JSON-valid stdout, exit code 0, and stderr size 0.

| Scenario | Profile | Commands | Median ms | P95 ms | Max stdout chars | Warnings | Comparison baseline |
| --- | --- | ---: | ---: | ---: | ---: | --- | --- |
| quick | compact | 4 | 3203 | 6191 | 4498 | none | Stateless CLI check/repo-graph/find/context-pack. |
| file-first | compact | 3 | 4572 | 4634 | 8361 | none | Stateless CLI outline/source/calls for a known file position. |
| agent-loop | compact | 8 | 6131 | 6542 | 8968 | none | Stateless CLI agent loop plus batch comparison. |
| diff | compact | 7 | 8860 | 10089 | 3110 | none | Changed-symbol, impact, diagnostics, review, context, test, and public API diff workflow. |
| mcp | compact | 4 | 1623 | 4320 | 19868 | none | MCP stdio warm-loop; direct tools report warm workspace/index metadata. |
| daemon | compact | 2 | 3028 | 3033 | 2664 | none | `navlyn serve` stdio status/refresh one-shot requests. |
| cache | compact | 3 | 5462 | 5574 | 4162 | none | On-disk cache cold refresh, warm status, warm refresh. |
| parallel | compact | 3 | 6433 | 6434 | 8361 | none | Same-workspace CLI repo-graph/find/outline processes started concurrently. |
| multi-workspace | compact | 3 | 2944 | 3060 | 115481 | none | Primary workspace check plus fixture workspace check/outline. |

These numbers are a reproducibility snapshot for release review, not a claim that other repositories or machines will match them. For 0.6.x work, consider a performance smoke healthy only when commands succeed, stdout is valid JSON, successful stderr is empty, truncation is expected and documented, and expected files are present in related/context/review outputs.

## Workflow Targets

Targets are local guardrails, not hosted-service SLOs:

- MCP reader warm-loop should stay the fastest path for repeated selected-symbol facts.
- Direct CLI first-pass commands should remain suitable for occasional human calls; agents should use MCP or batch when collecting several facts from the same workspace.
- Diff review should keep evidence output bounded and JSON-valid before chasing lower latency.
- Performance regressions should be explained by workspace size, command scope, output size, warnings/truncation, or an intentional semantic coverage change.

## Reading A Report

For agent adoption decisions, inspect more than elapsed time:

- `elapsedMs`: wall-clock cost for a single command or tool call.
- stdout size: downstream parsing and LLM context pressure.
- stderr size and exit code: workspace load health, warnings, and failures.
- JSON validity and top-level command/profile: whether automation can safely parse the result.
- result counts: candidate count, changed symbol count, related files, tests, routes, or diagnostics.
- truncation flags and warnings: whether the chosen profile or limits hid useful evidence.
- MCP metadata: whether a tool used the direct path, whether the workspace cache was hit, which content-sensitive `snapshotId` and graph-level `workspaceFingerprint` produced the result, and how large the in-memory document index is.
- stage timings: whether startup, workspace load, project selection, resolver execution, serialization, or MCP path overhead dominates the measured workflow.
- fuzzy/index behavior: whether repeated fuzzy or candidate-id flows reuse semantic enrichment in the same workspace snapshot.
- expected files: whether the files a maintainer expects are present in related/context outputs.

Record the SDK, operating system, repository commit, workspace path, Navlyn version, scenario, profile, iterations, and whether the first run included restore/build/cache warmup.

## Choosing A Workflow

Use direct CLI commands when:

- a human is running one fact at a time;
- the result is small;
- process startup is not the bottleneck.

Use CLI `batch` or MCP `navlyn_batch` when:

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
navlyn_read(candidateId: "sym:v1:...", view: "declaration")
navlyn_navigate(operation: "calls", candidateId: "sym:v1:...", limit: 30)
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

For MCP clients, `navlyn_batch` remains useful after the agent already knows it needs several batch-supported facts. Prefer direct focused tools for workspace summary, a single known file, or a selected symbol because they reuse the MCP workspace cache and `DocumentIndex` without encouraging broad fact collection.

For fuzzy symbol workflows, prefer reusing `candidateId` values returned by CLI `find` / `resolve-target` and MCP `navlyn_target` / `navlyn_file_outline`. Candidate records are validated against the current solution fingerprint, so same-snapshot follow-ups can skip broad declaration rediscovery while stale or unknown IDs still fall back to deterministic validation and diagnostics. Use CLI `about --profile light` or MCP `navlyn_navigate(operation: "symbol_info")` and `navlyn_impact` for first-pass selected-symbol facts; expand to a richer profile, a broader `scope`, or a larger `maxDocuments` only when the returned facts show that the broader search is needed.

Do not interpret faster compact output as better semantic coverage. It is smaller by design. If a compact result warns about truncation or omits the expected file, rerun with higher limits, `evidence`, or `full`.

## Release Readiness

Before a public release, run at least one quick performance smoke:

```powershell
dotnet build navlyn.slnx
./scripts/measure-navlyn-performance.ps1 -Workspace navlyn.slnx -Scenario quick -Iterations 1 -Warmup 0 -NoBuild
./scripts/measure-navlyn-performance.ps1 -Workspace navlyn.slnx -Scenario quick -Iterations 1 -Warmup 0 -NoBuild -IncludeStageTimings
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

## v0.9.0

See [measured product evidence](evals/v0.9.0-product-evidence.md) for the same-workspace warm comparison, discovery size, fixed live tasks, and classified public-package corpus results. Measurements are product experiments, not additional CI gates.

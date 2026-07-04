# Navlyn Performance

Navlyn loads C# workspaces through MSBuild/Roslyn. On large repositories, workspace load cost can dominate command latency. This document explains how to measure that behavior and how to choose lower-cost workflows.

## Execution Model

- CLI commands load the configured workspace for each process invocation.
- `navlyn-mcp` is a read-only stdio server that runs Navlyn commands in-process by default through the shared engine.
- File-first MCP tools (`navlyn_file_outline`, `navlyn_inspect_file`, and `navlyn_symbol_source`) use a direct Core resolver path with a lazy per-server workspace cache.
- `navlyn_batch` can reduce repeated workspace loads when several batch-supported facts should be collected together.
- `compact` and `evidence` profiles can reduce output size and downstream token pressure.

Navlyn does not currently include a daemon, file watcher, on-disk index, telemetry pipeline, or hosted service. The MCP direct workspace cache is session-local and should be refreshed by restarting the MCP server after source or project changes when freshness matters. Adapter-backed tools still preserve the CLI execution path and may load the workspace independently. Use `navlyn_batch` when several batch-supported adapter-backed facts should share one workspace load.

## Measure Locally

Use the performance script from the repository root:

```powershell
./scripts/measure-navlyn-performance.ps1 -Workspace navlyn.slnx -Scenario quick -Iterations 1 -Warmup 0 -NoBuild
./scripts/measure-navlyn-performance.ps1 -Workspace navlyn.slnx -Scenario file-first -Iterations 1 -Warmup 0 -NoBuild
./scripts/measure-navlyn-performance.ps1 -Workspace navlyn.slnx -Scenario agent-loop -Profile compact -Iterations 3 -Output artifacts/navlyn-agent-loop.json
./scripts/measure-navlyn-performance.ps1 -Workspace navlyn.slnx -Scenario mcp -Profile compact -Iterations 1 -Warmup 0 -NoBuild
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
- optional MCP metadata such as `executionPath`, `workspaceCacheStatus`, `workspaceCacheHit`, and `workspaceFingerprint`;
- timeout/skipped status.

Timings are environment-dependent. Treat local reports as release and investigation evidence, not a universal service-level objective.

## Reading A Report

For agent adoption decisions, inspect more than elapsed time:

- `elapsedMs`: wall-clock cost for a single command or tool call.
- stdout size: downstream parsing and LLM context pressure.
- stderr size and exit code: workspace load health, warnings, and failures.
- JSON validity and top-level command/profile: whether automation can safely parse the result.
- result counts: candidate count, changed symbol count, related files, tests, routes, or diagnostics.
- truncation flags and warnings: whether the chosen profile or limits hid useful evidence.
- MCP metadata: whether a tool used the direct path, whether the workspace cache was hit, and which session-local `snapshotId` / `workspaceFingerprint` produced the result.
- expected files: whether the files a maintainer expects are present in related/context outputs.

Record the SDK, operating system, repository commit, workspace path, Navlyn version, scenario, profile, iterations, and whether the first run included restore/build/cache warmup.

## Choosing A Workflow

Use direct CLI commands when:

- a human is running one fact at a time;
- the result is small;
- process startup is not the bottleneck.

Use `batch` or `navlyn_batch` when:

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

For MCP clients, `navlyn_batch` remains useful after the agent already knows it needs several batch-supported facts. Prefer the direct file-first tools for a single known file or symbol because they can reuse the MCP workspace cache without encouraging broad fact collection.

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
```

Keep generated reports under ignored paths such as `artifacts/` or `.docs/perf/`.

## Follow-Up Strategy

If repeated measurements show that workspace load dominates real agent workflows, future work can evaluate:

- broader warm workspace cache coverage with explicit invalidation semantics;
- on-disk symbol index;
- benchmark corpus and variance tracking;
- CI performance budgets after baseline variance is understood.

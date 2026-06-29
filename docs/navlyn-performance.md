# Navlyn Performance

Navlyn loads C# workspaces through MSBuild/Roslyn. On large repositories, workspace load cost can dominate command latency. This document explains how to measure that behavior and how to choose lower-cost workflows.

## Execution Model

- CLI commands load the configured workspace for each process invocation.
- `navlyn-mcp` is a read-only stdio server that runs Navlyn commands in-process by default through the shared engine.
- `navlyn_batch` can reduce repeated workspace loads when several batch-supported facts should be collected together.
- `compact` and `evidence` profiles can reduce output size and downstream token pressure.

Navlyn does not currently include a daemon, file watcher, on-disk index, telemetry pipeline, or hosted service. The MCP server reuses its process and command runtime across a stdio session, but it conservatively reloads the workspace for standalone tool calls so file-change behavior matches the CLI contract. Use `navlyn_batch` when several supported facts should share one workspace load.

## Measure Locally

Use the performance script from the repository root:

```powershell
./scripts/measure-navlyn-performance.ps1 -Workspace navlyn.slnx -Scenario quick -Iterations 1 -Warmup 0 -NoBuild
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

Prefer this pattern for a first broad scan:

```powershell
navlyn repo-graph --workspace navlyn.slnx --profile compact
navlyn resolve-target --workspace navlyn.slnx --query CheckCommand --assume-kind NamedType --limit 10
navlyn context-pack --workspace navlyn.slnx --query CheckCommand --assume-kind NamedType --goal modify --profile compact --budget-tokens 8000
```

Prefer batch when the agent already knows it needs several facts:

```powershell
Get-Content examples/batch/investigation-loop.json | navlyn batch --workspace navlyn.slnx
```

For MCP clients, the equivalent is `navlyn_batch`. The server runs in-process by default, so batching several supported facts mainly reduces repeated workspace load and keeps tool selection compact.

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
```

Keep generated reports under ignored paths such as `artifacts/` or `.docs/perf/`.

## Follow-Up Strategy

If repeated measurements show that workspace load dominates real agent workflows, future work can evaluate:

- warm workspace cache with explicit invalidation semantics;
- on-disk symbol index;
- benchmark corpus and variance tracking;
- CI performance budgets after baseline variance is understood.

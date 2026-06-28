# Navlyn Performance

Navlyn loads C# workspaces through MSBuild/Roslyn. On large repositories, workspace load cost can dominate command latency. This document explains how to measure that behavior and how to choose lower-cost workflows.

## Execution Model

- CLI commands load the configured workspace for each process invocation.
- `navlyn-mcp` is a read-only stdio wrapper that launches the Navlyn CLI as a subprocess per tool call.
- `navlyn_batch` can reduce repeated workspace loads when several batch-supported facts should be collected together.
- `compact` and `evidence` profiles can reduce output size and downstream token pressure.

Navlyn does not currently include a daemon, persistent workspace handle, on-disk index, telemetry pipeline, or hosted service.

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

- warm workspace or daemon mode;
- direct in-process MCP adapter;
- on-disk symbol index;
- benchmark corpus and variance tracking;
- CI performance budgets after baseline variance is understood.

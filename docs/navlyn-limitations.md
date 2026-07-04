# Navlyn Known Limits

Navlyn is intentionally a local, read-only, source-level evidence tool. These limits are part of the contract rather than hidden behavior.

## Current Limits

- Static source facts are not runtime proof. Routes, authorization, dependency injection, EF models, package usage, and impact are source-level evidence.
- Navlyn does not edit files, refactor code, run tests, publish review comments, scan secrets, or execute arbitrary shell commands.
- Workspace loading depends on local .NET SDK, MSBuild, restore state, and project files.
- `.code-workspace` folders may point outside the repository. Navlyn warns about external roots but still treats the configured workspace as user-approved input.
- MCP direct-path cache is session-local. It has no file watcher, no on-disk index, and no cross-process freshness guarantee. Restart the MCP server after source, project, SDK, or package changes when fresh facts matter.
- `candidateId` values are opaque anchors. They can change after source edits, generated-code changes, project filter changes, or Navlyn version changes.
- `compact` and `evidence` profiles intentionally trim output. Use warnings, limits, and truncation flags to decide when to rerun with higher limits or `full`.
- Generated code, metadata-only symbols, partial declarations, linked files, multi-target projects, and conditional compilation can produce multiple valid source contexts. Use project filters when precision matters.
- Navlyn does not claim package compatibility, binary compatibility, security correctness, or full runtime reachability.

## v1.0 Readiness Signals

0.5.x is intended to be useful for local agent workflows while keeping boundaries conservative. Strong v1.0 signals include:

- stable envelope schemas for CLI workflow and MCP tool results;
- broader command-specific schema coverage where it provides automation value;
- documented freshness behavior for any future watcher, daemon, or on-disk index;
- repeatable public performance/eval reports across representative repositories;
- clear package/version/release automation with package smoke coverage;
- issue reports that include workspace kind, SDK, command/tool, stdout/stderr, and reproduction fixture.

## Reporting Useful Issues

Include:

- Navlyn version and install source;
- OS and .NET SDK;
- workspace kind: `.code-workspace`, `.slnx`, `.sln`, `.csproj`, or `auto`;
- command or MCP tool name with arguments;
- stdout JSON and stderr diagnostics;
- whether the issue is semantic correctness, tool selection, performance, packaging, or documentation;
- minimal fixture or repository shape when possible.

# Navlyn Known Limits

Navlyn is intentionally a local, read-only, source-level evidence tool. These limits are part of the product contract, not footnotes. They are listed here so agents, automation, and human reviewers know when to trust Navlyn and when to ask for another kind of proof.

Short version: Navlyn is strong at C# source identity, supports Visual Basic through Roslyn/MSBuild, and reports bounded source facts. It is not a runtime, security, package-compatibility, or editing authority.

## Current Limits

- Static source facts are not runtime proof. Routes, authorization, dependency injection, EF models, package usage, and impact are source-level evidence.
- Navlyn does not edit files, refactor code, run tests, publish review comments, scan secrets, or execute arbitrary shell commands.
- Workspace loading depends on local .NET SDK, MSBuild, restore state, and project files. Load trusted repositories: design-time MSBuild evaluation, tasks, analyzers, and generators are not sandboxed by Navlyn's read-only tool surface. See [SECURITY.md](../SECURITY.md).
- Design-time MSBuild can generate intermediate files under `obj`; read-only semantic commands do not mean a filesystem without generated writes. Concurrent cold CLI loads can contend for those files. MCP's shared workspace and per-server external-adapter queue avoid that same-server race; independently launched processes need their own coordination.
- `navlyn.workspace.json` and `.code-workspace` folders may point outside the repository. CLI commands allow that by default with workspace-load warnings; `--workspace-root-policy repo-relative` blocks it, `allow-listed` permits configured `allowRoots`, and MCP defaults to `repo-relative`.
- MCP direct-path cache is session-local and has no file watcher or cross-process cache. Direct calls hash tracked inputs before reuse and before success, automatically reload stable changes, and fail closed on unreadable or continually changing inputs. Use `navlyn_workspace_refresh` after restoring custom assets outside tracked inputs or changing uninventoried inputs. The optional `.navlyn/cache` manifest stores lightweight freshness/index facts rather than a serialized Roslyn workspace.
- MCP deadlines include console/cache queue time and both direct and adapter execution. Cancellation is cooperative: a synchronous MSBuild/Roslyn operation may finish after the deadline, but the tool returns a timeout after cleanup rather than late success. Caller cancellation is reported separately.
- `candidateId` values are opaque anchors. They can change after source edits, generated-code changes, project filter changes, or Navlyn version changes.
- `compact` and `evidence` profiles intentionally trim output. Use warnings, limits, and truncation flags to decide when to rerun with higher limits or `full`.
- Generated code, metadata-only symbols, partial declarations, linked files, multi-target projects, and conditional compilation can produce multiple valid source contexts. Use project filters when precision matters.
- Navlyn does not claim package compatibility, binary compatibility, security correctness, or full runtime reachability.

## v1.0 Readiness Signals

Navlyn supports local agent workflows while keeping its evidence boundaries conservative. Strong v1.0 signals include:

- stable envelope schemas for CLI workflow and MCP tool results;
- broader command-specific schema coverage where it provides automation value;
- broader freshness behavior if a watcher or richer daemon/index model is added;
- repeatable public performance/eval reports across representative repositories;
- clear package/version/release automation with package smoke coverage;
- issue reports that include workspace kind, SDK, command/tool, stdout/stderr, and reproduction fixture.

## Reporting Useful Issues

Include:

- Navlyn version and install source;
- OS and .NET SDK;
- workspace kind: `navlyn.workspace.json`, `.code-workspace`, `.slnx`, `.sln`, `.csproj`, `.vbproj`, or `auto`;
- command or MCP tool name with arguments;
- stdout JSON and stderr diagnostics;
- whether the issue is semantic correctness, tool selection, performance, packaging, or documentation;
- minimal fixture or repository shape when possible.

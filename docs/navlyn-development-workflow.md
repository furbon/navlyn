# Navlyn Development Workflow

This document captures durable local development checks, command implementation workflow, review habits, and file-format rules.

## Documentation Map

- `docs/navlyn-cli-commands.md`: public CLI contract. Update this when implemented command behavior changes.
- `docs/navlyn-mcp-server.md`: public MCP server setup, tool surface, result envelope, and boundaries.
- `docs/navlyn-first-10-minutes.md`: short first-run onboarding path.
- `docs/navlyn-positioning.md`: category positioning against search, LSP, analyzers, MCP servers, and review bots.
- `docs/navlyn-distribution.md`: package validation and release workflow.
- `docs/navlyn-performance.md`: local performance measurement, MCP cost model, and release-readiness performance smoke.
- `docs/navlyn-agent-recipes.md`: batch recipes for agents and automation.
- `docs/navlyn-github-actions.md`: PR facts workflow example.
- `README.md`: project entry point for users. Keep it compact and current.
- `README_ja.md`: Japanese project entry point for users.
- `AGENTS.md`: repository-local agent guidance. Keep detailed designs out of this file.
- `.github/copilot-instructions.md`: short GitHub Copilot guidance. Keep it consistent with `AGENTS.md` without duplicating the full workflow.

Ignored local notes may exist in a checkout, but public docs, CI, and contributor workflow must not depend on ignored planning files.

## File Format

All `.cs` files must use:

- UTF-8 with BOM.
- CRLF line endings.
- Spaces for indentation, not tabs.

The repository includes `.editorconfig` and `.gitattributes` for editor and Git consistency. Generated or edited C# files should still be verified before commit.

```powershell
./scripts/normalize-csharp-files.ps1
./scripts/test-csharp-file-format.ps1
```

## Validation Layers

Navlyn uses separate validation layers so small changes can be checked quickly while release preparation still has a full quality gate.

- Quick validation: `./scripts/test-quick.ps1`
- CLI contract validation: `./scripts/test-cli-contract.ps1 -Suite core` for the regular loop, focused suites for affected public surfaces, and `./scripts/test-cli-contract.ps1 -Suite all` for release validation.
- xUnit component tests: `dotnet test navlyn.slnx`
- Focused fixture validation:
  - `./scripts/test-symbol-navigation.ps1`
  - `./scripts/test-fuzzy-discovery.ps1`
  - `./scripts/test-multi-project-navigation.ps1`
  - `./scripts/test-workspace-semantics.ps1`
  - `./scripts/test-diagnostics.ps1`
- Package install smoke: `./scripts/test-package-install.ps1`
- Release validation: `./scripts/test-release.ps1`

Keep these layers separate. Quick validation should prove that the solution builds, xUnit tests pass, core CLI entrypoints run, stdout/stderr discipline holds, exit codes are stable, and representative JSON shapes are intact. CLI contract validation should cover broader public command wiring, required options, stable error behavior, batch support, top-level JSON shape, and representative workflow commands such as `review-diff`, `review-pack`, `context-pack`, `repo-graph`, `public-api-diff`, `tests-for-symbol`, `tests-for-diff`, `framework-entrypoints`, `di-graph`, `where-registered`, and `di-impact`. Focused fixture checks should cover semantic details that need controlled source code, such as symbol binding, overload selection, local variables, parameters, definitions, references, cross-project navigation, generated-code filtering, compiler diagnostics, conditional compilation, multi-targeting, linked files, and review-pack signals.

PowerShell process tests own the public CLI boundary: real process invocation, stdout, stderr, exit codes, JSON contract, and MSBuild workspace behavior. xUnit owns fast internal logic and component behavior that does not need a process boundary. Resolver-level component coverage belongs in xUnit when it can protect a semantic case without duplicating CLI contract checks.

The xUnit resolver component suite covers representative behavior for definition, references, source-position lookup, symbol info, implementation and hierarchy relationships, fuzzy candidate selection, fuzzy reference summaries, workspace diagnostics filtering, diff symbol resolution, repository graph facts, public API comparison, context pack budgeting, test impact, framework entrypoint detection, and dependency injection registration facts. These tests use controlled Roslyn fixture projects and source marker helpers, so they should be the first place to add coverage for resolver internals, binding edge cases, ranking choices, or diagnostic filtering that can be asserted without spawning the CLI.

Validation timing can be recorded without changing public stdout/stderr contracts:

```powershell
./scripts/measure-validation.ps1 -Lane quick -NoBuild
./scripts/measure-validation.ps1 -Lane contract-core -NoBuild
./scripts/measure-validation.ps1 -Lane all -NoBuild
```

The wrapper writes structured reports under ignored `artifacts/test-timings/`. Use it before and after validation-script changes, release preparation, or performance-related refactors so slow lanes have concrete evidence instead of impressions.

CI runs restore, build, xUnit, C# file format validation, quick validation without duplicate xUnit, and the CLI contract core suite on Windows, Ubuntu, and macOS through `pwsh`. Release validation keeps the full CLI contract suite and focused fixture scripts. Keep script examples compatible with PowerShell Core and prefer `./scripts/...` plus `/` path separators in command examples so they work across all CI operating systems. The xUnit project disables target-framework parallelism because resolver and MCP process tests can load the same MSBuild workspace while test infrastructure is writing per-framework output files; run explicit framework lanes when debugging runtime-specific failures.

## Standard Local Checks

For a typical code change:

```powershell
dotnet restore navlyn.slnx
dotnet build navlyn.slnx
dotnet test navlyn.slnx --no-build
./scripts/test-quick.ps1 -NoBuild -SkipDotnetTest
```

For release preparation or a large refactor:

```powershell
./scripts/test-release.ps1
```

`test-release.ps1` restores and builds once, runs xUnit, checks C# file format, runs quick validation without rerunning xUnit, runs the full CLI contract suite, runs focused fixture scripts with `-NoBuild`, runs the public readiness audit, and runs local package install smoke when feasible.

Release publication and package ownership details live in `docs/navlyn-distribution.md`. Performance smoke guidance lives in `docs/navlyn-performance.md`.

## Test Selection Rules

- Docs-only change: `git diff --check`. For public workflow docs, inspect command names and examples.
- C# pure logic change: `dotnet build navlyn.slnx --no-restore`, `dotnet test navlyn.slnx --no-build`, and `./scripts/test-quick.ps1 -NoBuild -SkipDotnetTest`.
- CLI option, JSON shape, stdout/stderr, or exit code change: quick validation, `./scripts/test-cli-contract.ps1 -NoBuild -Suite core` for command wiring, `./scripts/test-cli-contract.ps1 -NoBuild -Suite all` when broad workflow/domain contract changed, and an affected command manual check.
- MCP server change: `dotnet test navlyn.slnx --no-build --filter "FullyQualifiedName~Navlyn.Tests.Mcp"`, quick validation, and a manual `navlyn.Mcp --help` or stdio client smoke when the process boundary changed.
- Workflow envelope, MCP envelope, schema, or golden snapshot change: `dotnet test navlyn.slnx --no-build --filter "FullyQualifiedName~Navlyn.Tests.Contracts"`, quick validation, full CLI contract validation when public JSON shape changed, and manual inspection of one affected JSON result.
- Review workflow, review pack, context pack, repository graph, public API diff, test impact, framework entrypoint, or DI command contract change: quick validation, `./scripts/test-cli-contract.ps1 -NoBuild -Suite all`, affected component tests, and an affected command manual check.
- Symbol lookup, source-position, or outline change: `dotnet test navlyn.slnx --no-build`, quick validation, and `./scripts/test-symbol-navigation.ps1 -NoBuild -Suite lookup`.
- Definition change: `dotnet test navlyn.slnx --no-build`, quick validation, and `./scripts/test-symbol-navigation.ps1 -NoBuild -Suite definition`.
- References change: `dotnet test navlyn.slnx --no-build`, quick validation, and `./scripts/test-symbol-navigation.ps1 -NoBuild -Suite references`.
- Implementations, callers, or calls change: `dotnet test navlyn.slnx --no-build`, quick validation, and `./scripts/test-symbol-navigation.ps1 -NoBuild -Suite edges`.
- Symbol source, signature, hierarchy, symbol-info, or scope change: `dotnet test navlyn.slnx --no-build`, quick validation, and `./scripts/test-symbol-navigation.ps1 -NoBuild -Suite source`.
- Fuzzy discovery change: `dotnet test navlyn.slnx --no-build`, quick validation, and `./scripts/test-fuzzy-discovery.ps1 -NoBuild`.
- Project filter or multi-project change: quick validation and `./scripts/test-multi-project-navigation.ps1 -NoBuild`.
- Workspace loading, conditional compilation, multi-targeting, or linked-file change: quick validation and `./scripts/test-workspace-semantics.ps1 -NoBuild`.
- Diagnostics change: `dotnet test navlyn.slnx --no-build`, quick validation, and `./scripts/test-diagnostics.ps1 -NoBuild`.
- Formatting or C# file encoding change: `./scripts/test-csharp-file-format.ps1`.
- Large refactor or release preparation: `./scripts/test-release.ps1`.

## Script Details

### Quick Validation

```powershell
./scripts/test-quick.ps1
./scripts/test-quick.ps1 -NoBuild
./scripts/test-quick.ps1 -NoBuild -SkipDotnetTest
./scripts/test-quick.ps1 -NoBuild -ShowOutput
```

The quick script verifies C# file format, builds the solution unless `-NoBuild` is provided, runs xUnit unless `-SkipDotnetTest` is provided, then checks representative CLI behavior for `check`, `overview`, a source-position command, and a stable usage error. Use `-SkipDotnetTest` only in a validation chain that already ran `dotnet test navlyn.slnx --no-build`.

For runtime compatibility changes, run both explicit xUnit lanes after building:

```powershell
dotnet test navlyn.slnx --framework net8.0 --no-build
dotnet test navlyn.slnx --framework net10.0 --no-build
```

### CLI Contract Validation

```powershell
./scripts/test-cli-contract.ps1
./scripts/test-cli-contract.ps1 -NoBuild
./scripts/test-cli-contract.ps1 -NoBuild -Suite core
./scripts/test-cli-contract.ps1 -NoBuild -Suite navigation
./scripts/test-cli-contract.ps1 -NoBuild -Suite workflow
./scripts/test-cli-contract.ps1 -NoBuild -Suite domain
./scripts/test-cli-contract.ps1 -NoBuild -Suite mcp-adjacent
./scripts/test-cli-contract.ps1 -NoBuild -Suite all
./scripts/test-cli-contract.ps1 -NoBuild -ShowOutput
```

The CLI contract script defaults to `-Suite all` for backward compatibility. The `core` suite covers root help, required-option failures, workspace commands, top-level JSON shape, path normalization from subdirectories, diagnostics shape, and stable workspace errors. The `navigation` suite adds symbol search, source-position, fuzzy discovery, symbol source, references, callers, calls, and related navigation contracts. The `workflow` suite adds review diff, review pack, public API diff, tests-for-symbol/diff, and context-pack contracts. The `domain` suite covers framework entrypoints, dependency injection, routes, options, handlers, EF model, and package usage. The `mcp-adjacent` suite covers batch request contracts and tool-adjacent command envelopes that agent clients depend on. The `all` suite runs every contract lane. Use a 600 second automation timeout for the `all` suite. It can exceed 300 seconds on slower local runs even when healthy.

### Contract Schemas And Golden Snapshots

```powershell
dotnet test navlyn.slnx --no-build --filter "FullyQualifiedName~Navlyn.Tests.Contracts"
```

Envelope schemas live in `docs/schemas`. Golden snapshots live under `navlyn.Tests/Contracts/GoldenSnapshots` and cover representative envelope shape after scrubbing nondeterministic fields. Update snapshots only when the public contract intentionally changes; keep command-specific semantic assertions in focused resolver or CLI tests.

### Performance Measurement

```powershell
./scripts/measure-navlyn-performance.ps1 -Workspace navlyn.slnx -Scenario quick -Iterations 1 -Warmup 0 -NoBuild
./scripts/measure-navlyn-performance.ps1 -Workspace navlyn.slnx -Scenario agent-loop -Profile compact -Iterations 3 -NoBuild -Output artifacts/performance-smoke/navlyn-agent-loop.json
./scripts/measure-navlyn-performance.ps1 -Workspace navlyn.slnx -Scenario all -Profile evidence -Iterations 1 -Warmup 0 -NoBuild
```

The performance script emits structured JSON with elapsed time, stdout/stderr size, exit code, JSON validity, key counts, truncation state, profile, and command arguments. Keep generated reports under ignored local paths such as `artifacts/performance-smoke/` unless a curated baseline is intentionally being published. The MCP scenario starts a local stdio MCP session and records representative tool-call latency/output-size measurements when the MCP server assembly is built; use `navlyn.Tests.Mcp` coverage for functional MCP validation. For 0.6.x release work, record at least quick, file-first, agent-loop, diff, and MCP smoke reports. Treat failures, non-JSON stdout, non-empty stderr on success, unexpected truncation, or missing expected files as performance acceptance failures even when elapsed time looks good.

### Package And PR Facts Scripts

```powershell
./scripts/test-package-install.ps1
./scripts/test-package-install.ps1 -Frameworks net8.0
./scripts/test-package-install.ps1 -Frameworks net10.0
./scripts/pack-release.ps1 -Output artifacts/packages
./scripts/publish-nuget.ps1 -DryRun
./scripts/write-navlyn-pr-facts.ps1 -Workspace navlyn.slnx -Output artifacts/navlyn-pr-facts
```

Package and PR facts scripts write generated output under ignored `artifacts/` paths. Package install smoke defaults to both `net8.0` and `net10.0`; pass `-Frameworks` only for focused debugging. Publishing is dry-run by default and `-DryRun` makes that explicit; pass `-Publish` only from an intentional release environment with `NUGET_API_KEY` set.
When publishing through GitHub Actions, use the guarded manual workflow with the `nuget-production` environment and NuGet Trusted Publishing.

### Focused Fixture Validation

Use focused fixture scripts for behavior that depends on controlled C# source layouts or MSBuild workspace context:

```powershell
./scripts/test-symbol-navigation.ps1 -NoBuild
./scripts/test-symbol-navigation.ps1 -NoBuild -Suite lookup
./scripts/test-symbol-navigation.ps1 -NoBuild -Suite definition
./scripts/test-symbol-navigation.ps1 -NoBuild -Suite references
./scripts/test-symbol-navigation.ps1 -NoBuild -Suite edges
./scripts/test-symbol-navigation.ps1 -NoBuild -Suite source
./scripts/test-fuzzy-discovery.ps1 -NoBuild
./scripts/test-multi-project-navigation.ps1 -NoBuild
./scripts/test-workspace-semantics.ps1 -NoBuild
./scripts/test-diagnostics.ps1 -NoBuild
```

Add fixture coverage for semantic behavior that must be observed through the real CLI, JSON envelope, stdout/stderr, exit code, batch command, or MSBuild workspace boundary. Add xUnit coverage for pure helpers and resolver component cases that can run faster without checking the process boundary.

Use xUnit component tests for resolver changes when the assertion is about internal semantic facts: selected overloads, source spans, containing symbols, call or implementation relationships, fuzzy ranking and ambiguity, or diagnostic inclusion/exclusion. Use PowerShell focused fixture scripts when the assertion is about command wiring, serialization shape, process behavior, project filter CLI behavior, or end-to-end workspace loading.

The focused fixture scripts cover these areas:

- `test-symbol-navigation.ps1`: type, method, property, parameter, local, alias, partial-declaration, generated-code, semantic edge-case, implementation, and call hierarchy scenarios. It supports `-Suite lookup|definition|references|edges|source|all`; release validation uses `all`.
- `test-fuzzy-discovery.ps1`: fuzzy candidate selection, ambiguity, generated-code exclusion, reference context, snippets, related and impact file summaries, static entrypoint chains, and fuzzy batch requests.
- `test-multi-project-navigation.ps1`: deterministic project context and navigation across a project reference.
- `test-workspace-semantics.ps1`: conditional compilation, multi-target project identity, target-specific source-position behavior, ambiguous multi-target project path filtering, and linked-file project context selection.
- `test-diagnostics.ps1`: deterministic compiler diagnostics output, project filtering, severity/id/limit filters, and generated-code exclusion.

## Operational Pitfalls

Avoid repeating known local-environment failures. If a check fails due to timeout, file locks, or another active process, diagnose that condition before rerunning the same command.

### Command Timeouts

Recommended command timeouts for automation:

- `dotnet restore navlyn.slnx`: at least 120 seconds.
- `dotnet build navlyn.slnx`: at least 120 seconds.
- `dotnet test navlyn.slnx`: at least 180 seconds.
- `./scripts/test-quick.ps1`: at least 300 seconds.
- `./scripts/test-cli-contract.ps1`: at least 600 seconds.
- `./scripts/test-symbol-navigation.ps1`: at least 420 seconds.
- `./scripts/test-fuzzy-discovery.ps1`: at least 420 seconds.
- `./scripts/test-multi-project-navigation.ps1`: at least 180 seconds.
- `./scripts/test-diagnostics.ps1`: at least 180 seconds.
- `./scripts/test-workspace-semantics.ps1`: at least 180 seconds.
- `./scripts/test-release.ps1`: at least 900 seconds.

Observed 0.6.0 local baseline on this Windows workstation after build: restore about 1-2 seconds, build about 4 seconds, `dotnet test navlyn.slnx --no-build` about 169 seconds with target frameworks serialized, quick validation without duplicate xUnit about 13 seconds, and CLI contract core about 34 seconds. Use these as a local sanity check, not a universal budget.

When Navlyn has already been built in the same turn, prefer validation scripts with `-NoBuild` to avoid rebuilding and to reduce the chance of `bin`/`obj` file locks.

If a command times out, do not immediately retry with the same timeout. First check whether a previous `dotnet` process is still running and whether it belongs to this repository. Then rerun once with the recommended timeout only after the previous process has exited or has been safely handled.

### DLL And Build Output Locks

On Windows, `dotnet build` can fail with copy or access errors when `navlyn/bin/.../navlyn.dll` or related files are still in use. Common causes are an earlier `dotnet run`, a timed-out test process that is still alive, another active validation run, or an IDE/tool window holding the output.

Useful investigation commands:

```powershell
Get-CimInstance Win32_Process -Filter "name = 'dotnet.exe'" |
    Select-Object ProcessId, CommandLine

Get-Process dotnet -ErrorAction SilentlyContinue |
    Select-Object Id, ProcessName, StartTime, Path
```

Only stop a process when it is clearly a stale Navlyn command from this workspace. Do not kill unknown `dotnet` processes. When stale build servers are the likely issue and no active Navlyn command is running, release build services before retrying:

```powershell
dotnet build-server shutdown
```

Then rerun the build once. If the same lock repeats, report the process information and the locked path instead of looping.

### Parallel Runs

Before starting long validation, check `git status --short` and be mindful of other active work in the same checkout. Avoid running multiple build-heavy scripts at the same time if they all build Navlyn. Build once, then run focused scripts with `-NoBuild`.

If unexpected files change while work is in progress, preserve them. If they affect the same files or make validation unreliable, inspect and report the conflict.

### Line Ending And Diff Warnings

`git diff --check` may print warnings such as `LF will be replaced by CRLF the next time Git touches it`. Those are Git line-ending conversion warnings, not whitespace errors. Treat `git diff --check` as failed only when it exits non-zero or reports actual whitespace problems.

## Practical Agent Workflow Review

Navlyn exists to be used by agents and automation on real C#/.NET repositories. Before closing a command change, review whether the current command set lets an agent avoid broad text search for semantic questions.

Use this review when scoping follow-up work:

- Can an agent discover the relevant file, type, member, or source span without already knowing the exact column?
- Can it inspect a file or type semantically, not only by name query?
- Can large result sets from `references`, `implementations`, `callers`, `calls`, and `diagnostics` be scoped, limited, grouped, and counted predictably?
- Does `--project` mean input interpretation context, result filtering, or both? Prefer separate option names when adding result-side scoping.
- Do symbol results include enough facts for an agent to choose between overloads, generic methods, constructors, extension methods, operators, indexers, nested types, and partial declarations?
- Does the behavior remain clear for multi-targeting, conditional compilation, linked files, generated code, and metadata-only symbols?
- Can the same workflow run efficiently through `batch`, including any new command or option?
- Is the command still complementing text search rather than trying to answer comments, string literals, docs, or non-C# source questions?

## Command Implementation Checklist

When adding a command, keep the change narrow and follow the established shape:

- Confirm the command is not already implemented in `docs/navlyn-cli-commands.md` and current code.
- Keep command classes thin: define options, call a resolver or service, translate errors, and write JSON.
- Reuse `WorkspaceCommand` for workspace-loading commands.
- Reuse `SourcePositionCommand` for commands that take `--file`, `--line`, and `--column`.
- Keep Roslyn and semantic behavior in `navlyn/Symbols`, not in CLI command classes.
- Use stable diagnostic IDs and preserve stdout for successful JSON result data only.
- Prefer repository-relative paths through the existing path display helpers.
- Add CLI contract coverage for public command wiring and output shape.
- Add fixture coverage for semantic behavior, especially overloads, aliases, partial declarations, metadata-only symbols, source spans, and project context.
- Add or update `batch` support when the command is intended for exploration loops.
- For file-wide or type-wide exploration commands, prefer compact semantic summaries over raw syntax dumps.
- For result-scoping options, keep input context options distinct from output filtering options.
- Update `docs/navlyn-cli-commands.md` for implemented behavior, then update README or this workflow only when their durable summaries changed.

## Refactoring Checklist

Prefer small refactors that remove current duplication without inventing a larger architecture:

- Add a helper only when two or more commands already share the same option parsing or execution shape.
- Keep helpers thin and concrete, like `WorkspaceCommand`, `SourcePositionCommand`, and `scripts/lib/navlyn-test-harness.ps1`.
- Avoid inheritance hierarchies, dependency injection, broad facades, or generic result abstractions until repeated concrete code makes the need obvious.
- After refactoring, run the same command and fixture checks as for behavior changes.

## Self-Review Checklist

Before finishing a change, review these points:

- Does each command still have a stable, deterministic JSON shape?
- Are stdout and stderr separated correctly on both success and failure?
- Are errors stable and specific enough for scripts to branch on?
- Are paths normalized before comparison and repository-relative when emitted?
- Are line and column values 1-based at the CLI boundary?
- Is output sorted deterministically by path, line, column, and name where applicable?
- Does the behavior make sense for multiple projects, partial declarations, aliases, and metadata-only symbols?
- Does the command give enough context for the next navigation call without opening files unnecessarily?
- Are large-workspace concerns handled through scope, limit, grouping, and total counts where applicable?
- Did any Roslyn search walk the whole workspace or every syntax node unnecessarily?
- Did tests cover the semantic distinction that could regress, rather than only Navlyn's own source layout?
- Is the chosen validation layer appropriate for the risk, with quick checks kept short and release validation kept comprehensive?

## Manual CLI Checks

When changing CLI behavior, inspect the affected command directly as well as running the relevant scripts:

```powershell
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- check --workspace navlyn.slnx
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- overview --workspace navlyn.slnx
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- diagnostics --workspace navlyn.slnx
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- repo-graph --workspace navlyn.slnx --profile compact
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- review-diff --workspace navlyn.slnx --profile evidence --symbol-limit 1 --impact-limit 1 --diagnostic-limit 1 --related-test-limit 1 --depth 1
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- context-pack --workspace navlyn.slnx --query CheckCommand --assume-kind NamedType --profile compact --budget-tokens 2000
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- public-api-diff --workspace navlyn.slnx --base HEAD --project "navlyn(net10.0)" --change-limit 5
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- tests-for-symbol --workspace navlyn.slnx --query RepoGraphResolver --assume-kind NamedType --project "Navlyn.Core(net10.0)" --test-project "navlyn.Tests(net10.0)" --test-limit 5
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- symbols --workspace navlyn.slnx --query Check
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- symbols-in --workspace navlyn.slnx --file Navlyn.CommandLine/Cli/NavlynCli.cs --line 53
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- symbol-at --workspace navlyn.slnx --file Navlyn.CommandLine/Cli/Commands/CheckCommand.cs --line 6 --column 23
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- definition --workspace navlyn.slnx --file Navlyn.CommandLine/Cli/NavlynCli.cs --line 53 --column 37
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- references --workspace navlyn.slnx --file Navlyn.CommandLine/Cli/NavlynCli.cs --line 53 --column 37
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- implementations --workspace navlyn.slnx --file Navlyn.CommandLine/Cli/Commands/CheckCommand.cs --line 6 --column 23
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- callers --workspace navlyn.slnx --file Navlyn.CommandLine/Cli/Commands/CheckCommand.cs --line 8 --column 27
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- calls --workspace navlyn.slnx --file Navlyn.CommandLine/Cli/NavlynCli.cs --line 53 --column 37
```

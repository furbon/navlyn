# Navlyn Development Workflow

This document captures durable local development checks, command implementation workflow, review habits, and file-format rules.

## Documentation Map

- `docs/navlyn-cli-commands.md`: public CLI contract. Update this when implemented command behavior changes.
- `README.md`: project entry point for users. Keep it compact and current.
- `README_ja.md`: Japanese project entry point for users.
- `AGENTS.md`: repository-local agent guidance. Keep detailed designs out of this file.

## File Format

All `.cs` files must use:

- UTF-8 with BOM.
- CRLF line endings.
- Spaces for indentation, not tabs.

The repository includes `.editorconfig` and `.gitattributes` for editor and Git consistency. Generated or edited C# files should still be verified before commit.

## Validation Layers

Navlyn uses complementary validation scripts:

- `.\scripts\smoke.ps1`: lightweight compatibility checks for the public CLI contract.
- `.\scripts\test-symbol-navigation.ps1`: focused semantic navigation checks against a purpose-built fixture project.
- `.\scripts\test-fuzzy-discovery.ps1`: focused fuzzy discovery checks against a purpose-built fixture project.
- `.\scripts\test-multi-project-navigation.ps1`: focused cross-project navigation checks against a small two-project fixture solution.
- `.\scripts\test-diagnostics.ps1`: focused compiler diagnostics checks against a small intentionally broken fixture project.
- `.\scripts\test-workspace-semantics.ps1`: focused workspace-context checks for conditional compilation, multi-targeting, and linked files.

Keep these layers separate. Smoke should prove that core commands run from the repository root, preserve stdout/stderr discipline, return stable exit codes, and keep representative JSON shapes intact. Focused fixture checks should cover semantic details that need controlled source code, such as symbol binding, overload selection, local variables, parameters, definitions, references, cross-project navigation, generated-code filtering, compiler diagnostics, conditional compilation, multi-targeting, and linked files.

When adding or changing a command:

- Update smoke when the public CLI command surface, required options, error behavior, or top-level JSON shape changes.
- Update or add focused fixture checks when correctness depends on Roslyn semantic behavior or source layouts that should not be coupled to Navlyn's own implementation files.
- Run the affected command manually when inspecting stdout, stderr, and exit behavior would catch contract mistakes that scripts may not make obvious.
- Keep generated artifacts, fixture build output, and local notes out of commits.

## Operational Pitfalls

Avoid wasting time and API budget by repeating known local-environment failures. If a check fails because of timeout, file locks, or another running agent, diagnose that condition first instead of rerunning the same command unchanged.

### Command Timeouts

Some fixture scripts legitimately take longer than short default tool timeouts, especially after semantic fixture coverage grows. Use an explicit timeout that matches the check before running it.

Recommended command timeouts for automation:

- `dotnet restore navlyn.slnx`: at least 120 seconds.
- `dotnet build navlyn.slnx`: at least 120 seconds.
- `.\scripts\smoke.ps1`: at least 300 seconds.
- `.\scripts\test-symbol-navigation.ps1`: at least 420 seconds.
- `.\scripts\test-fuzzy-discovery.ps1`: at least 420 seconds.
- `.\scripts\test-multi-project-navigation.ps1`: at least 180 seconds.
- `.\scripts\test-diagnostics.ps1`: at least 180 seconds.
- `.\scripts\test-workspace-semantics.ps1`: at least 180 seconds.

When Navlyn has already been built in the same turn, prefer focused checks with `-NoBuild` to avoid rebuilding and to reduce the chance of `bin`/`obj` file locks:

```powershell
.\scripts\test-symbol-navigation.ps1 -NoBuild
.\scripts\test-fuzzy-discovery.ps1 -NoBuild
.\scripts\test-multi-project-navigation.ps1 -NoBuild
.\scripts\test-diagnostics.ps1 -NoBuild
.\scripts\test-workspace-semantics.ps1 -NoBuild
```

If a command times out, do not immediately retry with the same timeout. First check whether a previous `dotnet` process is still running and whether it belongs to this repository. Then rerun once with the recommended timeout only after the previous process has exited or has been safely handled.

### DLL And Build Output Locks

On Windows, `dotnet build` can fail with copy/access errors when `navlyn\bin\...\navlyn.dll` or related files are still in use. Common causes are an earlier `dotnet run`, a timed-out test process that is still alive, another agent running checks, or an IDE/tool window holding the output.

Useful investigation commands:

```powershell
Get-CimInstance Win32_Process -Filter "name = 'dotnet.exe'" |
    Select-Object ProcessId, CommandLine

Get-Process dotnet -ErrorAction SilentlyContinue |
    Select-Object Id, ProcessName, StartTime, Path
```

Only stop a process when it is clearly a stale Navlyn command from this workspace. Do not kill unknown `dotnet` processes or processes that may belong to another active agent. If another agent is actively using the repository, wait for the process to finish or coordinate rather than fighting the lock.

When stale build servers are the likely issue and no active Navlyn command is running, release build services before retrying:

```powershell
dotnet build-server shutdown
```

Then rerun the build once. If the same lock repeats, report the process information and the locked path instead of looping.

### Parallel Agent Or Test Runs

Before starting long validation, check `git status --short` and be mindful of other active work in the same checkout. Avoid running multiple build-heavy scripts at the same time if they all build Navlyn; build once, then run focused scripts with `-NoBuild`. Parallel focused checks are acceptable only when the project has already built and no shared output is being written.

If unexpected files change while you are working, assume they may belong to the user or another agent. Do not revert them. If they affect the same files or make validation unreliable, pause to inspect and report the conflict.

### Line Ending And Diff Warnings

`git diff --check` may print warnings such as `LF will be replaced by CRLF the next time Git touches it`. Those are Git line-ending conversion warnings, not whitespace errors. Treat `git diff --check` as failed only when it exits non-zero or reports actual whitespace problems.

## Practical Agent Workflow Review

Navlyn exists to be used by agents and automation on real C#/.NET repositories. Before adding a phase or closing a command change, review whether the current command set lets an agent avoid falling back to broad `rg` searches for semantic questions.

Use this review when constructing future work:

- Can an agent discover the relevant file, type, member, or source span without already knowing the exact column?
- Can it inspect a file or type semantically, not only by name query? If not, consider whether an `outline`-style command is the right next step.
- Can large result sets from `references`, `implementations`, `callers`, `calls`, and `diagnostics` be scoped, limited, grouped, and counted predictably?
- Does `--project` mean input interpretation context, result filtering, or both? Prefer separate option names when adding result-side scoping.
- Do symbol results include enough facts for an agent to choose between overloads, generic methods, constructors, extension methods, operators, indexers, nested types, and partial declarations?
- Does the behavior remain clear for multi-targeting, conditional compilation, linked files, generated code, and metadata-only symbols?
- Can the same workflow run efficiently through `batch`, including any new command or option?
- Is the command still complementing text search rather than trying to answer comments, string literals, docs, or non-C# source questions?

## Command Implementation Checklist

When adding a command, keep the change narrow and follow the established shape:

- Confirm the command is not already implemented in `docs/navlyn-cli-commands.md` and current code.
- Keep command classes thin: define options, call a resolver/service, translate errors, and write JSON.
- Reuse `WorkspaceCommand` for workspace-loading commands.
- Reuse `SourcePositionCommand` for commands that take `--file`, `--line`, and `--column`.
- Keep Roslyn and semantic behavior in `navlyn/Symbols`, not in CLI command classes.
- Use stable diagnostic IDs and preserve stdout for successful JSON result data only.
- Prefer repository-relative paths through the existing path display helpers.
- Add smoke coverage for public CLI wiring and output shape.
- Add fixture coverage for semantic behavior, especially overloads, aliases, partial declarations, metadata-only symbols, source spans, and project context.
- Add or update `batch` support when the command is intended for agent exploration loops.
- For file-wide or type-wide exploration commands, prefer compact semantic summaries over raw syntax dumps.
- For result-scoping options, keep input context options distinct from output filtering options.
- Update `docs/navlyn-cli-commands.md` for implemented behavior, then update README or this workflow only when their durable summaries changed.

## Self-Review Checklist

Before finishing a change, review these points:

- Does each command still have a stable, deterministic JSON shape?
- Are stdout and stderr separated correctly on both success and failure?
- Are errors stable and specific enough for scripts to branch on?
- Are paths normalized before comparison and repository-relative when emitted?
- Are line and column values 1-based at the CLI boundary?
- Is output sorted deterministically by path, line, column, and name where applicable?
- Does the behavior make sense for multiple projects, partial declarations, aliases, and metadata-only symbols?
- Does the command give agents enough context to make the next navigation call without opening files unnecessarily?
- Are large-workspace concerns handled through scope, limit, grouping, and total counts where applicable?
- Did any Roslyn search walk the whole workspace or every syntax node unnecessarily?
- Did tests cover the semantic distinction that could regress, rather than only Navlyn's own source layout?

## Refactoring Checklist

Prefer small refactors that remove current duplication without inventing a larger architecture:

- Add a helper only when two or more commands already share the same option parsing or execution shape.
- Keep helpers thin and concrete, like `WorkspaceCommand` and `SourcePositionCommand`.
- Avoid inheritance hierarchies, dependency injection, broad facades, or generic result abstractions until repeated concrete code makes the need obvious.
- After refactoring, run the same command and fixture checks as for behavior changes.

## Smoke Checks

Run the smoke script from the repository root:

```powershell
.\scripts\smoke.ps1
```

The script:

- Normalizes C# file encoding and line endings.
- Verifies C# file format.
- Builds the solution unless `-NoBuild` is provided.
- Runs representative CLI checks for `check`, `overview`, `diagnostics`, `batch`, `symbols`, `symbols-in`, `symbol-at`, `definition`, `references`, `implementations`, `callers`, and `calls`.
- Includes representative fuzzy command wiring checks for `find`, `where-used`, `about`, `related`, `impact`, and `entrypoints`.
- Verifies stdout, stderr, and exit behavior.

Do not make smoke exhaustive. It should stay fast and broad enough to catch broken wiring, command registration problems, serialization drift, and common contract regressions.

Useful variants:

```powershell
.\scripts\smoke.ps1 -NoBuild
.\scripts\smoke.ps1 -NoBuild -ShowOutput
```

## Symbol Navigation Fixture Checks

Run focused semantic navigation checks against the fixture project:

```powershell
.\scripts\test-symbol-navigation.ps1
```

The script restores `tests/fixtures/SymbolNavigationFixture/SymbolNavigationFixture.csproj`, builds Navlyn unless `-NoBuild` is provided, and verifies `symbols`, `symbols-in`, `symbol-at`, `definition`, `references`, `implementations`, `callers`, and `calls` results for type, method, property, parameter, local, alias, partial-declaration, generated-code, semantic edge-case, implementation, and call hierarchy scenarios.

Use this layer for semantic cases that benefit from a stable sandbox. Extend the fixture before implementing commands or behavior that depends on new C# language scenarios, then add expectations as the command behavior becomes stable.

When adding new fixture scenarios, prefer focused source examples that capture the C# language behavior being fixed or protected.

## Fuzzy Discovery Fixture Checks

Run focused fuzzy discovery checks:

```powershell
.\scripts\test-fuzzy-discovery.ps1
```

The script restores `tests/fixtures/FuzzyDiscoveryFixture/FuzzyDiscoveryFixture.csproj`, builds Navlyn unless `-NoBuild` is provided, and verifies fuzzy candidate selection, ambiguity, medium-confidence exact-plus-weaker alternatives, generated-code exclusion, reference context, snippets, related and impact file summaries, static entrypoint chains, and fuzzy batch requests.

## Multi-Project Fixture Checks

Run focused cross-project checks against the two-project fixture solution:

```powershell
.\scripts\test-multi-project-navigation.ps1
```

The script restores `tests/fixtures/MultiProjectFixture/MultiProjectFixture.slnx`, builds Navlyn unless `-NoBuild` is provided, verifies deterministic `overview` project ordering, and checks `symbol-at`, `definition`, and `references` behavior across a project reference.

## Workspace Semantics Fixture Checks

Run focused workspace-context checks against the real-repository semantics fixture:

```powershell
.\scripts\test-workspace-semantics.ps1
```

The script restores `tests/fixtures/WorkspaceSemanticsFixture/WorkspaceSemanticsFixture.slnx`, builds Navlyn unless `-NoBuild` is provided, verifies `overview` project context facts, and checks preprocessor active/inactive branches, multi-target project identity, target-specific source-position behavior, ambiguous multi-target project path filtering, and linked-file project context selection.

## Diagnostics Fixture Checks

Run focused compiler diagnostics checks against the intentionally broken fixture project:

```powershell
.\scripts\test-diagnostics.ps1
```

The script restores `tests/fixtures/DiagnosticFixture/DiagnosticFixture.csproj`, builds Navlyn unless `-NoBuild` is provided, and verifies deterministic `diagnostics` output, project filtering, and generated-code exclusion behavior.

## File Format Scripts

Normalize C# files:

```powershell
.\scripts\normalize-csharp-files.ps1
```

Verify C# files:

```powershell
.\scripts\test-csharp-file-format.ps1
```

## Manual CLI Checks

When changing CLI behavior, inspect the affected command directly as well as running the relevant scripts:

```powershell
dotnet run --no-launch-profile --project navlyn -- check --workspace navlyn.slnx
dotnet run --no-launch-profile --project navlyn -- overview --workspace navlyn.slnx
dotnet run --no-launch-profile --project navlyn -- diagnostics --workspace navlyn.slnx
dotnet run --no-launch-profile --project navlyn -- symbols --workspace navlyn.slnx --query Check
dotnet run --no-launch-profile --project navlyn -- symbols-in --workspace navlyn.slnx --file navlyn\Cli\NavlynCli.cs --line 31
dotnet run --no-launch-profile --project navlyn -- symbol-at --workspace navlyn.slnx --file navlyn\Cli\Commands\CheckCommand.cs --line 6 --column 23
dotnet run --no-launch-profile --project navlyn -- definition --workspace navlyn.slnx --file navlyn\Cli\NavlynCli.cs --line 31 --column 37
dotnet run --no-launch-profile --project navlyn -- references --workspace navlyn.slnx --file navlyn\Cli\NavlynCli.cs --line 31 --column 37
dotnet run --no-launch-profile --project navlyn -- implementations --workspace navlyn.slnx --file navlyn\Cli\Commands\CheckCommand.cs --line 6 --column 23
dotnet run --no-launch-profile --project navlyn -- callers --workspace navlyn.slnx --file navlyn\Cli\Commands\CheckCommand.cs --line 8 --column 27
dotnet run --no-launch-profile --project navlyn -- calls --workspace navlyn.slnx --file navlyn\Cli\NavlynCli.cs --line 28 --column 32
```

# Agent Instructions

## Purpose

Navlyn is a C#/.NET CLI for repository-local semantic code navigation and investigation. It is built for agents, automation, and developers who need stable JSON facts about C# workspaces.

Agents working here should make small, verifiable changes while preserving the public CLI contract documented in `docs/navlyn-cli-commands.md`.

## Working Principles

- Inspect the current code before changing behavior.
- Keep changes narrow and consistent with the existing project style.
- Preserve user changes in the working tree. Do not revert unrelated edits.
- Keep generated artifacts, build output, and local notes out of commits.
- Prefer Roslyn/MSBuild APIs for C# semantic behavior.
- Keep command results on stdout and diagnostics, progress, and errors on stderr.
- Prefer deterministic JSON for automation-facing output.
- Normalize paths before comparing them and emit repository-relative paths with `/` separators where possible.

## C# And .NET

- Use modern C# with nullable reference types enabled.
- Store `.cs` files as UTF-8 with BOM, CRLF line endings, and spaces for indentation.
- Keep new C# files consistent with `.editorconfig`.
- Use `./scripts/normalize-csharp-files.ps1` if C# file formatting drifts.
- Use `./scripts/test-csharp-file-format.ps1` to verify C# file encoding and line endings directly.

## Investigation

Use `rg --files` and `rg "<query>"` for text search, comments, strings, docs, non-C# files, and fallback investigation. Prefer existing Navlyn commands for C# semantic questions that the CLI already supports.

For MCP clients, use `navlyn_file_outline` for one known source file, `navlyn_target` for approximate symbol intent, and `navlyn_read` or `navlyn_navigate` for one precise source or relationship fact. Use broader review, context, test, or batch tools only when the task requires them.

## Verification

Use the smallest useful check for the change: focused xUnit component tests and one affected command or fixture. For integrated changes, `./scripts/test-quick.ps1` builds once, runs product tests on .NET 10 once, and runs a small portable CLI smoke. Reuse successful outputs with `-NoBuild -SkipDotnetTest` rather than repeating checks. Release preparation adds `./scripts/test-release.ps1 -NoBuild -SkipDotnetTest`. CI runs the full product suite on Windows and small transport/CLI checks on Linux/macOS; packaging and installed-package smoke use one retained build. Do not make every fixture, live evaluation, or historical scoring campaign a release gate. See `docs/navlyn-development-workflow.md` for focused commands and diagnostics.

## Documentation

- Update `docs/navlyn-cli-commands.md` when implemented CLI behavior changes.
- Keep README files focused on users.
- Keep always-on instruction files short.
- Put durable development workflow guidance in `docs/navlyn-development-workflow.md`.

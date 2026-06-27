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

## Verification

For general code changes, start with:

```powershell
dotnet restore navlyn.slnx
dotnet build navlyn.slnx
dotnet test navlyn.slnx --no-build
./scripts/test-quick.ps1 -NoBuild
```

For CLI contract changes, also run `./scripts/test-cli-contract.ps1 -NoBuild` and manually inspect the affected command. For semantic navigation behavior, add or update xUnit resolver component coverage when possible, then run the focused fixture scripts that match the change. For large refactors or release preparation, run `./scripts/test-release.ps1`. Timeout and file-lock guidance lives in `docs/navlyn-development-workflow.md`.

If CLI behavior changes, run the affected command manually and inspect stdout, stderr, deterministic JSON shape, and exit behavior.

## Documentation

- Update `docs/navlyn-cli-commands.md` when implemented CLI behavior changes.
- Keep README files focused on users.
- Keep always-on instruction files short.
- Put durable development workflow guidance in `docs/navlyn-development-workflow.md`.

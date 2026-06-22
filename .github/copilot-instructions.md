# GitHub Copilot Instructions

Navlyn is a C#/.NET CLI for repository-local semantic code navigation.

Keep this file short because GitHub Copilot may attach it to every chat or coding-agent request.

Core rules:

- Inspect the current code before changing behavior.
- Preserve the public CLI contract in `docs/navlyn-cli-commands.md`.
- Prefer Roslyn/MSBuild APIs for C# semantic answers.
- Keep command results on stdout; send diagnostics, progress, and errors to stderr.
- Prefer deterministic JSON for automation-facing output.
- Use `rg` for text search, docs, comments, strings, non-C# files, and fallback investigation.
- Keep generated artifacts, build output, and local notes out of commits.
- Validate relevant code changes with `dotnet restore navlyn.slnx`, `dotnet build navlyn.slnx`, and `.\scripts\smoke.ps1`.

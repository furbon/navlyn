# GitHub Copilot Instructions

Navlyn is a C#/.NET CLI for repository-local semantic code navigation and investigation.

Keep this file short because GitHub Copilot may attach it to every chat or coding-agent request.

Core rules:

- Inspect the current code before changing behavior.
- Preserve the public CLI contract in `docs/navlyn-cli-commands.md`.
- Prefer Roslyn/MSBuild APIs for C# semantic answers.
- Keep command results on stdout; send diagnostics, progress, and errors to stderr.
- Prefer deterministic JSON for automation-facing output.
- Emit repository-relative JSON paths with `/` separators where possible.
- When asked to use Navlyn MCP, call actual `navlyn_*` MCP tools; if they are unavailable, say so instead of treating source reads as MCP usage.
- Use `rg` for text search, docs, comments, strings, non-C# files, and fallback investigation.
- Keep generated artifacts, build output, and local notes out of commits.
- Validate relevant code changes with `dotnet restore navlyn.slnx`, `dotnet build navlyn.slnx`, `dotnet test navlyn.slnx --no-build`, and `./scripts/test-quick.ps1 -NoBuild`.
- Run `./scripts/test-cli-contract.ps1 -NoBuild` for CLI contract changes. For semantic navigation changes, add or update xUnit resolver component coverage when possible, then run the matching focused fixture script.

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
- Use normal file reads and `rg` first when text is enough. Use Navlyn only when C# semantic identity, project context, source relationships, diff facts, or bounded evidence would change the answer.
- For MCP symbol work, use `navlyn_file_outline` for one known C# file, `navlyn_resolve_target` for approximate symbol intent, then `navlyn_symbol_source` or `navlyn_symbol_edges` for one precise fact.
- Do not run `navlyn_review_diff`, `navlyn_tests_for_*`, `navlyn_context_pack`, or `navlyn_batch` as a default checklist. Use them only for an actual diff, explicit test-impact need, bounded context escalation, or multiple already-needed facts.
- Use `rg` for text search, docs, comments, strings, non-C# files, and fallback investigation.
- Keep generated artifacts, build output, and local notes out of commits.
- Validate relevant code changes with `dotnet restore navlyn.slnx`, `dotnet build navlyn.slnx`, `dotnet test navlyn.slnx --no-build`, and `./scripts/test-quick.ps1 -NoBuild -SkipDotnetTest`.
- Run `./scripts/test-cli-contract.ps1 -NoBuild -Suite core` for CLI contract wiring changes, and `-Suite all` for broad workflow/domain contract changes. For semantic navigation changes, add or update xUnit resolver component coverage when possible, then run the matching focused fixture script suite.

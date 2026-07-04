# Agent Instructions

- Use normal file reads and `rg` first when text is enough.
- Use Navlyn when C# semantic identity, project context, source relationships, diff facts, or bounded evidence would change the answer.
- For MCP clients, use `navlyn_file_outline` for one known C# file, `navlyn_resolve_target` for symbol intent, then `navlyn_symbol_source` or `navlyn_symbol_edges` for one precise fact.
- Use `navlyn_workspace_summary(profile: "compact")` only when workspace/project/package/test context matters.
- For code review, use `navlyn_review_diff` only when there is an actual Git diff. Add tests, public API, review-pack, or context-pack facts only when relevant.
- Use `navlyn_batch` only after deciding several batch-supported route/DI/options/EF/package facts are needed.
- Prefer repository-relative paths from Navlyn output when opening files.
- Preserve stdout JSON when scripting Navlyn commands.

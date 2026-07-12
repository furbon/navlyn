# Agent Instructions

- Use normal file reads and `rg` first when text is enough.
- Use Navlyn when C# or Visual Basic semantic identity, project context, source relationships, diff facts, or bounded evidence would change the answer.
- Run `navlyn doctor --workspace <workspace>` when setup, SDK, restore, workspace loading, or first-command guidance is uncertain.
- For MCP clients, use the unified read-only tool surface. Use `navlyn_file_outline` for one known C# or Visual Basic file, `navlyn_resolve_target` for symbol intent, then `navlyn_symbol_source` or `navlyn_symbol_edges` for one precise fact.
- Use `navlyn_review_diff` only for actual diff review facts and `navlyn_edit_preflight` only before a concrete edit.
- Before a non-trivial C# or Visual Basic edit, run `navlyn edit-preflight` or MCP `navlyn_edit_preflight`; after editing, run `navlyn post-edit-guard` or `navlyn_wrong_symbol_guard` before widening scope.
- Use `navlyn_workspace_summary(profile: "compact")` only when workspace/project/package/test context matters.
- For code review, use `navlyn_review_diff` only when there is an actual Git diff. Add tests, public API, review-pack, or context-pack facts only when relevant.
- Use `navlyn_batch` only after deciding several batch-supported route/DI/options/EF/package facts are needed.
- Prefer repository-relative paths from Navlyn output when opening files.
- Preserve stdout JSON when scripting Navlyn commands.

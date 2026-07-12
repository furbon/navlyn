# Agent Instructions

- Use normal file reads and `rg` first when text is enough.
- Use Navlyn only when C# or Visual Basic semantic identity, project context, source relationships, diff facts, or bounded evidence would change the answer.
- Run `navlyn doctor --workspace auto` when workspace loading or first-command guidance is uncertain.
- Use `navlyn repo-graph --profile compact` only when workspace/project/package/test context matters.
- Use `navlyn target` to choose a candidate id before deeper symbol-specific commands.
- In MCP, use the unified read-only tool surface. Use `navlyn_file_outline` for one known C# or Visual Basic file and `navlyn_read` or `navlyn_symbol_edges` for one selected symbol fact.
- Use `navlyn_review` only for actual Git diff review facts and `navlyn_prepare_edit` only before a concrete edit.
- Before a non-trivial C# or Visual Basic edit, run `navlyn prepare-edit` or MCP `navlyn_prepare_edit`; after editing, run `navlyn verify-edit` or MCP `navlyn_verify_edit` before widening scope.
- Use `navlyn find` when you need a broader candidate list.
- Use `navlyn context-pack --goal modify --profile compact` only when smaller facts or normal file reads are not enough.
- Use `navlyn review --profile evidence` only for actual Git diff or pull request review facts.
- In MCP, use `navlyn_batch` only after deciding several batch-supported facts are needed from the same workspace.
- Treat `review-pack` findings as evidence-backed signals, not final review comments.
- Keep Navlyn stdout as JSON and send diagnostics or notes elsewhere.

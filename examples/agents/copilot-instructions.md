# Agent Instructions

- Use normal file reads and `rg` first when text is enough.
- Use Navlyn only when C# or Visual Basic semantic identity, project context, source relationships, diff facts, or bounded evidence would change the answer.
- Run `navlyn doctor --workspace <workspace>` when setup, SDK, restore, workspace loading, or first-command guidance is uncertain.
- Use `navlyn repo-graph --profile compact` only when workspace/project/package/test context matters.
- Use `navlyn resolve-target` to choose a candidate id before deeper symbol-specific commands.
- In MCP, the default `reader` profile exposes file-first and selected-symbol tools. Use `navlyn_file_outline` for one known C# or Visual Basic file and `navlyn_symbol_source` or `navlyn_symbol_edges` for one selected symbol fact.
- Start `navlyn-mcp` with `--tool-profile review` for actual Git diff review and guard checks, `edit` for `navlyn_edit_preflight`, or `full` when every MCP tool is needed.
- Before a non-trivial C# or Visual Basic edit, run `navlyn edit-preflight` or MCP `navlyn_edit_preflight`; after editing, run `navlyn post-edit-guard` or `navlyn_wrong_symbol_guard` before widening scope.
- Use `navlyn find` when you need a broader candidate list.
- Use `navlyn context-pack --goal modify --profile compact` only when smaller facts or normal file reads are not enough.
- Use `navlyn review-diff --profile evidence` only for actual Git diff or pull request review facts.
- In MCP, use `navlyn_batch` only in `full` profile after deciding several batch-supported facts are needed from the same workspace.
- Treat `review-pack` findings as evidence-backed signals, not final review comments.
- Keep Navlyn stdout as JSON and send diagnostics or notes elsewhere.

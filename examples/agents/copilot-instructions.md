# Agent Instructions

- Use normal file reads and `rg` first when text is enough.
- Use Navlyn only when C# semantic identity, project context, source relationships, diff facts, or bounded evidence would change the answer.
- Use `navlyn repo-graph --profile compact` only when workspace/project/package/test context matters.
- Use `navlyn resolve-target` to choose a candidate id before deeper symbol-specific commands.
- In MCP, use `navlyn_file_outline` for one known C# file and `navlyn_symbol_source` or `navlyn_symbol_edges` for one selected symbol fact.
- Use `navlyn find` when you need a broader candidate list.
- Use `navlyn context-pack --goal modify --profile compact` only when smaller facts or normal file reads are not enough.
- Use `navlyn review-diff --profile evidence` only for actual Git diff or pull request review facts.
- In MCP, use `navlyn_batch` only after deciding several batch-supported facts are needed from the same workspace.
- Treat `review-pack` findings as evidence-backed signals, not final review comments.
- Keep Navlyn stdout as JSON and send diagnostics or notes elsewhere.

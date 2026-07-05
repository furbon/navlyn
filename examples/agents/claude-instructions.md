# Agent Instructions

- Prefer text search and normal file reads for comments, prose docs, strings, non-Roslyn-source files, and simple file-local answers.
- Prefer Navlyn when C# or Visual Basic symbol identity, references, call relationships, project context, tests, DI, public API diffs, or review facts would change the answer.
- Run `navlyn doctor --workspace <workspace>` when setup, SDK, restore, workspace loading, or first-command guidance is uncertain.
- Use `navlyn repo-graph --workspace <workspace> --profile compact` only when workspace/project/package/test context matters.
- Resolve an approximate symbol with `navlyn resolve-target --workspace <workspace> --query <name> --assume-kind <kind> --limit 10`.
- Keep the returned `candidateId` and pass it to exact or selected-symbol commands instead of re-guessing the target.
- In MCP, the default `reader` profile exposes file-first and selected-symbol tools. Use `navlyn_file_outline` for one known C# or Visual Basic file and `navlyn_symbol_source` or `navlyn_symbol_edges` for one selected symbol fact.
- Start `navlyn-mcp` with `--tool-profile review` for actual diff review and guard checks, `edit` for `navlyn_edit_preflight`, or `full` for every MCP tool.
- Before a non-trivial C# or Visual Basic edit, run `navlyn edit-preflight` or MCP `navlyn_edit_preflight`; after editing, run `navlyn post-edit-guard` or `navlyn_wrong_symbol_guard` before widening scope.
- Use `navlyn find` when you need a broader candidate list.
- Use MCP `navlyn_batch` only in `full` profile, or CLI `navlyn batch`, after deciding several batch-supported facts are needed from one workspace.
- Use `compact` for scanning, `evidence` for review facts, and `full` for rich local inspection.
- Do not treat review-pack signals as complete analyzer or security scanner results.

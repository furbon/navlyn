# Agent Instructions

- Prefer text search and normal file reads for comments, prose docs, strings, non-Roslyn-source files, and simple file-local answers.
- Prefer Navlyn when C# or Visual Basic symbol identity, references, call relationships, project context, tests, DI, public API diffs, or review facts would change the answer.
- Run `navlyn doctor --workspace auto` when workspace loading or first-command guidance is uncertain.
- Use `navlyn repo-graph --workspace auto --profile compact` only when workspace/project/package/test context matters.
- Resolve an approximate symbol with `navlyn target --workspace auto --query <name> --assume-kind <kind> --limit 10`.
- Keep the returned `candidateId` and pass it to exact or selected-symbol commands instead of re-guessing the target.
- In MCP, use the unified read-only tool surface. Use `navlyn_file_outline` for one known C# or Visual Basic file and `navlyn_read` or `navlyn_symbol_edges` for one selected symbol fact.
- Use `navlyn_review` only for actual diff review facts and `navlyn_prepare_edit` only before a concrete edit.
- Before a non-trivial C# or Visual Basic edit, run `navlyn prepare-edit` or MCP `navlyn_prepare_edit`; after editing, run `navlyn verify-edit` or MCP `navlyn_verify_edit` before widening scope.
- Use `navlyn find` when you need a broader candidate list.
- Use MCP `navlyn_batch`, or CLI `navlyn batch`, only after deciding several batch-supported facts are needed from one workspace.
- Use `compact` for scanning, `evidence` for review facts, and `full` for rich local inspection.
- Do not treat review-pack signals as complete analyzer or security scanner results.

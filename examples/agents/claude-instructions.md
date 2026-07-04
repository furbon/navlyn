# Agent Instructions

- Prefer text search and normal file reads for comments, prose docs, strings, non-C# files, and simple file-local answers.
- Prefer Navlyn when C# symbol identity, references, call relationships, project context, tests, DI, public API diffs, or review facts would change the answer.
- Use `navlyn repo-graph --workspace <workspace> --profile compact` only when workspace/project/package/test context matters.
- Resolve an approximate symbol with `navlyn resolve-target --workspace <workspace> --query <name> --assume-kind <kind> --limit 10`.
- Keep the returned `candidateId` and pass it to exact or selected-symbol commands instead of re-guessing the target.
- In MCP, use `navlyn_file_outline` for one known C# file and `navlyn_symbol_source` or `navlyn_symbol_edges` for one selected symbol fact.
- Use `navlyn find` when you need a broader candidate list.
- Use `navlyn_batch` or `navlyn batch` only after deciding several batch-supported facts are needed from one workspace.
- Use `compact` for scanning, `evidence` for review facts, and `full` for rich local inspection.
- Do not treat review-pack signals as complete analyzer or security scanner results.

# Agent Instructions

- Prefer Navlyn for C# symbol facts, references, call relationships, tests, DI, public API diffs, and review facts.
- Prefer text search for comments, prose docs, and non-C# files.
- Start with `navlyn repo-graph --workspace <workspace> --profile compact`.
- Resolve an approximate symbol with `navlyn resolve-target --workspace <workspace> --query <name> --assume-kind <kind> --limit 10`.
- Keep the returned `candidateId` and pass it to exact or selected-symbol commands instead of re-guessing the target.
- Use `navlyn find` when you need a broader candidate list.
- Use `navlyn_batch` or `navlyn batch` when several facts are needed from one workspace.
- Use `compact` for scanning, `evidence` for review facts, and `full` for rich local inspection.
- Do not treat review-pack signals as complete analyzer or security scanner results.

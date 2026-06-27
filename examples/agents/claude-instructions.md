# Agent Instructions

- Prefer Navlyn for C# symbol facts, references, call relationships, tests, DI, public API diffs, and review facts.
- Prefer text search for comments, prose docs, and non-C# files.
- Use `navlyn_batch` or `navlyn batch` when several facts are needed from one workspace.
- Use `compact` for scanning, `evidence` for review facts, and `full` for rich local inspection.
- Do not treat review-pack signals as complete analyzer or security scanner results.

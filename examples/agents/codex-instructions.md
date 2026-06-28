# Agent Instructions

- Inspect existing code before changing behavior.
- Use Navlyn for Roslyn-backed C# facts and `rg` for text search.
- First Navlyn pass:
  - `navlyn repo-graph --workspace navlyn.slnx --profile compact`
  - `navlyn resolve-target --workspace navlyn.slnx --query <SymbolName> --assume-kind NamedType --limit 10`
  - reuse `candidateId` for `definition`, `references`, `impact`, `tests-for-symbol`, and `context-pack`.
- For review work, run `review-diff --profile evidence` and `review-pack --profile evidence`.
- For bounded reading material, run `context-pack --profile compact`.
- Use `find` when you need a broader candidate list instead of one selected target.
- Use `navlyn batch` when you need several facts from the same workspace.
- Keep generated artifacts and local reports out of commits.

# Agent Instructions

- Inspect existing code before changing behavior.
- Use `rg` and normal file reads for text search, comments, docs, strings, non-C# files, and simple file-local answers.
- Use Navlyn when Roslyn-backed C# identity, overloads, project context, references, impact, DI, public API, or review facts would change the answer.
- Minimal Navlyn path: `resolve-target --workspace navlyn.slnx --query <SymbolName> --assume-kind NamedType --limit 10`, then reuse `candidateId` for exact facts.
- MCP path: in default `reader` profile, use `navlyn_file_outline` for one known C# file, `navlyn_resolve_target` for approximate symbol intent, then `navlyn_symbol_source` or `navlyn_symbol_edges` for one precise fact.
- Start `navlyn-mcp` with `--tool-profile review` for actual diff review, `edit` for edit planning, or `full` for the complete compatibility surface.
- Ask for `repo-graph --profile compact` only when workspace/project/package/test context matters.
- For review work, use `review-diff --profile evidence` only when there is an actual Git diff.
- Use `context-pack --profile compact` only when smaller facts or normal file reads are not enough.
- Use `find` when you need a broader candidate list instead of one selected target.
- Use `navlyn batch`, or MCP `navlyn_batch` in `full` profile, when several batch-supported facts are already needed from the same workspace.
- Keep generated artifacts and local reports out of commits.

# Agent Instructions

- Start C# repository investigations with `repo-graph`, then use `resolve-target` and candidate ids for symbol workflows.
- For MCP clients, start with `navlyn_workspace_summary(profile: "compact")`, then `navlyn_resolve_target`, then `navlyn_exact_navigation` or `navlyn_context_pack`.
- For code review, collect `review-diff`, `tests-for-diff`, `public-api-diff`, and `review-pack` facts.
- Use `navlyn_batch` for route/DI/options/EF/package workflows that need several application-domain facts.
- Prefer repository-relative paths from Navlyn output when opening files.
- Preserve stdout JSON when scripting Navlyn commands.

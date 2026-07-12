# CI Instructions

- Prefer the `navlyn` CLI in CI; MCP is for interactive agent clients.
- Run `dotnet restore` for the target repository before semantic Navlyn commands.
- Start with `navlyn doctor --workspace auto` unless CI policy pins an explicit workspace, and fail only on explicit CI policy, not on uninspected warnings.
- For PR evidence, publish JSON artifacts from `review`, `tests-for-diff`, `public-api-diff`, or `./scripts/write-navlyn-pr-facts.ps1`.
- For edit-policy checks, use `verify-edit` or `wrong-symbol-guard` with an explicit `--fail-on-risk` threshold.
- Keep all generated reports under ignored artifact paths and do not commit local timing, eval, package, or review outputs.
- Navlyn does not run tests, publish review comments, decide SemVer, scan secrets, or prove runtime behavior.

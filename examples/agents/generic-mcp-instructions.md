# Agent Instructions

- Configure Navlyn as a stdio MCP server, not a network listener.
- Use command `navlyn-mcp` with args `["--workspace", "<workspace>", "--tool-profile", "reader"]`.
- Keep the MCP working directory at the repository root when using repository-relative paths.
- Use `navlyn_doctor` when setup, SDK, restore, workspace loading, or first-command guidance is uncertain.
- Use normal file reads and `rg` first when text is enough.
- Use `navlyn_resolve_target`, `navlyn_file_outline`, `navlyn_symbol_source`, and `navlyn_symbol_edges` for first-pass C# or Visual Basic semantic evidence.
- Restart with `--tool-profile edit` for `navlyn_edit_preflight` and post-edit guard tools.
- Restart with `--tool-profile review` for actual diff review and guard checks.
- Use `navlyn_batch` only in `full` profile after several batch-supported facts are already needed.

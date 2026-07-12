# Agent Instructions

- Configure Navlyn as a stdio MCP server, not a network listener.
- Use command `navlyn-mcp` with the MCP working directory at the repository root. Omit `--workspace` for normal repositories with one top-level workspace candidate.
- Add args `["--workspace", "<workspace>"]` only when automatic discovery is ambiguous or repository policy requires one explicit workspace.
- Use `navlyn_doctor` when workspace loading or first-command guidance is uncertain.
- Use normal file reads and `rg` first when text is enough.
- Use `navlyn_target`, `navlyn_file_outline`, `navlyn_read`, and `navlyn_symbol_edges` for first-pass C# or Visual Basic semantic evidence.
- Use `navlyn_prepare_edit` and `navlyn_verify_edit` only for concrete edit evidence.
- Use `navlyn_review` only for actual diff review facts.
- Use `navlyn_batch` only after several batch-supported facts are already needed.

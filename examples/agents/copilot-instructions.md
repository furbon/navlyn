# GitHub Copilot Instructions

Use this guidance with GitHub Copilot CLI or VS Code + GitHub Copilot after configuring Navlyn MCP. The Codex routing skill is a separate Codex-only installation and is not installed by this file.

- For Copilot CLI, copy `examples/install/copilot-cli-mcp.json` to the repository root as `.mcp.json` or `.github/mcp.json`; replace the command with the absolute path to your installed `navlyn-mcp.exe`. The Copilot CLI config uses `mcpServers`.
- For VS Code, configure `.vscode/mcp.json` using VS Code's `servers` format and `${workspaceFolder}`. Do not use the Copilot CLI config file as the VS Code config.
- A configuration file alone does not prove that MCP started. Confirm a live `navlyn_target` call against the installed 0.9.1 server in the repository you intend to inspect.

- Use normal file reads and `rg` first when text is enough.
- Use Navlyn only when C# or Visual Basic semantic identity, project context, source relationships, diff facts, or bounded evidence would change the answer.
- Run `navlyn doctor --workspace auto` when workspace loading or first-command guidance is uncertain.
- Use `navlyn repo-graph --profile compact` only when workspace/project/package/test context matters.
- Use `navlyn target` when symbol identity is unresolved; reuse a known source position or candidate directly for the requested fact.
- MCP defaults to target/read/file_outline/navigate with compact results. Advanced tools require `--surface full`. Use `navlyn_file_outline` for one known C# or Visual Basic file and `navlyn_read` or `navlyn_navigate` for one selected symbol fact.
- Use review, preparation, and verification when those semantic facts answer a requested question or material uncertainty.
- Use ordinary reads, `rg`, edits, and diff checks for locally clear changes. Do not add semantic calls simply because an edit or overload exists. Stop when the task is complete.
- Use `navlyn find` when you need a broader candidate list.
- Use `navlyn context-pack --goal modify --profile compact` only when smaller facts or normal file reads are not enough.
- Use `navlyn review --profile evidence` only for actual Git diff or pull request review facts.
- In MCP, use `navlyn_batch` only after deciding several batch-supported facts are needed from the same workspace.
- Treat `review-pack` findings as evidence-backed signals, not final review comments.
- Keep Navlyn stdout as JSON and send diagnostics or notes elsewhere.

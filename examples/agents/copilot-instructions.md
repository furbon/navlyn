# GitHub Copilot Instructions

Use this guidance with GitHub Copilot CLI or VS Code + GitHub Copilot after configuring Navlyn MCP. The Codex routing skill is a separate Codex-only installation and is not installed by this file.

- For Copilot CLI, copy `examples/install/copilot-cli-mcp.json` to the repository root as `.mcp.json` or `.github/mcp.json`; replace the command with the absolute path to your installed `navlyn-mcp.exe`. The Copilot CLI config uses `mcpServers`.
- For VS Code, configure `.vscode/mcp.json` using VS Code's `servers` format and `${workspaceFolder}`. Do not use the Copilot CLI config file as the VS Code config.
- A configuration file alone does not prove that MCP started. Confirm a live `navlyn_target` call against the installed 0.8.5 server in the repository you intend to inspect.

- Use normal file reads and `rg` first when text is enough.
- Use Navlyn only when C# or Visual Basic semantic identity, project context, source relationships, diff facts, or bounded evidence would change the answer.
- Run `navlyn doctor --workspace auto` when workspace loading or first-command guidance is uncertain.
- Use `navlyn repo-graph --profile compact` only when workspace/project/package/test context matters.
- Use `navlyn target` to choose a candidate id before deeper symbol-specific commands.
- In MCP, use the unified read-only tool surface. Use `navlyn_file_outline` for one known C# or Visual Basic file and `navlyn_read` or `navlyn_navigate` for one selected symbol fact.
- Use `navlyn_review` only for actual Git diff review facts and `navlyn_prepare_edit` only before a concrete edit.
- Before a non-trivial C# or Visual Basic edit, run `navlyn prepare-edit` or MCP `navlyn_prepare_edit`; after editing, run `navlyn verify-edit` or MCP `navlyn_verify_edit` before widening scope.
- Use `navlyn find` when you need a broader candidate list.
- Use `navlyn context-pack --goal modify --profile compact` only when smaller facts or normal file reads are not enough.
- Use `navlyn review --profile evidence` only for actual Git diff or pull request review facts.
- In MCP, use `navlyn_batch` only after deciding several batch-supported facts are needed from the same workspace.
- Treat `review-pack` findings as evidence-backed signals, not final review comments.
- Keep Navlyn stdout as JSON and send diagnostics or notes elsewhere.

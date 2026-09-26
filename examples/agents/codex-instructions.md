# Codex Agent Instructions

The Navlyn routing skill is installed separately from the MCP server. From the current repository, call the installer in the Navlyn source checkout and target this repository's `.agents/skills` directory:

```powershell
$navlynSource = 'C:\path\to\navlyn'
$skillRoot = Join-Path (Get-Location) '.agents/skills'
New-Item -ItemType Directory -Force $skillRoot | Out-Null
& (Join-Path $navlynSource 'scripts/install-routing-skill.ps1') -Action Install -DestinationRoot $skillRoot
```

Uninstall a managed copy with `& (Join-Path $navlynSource 'scripts/install-routing-skill.ps1') -Action Uninstall -DestinationRoot $skillRoot`. The installer uses an adjacent ownership marker and stops on a missing/invalid marker or divergent files; preserve and inspect conflicts before resolving them. The isolated activation smoke was verified with Codex CLI 0.155.0-alpha.16 on Windows in a process-scoped full-access session. This example does not imply support in GitHub Copilot.

- Inspect existing code before changing behavior.
- Use `rg` and normal file reads for text search, comments, docs, strings, non-Roslyn-source files, and simple file-local answers.
- Use Navlyn when Roslyn-backed C# or Visual Basic identity, overloads, project context, references, impact, DI, public API, or review facts would change the answer.
- Run `navlyn doctor --workspace auto` when workspace loading or first-command guidance is uncertain.
- Minimal Navlyn path: `target --workspace auto --query <SymbolName> --assume-kind NamedType --limit 10`, then reuse `candidateId` for exact facts.
- MCP path: use the unified read-only tool surface. Start with `navlyn_file_outline` for one known C# or Visual Basic file, `navlyn_target` for approximate symbol intent, then `navlyn_read` or `navlyn_navigate` for one precise fact.
- Use `navlyn_review` only for actual diff review facts and `navlyn_prepare_edit` only before a concrete edit.
- Before a non-trivial C# or Visual Basic edit, run `navlyn prepare-edit`; after editing, run `navlyn verify-edit` or `navlyn wrong-symbol-guard` before widening scope.
- Ask for `repo-graph --profile compact` only when workspace/project/package/test context matters.
- For review work, use `review --profile evidence` only when there is an actual Git diff.
- Use `context-pack --profile compact` only when smaller facts or normal file reads are not enough.
- Use `find` when you need a broader candidate list instead of one selected target.
- Use `navlyn batch`, or MCP `navlyn_batch`, when several batch-supported facts are already needed from the same workspace.
- Keep generated artifacts and local reports out of commits.

# Navlyn First 10 Minutes

This guide gets a C# repository from "Navlyn is installed" to "an agent has semantic evidence before editing" without reading the full command reference.

Use one path first. The CLI path is easiest to verify in a shell. The MCP path is best when an agent client should call Navlyn directly.

## 0-2 Minutes: Install And Check

Global install:

```powershell
dotnet tool install --global navlyn --version 0.6.0
dotnet tool install --global navlyn-mcp --version 0.6.0
```

Repository-local install:

```powershell
dotnet new tool-manifest
dotnet tool install navlyn --version 0.6.0
dotnet tool install navlyn-mcp --version 0.6.0
dotnet tool restore
```

Check the workspace:

```powershell
navlyn check --workspace path/to/YourRepo.slnx
```

Success means stdout is JSON with `ok: true`, the expected workspace path, and a project count. Stderr should be empty on success.

## 2-4 Minutes: Anchor One Symbol

Pick one C# type or method that an agent might edit:

```powershell
navlyn resolve-target --workspace path/to/YourRepo.slnx --query PaymentService --assume-kind NamedType --limit 10
```

Stop when there is one high-confidence `candidateId`. If candidates are ambiguous, ask the user or add `--project` / a more precise `--assume-kind`.

## 4-6 Minutes: Open Bounded Evidence

Use the returned `candidateId` instead of searching the name again:

```powershell
navlyn symbol-source --workspace path/to/YourRepo.slnx --candidate-id sym:v1:... --view declaration
navlyn references --workspace path/to/YourRepo.slnx --candidate-id sym:v1:... --group-by file --limit 50
```

This is the core pre-edit evidence loop: resolve the target, inspect bounded source, then inspect only the relationships needed for the task.

## 6-8 Minutes: Give An Agent MCP Reader Tools

Start narrow:

```json
{
  "command": "navlyn-mcp",
  "args": ["--workspace", "path/to/navlyn.workspace.json", "--tool-profile", "reader"]
}
```

In the first session, prefer:

```text
navlyn_resolve_target
navlyn_file_outline
navlyn_symbol_source
navlyn_symbol_edges
navlyn_about_symbol
```

Switch to `--tool-profile edit` for concrete edit planning and `review` for real Git diffs. Use `full` only when the client needs every tool, including `navlyn_batch`.

## 8-10 Minutes: Post-Edit Evidence

After an edit, check the actual diff:

```powershell
navlyn changed-symbols --workspace path/to/YourRepo.slnx --profile compact
navlyn review-diff --workspace path/to/YourRepo.slnx --profile evidence --symbol-limit 20 --impact-limit 40 --diagnostic-limit 40 --related-test-limit 20
```

Compare the changed symbols to the pre-edit `candidateId` and source facts. A mismatch is not proof of a bug, but it is the moment to pause before more edits.

## If It Fails

Check these first:

- `dotnet restore` succeeds for the target repository.
- The `--workspace` path points at the intended `.slnx`, `.sln`, `.csproj`, `.code-workspace`, or `navlyn.workspace.json`.
- `--workspace auto` is not ambiguous.
- The selected project or target framework is the one the agent should reason about.
- Generated or outside-root files are intentional and allowed by workspace policy.

Then run:

```powershell
navlyn check --workspace path/to/YourRepo.slnx
navlyn repo-graph --workspace path/to/YourRepo.slnx --profile compact
```

The first command checks load health. The second shows project names, target frameworks, package facts, and test relationships you can use for more precise follow-up calls.

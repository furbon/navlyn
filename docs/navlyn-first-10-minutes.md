# Navlyn First 10 Minutes

This guide gets a C# repository from "Navlyn is installed" to "an agent has semantic evidence before editing" without reading the full command reference. Before this guide, configure the MCP server from the [README](../README.md#use-with-mcp); `navlyn.workspace.json` is optional.

Use one path first. The CLI path is easiest to verify in a shell. The MCP path is best when an agent client should call Navlyn directly.

## 0-2 Minutes: Install And Diagnose

Global install:

```powershell
dotnet tool install --global navlyn --version 0.7.0
dotnet tool install --global navlyn-mcp --version 0.7.0
```

Repository-local install:

```powershell
dotnet new tool-manifest
dotnet tool install navlyn --version 0.7.0
dotnet tool install navlyn-mcp --version 0.7.0
dotnet tool restore
```

Diagnose the local SDK and workspace:

```powershell
navlyn doctor --workspace path/to/YourRepo.sln
```

Success means stdout is JSON with `ok: true`, the expected workspace path, SDK facts, checks, and a project count. Stderr should be empty on success. If the workspace path is wrong, `doctor` still returns JSON with `ok: false`, `workspace.error`, and `nextAction`.

## 2-4 Minutes: Anchor One Symbol

Pick one C# type or method that an agent might edit:

```powershell
navlyn target --workspace path/to/YourRepo.sln --query PaymentService --assume-kind NamedType --limit 10
```

Stop when there is one high-confidence `candidateId`. If candidates are ambiguous, ask the user or add `--project` / a more precise `--assume-kind`.

## 4-6 Minutes: Create Pre-Edit Evidence

Use the returned `candidateId` instead of searching the name again. For a concrete edit, `prepare-edit` collects the anchor, bounded source, bounded context, related tests, confidence evidence, and the post-edit guard command in one envelope:

```powershell
navlyn prepare-edit --workspace path/to/YourRepo.sln --candidate-id sym:v1:... --goal modify --change-kind behavior
```

If the task only needs one fact, use `read`, `references`, or `about` directly and stop. Use `prepare-edit` when an agent is about to modify code and needs a reusable evidence envelope.

## 6-8 Minutes: Give An Agent MCP Tools

Configure one read-only MCP surface for the workspace:

```json
{
  "command": "navlyn-mcp",
  "args": ["--workspace", "path/to/YourRepo.sln"]
}
```

The agent should choose the smallest semantic fact that answers the current question:

```text
navlyn_target
navlyn_file_outline
navlyn_read
navlyn_symbol_edges
navlyn_about_symbol
navlyn_prepare_edit
navlyn_verify_edit
navlyn_review
navlyn_doctor
```

Use edit and review tools only when their facts are relevant. They are read-only evidence tools; Navlyn still does not edit files, run tests, or publish review comments.

## 8-10 Minutes: Post-Edit Evidence

After an edit, check the actual diff:

```powershell
navlyn verify-edit --workspace path/to/YourRepo.sln --candidate-id sym:v1:... --fail-on-risk high
navlyn wrong-symbol-guard --workspace path/to/YourRepo.sln --query PaymentService --assume-kind NamedType --fail-on-risk medium
navlyn review --workspace path/to/YourRepo.sln --profile evidence --symbol-limit 20 --impact-limit 40 --diagnostic-limit 40 --related-test-limit 20
```

The guard commands return deterministic JSON even when policy fails. Exit code `1` means the diff did not satisfy the configured risk threshold, which is the moment to pause before more edits.

## If It Fails

Check these first:

- `dotnet restore` succeeds for the target repository.
- The `--workspace` path points at the intended `.slnx`, `.sln`, `.csproj`, `.vbproj`, `.code-workspace`, or `navlyn.workspace.json`.
- `--workspace auto` is not ambiguous.
- The selected project or target framework is the one the agent should reason about.
- Generated or outside-root files are intentional and allowed by workspace policy.

Then run:

```powershell
navlyn doctor --workspace path/to/YourRepo.sln
navlyn repo-graph --workspace path/to/YourRepo.sln --profile compact
```

The first command diagnoses SDK, workspace, restore assets, and repair hints. The second shows project names, target frameworks, package facts, and test relationships you can use for more precise follow-up calls.

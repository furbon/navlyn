# Navlyn

日本語: [`README_ja.md`](README_ja.md)

**Stop your coding agent from wandering through the codebase.**

Navlyn turns an instruction such as "fix `PaymentService`" into the small set of code, relationships, and checks an agent needs to act. It picks the intended target, gives the agent a reusable anchor for its investigation, and checks whether the actual diff stayed on that target.

```text
"Fix PaymentService"
        |
        v
choose the intended target -> read only what matters -> change it -> check the diff
```

Generic code search gives an agent a pile of matches. Navlyn keeps each next question attached to one selected target: which symbol the user meant, what code it depends on, what should be read before changing it, and whether the edit landed where intended. The agent reads less unrelated code, makes fewer broad searches, and keeps its context on facts that can change the edit.

`navlyn-mcp` is the primary way to use Navlyn with coding agents. The `navlyn` CLI uses the same engine for shell workflows, CI, and first-run verification.

## Three-Minute Path

The current `0.8.0-preview.1` candidate is a local release rehearsal and is not published to NuGet. On Windows with PowerShell 7 and .NET SDK 10, use the [local-feed quick start](docs/navlyn-first-10-minutes.md) to pack both target frameworks, install both tools with `--tool-path`, and run the installed absolute `navlyn.exe` to get a first semantic fact. The checked-in package manifest also names this preview; restore it only with the matching local feed, never public NuGet.

### Published 0.7.0

The following commands use the published `0.7.0` packages. To exercise the `0.8.0-preview.1` candidate, use the local-feed quick start above.

Install the MCP server:

```powershell
dotnet tool install --global navlyn-mcp --version 0.7.0
```

Install the CLI too when you want shell or CI JSON facts:

```powershell
dotnet tool install --global navlyn --version 0.7.0
```

Then verify one workspace and one symbol. In a normal repository with one top-level `.slnx`, `.sln`, `.csproj`, or `.vbproj`, use `auto`:

```powershell
navlyn doctor --workspace auto
navlyn target --workspace auto --query PaymentService --assume-kind NamedType --limit 10
navlyn read --workspace auto --candidate-id sym:v1:... --view declaration --max-lines 80
navlyn prepare-edit --workspace auto --candidate-id sym:v1:... --goal modify --change-kind behavior
```

Use the `candidateId` from `target` for follow-up calls. After an edit, inspect the real diff:

```powershell
navlyn review --workspace auto --profile evidence
```

If `auto` finds no workspace or more than one best candidate, pass the intended `.slnx`, `.sln`, `.csproj`, or `.vbproj` path explicitly, or add `navlyn.workspace.json` to make the repository choice shared.

## Use With MCP

For a repository with one top-level workspace candidate, the MCP server can use the repository root working directory and discover the workspace automatically. Use an explicit `--workspace` only when the repository has multiple plausible solutions or projects. If an MCP client does not launch servers from the repository root, pass `--working-directory <repo-root>` instead of `--workspace`.

### GitHub Copilot CLI

For a locally installed preview, copy [the Copilot CLI MCP example](examples/install/copilot-cli-mcp.json) to `.mcp.json` or `.github/mcp.json` in the repository root and replace the executable path with the absolute path to `navlyn-mcp.exe`. Copilot CLI configuration uses `mcpServers`. To enable repository MCP configuration for a prompt session in PowerShell, set the documented opt-in variable before starting Copilot:

```powershell
$env:GITHUB_COPILOT_PROMPT_MODE_WORKSPACE_MCP = 'true'
copilot
```

The `0.8.0-preview.1` package was exercised with Copilot CLI `1.0.88` on Windows using its explicit additional-config option: the installed server started and returned a semantic `navlyn_target` result. This verifies that client/package pair only. It does not mean the Codex skill works in Copilot. See the official [Copilot CLI MCP documentation](https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/add-mcp-servers) for current project-config opt-in behavior.

### GitHub Copilot In VS Code

Create `.vscode/mcp.json` in the repository root. For the local preview, replace `navlyn-mcp` with the installed executable's absolute path:

```json
{
  "servers": {
    "navlyn": {
      "type": "stdio",
      "command": "navlyn-mcp",
      "cwd": "${workspaceFolder}"
    }
  }
}
```

VS Code uses `.vscode/mcp.json` and the `servers` property as shown above. This configuration is documented; VS Code was not exercised in the release rehearsal. See the official [VS Code MCP documentation](https://code.visualstudio.com/docs/agent-customization/mcp-servers).

### Codex

For a published global installation, run this from the repository root. For the local preview, use the absolute executable in the [quick start](docs/navlyn-first-10-minutes.md#mcp-client-setup):

```powershell
codex mcp add navlyn -- navlyn-mcp
```

### Claude Code

Create `.mcp.json` in the repository root:

```json
{
  "mcpServers": {
    "navlyn": {
      "type": "stdio",
      "command": "navlyn-mcp",
      "args": ["--working-directory", "${CLAUDE_PROJECT_DIR:-.}"]
    }
  }
}
```

## Codex Routing Skill

The Navlyn routing skill is separate from `navlyn-mcp`. From the inspected repository, call the installer from the Navlyn source checkout and target that repository's `.agents/skills` directory:

```powershell
$navlynSource = (Resolve-Path '<navlyn-source-checkout>').Path
$skillRoot = Join-Path (Get-Location) '.agents/skills'
New-Item -ItemType Directory -Force $skillRoot | Out-Null
& (Join-Path $navlynSource 'scripts/install-routing-skill.ps1') -Action Install -DestinationRoot $skillRoot
```

Identical reinstall is safe. A valid adjacent `.navlyn-semantic-routing.install.json` marker records ownership. If the marker is absent/invalid, managed bytes differ, or unrelated files exist in the skill directory, the installer stops and preserves the content. Resolve a conflict only after reviewing it. Uninstall a managed copy with:

```powershell
& (Join-Path $navlynSource 'scripts/install-routing-skill.ps1') -Action Uninstall -DestinationRoot $skillRoot
```

Codex CLI `0.155.0-alpha.16` on Windows passed isolated discovery and a six-case activation smoke in a process-scoped full-access session with no source write attempts or diff. The tested read-only Windows sandbox could not launch WindowsApps PowerShell, so activation in that sandbox mode is not established. The preview skill is not a Copilot skill. See [the release contract](docs/navlyn-release-contract.md#client-support-claims) for tested support boundaries.

Navlyn MCP exposes one stable read-only semantic tool surface. The default startup discovers a single repository-local workspace candidate and fails closed when that choice is ambiguous. The agent should start with the smallest relevant fact, reuse `candidateId`, and stop when the returned JSON answers the question.

## What The Agent Gets

| Agent question | Navlyn MCP tool |
| --- | --- |
| Which symbol did the user mean? | `navlyn_target` |
| Show the declaration for this selected symbol. | `navlyn_read` |
| Who calls or references it? | `navlyn_navigate` |
| What should I know before changing it? | `navlyn_prepare_edit` |
| Did the actual diff stay on target? | `navlyn_verify_edit` |
| What did this Git diff affect? | `navlyn_review` |

The useful unit is one answerable question, not a repository dump. Navlyn returns bounded JSON facts so an agent can keep an exact `candidateId` through its investigation and only request the next relationship or source slice when it needs it.

## Use / Do Not Use

| Use Navlyn For | Do Not Use Navlyn For |
| --- | --- |
| C# or Visual Basic symbol identity, overloads, partial declarations, project context, target frameworks, DI, routes, related tests, and Git diff evidence. | Comments, strings, docs, arbitrary text search, generated artifacts, runtime proof, security scanning, test execution, editing, refactoring, or publishing review comments. |

Use normal file reads and `rg` when text is enough. Use Navlyn when Roslyn/MSBuild facts would change what the agent reads or edits.

## Workspace Choice

Most repositories do not need `navlyn.workspace.json`. For the MCP server, omit `--workspace` in the repository root; for CLI commands, use `--workspace auto`. Both forms select a single top-level `navlyn.workspace.json`, `.code-workspace`, `.slnx`, `.sln`, `.csproj`, or `.vbproj` candidate and fail instead of guessing when the best candidate is ambiguous.

Add `navlyn.workspace.json` only when a repository has several possible solutions/projects and needs one shared choice. Its smallest useful form is:

```json
{
  "primaryWorkspace": "YourRepo.sln"
}
```

The complete configuration reference, including candidate discovery and root policy, is in [docs/navlyn-workspace.md](docs/navlyn-workspace.md).

## CLI And CI

The CLI is optional after MCP setup, but it is the easiest way to verify installs, script facts, and produce CI evidence.

## Boundaries

Navlyn is local and read-only. It does not edit files, run arbitrary shell commands, call the network, upload source, or claim to prove runtime behavior. Use it where compiler and project facts matter; use normal file reads and `rg` where text is enough.

## Documentation

- [MCP server reference](docs/navlyn-mcp-server.md): stable tool surface, resources, and protocol behavior.
- [Workspace configuration](docs/navlyn-workspace.md): when and how to use `navlyn.workspace.json`.
- [First investigation](docs/navlyn-first-10-minutes.md): a short semantic investigation flow after setup.
- [Demos and case studies](docs/navlyn-demo-walkthroughs.md): reproducible current-repo and fixture-backed evidence.
- [CLI command reference](docs/navlyn-cli-commands.md): complete command and JSON contract.
- [Agent recipes](docs/navlyn-agent-recipes.md): focused CLI and MCP workflows.

Client-specific configuration formats are documented by [GitHub Copilot CLI](https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/add-mcp-servers), [Codex CLI](https://learn.chatgpt.com/docs/extend/mcp?surface=cli), [VS Code](https://code.visualstudio.com/docs/agent-customization/mcp-servers), and [Claude Code](https://docs.anthropic.com/en/docs/claude-code/mcp). A configuration example is documented support only; it does not establish client verification.

## License

Navlyn is licensed under the MIT License. See [LICENSE](LICENSE).

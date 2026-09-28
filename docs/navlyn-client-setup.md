# Use Navlyn from an AI tool

[日本語](navlyn-client-setup_ja.md)

MCP lets an AI tool call an external program. For Navlyn, that program is `navlyn-mcp`. Once connected, the AI tool can call `navlyn_target` and the other Navlyn tools.

These steps use Windows. Prepare [PowerShell 7](https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-windows) and a [.NET SDK](https://dotnet.microsoft.com/download) that can load your repository.

Run `dotnet tool list --global` to check for `navlyn-mcp`. If it is absent, install it below. If version 0.8.1 is listed, skip installation. For an older version, run `dotnet tool update --global navlyn-mcp --version 0.8.1`.

```powershell
dotnet tool install --global navlyn-mcp --version 0.8.1
$mcpExe = Join-Path $HOME '.dotnet/tools/navlyn-mcp.exe'
Test-Path $mcpExe
$mcpExe
```

If the tool was already installed, still run the `$mcpExe` lines above. When `Test-Path` returns `True`, use the final path printed to replace `C:\path\to\navlyn-mcp.exe` below. Open the repository you want to inspect before configuring a client.

## VS Code with GitHub Copilot

Follow [VS Code's GitHub Copilot setup](https://code.visualstudio.com/docs/copilot/setup) to sign in and make Copilot Chat available. See [VS Code's MCP guide](https://code.visualstudio.com/docs/agent-customization/mcp-servers) for the server controls.

1. Open the repository in VS Code.
2. Create `.vscode/mcp.json` in that repository and save the example below. Replace the executable path; keep the doubled backslashes in JSON.
3. Run `MCP: List Servers` from the Command Palette and start `navlyn`. If prompted to trust it, check the executable path before accepting.
4. Open Copilot Chat in `Agent` mode and enable Navlyn in the tool picker. Ask it to use `navlyn_target` to find a real type in this repository. Check the tool call and result.

```json
{
  "servers": {
    "navlyn": {
      "type": "stdio",
      "command": "C:\\path\\to\\navlyn-mcp.exe",
      "args": ["--working-directory", "${workspaceFolder}"]
    }
  }
}
```

If it cannot connect, check the executable path, the folder open in VS Code, and errors shown by `MCP: List Servers`. Remove the `navlyn` entry from `.vscode/mcp.json` to undo this setup.

## GitHub Copilot CLI

[Install and sign in to Copilot CLI](https://docs.github.com/en/copilot/how-tos/copilot-cli/install-copilot-cli). Its [MCP setup guide](https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/add-mcp-servers) describes repository configuration. Create `.mcp.json` at the repository root with the following content. Replace both paths.

```json
{
  "mcpServers": {
    "navlyn": {
      "type": "local",
      "command": "C:\\path\\to\\navlyn-mcp.exe",
      "args": ["--working-directory", "C:\\path\\to\\your-repository"]
    }
  }
}
```

Start `copilot` in that repository and respond to its repository trust prompt. Run `/mcp list` to check that `navlyn` appears, then ask it to use `navlyn_target` to find a real type and check the result. You can also list servers from a terminal with `copilot mcp list`.

For prompt mode in an untrusted folder, set `$env:GITHUB_COPILOT_PROMPT_MODE_WORKSPACE_MCP = 'true'` before starting it.

Copilot CLI does not read `.vscode/mcp.json`. Remove the entry from `.mcp.json` to undo this setup.

## Codex

Follow the [Codex quickstart](https://developers.openai.com/codex/quickstart). Replace `$mcpExe` with the absolute path to the installed `navlyn-mcp.exe`:

```powershell
$mcpExe = 'C:\path\to\navlyn-mcp.exe'
codex mcp add navlyn -- $mcpExe
codex mcp list
```

Start Codex from the repository you want to inspect and ask it to use `navlyn_target` to find a real type. If Codex starts elsewhere, add `--working-directory C:\path\to\your-repository` after the executable when registering it. Use `codex mcp remove navlyn` to remove the registration. See the [Codex MCP reference](https://developers.openai.com/codex/mcp).

To give Codex guidance on when to use Navlyn, see [install the Codex routing skill](navlyn-codex-routing-skill.md).

## Claude Code

Follow the [Claude Code quickstart](https://code.claude.com/docs/en/quickstart) to install and sign in. Run this from the repository you want to inspect, replacing both paths:

```powershell
claude mcp add --transport stdio navlyn -- 'C:\path\to\navlyn-mcp.exe' --working-directory 'C:\path\to\your-repository'
claude mcp list
```

In Claude Code, open `/mcp` to check the `navlyn` connection. Ask it to use `navlyn_target` to find a real type and check the tool result. Run `claude mcp remove navlyn` to remove the registration. See the [official MCP guide](https://code.claude.com/docs/en/mcp).

## After connecting

If `navlyn_target` cannot find the intended code, [run `doctor`](navlyn-first-10-minutes.md#2-run-it-in-a-repository) from the repository root. If several workspaces are possible, see [workspace configuration](navlyn-workspace.md). The [MCP reference](navlyn-mcp-server.md) lists all Navlyn tools.

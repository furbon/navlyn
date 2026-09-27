# Tell Codex when to use Navlyn

[日本語](navlyn-codex-routing-skill_ja.md)

The `navlyn-semantic-routing` skill gives Codex guidance on when to use Navlyn for C# and Visual Basic symbols. To let Codex actually call Navlyn tools, first complete the [Codex MCP setup](navlyn-client-setup.md#codex).

## 1. Get the Navlyn source

The skill is not part of the NuGet packages. Get the [Navlyn GitHub repository](https://github.com/furbon/navlyn). If you have Git:

```powershell
$navlynSource = Join-Path $HOME 'navlyn-source'
git clone https://github.com/furbon/navlyn.git $navlynSource
```

If you already have a source checkout, skip the clone and replace `$navlynSource` in the next step with its path.

## 2. Install in the repository you work on

Open PowerShell at the root of the repository you want to inspect. If you chose a different source directory above, set `$navlynSource` to its absolute path:

```powershell
$navlynSource = Join-Path $HOME 'navlyn-source'
$skillRoot = Join-Path (Get-Location) '.agents/skills'
New-Item -ItemType Directory -Force $skillRoot | Out-Null
& (Join-Path $navlynSource 'scripts/install-routing-skill.ps1') -Action Install -DestinationRoot $skillRoot
Get-ChildItem (Join-Path $skillRoot 'navlyn-semantic-routing')
```

If you see `SKILL.md` and `references`, start a new Codex session. Ask it to find references to a C# type or method and check whether it chooses a Navlyn tool when appropriate. Installing the skill alone does not confirm an MCP connection; also perform the [MCP connection check](navlyn-client-setup.md#codex).

## Update or remove

After updating the Navlyn source, run the `-Action Install` command again to update the skill. To remove it:

```powershell
& (Join-Path $navlynSource 'scripts/install-routing-skill.ps1') -Action Uninstall -DestinationRoot $skillRoot
```

The installer writes an ownership file named `.navlyn-semantic-routing.install.json`. It stops without overwriting or deleting files if managed files were edited by hand or unrelated files occupy the destination. Inspect the reported conflict before retrying.

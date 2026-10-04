# Tell Codex when to use Navlyn

[日本語](navlyn-codex-routing-skill_ja.md)

The optional `navlyn-semantic-routing` skill gives Codex guidance when C# or Visual Basic binding needs semantic evidence. An installed `navlyn` CLI is sufficient for occasional use; a running MCP server is not required. For repeated semantic calls in one session, [Codex MCP setup](navlyn-client-setup.md#codex) offers a shared workspace. Ordinary reads, search and clear local edits use normal tools.

For a light CLI integration, a repository instruction can state: "Navlyn CLI is available for uncertain binding or relationships; ordinary reads/search remain normal. At a known call position, `navlyn read --workspace <project> --file <source> --line <line> --column <column> --view body --external-source decompiled` returns the bound referenced-member body." The skill below is optional additional guidance, not a required file-reading preamble.

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

If you see `SKILL.md` and `references`, start a new Codex session. Ask it for a fact with uncertain symbol binding and check its actual tool choice. Installing the skill does not install either runtime. Verify the CLI with `navlyn --version`, or perform the [MCP connection check](navlyn-client-setup.md#codex) if you chose MCP.

## Update or remove

After updating the Navlyn source, run the `-Action Install` command again to update the skill. To remove it:

```powershell
& (Join-Path $navlynSource 'scripts/install-routing-skill.ps1') -Action Uninstall -DestinationRoot $skillRoot
```

The installer writes an ownership file named `.navlyn-semantic-routing.install.json`. It stops without overwriting or deleting files if managed files were edited by hand or unrelated files occupy the destination. Inspect the reported conflict before retrying.

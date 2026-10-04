# Navlyn

[日本語](https://github.com/furbon/navlyn/blob/main/README_ja.md)

Navlyn helps coding agents identify the right type or method in a .NET repository before they edit it. It focuses on C# and supports some Visual Basic scenarios. You can find declarations, references, and code worth reading before a change, then check which types or methods the diff affected.

Use the `navlyn-mcp` server with an agent, or the `navlyn` command from a terminal or CI. Both read local code and return JSON results.

## Start with your environment

- [Use Navlyn in VS Code with GitHub Copilot](https://github.com/furbon/navlyn/blob/main/docs/navlyn-client-setup.md#vs-code-with-github-copilot)
- [Use Navlyn in GitHub Copilot CLI](https://github.com/furbon/navlyn/blob/main/docs/navlyn-client-setup.md#github-copilot-cli)
- [Use Navlyn in Codex](https://github.com/furbon/navlyn/blob/main/docs/navlyn-client-setup.md#codex)
- [Use Navlyn in Claude Code](https://github.com/furbon/navlyn/blob/main/docs/navlyn-client-setup.md#claude-code)
- [Look up a type in a terminal](https://github.com/furbon/navlyn/blob/main/docs/navlyn-first-10-minutes.md)

To help Codex choose when to use Navlyn, see [install the Codex routing skill](https://github.com/furbon/navlyn/blob/main/docs/navlyn-codex-routing-skill.md).

## Try it in a terminal

You need a .NET SDK that can load the repository you want to inspect. Install the tools from NuGet:

```powershell
dotnet tool install --global navlyn --version 0.8.6
```

Open a new terminal at the root of the repository you want to inspect and check that Navlyn can select a workspace:

```powershell
navlyn doctor --workspace auto
```

If `auto` cannot choose one workspace, pass the intended `.slnx`, `.sln`, `.csproj`, or `.vbproj` path. The [10-minute guide](https://github.com/furbon/navlyn/blob/main/docs/navlyn-first-10-minutes.md) shows an isolated installation and walks through one type lookup.

For repository selection, see [workspace configuration](https://github.com/furbon/navlyn/blob/main/docs/navlyn-workspace.md). Complete command and result details are in the [CLI reference](https://github.com/furbon/navlyn/blob/main/docs/navlyn-cli-commands.md) and [MCP reference](https://github.com/furbon/navlyn/blob/main/docs/navlyn-mcp-server.md).

The Navlyn commands run locally and do not upload code. They do not edit code, run tests, or prove runtime behavior. Navlyn uses MSBuild to load projects, so run it only on repositories you trust. See [limitations and safety notes](https://github.com/furbon/navlyn/blob/main/docs/navlyn-limitations.md) for details.

Both .NET 8 and .NET 10 tool assets are included. See the [runtime support policy](https://github.com/furbon/navlyn/blob/main/docs/navlyn-runtime-support.md) for the upcoming .NET 8 support boundary.

Navlyn is released under the MIT License. See [LICENSE](https://github.com/furbon/navlyn/blob/main/LICENSE).

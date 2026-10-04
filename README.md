# Navlyn

[日本語](https://github.com/furbon/navlyn/blob/main/README_ja.md)

Navlyn gives coding agents compiler-resolved facts about .NET code when binding or relationships are uncertain. It focuses on C# and supports some Visual Basic scenarios. It can select a bound overload, read a referenced DLL's implementation, find callers, or investigate a change.

For occasional semantic questions, an agent can call the installed `navlyn` CLI when needed. A running MCP server is not required. Use `navlyn-mcp` for repeated semantic calls that benefit from a shared workspace. Both return local JSON facts; ordinary reads, search and clear local edits use normal tools. See [routing and CLI integration](https://github.com/furbon/navlyn/blob/main/docs/navlyn-codex-routing-skill.md).

## Start with your environment

- [Use Navlyn in VS Code with GitHub Copilot](https://github.com/furbon/navlyn/blob/main/docs/navlyn-client-setup.md#vs-code-with-github-copilot)
- [Use Navlyn in GitHub Copilot CLI](https://github.com/furbon/navlyn/blob/main/docs/navlyn-client-setup.md#github-copilot-cli)
- [Use Navlyn in Codex](https://github.com/furbon/navlyn/blob/main/docs/navlyn-client-setup.md#codex)
- [Use Navlyn in Claude Code](https://github.com/furbon/navlyn/blob/main/docs/navlyn-client-setup.md#claude-code)
- [Look up a type in a terminal](https://github.com/furbon/navlyn/blob/main/docs/navlyn-first-10-minutes.md)

To help Codex choose when to use Navlyn, see [install the Codex routing skill](https://github.com/furbon/navlyn/blob/main/docs/navlyn-codex-routing-skill.md).

## Try it in a terminal

You need a .NET 10 SDK that can load the repository you want to inspect. Install the tools from NuGet:

```powershell
dotnet tool install --global navlyn --version 0.9.2
```

Open a new terminal at the root of the repository you want to inspect and check that Navlyn can select a workspace:

```powershell
navlyn doctor --workspace auto
```

If `auto` cannot choose one workspace, pass the intended `.slnx`, `.sln`, `.csproj`, or `.vbproj` path. The [10-minute guide](https://github.com/furbon/navlyn/blob/main/docs/navlyn-first-10-minutes.md) shows an isolated installation and walks through one type lookup.

For repository selection, see [workspace configuration](https://github.com/furbon/navlyn/blob/main/docs/navlyn-workspace.md). Complete command and result details are in the [CLI reference](https://github.com/furbon/navlyn/blob/main/docs/navlyn-cli-commands.md) and [MCP reference](https://github.com/furbon/navlyn/blob/main/docs/navlyn-mcp-server.md).

The Navlyn commands run locally and do not upload code. They do not edit code, run tests, or prove runtime behavior. Navlyn uses MSBuild to load projects, so run it only on repositories you trust. See [limitations and safety notes](https://github.com/furbon/navlyn/blob/main/docs/navlyn-limitations.md) for details.

Navlyn v0.9.2 requires a .NET 10 SDK and ships only `net10.0` tool assets. See the [runtime support policy](https://github.com/furbon/navlyn/blob/main/docs/navlyn-runtime-support.md).

Navlyn is released under the MIT License. See [LICENSE](https://github.com/furbon/navlyn/blob/main/LICENSE).

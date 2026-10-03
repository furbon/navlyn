# Security Policy

## Supported Versions

Security fixes are handled for the latest public Navlyn release.

## Reporting A Vulnerability

Please report suspected vulnerabilities privately to the maintainer through GitHub's private vulnerability reporting flow if it is enabled for the repository. If private reporting is not available, open a minimal public issue that says a private security report is needed without including exploit details.

Include:

- affected package or command;
- Navlyn version;
- operating system;
- reproduction steps;
- whether the issue affects the CLI, MCP server, package distribution, or documentation.

## Security Boundary

Navlyn is a local facts provider for C#/.NET source workspaces. Its CLI and MCP tools do not offer source editing, arbitrary shell execution, network fetching, or workspace switching.

Load only trusted repositories. Roslyn workspace loading evaluates MSBuild projects and imports and can invoke design-time build tasks, analyzers, and generators supplied by the repository or its dependencies. Read-only Navlyn tools do not sandbox that build machinery. Restore dependencies separately with your normal trust policy; source inspection is not a way to safely open an untrusted build.

`navlyn.workspace.json` and `.code-workspace` files can intentionally reference folders outside the repository. CLI defaults allow those roots with warnings; `--workspace-root-policy repo-relative` blocks external discovery roots. MCP defaults to `repo-relative`; `allow-listed` permits configured `allowRoots`, while `all` deliberately broadens scope. Review configured roots and linked project inputs before loading private source. Root policy constrains workspace discovery, not the privileges of MSBuild or an operating-system sandbox.

The MCP direct-path cache is session-local and has no file watcher. Direct calls hash tracked source, project, configuration, reference assembly, analyzer, and default restore-assets inputs before reuse and before returning success. Stable changes reload automatically; unreadable or continually changing inputs fail closed. Force `navlyn_workspace_refresh` after restoring assets at custom locations outside tracked inputs, or changing uninventoried inputs. See the [MCP cache coverage](docs/navlyn-mcp-server.md) for details.

Navlyn results are bounded source-level evidence. They are not runtime proof, authorization proof, package compatibility proof, secret scanning, or a replacement for security review.

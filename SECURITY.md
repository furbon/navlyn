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

Navlyn is a local facts provider for C#/.NET source workspaces. The MCP server is intentionally read-only and does not edit files, execute arbitrary shell commands, access the network, or change the configured workspace.

`.code-workspace` files can intentionally reference folders outside the repository. Navlyn warns about external roots but treats the configured workspace as user-approved local input. Review workspace files before using them with private source.

The MCP direct-path cache is session-local and has no file watcher. Restart the MCP server after source, project, SDK, or package changes when freshness matters.

Navlyn results are bounded source-level evidence. They are not runtime proof, authorization proof, package compatibility proof, secret scanning, or a replacement for security review.

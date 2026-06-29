# Navlyn Architecture

Navlyn 0.4.0 is split into shared implementation assemblies and two tool frontends.

## Projects

- `Navlyn.Core`: Roslyn/MSBuild workspace loading, path handling, diagnostics, candidate IDs, resolver models, and semantic resolver implementations.
- `Navlyn.CommandLine`: the reusable command-line runtime, `System.CommandLine` command definitions, stdout/stderr JSON behavior, output profiles, and batch dispatch.
- `navlyn`: the packaged CLI .NET tool. It is a thin executable that configures console encoding and invokes `Navlyn.CommandLine`.
- `navlyn.Mcp`: the packaged MCP .NET tool. It owns MCP tool/resource/prompt schemas and result envelopes, and it calls `Navlyn.CommandLine` in-process by default.

`navlyn.Mcp` must not reference the `navlyn` executable project. The MCP package includes the shared assemblies through project references, so installing `navlyn-mcp` alone is enough for normal MCP use.

## Execution Paths

CLI:

1. `navlyn` parses CLI arguments through `Navlyn.CommandLine`.
2. Command handlers load the workspace through `Navlyn.Core`.
3. Resolver results are formatted as the existing deterministic CLI JSON on stdout.
4. Diagnostics, progress, and errors stay on stderr.

MCP default:

1. `navlyn.Mcp` receives an MCP tool, resource, or prompt request.
2. MCP arguments are validated and mapped to an allowlisted logical Navlyn command.
3. `NavlynInProcessCommandAdapter` runs the shared command runtime in-process.
4. The MCP result envelope returns `sourceCommand` for traceability and the command JSON under `result`.

MCP legacy external CLI:

1. This path is used only when `--navlyn-executable` is explicitly supplied.
2. `NavlynCliRunner` starts the configured process and maps stdout/stderr into the same MCP envelope.
3. This remains a compatibility, debugging, and development escape hatch, not the normal install path.

## Cache Boundary

The MCP server reuses its process, loaded assemblies, command runtime, and MSBuildLocator registration across the stdio session. Standalone tool calls still reload the workspace conservatively so CLI and MCP JSON contracts, diagnostics, path behavior, and file-change semantics remain aligned.

Use `navlyn_batch` when several batch-supported facts should share one workspace load. Navlyn 0.4.0 does not add a daemon, file watcher, editing surface, network access, or arbitrary command execution.

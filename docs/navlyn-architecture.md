# Navlyn Architecture

Navlyn 0.5.0 is split into shared implementation assemblies and two tool frontends.

## Projects

- `Navlyn.Core`: Roslyn/MSBuild workspace loading, path handling, diagnostics, candidate IDs, resolver models, and semantic resolver implementations.
- `Navlyn.CommandLine`: the reusable command-line runtime, `System.CommandLine` command definitions, stdout/stderr JSON behavior, output profiles, and batch dispatch.
- `navlyn`: the packaged CLI .NET tool. It is a thin executable that configures console encoding and invokes `Navlyn.CommandLine`.
- `navlyn.Mcp`: the packaged MCP .NET tool. It owns MCP tool/resource/prompt schemas and result envelopes, calls `Navlyn.CommandLine` in-process by default, and uses direct Core resolver paths for selected cheap file-first tools.

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
3. Cheap file-first tools such as `navlyn_file_outline`, `navlyn_inspect_file`, and `navlyn_symbol_source` use direct Core resolver paths with a lazy per-server workspace cache.
4. Other tools use `NavlynInProcessCommandAdapter`, which runs the shared command runtime in-process.
5. The MCP result envelope returns `sourceCommand` for traceability and the command JSON under `result`.

MCP legacy external CLI:

1. This path is used only when `--navlyn-executable` is explicitly supplied.
2. `NavlynCliRunner` starts the configured process and maps stdout/stderr into the same MCP envelope.
3. This remains a compatibility, debugging, and development escape hatch, not the normal install path.

## Cache Boundary

The MCP server reuses its process, loaded assemblies, command runtime, MSBuildLocator registration, and a lazy workspace cache for direct file-first tools. `navlyn_file_outline` seeds an in-memory candidate target map for the current server process, so immediate `navlyn_symbol_source(candidateId: "...")` follow-ups can avoid a broad candidate scan. Tools that still run through the command adapter preserve the existing CLI behavior and may load the workspace independently.

The direct cache is session-local and has no file watcher. Restart the MCP server after source or project changes when freshness matters. Use `navlyn_batch` when several batch-supported adapter-backed facts should share one workspace load. Navlyn does not add a daemon, on-disk index, editing surface, network access, or arbitrary command execution.

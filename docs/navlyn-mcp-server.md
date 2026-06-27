# Navlyn MCP Server

Navlyn includes a read-only stdio MCP server in the separate `navlyn.Mcp` project. It gives MCP clients a small, high-level tool surface for C#/.NET repository investigation while preserving the same deterministic CLI JSON contract underneath.

The server is intentionally facts-only. It does not edit files, execute arbitrary shell commands, call arbitrary Navlyn commands, access the network, or change the configured workspace. Successful tool calls return a Navlyn MCP result envelope with the CLI JSON under `result`; the inner result shapes remain documented in [`navlyn-cli-commands.md`](navlyn-cli-commands.md).

## When To Use It

Use `navlyn-mcp` when an agent client should ask semantic C# questions through MCP instead of composing CLI commands itself:

- start a repository investigation with project, package, target framework, and test relationship facts;
- resolve approximate symbol names into deterministic candidates and `candidateId` values;
- gather selected-symbol summaries, related files, static impact, and entrypoint chains;
- collect PR or working-tree review facts from a Git diff;
- request bounded reading material before review, modification, or explanation work;
- run several batch-supported facts in one MCP tool call.

Use `rg`, normal file reads, or editor tools for comments, prose docs, strings, generated artifacts, and non-C# content. Navlyn's MCP server is a semantic C#/.NET facts provider, not a general repository search server.

## Starting The Server

Installed .NET tool command shape:

```json
{
  "command": "navlyn-mcp",
  "args": ["--workspace", "path/to/YourRepo.slnx"]
}
```

Local development from this repository:

```powershell
dotnet build navlyn.slnx
dotnet run --no-launch-profile --project navlyn.Mcp -- --workspace navlyn.slnx --navlyn-executable dotnet --navlyn-arg navlyn/bin/Debug/net10.0/navlyn.dll
```

Equivalent MCP client configuration for local development:

```json
{
  "command": "dotnet",
  "args": [
    "navlyn.Mcp/bin/Debug/net10.0/navlyn.Mcp.dll",
    "--workspace",
    "navlyn.slnx",
    "--navlyn-executable",
    "dotnet",
    "--navlyn-arg",
    "navlyn/bin/Debug/net10.0/navlyn.dll"
  ]
}
```

Local package smoke testing uses the standard .NET tool flow:

```powershell
./scripts/test-package-install.ps1
```

## Server Options

- `--workspace <path>`: required `.slnx`, `.sln`, or `.csproj` path. Tool calls are locked to this workspace.
- `--navlyn-executable <command>`: Navlyn CLI executable. Defaults to `navlyn`.
- `--navlyn-arg <arg>`: prefix argument passed before the CLI command. Repeat for local development with `dotnet navlyn.dll`.
- `--working-directory <path>`: child process working directory. Defaults to the repository root when found.
- `--timeout-ms <number>`: per-tool timeout. Defaults to `120000`.
- `--max-json-chars <number>`: maximum CLI stdout JSON size accepted by the wrapper. Defaults to `4000000`.

The server writes MCP protocol messages to stdout. Logs and diagnostics go to stderr.

## Tool Selection

The MCP surface is deliberately small. Prefer the specific high-level tool for the investigation task, and use `navlyn_batch` when the desired command is batch-supported but not exposed as a dedicated MCP tool.

| Tool | Use It For | CLI Backing |
| --- | --- | --- |
| `navlyn_workspace_summary` | First scan: projects, target frameworks, references, packages, test relationships, MSBuild file facts | `repo-graph` |
| `navlyn_find_symbol` | Approximate symbol names, deterministic candidates, `candidateId` selection | `find` |
| `navlyn_about_symbol` | Selected-symbol definition, member outline, reference summary, shallow relations | `about` |
| `navlyn_related_files` | File-first investigation map around a selected symbol | `related` |
| `navlyn_impact` | Static source impact before editing or reviewing a symbol | `impact` |
| `navlyn_entrypoints` | Static caller chains or framework-aware entrypoint discovery | `entrypoints`, `framework-entrypoints` |
| `navlyn_review_diff` | Changed symbols, impact, diagnostics, related tests, and review facts for a Git diff | `review-diff` |
| `navlyn_context_pack` | Bounded reading material for `review`, `modify`, or `understand` workflows | `context-pack` |
| `navlyn_batch` | Multiple batch-supported CLI facts in one MCP tool call | `batch` |

`navlyn_workspace_summary`, `navlyn_review_diff`, and `navlyn_context_pack` accept optional `profile` values of `compact`, `evidence`, or `full` and forward them to the CLI. `navlyn_batch` accepts request-level `profile` fields for profiled workflow commands. Use `compact` for small first scans, `evidence` for review/CI facts, and `full` when compatibility with the rich CLI result is more important than output size.

## Common Flows

Start a repository investigation:

```text
navlyn_workspace_summary(profile: "compact")
navlyn_find_symbol(query: "PaymentService", assumeKind: "NamedType")
navlyn_about_symbol(candidateId: "sym:v1:...")
navlyn_related_files(candidateId: "sym:v1:...", limit: 30)
```

Before a non-trivial edit:

```text
navlyn_find_symbol(query: "PaymentService", assumeKind: "NamedType")
navlyn_impact(candidateId: "sym:v1:...", depth: 2)
navlyn_context_pack(candidateId: "sym:v1:...", goal: "modify", profile: "compact")
```

Review the current diff:

```text
navlyn_review_diff(profile: "evidence")
navlyn_context_pack(diff: true, goal: "review", profile: "compact")
```

Gather public API, test, framework, or DI facts through batch:

```json
{
  "requests": [
    { "id": "api", "command": "public-api-diff", "base": "main", "profile": "evidence" },
    { "id": "tests", "command": "tests-for-diff", "profile": "compact" },
    { "id": "di", "command": "di-graph", "profile": "compact" }
  ]
}
```

## Result Envelope

Success:

```json
{
  "ok": true,
  "tool": "navlyn_workspace_summary",
  "sourceCommand": {
    "command": "repo-graph",
    "arguments": ["repo-graph", "--workspace", "navlyn.slnx"]
  },
  "workspace": "navlyn.slnx",
  "result": {
    "command": "repo-graph"
  }
}
```

Failure:

```json
{
  "ok": false,
  "tool": "navlyn_find_symbol",
  "sourceCommand": null,
  "workspace": "navlyn.slnx",
  "error": {
    "code": "NAVLYN_MCP_INVALID_ARGUMENT",
    "message": "query is required."
  }
}
```

CLI failures preserve the first `NAVLYN####` diagnostic code found on stderr when available. Wrapper failures use `NAVLYN_MCP_*` codes.

For `navlyn_batch`, error layering is important:

- `NAVLYN_MCP_INVALID_ARGUMENT` means the MCP wrapper rejected the tool input before launching the CLI.
- A CLI fatal batch error, such as invalid top-level JSON, returns MCP `ok: false` with a CLI diagnostic such as `NAVLYN1008`.
- A per-request batch failure is a successful MCP tool call whose `result.results[]` item has `ok: false` and its own `error`.

## Boundaries

The MCP server is a thin stdio wrapper over the Navlyn CLI. It does not add resources, prompts, editing/refactoring tools, arbitrary command execution, warm workspace handles, or a daemon.

`navlyn_batch` exposes the existing CLI `batch` command only. Batch coverage includes `repo-graph`, `public-api-diff`, `tests-for-symbol`, `tests-for-diff`, `framework-entrypoints`, `di-graph`, `where-registered`, `di-impact`, and `review-pack`. Public API diff, test impact, DI, and review-pack facts are available through `navlyn_batch` rather than dedicated MCP tools.

Static impact, framework entrypoint, DI, and review-pack results are bounded source-level facts. They are useful evidence for agents and reviewers, but they are not complete runtime proofs, security scans, or replacement review comments.

## Performance Notes

The MCP server runs the Navlyn CLI as a subprocess per tool call. On large solutions, repeated workspace load cost can dominate tool-call latency. Prefer `navlyn_batch` when an agent needs several batch-supported facts from the same workspace, and use `profile: "compact"` or `profile: "evidence"` when output size is the limiting factor.

Use `./scripts/measure-navlyn-performance.ps1` from the repository root to compare CLI direct calls and CLI batch for local performance investigation. Functional MCP behavior is covered by the MCP tests in the solution.

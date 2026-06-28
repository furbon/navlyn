# Navlyn CLI Commands

This document describes the public CLI contract for the repository version you are reading. Keep examples compact and update this file when public command behavior changes.

## Command Families

Navlyn commands are grouped by investigation style:

| Family | Commands | Purpose |
| --- | --- | --- |
| Workspace facts | `check`, `overview`, `repo-graph`, `diagnostics`, `symbol-diagnostics`, `diagnostic-pack` | Load workspaces and report deterministic repository, project, package, and compiler facts. |
| Fuzzy investigations | `find`, `resolve-target`, `where-used`, `about`, `related`, `impact`, `entrypoints` | Resolve approximate symbol intent into candidates, selected target envelopes, selected-symbol summaries, related files, impact, and entrypoint chains. |
| Diff and review | `changed-symbols`, `impact-diff`, `diagnostics-diff`, `review-diff`, `review-pack` | Produce evidence-first facts for Git diffs and review workflows. |
| Context and batching | `context-pack`, `batch` | Build bounded agent reading material and run multiple machine-readable requests through one workspace load. |
| Public API and tests | `public-api-diff`, `tests-for-symbol`, `tests-for-diff` | Compare source-level public/protected API surface and discover related test candidates. |
| Framework and DI | `framework-entrypoints`, `di-graph`, `where-registered`, `di-impact` | Report framework-aware entrypoints and source-level Microsoft.Extensions.DependencyInjection facts. |
| .NET application domains | `route-map`, `route-impact`, `options-graph`, `config-impact`, `where-handled`, `message-flow`, `ef-model`, `entity-impact`, `package-usage`, `package-impact` | Report source-level ASP.NET Core route/auth, options/configuration, MediatR, EF Core, and package usage facts. |
| Source navigation primitives | `symbols`, `symbols-in`, `outline`, `symbol-at`, `symbol-info`, `scope-at`, `symbol-source`, `signature`, `definition`, `references`, `implementations`, `type-hierarchy`, `callers`, `calls` | Return exact Roslyn-backed source-position and symbol navigation facts. |

## General Contract

- stdout is reserved for command result JSON.
- stderr is reserved for diagnostics and errors.
- Running `navlyn` without a command writes `NAVLYN1001` and root help to stderr and returns exit code `2`.
- Running `navlyn --help` writes root help to stdout and returns exit code `0`.
- Automation-facing output uses deterministic JSON.
- User-facing line and column values are 1-based.
- Paths are repository-relative when possible and use `/` separators in JSON output.
- Usage errors return exit code `2`.
- Runtime or workspace load failures return exit code `1`.
- Source-position commands resolve one symbol at the requested file, line, and column. Commands built on the source-position input model also accept `--candidate-id` where documented; use exactly one input mode: `--candidate-id` or `--file --line --column`. Use exploratory commands such as `symbols-in`, not `definition` or `references`, for line/range-wide symbol discovery.
- Property and event accessor method positions are normalized to their associated source property or event for navigation output.
- Source-backed result locations keep the existing `path`, `line`, and `column` fields and include additive `endLine` and `endColumn` fields where Roslyn exposes a valid source span. `line` / `column` are 1-based inclusive start positions. `endLine` / `endColumn` are 1-based exclusive end positions.
- Source-position command top-level `file`, `line`, and `column` fields report the requested input point, or the effective resolved point when `--candidate-id` is used. Result locations and symbol-shaped source locations carry spans.
- Source-position command results include additive `selectionInput` when `--candidate-id` is used.
- Symbol-shaped results keep the existing `name`, `kind`, `container`, `path`, `line`, and `column` fields where applicable, may include additive `endLine` / `endColumn`, and may include an additive `facts` object for richer agent decisions.
- Source-navigation commands keep source locations as the default result surface. Commands that support metadata reporting require explicit `--include-metadata` / `includeMetadata`.
- Metadata-only symbols have no source span and use null source location fields such as `path`, `line`, `column`, `endLine`, and `endColumn` where those fields are present.
- Project-shaped results may include additive workspace context fields such as `targetFramework`, `languageVersion`, and `preprocessorSymbols`.

Run examples from the repository root. Use `--no-launch-profile` when running through `dotnet run` so Visual Studio launch settings do not affect stdout.

## JSON Compatibility Policy

Navlyn output is intended for agents, CI, and scripts. Consumers should parse named fields and ignore unknown fields.

Compatibility guarantees:

- Successful commands write one JSON document to stdout.
- Diagnostics, progress, and errors are written to stderr.
- Usage errors exit `2`; runtime and workspace failures exit `1`.
- Existing documented top-level fields keep their meaning within the same major workflow schema.
- New fields may be added at any object level.
- New enum-like string values may be added when Roslyn or Navlyn can report more precise facts.
- Arrays are ordered deterministically by command-specific stable keys such as path, line, column, name, kind, project, and configured ranking.
- Paths in JSON are repository-relative where possible and use `/` separators.

Not guaranteed:

- Opaque identifiers such as `candidateId` stay stable across source edits, generated-code changes, project filter changes, or Navlyn versions.
- `full` output remains small or token-budget friendly.
- Static impact, framework, DI, application-domain, diagnostics, and review facts are complete runtime proofs.
- The exact set of additive symbol `facts` fields is fixed.

Breaking changes require documentation updates and contract-test updates. Renaming or removing documented fields, changing exit behavior, moving diagnostics to stdout, changing path normalization, or changing profile semantics is breaking. Deprecation should be additive first: keep the old field, add a replacement or warning when practical, document the transition, and remove only in an intentional breaking release.

Profiled workflow output uses `schemaVersion: "navlyn.workflow.v1"`. That schema version covers the shared workflow envelope (`schemaVersion`, `navlynVersion`, `workspace`, `kind`, `command`, `profile`, `configuration`, `reproCommand`, summaries, limits, warnings, and next actions), not every command's rich inner domain facts. A future `navlyn.workflow.v2` would be used for incompatible envelope changes.

## Output Profiles

High-level workflow commands support `--profile compact|evidence|full`. The default is `full`.

- `full` preserves the rich command result and adds workflow metadata such as `schemaVersion`, `navlynVersion`, `profile`, `configuration`, and `reproCommand`.
- `evidence` keeps summary and evidence-first sections for review, CI, and MCP callers while trimming snippet text and deep arrays.
- `compact` keeps metadata, summary counts, warnings, next actions, and small highlights for output-budgeted agent scans.

Profile compatibility:

- `full` is the best choice for tools that need command-specific detail and can tolerate additive fields.
- `evidence` and `compact` are stable workflow profiles, but intentionally omit or trim deep details. Consumers should rely on their summaries, warnings, limits, truncation flags, and highlights instead of expecting every field from `full`.
- A command may add new compact/evidence highlights, warning codes, or next actions without a schema-version bump.
- If a profile truncates data, it reports truncation through `truncated`, section-level `truncated`, profile limits, command limits, warnings, or a combination documented by the command.

Profiled workflow output uses `schemaVersion: "navlyn.workflow.v1"`. The supported direct commands are `repo-graph`, `changed-symbols`, `impact-diff`, `diagnostics-diff`, `review-diff`, `review-pack`, `context-pack`, `public-api-diff`, `tests-for-symbol`, `tests-for-diff`, `framework-entrypoints`, `di-graph`, `where-registered`, `di-impact`, `route-map`, `route-impact`, `options-graph`, `config-impact`, `where-handled`, `message-flow`, `ef-model`, `entity-impact`, `package-usage`, and `package-impact`.

```powershell
dotnet run --no-launch-profile --project navlyn -- review-diff --workspace navlyn.slnx --profile evidence
dotnet run --no-launch-profile --project navlyn -- review-pack --workspace navlyn.slnx --profile evidence
dotnet run --no-launch-profile --project navlyn -- context-pack --workspace navlyn.slnx --diff --profile compact
dotnet run --no-launch-profile --project navlyn -- repo-graph --workspace navlyn.slnx --profile compact
```

## MCP Server

The separate `navlyn.Mcp` project exposes a read-only stdio MCP server for agent clients. MCP tool results wrap existing CLI JSON in `{ ok, tool, sourceCommand, workspace, result, error }`; the inner `result` shapes remain the CLI contract documented here.

See [`navlyn-mcp-server.md`](navlyn-mcp-server.md) for setup, tool names, result envelope, and boundaries.

## Rich Symbol Facts

Symbol `facts` are additive and intended for agents that need to choose between overloads, generic members, source symbols, and metadata symbols without immediately opening source files. Fields vary by symbol kind and may include:

- `displayName`, `fullyQualifiedName`, `signature`, `documentationCommentId`.
- `namespace`, `containingType`, `project`, `assembly`, `accessibility`.
- `isSource`, `isMetadata`, `isStatic`, `isAbstract`, `isVirtual`, `isOverride`, `isAsync`, `isExtensionMethod`, `isConstructor`, `isOperator`, `isIndexer`.
- `arity`, `typeParameters`, `typeArguments`, `constructedFrom`.
- `parameters`, `returnType`, `propertyType`, `eventType`, `fieldType`.
- `attributes`, with attribute type and constructor facts when Roslyn exposes them.

Nullability facts use declared type annotations such as `Annotated` and `NotAnnotated`. Nullable flow-state is not reported by this contract.

## C# Semantic Normalization

Navlyn reports source-backed synthesized-adjacent members when Roslyn exposes a useful source location, such as record positional properties and record `Deconstruct` methods. Symbols without source definitions still follow each command's source-only behavior by default; for example, `definition` reports `NAVLYN1305` when no source definition exists unless `--include-metadata` is used.

Accessor methods are normalized to the associated property or event for `symbol-at`, `definition`, `references`, `callers`, `calls`, `type-hierarchy`, and related source-position output. This keeps `get`, `set`, `add`, and `remove` keyword positions aligned with the user-facing source symbol.

## Workspace Context

Navlyn uses the Roslyn project/document context loaded by MSBuild. Conditional compilation follows the selected project context: active `#if` branches are visible to semantic commands, while inactive branches are disabled text and do not produce C# symbols. `symbols` omits inactive declarations, `symbols-in` returns an empty array for inactive text, and source-position commands such as `symbol-at` report `NAVLYN1304` when the selected inactive text has no symbol.

Multi-targeted projects are loaded as separate Roslyn projects when MSBuild exposes them that way. Their names commonly include the target framework, such as `MyProject(net10.0)`, and `overview` reports `targetFramework` when Navlyn can determine it. Use the exact project name to select a target-specific context. Filtering by a `.csproj` path can be ambiguous when the same project file expands to multiple target frameworks, and returns `NAVLYN1007`.

Linked files are matched and emitted by physical repository-relative path. When one physical file is linked into multiple projects, use `--project` to choose the intended semantic context. Without `--project`, Navlyn chooses a deterministic first matching document; returned symbol facts identify the project that produced a symbol when one is available.

## Generated Code

Commands include generated code by default. Commands that inspect source files, source locations, compiler diagnostics, or symbol declarations support `--exclude-generated`.

Generated-code detection treats these paths as generated:

- Files under `obj` or `bin` directories.
- File names starting with `TemporaryGeneratedFile_`.
- File names ending with `.g.cs`, `.generated.cs`, `.designer.cs`, `.AssemblyInfo.cs`, or `.AssemblyAttributes.cs`.

## Fuzzy Discovery Commands

Fuzzy commands are official agent-oriented shortcuts over the precise semantic primitives. They accept compact symbol-like queries, return deterministic candidates with reason codes, and avoid merging semantic results from different plausible symbols unless the output is explicitly candidate-grouped.

Common options:

- `--workspace <path>`: `.slnx`, `.sln`, or `.csproj`.
- `--query <text>`: required fuzzy symbol query.
- `--assume-kind <kind>`: optional repeated Roslyn symbol kind used for ranking.
- `--match smart|exact|contains|regex`: defaults to `smart`.
- `--case-sensitive`: makes name matching case-sensitive where applicable.
- `--candidate-id <id>`: selects a candidate returned by a previous fuzzy command. Supported by fuzzy workflows such as `resolve-target`, `where-used`, `about`, `related`, `impact`, `entrypoints`, and `context-pack`; exact source-navigation commands such as `symbol-at`, `symbol-info`, `definition`, `references`, `implementations`, `type-hierarchy`, `callers`, and `calls`; test/DI/application-domain commands such as `tests-for-symbol`, `where-registered`, `di-impact`, `where-handled`, `message-flow`, and `entity-impact`; not supported by `find`.
- `--candidate-policy fail|select|group`: controls ambiguous query handling. Defaults preserve existing behavior: `group` for `find` and `where-used`, `fail` for other selected-candidate workflows.
- `--min-confidence high|medium|low`: minimum confidence required before returning selected-candidate facts. Defaults to `low` for `find` and `medium` for selected-candidate workflows.
- `--explain-selection`: adds structured selection explanation.
- `--project <project>`: optional repeated input project context filter.
- `--exclude-generated`: excludes generated declarations and source locations.
- `--limit <number>`: command-specific result limit. For `find`, this limits returned candidates. For `where-used`, `related`, `impact`, and `entrypoints`, it limits returned references, files, or chains.

Common output fields:

- `confidence`: one of `high`, `medium`, `low`, `ambiguous`, or `none`.
- `totalCandidates`: candidate count before the candidate display limit.
- `candidateCount`: returned candidate count.
- `candidates`: returned candidates with `name`, `kind`, `container`, `facts`, source location, `reasonCodes`, `candidateId`, and `selector`.
- `selectedCandidate`: emitted only when selection rules choose one candidate.
- `alternatives`: emitted when one candidate is selected but meaningful alternatives remain.
- `warnings`: machine-readable warning strings.
- `nextActions`: machine-oriented follow-up command hints.
- `candidateLimit` and `candidatesTruncated`: candidate display limit and whether additional candidates were omitted.
- `selectionInput`: `query` or `candidateId`.
- `selectionExplanation`: emitted only with `--explain-selection`.

Fuzzy candidates and fuzzy source locations use the same source span convention as direct commands. `nextActions` remain command-invocation hints and keep point-style `file`, `line`, and `column` inputs. When a selected candidate has a `candidateId`, `nextActions` may also include additive `candidateId`, `mcpTool`, and `arguments` fields so MCP callers can invoke the recommended tool directly without re-running fuzzy selection.

Fuzzy selection rules prefer exact case-sensitive matches, then exact case-insensitive matches, then contains and normalized-name matches. Multiple exact matches are reported as `ambiguous`. A single exact match with weaker alternatives is selected with `medium` confidence. Heuristic-only matches are selected only when ranking produces one dominant candidate.

Candidate IDs are opaque deterministic handles for current source declarations. They have the form `sym:v1:<sha256-prefix>` and are paired with a human-readable `selector` containing kind, name, fully qualified name, documentation comment id, project, target framework, path, and source span. Source edits that move or change a declaration can change its candidate id. Partial declarations are declaration-specific, and multi-targeted projects can produce different ids per target framework.

When a source-position command is invoked with `--candidate-id`, Navlyn re-resolves that declaration in the current workspace and then runs the same source-position resolver at the candidate declaration point. Malformed candidate ids produce `NAVLYN1701`; stale or filtered-out ids produce `NAVLYN1702`; duplicate ids in the current workspace produce `NAVLYN1703`. These are usage errors with no stdout output.

When `--candidate-id` is used, `--query`, `--assume-kind`, `--match`, and `--case-sensitive` cannot be combined with it. Invalid or missing candidate ids return a usage error with no stdout.

### `find`

Finds source symbols that plausibly match a compact query.

```powershell
dotnet run --no-launch-profile --project navlyn -- find --workspace navlyn.slnx --query CheckCommand --assume-kind NamedType
```

`find` returns the common fuzzy envelope and candidate list. It does not return references or relation summaries. Use its `candidateId` values to re-query selected-candidate workflows without repeating fuzzy selection.

### `resolve-target`

Resolves the first target an agent should anchor on from a fuzzy query, a previous `candidateId`, or an exact source position. Use it as a small standard entrypoint when the caller wants one selected target plus next actions. Use `find` instead when the caller wants a full candidate list for manual choice.

```powershell
dotnet run --no-launch-profile --project navlyn -- resolve-target --workspace navlyn.slnx --query CheckCommand --assume-kind NamedType
dotnet run --no-launch-profile --project navlyn -- resolve-target --workspace navlyn.slnx --candidate-id sym:v1:...
dotnet run --no-launch-profile --project navlyn -- resolve-target --workspace navlyn.slnx --file navlyn/Cli/Commands/CheckCommand.cs --line 6 --column 23
```

Input modes:

- `--query <text>` with fuzzy options such as `--assume-kind`, `--match`, `--candidate-policy`, and `--min-confidence`.
- `--candidate-id <id>` returned by a previous fuzzy command.
- `--file <path> --line <number> --column <number>` for exact source-position anchoring.

Result shape:

```json
{
  "workspace": "navlyn.slnx",
  "kind": "solution",
  "command": "resolve-target",
  "selectionInput": {
    "mode": "query",
    "query": "CheckCommand"
  },
  "selectedTarget": {
    "name": "CheckCommand",
    "kind": "NamedType",
    "container": "Navlyn.Cli.Commands",
    "path": "navlyn/Cli/Commands/CheckCommand.cs",
    "line": 6,
    "column": 23
  },
  "candidateId": "sym:v1:b80453171915c2303ccb9b591fdcebb6",
  "confidence": "high",
  "candidateCount": 1,
  "totalCandidates": 1,
  "recommendedNextActions": [
    {
      "command": "definition",
      "candidateId": "sym:v1:b80453171915c2303ccb9b591fdcebb6"
    }
  ],
  "warnings": []
}
```

When query mode cannot safely select one target, `selectedTarget` and `candidateId` are omitted, `ambiguityReason` is populated, and limited `candidates` are returned. Malformed or stale `candidateId` values use the same candidate-id diagnostics as other fuzzy commands. Source-position mode does not currently mint a `candidateId`; follow its file/line/column next actions or run query mode when a candidate id is needed.

Future work candidates such as fully qualified name input, XML documentation comment id input, and metadata-name input are intentionally not part of the current contract.

### `where-used`

Resolves a fuzzy query and lists source references for the selected candidate. References include semantic `containingSymbol` context by default.

```powershell
dotnet run --no-launch-profile --project navlyn -- where-used --workspace navlyn.slnx --query CheckCommand --assume-kind NamedType --limit 100
```

Additional options:

- `--usage-kind <kind>`: filters references by usage kind. Can be specified more than once or as comma-separated values. Supported values are `read`, `write`, `invoke`, `construct`, `inherit`, `implement`, `override`, `attribute`, `nameof`, and `typeof`.
- `--group-by <group>`: adds grouped reference summaries. Can be specified more than once or as comma-separated values. Supported values are `file`, `project`, `containing-symbol`, `usage-kind`, and `test-vs-production`.
- `--include-snippets`: adds bounded source snippets to returned locations and file summaries.
- `--snippet-lines <number>`: context lines before and after the matched line. Defaults to `1`.

Selected-candidate output adds:

- `totalMatches`: reference count before `--limit`.
- `limit` and `truncated`: applied reference limit and whether references were omitted.
- `references`: limited source reference locations with additive `usageKind`.
- `files`: file summaries with `referenceCount`, `firstLine`, reason codes, and additive `usageKindCounts`.
- `usageKindCounts`: selected-candidate usage kind counts after filters and before `--limit`.
- `groups`: emitted when `--group-by` is provided.

Ambiguous output omits `selectedCandidate` and may include `candidateResults`, with per-candidate file summaries instead of merged references.

### `about`

Resolves a fuzzy query and returns a compact semantic summary of the selected symbol.

```powershell
dotnet run --no-launch-profile --project navlyn -- about --workspace navlyn.slnx --query CheckCommand --assume-kind NamedType
```

Additional options:

- `--member-limit <number>`: defaults to `50`.
- `--reference-limit <number>`: defaults to `100`.
- `--relation-limit <number>`: defaults to `25`.
- `--include-snippets`.
- `--snippet-lines <number>`.

Selected-candidate output may include:

- `definition`: selected declaration source location.
- `members`: type member outline summary when the selected candidate is a type, with `totalMembers`, `limit`, and `truncated`.
- `references`: reference count, `limit`, `truncated`, limited locations, and top files.
- `relations`: shallow callers, calls, implementations, and hierarchy summaries where applicable.

### `related`

Resolves a fuzzy query and returns files likely related to the selected symbol for investigation.

```powershell
dotnet run --no-launch-profile --project navlyn -- related --workspace navlyn.slnx --query CheckCommand --include references,calls,callers,hierarchy --limit 50
```

Additional options:

- `--include <modes>`: comma-separated `declarations`, `references`, `callers`, `calls`, `implementations`, and `hierarchy`. Defaults to all modes.
- `--include-snippets`.
- `--snippet-lines <number>`.

Output adds `totalFiles`, `limit`, `truncated`, and `files`, ordered by reason priority and deterministic path tie-breakers. File reasons include values such as `declares-selected-symbol`, `references-selected-symbol`, `caller-of-selected-member`, `callee-of-selected-member`, `implements-selected-symbol`, and `hierarchy-related-symbol`.

### `impact`

Resolves a fuzzy query and estimates static source areas likely affected by changing the selected symbol. This is static source navigation, not a complete runtime behavior graph.

```powershell
dotnet run --no-launch-profile --project navlyn -- impact --workspace navlyn.slnx --query CheckCommand --depth 2
```

Additional options:

- `--include <modes>`: comma-separated `references`, `callers`, `calls`, `implementations`, and `hierarchy`.
- `--depth <number>`: bounded traversal depth. Defaults to `3`.
- `--include-snippets`.
- `--snippet-lines <number>`.

Output is file-first like `related`, but file entries may include `impactLevel` values such as `direct` or `indirect`.

### `entrypoints`

Resolves a fuzzy query and traces bounded static caller chains upstream from selected members.

```powershell
dotnet run --no-launch-profile --project navlyn -- entrypoints --workspace navlyn.slnx --query CheckCommand --depth 3
```

Additional options:

- `--depth <number>`: maximum caller-chain depth. Defaults to `3`.
- `--include-snippets`.
- `--snippet-lines <number>`.
- `--framework-aware`: add framework-aware entrypoint facts and annotate chains whose terminal symbol is a framework entrypoint.
- `--framework <framework>`: when `--framework-aware` is set, restrict detection to `aspnetcore`, `test`, `worker`, `azure-functions`, `grpc`, `mediatr`, or `messaging`. Defaults to `aspnetcore,test,worker`.

Output adds `totalChains`, `limit`, `truncated`, and `chains`. Each chain contains ordered `symbols` and an `endReason`, such as `no-upstream-callers`, `depth-limit`, or `cycle-detected`. With `--framework-aware`, output additively includes `frameworkAware`, `frameworks`, `frameworkEntrypoints`, and per-chain `entrypoint` when the terminal symbol matches a framework-aware entrypoint. `framework-entrypoint` is an additional `endReason`.

### `framework-entrypoints`

Discovers framework-aware .NET entrypoint declarations in the workspace.

```powershell
dotnet run --no-launch-profile --project navlyn -- framework-entrypoints --workspace navlyn.slnx
dotnet run --no-launch-profile --project navlyn -- framework-entrypoints --workspace navlyn.slnx --framework aspnetcore --project WebApp
```

Additional options:

- `--project <project>`: optional repeated project context filter.
- `--framework <framework>`: repeated or comma-separated values from `aspnetcore`, `test`, `worker`, `azure-functions`, `grpc`, `mediatr`, and `messaging`. Defaults to `aspnetcore,test,worker`.
- `--entrypoint-kind <kind>`: optional repeated entrypoint kind filter.
- `--exclude-generated`.
- `--limit <number>`: defaults to `100`.
- `--evidence-limit <number>`: defaults to `5`.
- `--include-snippets`.
- `--snippet-lines <number>`: defaults to `1`.

Output includes `frameworks`, optional project filters, `limits`, and an `entrypoints` section with `totalEntrypoints`, `limit`, `truncated`, and `items`. Each item includes `entrypointKind`, `framework`, symbol facts, project facts, source span, `confidence`, `reasonCodes`, and evidence.

Framework detection is source-level and evidence-backed. ASP.NET Core controller actions, Minimal API handlers, middleware registrations, xUnit/NUnit/MSTest test methods, `BackgroundService.ExecuteAsync`, `IHostedService.StartAsync`, and `AddHostedService<T>()` are supported. Azure Functions, gRPC, MediatR, messaging handlers, runtime route tables, middleware ordering, and full runtime reachability are not claimed.

## Diff Workflow Commands

Diff workflow commands are review-oriented entrypoints for agents. They read Git diffs, resolve current source symbols with Roslyn, and return deterministic facts. They do not generate review comments or claim complete runtime impact.

Common options:

- `--workspace <path>`: `.slnx`, `.sln`, or `.csproj`.
- `--base <ref>`: compare from this Git ref. With `--head`, compares base to head; without `--head`, compares base to the working tree.
- `--head <ref>`: compare to this Git ref. Requires `--base`.
- `--staged`: use staged changes.
- `--include-unstaged`: use unstaged working tree changes. This is the default when `--base`, `--head`, and `--staged` are omitted.
- `--project <project>`: optional repeated project context filter.
- `--exclude-generated`: excludes generated source files and source locations.

Diff input modes:

- `--base <ref> --head <ref>` runs a base/head Git diff.
- `--base <ref>` compares the working tree to the base ref.
- `--staged` uses staged changes.
- With none of those options, Navlyn uses the working tree and includes untracked files that are not ignored by Git.

Diff workflow output uses a common envelope with `workspace`, `kind`, `command`, `diff`, `limits`, `truncated`, `warnings`, and `nextActions`. The `diff` object reports `mode`, `base`, `head`, `staged`, `includeUnstaged`, `totalFiles`, and ordered `files`. Each file reports `path`, optional `oldPath`, `status`, and `hunks`.

Diff workflows invoke the `git` executable through a small provider boundary. This keeps Navlyn dependency-light while preserving stable diff behavior across CLI and automation use.

Invalid diff option combinations produce `NAVLYN1503` on stderr, exit code `2`, and no stdout output. Git repository discovery failures produce `NAVLYN1501`. Git command failures produce `NAVLYN1502`.

### `changed-symbols`

Extracts current source symbols touched by a diff.

```powershell
dotnet run --no-launch-profile --project navlyn -- changed-symbols --workspace navlyn.slnx
dotnet run --no-launch-profile --project navlyn -- changed-symbols --workspace navlyn.slnx --staged --symbol-limit 100
```

Additional options:

- `--symbol-limit <number>`: defaults to `100`.

Output adds:

- `changedSymbols`: `totalSymbols`, `limit`, `truncated`, and `symbols`.
- `unresolvedChanges`: diff hunks that could not be mapped to a current source symbol, such as deleted-only code.

Each changed symbol reports symbol facts, source span, `changeKinds`, bounded `changedLines`, `totalChangedLines`, `changedLineLimit`, `changedLinesTruncated`, and `reasonCodes`. Deleted-only symbols are not reconstructed by this command.

### `impact-diff`

Returns bounded static impact facts for changed symbols.

```powershell
dotnet run --no-launch-profile --project navlyn -- impact-diff --workspace navlyn.slnx --impact-limit 100 --depth 2
```

Additional options:

- `--symbol-limit <number>`: defaults to `50`.
- `--impact-limit <number>`: defaults to `100`.
- `--depth <number>`: defaults to `2`.
- `--include <modes>`: comma-separated `references`, `callers`, `calls`, `implementations`, and `entrypoints`. Defaults to `references,callers,calls,implementations`.
- `--include-snippets`.
- `--snippet-lines <number>`: defaults to `1`.

Output adds `changedSymbols`, `unresolvedChanges`, and `impact`. Impact items include the changed symbol, limited `references`, `callers`, `calls`, `implementations`, `entrypointChains`, `affectedFiles`, and conservative `riskReasons`. This is static source impact, not complete runtime impact.

### `diagnostics-diff`

Returns current compiler diagnostics scoped to changed and affected files.

```powershell
dotnet run --no-launch-profile --project navlyn -- diagnostics-diff --workspace navlyn.slnx --diagnostic-limit 100
```

Additional options:

- `--symbol-limit <number>`: defaults to `50`.
- `--impact-limit <number>`: defaults to `100`.
- `--diagnostic-limit <number>`: defaults to `100`.
- `--severity <severity>`: filters by `Hidden`, `Info`, `Warning`, or `Error`. Can be specified more than once.
- `--id <diagnostic-id>`: filters by exact compiler diagnostic id. Can be specified more than once.

Output adds `changedSymbols`, `unresolvedChanges`, `diagnosticsScope`, and `diagnostics`. Diagnostics are current scoped diagnostics only; before/after diagnostic deltas are not reported by this command.

### `review-diff`

Creates a one-shot review facts pack for AI review workflows.

```powershell
dotnet run --no-launch-profile --project navlyn -- review-diff --workspace navlyn.slnx
dotnet run --no-launch-profile --project navlyn -- review-diff --workspace navlyn.slnx --staged --include-snippets
```

Additional options:

- `--symbol-limit <number>`: defaults to `50`.
- `--impact-limit <number>`: defaults to `100`.
- `--diagnostic-limit <number>`: defaults to `100`.
- `--related-test-limit <number>`: defaults to `50`.
- `--depth <number>`: defaults to `2`.
- `--include-snippets`.
- `--snippet-lines <number>`: defaults to `1`.

Output adds `changedSymbols`, `unresolvedChanges`, `publicContractChanges`, `impact`, `relatedTests`, `diagnosticsScope`, `diagnostics`, and `findings`.

`findings` are evidence-first facts, not review comments. `publicContractChanges` is a current-workspace heuristic for public or protected changed symbols; use `public-api-diff` for before/after public API comparison. `relatedTests` is heuristic and based on test-named paths and source references where available.

### `review-pack`

Runs deterministic review packs that return evidence-backed signals for AI review workflows. This is not a complete analyzer or security scanner, and it does not generate review comments.

```powershell
dotnet run --no-launch-profile --project navlyn -- review-pack --workspace navlyn.slnx
dotnet run --no-launch-profile --project navlyn -- review-pack --workspace navlyn.slnx --scope workspace --pack async --pack security --profile evidence
dotnet run --no-launch-profile --project navlyn -- review-pack --workspace navlyn.slnx --pack architecture --architecture-config .navlyn.yml
```

Additional options:

- `--pack <pack>`: `async`, `disposal`, `nullability`, `security`, `architecture`, or `all`. Can be repeated or comma-separated. Defaults to `all`.
- `--scope diff|workspace`: defaults to `diff`.
- Diff options in diff scope: `--base`, `--head`, `--staged`, `--include-unstaged`.
- `--project <project>`.
- `--exclude-generated`.
- `--finding-limit <number>`: defaults to `100`.
- `--evidence-limit <number>`: defaults to `5`.
- `--symbol-limit <number>`: defaults to `100`.
- `--file-limit <number>`: defaults to `100`.
- `--include-snippets`.
- `--snippet-lines <number>`: defaults to `1`.
- `--architecture-config <path>`: optional `.navlyn.yml` architecture rule config. When omitted, `review-pack` searches for `.navlyn.yml` from the current working directory, current repository root, workspace directory, and workspace repository root.

Output adds `scope`, `packs`, `summary`, `findings`, `packResults`, `limits`, `truncated`, `warnings`, and `nextActions`. Each finding has `pack`, `ruleId`, `severity`, `confidence`, `claim`, `evidence`, `sourceLocations`, `symbolIds`, `reasonCodes`, and item `nextActions`.

Implemented pack signals include:

- `async`: sync-over-async, async void, fire-and-forget task-like calls, and missing CancellationToken forwarding signals.
- `disposal`: locally created disposable values without obvious disposal or transfer, and sync disposal syntax on async disposable values.
- `nullability`: null-forgiving suppressions, required member signals, and public API declarations in nullable-disabled projects.
- `security`: endpoint auth-surface signals, SQL-like constructed strings, file/process/reflection API usage, deserialization calls, and sensitive-looking logging arguments.
- `architecture`: project and namespace dependency rules from a limited `.navlyn.yml` schema.

Architecture config example:

```yaml
version: 1
rules:
  - id: no-cli-from-core
    kind: namespace-dependency
    from: "Navlyn.Symbols"
    disallow:
      - "Navlyn.Cli"
```

Invalid pack values, invalid limits, invalid diff option combinations, and explicit invalid architecture config paths return exit code `2`, write diagnostics to stderr, and produce no stdout.

## Public API Diff Command

`public-api-diff` compares source-level public / protected API surface between a base Git ref and either the current working tree or a head Git ref. It is not a NuGet package diff, reference assembly diff, or IL/binary scanner.

```powershell
dotnet run --no-launch-profile --project navlyn -- public-api-diff --workspace navlyn.slnx --base main
dotnet run --no-launch-profile --project navlyn -- public-api-diff --workspace navlyn.slnx --base main --head HEAD --project navlyn
```

Required options:

- `--workspace <path>`: `.slnx`, `.sln`, or `.csproj`.
- `--base <ref>`: Git ref to compare from.

Optional options:

- `--head <ref>`: Git ref to compare to. When omitted, compares the base ref to the current working tree / loaded workspace.
- `--project <project>`: filters by exact project name or repository-relative `.csproj` path. Can be specified more than once.
- `--exclude-generated`: excludes generated source paths.
- `--include-additions`: includes additions. Defaults to true.
- `--include-attributes`: includes attribute changes. Defaults to true.
- `--symbol-limit <number>`: maximum public API symbols captured per side. Defaults to `5000`.
- `--change-limit <number>`: maximum changes returned. Defaults to `200`.

Result shape:

```json
{
  "workspace": "navlyn.slnx",
  "kind": "solution",
  "command": "public-api-diff",
  "comparison": {
    "base": "main",
    "head": "workingTree",
    "mode": "gitSourceSnapshot",
    "workspacePath": "navlyn.slnx"
  },
  "projects": null,
  "limits": {
    "symbolLimit": 5000,
    "changeLimit": 200
  },
  "summary": {
    "totalChanges": 1,
    "breakingSourceChanges": 1,
    "breakingBinaryChanges": 1,
    "additions": 0,
    "removals": 1,
    "signatureChanges": 0
  },
  "changes": {
    "totalChanges": 1,
    "limit": 200,
    "truncated": false,
    "items": [
      {
        "code": "public-member-removed",
        "kind": "removal",
        "sourceCompatibility": {
          "risk": "breaking",
          "confidence": "high",
          "reasonCodes": ["public-member-removed"]
        },
        "binaryCompatibility": {
          "risk": "breaking",
          "confidence": "high",
          "reasonCodes": ["public-member-removed"]
        },
        "symbol": {},
        "before": {},
        "evidence": [],
        "reasonCodes": ["public-member-removed"]
      }
    ]
  },
  "truncated": false,
  "warnings": [
    "Public API diff uses source-level snapshots; it is not a NuGet package or IL diff."
  ],
  "nextActions": []
}
```

Change codes include `public-type-added`, `public-type-removed`, `public-member-added`, `public-member-removed`, `public-signature-changed`, `generic-constraints-changed`, `nullable-annotation-changed`, `default-parameter-changed`, `interface-member-added`, `abstract-virtual-sealed-changed`, `enum-member-added`, `enum-member-removed`, `enum-member-value-changed`, `attribute-added`, `attribute-removed`, and `attribute-argument-changed`.

Risk values are `breaking`, `risk`, or `compatible` and are reported separately for source and binary compatibility. Navlyn does not emit a SemVer conclusion.

## Test Impact Commands

`tests-for-symbol` and `tests-for-diff` return deterministic related test candidates. They do not execute tests, read coverage files, or analyze runtime test results.

### `tests-for-symbol`

Finds tests related to a selected symbol.

```powershell
dotnet run --no-launch-profile --project navlyn -- tests-for-symbol --workspace navlyn.slnx --query RepoGraphResolver --assume-kind NamedType
dotnet run --no-launch-profile --project navlyn -- tests-for-symbol --workspace navlyn.slnx --candidate-id sym:v1:...
dotnet run --no-launch-profile --project navlyn -- tests-for-symbol --workspace navlyn.slnx --file navlyn/RepoGraph/RepoGraphResolver.cs --line 7 --column 23
```

Input modes:

- `--query <text>`: fuzzy symbol query.
- `--candidate-id <id>`: selects a candidate returned by a previous fuzzy command.
- `--file <path> --line <number> --column <number>`: source-position mode.

Exactly one input mode is required.

Additional options:

- `--project <project>`: production project filter.
- `--test-project <project>`: test project filter.
- `--exclude-generated`.
- `--candidate-limit <number>`: defaults to `20`.
- `--test-limit <number>`: defaults to `50`.
- `--reference-limit <number>`: defaults to `200`.
- `--include-snippets`.
- `--snippet-lines <number>`: defaults to `1`.
- `--candidate-policy fail|select|group`: defaults to `fail`; `group` is not supported by this command.
- `--min-confidence high|medium|low`: defaults to `medium`.
- `--explain-selection`.

Output includes `selectionInput`, optional fuzzy `selection`, `subject`, `testProjects`, `limits`, and a limited `tests` section. Test candidates include `kind`, `framework`, symbol facts, project facts, `confidence`, deterministic `score`, `reasonCodes`, and evidence locations.

### `tests-for-diff`

Finds tests related to changed symbols in a diff.

```powershell
dotnet run --no-launch-profile --project navlyn -- tests-for-diff --workspace navlyn.slnx --base main
dotnet run --no-launch-profile --project navlyn -- tests-for-diff --workspace navlyn.slnx --staged --test-limit 20
```

Diff options:

- `--base <ref>`
- `--head <ref>`
- `--staged`
- `--include-unstaged`

Additional options:

- `--project <project>`: production project filter.
- `--test-project <project>`: test project filter.
- `--exclude-generated`.
- `--symbol-limit <number>`: defaults to `50`.
- `--test-limit <number>`: defaults to `100`.
- `--reference-limit <number>`: defaults to `200`.
- `--include-snippets`.
- `--snippet-lines <number>`: defaults to `1`.

Output includes the diff, changed symbols, unresolved changes, discovered test projects, and a limited `tests` section. `review-diff.relatedTests` uses the same resolver while preserving the existing `relatedTests` section shape.

## Dependency Injection Commands

DI commands return source-level `Microsoft.Extensions.DependencyInjection` registration facts. They do not execute application code, build a runtime service provider, run DI validation, or claim complete runtime conditional registration analysis.

### `di-graph`

Reports source-level service registrations, constructor dependency edges, and conservative risk facts.

```powershell
dotnet run --no-launch-profile --project navlyn -- di-graph --workspace navlyn.slnx
dotnet run --no-launch-profile --project navlyn -- di-graph --workspace navlyn.slnx --project WebApp --registration-limit 200
```

Additional options:

- `--project <project>`: optional repeated project context filter.
- `--registration-limit <number>`: defaults to `200`.
- `--dependency-limit <number>`: defaults to `300`.
- `--risk-limit <number>`: defaults to `100`.
- `--include-options`: include options registrations. Defaults to true.
- `--include-hosted-services`: include hosted service registrations. Defaults to true.
- `--include-risks`: include conservative DI risk facts. Defaults to true.
- `--exclude-generated`.
- `--include-snippets`.
- `--snippet-lines <number>`: defaults to `1`.

Output includes `registrations`, `dependencies`, and `risks` sections. Registration items include `registrationKind`, `lifetime`, `serviceType`, `implementationType`, optional `factory` / `instance`, source span, project facts, `confidence`, `reasonCodes`, and evidence. Risk facts are evidence-first and include `riskKind`, `severity`, `confidence`, `claim`, related types, reason codes, and evidence.

Supported patterns include direct `AddSingleton`, `AddScoped`, `AddTransient`, `TryAdd*`, `ServiceDescriptor.*`, `AddHostedService<T>()`, `Configure<TOptions>()`, and `AddOptions<TOptions>()` calls where Roslyn can resolve source facts.

### `where-registered`

Finds DI registrations for a selected type.

```powershell
dotnet run --no-launch-profile --project navlyn -- where-registered --workspace navlyn.slnx --query WidgetService --assume-kind NamedType
dotnet run --no-launch-profile --project navlyn -- where-registered --workspace navlyn.slnx --candidate-id sym:v1:...
dotnet run --no-launch-profile --project navlyn -- where-registered --workspace navlyn.slnx --file src/App/WidgetService.cs --line 7 --column 21
```

Input modes:

- `--query <text>`: fuzzy type query.
- `--candidate-id <id>`: selects a candidate returned by a previous fuzzy command.
- `--file <path> --line <number> --column <number>`: source-position mode.

Exactly one input mode is required.

Additional options:

- `--project <project>`.
- `--exclude-generated`.
- `--candidate-limit <number>`: defaults to `20`.
- `--registration-limit <number>`: defaults to `50`.
- `--dependency-limit <number>`: defaults to `100`.
- `--include-snippets`.
- `--snippet-lines <number>`: defaults to `1`.
- `--candidate-policy fail|select|group`: defaults to `fail`; `group` is not supported by this command.
- `--min-confidence high|medium|low`: defaults to `medium`.
- `--explain-selection`.

Output includes `selectionInput`, optional fuzzy `selection`, `subject`, `limits`, matching `registrations`, and `constructorDependencies` for selected implementation types.

### `di-impact`

Returns DI registrations, constructor dependencies, consumers, and conservative risk facts for a selected type.

```powershell
dotnet run --no-launch-profile --project navlyn -- di-impact --workspace navlyn.slnx --query IWidgetStore --assume-kind NamedType --depth 2
```

Input modes and fuzzy selection options match `where-registered`.

Additional options:

- `--project <project>`.
- `--exclude-generated`.
- `--candidate-limit <number>`: defaults to `20`.
- `--registration-limit <number>`: defaults to `50`.
- `--consumer-limit <number>`: defaults to `50`.
- `--dependency-limit <number>`: defaults to `100`.
- `--risk-limit <number>`: defaults to `50`.
- `--depth <number>`: defaults to `2`.
- `--include-snippets`.
- `--snippet-lines <number>`: defaults to `1`.

Output includes `registrations`, `constructorDependencies`, `consumers`, and `risks`. Reported risks include `multiple-registrations`, `captive-dependency`, and `unresolved-service-candidate`.

## .NET Application Domain Commands

Application domain commands return source-level facts for common .NET application patterns. They do not execute the app, inspect runtime route tables, evaluate authorization policies, read secret values, build an EF runtime model, run migrations, or prove package binary compatibility.

Common options:

- `--workspace <path>`: `.slnx`, `.sln`, or `.csproj`.
- `--project <project>`: optional repeated project context filter.
- `--exclude-generated`.
- `--include-snippets`.
- `--snippet-lines <number>`: defaults to `1`.
- `--profile compact|evidence|full`.

Common output includes `workspace`, `kind`, `command`, optional `projects`, `limits`, bounded sections with `totalItems`, `limit`, `truncated`, `items`, top-level `truncated`, `warnings`, and `nextActions`. Domain facts include source spans, project facts, `confidence`, `reasonCodes`, and evidence. Empty source-level results return successful JSON with warnings such as `no-routes-found`, `no-options-found`, `no-message-handlers-found`, `no-ef-model-facts-found`, or `no-package-usage-found`.

### `route-map`

Reports source-level ASP.NET Core controller action and Minimal API route/auth facts.

```powershell
dotnet run --no-launch-profile --project navlyn -- route-map --workspace src/Web/Web.csproj
dotnet run --no-launch-profile --project navlyn -- route-map --workspace src/Web/Web.csproj --route orders --auth required
```

Additional options:

- `--route <fragment>`: optional repeated route pattern fragment filter.
- `--endpoint-kind <kind>`: repeated or comma-separated `controller-action`, `minimal-api`, or `any`.
- `--auth any|required|anonymous|unknown`: defaults to `any`.
- `--route-limit <number>`: defaults to `100`.
- `--evidence-limit <number>`: defaults to `5`.

Output includes a `routes` section. Route items include `endpointKind`, `httpMethods`, `routePattern`, `normalizedRoutePattern`, `handler`, `auth`, project/source span, `confidence`, `reasonCodes`, and evidence. Auth facts are source metadata from attributes or visible Minimal API calls such as `RequireAuthorization` / `AllowAnonymous`; they are not runtime authorization proof.

### `route-impact`

Reports route-map facts for routes matching one pattern fragment.

```powershell
dotnet run --no-launch-profile --project navlyn -- route-impact --workspace src/Web/Web.csproj --route /orders
```

Required options:

- `--route <fragment>`.

Additional options match `route-map` limits and snippet/generated options. Output includes the matched `routes` section.

### `options-graph`

Reports source-level options/configuration facts.

```powershell
dotnet run --no-launch-profile --project navlyn -- options-graph --workspace src/App/App.csproj
dotnet run --no-launch-profile --project navlyn -- options-graph --workspace src/App/App.csproj --query PaymentOptions
```

Additional options:

- `--query <type-or-key-fragment>`: optional option type or configuration key filter.
- `--option-limit <number>`: defaults to `100`.
- `--consumer-limit <number>`: defaults to `100`.
- `--binding-limit <number>`: defaults to `100`.
- `--evidence-limit <number>`: defaults to `5`.

Output includes `options`, `bindings`, `consumers`, and `validations`. Supported source patterns include visible `Configure<TOptions>`, `AddOptions<TOptions>`, `Bind`, `GetSection("...")`, `Validate*` calls, and constructor or primary-constructor consumers of `IOptions<T>`, `IOptionsSnapshot<T>`, and `IOptionsMonitor<T>`. Navlyn reports source keys and types only; it does not read configuration values.

### `config-impact`

Reports options/configuration facts filtered by a required query.

```powershell
dotnet run --no-launch-profile --project navlyn -- config-impact --workspace src/App/App.csproj --query Payments
```

Required options:

- `--query <type-or-key-fragment>`.

Limits and output sections match `options-graph`.

### `where-handled`

Finds source-level MediatR handlers for a selected request or notification type.

```powershell
dotnet run --no-launch-profile --project navlyn -- where-handled --workspace src/App/App.csproj --query CreateOrderCommand --assume-kind NamedType
dotnet run --no-launch-profile --project navlyn -- where-handled --workspace src/App/App.csproj --candidate-id sym:v1:...
dotnet run --no-launch-profile --project navlyn -- where-handled --workspace src/App/App.csproj --file src/App/CreateOrderCommand.cs --line 6 --column 22
```

Input modes:

- `--query <text>`
- `--candidate-id <id>`
- `--file <path> --line <number> --column <number>`

Exactly one input mode is required. Fuzzy selection options match `where-registered`.

Additional options:

- `--candidate-limit <number>`: defaults to `20`.
- `--handler-limit <number>`: defaults to `100`.
- `--evidence-limit <number>`: defaults to `5`.

Output includes `selectionInput`, optional fuzzy `selection`, `subject`, `handlers`, and an empty `callSites` section. Handler facts cover `IRequestHandler<TRequest,TResponse>`, `IRequestHandler<TRequest>`, and `INotificationHandler<TNotification>` when visible in source.

### `message-flow`

Reports bounded MediatR handler and `Send` / `Publish` call-site facts for a selected request or notification type.

```powershell
dotnet run --no-launch-profile --project navlyn -- message-flow --workspace src/App/App.csproj --query CreateOrderCommand --assume-kind NamedType
```

Input modes and fuzzy selection options match `where-handled`.

Additional options:

- `--candidate-limit <number>`: defaults to `20`.
- `--handler-limit <number>`: defaults to `100`.
- `--call-site-limit <number>`: defaults to `100`.
- `--evidence-limit <number>`: defaults to `5`.

Output includes `handlers` and `callSites`. Call-site facts are source-level and currently require Roslyn to resolve a direct message type from the `Send` or `Publish` argument.

### `ef-model`

Reports source-level EF Core model facts.

```powershell
dotnet run --no-launch-profile --project navlyn -- ef-model --workspace src/App/App.csproj
dotnet run --no-launch-profile --project navlyn -- ef-model --workspace src/App/App.csproj --entity Order
```

Additional options:

- `--entity <fragment>`: optional entity type filter.
- `--dbcontext <fragment>`: optional DbContext type filter.
- `--entity-limit <number>`: defaults to `200`.
- `--query-site-limit <number>`: defaults to `200`.
- `--evidence-limit <number>`: defaults to `5`.

Output includes `dbContexts`, `entities`, `dbSets`, `configurations`, and `querySites`. Supported source patterns include types inheriting `DbContext`, `DbSet<TEntity>` properties, `IEntityTypeConfiguration<TEntity>`, and visible LINQ/query calls on `DbSet` / queryable entity expressions. This is not an EF runtime model or migration diff.

### `entity-impact`

Reports EF source facts for a selected entity type.

```powershell
dotnet run --no-launch-profile --project navlyn -- entity-impact --workspace src/App/App.csproj --query Order --assume-kind NamedType
```

Input modes and fuzzy selection options match `where-handled`.

Additional options:

- `--candidate-limit <number>`: defaults to `20`.
- `--entity-limit <number>`: defaults to `200`.
- `--query-site-limit <number>`: defaults to `200`.
- `--evidence-limit <number>`: defaults to `5`.

Output includes matching `dbSets`, `configurations`, and `querySites`.

### `package-usage`

Reports source-level package reference and usage facts.

```powershell
dotnet run --no-launch-profile --project navlyn -- package-usage --workspace navlyn.slnx --package Microsoft.CodeAnalysis.CSharp.Workspaces
dotnet run --no-launch-profile --project navlyn -- package-usage --workspace src/App/App.csproj --package Microsoft.EntityFrameworkCore --namespace Microsoft.EntityFrameworkCore
```

Required options:

- `--package <id>`.

Additional options:

- `--namespace <namespace-fragment>`: optional repeated namespace hint for source usage attribution.
- `--usage-limit <number>`: defaults to `200`.
- `--reference-limit <number>`: defaults to `100`.
- `--include-tests`: defaults to true.

Output includes `packageReferences` from project files and `usages` from source namespace/symbol matches. Without namespace hints, Navlyn uses a conservative package-id namespace guess with lower confidence. Package usage is source-level evidence, not package API compatibility proof.

### `package-impact`

Reports the same package reference and source usage facts as `package-usage`, positioned for package change/upgrade impact investigation.

```powershell
dotnet run --no-launch-profile --project navlyn -- package-impact --workspace src/App/App.csproj --package Microsoft.EntityFrameworkCore --namespace Microsoft.EntityFrameworkCore
```

Required and optional options match `package-usage`.

## Context Pack Command

`context-pack` creates a bounded facts pack for agent investigations. It has three explicit input modes:

- Query mode: `--query <text>`
- Candidate mode: `--candidate-id <id>`
- Diff mode: `--diff`

The command returns deterministic JSON facts, ranked context items, budget information, and next-action hints. It does not generate prose summaries, review comments, or source-file dumps.

Common options:

- `--workspace <path>`: `.slnx`, `.sln`, or `.csproj`.
- `--project <project>`: optional repeated project context filter.
- `--exclude-generated`: excludes generated declarations, diagnostics, and snippets where applicable.
- `--goal review|modify|understand`: ranking profile. Defaults to `understand` in query mode and `review` in diff mode.
- `--change-kind behavior|signature|rename|constructor|nullability|async|public-api|di-registration|endpoint`: optional ranking hint, primarily for `--goal modify`.
- `--budget-tokens <number>`: approximate material budget. Defaults to `8000`.
- `--item-limit <number>`: maximum ranked context items. Defaults to `80`.
- `--snippet-policy none|signature|line|block`: defaults to `line`.
- `--snippet-lines <number>`: context lines for block snippets. Defaults to `1`.

Budgeting uses the deterministic `chars-div-4-v1` estimator: `charLimit = budgetTokens * 4`, and estimated tokens are `Ceiling(chars / 4)`. The budget applies to ranked context material in `pack.items`, not every JSON punctuation character in the result envelope. When material is omitted, `pack.omitted` records the reason and a follow-up command.

### `context-pack --query` / `--candidate-id`

Resolves a fuzzy symbol query or a previous `candidateId` and returns selected-symbol context.

```powershell
dotnet run --no-launch-profile --project navlyn -- context-pack --workspace navlyn.slnx --query CheckCommand --assume-kind NamedType
dotnet run --no-launch-profile --project navlyn -- context-pack --workspace navlyn.slnx --query CheckCommand --goal modify --snippet-policy signature
dotnet run --no-launch-profile --project navlyn -- context-pack --workspace navlyn.slnx --candidate-id sym:v1:...
```

Query options:

- `--query <text>` or `--candidate-id <id>`: exactly one is required in query mode.
- `--assume-kind <kind>`: optional repeated Roslyn symbol kind.
- `--match smart|exact|contains|regex`: defaults to `smart`.
- `--case-sensitive`.
- `--candidate-policy fail|select|group`: defaults to `fail`; `group` is not supported by `context-pack`.
- `--min-confidence high|medium|low`: defaults to `medium`.
- `--explain-selection`.
- `--candidate-limit <number>`: defaults to `20`.
- `--member-limit <number>`: defaults to `50`.
- `--reference-limit <number>`: defaults to `100`.
- `--relation-limit <number>`: defaults to `25`.
- `--file-limit <number>`: defaults to `50`.
- `--diagnostic-limit <number>`: defaults to `50` in query mode.
- `--depth <number>`: defaults to `2`.

Query mode output adds `query`, `selection`, and a `pack` whose sections include definition, member outline, references, related files, call relations, and diagnostics. `selection` includes candidate ids and may include `selectionInput` / `selectionExplanation`. `pack.items` is an ordered reading queue derived from those facts.

Ambiguous or unmatched queries exit successfully with candidates, no selected candidate, and an empty context pack. Navlyn does not merge context from multiple plausible symbols.

### `context-pack --diff`

Builds a review-oriented context pack from diff workflow facts.

```powershell
dotnet run --no-launch-profile --project navlyn -- context-pack --workspace navlyn.slnx --diff
dotnet run --no-launch-profile --project navlyn -- context-pack --workspace navlyn.slnx --diff --staged --budget-tokens 8000
```

Diff options:

- `--diff`: required for diff mode.
- `--base <ref>`.
- `--head <ref>`: requires `--base`.
- `--staged`.
- `--include-unstaged`: default when no base/head/staged mode is selected.
- `--symbol-limit <number>`: defaults to `50`.
- `--impact-limit <number>`: defaults to `100`.
- `--diagnostic-limit <number>`: defaults to `100` in diff mode.
- `--related-test-limit <number>`: defaults to `50`.
- `--depth <number>`: defaults to `2`.

Diff mode reuses `review-diff` facts and warnings. Diagnostics are current scoped diagnostics, and public contract changes are current-workspace heuristics. The `pack` sections include changed symbols, unresolved changes, public contract changes, impact, related tests, diagnostics scope, diagnostics, and findings.

Top-level shape:

```json
{
  "workspace": "navlyn.slnx",
  "kind": "solution",
  "command": "context-pack",
  "mode": "query",
  "goal": "understand",
  "changeKind": "signature",
  "query": {},
  "selection": {},
  "budget": {
    "requestedTokens": 8000,
    "estimator": "chars-div-4-v1",
    "charLimit": 32000,
    "estimatedTokensUsed": 1200,
    "charsUsed": 4800,
    "truncated": false
  },
  "limits": {},
  "pack": {
    "root": {},
    "sections": {},
    "items": [],
    "omitted": []
  },
  "truncated": false,
  "warnings": [],
  "nextActions": []
}
```

Invalid mode combinations such as missing all of `--query`, `--candidate-id`, and `--diff`, combining input modes, or using diff options without `--diff` return exit code `2`, write diagnostics to stderr, and produce no stdout. Invalid `--change-kind` values are rejected by the parser with exit code `2` and no stdout. `context-pack` is supported by `batch` with lower camel case payload fields such as `query`, `candidateId`, `diff`, `goal`, `changeKind`, `budgetTokens`, `itemLimit`, `snippetPolicy`, `candidateLimit`, `memberLimit`, `referenceLimit`, `relationLimit`, `fileLimit`, `diagnosticLimit`, `symbolLimit`, `impactLimit`, `relatedTestLimit`, `depth`, and `profile`.

## `check`

Validates that a workspace can be loaded.

```powershell
dotnet run --no-launch-profile --project navlyn -- check --workspace navlyn.slnx
```

Required options:

- `--workspace <path>`: `.slnx`, `.sln`, or `.csproj`.

Result shape:

```json
{
  "ok": true,
  "workspace": "navlyn.slnx",
  "kind": "solution",
  "projects": 1
}
```

## `overview`

Reports a compact workspace overview.

```powershell
dotnet run --no-launch-profile --project navlyn -- overview --workspace navlyn.slnx
```

Required options:

- `--workspace <path>`: `.slnx`, `.sln`, or `.csproj`.

Result shape:

```json
{
  "workspace": "navlyn.slnx",
  "kind": "solution",
  "projects": [
    {
      "name": "navlyn",
      "path": "navlyn/navlyn.csproj",
      "language": "C#",
      "assemblyName": "navlyn",
      "targetFramework": "net10.0",
      "languageVersion": "CSharp14",
      "preprocessorSymbols": ["DEBUG", "NET10_0", "TRACE"]
    }
  ]
}
```

`targetFramework`, `languageVersion`, and `preprocessorSymbols` are additive project context fields and are omitted when not available.

## `repo-graph`

Reports deterministic repository, project, package, and relationship facts for agent investigation.

```powershell
dotnet run --no-launch-profile --project navlyn -- repo-graph --workspace navlyn.slnx
dotnet run --no-launch-profile --project navlyn -- repo-graph --workspace navlyn.slnx --project navlyn
dotnet run --no-launch-profile --project navlyn -- repo-graph --workspace navlyn.slnx --relationship-limit 50
```

Required options:

- `--workspace <path>`: `.slnx`, `.sln`, or `.csproj`.

Optional options:

- `--project <project>`: filters by exact project name or repository-relative `.csproj` path. Can be specified more than once.
- `--include-packages`: includes direct package references. Defaults to true.
- `--include-msbuild-files`: includes repository MSBuild files such as `Directory.Build.props`, `Directory.Build.targets`, and `Directory.Packages.props`. Defaults to true.
- `--include-preprocessor-symbols`: includes project preprocessor symbols. Defaults to true.
- `--classification`: includes project classification facts. Defaults to true.
- `--relationship-limit <number>`: returns at most this many inferred relationships. Defaults to `200`.

Result shape:

```json
{
  "workspace": "navlyn.slnx",
  "kind": "solution",
  "command": "repo-graph",
  "projects": {
    "totalProjects": 2,
    "items": [
      {
        "id": "project:navlyn/navlyn.csproj:net10.0",
        "name": "navlyn",
        "path": "navlyn/navlyn.csproj",
        "language": "C#",
        "assemblyName": "navlyn",
        "targetFramework": "net10.0",
        "targetFrameworks": ["net10.0"],
        "outputType": "Exe",
        "sdk": "Microsoft.NET.Sdk",
        "nullable": "enable",
        "implicitUsings": "enable",
        "languageVersion": "CSharp14",
        "preprocessorSymbols": ["DEBUG", "NET10_0", "TRACE"],
        "classification": {
          "kind": "tooling",
          "confidence": "high",
          "reasonCodes": ["pack-as-tool", "output-type-exe"]
        }
      }
    ]
  },
  "edges": {
    "projectReferences": [],
    "packageReferences": []
  },
  "relationships": {
    "totalItems": 0,
    "limit": 200,
    "truncated": false,
    "items": []
  },
  "repository": {
    "root": ".",
    "centralPackageManagement": {
      "enabled": false,
      "files": []
    },
    "msbuildFiles": []
  },
  "limits": {
    "relationshipLimit": 200
  },
  "truncated": false,
  "warnings": [],
  "nextActions": []
}
```

Project classification is heuristic and evidence-backed. Known kinds are `test`, `benchmark`, `tooling`, `executable`, `library`, and `unknown`. Test relationships are inferred only when a test-classified project references a production-classified project. MSBuild files are discovered structurally; `repo-graph` does not claim a full evaluated MSBuild explanation and does not run restore.

## `diagnostics`

Reports compiler diagnostics for the loaded workspace.

```powershell
dotnet run --no-launch-profile --project navlyn -- diagnostics --workspace navlyn.slnx
dotnet run --no-launch-profile --project navlyn -- diagnostics --workspace navlyn.slnx --project navlyn
```

Required options:

- `--workspace <path>`: `.slnx`, `.sln`, or `.csproj`.

Optional options:

- `--project <project>`: filters by exact project name or repository-relative `.csproj` path. Can be specified more than once. Project names are case-sensitive.
- `--exclude-generated`: excludes diagnostics whose source location is generated code.
- `--severity <severity>`: filters by `Hidden`, `Info`, `Warning`, or `Error`. Can be specified more than once.
- `--id <diagnostic-id>`: filters by exact compiler diagnostic id. Can be specified more than once.
- `--limit <number>`: returns at most this many diagnostics after filtering. Must be 1 or greater.

Result shape:

```json
{
  "workspace": "navlyn.slnx",
  "kind": "solution",
  "severities": ["Error"],
  "ids": ["CS0246"],
  "limit": 10,
  "totalDiagnostics": 1,
  "diagnostics": [
    {
      "project": {
        "name": "navlyn",
        "path": "navlyn/navlyn.csproj",
        "targetFramework": "net10.0"
      },
      "severity": "Error",
      "id": "CS0246",
      "message": "The type or namespace name 'MissingType' could not be found.",
      "path": "Example.cs",
      "line": 5,
      "column": 12,
      "endLine": 5,
      "endColumn": 23
    }
  ]
}
```

`projects` is emitted only when one or more `--project` filters are provided, and reports the filters that were applied.
`severities`, `ids`, and `limit` are emitted only when the corresponding filters are provided.
`totalDiagnostics` reports the number of diagnostics after project, generated-code, severity, and id filtering and before `--limit` truncation.
Each diagnostic `project` reports the project that produced the diagnostic.
`excludeGenerated` is emitted only when `--exclude-generated` is provided.
Compiler diagnostics are reported in stdout JSON and do not cause a non-zero exit code.
Invalid `--severity` values produce `NAVLYN1009` on stderr, exit code `2`, and no stdout output.
Invalid `--limit` values produce `NAVLYN1003` on stderr, exit code `2`, and no stdout output.
Invalid empty `--project` values produce `NAVLYN1005` on stderr, exit code `2`, and no stdout output.
Unknown `--project` values produce `NAVLYN1006` on stderr, exit code `2`, and no stdout output.
Ambiguous `--project` values produce `NAVLYN1007` on stderr, exit code `2`, and no stdout output.

## `batch`

Runs multiple machine-readable requests through one workspace load. This command is intended for agents that need several navigation or search facts from the same workspace.

```powershell
Get-Content ./batch-request.json | dotnet run --no-launch-profile --project navlyn -- batch --workspace navlyn.slnx
dotnet run --no-launch-profile --project navlyn -- batch --workspace navlyn.slnx --input ./batch-request.json
```

Required options:

- `--workspace <path>`: `.slnx`, `.sln`, or `.csproj`.

Optional options:

- `--input <path>`: reads batch JSON from a file. When omitted, batch JSON is read from stdin.

Input shape:

```json
{
  "defaults": {
    "project": "navlyn",
    "excludeGenerated": false
  },
  "requests": [
    {
      "id": "navlyncli-symbol",
      "command": "symbol-at",
      "file": "navlyn/Cli/NavlynCli.cs",
      "line": 31,
      "column": 37
    },
    {
      "id": "check-symbols",
      "command": "symbols",
      "query": "Check",
      "limit": 5
    }
  ]
}
```

Each request must include:

- `id`: caller-provided correlation id. It must be unique enough for the caller's own workflow.
- `command`: one of `overview`, `diagnostics`, `symbols`, `symbols-in`, `outline`, `symbol-at`, `symbol-info`, `definition`, `references`, `implementations`, `type-hierarchy`, `callers`, `calls`, `find`, `resolve-target`, `where-used`, `about`, `related`, `impact`, `entrypoints`, `review-diff`, `review-pack`, `context-pack`, `repo-graph`, `public-api-diff`, `tests-for-symbol`, `tests-for-diff`, `framework-entrypoints`, `di-graph`, `where-registered`, `di-impact`, `route-map`, `route-impact`, `options-graph`, `config-impact`, `where-handled`, `message-flow`, `ef-model`, `entity-impact`, `package-usage`, or `package-impact`.

Top-level `defaults.project` applies to requests that support project scoping. Requests may override it with `project`. `symbols` and `diagnostics` may also use `projects` for multiple project filters; do not specify both `project` and `projects` on the same request.
Top-level `defaults.excludeGenerated` applies to requests that support generated-code filtering. Requests may override it with `excludeGenerated`.
Requests for profiled workflow commands may include `profile: "compact"`, `profile: "evidence"`, or `profile: "full"`. Invalid request-level profile values are per-request failures with `ok: false`; the batch envelope itself remains a successful command result when the top-level JSON is valid.

Request options otherwise use the same names as the command JSON concepts:

- `diagnostics`: optional `project`, `projects`, `excludeGenerated`, `severity`, `severities`, `diagnosticId`, `diagnosticIds`, `ids`, `limit`. Batch request `id` is reserved for correlation, so use `diagnosticId` / `diagnosticIds` / `ids` for diagnostic id filtering.
- `symbols`: required `query`; optional `match`, `caseSensitive`, `limit`, `kinds`, `namespaces`, `namespaceMatch`, `containers`, `containerMatch`, `accessibilities`, `project`, `projects`, `excludeGenerated`.
- `symbols-in`: required `file`, `line`; optional `startColumn`, `endColumn`, `project`, `excludeGenerated`.
- `outline`: required `file`; optional `project`, `excludeGenerated`.
- `symbol-at`, `symbol-info`, `type-hierarchy`: required exactly one input mode: `candidateId` or `file` with `line` and `column`; optional `project`, `excludeGenerated`.
- `definition`: required exactly one input mode: `candidateId` or `file` with `line` and `column`; optional `project`, `excludeGenerated`, `includeMetadata`.
- `references`: required exactly one input mode: `candidateId` or `file` with `line` and `column`; optional `project`, `excludeGenerated`, `resultProject`, `resultProjects`, `resultPath`, `resultPaths`, `resultKind`, `resultKinds`, `usageKind`, `usageKinds`, `groupBy`, `limit`.
- `implementations`, `callers`: required exactly one input mode: `candidateId` or `file` with `line` and `column`; optional `project`, `excludeGenerated`, `resultProject`, `resultProjects`, `resultPath`, `resultPaths`, `resultKind`, `resultKinds`, `limit`.
- `calls`: required exactly one input mode: `candidateId` or `file` with `line` and `column`; optional `project`, `excludeGenerated`, `resultProject`, `resultProjects`, `resultPath`, `resultPaths`, `resultKind`, `resultKinds`, `limit`, `includeMetadata`.
- `find`: required `query`; optional `assumeKind`, `assumeKinds`, `match`, `caseSensitive`, `project`, `projects`, `excludeGenerated`, `limit`, `candidatePolicy`, `minConfidence`, `explainSelection`.
- `resolve-target`: required exactly one input mode: `query`, `candidateId`, or `file` with `line` and `column`; optional `assumeKind`, `assumeKinds`, `match`, `caseSensitive`, `project`, `projects`, `excludeGenerated`, `limit`, `candidatePolicy`, `minConfidence`, `explainSelection`.
- `where-used`: required `query` or `candidateId`; optional `assumeKind`, `assumeKinds`, `match`, `caseSensitive`, `project`, `projects`, `excludeGenerated`, `limit`, `usageKind`, `usageKinds`, `groupBy`, `includeSnippets`, `snippetLines`, `candidatePolicy`, `minConfidence`, `explainSelection`.
- `about`: required `query` or `candidateId`; optional `assumeKind`, `assumeKinds`, `match`, `caseSensitive`, `project`, `projects`, `excludeGenerated`, `memberLimit`, `referenceLimit`, `relationLimit`, `includeSnippets`, `snippetLines`, `candidatePolicy`, `minConfidence`, `explainSelection`.
- `related`, `impact`: required `query` or `candidateId`; optional `assumeKind`, `assumeKinds`, `match`, `caseSensitive`, `project`, `projects`, `excludeGenerated`, `include`, `limit`, `depth`, `includeSnippets`, `snippetLines`, `candidatePolicy`, `minConfidence`, `explainSelection`.
- `entrypoints`: required `query` or `candidateId`; optional `assumeKind`, `assumeKinds`, `match`, `caseSensitive`, `project`, `projects`, `excludeGenerated`, `limit`, `depth`, `includeSnippets`, `snippetLines`, `candidatePolicy`, `minConfidence`, `explainSelection`.
- `review-diff`: optional `base`, `head`, `staged`, `includeUnstaged`, `project`, `projects`, `excludeGenerated`, `symbolLimit`, `impactLimit`, `diagnosticLimit`, `relatedTestLimit`, `depth`, `includeSnippets`, `snippetLines`, `profile`.
- `review-pack`: optional `pack`, `scope`, `base`, `head`, `staged`, `includeUnstaged`, `project`, `projects`, `excludeGenerated`, `findingLimit`, `evidenceLimit`, `symbolLimit`, `fileLimit`, `includeSnippets`, `snippetLines`, `architectureConfig`, `profile`.
- `context-pack`: required `query`, `candidateId`, or `diff: true`; optional `base`, `head`, `staged`, `includeUnstaged`, `goal`, `changeKind`, `budgetTokens`, `itemLimit`, `snippetPolicy`, `snippetLines`, `candidateLimit`, `memberLimit`, `referenceLimit`, `relationLimit`, `fileLimit`, `diagnosticLimit`, `symbolLimit`, `impactLimit`, `relatedTestLimit`, `depth`, `candidatePolicy`, `minConfidence`, `explainSelection`, `project`, `projects`, `excludeGenerated`, `profile`.
- `repo-graph`: optional `project`, `projects`, `includePackages`, `includeMsbuildFiles`, `includePreprocessorSymbols`, `classification`, `relationshipLimit`, `profile`.
- `public-api-diff`: required `base`; optional `head`, `project`, `projects`, `excludeGenerated`, `includeAdditions`, `includeAttributes`, `symbolLimit`, `changeLimit`, `profile`. Batch `public-api-diff` compares refs only and does not accept `staged` or `includeUnstaged`.
- `tests-for-symbol`: required exactly one input mode: `query`, `candidateId`, or `file` with `line` and `column`; optional `assumeKind`, `assumeKinds`, `match`, `caseSensitive`, `candidatePolicy`, `minConfidence`, `explainSelection`, `project`, `projects`, `testProject`, `testProjects`, `excludeGenerated`, `candidateLimit`, `testLimit`, `referenceLimit`, `includeSnippets`, `snippetLines`, `profile`.
- `tests-for-diff`: optional `base`, `head`, `staged`, `includeUnstaged`, `project`, `projects`, `testProject`, `testProjects`, `excludeGenerated`, `symbolLimit`, `testLimit`, `referenceLimit`, `includeSnippets`, `snippetLines`, `profile`.
- `framework-entrypoints`: optional `project`, `projects`, `framework`, `frameworks`, `entrypointKind`, `entrypointKinds`, `excludeGenerated`, `limit`, `evidenceLimit`, `includeSnippets`, `snippetLines`, `profile`.
- `di-graph`: optional `project`, `projects`, `registrationLimit`, `dependencyLimit`, `riskLimit`, `includeOptions`, `includeHostedServices`, `includeRisks`, `excludeGenerated`, `includeSnippets`, `snippetLines`, `profile`.
- `where-registered`: required exactly one input mode: `query`, `candidateId`, or `file` with `line` and `column`; optional `assumeKind`, `assumeKinds`, `match`, `caseSensitive`, `candidatePolicy`, `minConfidence`, `explainSelection`, `project`, `projects`, `excludeGenerated`, `candidateLimit`, `registrationLimit`, `dependencyLimit`, `includeSnippets`, `snippetLines`, `profile`.
- `di-impact`: required exactly one input mode: `query`, `candidateId`, or `file` with `line` and `column`; optional `assumeKind`, `assumeKinds`, `match`, `caseSensitive`, `candidatePolicy`, `minConfidence`, `explainSelection`, `project`, `projects`, `excludeGenerated`, `candidateLimit`, `registrationLimit`, `consumerLimit`, `dependencyLimit`, `riskLimit`, `depth`, `includeSnippets`, `snippetLines`, `profile`.
- `route-map`: optional `project`, `projects`, `routes`, `endpointKinds`, `auth`, `excludeGenerated`, `routeLimit`, `evidenceLimit`, `includeSnippets`, `snippetLines`, `profile`.
- `route-impact`: required `route`; optional `project`, `projects`, `excludeGenerated`, `routeLimit`, `evidenceLimit`, `includeSnippets`, `snippetLines`, `profile`.
- `options-graph`: optional `query`, `project`, `projects`, `excludeGenerated`, `optionLimit`, `consumerLimit`, `bindingLimit`, `evidenceLimit`, `includeSnippets`, `snippetLines`, `profile`.
- `config-impact`: required `query`; optional `project`, `projects`, `excludeGenerated`, `optionLimit`, `consumerLimit`, `bindingLimit`, `evidenceLimit`, `includeSnippets`, `snippetLines`, `profile`.
- `where-handled`: required exactly one input mode: `query`, `candidateId`, or `file` with `line` and `column`; optional `assumeKind`, `assumeKinds`, `match`, `caseSensitive`, `candidatePolicy`, `minConfidence`, `explainSelection`, `project`, `projects`, `excludeGenerated`, `candidateLimit`, `handlerLimit`, `evidenceLimit`, `includeSnippets`, `snippetLines`, `profile`.
- `message-flow`: required exactly one input mode: `query`, `candidateId`, or `file` with `line` and `column`; optional `assumeKind`, `assumeKinds`, `match`, `caseSensitive`, `candidatePolicy`, `minConfidence`, `explainSelection`, `project`, `projects`, `excludeGenerated`, `candidateLimit`, `handlerLimit`, `callSiteLimit`, `evidenceLimit`, `includeSnippets`, `snippetLines`, `profile`.
- `ef-model`: optional `entity`, `dbcontext`, `project`, `projects`, `excludeGenerated`, `entityLimit`, `querySiteLimit`, `evidenceLimit`, `includeSnippets`, `snippetLines`, `profile`.
- `entity-impact`: required exactly one input mode: `query`, `candidateId`, or `file` with `line` and `column`; optional `assumeKind`, `assumeKinds`, `match`, `caseSensitive`, `candidatePolicy`, `minConfidence`, `explainSelection`, `project`, `projects`, `excludeGenerated`, `candidateLimit`, `entityLimit`, `querySiteLimit`, `evidenceLimit`, `includeSnippets`, `snippetLines`, `profile`.
- `package-usage` and `package-impact`: required `package`; optional `namespaces`, `project`, `projects`, `includeTests`, `excludeGenerated`, `usageLimit`, `referenceLimit`, `profile`.

Only `review-diff` from the primitive diff workflow command set is supported in `batch`; `changed-symbols`, `impact-diff`, `diagnostics-diff`, `scope-at`, `symbol-source`, `signature`, `symbol-diagnostics`, and `diagnostic-pack` remain direct CLI commands. Use `review-diff`, `review-pack`, `context-pack` with `diff: true`, `public-api-diff`, or `tests-for-diff` in `batch` for agent review workflows.

Example agent investigation batch:

```json
{
  "requests": [
    { "id": "summary", "command": "repo-graph", "profile": "compact", "relationshipLimit": 20 },
    { "id": "tests", "command": "tests-for-symbol", "profile": "evidence", "query": "CheckCommand", "assumeKind": "NamedType", "testLimit": 10 },
    { "id": "di", "command": "di-impact", "query": "WorkspaceLoader", "assumeKind": "NamedType", "registrationLimit": 10, "consumerLimit": 10 }
  ]
}
```

Result shape:

```json
{
  "workspace": "navlyn.slnx",
  "kind": "solution",
  "totalRequests": 2,
  "succeededRequests": 1,
  "failedRequests": 1,
  "results": [
    {
      "id": "navlyncli-symbol",
      "command": "symbol-at",
      "ok": true,
      "result": {
        "file": "navlyn/Cli/NavlynCli.cs",
        "line": 31,
        "column": 37,
        "symbol": {
          "name": "CheckCommand",
          "kind": "NamedType",
          "container": "Navlyn.Cli.Commands",
          "path": "navlyn/Cli/Commands/CheckCommand.cs",
          "line": 6,
          "column": 23
        }
      }
    },
    {
      "id": "missing-definition",
      "command": "definition",
      "ok": false,
      "error": {
        "code": "NAVLYN1305",
        "message": "Source definition was not found for symbol: String."
      }
    }
  ]
}
```

Batch requests run sequentially.
Individual request failures are reported in stdout JSON as `ok: false` and do not write per-request errors to stderr. Invalid batch JSON, missing `requests`, missing request `id` or `command`, unsupported input shape, workspace load failures, and fatal execution failures produce a non-zero exit code.
Invalid batch input produces `NAVLYN1008` on stderr, exit code `2`, and no stdout output.
Unsupported request commands are reported as per-request `ok: false` items with `NAVLYN1008` so callers can still correlate the failure to the request `id`.

## `symbols`

Finds C# source symbol declarations by name.

```powershell
dotnet run --no-launch-profile --project navlyn -- symbols --workspace navlyn.slnx --query Check
dotnet run --no-launch-profile --project navlyn -- symbols --workspace navlyn.slnx --query CheckCommand --match exact
dotnet run --no-launch-profile --project navlyn -- symbols --workspace navlyn.slnx --query "^Check.*Command$" --match regex
dotnet run --no-launch-profile --project navlyn -- symbols --workspace navlyn.slnx --query check --case-sensitive
dotnet run --no-launch-profile --project navlyn -- symbols --workspace navlyn.slnx --query Command --limit 5
dotnet run --no-launch-profile --project navlyn -- symbols --workspace navlyn.slnx --query Command --kind NamedType
dotnet run --no-launch-profile --project navlyn -- symbols --workspace navlyn.slnx --query CheckCommand --project navlyn
dotnet run --no-launch-profile --project navlyn -- symbols --workspace tests/fixtures/MultiProjectFixture/MultiProjectFixture.slnx --query SharedWidget --project Library
```

Required options:

- `--workspace <path>`: `.slnx`, `.sln`, or `.csproj`.
- `--query <text>`: symbol name query.

Optional options:

- `--match contains|exact|regex`: defaults to `contains`.
- `--case-sensitive`: defaults to case-insensitive matching.
- `--limit <number>`: returns at most the first matching declarations from the deterministic result order. Must be 1 or greater.
- `--kind <kind>`: filters by the case-sensitive stable symbol kind string emitted in `matches[].kind`. Can be specified more than once.
- `--project <project>`: filters by exact project name or repository-relative `.csproj` path. Can be specified more than once. Project names are case-sensitive. Project names that match multiple loaded projects are ambiguous and produce a usage error.
- `--exclude-generated`: excludes generated source files from declaration search.
- `--namespace <namespace>`: filters by containing namespace. Can be specified more than once.
- `--namespace-match contains|exact|regex`: namespace match mode. Defaults to `contains`.
- `--container <container>`: filters by containing symbol display string. Can be specified more than once.
- `--container-match contains|exact|regex`: container match mode. Defaults to `contains`.
- `--accessibility <accessibility>`: filters by Roslyn accessibility string such as `Public`, `Internal`, `Private`, `Protected`, `ProtectedOrInternal`, or `ProtectedAndInternal`. Can be specified more than once.

Result shape:

```json
{
  "query": "CheckCommand",
  "match": "contains",
  "caseSensitive": false,
  "kinds": [],
  "namespaces": ["Navlyn.Cli.Commands"],
  "namespaceMatch": "exact",
  "containers": ["CheckCommand"],
  "containerMatch": "contains",
  "accessibilities": ["Public"],
  "projects": [
    {
      "filter": "navlyn",
      "name": "navlyn",
      "path": "navlyn/navlyn.csproj",
      "targetFramework": "net10.0"
    }
  ],
  "limit": null,
  "totalMatches": 1,
  "matches": [
    {
      "name": "CheckCommand",
      "kind": "NamedType",
      "container": "Navlyn.Cli.Commands",
      "path": "navlyn/Cli/Commands/CheckCommand.cs",
      "line": 6,
      "column": 23,
      "endLine": 6,
      "endColumn": 35
    }
  ]
}
```

`totalMatches` reports the number of matches after `--kind` filtering and before `--limit` truncation.
Namespace, container, accessibility, project, and generated-code filters are also applied before `totalMatches` and `--limit`.
`kinds` reports the normalized kind filter values used for the query.
`namespaces`, `namespaceMatch`, `containers`, `containerMatch`, and `accessibilities` are emitted only when those filters are provided.
`projects` is emitted only when one or more `--project` filters are provided, and reports the filters that were applied.
`excludeGenerated` is emitted only when `--exclude-generated` is provided.
Invalid regular expressions produce `NAVLYN1002` on stderr, exit code `2`, and no stdout output.
Invalid `--limit` values produce `NAVLYN1003` on stderr, exit code `2`, and no stdout output.
Unknown `--kind` values produce `NAVLYN1004` on stderr, exit code `2`, and no stdout output.
Unknown `--accessibility` values produce `NAVLYN1004` on stderr, exit code `2`, and no stdout output.
Invalid empty `--project` values produce `NAVLYN1005` on stderr, exit code `2`, and no stdout output.
Unknown `--project` values produce `NAVLYN1006` on stderr, exit code `2`, and no stdout output.
Ambiguous `--project` values produce `NAVLYN1007` on stderr, exit code `2`, and no stdout output.
Partial declarations produce one match per source declaration.

## `symbols-in`

Lists C# symbols resolved from identifier tokens on a source line or column span. This is an exploratory helper for choosing an exact position before calling `symbol-at`, `definition`, or `references`.

```powershell
dotnet run --no-launch-profile --project navlyn -- symbols-in --workspace navlyn.slnx --file navlyn/Cli/NavlynCli.cs --line 31
dotnet run --no-launch-profile --project navlyn -- symbols-in --workspace navlyn.slnx --file navlyn/Cli/NavlynCli.cs --line 31 --start-column 37 --end-column 49 --project navlyn
```

Required options:

- `--workspace <path>`: `.slnx`, `.sln`, or `.csproj`.
- `--file <path>`: C# source file in the workspace.
- `--line <number>`: 1-based source line.

Optional options:

- `--start-column <number>`: 1-based inclusive start column. Defaults to the start of the line.
- `--end-column <number>`: 1-based exclusive end column. Defaults to the end of the line.
- `--project <project>`: resolves the source file in the context of an exact project name or repository-relative `.csproj` path. Project names are case-sensitive. Project names that match multiple loaded projects are ambiguous and produce a usage error.
- `--exclude-generated`: rejects generated input files.

Result shape:

```json
{
  "file": "navlyn/Cli/NavlynCli.cs",
  "line": 31,
  "startColumn": 1,
  "endColumn": 60,
  "project": {
    "filter": "navlyn",
    "name": "navlyn",
    "path": "navlyn/navlyn.csproj"
  },
  "symbols": [
    {
      "name": "CheckCommand",
      "kind": "NamedType",
      "container": "Navlyn.Cli.Commands",
      "line": 31,
      "column": 37,
      "endLine": 31,
      "endColumn": 49
    }
  ]
}
```

Each result `line` and `column` is a position that can be passed to `symbol-at`, `definition`, or `references` with the same `--file`. Each result `endLine` and `endColumn` reports the exclusive end of the identifier token. The top-level `startColumn` and `endColumn` report the inspected line span.
`symbols-in` intentionally stays identifier-token focused by default. Operators, indexer punctuation, predefined-type keywords, and other non-identifier semantic constructs should be queried with exact source-position commands such as `symbol-at`, `definition`, or `symbol-info`.
`project` is emitted only when `--project` is provided.
`excludeGenerated` is emitted only when `--exclude-generated` is provided.
Lines or spans with no C# symbols produce an empty `symbols` array.
Invalid source file paths produce `NAVLYN1301` on stderr, exit code `2`, and no stdout output.
Source files outside the loaded workspace produce `NAVLYN1302` on stderr, exit code `2`, and no stdout output.
Invalid lines, columns, or spans produce `NAVLYN1303` on stderr, exit code `2`, and no stdout output.
Invalid empty `--project` values produce `NAVLYN1005` on stderr, exit code `2`, and no stdout output.
Unknown `--project` values produce `NAVLYN1006` on stderr, exit code `2`, and no stdout output.
Ambiguous `--project` values produce `NAVLYN1007` on stderr, exit code `2`, and no stdout output.
Source files outside the selected `--project` produce `NAVLYN1306` on stderr, exit code `2`, and no stdout output.
Generated source files with `--exclude-generated` produce `NAVLYN1307` on stderr, exit code `2`, and no stdout output.

## `outline`

Returns a semantic outline for one C# source file.

```powershell
dotnet run --no-launch-profile --project navlyn -- outline --workspace navlyn.slnx --file navlyn/Cli/Commands/CheckCommand.cs
```

Required options:

- `--workspace <path>`: `.slnx`, `.sln`, or `.csproj`.
- `--file <path>`: C# source file in the workspace.

Optional options:

- `--project <project>`: resolves the source file in the context of an exact project name or repository-relative `.csproj` path.
- `--exclude-generated`: rejects generated input files.

Result shape:

```json
{
  "file": "navlyn/Cli/Commands/CheckCommand.cs",
  "entries": [
    {
      "name": "Create",
      "kind": "Method",
      "container": "Navlyn.Cli.Commands.CheckCommand",
      "facts": {
        "signature": "System.CommandLine.Command Navlyn.Cli.Commands.CheckCommand.Create()",
        "accessibility": "Public",
        "isStatic": true
      },
      "path": "navlyn/Cli/Commands/CheckCommand.cs",
      "line": 8,
      "column": 5,
      "endLine": 14,
      "endColumn": 6
    }
  ]
}
```

Outline entries include namespaces, types, delegates, enum members, constructors, methods, properties, events, fields, indexers, operators, and local functions. Lambda expressions and local variables are intentionally not part of the outline contract.

## `symbol-at`

Resolves the C# symbol at a source position.

```powershell
dotnet run --no-launch-profile --project navlyn -- symbol-at --workspace navlyn.slnx --file navlyn/Cli/Commands/CheckCommand.cs --line 6 --column 23 --project navlyn
```

Required options:

- `--workspace <path>`: `.slnx`, `.sln`, or `.csproj`.
- Target input: either `--candidate-id <id>` or all of `--file <path>`, `--line <number>`, and `--column <number>`.

Optional options:

- `--project <project>`: resolves the source file in the context of an exact project name or repository-relative `.csproj` path. Project names are case-sensitive.
- `--exclude-generated`: rejects generated input files.

Result shape:

```json
{
  "file": "navlyn/Cli/Commands/CheckCommand.cs",
  "line": 6,
  "column": 23,
  "project": {
    "filter": "navlyn",
    "name": "navlyn",
    "path": "navlyn/navlyn.csproj"
  },
  "symbol": {
    "name": "CheckCommand",
    "kind": "NamedType",
    "container": "Navlyn.Cli.Commands",
    "path": "navlyn/Cli/Commands/CheckCommand.cs",
    "line": 6,
    "column": 23
  }
}
```

If the resolved symbol has no source location, `symbol.path`, `symbol.line`, and `symbol.column` are `null`.
`project` is emitted only when `--project` is provided.
`excludeGenerated` is emitted only when `--exclude-generated` is provided.
When the position is on a partial declaration, `symbol-at` reports that declaration location. Use `definition` when all source definitions for a symbol are needed.

Invalid source file paths produce `NAVLYN1301` on stderr, exit code `2`, and no stdout output.
Source files outside the loaded workspace produce `NAVLYN1302` on stderr, exit code `2`, and no stdout output.
Invalid source positions produce `NAVLYN1303` on stderr, exit code `2`, and no stdout output.
Positions without a C# symbol produce `NAVLYN1304` on stderr, exit code `2`, and no stdout output.
Invalid empty `--project` values produce `NAVLYN1005` on stderr, exit code `2`, and no stdout output.
Unknown `--project` values produce `NAVLYN1006` on stderr, exit code `2`, and no stdout output.
Ambiguous `--project` values produce `NAVLYN1007` on stderr, exit code `2`, and no stdout output.
Source files outside the selected `--project` produce `NAVLYN1306` on stderr, exit code `2`, and no stdout output.
Generated source files with `--exclude-generated` produce `NAVLYN1307` on stderr, exit code `2`, and no stdout output.

## `symbol-info`

Returns the selected symbol plus expression and binding facts at a source position. This command is additive exploration; it does not change the single-symbol contract of `symbol-at`, `definition`, or `references`.

```powershell
dotnet run --no-launch-profile --project navlyn -- symbol-info --workspace navlyn.slnx --file navlyn/Cli/NavlynCli.cs --line 31 --column 37
```

Required options:

- `--workspace <path>`: `.slnx`, `.sln`, or `.csproj`.
- Target input: either `--candidate-id <id>` or all of `--file <path>`, `--line <number>`, and `--column <number>`.

Optional options:

- `--project <project>`: resolves the source file in the context of an exact project name or repository-relative `.csproj` path.
- `--exclude-generated`: rejects generated input files.

Result shape:

```json
{
  "file": "navlyn/Cli/NavlynCli.cs",
  "line": 31,
  "column": 37,
  "symbol": {
    "name": "CheckCommand",
    "kind": "NamedType",
    "container": "Navlyn.Cli.Commands",
    "facts": {}
  },
  "expression": {
    "kind": "IdentifierName",
    "type": {},
    "convertedType": {}
  },
  "containingSymbol": {
    "name": "CreateRootCommand",
    "kind": "Method",
    "container": "Navlyn.Cli.NavlynCli",
    "facts": {}
  },
  "invocation": {
    "kind": "Invocation",
    "target": {},
    "arguments": []
  }
}
```

When applicable, `symbol-info` may include `invocation`, `attribute`, `return`, and `lambda` objects. Invocation and object-creation entries include selected target facts and argument-to-parameter mapping, including target-typed `new` when Roslyn exposes the constructed type. Attribute entries distinguish attribute type from attribute constructor. Return entries distinguish declared return type from expression and converted types. Lambda entries include target type and inferred return type where Roslyn exposes them. Nullable flow-state is not reported.

## `scope-at`

Returns enclosing C# scope facts for a source position. Unlike `symbol-at`, this command is useful on positions inside a member body because it reports the surrounding namespace, type, member, local function, lambda, or top-level statement stack.

```powershell
dotnet run --no-launch-profile --project navlyn -- scope-at --workspace navlyn.slnx --file navlyn/Cli/NavlynCli.cs --line 31 --column 37
```

Required options:

- `--workspace <path>`: `.slnx`, `.sln`, or `.csproj`.
- Target input: either `--candidate-id <id>` or all of `--file <path>`, `--line <number>`, and `--column <number>`.

Optional options:

- `--project <project>`: resolves the source file in the context of an exact project name or repository-relative `.csproj` path.
- `--exclude-generated`: rejects generated input files.

Result shape:

```json
{
  "file": "navlyn/Cli/NavlynCli.cs",
  "line": 31,
  "column": 37,
  "projectContext": {
    "name": "navlyn",
    "path": "navlyn/navlyn.csproj",
    "targetFramework": "net10.0",
    "languageVersion": "CSharp14",
    "preprocessorSymbols": []
  },
  "scopes": [
    {
      "kind": "Type",
      "syntaxKind": "ClassDeclaration",
      "path": "navlyn/Cli/NavlynCli.cs",
      "line": 7,
      "column": 1,
      "endLine": 72,
      "endColumn": 2,
      "symbol": {}
    },
    {
      "kind": "Member",
      "syntaxKind": "MethodDeclaration",
      "path": "navlyn/Cli/NavlynCli.cs",
      "line": 28,
      "column": 5,
      "endLine": 70,
      "endColumn": 6,
      "symbol": {}
    }
  ],
  "containingSymbol": {}
}
```

Scopes are ordered outer-to-inner. `scope-at` reports source-level syntax/semantic context only; preprocessor inactive text may have no useful scope.

## `symbol-source`

Returns bounded source slices for the selected C# symbol. This is for reading a selected declaration safely; it is not an arbitrary file-range dump.

```powershell
dotnet run --no-launch-profile --project navlyn -- symbol-source --workspace navlyn.slnx --candidate-id sym:v1:... --view declaration
```

Required options:

- `--workspace <path>`: `.slnx`, `.sln`, or `.csproj`.
- Target input: either `--candidate-id <id>` or all of `--file <path>`, `--line <number>`, and `--column <number>`.

Optional options:

- `--view signature|declaration|body|members|xml-doc|attributes`: defaults to `declaration`.
- `--max-lines <number>`: maximum source lines per slice. Defaults to `80`.
- `--budget-tokens <number>`: approximate character budget per slice using `tokens * 4`. Defaults to `4000`.
- `--project <project>`.
- `--exclude-generated`.

Result shape:

```json
{
  "file": "tests/fixtures/SymbolNavigationFixture/FixtureCode.cs",
  "line": 50,
  "column": 19,
  "view": "declaration",
  "limits": {
    "maxLines": 80,
    "budgetTokens": 4000
  },
  "symbol": {},
  "slices": [
    {
      "textKind": "declaration",
      "path": "tests/fixtures/SymbolNavigationFixture/FixtureCode.cs",
      "startLine": 50,
      "startColumn": 1,
      "endLine": 53,
      "endColumn": 2,
      "lines": [],
      "truncated": false
    }
  ],
  "truncated": false,
  "warnings": []
}
```

Metadata-only symbols return an empty `slices` array with `metadata-only-symbol` in `warnings`. Partial symbols can return multiple slices ordered by path and source position. `members` returns member declaration slices for type declarations.

## `signature`

Returns API-shape facts for one selected C# symbol.

```powershell
dotnet run --no-launch-profile --project navlyn -- signature --workspace navlyn.slnx --file navlyn/Cli/Commands/CheckCommand.cs --line 8 --column 27
```

Required options:

- `--workspace <path>`: `.slnx`, `.sln`, or `.csproj`.
- Target input: either `--candidate-id <id>` or all of `--file <path>`, `--line <number>`, and `--column <number>`.

Optional options:

- `--project <project>`.
- `--exclude-generated`.

Result shape:

```json
{
  "file": "navlyn/Cli/Commands/CheckCommand.cs",
  "line": 8,
  "column": 27,
  "symbol": {
    "name": "Create",
    "kind": "Method",
    "facts": {}
  },
  "apiShape": {
    "displayName": "Navlyn.Cli.Commands.CheckCommand.Create()",
    "documentationCommentId": "M:Navlyn.Cli.Commands.CheckCommand.Create",
    "accessibility": "Public",
    "modifiers": ["public", "static"],
    "typeParameters": null,
    "genericConstraints": null,
    "parameters": [],
    "returnType": {},
    "attributes": [],
    "overriddenSymbol": null,
    "implementedSymbols": null,
    "declarationCount": 1,
    "isPartial": false,
    "declarations": []
  }
}
```

`signature` complements `about`: it focuses on the selected symbol's current API shape instead of references, related files, and relation summaries.

## `symbol-diagnostics`

Returns compiler diagnostics whose source span intersects the selected symbol declaration span.

```powershell
dotnet run --no-launch-profile --project navlyn -- symbol-diagnostics --workspace tests/fixtures/DiagnosticFixture/DiagnosticFixture.csproj --file tests/fixtures/DiagnosticFixture/BrokenCode.cs --line 5 --column 24 --limit 10
```

Required options:

- `--workspace <path>`: `.slnx`, `.sln`, or `.csproj`.
- Target input: either `--candidate-id <id>` or all of `--file <path>`, `--line <number>`, and `--column <number>`.

Optional options:

- `--severity Hidden|Info|Warning|Error`: can be specified more than once.
- `--id <id>`: can be specified more than once.
- `--limit <number>`.
- `--project <project>`.
- `--exclude-generated`.

Result shape:

```json
{
  "file": "tests/fixtures/DiagnosticFixture/BrokenCode.cs",
  "line": 5,
  "column": 24,
  "symbol": {},
  "filters": {
    "severities": [],
    "ids": [],
    "limit": 10
  },
  "totalDiagnostics": 1,
  "truncated": false,
  "diagnostics": [
    {
      "project": {},
      "severity": "Error",
      "id": "CS0246",
      "message": "...",
      "path": "tests/fixtures/DiagnosticFixture/BrokenCode.cs",
      "line": 5,
      "column": 12,
      "endLine": 5,
      "endColumn": 23,
      "reasonCodes": ["diagnostic-intersects-symbol-span"]
    }
  ]
}
```

This is source-span scoped, not a guarantee that every diagnostic causally related to the symbol is included.

## `diagnostic-pack`

Creates a bounded facts pack around compiler diagnostics. It can start from a diagnostic id or a diagnostic source position.

```powershell
dotnet run --no-launch-profile --project navlyn -- diagnostic-pack --workspace tests/fixtures/DiagnosticFixture/DiagnosticFixture.csproj --id CS0246 --limit 5
dotnet run --no-launch-profile --project navlyn -- diagnostic-pack --workspace tests/fixtures/DiagnosticFixture/DiagnosticFixture.csproj --file tests/fixtures/DiagnosticFixture/BrokenCode.cs --line 5 --column 12
```

Input modes:

- `--id <id>`: exact diagnostic id mode.
- `--file <path> --line <number> --column <number>`: source-position mode.

Exactly one input mode is required.

Optional options:

- `--project <project>`: optional repeated project filter.
- `--exclude-generated`.
- `--severity Hidden|Info|Warning|Error`: can be specified more than once.
- `--limit <number>`: defaults to `20`.
- `--budget-tokens <number>`: source context budget for representative context. Defaults to `3000`.

Result shape:

```json
{
  "workspace": "tests/fixtures/DiagnosticFixture/DiagnosticFixture.csproj",
  "kind": "project",
  "input": {
    "mode": "id",
    "id": "CS0246"
  },
  "filters": {},
  "totalDiagnostics": 2,
  "truncated": false,
  "diagnostics": [],
  "context": {
    "scope": {},
    "signature": {},
    "source": {},
    "warnings": []
  },
  "warnings": [],
  "nextActions": []
}
```

`context` is built from the first source-backed matching diagnostic. If no diagnostics match, the command exits successfully with `totalDiagnostics: 0`, empty diagnostics, and `no-diagnostics-matched` in `warnings`. The pack is facts-only and does not generate fix suggestions.

## `implementations`

Finds source implementations for the C# symbol at a source position. The covered source cases include interface types, interface members, explicit interface implementations, generic interface member implementations, and abstract or virtual method overrides. Non-applicable symbols return an empty `implementations` array.

```powershell
dotnet run --no-launch-profile --project navlyn -- implementations --workspace tests/fixtures/SymbolNavigationFixture/SymbolNavigationFixture.csproj --file tests/fixtures/SymbolNavigationFixture/FixtureCode.cs --line 50 --column 18
```

Required options:

- `--workspace <path>`: `.slnx`, `.sln`, or `.csproj`.
- Target input: either `--candidate-id <id>` or all of `--file <path>`, `--line <number>`, and `--column <number>`.

Optional options:

- `--project <project>`: resolves the source file in the context of an exact project name or repository-relative `.csproj` path. Project names are case-sensitive.
- `--exclude-generated`: rejects generated input files and excludes generated implementation locations.
- `--result-project <project>`: filters result-side implementation symbols by project. This is separate from input `--project`.
- `--result-path <path-fragment>`: filters result-side implementation paths by repository-relative path fragment. Can be specified more than once.
- `--result-kind <kind>`: filters result-side implementation symbols by symbol kind. Can be specified more than once.
- `--limit <number>`: limits the number of implementation items returned. `totalMatches` reports the item count before truncation.

Result shape:

```json
{
  "file": "tests/fixtures/SymbolNavigationFixture/FixtureCode.cs",
  "line": 50,
  "column": 18,
  "limit": 10,
  "totalMatches": 1,
  "symbol": {
    "name": "IWidgetFormatter",
    "kind": "NamedType",
    "container": "SymbolNavigationFixture"
  },
  "implementations": [
    {
      "name": "DefaultWidgetFormatter",
      "kind": "NamedType",
      "container": "SymbolNavigationFixture",
      "path": "tests/fixtures/SymbolNavigationFixture/FixtureCode.cs",
      "line": 55,
      "column": 21,
      "endLine": 55,
      "endColumn": 43
    }
  ]
}
```

`project` is emitted only when `--project` is provided.
`excludeGenerated` is emitted only when `--exclude-generated` is provided.
Implementation output is ordered deterministically by path, line, column, name, kind, and container.

Invalid source file paths produce `NAVLYN1301` on stderr, exit code `2`, and no stdout output.
Source files outside the loaded workspace produce `NAVLYN1302` on stderr, exit code `2`, and no stdout output.
Invalid source positions produce `NAVLYN1303` on stderr, exit code `2`, and no stdout output.
Positions without a C# symbol produce `NAVLYN1304` on stderr, exit code `2`, and no stdout output.
Invalid empty `--project` values produce `NAVLYN1005` on stderr, exit code `2`, and no stdout output.
Unknown `--project` values produce `NAVLYN1006` on stderr, exit code `2`, and no stdout output.
Ambiguous `--project` values produce `NAVLYN1007` on stderr, exit code `2`, and no stdout output.
Source files outside the selected `--project` produce `NAVLYN1306` on stderr, exit code `2`, and no stdout output.
Generated source files with `--exclude-generated` produce `NAVLYN1307` on stderr, exit code `2`, and no stdout output.

## `type-hierarchy`

Explores source type and member inheritance relationships. This is source navigation support, not a complete runtime dispatch graph.

```powershell
dotnet run --no-launch-profile --project navlyn -- type-hierarchy --workspace tests/fixtures/SymbolNavigationFixture/SymbolNavigationFixture.csproj --file tests/fixtures/SymbolNavigationFixture/FixtureCode.cs --line 50 --column 18
```

Required options:

- `--workspace <path>`: `.slnx`, `.sln`, or `.csproj`.
- Target input: either `--candidate-id <id>` or all of `--file <path>`, `--line <number>`, and `--column <number>`.

Optional options:

- `--project <project>`: resolves the source file in the context of an exact project name or repository-relative `.csproj` path.
- `--exclude-generated`: rejects generated input files and excludes generated source hierarchy locations.

Result shape:

```json
{
  "file": "tests/fixtures/SymbolNavigationFixture/FixtureCode.cs",
  "line": 50,
  "column": 18,
  "symbol": {
    "name": "IWidgetFormatter",
    "kind": "NamedType",
    "container": "SymbolNavigationFixture",
    "facts": {}
  },
  "baseTypes": [],
  "interfaces": [],
  "derivedTypes": [],
  "implementingTypes": [
    {
      "name": "DefaultWidgetFormatter",
      "kind": "NamedType",
      "container": "SymbolNavigationFixture",
      "facts": {},
      "path": "tests/fixtures/SymbolNavigationFixture/FixtureCode.cs",
      "line": 55,
      "column": 21
    }
  ],
  "baseMembers": [],
  "overridingMembers": [],
  "implementedMembers": []
}
```

Source results include locations. Metadata-only related symbols can still be represented with `facts.isMetadata: true` and null source location fields when Roslyn exposes useful symbol facts.

## `callers`

Finds source callers for the C# symbol at a source position. This is static source navigation, not a complete runtime dispatch graph. Navlyn includes direct source callers and related implementation or override symbols where Roslyn can resolve them reliably. Property and event accessor positions are normalized to the associated property or event before caller lookup.

```powershell
dotnet run --no-launch-profile --project navlyn -- callers --workspace navlyn.slnx --file navlyn/Cli/Commands/CheckCommand.cs --line 8 --column 27
```

Required options:

- `--workspace <path>`: `.slnx`, `.sln`, or `.csproj`.
- Target input: either `--candidate-id <id>` or all of `--file <path>`, `--line <number>`, and `--column <number>`.

Optional options:

- `--project <project>`: resolves the source file in the context of an exact project name or repository-relative `.csproj` path. Project names are case-sensitive.
- `--exclude-generated`: rejects generated input files and excludes generated caller locations.
- `--result-project <project>`: filters result-side caller symbols by project. This is separate from input `--project`.
- `--result-path <path-fragment>`: filters result-side caller symbol paths by repository-relative path fragment. Can be specified more than once.
- `--result-kind <kind>`: filters result-side caller symbols by symbol kind. Can be specified more than once.
- `--limit <number>`: limits the number of caller groups returned. `totalGroups` reports the group count before truncation.

Result shape:

```json
{
  "file": "navlyn/Cli/Commands/CheckCommand.cs",
  "line": 8,
  "column": 27,
  "limit": 10,
  "totalGroups": 1,
  "symbol": {
    "name": "Create",
    "kind": "Method",
    "container": "Navlyn.Cli.Commands.CheckCommand",
    "path": "navlyn/Cli/Commands/CheckCommand.cs",
    "line": 8,
    "column": 27,
    "endLine": 8,
    "endColumn": 33
  },
  "callers": [
    {
      "symbol": {
        "name": "CreateRootCommand",
        "kind": "Method",
        "container": "Navlyn.Cli.NavlynCli",
        "path": "navlyn/Cli/NavlynCli.cs",
        "line": 28,
        "column": 32,
        "endLine": 28,
        "endColumn": 49
      },
      "locations": [
        {
          "path": "navlyn/Cli/NavlynCli.cs",
          "line": 31,
          "column": 50,
          "endLine": 31,
          "endColumn": 56
        }
      ]
    }
  ]
}
```

`project` is emitted only when `--project` is provided.
`excludeGenerated` is emitted only when `--exclude-generated` is provided.
Caller output is grouped by calling source symbol. Groups and locations are ordered deterministically by source location and symbol facts.

Invalid source file paths produce `NAVLYN1301` on stderr, exit code `2`, and no stdout output.
Source files outside the loaded workspace produce `NAVLYN1302` on stderr, exit code `2`, and no stdout output.
Invalid source positions produce `NAVLYN1303` on stderr, exit code `2`, and no stdout output.
Positions without a C# symbol produce `NAVLYN1304` on stderr, exit code `2`, and no stdout output.
Invalid empty `--project` values produce `NAVLYN1005` on stderr, exit code `2`, and no stdout output.
Unknown `--project` values produce `NAVLYN1006` on stderr, exit code `2`, and no stdout output.
Ambiguous `--project` values produce `NAVLYN1007` on stderr, exit code `2`, and no stdout output.
Source files outside the selected `--project` produce `NAVLYN1306` on stderr, exit code `2`, and no stdout output.
Generated source files with `--exclude-generated` produce `NAVLYN1307` on stderr, exit code `2`, and no stdout output.

## `calls`

Finds source callees from the containing C# member at a source position. The requested position selects the containing source member; it does not need to be on a specific invocation expression.

```powershell
dotnet run --no-launch-profile --project navlyn -- calls --workspace navlyn.slnx --file navlyn/Cli/NavlynCli.cs --line 28 --column 32
```

Required options:

- `--workspace <path>`: `.slnx`, `.sln`, or `.csproj`.
- Target input: either `--candidate-id <id>` or all of `--file <path>`, `--line <number>`, and `--column <number>`.

Optional options:

- `--project <project>`: resolves the source file in the context of an exact project name or repository-relative `.csproj` path. Project names are case-sensitive.
- `--exclude-generated`: rejects generated input files and excludes generated callee locations.
- `--result-project <project>`: filters result-side callee symbols by project. This is separate from input `--project`.
- `--result-path <path-fragment>`: filters result-side callee symbol paths by repository-relative path fragment. Can be specified more than once.
- `--result-kind <kind>`: filters result-side callee symbols by symbol kind. Can be specified more than once.
- `--limit <number>`: limits the number of callee groups returned. `totalGroups` reports the group count before truncation.
- `--include-metadata`: includes metadata-only callee symbols when static analysis resolves them. Metadata callee symbols have `facts.isMetadata: true` and null `path`, `line`, `column`, `endLine`, and `endColumn`.

Result shape:

```json
{
  "file": "navlyn/Cli/NavlynCli.cs",
  "line": 28,
  "column": 32,
  "limit": 10,
  "totalGroups": 1,
  "caller": {
    "name": "CreateRootCommand",
    "kind": "Method",
    "container": "Navlyn.Cli.NavlynCli",
    "path": "navlyn/Cli/NavlynCli.cs",
    "line": 28,
    "column": 32,
    "endLine": 28,
    "endColumn": 49
  },
  "calls": [
    {
      "symbol": {
        "name": "Create",
        "kind": "Method",
        "container": "Navlyn.Cli.Commands.CheckCommand",
        "path": "navlyn/Cli/Commands/CheckCommand.cs",
        "line": 8,
        "column": 27,
        "endLine": 8,
        "endColumn": 33
      },
      "locations": [
        {
          "path": "navlyn/Cli/NavlynCli.cs",
          "line": 31,
          "column": 37,
          "endLine": 31,
          "endColumn": 58
        }
      ]
    }
  ]
}
```

`project` is emitted only when `--project` is provided.
`excludeGenerated` is emitted only when `--exclude-generated` is provided.
`includeMetadata` is emitted only when `--include-metadata` is provided.
`calls` reports source callees such as method calls, constructors, source properties, events, indexers, user-defined operators, local functions, and source delegate-valued locals or members invoked through a delegate call where static analysis resolves them. Metadata-only callees are omitted by default. With `--include-metadata`, metadata-only callee groups are included when Roslyn resolves the callee; their group `locations` still report source call sites, while the callee symbol source location fields are null. `--result-kind` applies to metadata groups, while `--result-project` and `--result-path` exclude metadata groups because they have no source symbol path. Groups and locations are ordered deterministically by source location and symbol facts.

Invalid source file paths produce `NAVLYN1301` on stderr, exit code `2`, and no stdout output.
Source files outside the loaded workspace produce `NAVLYN1302` on stderr, exit code `2`, and no stdout output.
Invalid source positions produce `NAVLYN1303` on stderr, exit code `2`, and no stdout output.
Positions without a containing C# member produce `NAVLYN1304` on stderr, exit code `2`, and no stdout output.
Invalid empty `--project` values produce `NAVLYN1005` on stderr, exit code `2`, and no stdout output.
Unknown `--project` values produce `NAVLYN1006` on stderr, exit code `2`, and no stdout output.
Ambiguous `--project` values produce `NAVLYN1007` on stderr, exit code `2`, and no stdout output.
Source files outside the selected `--project` produce `NAVLYN1306` on stderr, exit code `2`, and no stdout output.
Generated source files with `--exclude-generated` produce `NAVLYN1307` on stderr, exit code `2`, and no stdout output.

## `definition`

Finds source definitions for the C# symbol at a source position.

```powershell
dotnet run --no-launch-profile --project navlyn -- definition --workspace navlyn.slnx --file navlyn/Cli/NavlynCli.cs --line 31 --column 37 --project navlyn
```

Required options:

- `--workspace <path>`: `.slnx`, `.sln`, or `.csproj`.
- Target input: either `--candidate-id <id>` or all of `--file <path>`, `--line <number>`, and `--column <number>`.

Optional options:

- `--project <project>`: resolves the source file in the context of an exact project name or repository-relative `.csproj` path. Project names are case-sensitive.
- `--exclude-generated`: rejects generated input files and excludes generated definition locations.
- `--include-metadata`: returns metadata-only symbol facts instead of `NAVLYN1305` when the selected symbol has no source definition. Source definition locations are still reported only in `definitions`.

Result shape:

```json
{
  "file": "navlyn/Cli/NavlynCli.cs",
  "line": 31,
  "column": 37,
  "project": {
    "filter": "navlyn",
    "name": "navlyn",
    "path": "navlyn/navlyn.csproj"
  },
  "symbol": {
    "name": "CheckCommand",
    "kind": "NamedType",
    "container": "Navlyn.Cli.Commands"
  },
  "definitions": [
    {
      "path": "navlyn/Cli/Commands/CheckCommand.cs",
      "line": 6,
      "column": 23,
      "endLine": 6,
      "endColumn": 35
    }
  ]
}
```

Partial types and other symbols with multiple source definitions return multiple entries in deterministic path, line, column, endLine, and endColumn order.
Accessor keyword positions resolve to the associated source property or event definition.
`project` is emitted only when `--project` is provided.
`excludeGenerated` is emitted only when `--exclude-generated` is provided.
`includeMetadata` is emitted only when `--include-metadata` is provided.
When `--include-metadata` is provided and no source definition exists, `definition` exits successfully, returns the selected symbol with `facts.isMetadata: true`, and returns an empty `definitions` array. Without `--include-metadata`, symbols without source definitions continue to produce `NAVLYN1305`.

Invalid source file paths produce `NAVLYN1301` on stderr, exit code `2`, and no stdout output.
Source files outside the loaded workspace produce `NAVLYN1302` on stderr, exit code `2`, and no stdout output.
Invalid source positions produce `NAVLYN1303` on stderr, exit code `2`, and no stdout output.
Positions without a C# symbol produce `NAVLYN1304` on stderr, exit code `2`, and no stdout output.
Symbols without source definitions produce `NAVLYN1305` on stderr, exit code `2`, and no stdout output unless `--include-metadata` is provided.
Invalid empty `--project` values produce `NAVLYN1005` on stderr, exit code `2`, and no stdout output.
Unknown `--project` values produce `NAVLYN1006` on stderr, exit code `2`, and no stdout output.
Ambiguous `--project` values produce `NAVLYN1007` on stderr, exit code `2`, and no stdout output.
Source files outside the selected `--project` produce `NAVLYN1306` on stderr, exit code `2`, and no stdout output.
Generated source files with `--exclude-generated` produce `NAVLYN1307` on stderr, exit code `2`, and no stdout output.

## `references`

Finds source references for the C# symbol at a source position. Declaration locations are not included unless Roslyn reports them as reference locations for that symbol kind.

```powershell
dotnet run --no-launch-profile --project navlyn -- references --workspace navlyn.slnx --file navlyn/Cli/NavlynCli.cs --line 31 --column 37 --project navlyn
```

Required options:

- `--workspace <path>`: `.slnx`, `.sln`, or `.csproj`.
- Target input: either `--candidate-id <id>` or all of `--file <path>`, `--line <number>`, and `--column <number>`.

Optional options:

- `--project <project>`: resolves the source file in the context of an exact project name or repository-relative `.csproj` path. Project names are case-sensitive.
- `--exclude-generated`: rejects generated input files and excludes generated reference locations.
- `--result-project <project>`: filters result-side reference locations by project. This is separate from input `--project`.
- `--result-path <path-fragment>`: filters result-side reference paths by repository-relative path fragment. Can be specified more than once.
- `--result-kind <kind>`: filters by the referenced symbol kind. Can be specified more than once.
- `--usage-kind <kind>`: filters by source-level usage kind. Can be specified more than once or as comma-separated values. Supported values are `read`, `write`, `invoke`, `construct`, `inherit`, `implement`, `override`, `attribute`, `nameof`, and `typeof`.
- `--group-by <group>`: adds grouped summaries. Can be specified more than once or as comma-separated values. Supported values are `file`, `project`, `containing-symbol`, `usage-kind`, and `test-vs-production`.
- `--limit <number>`: limits the number of reference locations returned. `totalMatches` reports the location count before truncation.

Result shape:

```json
{
  "file": "navlyn/Cli/NavlynCli.cs",
  "line": 31,
  "column": 37,
  "limit": 10,
  "totalMatches": 1,
  "usageKindCounts": [
    {
      "usageKind": "construct",
      "count": 1
    }
  ],
  "project": {
    "filter": "navlyn",
    "name": "navlyn",
    "path": "navlyn/navlyn.csproj"
  },
  "symbol": {
    "name": "CheckCommand",
    "kind": "NamedType",
    "container": "Navlyn.Cli.Commands"
  },
  "references": [
    {
      "path": "navlyn/Cli/NavlynCli.cs",
      "line": 31,
      "column": 37,
      "endLine": 31,
      "endColumn": 49,
      "usageKind": "construct",
      "containingSymbol": {
        "name": "CreateRootCommand",
        "kind": "Method",
        "container": "Navlyn.Cli.NavlynCli",
        "path": "navlyn/Cli/NavlynCli.cs",
        "line": 28,
        "column": 32,
        "endLine": 28,
        "endColumn": 49,
        "facts": {}
      }
    }
  ]
}
```

Symbols with no source references return an empty `references` array. Reference output is ordered deterministically by path, line, column, endLine, and endColumn.
Each source reference includes additive semantic `containingSymbol` context when Roslyn can resolve the enclosing source symbol.
Each source reference includes additive `usageKind`. Usage kind is a bounded source-level classification, not flow-sensitive runtime proof. `totalMatches` and `usageKindCounts` are computed after result and usage-kind filters and before `--limit`. `groups` is emitted only when `--group-by` is provided and contains deterministic count summaries with a first reference location.
Accessor keyword positions resolve references for the associated source property or event.
`project` is emitted only when `--project` is provided.
`excludeGenerated` is emitted only when `--exclude-generated` is provided.

Invalid source file paths produce `NAVLYN1301` on stderr, exit code `2`, and no stdout output.
Source files outside the loaded workspace produce `NAVLYN1302` on stderr, exit code `2`, and no stdout output.
Invalid source positions produce `NAVLYN1303` on stderr, exit code `2`, and no stdout output.
Positions without a C# symbol produce `NAVLYN1304` on stderr, exit code `2`, and no stdout output.
Invalid empty `--project` values produce `NAVLYN1005` on stderr, exit code `2`, and no stdout output.
Unknown `--project` values produce `NAVLYN1006` on stderr, exit code `2`, and no stdout output.
Ambiguous `--project` values produce `NAVLYN1007` on stderr, exit code `2`, and no stdout output.
Source files outside the selected `--project` produce `NAVLYN1306` on stderr, exit code `2`, and no stdout output.
Generated source files with `--exclude-generated` produce `NAVLYN1307` on stderr, exit code `2`, and no stdout output.

# Navlyn CLI Commands

This document describes the currently implemented CLI contract. Keep examples compact and update this file when public command behavior changes.

## General Contract

- stdout is reserved for command result JSON.
- stderr is reserved for diagnostics and errors.
- Automation-facing output uses deterministic JSON.
- User-facing line and column values are 1-based.
- Paths are repository-relative when possible.
- Usage errors return exit code `2`.
- Runtime or workspace load failures return exit code `1`.
- Source-position commands resolve one symbol at the requested file, line, and column. Use exploratory commands such as `symbols-in`, not `definition` or `references`, for line/range-wide symbol discovery.
- Property and event accessor method positions are normalized to their associated source property or event for navigation output.
- Source-backed result locations keep the existing `path`, `line`, and `column` fields and include additive `endLine` and `endColumn` fields where Roslyn exposes a valid source span. `line` / `column` are 1-based inclusive start positions. `endLine` / `endColumn` are 1-based exclusive end positions.
- Source-position command top-level `file`, `line`, and `column` fields report the requested input point. Result locations and symbol-shaped source locations carry spans.
- Symbol-shaped results keep the existing `name`, `kind`, `container`, `path`, `line`, and `column` fields where applicable, may include additive `endLine` / `endColumn`, and may include an additive `facts` object for richer agent decisions.
- Source-navigation commands keep source locations as the default result surface. Commands that support metadata reporting require explicit `--include-metadata` / `includeMetadata`.
- Metadata-only symbols have no source span and use null source location fields such as `path`, `line`, `column`, `endLine`, and `endColumn` where those fields are present.
- Project-shaped results may include additive workspace context fields such as `targetFramework`, `languageVersion`, and `preprocessorSymbols`.

Run examples from the repository root. Use `--no-launch-profile` when running through `dotnet run` so Visual Studio launch settings do not affect stdout.

## Rich Symbol Facts

Symbol `facts` are additive and intended for agents that need to choose between overloads, generic members, source symbols, and metadata symbols without immediately opening source files. Fields vary by symbol kind and may include:

- `displayName`, `fullyQualifiedName`, `signature`, `documentationCommentId`.
- `namespace`, `containingType`, `project`, `assembly`, `accessibility`.
- `isSource`, `isMetadata`, `isStatic`, `isAbstract`, `isVirtual`, `isOverride`, `isAsync`, `isExtensionMethod`, `isConstructor`, `isOperator`, `isIndexer`.
- `arity`, `typeParameters`, `typeArguments`, `constructedFrom`.
- `parameters`, `returnType`, `propertyType`, `eventType`, `fieldType`.
- `attributes`, with attribute type and constructor facts when Roslyn exposes them.

Nullability facts currently use declared type annotations such as `Annotated` and `NotAnnotated`. Nullable flow-state is not reported by the current contract.

## C# Semantic Normalization

Navlyn reports source-backed synthesized-adjacent members when Roslyn exposes a useful source location, such as record positional properties and record `Deconstruct` methods. Symbols without source definitions still follow each command's source-only behavior by default; for example, `definition` reports `NAVLYN1305` when no source definition exists unless `--include-metadata` is used.

Accessor methods are normalized to the associated property or event for `symbol-at`, `definition`, `references`, `callers`, `calls`, `type-hierarchy`, and related source-position output. This keeps `get`, `set`, `add`, and `remove` keyword positions aligned with the user-facing source symbol.

## Workspace Context

Navlyn uses the Roslyn project/document context loaded by MSBuild. Conditional compilation follows the selected project context: active `#if` branches are visible to semantic commands, while inactive branches are disabled text and do not produce C# symbols. `symbols` omits inactive declarations, `symbols-in` returns an empty array for inactive text, and source-position commands such as `symbol-at` report `NAVLYN1304` when the selected inactive text has no symbol.

Multi-targeted projects are loaded as separate Roslyn projects when MSBuild exposes them that way. Their names commonly include the target framework, such as `MyProject(net10.0)`, and `overview` reports `targetFramework` when Navlyn can determine it. Use the exact project name to select a target-specific context. Filtering by a `.csproj` path can be ambiguous when the same project file expands to multiple target frameworks, and returns `NAVLYN1007`.

Linked files are matched and emitted by physical repository-relative path. When one physical file is linked into multiple projects, use `--project` to choose the intended semantic context. Without `--project`, Navlyn chooses a deterministic first matching document; returned symbol facts identify the project that produced a symbol when one is available.

## Generated Code

Commands include generated code by default. Commands that inspect source files, source locations, compiler diagnostics, or symbol declarations support `--exclude-generated`.

Generated-code detection currently treats these paths as generated:

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
- `--project <project>`: optional repeated input project context filter.
- `--exclude-generated`: excludes generated declarations and source locations.
- `--limit <number>`: command-specific result limit. For `find`, this limits returned candidates. For `where-used`, `related`, `impact`, and `entrypoints`, it limits returned references, files, or chains.

Common output fields:

- `confidence`: one of `high`, `medium`, `low`, `ambiguous`, or `none`.
- `totalCandidates`: candidate count before the candidate display limit.
- `candidateCount`: returned candidate count.
- `candidates`: returned candidates with `name`, `kind`, `container`, `facts`, source location, and `reasonCodes`.
- `selectedCandidate`: emitted only when selection rules choose one candidate.
- `alternatives`: emitted when one candidate is selected but meaningful alternatives remain.
- `warnings`: machine-readable warning strings.
- `nextActions`: machine-oriented follow-up command hints.

Fuzzy candidates and fuzzy source locations use the same source span convention as direct commands. `nextActions` remain command-invocation hints and keep point-style `file`, `line`, and `column` inputs.

Fuzzy selection rules prefer exact case-sensitive matches, then exact case-insensitive matches, then contains and normalized-name matches. Multiple exact matches are reported as `ambiguous`. A single exact match with weaker alternatives is selected with `medium` confidence. Heuristic-only matches are selected only when ranking produces one dominant candidate.

### `find`

Finds source symbols that plausibly match a compact query.

```powershell
dotnet run --no-launch-profile --project navlyn -- find --workspace navlyn.slnx --query CheckCommand --assume-kind NamedType
```

`find` returns the common fuzzy envelope and candidate list. It does not return references or relation summaries.

### `where-used`

Resolves a fuzzy query and lists source references for the selected candidate. References include semantic `containingSymbol` context by default.

```powershell
dotnet run --no-launch-profile --project navlyn -- where-used --workspace navlyn.slnx --query CheckCommand --assume-kind NamedType --limit 100
```

Additional options:

- `--include-snippets`: adds bounded source snippets to returned locations and file summaries.
- `--snippet-lines <number>`: context lines before and after the matched line. Defaults to `1`.

Selected-candidate output adds:

- `totalMatches`: reference count before `--limit`.
- `references`: limited source reference locations.
- `files`: file summaries with `referenceCount`, `firstLine`, and reason codes.

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
- `members`: type member outline summary when the selected candidate is a type.
- `references`: reference count, limited locations, and top files.
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

Output adds `files`, ordered by reason priority and deterministic path tie-breakers. File reasons include values such as `declares-selected-symbol`, `references-selected-symbol`, `caller-of-selected-member`, `callee-of-selected-member`, `implements-selected-symbol`, and `hierarchy-related-symbol`.

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

Output adds `totalChains` and `chains`. Each chain contains ordered `symbols` and an `endReason`, such as `no-upstream-callers`, `depth-limit`, or `cycle-detected`. Framework-specific entrypoint heuristics such as Unity lifecycle methods, ASP.NET endpoints, and test frameworks are out of scope for this initial static workflow.

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
      "path": "navlyn\\navlyn.csproj",
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
        "path": "navlyn\\navlyn.csproj",
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
Get-Content .\batch-request.json | dotnet run --no-launch-profile --project navlyn -- batch --workspace navlyn.slnx
dotnet run --no-launch-profile --project navlyn -- batch --workspace navlyn.slnx --input .\batch-request.json
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
      "file": "navlyn\\Cli\\NavlynCli.cs",
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
- `command`: one of `overview`, `diagnostics`, `symbols`, `symbols-in`, `outline`, `symbol-at`, `symbol-info`, `definition`, `references`, `implementations`, `type-hierarchy`, `callers`, `calls`, `find`, `where-used`, `about`, `related`, `impact`, or `entrypoints`.

Top-level `defaults.project` applies to requests that support project scoping. Requests may override it with `project`. `symbols` and `diagnostics` may also use `projects` for multiple project filters; do not specify both `project` and `projects` on the same request.
Top-level `defaults.excludeGenerated` applies to requests that support generated-code filtering. Requests may override it with `excludeGenerated`.

Request options otherwise use the same names as the command JSON concepts:

- `diagnostics`: optional `project`, `projects`, `excludeGenerated`, `severity`, `severities`, `diagnosticId`, `diagnosticIds`, `ids`, `limit`. Batch request `id` is reserved for correlation, so use `diagnosticId` / `diagnosticIds` / `ids` for diagnostic id filtering.
- `symbols`: required `query`; optional `match`, `caseSensitive`, `limit`, `kinds`, `namespaces`, `namespaceMatch`, `containers`, `containerMatch`, `accessibilities`, `project`, `projects`, `excludeGenerated`.
- `symbols-in`: required `file`, `line`; optional `startColumn`, `endColumn`, `project`, `excludeGenerated`.
- `outline`: required `file`; optional `project`, `excludeGenerated`.
- `symbol-at`, `symbol-info`, `type-hierarchy`: required `file`, `line`, `column`; optional `project`, `excludeGenerated`.
- `definition`: required `file`, `line`, `column`; optional `project`, `excludeGenerated`, `includeMetadata`.
- `references`, `implementations`, `callers`: required `file`, `line`, `column`; optional `project`, `excludeGenerated`, `resultProject`, `resultProjects`, `resultPath`, `resultPaths`, `resultKind`, `resultKinds`, `limit`.
- `calls`: required `file`, `line`, `column`; optional `project`, `excludeGenerated`, `resultProject`, `resultProjects`, `resultPath`, `resultPaths`, `resultKind`, `resultKinds`, `limit`, `includeMetadata`.
- `find`: required `query`; optional `assumeKind`, `assumeKinds`, `match`, `caseSensitive`, `project`, `projects`, `excludeGenerated`, `limit`.
- `where-used`: required `query`; optional `assumeKind`, `assumeKinds`, `match`, `caseSensitive`, `project`, `projects`, `excludeGenerated`, `limit`, `includeSnippets`, `snippetLines`.
- `about`: required `query`; optional `assumeKind`, `assumeKinds`, `match`, `caseSensitive`, `project`, `projects`, `excludeGenerated`, `memberLimit`, `referenceLimit`, `relationLimit`, `includeSnippets`, `snippetLines`.
- `related`, `impact`: required `query`; optional `assumeKind`, `assumeKinds`, `match`, `caseSensitive`, `project`, `projects`, `excludeGenerated`, `include`, `limit`, `depth`, `includeSnippets`, `snippetLines`.
- `entrypoints`: required `query`; optional `assumeKind`, `assumeKinds`, `match`, `caseSensitive`, `project`, `projects`, `excludeGenerated`, `limit`, `depth`, `includeSnippets`, `snippetLines`.

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
        "file": "navlyn\\Cli\\NavlynCli.cs",
        "line": 31,
        "column": 37,
        "symbol": {
          "name": "CheckCommand",
          "kind": "NamedType",
          "container": "Navlyn.Cli.Commands",
          "path": "navlyn\\Cli\\Commands\\CheckCommand.cs",
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

Batch requests run sequentially. Parallel batch execution is deferred until workspace sharing behavior has been reviewed.
Individual request failures are reported in stdout JSON as `ok: false` and do not write per-request errors to stderr. Invalid batch JSON, missing `requests`, missing request `id` or `command`, unsupported input shape, workspace load failures, and fatal execution failures produce a non-zero exit code.
Invalid batch input produces `NAVLYN1008` on stderr, exit code `2`, and no stdout output.

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
dotnet run --no-launch-profile --project navlyn -- symbols --workspace tests\fixtures\MultiProjectFixture\MultiProjectFixture.slnx --query SharedWidget --project Library
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
      "path": "navlyn\\navlyn.csproj",
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
      "path": "navlyn\\Cli\\Commands\\CheckCommand.cs",
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
dotnet run --no-launch-profile --project navlyn -- symbols-in --workspace navlyn.slnx --file navlyn\Cli\NavlynCli.cs --line 31
dotnet run --no-launch-profile --project navlyn -- symbols-in --workspace navlyn.slnx --file navlyn\Cli\NavlynCli.cs --line 31 --start-column 37 --end-column 49 --project navlyn
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
  "file": "navlyn\\Cli\\NavlynCli.cs",
  "line": 31,
  "startColumn": 1,
  "endColumn": 60,
  "project": {
    "filter": "navlyn",
    "name": "navlyn",
    "path": "navlyn\\navlyn.csproj"
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
dotnet run --no-launch-profile --project navlyn -- outline --workspace navlyn.slnx --file navlyn\Cli\Commands\CheckCommand.cs
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
  "file": "navlyn\\Cli\\Commands\\CheckCommand.cs",
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
      "path": "navlyn\\Cli\\Commands\\CheckCommand.cs",
      "line": 8,
      "column": 5,
      "endLine": 14,
      "endColumn": 6
    }
  ]
}
```

Outline entries include namespaces, types, delegates, enum members, constructors, methods, properties, events, fields, indexers, operators, and local functions. Lambda expressions and local variables are intentionally not part of the initial outline contract.

## `symbol-at`

Resolves the C# symbol at a source position.

```powershell
dotnet run --no-launch-profile --project navlyn -- symbol-at --workspace navlyn.slnx --file navlyn\Cli\Commands\CheckCommand.cs --line 6 --column 23 --project navlyn
```

Required options:

- `--workspace <path>`: `.slnx`, `.sln`, or `.csproj`.
- `--file <path>`: C# source file in the workspace.
- `--line <number>`: 1-based source line.
- `--column <number>`: 1-based source column.

Optional options:

- `--project <project>`: resolves the source file in the context of an exact project name or repository-relative `.csproj` path. Project names are case-sensitive.
- `--exclude-generated`: rejects generated input files.

Result shape:

```json
{
  "file": "navlyn\\Cli\\Commands\\CheckCommand.cs",
  "line": 6,
  "column": 23,
  "project": {
    "filter": "navlyn",
    "name": "navlyn",
    "path": "navlyn\\navlyn.csproj"
  },
  "symbol": {
    "name": "CheckCommand",
    "kind": "NamedType",
    "container": "Navlyn.Cli.Commands",
    "path": "navlyn\\Cli\\Commands\\CheckCommand.cs",
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
dotnet run --no-launch-profile --project navlyn -- symbol-info --workspace navlyn.slnx --file navlyn\Cli\NavlynCli.cs --line 31 --column 37
```

Required options:

- `--workspace <path>`: `.slnx`, `.sln`, or `.csproj`.
- `--file <path>`: C# source file in the workspace.
- `--line <number>`: 1-based source line.
- `--column <number>`: 1-based source column.

Optional options:

- `--project <project>`: resolves the source file in the context of an exact project name or repository-relative `.csproj` path.
- `--exclude-generated`: rejects generated input files.

Result shape:

```json
{
  "file": "navlyn\\Cli\\NavlynCli.cs",
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

## `implementations`

Finds source implementations for the C# symbol at a source position. The covered source cases include interface types, interface members, explicit interface implementations, generic interface member implementations, and abstract or virtual method overrides. Non-applicable symbols return an empty `implementations` array.

```powershell
dotnet run --no-launch-profile --project navlyn -- implementations --workspace tests\fixtures\SymbolNavigationFixture\SymbolNavigationFixture.csproj --file tests\fixtures\SymbolNavigationFixture\FixtureCode.cs --line 50 --column 18
```

Required options:

- `--workspace <path>`: `.slnx`, `.sln`, or `.csproj`.
- `--file <path>`: C# source file in the workspace.
- `--line <number>`: 1-based source line.
- `--column <number>`: 1-based source column.

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
  "file": "tests\\fixtures\\SymbolNavigationFixture\\FixtureCode.cs",
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
      "path": "tests\\fixtures\\SymbolNavigationFixture\\FixtureCode.cs",
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
dotnet run --no-launch-profile --project navlyn -- type-hierarchy --workspace tests\fixtures\SymbolNavigationFixture\SymbolNavigationFixture.csproj --file tests\fixtures\SymbolNavigationFixture\FixtureCode.cs --line 50 --column 18
```

Required options:

- `--workspace <path>`: `.slnx`, `.sln`, or `.csproj`.
- `--file <path>`: C# source file in the workspace.
- `--line <number>`: 1-based source line.
- `--column <number>`: 1-based source column.

Optional options:

- `--project <project>`: resolves the source file in the context of an exact project name or repository-relative `.csproj` path.
- `--exclude-generated`: rejects generated input files and excludes generated source hierarchy locations.

Result shape:

```json
{
  "file": "tests\\fixtures\\SymbolNavigationFixture\\FixtureCode.cs",
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
      "path": "tests\\fixtures\\SymbolNavigationFixture\\FixtureCode.cs",
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
dotnet run --no-launch-profile --project navlyn -- callers --workspace navlyn.slnx --file navlyn\Cli\Commands\CheckCommand.cs --line 8 --column 27
```

Required options:

- `--workspace <path>`: `.slnx`, `.sln`, or `.csproj`.
- `--file <path>`: C# source file in the workspace.
- `--line <number>`: 1-based source line.
- `--column <number>`: 1-based source column.

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
  "file": "navlyn\\Cli\\Commands\\CheckCommand.cs",
  "line": 8,
  "column": 27,
  "limit": 10,
  "totalGroups": 1,
  "symbol": {
    "name": "Create",
    "kind": "Method",
    "container": "Navlyn.Cli.Commands.CheckCommand",
    "path": "navlyn\\Cli\\Commands\\CheckCommand.cs",
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
        "path": "navlyn\\Cli\\NavlynCli.cs",
        "line": 28,
        "column": 32,
        "endLine": 28,
        "endColumn": 49
      },
      "locations": [
        {
          "path": "navlyn\\Cli\\NavlynCli.cs",
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
dotnet run --no-launch-profile --project navlyn -- calls --workspace navlyn.slnx --file navlyn\Cli\NavlynCli.cs --line 28 --column 32
```

Required options:

- `--workspace <path>`: `.slnx`, `.sln`, or `.csproj`.
- `--file <path>`: C# source file in the workspace.
- `--line <number>`: 1-based source line.
- `--column <number>`: 1-based source column.

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
  "file": "navlyn\\Cli\\NavlynCli.cs",
  "line": 28,
  "column": 32,
  "limit": 10,
  "totalGroups": 1,
  "caller": {
    "name": "CreateRootCommand",
    "kind": "Method",
    "container": "Navlyn.Cli.NavlynCli",
    "path": "navlyn\\Cli\\NavlynCli.cs",
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
        "path": "navlyn\\Cli\\Commands\\CheckCommand.cs",
        "line": 8,
        "column": 27,
        "endLine": 8,
        "endColumn": 33
      },
      "locations": [
        {
          "path": "navlyn\\Cli\\NavlynCli.cs",
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
dotnet run --no-launch-profile --project navlyn -- definition --workspace navlyn.slnx --file navlyn\Cli\NavlynCli.cs --line 31 --column 37 --project navlyn
```

Required options:

- `--workspace <path>`: `.slnx`, `.sln`, or `.csproj`.
- `--file <path>`: C# source file in the workspace.
- `--line <number>`: 1-based source line.
- `--column <number>`: 1-based source column.

Optional options:

- `--project <project>`: resolves the source file in the context of an exact project name or repository-relative `.csproj` path. Project names are case-sensitive.
- `--exclude-generated`: rejects generated input files and excludes generated definition locations.
- `--include-metadata`: returns metadata-only symbol facts instead of `NAVLYN1305` when the selected symbol has no source definition. Source definition locations are still reported only in `definitions`.

Result shape:

```json
{
  "file": "navlyn\\Cli\\NavlynCli.cs",
  "line": 31,
  "column": 37,
  "project": {
    "filter": "navlyn",
    "name": "navlyn",
    "path": "navlyn\\navlyn.csproj"
  },
  "symbol": {
    "name": "CheckCommand",
    "kind": "NamedType",
    "container": "Navlyn.Cli.Commands"
  },
  "definitions": [
    {
      "path": "navlyn\\Cli\\Commands\\CheckCommand.cs",
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
dotnet run --no-launch-profile --project navlyn -- references --workspace navlyn.slnx --file navlyn\Cli\NavlynCli.cs --line 31 --column 37 --project navlyn
```

Required options:

- `--workspace <path>`: `.slnx`, `.sln`, or `.csproj`.
- `--file <path>`: C# source file in the workspace.
- `--line <number>`: 1-based source line.
- `--column <number>`: 1-based source column.

Optional options:

- `--project <project>`: resolves the source file in the context of an exact project name or repository-relative `.csproj` path. Project names are case-sensitive.
- `--exclude-generated`: rejects generated input files and excludes generated reference locations.
- `--result-project <project>`: filters result-side reference locations by project. This is separate from input `--project`.
- `--result-path <path-fragment>`: filters result-side reference paths by repository-relative path fragment. Can be specified more than once.
- `--result-kind <kind>`: filters by the referenced symbol kind. Can be specified more than once.
- `--limit <number>`: limits the number of reference locations returned. `totalMatches` reports the location count before truncation.

Result shape:

```json
{
  "file": "navlyn\\Cli\\NavlynCli.cs",
  "line": 31,
  "column": 37,
  "limit": 10,
  "totalMatches": 1,
  "project": {
    "filter": "navlyn",
    "name": "navlyn",
    "path": "navlyn\\navlyn.csproj"
  },
  "symbol": {
    "name": "CheckCommand",
    "kind": "NamedType",
    "container": "Navlyn.Cli.Commands"
  },
  "references": [
    {
      "path": "navlyn\\Cli\\NavlynCli.cs",
      "line": 31,
      "column": 37,
      "endLine": 31,
      "endColumn": 49,
      "containingSymbol": {
        "name": "CreateRootCommand",
        "kind": "Method",
        "container": "Navlyn.Cli.NavlynCli",
        "path": "navlyn\\Cli\\NavlynCli.cs",
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

# Navlyn Workspace Configuration

Read this page only when a direct `.slnx`, `.sln`, `.csproj`, or `.vbproj` path is not enough. The normal setup is in the [README](../README.md#workspace-choice).

`navlyn.workspace.json` makes the workspace choice explicit for repositories with several possible solutions or projects. It is a Navlyn configuration file, not the workspace itself.

## Start With One Choice

Most repositories that need this file need only this:

```json
{
  "primaryWorkspace": "YourRepo.sln"
}
```

Paths are relative to `navlyn.workspace.json`. `primaryWorkspace` wins over every discovery setting.

## Let Navlyn Choose A Candidate

Omit `primaryWorkspace` only when Navlyn should choose from a limited set of directories:

```json
{
  "workspaceCandidates": ["src", "tools"],
  "excludes": ["artifacts", "bin", "obj"],
  "generatedFolders": ["src/Generated"],
  "tests": {
    "include": false
  }
}
```

Each directory is searched only at its top level. If the best candidates are still ambiguous, Navlyn stops and asks you to set `primaryWorkspace` or pass `--workspace` directly.

`excludes`, `generatedFolders`, and `tests.include` affect this candidate choice only. They do not filter source files after the selected solution/project has loaded.

## Reference

| Field | Default | Use it for |
| --- | --- | --- |
| `$schema` | none | Optional editor schema reference. It does not change Navlyn behavior. |
| `primaryWorkspace` | none | The one `.code-workspace`, `.slnx`, `.sln`, `.csproj`, or `.vbproj` to load. |
| `workspaceCandidates` | `["."]` | Files or directories to search when there is no `primaryWorkspace`. |
| `excludes` | `[]` | Candidate paths to skip. Prefixes and simple wildcard patterns are supported. |
| `generatedFolders` | `[]` | Candidate paths to skip because they contain generated projects. |
| `tests.include` | `true` | Set `false` to skip test-like project candidates during discovery. |
| `tests.projects` | `[]` | Reserved. It is accepted but does not currently change candidate selection. |
| `defaultRootPolicy` | CLI: `all`; MCP: `repo-relative` | Limit whether the configuration can select a workspace outside the repository root. |
| `allowRoots` | `[]` | Extra roots allowed when `defaultRootPolicy` is `allow-listed`. |
| `cacheHints.enabled` | `false` | Let `--cache auto` use Navlyn's lightweight workspace cache manifest. |
| `cacheHints.directory` | `.navlyn/cache` | Directory for that cache manifest. |

`--workspace-root-policy` overrides `defaultRootPolicy` for one command. Use `allow-listed` with `allowRoots` when a deliberate external workspace is needed; use `all` only when broad access is intentional.

The JSON schema is [navlyn-workspace.schema.json](schemas/navlyn-workspace.schema.json). It is useful for editor completion; this page describes when each setting should be used.

## `auto`

`--workspace auto` is the no-file alternative for CLI commands. MCP startup uses this behavior by default when `--workspace` is omitted. From the repository root, Navlyn considers one top-level candidate in this order: `navlyn.workspace.json`, `.code-workspace`, `.slnx`, `.sln`, `.csproj`, then `.vbproj`. Use it when that choice is unambiguous; Navlyn fails instead of guessing when multiple best-priority candidates exist.

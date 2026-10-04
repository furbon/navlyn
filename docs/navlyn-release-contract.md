# Navlyn Release and Delivery Contract

This document defines the 0.9.2 delivery contract and retains the 0.8.0 compatibility evidence. Passing a local check establishes only that check; public availability and client support require separate observed evidence.

## Release identity and package metadata

The release identity is `0.9.2` across `Version`, `PackageVersion`, release notes, documentation, installed outputs, release manifests, and resolved `InformationalVersion`. `AssemblyVersion` and `FileVersion` are numeric `0.9.2.0`. Both packages target only `net10.0`, and keep their IDs and command names as `navlyn` / `navlyn` and `navlyn-mcp` / `navlyn-mcp`.

Both packages must agree on authorship (`furbon.tech`), MIT license expression, repository URL and type (`https://github.com/furbon/navlyn`, `git`), project URL, copyright, README, icon, and target frameworks. Package inspection must establish that the README and icon are included, required metadata is present, and no build intermediates, credentials, machine paths, source archives, or unrelated files are present. A release contract check must fail on identity or metadata drift before package generation is accepted.

The 0.9.2 release defaults to four focused MCP tools and compact responses. Existing consumers requiring the previous 25-tool inventory and full responses must specify `--surface full`; CLI output remains complete. Source selection fixes are documented in the CLI reference. The earlier 0.7.0-to-0.8.0 migration changed `tools/list` from 33 tools to 25: 16 names retired and eight new names appeared. See the [MCP migration table](navlyn-mcp-server.md#v070-to-v080-tool-migration) and current schemas before reusing saved calls. Among retained names, `navlyn_verify_edit` gained optional symbol-selection inputs; `navlyn_target.mode` and `navlyn_read.externalSource` were also added in 0.8.0.

## Codex routing skill source and lifecycle

The canonical three-file source is `.agents/skills/navlyn-semantic-routing/`:

- `SKILL.md`
- `references/routing-matrix.md`
- `references/evidence-boundaries.md`

The supported Codex destinations are a repository's `.agents/skills/navlyn-semantic-routing/` or the user skill directory `$HOME/.agents/skills/navlyn-semantic-routing/`. Codex discovers repository skills from the current directory up through its repository root and user skills from its user skill root; discovery must be demonstrated in a fresh client process or with an officially supported equivalent. An isolated lifecycle test must set an isolated home and use a disposable repository so that no real user directory is touched. On Windows, overriding home environment variables alone may not redirect Codex's native user-home lookup; the fresh app-server discovery test registers the isolated skill parent through process-scoped `skills/extraRoots/set` and checks that the returned skill has user scope and the isolated path. File presence alone is not discovery evidence.

The skill install/update contract is:

1. First install copies exactly the three source files into the selected supported destination. It stores a deterministic JSON ownership marker as a sidecar in the destination's parent, never inside the exact three-file skill directory. The stable sidecar name is `.navlyn-semantic-routing.install.json`; repository and user installs therefore keep their marker adjacent to the skill directory.
2. The marker schema contains a schema identifier/version, Navlyn version (`0.9.2` for this release), normalized absolute destination, the three canonical relative file paths and their SHA-256 hashes, plus an explicit ownership statement that the installer manages only those three files and that destination directory. The marker itself must be created or updated atomically by the installer and must not overwrite an unrelated existing file.
3. Reinstalling the identical version and bytes is idempotent and makes no content changes. If the marker is missing, malformed, has an unsupported schema/version, names another normalized destination, or its recorded ownership/content does not match the current install, installation/update and uninstall fail closed with a diagnostic; they do not infer ownership from file names or silently recreate the marker.
4. Updating files owned by the prior managed version is allowed only when the valid marker identifies that destination and all three installed-file hashes match its recorded hashes. A controlled older fixture must prove this upgrade path. A missing or tampered managed file is a conflict and blocks update.
5. Divergent destination files are user-owned conflicts. Update fails closed, preserves every divergent byte, and explains how to resolve the conflict. No implicit force behavior is allowed.
6. Uninstall removes only files that still match the valid marker's recorded content. Missing or modified managed files, an invalid/missing marker, or unrelated entries inside the skill directory are conflicts; uninstall preserves the affected state and reports the conflict. After removing the three matching files, the installer may remove the now-empty skill directory, then remove only its valid sidecar marker. It must preserve modified, missing, or unrelated user files and never recursively remove the skill directory.
7. Any forced replacement, if ever added, requires a separate explicit option and its own preservation test. It is outside this locked default contract.

The isolated test home, installed inventory, compatibility marker, and discovery result must be captured as sanitized evidence. Manual installation fallback instructions must name the chosen destination and the same conflict-preservation behavior.

## Isolated consumer root and cleanup

Consumer installation tests use a unique, task-owned absolute `consumer-root` with these sibling directories:

```text
consumer-root/
  nuget-source/
  tools-cli-only/
  tools-mcp-only/
  tools-combined/
  consumer-workspace/
  isolated-home/
  transient-reports/
```

The consumer workspace must be unrelated to the Navlyn repository. Tests install with `dotnet tool install --tool-path`; they never use global tool installation. Installed commands run from `consumer-workspace` and must not resolve assemblies or content from the repository's `bin/` or `obj/` directories. CLI-only, MCP-only, and combined installations are separate cases.

Before cleanup, resolve every cleanup path to an absolute normalized path and prove it is beneath the validated task-owned `consumer-root`. Reject parent paths, reparse-point escapes, and any path outside that root. Recursively delete only task-created child paths beneath the root. Remove `consumer-root` itself only as a final nonrecursive directory removal, after revalidating its normalized identity and task ownership, confirming it is not a reparse point, and confirming it is empty. Durable sanitized reports and first-failure evidence are copied to a separate validated ignored evidence directory and hashed before the disposable root is removed. Cleanup runs after success and failure; a keep-artifacts option may preserve the disposable root for diagnosis.

The release-contract harness rejects project-local `Version`, `PackageVersion`, `AssemblyVersion`, `FileVersion`, or `InformationalVersion` declarations in either tool project. Phase 2 must additionally inspect evaluated MSBuild/package values to catch imported or command-line overrides and prove that the emitted assembly versions remain numeric.

## Client support claims

Use only these support states. The observed client runs below are historical 0.8.0 evidence; any 0.9.2 client claim needs its own installed-package call.

| Client or surface | Contract state | Claim boundary |
| --- | --- | --- |
| Codex routing skill | `verified` for the tested Codex version, OS, and execution mode after isolated install, fresh-process discovery, and live activation | Codex CLI 0.155.0-alpha.16 on Windows passed an installed six-case activation smoke in a process-scoped full-access session with no write attempts or source diff. WindowsApps PowerShell could not launch under the tested read-only sandbox; do not generalize this smoke to that sandbox mode. |
| GitHub Copilot CLI MCP | `verified` for the tested Windows client and package only after an installed-server semantic call is observed | Copilot CLI 1.0.88 on Windows called `navlyn_target` from isolated `navlyn-mcp` 0.8.0 and returned the consumer symbol; this does not claim Codex skill support in Copilot. |
| VS Code built-in Copilot MCP | `verified` for the tested Windows client and package | VS Code 1.139.1 Copilot Chat called `navlyn_target` on installed `navlyn-mcp` 0.8.0 and returned `Navlyn.Mcp.Execution.NavlynMcpWorkspaceCache` from the current workspace. The original workspace MCP configuration was restored after the test. |
| MCP stdio clients | `documented` unless an identified client/version passes the installed-server protocol harness; then `verified` only for that pair | Configuration examples do not establish client verification. |
| Unexercised MCP client | `unsupported` as a support claim | Do not imply tested compatibility from MCP protocol compatibility alone. |
| VS Code extension or Marketplace | `deferred` | No extension package or Marketplace listing is part of this release. |

`verified` means exercised by a current automated or manual harness and accompanied by reproducible evidence. `documented` means setup/configuration guidance exists but the client was not exercised for this release. `unsupported` means compatibility is not claimed. `deferred` means the surface is intentionally outside the release. A configuration snippet can never, by itself, promote a client to `verified`.

The [v0.8.0 public repository evaluation](evals/v0.8.0-public-oss.md) records seven semantic passes and two classified no-go cases from nine predeclared repositories, with the original warm MCP performance limits applied to the supported cases.

## Delivery lanes

| Lane | Allowed purpose and evidence | Explicit boundary |
| --- | --- | --- |
| Pull request | Deterministic, credential-free restore/build/test and focused release, package, skill, and documentation contract checks; bounded local-feed smoke may run on the primary OS. | Ordinary pushes and pull requests cannot publish. |
| Release candidate | Exact-main Windows validation; one installed consumer smoke, package inspection, checksums, and retained publication inputs. Reuse the tested build; run client/skill checks only when those inputs change. | Candidate evidence must match the reviewed source before publication. |
| Live evaluation | Explicit manual run naming client, version, model, reasoning effort, scenarios, and evidence destination; raw traces remain private or ignored unless deliberately curated. | Never a silent or required unauthenticated PR check; never expose credentials. |
| Ubuntu publish rehearsal | Manual workflow on the reviewed `main` commit; source and package contracts, pack, dry run, and retained package bytes without NuGet credentials. | A rehearsal does not publish or satisfy protected environment review. |
| Publication | Separate manual, protected workflow on reviewed `main`, reusing authenticated package/setup inputs from successful exact-main CI, with pre-push byte retention, Trusted Publishing, and the required environment review. | NuGet package versions are immutable; tag verification checks successful source validation and actual public package content. |

Release validation uses isolated tool paths and client profiles, preserving existing user installations. Repository rules, environment protection, and NuGet account policy remain in force; published package bytes are never replaced.

## Live-evaluation invalidation

| Changed input | Required replacement evidence |
| --- | --- |
| Package metadata or CI only | Re-score the current authoritative trace and rerun installed-skill discovery smoke. |
| Skill installation path only, with skill bytes unchanged | Rerun installed-skill discovery and bounded semantic-activation / text-only controls. |
| Skill text, MCP descriptions, tool schema, scenarios, or scorer | Run a fresh paired live corpus. |
| Client version or model-matrix claim | Run fresh evidence for that exact client/model pair. |

Each live record identifies the client and version, model and reasoning effort, scenario hash, skill hash, MCP/server identity, paired run, and scorer version. Live evaluations are manual or credential-gated. Never reuse invalidated evidence, tune the threshold or remove scenarios to pass an individual failure, or describe stale evidence as current.

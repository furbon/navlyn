# Changelog

All notable public release changes for Navlyn are tracked here.

## 0.9.0 - 2026-10-04

- Require a .NET 10 SDK; publish only net10.0 CLI/MCP assets. Remove .NET 8 targets, installer fallback, SDK CI setup, and compatibility package checks.
- Reuse the shared MCP workspace for target variants and focused navigation while preserving CLI validation, selected-symbol behavior, and content-sensitive freshness.
- Add explicit `--target-framework` / MCP `targetFramework` and batch defaults/per-request selection; enforce it for source positions and cached candidate IDs.
- Upgrade ModelContextProtocol to 2.0.0; retain actual legacy stdio and discovery-first clients, tool names, and structured envelopes.
- Compact JSON text and nullable schemas. Correct advertised output-schema required fields to match the serializer's omitted nulls, and accept null sourceCommand in the shared error schema.
- Prune generated/build trees before repository metadata discovery, reuse the scan, and fix sibling-directory prefix matching.
- Keep the reduced validation pipeline; remove repeated publication recovery tests already run on tested source.
- Record small fixed live-task and external-package corpus evidence, including failures and cases where Navlyn costs more than ordinary file reading.

## 0.8.7 - 2026-10-04

- Stop and reap external MCP child processes on deadlines, caller cancellation, and blocked batch input. Bound retained stdout/stderr while draining both streams.
- Replace repeated repository loads, synthetic evaluation framework tests, duplicate runtime suites, and full release-script campaigns with one .NET 10 product suite and small portable CLI/MCP checks.
- Reuse external-source fixture setup and warm reader sessions; preserve mutation/isolation assertions.
- Use a shared process harness with argument-safe invocation and deadlines across fixture scripts. Fix Release packaging to validate the configuration it packs.
- Pin workflow Actions to full commit SHAs. Retain stage logs, timing, TRX, and exact tested publication inputs.
- Measure repeated MCP calls within one persistent session, with cold/warm and comparable version/environment metadata.
- Plan full .NET 10 migration in 0.9.0; existing .NET 8 package assets receive no additional compatibility lane.

## 0.8.6

- Enforce configured MCP deadlines across direct tools, adapters, and queue waits; distinguish caller cancellation and wait for resource cleanup.
- Preserve complete workflow reproduction arguments, working directory, and batch stdin; verify deterministic replay.
- Propagate schema-wrapper build/test failures and protect historical documentation during patch changes and preview promotion.
- Extend natural-kind parity coverage across CLI, MCP, and batch, with canonical kinds and invalid-input checks.
- Repair MCP performance smoke to use current tool names and a method source position; save failed measurements and propagate failure instead of reporting a successful run.
- Retain CI TRX, command logs, and stage timings; expose per-package publication progress and exact retained-artifact recovery inputs in Actions summaries.
- Clarify trusted MSBuild loading, root-policy boundaries, automatic freshness, historical evidence, and .NET 8/10 runtime support.
- Update System.CommandLine, Microsoft.NET.Test.Sdk, Microsoft.Extensions.Hosting, .NET 10 StringTools, and setup-dotnet while retaining .NET 8 MSBuild compatibility and MCP SDK 1.x.

## 0.8.5

- Accept case-insensitive symbol kinds and natural type aliases such as `class`, `interface`, and `record`, with canonical JSON kinds across CLI, MCP, and batch operations.
- Derive release identity, assembly versions, package notes, and script expectations from the shared version, with one command to update current installation examples.
- Reuse packages validated on the exact merged-main commit in protected publication and annotated-tag verification, avoiding repeated source tests and release packing.

## 0.8.4

- Explain valid Roslyn symbol kinds in CLI/MCP guidance and invalid-kind errors, including `NamedType` for classes and interfaces.
- Fix first MCP loads of explicit workspaces being rejected because unrelated artifact directories contain links; freshness checks still track loaded projects and source.
- Wait up to ten minutes for NuGet indexing in the existing publication attempt, retaining exact-package verification and recovery.
- Prevent isolated setup and consumer checks from adding temporary .NET tool paths to the user's persistent PATH, and reuse the release build in package smoke.

## 0.8.3

- Ambiguous `target` results now offer an exact `candidateId` selection action for each visible candidate instead of suggesting a broader search.
- `navlyn-mcp --version` prints the installed server version without opening an MCP session.

## 0.8.2

- Corrected selected-source paths when commands run from a nested workspace directory and synchronized first-time MSBuild registration.
- Added a standalone VS Code setup bundle with an explicit plan, isolated or global installation, connection checks, and ownership-aware update, undo, and removal.
- Added retained-artifact publication recovery with per-package verification and fail-closed recovery of uncertain publication attempts.


## 0.8.1

- Reorganized the English and Japanese README files around direct paths to terminal, VS Code, GitHub Copilot CLI, Codex, and Claude Code setup.
- Added paired first-run, client setup, and Codex routing-skill guides, with installation, connection checks, and removal steps.
- Rewrote Japanese workspace guidance in natural Japanese and aligned the English entry points.
- Kept the read-only CLI and 25-tool MCP contracts unchanged from 0.8.0.

## 0.8.0

- Added opt-in metadata and decompiled reads for exact external library members, including verified NuGet `ref`/implementation and RID assets. Results identify the selected PE and keep reconstructed C# distinct from original source.
- Strengthened long-running MCP workspace freshness, generation publication, and source, reference assembly, analyzer, and restore-assets input tracking while keeping stale reads fail-closed.
- Consolidated the MCP surface to 25 read-only tools. See the [migration table](docs/navlyn-mcp-server.md#stable-tool-surface) for retired names and canonical replacements.
- Updated clean-install, client setup, package identity, three-OS CI, and protected NuGet publication checks for the public release.

## 0.8.0-preview.1 - 2026-09-26

Unpublished preview identity for the v0.8 release-readiness rehearsal. `0.7.0` remains the public NuGet release. Preview packages are produced only in local or CI rehearsal artifacts and are not claimed to be available from nuget.org. The CLI and MCP packages retain their read-only semantic contracts. The MCP surface is consolidated to 25 tools; see [the migration table](docs/navlyn-mcp-server.md#stable-tool-surface) for retired names and canonical starting points. This entry records rehearsal changes and does not announce a public release.

## 0.7.0 - 2026-07-12

Public release preparation for Navlyn as a read-only semantic evidence layer for C#/.NET coding agents.

- Centralized version identity across the CLI, MCP server, shared assemblies, package metadata, workflow envelopes, and workspace status facts.
- Updated first-run README and setup guidance around the shortest path from install to `doctor`, `resolve-target`, selected-source evidence, edit preflight, and post-edit diff review.
- Expanded release-readiness documentation for reproducible demos, fixture-backed case studies, discovery channels, schema/contract policy, freshness handling, and MCP setup boundaries.
- Kept the public contract source-level, bounded, facts-only, and read-only. Navlyn still does not edit files, run tests, upload source, scan security issues, or prove runtime behavior.

## 0.6.0 - 2026-07-05

Release candidate for agent preflight onboarding, wrong-symbol edit evidence loops, performance baselines, and release identity.

- Added a first-10-minutes onboarding guide that walks from install to `check`, `resolve-target`, selected-symbol evidence, MCP reader setup, and post-edit diff verification.
- Added positioning guidance that explains Navlyn as read-only C# semantic evidence before edit, distinct from `rg`, LSP, Roslyn analyzers, generic MCP search, editing MCP servers, CI review bots, and hosted code search.
- Added agent evidence eval guidance for wrong-symbol avoidance, pre-edit anchors, post-edit changed-symbol checks, tool-call count, JSON validity, stderr cleanliness, latency, output size, and expected-file presence.
- Refreshed README and README_ja around the three practical agent workflows: anchor the intended symbol, build only the needed reading queue, and verify the actual diff after editing.
- Changed MCP startup to expose one stable read-only tool surface by default. Old `--tool-profile reader|review|edit|full` values remain accepted as deprecated compatibility aliases and no longer hide tools.
- Stabilized multi-target xUnit execution by disabling target-framework parallelism for the test project, avoiding MSBuild workspace load conflicts during standard `dotnet test navlyn.slnx --no-build` validation.
- Updated performance documentation with a 0.6.0 local baseline, performance acceptance criteria, and guidance for MCP, direct CLI, diff, batch, daemon, cache, parallel, and multi-workspace smoke checks.
- Synchronized package versions, install examples, client setup, architecture/distribution/discovery docs, MCP test client metadata, package smoke metadata, and package release notes for `navlyn` and `navlyn-mcp` 0.6.0.

## 0.5.0 - 2026-07-04

Release candidate for agent-facing performance, workspace, contract, runtime, and OSS readiness.

- Added startup-fixed MCP tool profiles (`reader`, `review`, `edit`, `full`) with profile-gated tool discovery, need-triggered descriptions, and deterministic blocked-call envelopes.
- Added MCP file-first and selected-symbol tools for `navlyn_file_outline`, `navlyn_symbol_source`, `navlyn_symbol_edges`, `navlyn_inspect_file`, and `navlyn_workspace_status` / `navlyn_workspace_refresh`.
- Added direct warm-path MCP execution for selected reader tools with session-local workspace reuse, workspace fingerprint metadata, snapshot id, freshness status, cache status, index status, document-index sizing, and cost class.
- Added `navlyn.workspace.json`, `.code-workspace` loading, `--workspace auto`, repository root policy controls, and diagnostics for invalid, empty, ambiguous, or external-root workspace files.
- Added workspace status/refresh CLI commands, an opt-in local read-only `navlyn serve` daemon for status/refresh requests, and an opt-in lightweight on-disk workspace index manifest that stores no source text.
- Added declaration indexing, same-snapshot candidate record reuse, fuzzy semantic enrichment caching, and scoped/budgeted reverse-edge search metadata for `references`, `callers`, `about`, and `impact`.
- Added focused automation schemas and golden snapshots for MCP profiles/envelopes, target selection, file-first output, workspace status/cache, and scoped search metadata.
- Added automated tool-selection evals and expanded performance measurement for quick, file-first, agent-loop, MCP warm-loop, daemon, on-disk cache, parallel same-workspace agents, and multi-workspace scenarios.
- Added .NET 8 and .NET 10 test lanes, tool assets, CI validation, package install smoke coverage, release pack validation, and publish dry-run support for `navlyn` and `navlyn-mcp`.
- Refreshed README, README_ja, demos, client setup, agent recipes, distribution, discovery, performance, architecture, known limits, issue templates, package metadata, and release identity for the synchronized `0.5.0` release.

## 0.4.0 - 2026-06-29

Release candidate for standalone MCP execution and shared engine packaging.

- Split the Roslyn/MSBuild resolver implementation into `Navlyn.Core` and the reusable command-line frontend into `Navlyn.CommandLine`, with both tool packages sharing that implementation.
- Changed `navlyn-mcp` to run Navlyn commands in-process by default, so MCP users only need to install `navlyn-mcp`.
- Kept `--navlyn-executable` as an explicit legacy external CLI escape hatch for compatibility, debugging, and local development.
- Preserved MCP `sourceCommand` as the logical Navlyn command behind a tool/resource result, even when no CLI process is launched.
- Added regression coverage for standalone MCP execution, legacy option parsing, result envelope compatibility, package install shapes, and architecture guardrails.
- Updated package metadata, install examples, performance guidance, distribution docs, and release notes for the synchronized `navlyn` and `navlyn-mcp` `0.4.0` release.

## 0.3.0 - 2026-06-28

Release candidate for synchronized CLI/MCP agent-readiness hardening.

- Tightened source-position `--project` handling for `tests-for-symbol`, dependency-injection subject commands, and application-domain subject commands so Roslyn resolution uses the requested project context.
- Aligned MCP command-builder validation with CLI source-position and diff-mode semantics for agent-facing calls.
- Added MCP command-builder regression coverage for invalid source-position fuzzy options, multiple source-position project filters, and diff-mode fuzzy selection options.
- Updated package metadata, install examples, distribution docs, local tool manifest examples, smoke scripts, and release notes for the synchronized `navlyn` and `navlyn-mcp` `0.3.0` release.

## 0.2.0 - 2026-06-28

Release candidate for resolve-target anchored agent workflows.

- Added the `resolve-target` CLI command for stable symbol target selection from queries, candidate IDs, and source positions.
- Added `resolve-target` support to `batch` and exposed `navlyn_resolve_target` through the MCP server.
- Updated agent recipes, MCP prompts, installation examples, demo walkthroughs, JSON compatibility notes, and performance guidance around candidate-ID based workflows.
- Updated package metadata, tags, release notes, and discovery-channel guidance for the synchronized `navlyn` and `navlyn-mcp` release.

## 0.1.0 - 2026-06-28

Initial public release candidate.

- Added the `navlyn` .NET tool for deterministic C#/.NET semantic navigation and investigation.
- Added the `navlyn-mcp` .NET tool for a read-only stdio MCP server over Navlyn facts.
- Added agent-oriented fuzzy discovery, exact navigation, context packs, review facts, related tests, dependency injection facts, public API diffing, and .NET application-domain source facts.
- Added release validation, package smoke testing, release packing, dry-run NuGet publish scripting, and public readiness auditing.

Navlyn reports bounded source-level facts. It is not a runtime proof engine, security scanner, or replacement for tests.

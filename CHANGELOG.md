# Changelog

All notable public release changes for Navlyn are tracked here.

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

# Changelog

All notable public release changes for Navlyn are tracked here.

## 0.5.0 - 2026-07-04

Release candidate for agent-facing performance, workspace, contract, and OSS readiness.

- Added MCP file-first tools for `navlyn_file_outline`, `navlyn_symbol_source`, `navlyn_symbol_edges`, and `navlyn_inspect_file`, with outline `candidateId` flow into source lookup.
- Added direct warm-path MCP execution for selected file-first tools with session-local workspace reuse, workspace fingerprint metadata, snapshot id, cache status, index status, and cost class.
- Added .NET 8 and .NET 10 tool assets for `navlyn` and `navlyn-mcp`.
- Added `.code-workspace` loading, `--workspace auto` support for clear top-level workspace candidates, and diagnostics for invalid, empty, ambiguous, or external-root workspace files.
- Hardened automation-facing contracts with workflow and MCP envelope schemas plus representative golden output snapshots.
- Refreshed README, demos, client setup, tool-selection eval guidance, performance docs, known limits, issue templates, package metadata, and release identity for the synchronized `0.5.0` release.

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

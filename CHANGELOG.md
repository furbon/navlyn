# Changelog

All notable public release changes for Navlyn are tracked here.

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

# Navlyn JSON Schemas

This directory contains envelope-first schemas plus focused schemas for high-risk automation-facing Navlyn JSON:

- `navlyn-workflow-envelope.schema.json`: shared `schemaVersion: "navlyn.workflow.v1"` CLI workflow profile envelope.
- `navlyn-mcp-tool-result.schema.json`: shared MCP structured-content tool result envelope.
- `navlyn-workspace.schema.json`: repository-local `navlyn.workspace.json` discovery and root-policy configuration. See [workspace configuration](../navlyn-workspace.md) before using it.
- `navlyn-resolve-target-result.schema.json`: high-use `resolve-target` target-selection result shape.
- `navlyn-file-outline-result.schema.json`: file-first `outline` / MCP file outline entry shape with reusable `candidateId` values.
- `navlyn-symbol-source-result.schema.json`: bounded `symbol-source` / MCP symbol source result shape.
- `navlyn-workspace-status-result.schema.json`: `workspace-status` and `workspace-refresh` result shape, including snapshot and cache freshness fields.
- `navlyn-symbol-search-metadata.schema.json`: `search` metadata emitted by scoped reverse-edge operations such as `references` and `callers`.
- `navlyn-edit-preflight-result.schema.json`: `edit-preflight` / MCP edit-preflight anchor, evidence, confidence, and next-guard envelope.
- `navlyn-agent-guard-result.schema.json`: `post-edit-guard` and `wrong-symbol-guard` result shape, including risk, match scores, and policy fields.

The schemas intentionally focus on automation-critical envelopes and high-risk command results rather than every command-specific domain object. Those facts are documented in `docs/navlyn-cli-commands.md` and guarded by focused resolver tests, CLI contract tests, and representative golden snapshots. When a command adds automation-critical top-level fields or freshness/cost metadata, add or update a focused schema here and cover it from `navlyn.Tests/Contracts`.

Run `./scripts/test-contract-schemas.ps1 -NoBuild` after editing schema files, golden snapshots, MCP tool surface membership, or public automation-facing result shapes.

Compatibility is additive within the same major envelope schema: clients should parse named fields and ignore unknown properties.

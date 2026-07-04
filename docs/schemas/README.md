# Navlyn JSON Schemas

This directory contains envelope-first schemas for automation-facing Navlyn JSON:

- `navlyn-workflow-envelope.schema.json`: shared `schemaVersion: "navlyn.workflow.v1"` CLI workflow profile envelope.
- `navlyn-mcp-tool-result.schema.json`: shared MCP structured-content tool result envelope.

The schemas intentionally do not enumerate every command-specific domain object. Those facts are documented in `docs/navlyn-cli-commands.md` and guarded by focused resolver tests, CLI contract tests, and representative golden snapshots.

Compatibility is additive within the same major envelope schema: clients should parse named fields and ignore unknown properties.

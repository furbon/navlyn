## Summary

## Validation

- Record the focused tests and manual command checks relevant to this change.
- Run `./scripts/test-quick.ps1` once for an integrated code change; reuse its build/tests in subsequent checks.
- Run `./scripts/test-release.ps1 -NoBuild -SkipDotnetTest` for release or package changes after product tests pass.
- Use one relevant fixture script when command wiring or workspace binding changes. Do not run every script for each change.

## Contract And Docs

- Public CLI/MCP behavior is reflected in the corresponding command documentation.
- stdout/stderr, exit codes, selection, freshness, and installation were considered where relevant.
- No generated packages, build output, secrets, or local reports are included.

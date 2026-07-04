## Summary

## Validation

- [ ] `dotnet restore navlyn.slnx`
- [ ] `dotnet build navlyn.slnx`
- [ ] `dotnet test navlyn.slnx --no-build`
- [ ] `./scripts/test-quick.ps1 -NoBuild -SkipDotnetTest`
- [ ] `./scripts/test-cli-contract.ps1 -NoBuild -Suite core` if CLI/MCP contract changed
- [ ] `./scripts/test-cli-contract.ps1 -NoBuild -Suite all` if broad workflow/domain contract changed
- [ ] `./scripts/test-release.ps1` if release/package behavior changed

## Contract And Docs

- [ ] Public CLI behavior is reflected in `docs/navlyn-cli-commands.md`
- [ ] Public MCP behavior is reflected in `docs/navlyn-mcp-server.md`
- [ ] stdout/stderr and exit-code behavior were considered
- [ ] No generated packages, build output, secrets, or local reports are included

# Contributing

Thanks for helping make Navlyn better.

Navlyn is a C#/.NET CLI and MCP server for repository-local semantic investigation. Keep changes small, verifiable, and consistent with the public CLI contract in `docs/navlyn-cli-commands.md`.

## Development

Use .NET 10 and PowerShell Core-compatible scripts.

```powershell
dotnet restore navlyn.slnx
dotnet build navlyn.slnx
dotnet test navlyn.slnx --no-build
./scripts/test-quick.ps1 -NoBuild
```

For CLI contract changes, also run:

```powershell
./scripts/test-cli-contract.ps1 -NoBuild
```

For release preparation:

```powershell
./scripts/test-release.ps1
```

More workflow detail lives in `docs/navlyn-development-workflow.md`.

## Pull Requests

- Preserve stdout for successful JSON command results.
- Send diagnostics, progress, and errors to stderr.
- Keep JSON changes additive unless a breaking change has been explicitly planned.
- Update public docs when implemented CLI or MCP behavior changes.
- Do not commit generated packages, build output, local performance reports, secrets, or `.docs/` planning files.

## Boundaries

Navlyn provides deterministic source-level facts. It does not execute tests through MCP, edit files, run arbitrary shell commands, prove runtime behavior, inspect secrets, or act as a security scanner.

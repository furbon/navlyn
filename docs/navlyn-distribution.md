# Navlyn Distribution

This document describes the release packaging workflow for Navlyn.

Navlyn is distributed as two separate .NET tool packages:

- `navlyn`: the CLI for semantic navigation and investigation.
- `navlyn-mcp`: the read-only stdio MCP server wrapper.

Keeping the packages separate lets CLI users avoid MCP dependencies and lets MCP users install a focused server command.

## User Install Shape

Published packages should behave like normal .NET tools from any configured NuGet source:

```powershell
dotnet tool install --global navlyn
dotnet tool install --global navlyn-mcp
```

Installed command names are:

- `navlyn`
- `navlyn-mcp`

## Local Package Smoke

Run a local pack/install smoke before publishing:

```powershell
./scripts/test-package-install.ps1
```

The script packs both tools, installs them from a local package source into `artifacts/package-smoke/tools`, and runs:

- `navlyn --help`
- `navlyn check --workspace navlyn.slnx`
- `navlyn repo-graph --workspace navlyn.slnx --profile compact`
- `navlyn-mcp --help`

`artifacts/` is ignored and should not be committed.

## Release Pack

Create release packages and a manifest:

```powershell
./scripts/pack-release.ps1 -Output artifacts/packages
```

By default this runs release validation before packing. Use `-NoValidation` only when validation already ran in the same environment.

## NuGet Publish

Publishing is opt-in. Dry-run is the default:

```powershell
./scripts/publish-nuget.ps1
```

To publish, set `NUGET_API_KEY` and pass `-Publish`:

```powershell
$env:NUGET_API_KEY = '<key>'
./scripts/publish-nuget.ps1 -Publish
```

The script reads `artifacts/packages/navlyn-release-pack.json` unless package paths are supplied explicitly.

## Release Checklist

- Update versions and release notes in both tool projects.
- Run `./scripts/test-release.ps1`.
- Run `./scripts/test-package-install.ps1`.
- Run `./scripts/pack-release.ps1`.
- Dry-run `./scripts/publish-nuget.ps1`.
- Publish with `-Publish` only from an intentional release environment.

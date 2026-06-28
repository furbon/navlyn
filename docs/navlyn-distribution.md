# Navlyn Distribution

This document is the release packaging and publication runbook for Navlyn.

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

## Release Identity

The first public NuGet release is `0.1.0`.

Keep `navlyn` and `navlyn-mcp` versions synchronized for the initial public releases. Both packages should use the same repository URL, license expression, README, package icon, author, and release notes discipline.

Package metadata checklist:

- `PackageId`
- `ToolCommandName`
- `Version`
- `Authors`
- `Description`
- `PackageLicenseExpression`
- `PackageReadmeFile`
- `PackageIcon`
- `PackageTags`
- `RepositoryUrl`
- `RepositoryType`
- `PackageProjectUrl`
- `PackageReleaseNotes`
- `Copyright`
- `NeutralLanguage`
- `PackageRequireLicenseAcceptance`

The package icon must be a committed PNG or JPEG included in the package. Navlyn uses `assets/navlyn-icon.png`.

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
- a minimal installed-tool MCP stdio smoke when supported by the local environment

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

Use a NuGet API key scoped as narrowly as possible for the package IDs and publish operation. Keep the key out of the repository and store it only in the protected GitHub environment or local release environment.

## GitHub Manual Publish Workflow

The repository may include a guarded manual workflow at `.github/workflows/publish-nuget.yml`.

Required repository setup before using it:

- Create a GitHub environment named `nuget-production`.
- Add required reviewers to the environment.
- Add `NUGET_API_KEY` as an environment secret.
- Keep the workflow trigger as `workflow_dispatch` only.

The workflow must run release validation before packing and publishing. Normal `push` and `pull_request` CI must never publish packages.

## GitHub Release

After packages are published and install smoke passes from NuGet:

1. Create a `v0.1.0` tag.
2. Create a GitHub Release using the `CHANGELOG.md` entry.
3. Link to the NuGet install commands.
4. Optionally attach `navlyn-release-pack.json` and package artifacts for traceability.

Do not create the public release before package smoke and dry-run publish have succeeded.

## Post-Release Smoke

After NuGet indexing completes, test installation from the public feed in a clean shell:

```powershell
dotnet tool install --global navlyn --version 0.1.0
dotnet tool install --global navlyn-mcp --version 0.1.0
navlyn --help
navlyn-mcp --help
navlyn check --workspace path/to/YourRepo.slnx
```

For local machines that already have the tools installed, use a temporary `--tool-path` instead of global install.

## Rollback / Unlist

NuGet packages are immutable after publication. If a bad package is published:

- Publish a fixed newer version when possible.
- Use NuGet unlist only for packages that should be hidden from search.
- Keep the GitHub Release notes clear about the replaced version.
- Do not rewrite tags that users may already have fetched unless the release was never announced.

## Release Checklist

- Confirm package IDs `navlyn` and `navlyn-mcp` are available or owned by the maintainer.
- Confirm repository About description and topics are set on GitHub.
- Confirm the `nuget-production` environment and `NUGET_API_KEY` secret are configured if using the manual publish workflow.
- Update versions and release notes in both tool projects.
- Update `CHANGELOG.md`.
- Run `./scripts/test-release.ps1`.
- Run `./scripts/test-package-install.ps1`.
- Run `./scripts/pack-release.ps1`.
- Dry-run `./scripts/publish-nuget.ps1`.
- Publish with `-Publish` only from an intentional release environment.
- Create the GitHub Release only after package publication and post-release smoke succeed.

Generated packages, package smoke tools, release manifests, performance reports, binlogs, and local notes belong under ignored `artifacts/` or `.docs/` paths and should not be committed.

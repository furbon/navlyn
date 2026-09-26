# Navlyn Distribution

This document explains how Navlyn is packaged, installed, validated, and published. It is useful for two audiences:

- users who want to know what gets installed and which runtime assets are included;
- maintainers who need the release packaging and publication runbook.

Discovery-channel copy, GitHub About/topics suggestions, VS Code MCP install-link notes, and registry boundaries live in [`navlyn-discovery-channels.md`](navlyn-discovery-channels.md).

Navlyn is distributed as two separate .NET tool packages:

- `navlyn`: the CLI for semantic navigation and investigation.
- `navlyn-mcp`: the standalone read-only stdio MCP server.

Keeping the packages separate lets CLI users install only `navlyn` and MCP users install only `navlyn-mcp`. The two packages share the same Navlyn core engine; `navlyn-mcp` does not require a separate `navlyn` CLI installation for normal use.

The installed tools are local .NET tools. They do not install a background service, browser extension, editor plugin, or hosted component.

Both tool packages include `net8.0` and `net10.0` assets. The .NET SDK selects the compatible tool asset during install or restore. Semantic workspace loading still requires an installed .NET SDK/MSBuild that can load the target repository.

The Windows release lane installs both .NET 8 and .NET 10 SDKs, runs the xUnit suite on both target frameworks, and tests isolated package installation with `dotnet tool install --framework net8.0` and `--framework net10.0`.

## Current Release State

`0.7.0` is the published public release. `0.8.0-preview.1` is an unpublished local release-rehearsal identity; it is not available from nuget.org and the rehearsal does not authorize publication. Historical references to `0.7.0` below describe that public release. The manual protected publish workflow remains a separate release operation.

To reproduce the preview rehearsal on Windows without changing global tools or real client settings, use .NET SDK 10 to build both target frameworks:

```powershell
$releaseOutput = "artifacts/preview-$([guid]::NewGuid().ToString('N'))"
$checks = "artifacts/preview-checks-$([guid]::NewGuid().ToString('N'))"
./scripts/test-release-contract.ps1
./scripts/pack-release.ps1 -NoValidation -Output $releaseOutput
$manifest = Join-Path $releaseOutput 'navlyn-release-pack.json'
./scripts/test-package-contract.ps1 -Manifest $manifest
New-Item -ItemType Directory -Path $checks | Out-Null
./scripts/test-consumer-install.ps1 -Manifest $manifest -OutputReport (Join-Path $PWD "$checks/consumer-install.json")
```

The consumer harness installs into isolated `--tool-path` directories and tests both target frameworks. To install manually from a local feed, use `dotnet tool install --tool-path <isolated-tool-dir> --add-source <local-feed> navlyn --version 0.8.0-preview.1` (repeat for `navlyn-mcp`). Run the installed executable by absolute path from a separate consumer workspace. Rollback can be exercised by passing an older package manifest with `-RollbackManifest`; uninstall and cleanup are included in the harness. Reports and packages are written under ignored `artifacts/` paths.

## User Install Shape

Published packages should behave like normal .NET tools from any configured NuGet source:

```powershell
dotnet tool install --global navlyn
dotnet tool install --global navlyn-mcp
```

Installed command names are:

- `navlyn`
- `navlyn-mcp`

First smoke after install:

```powershell
navlyn doctor --workspace auto
navlyn repo-graph --workspace auto --profile compact
navlyn-mcp --help
```

## Repository-Local Tool Manifest

For teams and agent workspaces, prefer a repository-local .NET tool manifest when you want every contributor and CI job to use the same Navlyn versions:

```powershell
dotnet new tool-manifest
dotnet tool install navlyn --version 0.7.0
dotnet tool install navlyn-mcp --version 0.7.0
dotnet tool restore
dotnet tool run navlyn -- doctor --workspace auto
```

Commit `.config/dotnet-tools.json` after reviewing the exact versions. A copyable manifest shape lives in [`../examples/install/dotnet-tools.json`](../examples/install/dotnet-tools.json).

Use global tools for individual machines and quick evaluation. Use local tools for repository policy, reproducible agent setup, and CI scripts. In CI, run `dotnet tool restore` before invoking `dotnet tool run navlyn -- ...`.

Local MCP server configuration can still invoke `navlyn-mcp` when the restored local tool directory is on the command path for that process. If that is not true in your client, use an absolute command path or a small wrapper script outside the committed example.

## MCP Client Setup Examples

The installed stdio server shape for a normal repository with one top-level workspace candidate is:

```json
{
  "command": "navlyn-mcp",
  "cwd": "."
}
```

Set `cwd` to the repository root, or pass `args: ["--working-directory", "."]` when the MCP client cannot set `cwd`. Add `args: ["--workspace", "path/to/YourRepo.slnx"]` only when automatic workspace discovery is ambiguous or the repository policy requires one explicit workspace.

For VS Code workspace configuration, use `.vscode/mcp.json` with a `servers` object. See [`../examples/install/vscode-mcp.json`](../examples/install/vscode-mcp.json).

For local development from this repository, use [`../examples/mcp/local-development.json`](../examples/mcp/local-development.json). For installed tools, use [`../examples/mcp/dotnet-tool.json`](../examples/mcp/dotnet-tool.json).

When an agent needs several facts from one workspace, prefer CLI `navlyn batch`, or MCP `navlyn_batch`, with examples from [`../examples/batch`](../examples/batch). This reduces repeated workspace load cost after the needed facts are known.

## Release Identity

The public release is `0.7.0`; the current unreleased rehearsal identity is `0.8.0-preview.1`.

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

For the current preview, run the unique-output commands in [Current Release State](#current-release-state) to pack and validate the candidate, then install it from its local feed into isolated tool paths and a separate consumer workspace.

The consumer script verifies CLI-only, MCP-only, and combined installations for `net8.0` and `net10.0`, executes installed tools outside the Navlyn repository, checks the exact 25-tool MCP surface and a semantic call, and removes its isolated installation after the run. It never uses global tool installation. Pass `-RollbackManifest <previous-package-manifest>` to include an older package version in the update/rollback scenario. The release workflow also exercises the lifecycle and cleanup path. `artifacts/` is ignored and should not be committed.

## Release Pack

Create release packages and a manifest:

```powershell
./scripts/pack-release.ps1 -Output artifacts/packages
./scripts/test-package-contract.ps1 -Manifest artifacts/packages/navlyn-release-pack.json
./scripts/publish-nuget.ps1 -Manifest artifacts/packages/navlyn-release-pack.json -DryRun
```

By default this runs release validation before packing. Use `-NoValidation` only when validation already ran in the same environment.

## NuGet Publish

Publishing is opt-in. Dry-run is the default:

```powershell
./scripts/publish-nuget.ps1 -DryRun
```

To publish from GitHub Actions, use NuGet Trusted Publishing. The publish workflow exchanges the GitHub OIDC token for a short-lived NuGet API key shortly before pushing packages, then passes that temporary value to this script as `NUGET_API_KEY`.

For local emergency publishing only, set `NUGET_API_KEY` manually and pass `-Publish`:

```powershell
$env:NUGET_API_KEY = '<key>'
./scripts/publish-nuget.ps1 -Publish
```

The script reads `artifacts/packages/navlyn-release-pack.json` unless package paths are supplied explicitly.

Avoid long-lived NuGet API keys for normal releases. If a fallback key is ever created, scope it as narrowly as possible for the package IDs and publish operation, keep it out of the repository, and delete it after use.

## GitHub Manual Publish Workflow

The repository may include a guarded manual workflow at `.github/workflows/publish-nuget.yml`.

Required repository setup before using it:

- Create a GitHub environment named `nuget-production`.
- Add required reviewers to the environment.
- Add an environment variable named `NUGET_USER` with the nuget.org profile name, not an email address.
- Configure nuget.org Trusted Publishing for the package-owning NuGet account, GitHub repository owner `furbon`, repository `navlyn`, workflow file `publish-nuget.yml`, and environment `nuget-production`.
- Keep the workflow trigger as `workflow_dispatch` only.

The workflow must run release validation before packing and publishing. Normal `push` and `pull_request` CI must never publish packages.

## GitHub Release

For a future authorized public release, after packages are published and install smoke passes from NuGet:

1. Create a tag matching the published version (the existing public release is `v0.7.0`).
2. Create a GitHub Release using the `CHANGELOG.md` entry.
3. Link to the NuGet install commands.
4. Optionally attach `navlyn-release-pack.json` and package artifacts for traceability.

Do not create the public release before package smoke and dry-run publish have succeeded.

## Post-Release Smoke

After NuGet indexing completes, test installation from the public feed in a clean shell:

```powershell
dotnet tool install --global navlyn --version 0.7.0
dotnet tool install --global navlyn-mcp --version 0.7.0
navlyn --help
navlyn-mcp --help
navlyn doctor --workspace auto
```

For a local rehearsal, prefer the isolated `scripts/test-consumer-install.ps1` harness above. If testing manually, use a temporary `--tool-path` and invoke the installed executable by absolute path rather than changing global tools.

## Rollback / Unlist

NuGet packages are immutable after publication. If a bad package is published:

- Publish a fixed newer version when possible.
- Use NuGet unlist only for packages that should be hidden from search.
- Keep the GitHub Release notes clear about the replaced version.
- Do not rewrite tags that users may already have fetched unless the release was never announced.

## Release Checklist

- Confirm package IDs `navlyn` and `navlyn-mcp` are available or owned by the maintainer.
- Confirm repository About description and topics are set on GitHub.
- Confirm the `nuget-production` environment, `NUGET_USER` environment variable, and nuget.org Trusted Publishing policy are configured if using the manual publish workflow.
- Update versions and release notes in both tool projects.
- Update `CHANGELOG.md`.
- Run `./scripts/test-release.ps1`.
- Run the isolated preview package smoke in [Local Package Smoke](#local-package-smoke).
- Run `./scripts/pack-release.ps1`.
- Dry-run `./scripts/publish-nuget.ps1 -DryRun`.
- Publish with `-Publish` only from an intentional release environment.
- Create the GitHub Release only after package publication and post-release smoke succeed.

Generated packages, package smoke tools, release manifests, performance reports, binlogs, and local notes belong under ignored local paths such as `artifacts/` and should not be committed.

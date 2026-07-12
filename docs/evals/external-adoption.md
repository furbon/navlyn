# Navlyn External Adoption Eval

This eval checks whether Navlyn can be adopted from a fresh clone of real public C# repositories without hand-curated fixtures.

It is intentionally networked and is not part of the default release script. Run it when preparing a release, validating adoption claims, or investigating why an external repository fails to load.

## Runner

```powershell
./scripts/measure-external-adoption.ps1 `
  -NoBuild `
  -Repositories `
    https://github.com/Tyrrrz/CliWrap.git,`
    https://github.com/jbogard/MediatR.git,`
    https://github.com/commandlineparser/commandline.git,`
    https://github.com/rosenbjerg/recreate-sln-structure.git `
  -CommandTimeoutSeconds 240 `
  -Output artifacts/external-adoption/external-adoption-corpus-report.json
```

Build first by omitting `-NoBuild` when the local Navlyn binaries are stale.

The runner performs:

- `git clone --depth 1` into `artifacts/external-adoption/repos`.
- `dotnet restore` in the external repository.
- Workspace discovery for `navlyn.workspace.json`, `.code-workspace`, `.slnx`, `.sln`, `.csproj`, or `.vbproj`.
- Product-code type discovery, preferring `src` and avoiding test, benchmark, and sample folders.
- `navlyn doctor`.
- `navlyn target` by source position.
- `navlyn prepare-edit` by source position with bounded reference and test limits.

If the primary solution is too broad or fails because unrelated projects cannot load, the runner falls back to the nearest project file for the discovered target. This mirrors the recommended agent recovery path: use a solution for broad workspace health, but narrow to the relevant project for edit preparation when a large solution is slow or partially incompatible.

## Statuses

- `clean-success`: `doctor`, `target`, and `prepare-edit` all returned valid JSON with exit code 0 on the effective workspace.
- `degraded-target-success`: `target` returned valid JSON, but `doctor` or `prepare-edit` did not fully pass.
- `no-go`: the repository cloned, but Navlyn could not produce a usable target on the effective workspace.
- `clone-failed`: the repository could not be cloned.

The report includes stdout/stderr character counts and previews for restore, doctor, target, and prepare-edit so failures are diagnosable without rerunning immediately.

## Current v0.7.0 Corpus Result

Last local run: 2026-07-12.

Report: `artifacts/external-adoption/external-adoption-corpus-report.json`.

Summary:

- Repositories: 4
- Clean successes: 3
- Degraded target successes: 0
- No-go: 1
- Clone failures: 0

Clean successes:

- `https://github.com/Tyrrrz/CliWrap.git`: solution health was valid; edit preparation completed on nearest project fallback.
- `https://github.com/jbogard/MediatR.git`: solution load was blocked by unrelated test-project target framework diagnostics; edit preparation completed on nearest project fallback.
- `https://github.com/commandlineparser/commandline.git`: edit preparation completed on nearest project fallback.

No-go:

- `https://github.com/rosenbjerg/recreate-sln-structure.git`: the repository requests .NET SDK `9.0.0` via `global.json`; the local machine has SDK `8.0.422`, `10.0.109`, and `10.0.301`, so restore and MSBuild workspace loading fail before semantic navigation can proceed.

## Release Gate

A v0.7.x adoption claim should not cite this eval unless:

- At least three public repositories report `clean-success`.
- At least one incompatible environment reports a diagnosable `no-go` with stderr/stdout previews.
- No runner failure is caused by pipe deadlock, invalid JSON parsing, comment-only type discovery, or missing failure previews.
- The generated report is attached to release notes or review evidence.


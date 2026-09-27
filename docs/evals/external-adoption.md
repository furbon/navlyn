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

## Recorded v0.8.0-preview.1 Corpus Result

Last local Windows run: 2026-09-26. The report is retained at `artifacts/release-readiness-goal-20260926/p8-external-adoption.json` (SHA-256 `82deb5e0067070030f2fea7e6f2f61c36a953bd221dffbd98e9597d5110956f9`). This ignored report is local release evidence, not part of the packages.

Summary:

- Repositories: 4
- Clean successes: 3
- Degraded target successes: 0
- No-go: 1
- Clone failures: 0

Clean successes:

- `https://github.com/Tyrrrz/CliWrap.git` at `804ad88ddd8fa61cbd7d81246051d34974520b9a`: `doctor`, `target`, and `prepare-edit` passed on the nearest project fallback.
- `https://github.com/jbogard/MediatR.git` at `916ef1b3d68ccdc96db8f914eaf1b32fc7db52c5`: the solution restore failed, but all three Navlyn commands passed on the nearest project fallback.
- `https://github.com/commandlineparser/commandline.git` at `1e3607b97af6141743edb3c434c06d5b492f6fb3`: all three commands passed on the nearest project fallback.

No-go:

- `https://github.com/rosenbjerg/recreate-sln-structure.git` at `fbbbd1bbe0624363dd3e4327d3393db8f6498b9a`: its `global.json` requests .NET SDK `9.0.0` with `latestMinor` roll-forward. This Windows machine has .NET SDK 8 and 10, so restore and workspace loading fail before semantic navigation; the report retains both diagnostic previews.

## Release Gate

A v0.8 preview adoption claim should not cite this eval unless:

- At least three public repositories report `clean-success`.
- At least one incompatible environment reports a diagnosable `no-go` with stderr/stdout previews.
- No runner failure is caused by pipe deadlock, invalid JSON parsing, comment-only type discovery, or missing failure previews.
- The generated report is attached to release notes or review evidence.


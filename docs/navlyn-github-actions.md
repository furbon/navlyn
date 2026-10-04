# Navlyn GitHub Actions

Navlyn can publish deterministic facts for a pull request as a job summary and JSON artifacts.

The recommended first integration is an artifact workflow, not automatic review comments. This keeps Navlyn in facts-provider mode and lets a human or agent decide what to do with the results.

## Local PR Facts Script

```powershell
dotnet build navlyn.slnx
./scripts/write-navlyn-pr-facts.ps1 -Workspace navlyn.slnx -Output artifacts/navlyn-pr-facts
```

The script writes:

- `review-diff.json`
- `context-pack.json`
- `tests-for-diff.json`
- `public-api-diff.json`
- `review-pack.json`
- `summary.md`
- `manifest.json`

## Example Workflow

See `examples/github-actions/navlyn-pr-facts.yml`.

The example workflow:

- checks out the repository with history for diff commands,
- builds Navlyn,
- runs `write-navlyn-pr-facts.ps1`,
- appends `summary.md` to the GitHub job summary,
- uploads JSON facts as an artifact.

The copyable example targets Windows and builds the checked-out source. Downstream repositories can install released versions from NuGet; source builds remain useful for validating changes before publication.

## Release workflow lanes

The [CI workflow](../.github/workflows/ci.yml) builds Release once on each OS. Windows runs product tests on .NET 10 and fast release checks; Linux/macOS run focused transport and portable CLI checks. The Windows main run creates and tests packages once, then retains exact publication inputs. Logs, timings, and TRX are retained on every outcome.

The [release-validation workflow](../.github/workflows/release-validation.yml) verifies an annotated tag against the successful main run and public packages. It reuses evidence rather than rerunning source tests. Manual dispatch must select the release tag.

The [protected publish workflow](../.github/workflows/publish-nuget.yml) reuses the exact tested main artifacts, uses the `nuget-production` environment and Trusted Publishing, and records publication/recovery evidence. Ordinary push and pull request triggers cannot publish packages. The optional [Ubuntu rehearsal](../.github/workflows/publish-rehearsal.yml) validates/builds once, packs those Release outputs, and performs a credential-free dry run. Live model comparisons run separately with a small named task set.

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

The copyable example targets Windows and builds the checked-out source. For the current unpublished `0.8.0-preview.1` rehearsal, keep this job local to the repository; `0.7.0` is the public NuGet release. The example does not claim that preview packages are available to downstream repositories.

## Release workflow lanes

The repository's [CI workflow](../.github/workflows/ci.yml) runs deterministic, credential-free checks on Windows for pushes and pull requests, including restore/build/tests, focused contracts, package inspection, and skill lifecycle checks. The [release-validation workflow](../.github/workflows/release-validation.yml) runs the full release suite and isolated consumer/skill checks on Windows for manual dispatch and release refs, then stores candidate evidence. It runs no live model evaluation and does not publish.

Live evaluation is a separate explicit, credential-gated operation using a named client/model/scenario set. NuGet publication remains in the separate manual [protected publish workflow](../.github/workflows/publish-nuget.yml), with the `nuget-production` environment and Trusted Publishing. Ordinary push and pull request triggers cannot publish packages.

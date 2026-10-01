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

The repository's [CI workflow](../.github/workflows/ci.yml) restores, builds, and tests on Windows, Ubuntu, and macOS for pushes and pull requests; Windows also runs focused contracts, package inspection, and skill lifecycle checks. The [release-validation workflow](../.github/workflows/release-validation.yml) runs the full release suite and isolated consumer/skill checks on Windows for manual dispatch and release refs, then stores candidate evidence. It runs no live model evaluation and does not publish.

Live evaluation is a separate explicit, credential-gated operation using a named client/model/scenario set. After the reviewed commit lands on `main`, the manual [Ubuntu publish rehearsal](../.github/workflows/publish-rehearsal.yml) validates that exact commit, packs both tools, checks the package contract, retains the bytes, and runs a dry run without NuGet credentials. NuGet publication remains in the separate manual [protected publish workflow](../.github/workflows/publish-nuget.yml), with the `nuget-production` environment and Trusted Publishing. It repeats the Ubuntu source/package checks on the dispatched `main` commit and retains package bytes before login. Ordinary push and pull request triggers cannot publish packages.

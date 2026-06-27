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

Use published tools in downstream repositories once packages are available. During local development, the example builds from source.

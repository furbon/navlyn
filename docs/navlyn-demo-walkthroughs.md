# Navlyn Demo Walkthroughs

These demos show Navlyn as a read-only semantic evidence layer. They use this repository and committed fixtures so the commands stay reproducible.

Navlyn reports static source-level facts. It does not prove runtime behavior, write review comments, run tests, or replace human judgment.

## One-Minute Copy/Paste Demo

Run from this repository after restore:

```powershell
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- resolve-target --workspace navlyn.slnx --project "Navlyn.CommandLine(net10.0)" --query CheckCommand --assume-kind NamedType --limit 5
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- symbol-source --workspace navlyn.slnx --project "Navlyn.CommandLine(net10.0)" --file Navlyn.CommandLine/Cli/Commands/CheckCommand.cs --line 6 --column 23 --view declaration
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- review-diff --workspace navlyn.slnx --base HEAD --head HEAD --profile compact --symbol-limit 3 --impact-limit 3 --diagnostic-limit 3 --related-test-limit 3
```

Check that stdout is JSON for each command and stderr is empty or diagnostic-only. The first two commands demonstrate fuzzy-to-exact symbol investigation. The third command demonstrates the review envelope on an explicit Git range; replace the refs with PR refs or omit them on a dirty branch to inspect a real diff.

## Demo 1: Symbol Investigation

Failure mode: an agent searches text, opens the wrong declaration, and edits before checking references or tests.

Run:

```powershell
navlyn repo-graph --workspace navlyn.slnx --profile compact
navlyn resolve-target --workspace navlyn.slnx --query CheckCommand --assume-kind NamedType --limit 5
navlyn context-pack --workspace navlyn.slnx --query CheckCommand --assume-kind NamedType --goal modify --profile compact --budget-tokens 2000
```

Useful output excerpt:

```json
{
  "confidence": "high",
  "candidateCount": 1,
  "selectedTarget": {
    "name": "CheckCommand",
    "kind": "NamedType",
    "path": "Navlyn.CommandLine/Cli/Commands/CheckCommand.cs",
    "line": 6,
    "column": 23
  },
  "candidateId": "sym:v1:...",
  "selector": {
    "project": "Navlyn.CommandLine(net10.0)",
    "targetFramework": "net10.0"
  },
  "recommendedNextActions": [
    { "command": "definition", "candidateId": "sym:v1:..." },
    { "command": "references", "candidateId": "sym:v1:..." },
    { "command": "about", "candidateId": "sym:v1:..." }
  ]
}
```

Why it matters: the `candidateId` gives the agent a stable declaration anchor for follow-up commands. The `context-pack` output then gives bounded reading material instead of a raw source dump.

## Demo 2: PR Review Facts

Failure mode: an agent reviews a diff from raw patches only and misses diagnostics, source impact, public API changes, or related tests.

Run from a dirty branch or with explicit refs:

```powershell
navlyn review-diff --workspace navlyn.slnx --profile evidence --symbol-limit 3 --impact-limit 5 --diagnostic-limit 5 --related-test-limit 5 --depth 1
navlyn tests-for-diff --workspace navlyn.slnx --profile compact --test-limit 10
navlyn context-pack --workspace navlyn.slnx --diff --goal review --profile compact --budget-tokens 8000
```

Useful output excerpt:

```json
{
  "command": "review-diff",
  "profile": "evidence",
  "diff": {
    "mode": "workingTree",
    "totalFiles": 11
  },
  "summary": {
    "changedSymbols": { "totalSymbols": 0, "truncated": false },
    "diagnostics": { "totalDiagnostics": 0, "truncated": false },
    "relatedTests": { "totalCandidates": 0, "truncated": false }
  },
  "warnings": [
    "Public contract changes are current-workspace heuristics; this review-diff pack does not include before/after public API diff.",
    "Diagnostics are current scoped diagnostics, not before/after diagnostic deltas."
  ]
}
```

Why it matters: `review-diff` is an evidence pack, not a reviewer. It gives the agent and human reviewer scoped facts and explicitly reports bounded limitations.

## Demo 3: ASP.NET And Application-Domain Facts

Failure mode: an agent treats a controller, minimal API endpoint, MediatR request, or EF entity as plain text and misses framework links.

Run against the application-domain fixture:

```powershell
navlyn route-map --workspace tests/fixtures/ApplicationDomainFixture/ApplicationDomainFixture.csproj --profile compact
navlyn where-handled --workspace tests/fixtures/ApplicationDomainFixture/ApplicationDomainFixture.csproj --query CreateOrderCommand --assume-kind NamedType --profile compact
navlyn ef-model --workspace tests/fixtures/ApplicationDomainFixture/ApplicationDomainFixture.csproj --entity Order --profile compact
navlyn package-usage --workspace tests/fixtures/ApplicationDomainFixture/ApplicationDomainFixture.csproj --package Microsoft.EntityFrameworkCore --namespace Microsoft.EntityFrameworkCore --profile compact
```

Useful output excerpt:

```json
{
  "command": "route-map",
  "summary": {
    "routes": {
      "totalItems": 4,
      "truncated": false
    }
  },
  "highlights": {
    "routes": {
      "items": [
        {
          "endpointKind": "controller-action",
          "httpMethods": ["GET"],
          "normalizedRoutePattern": "/orders/{id}",
          "auth": { "kind": "required", "policies": ["Orders.Read"] },
          "handler": {
            "name": "Get",
            "container": "ApplicationDomainFixture.OrdersController"
          }
        }
      ]
    }
  }
}
```

Why it matters: route, auth, handler, MediatR, EF, and package usage facts are bounded source-level evidence. They help an agent decide what to read next, but they are not runtime route tables, authorization proof, EF runtime models, or package compatibility scans.

## Case Study Candidates

Medium-size OSS case studies should be reproducible and should record commit hash, SDK, workspace path, command timings, stdout size, truncation state, warnings, and whether expected files were present.

Good candidate shapes:

- ASP.NET Core service with controllers, minimal APIs, DI, options, EF, and tests.
- Library repository with public API surface and multi-project tests.
- Tooling repository with CLI commands, fixtures, and review workflows.

Do not publish case-study numbers without running the commands in a clean clone. If clone/build cost is high, keep the item as a candidate and record the reason.

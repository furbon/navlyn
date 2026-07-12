# Navlyn Demo Walkthroughs

These demos show the moment Navlyn is meant for: an agent is about to reason about C# code, and plain text search is not enough.

Each walkthrough is reproducible from this repository or committed fixtures. Each one starts from a realistic failure mode, then shows the smallest Navlyn facts that reduce that failure. Navlyn reports static source-level evidence; it does not prove runtime behavior, write review comments, run tests, or replace human judgment.

## Three-Minute Copy/Paste Demo

Run from this repository after restore:

```powershell
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- resolve-target --workspace navlyn.slnx --project "Navlyn.CommandLine(net10.0)" --query CheckCommand --assume-kind NamedType --limit 5
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- edit-preflight --workspace navlyn.slnx --project "Navlyn.CommandLine(net10.0)" --query CheckCommand --assume-kind NamedType --goal modify --change-kind behavior
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- review-diff --workspace navlyn.slnx --base HEAD --head HEAD --profile compact --symbol-limit 3 --impact-limit 3 --diagnostic-limit 3 --related-test-limit 3
```

Check that stdout is JSON for each command and stderr is empty or diagnostic-only. The fields to inspect are:

- `resolve-target`: `confidence`, `candidateId`, `selectedTarget.path`, `ambiguitySummary` when no target is selected.
- `edit-preflight`: anchor fields, source/context/test evidence, known unknowns, and the post-edit guard command.
- `review-diff`: `diff.mode`, changed-symbol/diagnostic/impact counts, warnings, truncation flags, and `profile`.

Stop after the returned fields answer the question. Do not treat `recommendedNextActions` as a checklist. The third command demonstrates a bounded review envelope on an explicit Git range; replace the refs with PR refs or omit them on a dirty branch to inspect a real diff.

If you are evaluating Navlyn from a package install rather than this repository, run the same shape against your solution:

```powershell
navlyn doctor --workspace auto
navlyn target --workspace auto --query PaymentService --assume-kind NamedType
navlyn prepare-edit --workspace auto --candidate-id sym:v1:... --goal modify --change-kind behavior
```

Use an explicit workspace path only when `auto` is ambiguous.

## Demo 1: Symbol Investigation

Failure mode: an agent searches text, opens the wrong declaration, and edits before checking references or tests.

Run:

```powershell
navlyn repo-graph --workspace navlyn.slnx --profile compact
navlyn resolve-target --workspace navlyn.slnx --query CheckCommand --assume-kind NamedType --limit 5
navlyn edit-preflight --workspace navlyn.slnx --query CheckCommand --assume-kind NamedType --goal modify --change-kind behavior --budget-tokens 2000
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

Why it matters: the `candidateId` gives the agent a stable declaration anchor for follow-up commands. The `edit-preflight` output then gives bounded source, context, related test evidence, known unknowns, and a post-edit guard command instead of a raw source dump.

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

## Demo 2b: Wrong-Symbol Guard

Failure mode: an agent correctly anchors `CheckCommand`, edits elsewhere, and then keeps going without checking whether the diff still matches the target.

Run:

```powershell
navlyn resolve-target --workspace navlyn.slnx --project "Navlyn.CommandLine(net10.0)" --query CheckCommand --assume-kind NamedType --limit 5
navlyn edit-preflight --workspace navlyn.slnx --candidate-id sym:v1:... --goal modify --change-kind behavior
navlyn wrong-symbol-guard --workspace navlyn.slnx --query CheckCommand --assume-kind NamedType --fail-on-risk medium
```

Useful fields:

- `anchor`: the intended symbol identity.
- `changedSymbols` or equivalent diff summary: what actually changed.
- `risk`: whether the diff is empty, outside the target, ambiguous, or aligned.
- `warnings` / `nextActions`: why the agent should stop, ask, or inspect.

Why it matters: the guard is a fail-closed evidence step. It does not prove the edit is correct; it tells an agent when the actual diff is not yet safe to continue.

## Demo 3: ASP.NET And Application-Domain Facts

Failure mode: an agent treats a controller, minimal API endpoint, MediatR request, or EF entity as plain text and misses framework links.

Run against the application-domain fixture:

```powershell
navlyn route-map --workspace tests/fixtures/ApplicationDomainFixture/ApplicationDomainFixture.csproj --profile compact
navlyn where-handled --workspace tests/fixtures/ApplicationDomainFixture/ApplicationDomainFixture.csproj --query CreateOrderCommand --assume-kind NamedType --profile compact
navlyn ef-model --workspace tests/fixtures/ApplicationDomainFixture/ApplicationDomainFixture.csproj --entity Order --profile compact
navlyn package-usage --workspace tests/fixtures/ApplicationDomainFixture/ApplicationDomainFixture.csproj --package Microsoft.EntityFrameworkCore --namespace Microsoft.EntityFrameworkCore --profile compact
Get-Content examples/batch/application-facts.json | navlyn batch --workspace tests/fixtures/ApplicationDomainFixture/ApplicationDomainFixture.csproj
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

## Case Studies

Reproducible current-repo and fixture-backed case studies live in [`navlyn-case-studies.md`](navlyn-case-studies.md). They avoid external clone/build risk and keep public claims tied to committed workspaces.

Medium-size OSS case studies remain useful later, but they should be published only when a clean clone, restore, workspace load, and representative commands are stable within 30 minutes. Record commit hash, SDK, workspace path, command timings, stdout size, truncation state, warnings, and whether expected files were present.

Good candidate shapes:

- ASP.NET Core service with controllers, minimal APIs, DI, options, EF, and tests.
- Library repository with public API surface and multi-project tests.
- Tooling repository with CLI commands, fixtures, and review workflows.

Do not publish case-study numbers without running the commands in a clean clone. If clone/build cost is high, keep the item as a candidate and record the reason.

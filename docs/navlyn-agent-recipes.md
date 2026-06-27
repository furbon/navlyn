# Navlyn Agent Recipes

These recipes are starting points for agents, MCP clients, CI jobs, and local automation. They keep Navlyn in facts-provider mode: commands return deterministic JSON evidence, and the agent or human reviewer decides what to do with it.

Use `compact` for first scans, `evidence` for review and CI facts, and `full` when downstream tooling expects the richest command result.

## First Workspace Scan

Start with repository structure before asking symbol-specific questions. This lets an agent understand project roles, target frameworks, package references, test relationships, and useful next actions.

CLI:

```powershell
navlyn repo-graph --workspace navlyn.slnx --profile compact
```

Batch:

```json
{
  "requests": [
    { "id": "repo", "command": "repo-graph", "profile": "compact" },
    { "id": "diagnostics", "command": "diagnostics", "limit": 20 }
  ]
}
```

Run with:

```powershell
Get-Content examples/batch/investigation-loop.json | navlyn batch --workspace navlyn.slnx
```

## Symbol Investigation

When the agent has an approximate symbol name, let Navlyn resolve candidates first. Reuse `candidateId` for deeper calls so the investigation remains anchored to the same declaration.

```powershell
navlyn find --workspace navlyn.slnx --query CheckCommand --assume-kind NamedType --limit 10
navlyn about --workspace navlyn.slnx --candidate-id sym:v1:...
navlyn references --workspace navlyn.slnx --candidate-id sym:v1:... --usage-kind invoke --usage-kind construct --group-by file --group-by usage-kind --limit 50
navlyn related --workspace navlyn.slnx --candidate-id sym:v1:... --limit 30
navlyn impact --workspace navlyn.slnx --candidate-id sym:v1:... --depth 2
```

Use `about` for a compact selected-symbol summary, `references` with `usageKind` and `groupBy` when the agent needs precise read/write/invocation/construction evidence, `related` for a file-first reading map, and `impact` before edits or risk analysis.

## Bounded Context Before Editing

Use `context-pack` when an agent needs the right material to read, not every possible reference. Select the goal that matches the work:

- `review`: prioritize changed symbols, diagnostics, impact facts, tests, and evidence.
- `modify`: prioritize definitions, members, implementations, references, and related tests.
- `understand`: prioritize outlines, relationships, repository context, and representative references.

```powershell
navlyn context-pack --workspace navlyn.slnx --query CheckCommand --assume-kind NamedType --goal modify --change-kind signature --profile compact --budget-tokens 8000
navlyn context-pack --workspace navlyn.slnx --candidate-id sym:v1:... --goal understand --snippet-policy signature
```

`context-pack` returns ranked items, budget fields, truncation state, warnings, and next actions. It does not dump raw source files or generate prose summaries.

## PR Review Facts

For code review, begin with diff facts rather than broad symbol search.

```powershell
navlyn review-diff --workspace navlyn.slnx --profile evidence
navlyn tests-for-diff --workspace navlyn.slnx --profile compact
navlyn public-api-diff --workspace navlyn.slnx --base main --profile evidence
navlyn review-pack --workspace navlyn.slnx --profile evidence
navlyn context-pack --workspace navlyn.slnx --diff --goal review --profile compact
```

Batch form:

```json
{
  "requests": [
    { "id": "review", "command": "review-diff", "profile": "evidence", "symbolLimit": 20, "impactLimit": 40 },
    { "id": "context", "command": "context-pack", "diff": true, "goal": "review", "profile": "compact", "budgetTokens": 8000 },
    { "id": "tests", "command": "tests-for-diff", "profile": "compact", "testLimit": 20 },
    { "id": "api", "command": "public-api-diff", "profile": "evidence", "base": "main", "changeLimit": 20 },
    { "id": "packs", "command": "review-pack", "profile": "evidence", "pack": ["async", "security"], "findingLimit": 50 }
  ]
}
```

Review packs return evidence-backed signals for an AI reviewer to inspect. They are not complete analyzers, security scanners, or final review comments.

## Public API Review

Use `public-api-diff` when package or library compatibility matters. It compares source-level public and protected API surface between Git refs.

```powershell
navlyn public-api-diff --workspace navlyn.slnx --base main --head HEAD --profile evidence
navlyn public-api-diff --workspace navlyn.slnx --base main --project navlyn --change-limit 50
```

Navlyn reports source and binary compatibility risk fields, but it does not emit a SemVer conclusion. Treat the output as compatibility evidence for release review.

## Related Tests

Use related-test commands to decide what to inspect or run. Navlyn does not execute tests or read coverage files.

```powershell
navlyn tests-for-symbol --workspace navlyn.slnx --query RepoGraphResolver --assume-kind NamedType --test-limit 20
navlyn tests-for-diff --workspace navlyn.slnx --profile compact --test-limit 20
```

The output is useful when an agent needs to propose test updates or decide which test files to read after an implementation change.

## Dependency Injection Investigation

Use DI commands when references alone do not explain how a type is constructed or consumed through Microsoft.Extensions.DependencyInjection.

```powershell
navlyn di-graph --workspace navlyn.slnx --profile compact
navlyn where-registered --workspace navlyn.slnx --query MyService --assume-kind NamedType --profile evidence
navlyn di-impact --workspace navlyn.slnx --query MyService --assume-kind NamedType --profile compact
```

DI facts are source-level and pattern-based. They are useful for registrations, constructor dependencies, consumer relationships, and reported risk facts such as multiple registrations or captive dependency candidates.

## .NET Application Domains

Use application-domain packs when the code question is about framework patterns rather than one symbol reference list.

```powershell
navlyn route-map --workspace navlyn.slnx --profile compact
navlyn options-graph --workspace navlyn.slnx --query PaymentOptions --profile compact
navlyn where-handled --workspace navlyn.slnx --query CreateOrderCommand --assume-kind NamedType --profile compact
navlyn ef-model --workspace navlyn.slnx --entity Order --profile compact
navlyn package-usage --workspace navlyn.slnx --package Microsoft.EntityFrameworkCore --namespaces Microsoft.EntityFrameworkCore --profile compact
```

These commands report bounded source-level evidence. They do not claim complete runtime route tables, effective authorization, secret/config values, EF runtime models, or package compatibility.

## Framework Entrypoints

Use framework entrypoint commands to help an agent understand how code may be reached from application or test frameworks.

```powershell
navlyn framework-entrypoints --workspace navlyn.slnx --profile compact
navlyn entrypoints --workspace navlyn.slnx --query CheckCommand --assume-kind NamedType --framework-aware --depth 3
```

Entrypoint detection is evidence-backed and bounded. It is not a runtime route table or full reachability graph.

## MCP Recipes

Equivalent MCP flow for a symbol investigation:

```text
navlyn_workspace_summary(profile: "compact")
navlyn_find_symbol(query: "CheckCommand", assumeKind: "NamedType")
navlyn_exact_navigation(operation: "references", candidateId: "sym:v1:...", usageKinds: ["invoke", "construct"], groupBy: ["file", "usage-kind"], limit: 50)
navlyn_about_symbol(candidateId: "sym:v1:...")
navlyn_related_files(candidateId: "sym:v1:...", limit: 30)
navlyn_context_pack(candidateId: "sym:v1:...", goal: "modify", changeKind: "signature", profile: "compact")
```

Equivalent MCP flow for a review:

```text
navlyn_review_diff(profile: "evidence")
navlyn_context_pack(diff: true, goal: "review", profile: "compact")
navlyn_batch(requests: [
  { id: "tests", command: "tests-for-diff", profile: "compact" },
  { id: "api", command: "public-api-diff", base: "main", profile: "evidence" },
  { id: "packs", command: "review-pack", pack: ["async", "security"], profile: "evidence" }
])
```

## GitHub Actions

Use the PR facts script to publish deterministic facts as a job summary and JSON artifact:

```powershell
./scripts/write-navlyn-pr-facts.ps1 -Workspace navlyn.slnx -Output artifacts/navlyn-pr-facts -Base main
```

See [`navlyn-github-actions.md`](navlyn-github-actions.md) and `examples/github-actions/navlyn-pr-facts.yml`.

## Performance Measurement

Use the performance script as a local measurement source:

```powershell
./scripts/measure-navlyn-performance.ps1 -Workspace navlyn.slnx -Scenario agent-loop -Iterations 3 -Profile compact -Output artifacts/navlynbench-agent-loop.json
```

Track tool call count, stdout size, latency, truncation, and whether expected files appear in related/context outputs. Keep local reports under ignored paths such as `artifacts/`.

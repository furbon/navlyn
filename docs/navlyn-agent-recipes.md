# Navlyn Agent Recipes

These recipes teach agents how to use Navlyn without turning it into a broad checklist. Navlyn is a facts provider: commands return deterministic JSON evidence, and the agent or human reviewer decides what to do with it.

The core habit is simple:

1. use `rg` or normal file reads when text is enough;
2. use one Navlyn call when C# semantic identity matters;
3. reuse `candidateId`;
4. stop when the returned facts answer the question;
5. escalate to impact, tests, context, or batch only when the smaller fact shows it is needed.

For a first install-to-success path, see [`navlyn-first-10-minutes.md`](navlyn-first-10-minutes.md). For category positioning, see [`navlyn-positioning.md`](navlyn-positioning.md).

Use `compact` for first scans, `evidence` for review and CI facts, and `full` when downstream tooling expects the richest command result.

## Use And Stop Rules

Use normal file reads and `rg` first when text is enough. Use Navlyn when a C# semantic fact would change the answer:

- The user points at a type, method, property, overload, partial declaration, or source position.
- Project, target framework, generated-code, or linked-file context matters.
- The task is a real Git diff review or edit-risk investigation.
- Static source-level DI, route, public API, package, framework entrypoint, or related-test facts are useful evidence.

Do not use Navlyn for comments, strings, docs, Markdown, non-C# files, generated artifacts, or final review prose. Navlyn does not run tests, edit files, prove runtime behavior, or replace a human reviewer.

Stop once the returned facts answer the question. Escalate only when the next fact is needed:

| Situation | Minimal Navlyn Call | Escalate To |
| --- | --- | --- |
| Approximate symbol name | `resolve-target` | `find` if candidates are ambiguous or the user needs alternatives |
| Known symbol identity | `definition`, `references`, or `symbol-source` via CLI; `navlyn_symbol_source` or `navlyn_symbol_edges` via MCP | `impact` for edit risk, `context-pack` for a reading queue |
| Single-file review | `outline` via CLI or `navlyn_file_outline` / `navlyn_inspect_file` via MCP when semantic structure matters | `context-pack` only if the file does not contain enough context |
| Agent edit preflight | `edit-preflight` via CLI or `navlyn_edit_preflight` in MCP `edit` profile | `change-intent-pack`, `agent-handoff-pack`, or `confidence-ledger` when the work is handed off or audited |
| Post-edit wrong-symbol check | `post-edit-guard` with the pre-edit `candidateId` or saved preflight | `wrong-symbol-guard` when the intended target should be re-resolved from query/source position |
| Real Git diff review | `review-diff` | `tests-for-diff`, `public-api-diff`, `review-pack`, or `context-pack --diff` only when relevant |
| Multiple known facts from one workspace | Individual tools first | CLI `batch` or MCP `navlyn_batch` in `--tool-profile full` after the needed facts are known |

## Workspace Context

Ask for repository structure only when it would affect the answer. This lets an agent understand project roles, target frameworks, package references, test relationships, and useful next actions without making workspace summary a default first call.

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

When the agent has an approximate symbol name, resolve a target first. Reuse `candidateId` for deeper calls so the investigation remains anchored to the same declaration. Use `find` when the agent or human needs a broader candidate list.

```powershell
navlyn resolve-target --workspace navlyn.slnx --query CheckCommand --assume-kind NamedType --limit 10
navlyn find --workspace navlyn.slnx --query CheckCommand --assume-kind NamedType --limit 10
navlyn about --workspace navlyn.slnx --candidate-id sym:v1:...
navlyn references --workspace navlyn.slnx --candidate-id sym:v1:... --usage-kind invoke --usage-kind construct --group-by file --group-by usage-kind --limit 50
navlyn related --workspace navlyn.slnx --candidate-id sym:v1:... --limit 30
navlyn impact --workspace navlyn.slnx --candidate-id sym:v1:... --depth 2
```

Use `about` for a compact selected-symbol summary, `references` with `usageKind` and `groupBy` when the agent needs precise read/write/invocation/construction evidence, `related` for a file-first reading map, and `impact` before edits or risk analysis.

MCP clients can use the same stop rules with dedicated file-first tools:

```text
navlyn_file_outline(file: "Navlyn.CommandLine/Cli/Commands/CheckCommand.cs")
navlyn_symbol_source(candidateId: "sym:v1:...", view: "declaration")
navlyn_symbol_edges(operation: "references", candidateId: "sym:v1:...", usageKinds: ["invoke"], groupBy: ["file"], limit: 50)
```

## Bounded Context Before Editing

Use `context-pack` as an escalation when smaller facts or normal file reads are not enough and the agent needs the right material to read, not every possible reference. Select the goal that matches the work:

- `review`: prioritize changed symbols, diagnostics, impact facts, tests, and evidence.
- `modify`: prioritize definitions, members, implementations, references, and related tests.
- `understand`: prioritize outlines, relationships, repository context, and representative references.

```powershell
navlyn context-pack --workspace navlyn.slnx --query CheckCommand --assume-kind NamedType --goal modify --change-kind signature --profile compact --budget-tokens 8000
navlyn context-pack --workspace navlyn.slnx --candidate-id sym:v1:... --goal understand --snippet-policy signature
```

`context-pack` returns ranked items, budget fields, truncation state, warnings, and next actions. It does not dump raw source files or generate prose summaries.

## Pre-Edit And Post-Edit Guardrail

Use Navlyn as an evidence loop around the edit, not as the editor.

Before editing:

```powershell
navlyn edit-preflight --workspace navlyn.slnx --query CheckCommand --assume-kind NamedType --goal modify --change-kind behavior
navlyn change-intent-pack --workspace navlyn.slnx --query CheckCommand --assume-kind NamedType --goal modify --change-kind behavior
```

After editing:

```powershell
navlyn post-edit-guard --workspace navlyn.slnx --candidate-id sym:v1:... --fail-on-risk high
navlyn wrong-symbol-guard --workspace navlyn.slnx --query CheckCommand --assume-kind NamedType --fail-on-risk medium
navlyn review-diff --workspace navlyn.slnx --profile evidence --symbol-limit 20 --impact-limit 40 --diagnostic-limit 40 --related-test-limit 20
```

`post-edit-guard` compares the diff with a saved anchor or `candidateId`; `wrong-symbol-guard` re-resolves the intended target from query, candidate, or source position and then compares changed symbols. Both commands write JSON even when policy fails. A mismatch is a warning to pause and inspect; it is not a proof that the edit is wrong.

## PR Review Facts

For an actual Git diff, begin with diff facts rather than broad symbol search. For a single-file review with no diff, read the file first and use symbol/source-position tools only when semantic identity or relationships matter.

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
navlyn public-api-diff --workspace navlyn.slnx --base main --project "navlyn(net10.0)" --change-limit 50
```

Navlyn reports source and binary compatibility risk fields, but it does not emit a SemVer conclusion. Treat the output as compatibility evidence for release review.

## Related Tests

Use related-test commands to decide what to inspect or run after an edit plan, explicit user request, or diff review needs test impact. Do not use them for first-pass comprehension. Navlyn does not execute tests or read coverage files.

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
navlyn package-usage --workspace navlyn.slnx --package Microsoft.EntityFrameworkCore --namespace Microsoft.EntityFrameworkCore --profile compact
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

Minimal MCP flow for a symbol investigation:

```text
navlyn_doctor()
navlyn_resolve_target(query: "CheckCommand", assumeKind: "NamedType")
navlyn_exact_navigation(operation: "references", candidateId: "sym:v1:...", usageKinds: ["invoke", "construct"], groupBy: ["file", "usage-kind"], limit: 50)
navlyn_about_symbol(candidateId: "sym:v1:...")
```

Escalate from that flow only when needed:

```text
navlyn_workspace_summary(profile: "compact") // project/package/test context is needed
navlyn_related_files(candidateId: "sym:v1:...", limit: 30) // file map is needed
navlyn_context_pack(candidateId: "sym:v1:...", goal: "modify", changeKind: "signature", profile: "compact") // bounded reading queue is needed
```

MCP flow for a non-trivial edit:

```text
Start navlyn-mcp with --tool-profile edit.
navlyn_edit_preflight(query: "CheckCommand", assumeKind: "NamedType", goal: "modify", changeKind: "behavior")
// edit outside Navlyn
navlyn_post_edit_guard(candidateId: "sym:v1:...", failOnRisk: "high")
```

MCP flow for a real diff review:

```text
Start navlyn-mcp with --tool-profile review.
navlyn_review_diff(profile: "evidence")
```

If several review follow-ups are already needed, restart or configure the server with `--tool-profile full` before using `navlyn_batch`:

```text
navlyn_batch(requests: [
  { id: "tests", command: "tests-for-diff", profile: "compact" },
  { id: "api", command: "public-api-diff", base: "main", profile: "evidence" },
  { id: "packs", command: "review-pack", pack: ["async", "security"], profile: "evidence" }
])
```

Use `navlyn_batch` in the review flow only when those follow-up facts are already needed. Otherwise call the one relevant follow-up tool in `review` profile or stop after `navlyn_review_diff`.

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

## Agent Evidence Evals

Use [`evals/agent-evidence.md`](evals/agent-evidence.md) to evaluate wrong-symbol avoidance, pre-edit anchor presence, post-edit changed-symbol checks, tool-call count, JSON validity, stderr cleanliness, latency, output size, and expected-file presence.

# Navlyn Agent Evidence Evals

These evals focus on whether Navlyn helps an agent avoid wrong-symbol edits and stop after enough evidence. They complement the deterministic tool-selection eval in `docs/evals/tool-selection.md`.

Run the executable release smoke from the repository root:

```powershell
./scripts/test-agent-evidence-eval.ps1 -NoBuild
```

For a fast local loop that skips the MCP latency scenario:

```powershell
./scripts/test-agent-evidence-eval.ps1 -NoBuild -SkipMcpLatency
```

The script writes a JSON report under ignored `artifacts/evals/agent-evidence-eval-report.json`. It checks pre-edit anchor presence, post-edit guard fail-closed behavior on an empty diff, wrong-symbol guard behavior, intent/handoff/confidence projections, JSON validity, stderr discipline, and optional MCP warm latency.

## Metrics

Track these fields for each trace:

- `wrongSymbolAvoided`: whether the agent anchored the intended symbol before editing.
- `preEditAnchorPresent`: whether a `candidateId` or exact source-position result was recorded.
- `postEditChangedSymbolsChecked`: whether the dirty diff was inspected after editing.
- `changedSymbolsMatchAnchor`: whether changed symbols align with the pre-edit anchor by name, kind, container, project, path, or source span.
- `toolCallCount`: number of Navlyn calls in the task.
- `stdoutJsonValid` and `stderrClean`: automation contract health.
- `latencyMs` and `stdoutChars`: performance and downstream context cost.
- `expectedFilesPresent`: whether expected source or test files appear in related/context/review output.

## Scenario Set

| Scenario | Expected Navlyn Behavior | Pass Condition |
| --- | --- | --- |
| Ambiguous type name | Resolve target with `--assume-kind`, inspect candidates, ask if ambiguous | No edit starts without a selected target. |
| Overloaded method change | Resolve by source position or exact candidate, inspect callers/references | The edited member matches the anchored overload. |
| Partial class edit | Resolve target and inspect source locations or context pack | The agent sees the relevant partial declaration before editing. |
| Multi-target project | Use `--project` or inspect target framework facts | The edit is made in the intended target framework context. |
| Pre-edit evidence envelope | Run `edit-preflight` or `navlyn_edit_preflight` | Anchor, source evidence, context, confidence, known unknowns, and next guard command are present. |
| Diff review after edit | Run `post-edit-guard`, `wrong-symbol-guard`, or `review-diff` | Changed symbols are compared with the pre-edit anchor. |
| Related tests | Use `tests-for-symbol` or `tests-for-diff` only after an edit plan or diff | Test files are evidence, not a first-pass checklist. |
| Text-only task | Use file read or `rg` | No Navlyn call is made for Markdown/comments/config text. |
| MCP warm latency | Run the performance MCP scenario | The report is JSON-valid and records MCP tool timing for the release environment. |

## Trace Template

```json
{
  "scenarioId": "wrong-symbol-overload",
  "navlynVersion": "0.6.0",
  "workspace": "path/to/YourRepo.slnx",
  "prompt": "Change PaymentService.Process",
  "preEditAnchor": {
    "candidateId": "sym:v1:...",
    "name": "Process",
    "kind": "Method",
    "container": "Billing.PaymentService",
    "project": "Billing.Api(net10.0)",
    "path": "src/Billing/PaymentService.cs"
  },
  "navlynCalls": [
    "resolve-target",
    "symbol-source",
    "references",
    "changed-symbols"
  ],
  "postEditChangedSymbolsChecked": true,
  "changedSymbolsMatchAnchor": true,
  "toolCallCount": 4,
  "stdoutJsonValid": true,
  "stderrClean": true,
  "result": "pass"
}
```

## Manual Scoring

Use `pass` when the agent got the smallest useful semantic evidence and stopped. Use `partial` when the evidence was useful but broader than needed. Use `fail` when the agent skipped semantic anchoring before a risky C# edit, changed a different symbol than the anchor, or ran broad Navlyn workflows as a checklist.

For release notes, report summary counts and representative traces rather than model-specific claims. Keep raw local traces under ignored `artifacts/` unless a curated fixture trace is intentionally published.

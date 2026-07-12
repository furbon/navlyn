# Navlyn Tool-Selection Eval

This eval checks whether an agent chooses the smallest useful Navlyn surface for a request. It is a tool-choice policy eval, not a model benchmark.

For wrong-symbol avoidance, pre-edit anchors, and post-edit changed-symbol verification, see `docs/evals/agent-evidence.md`.

The machine-readable scenario file is `docs/evals/tool-selection.scenarios.json`. Run the baseline scorer from the repository root:

```powershell
./scripts/test-tool-selection-eval.ps1 -UseBaselineTraces
dotnet test navlyn.slnx --no-build --filter ToolSelection
```

To score an actual agent trace, write a JSON file with `traces` entries containing `scenarioId`, `chosenSequence`, `stopCondition`, `stdoutJsonValid`, and `stderrClean`, then pass `-TraceFile`.

The v0.7.0 set has 21 executable scenarios covering the canonical agent workflow, text-only no-Navlyn prompts, overloads, partial classes, multi-target context, generated-file avoidance, stale candidate handling, route and DI advanced facts, output-budget partial results, and public API release checks.

## Scoring

Mark each scenario:

- `pass`: the chosen tools match the expected first step and stop condition.
- `partial`: the tool is useful but broader than needed.
- `fail`: the agent uses Navlyn when text/file reading is enough, skips Navlyn when C# or Visual Basic semantic identity matters, or runs broad workflows as a checklist.

Record the prompt, chosen tool sequence, stop condition, and any stdout/stderr issues.

When evaluating MCP, assume the unified read-only tool surface with canonical tools first. `navlyn_target`, `navlyn_read`, `navlyn_prepare_edit`, `navlyn_verify_edit`, and `navlyn_review` are the primary workflow tools. Broad review, tests, public API, DI, context-pack, and batch tools are available but should not be chosen unless the prompt and returned evidence make them relevant.

Canonical scenarios score the canonical names as the correct first step. Older advanced aliases such as `navlyn_resolve_target`, `navlyn_symbol_source`, `navlyn_edit_preflight`, `navlyn_post_edit_guard`, and `navlyn_review_diff` remain supported compatibility tools, but a new agent trace should not receive full tool-selection credit for choosing them when the canonical tool answers the same question.

## Scenarios

| Scenario group | Expected First Step | Stop Condition | Avoid |
| --- | --- | --- | --- |
| Canonical symbol target/read/edit/review | `navlyn_target`, `navlyn_read`, `navlyn_prepare_edit`, `navlyn_verify_edit`, or `navlyn_review` matching the task | Candidate id, bounded source, pre-edit envelope, guard result, or review facts | Advanced tools as a checklist |
| Text-only prompts | `rg` or file read | Text match or Markdown section found | Any Navlyn command |
| Ambiguous/overloaded/partial/multi-target prompts | `navlyn_target` or `navlyn_prepare_edit` with narrowing fields | Ambiguity reported, selected project/overload, or partial context | Reading/editing from an ambiguous name alone |
| Generated/stale/output-budget prompts | `navlyn_target`, `navlyn_verify_edit`, or scoped edge facts | Generated exclusion/warning, stale or guard risk, partial result with rerun hint | Silent broad search or source dump |
| Domain/release prompts | `route-map`, `navlyn_di_impact`, or `navlyn_public_api_diff` | Domain-specific source facts with limitations | Runtime/security/API claims outside static evidence |

## Manual Trace Template

```text
Date:
Navlyn version:
Repository/workspace:
MCP surface, if any:
Prompt:
Chosen sequence:
Expected sequence:
Result: pass | partial | fail
Reason:
Follow-up change:
```

Use local performance reports for latency/output-size observations. Use this eval for tool choice and stopping behavior.

# Navlyn Tool-Selection Eval

This eval checks whether an agent chooses the smallest useful Navlyn surface for a request. It is a manual policy trace, not a model benchmark.

## Scoring

Mark each scenario:

- `pass`: the chosen tools match the expected first step and stop condition.
- `partial`: the tool is useful but broader than needed.
- `fail`: the agent uses Navlyn when text/file reading is enough, skips Navlyn when C# semantic identity matters, or runs broad workflows as a checklist.

Record the prompt, chosen tool sequence, stop condition, and any stdout/stderr issues.

## Scenarios

| Prompt | Expected First Step | Stop Condition | Avoid |
| --- | --- | --- | --- |
| "What does `CheckCommand` refer to?" | `navlyn_resolve_target` or `navlyn find` with `assumeKind: "NamedType"` | One high-confidence `candidateId` or an ambiguity to ask about | Running review or context tools |
| "Open the declaration for this known file position." | `navlyn_symbol_source` or CLI `symbol-source` with file/line/column | Bounded source for one symbol | Workspace summary |
| "Review this PR." | `navlyn_review_diff` with `profile: "evidence"` | Changed-symbol, diagnostic, impact, and related-test facts are available | Raw file outline as the first step |
| "Find install instructions in README." | Normal file read or `rg` | Relevant Markdown section found | Any Navlyn command |
| "Who calls this method?" | Resolve the target, then `navlyn_symbol_edges(operation: "callers")` or CLI `callers` | Callers answer the question or limits indicate rerun | `context-pack` before callers |
| "What files should be read before changing this symbol?" | Resolve the target, inspect references or impact, then `context-pack --goal modify` if needed | Bounded reading queue is present | Treating `nextActions` as a checklist |
| "What projects and target frameworks are in this repo?" | `navlyn_workspace_summary(profile: "compact")` or CLI `repo-graph --profile compact` | Project/target facts are returned | Symbol navigation |
| "Is this route protected?" | `route-map` or `route-impact` for ASP.NET facts | Route/auth source facts are returned with limitations understood | Runtime security claims |

## Manual Trace Template

```text
Date:
Navlyn version:
Repository/workspace:
Prompt:
Chosen sequence:
Expected sequence:
Result: pass | partial | fail
Reason:
Follow-up change:
```

Use local performance reports for latency/output-size observations. Use this eval for tool choice and stopping behavior.

# Navlyn Tool-Selection Eval

This eval checks whether an agent chooses the smallest useful Navlyn surface for a request. It is a tool-choice policy eval, not a model benchmark.

For wrong-symbol avoidance, pre-edit anchors, and post-edit changed-symbol verification, see `docs/evals/agent-evidence.md`.

The machine-readable scenario file is `docs/evals/tool-selection.scenarios.json`. Run the baseline scorer from the repository root:

```powershell
./scripts/test-tool-selection-eval.ps1 -UseBaselineTraces
dotnet test navlyn.slnx --no-build --filter ToolSelection
```

To score an actual agent trace, write a JSON file with `traces` entries containing `scenarioId`, `chosenSequence`, `stopCondition`, `stdoutJsonValid`, and `stderrClean`, then pass `-TraceFile`.

## Scoring

Mark each scenario:

- `pass`: the chosen tools match the expected first step and stop condition.
- `partial`: the tool is useful but broader than needed.
- `fail`: the agent uses Navlyn when text/file reading is enough, skips Navlyn when C# semantic identity matters, or runs broad workflows as a checklist.

Record the prompt, chosen tool sequence, stop condition, and any stdout/stderr issues.

When evaluating MCP, record the active startup tool profile. Default `reader` profile should make broad review, tests, public API, DI, context-pack, and batch tools unavailable as first-pass choices. Use `review`, `edit`, or `full` only when the prompt requires that surface.

## Scenarios

| Prompt | Expected First Step | Stop Condition | Avoid |
| --- | --- | --- | --- |
| "What does `CheckCommand` refer to?" | In MCP `reader` profile, `navlyn_resolve_target`; in CLI, `find` or `resolve-target` with `assumeKind: "NamedType"` | One high-confidence `candidateId` or an ambiguity to ask about | Running review, context, test, public API, DI, or batch tools |
| "Open the declaration for this known file position." | `navlyn_symbol_source` or CLI `symbol-source` with file/line/column | Bounded source for one symbol | Workspace summary |
| "Review this PR." | Start MCP with `--tool-profile review`, then `navlyn_review_diff` with `profile: "evidence"` | Changed-symbol, diagnostic, impact, and related-test facts are available | Raw file outline as the first step; forcing review tools into `reader` |
| "Find install instructions in README." | Normal file read or `rg` | Relevant Markdown section found | Any Navlyn command |
| "Who calls this method?" | Resolve the target, then `navlyn_symbol_edges(operation: "callers")` or CLI `callers` | Callers answer the question or limits indicate rerun | `context-pack` before callers |
| "What files should be read before changing this symbol?" | Start MCP with `--tool-profile edit`, resolve the target, inspect references or impact, then `navlyn_context_pack` if needed | Bounded reading queue is present | Treating `nextActions` as a checklist; running every exposed edit tool |
| "What projects and target frameworks are in this repo?" | `navlyn_workspace_summary(profile: "compact")` or CLI `repo-graph --profile compact` | Project/target facts are returned | Symbol navigation |
| "Inspect this known C# file before deciding if more facts are needed." | In MCP `reader` profile, `navlyn_file_outline` or `navlyn_inspect_file` | File outline answers or yields one selected follow-up | `navlyn_batch`, `navlyn_review_diff`, `navlyn_tests_for_diff`, `navlyn_public_api_diff`, `navlyn_di_impact` |
| "Is this route protected?" | `route-map` or `route-impact` for ASP.NET facts | Route/auth source facts are returned with limitations understood | Runtime security claims |

## Manual Trace Template

```text
Date:
Navlyn version:
Repository/workspace:
MCP tool profile, if any:
Prompt:
Chosen sequence:
Expected sequence:
Result: pass | partial | fail
Reason:
Follow-up change:
```

Use local performance reports for latency/output-size observations. Use this eval for tool choice and stopping behavior.

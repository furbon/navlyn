# Navlyn Tool-Selection Eval

This evaluation measures whether an agent chooses the smallest useful Navlyn MCP tool or an ordinary repository action, passes the right arguments, uses returned evidence, and stops at the right time. It is a routing contract, not a model benchmark or a claim that static analysis proves runtime behavior.

Run the checked-in v2 baseline from the repository root:

```powershell
./scripts/test-tool-selection-eval.ps1 -UseBaselineTraces
./scripts/test-tool-selection-eval.ps1
```

To score an observed run, provide a trace file with schemaVersion `navlyn.tool-selection-eval.trace.v2` and one trace per scenario id. Each call records an explicit `kind` (`mcp` or `ordinary`), exact invoked name, actual arguments, and selected result fields. MCP names must be from the current 25-tool surface; ordinary actions are `file-read`, `rg`, `git`, `build`, or `test`. A CLI logical command is not an MCP tool name. Include skill activation, stop reason, semantic checks, claims, availability/freshness environment, stdout character count, latency, `outputValid`, and stderr cleanliness. `outputValid` means valid MCP JSON for MCP calls, or the expected captured output format for the named ordinary action; it does not mean every ordinary output is JSON. MCP selected-result evidence is nested under the actual envelope key, for example `result.command` for success or `error.message` for a reported failure; ordinary evidence names the selected action output, such as `stdout` or `exitCode`. The baseline traces are synthetic contract fixtures, not captured product telemetry.

## Scoring contract

Every scenario scores eleven independent criteria: expected skill activation, first action kind/name, an exact accepted action sequence, avoidance of forbidden tools/sequences, required argument values, evidence-backed stop, semantic correctness checks, call/output/latency budgets, availability and freshness handling, unsupported-claim avoidance, and stdout/stderr behavior. Full score requires every criterion.

A stop reason alone is insufficient. It must be accepted by that scenario and every typed predicate in one evidence alternative must match the selected result fields of the specified call. Predicate paths exactly cover the alternative's `requiredFields`; supported checks are exact deep equality, integer minimum, array minimum cardinality, nonblank string, and required/minimum object properties. Predicate types are limited to string, number, boolean, array, and object; unknown keys, mismatched operators, and command-only alternatives are invalid. This preserves explicit `false` and `0` evidence, while requiring nonempty source slices and candidate lists. For example, missing-workspace accepts `result.workspace.loaded == false`; generated-code avoidance accepts candidate count zero only with the accompanying no-candidates state.

External v2 traces use strict JSON types: booleans remain booleans, measurements are nonnegative integer JSON numbers, calls/claims are arrays, and arguments, selected result fields, semantic checks, and environment are objects. Scenario-required semantic checks must be exactly boolean `true`; duplicate scenario IDs and numeric strings are rejected. `result.command` alone is not evidence that the requested fact was obtained: source reads should use fields such as `result.symbol.path` or `result.slices`, while a caller lookup should use `result.callers`. Unsupported claims are checked case-insensitively against scenario-declared fragments such as `runtime behavior` or `security guarantee`. Argument values, including arrays and objects, are compared structurally rather than as strings.

The scenario inventory covers all required routing classes:

| Task class | Typical first action |
| --- | --- |
| Ambiguous symbol identity, ambiguity | `navlyn_target` list |
| Exact position, overload, multi-project | `navlyn_target` or anchored `navlyn_read` |
| Partial declaration, pre-edit | `navlyn_prepare_edit` |
| References/callers, partial result | `navlyn_navigate` |
| Known-file outline | `navlyn_file_outline` |
| Multi-target, stale workspace | `navlyn_workspace_summary` / `navlyn_workspace_status` |
| Linked file | `navlyn_read` |
| Generated-code avoidance | `navlyn_target` with exclusion |
| Post-edit, stale candidate | `navlyn_verify_edit` |
| Actual diff review | `navlyn_review` |
| Diagnostics, DI, routes, options, messages, EF, packages | Focused domain tool |
| Context escalation | `navlyn_context_pack` |
| Two-fact batch | `navlyn_batch` |
| Comments, strings, Markdown, generated-artifact text, arbitrary text search | `rg` |
| Configuration, simple file read | `file-read` |
| Build/test execution | `build` or `test` |
| Missing workspace | `navlyn_doctor` |
| Unavailable MCP, explicit no-Navlyn override | Explicitly allowed ordinary fallback |

Other scenarios cover stale identity and task-boundary cases. Each machine-readable row includes fixture and workspace, activation expectation, first action, accepted and forbidden sequences, required arguments, stop evidence, semantic checks, maximum calls, output and latency budgets, unsupported claims, availability/freshness setup, and a baseline trace with actual arguments and selected result fields.

## Interpreting results

A failed criterion is actionable evidence about the routing contract; inspect the per-scenario `criteria` object rather than relying only on the aggregate score. A high score does not establish semantic truth or application behavior: it only establishes that the trace followed the declared route and that its recorded evidence satisfies the contract. Refresh workspaces when freshness is stale or unknown, state unavailability honestly, and do not claim runtime, security, delivery, or execution facts that the selected static result does not contain.

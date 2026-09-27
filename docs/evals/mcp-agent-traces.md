# Navlyn MCP Agent Trace Eval

This evaluation scores agent traces against Navlyn's routing and safety contract. The committed replay file contains synthetic contract fixtures, not telemetry or evidence of live agent effectiveness. Each replay row links by `scenarioId` to the authoritative v2 scenarios in `tool-selection.scenarios.json`; expectations are sourced from that file rather than duplicated in the observation.

Run the replay from the repository root:

```powershell
./scripts/test-mcp-agent-trace-eval.ps1
```

To score another v2 trace file, pass `-TraceFile`; the default scenario source may be overridden with `-ScenarioFile`, and `-Output` selects the JSON report destination. Every trace records skill activation, ordered MCP or ordinary calls with arguments and selected returned fields, stop reason, semantic checks, claims, environment availability/freshness, edit timing and anchor state, broad-checklist state, stdout size, latency, output validity, and stderr cleanliness. JSON types are strict: flags are booleans, measurements are nonnegative integer numbers, calls and claims are arrays, evidence/arguments/environment/checks are objects, and duplicate scenario IDs are rejected.

Each linked scenario declares typed stop predicates whose paths exactly cover its required evidence paths. The bounded predicate vocabulary supports exact deep equality, integer minimum, array minimum cardinality, nonblank strings, and required/minimum object properties, over string/number/boolean/array/object values. Unknown keys and mismatched operators/types are rejected. Stop evidence passes only when the stop reason is accepted and every predicate in one alternative passes; explicit `false` and zero values are preserved rather than treated as missing.

## Replay results

The current replay has 34 scenario-linked traces: 27 semantic-work cases and 7 ordinary-action cases, including text-only work and unavailable/override conditions. It reports per-trace criteria and aggregate numerator/denominator metrics. The generated report is `artifacts/evals/mcp-agent-trace-report.json`.

The replay currently meets its applicable contract gates: semantic Navlyn recall 27/27; all 2/2 semantic MCP edit attempts anchored before editing; text-only false positives 0/5; correct first action 34/34 overall and 15/15 for the core seven; legacy MCP calls 0/27; ambiguous-target edits 0; stale-candidate silent reuse 0; unsupported claims 0; evidence-backed stops 34/34; broad checklists 0/34; and within-call-budget traces 34/34. The canonical-loop stdout sample is 15 traces, p95 1,500 characters and maximum 1,500 characters.

These results are fixture/scorer evidence only. Live-only skill-on versus skill-off latency comparison and live `tools/list` payload comparison are not applicable to this replay. The report also marks new focused tool invalid-input coverage and existing CLI regression gates not applicable; those require their focused contract suites.

## Acceptance gates

All applicable safety metrics are hard gates. The scorer evaluates semantic Navlyn recall (at least 95%), identity-critical edit anchoring (100%), text-only Navlyn false positives (at most 5%), correct first action (at least 92% overall and 95% for the core seven where applicable), removed/legacy MCP use (zero), ambiguous-target edits (zero), stale-candidate silent reuse (zero), unsupported runtime/security/test claims (zero), evidence-backed stop (at least 95%), broad checklist use (at most 5%), call-budget compliance (at least 95%), and canonical-loop stdout p95/max (at most 20,000/40,000 characters).

The replay cannot establish skill-on latency regression versus skill-off or `tools/list` size versus v0.7; those remain live comparison gates. Focused invalid-input coverage and existing CLI contract regressions are separate test gates, not replay claims. A passing replay does not complete those gates or establish skill effectiveness.

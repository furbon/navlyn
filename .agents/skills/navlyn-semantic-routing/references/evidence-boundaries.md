# Evidence Boundaries

Use these rules whenever a semantic result could be overread. Report the observed evidence, its scope, and any limit that affects the conclusion.

## Confidence values and ambiguity handling

Confidence values are `high`, `medium`, or `low`; preserve the value and the evidence that produced it. Confidence is not certainty. When multiple candidates remain material, fail closed: do not read or edit one as though it were uniquely intended. Ask the user, refine the query with supported project/kind constraints, or use `navlyn_target(mode: "list")` only when broader candidate discovery is explicitly appropriate. Do not convert an unresolved list into a selection by ranking alone.

## Candidate freshness and stale-ID behavior

`candidateId` is a reusable anchor for the current indexed workspace, not a permanent symbol identifier. Source edits, project changes, target-framework changes, or refreshes can invalidate it. If Navlyn rejects a stale ID, resolve the target again from current source and inspect confidence; never substitute a different candidate silently. Reuse the ID during one coherent investigation while it remains accepted and its workspace context is unchanged.

## Workspace cache freshness and refresh triggers

Read freshness, snapshot identity, cache status, warnings, and workspace fingerprint when available. A warm cache means a cached workspace was used; it does not prove the source is current on disk or that a build ran. Refresh when results conflict with current files, cache metadata indicates a stale or unavailable snapshot, a load failure needs recovery, or the user explicitly requests refresh. Do not refresh by default: it costs time and does not execute compilation or application code.

## Partial and truncated results

Inspect `partial`, truncation, result limits, scope, cost class, warnings, and rerun hints. A partial or truncated result describes only returned evidence. Narrow or widen scope deliberately, increase an allowed limit when it changes the decision, or follow a specific rerun hint. A missing result in bounded output is not proof of absence. State what was searched and what could not be established.

## Generated-code distinction

Generated source can expose compiler-visible facts without expressing hand-authored intent. Check generated markers and `excludeGenerated` behavior. Include generated files only when the task concerns generated output or framework/build-produced declarations. Label generated evidence; do not infer author intent, source-of-truth ownership, or behavior of the generator from generated code alone.

## Linked-file project context

A linked source path may compile under multiple projects, references, symbols, or target frameworks. Preserve project and target-framework context when selecting, reading, navigating, or comparing it. The same path is not necessarily the same semantic symbol. If the context is unknown or changes the result, inspect workspace/project facts or ask which project/framework is intended.

## Static versus runtime proof boundaries

Navlyn reports indexed source and workspace facts. It does not establish runtime dispatch, reflection behavior, effective authorization, DI container resolution, deployed configuration values, database state, package loading, test execution, build success, or production reachability. Source patterns can suggest behavior but do not prove it occurs. Use runtime, build, test, deployment, or security tools when those claims are required, and keep the claim proportional to that evidence.

## Unavailable MCP fallback

If MCP is unavailable, do not invent a successful Navlyn result or repeatedly retry without reason. Use ordinary file reads and `rg` for text, Git for diffs, and the repository's normal build/test tools for execution. Use CLI commands only when the user or task authorizes them and the CLI is available; distinguish a CLI action from an MCP tool. Explain which semantic checks could not be performed.

## Missing workspace or ambiguous workspace fallback

For a missing or unloadable workspace, use `navlyn_doctor` only when setup/load diagnosis is the question. Check the configured path and workspace status; use refresh only when cache freshness or recovery justifies it. If several repositories, projects, or target frameworks could be intended, ask or select explicitly rather than choosing the first. If Navlyn cannot load the relevant context, fall back to ordinary inspection and state the limitation.

## Explicit user override behavior

An explicit user instruction not to use Navlyn takes precedence; use the requested ordinary tools and do not call an MCP tool. If the user requests a CLI action, do not silently relabel it as an MCP call. If the user asks for runtime proof, tests, edits, formatting, or publishing, use the corresponding authorized tool rather than claiming Navlyn performed it. An override changes routing, not the evidence standard.

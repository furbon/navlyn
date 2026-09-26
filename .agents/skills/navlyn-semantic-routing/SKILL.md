---
name: navlyn-semantic-routing
description: Use Navlyn for semantic C# or Visual Basic decisions that require resolving a symbol or source position, selecting an overload, declaration, or binding context across projects or target frameworks, inspecting semantic structure or relationships, distinguishing partial, linked, or generated source, checking workspace freshness or diagnostics, assessing edit or Git-diff risk, or querying supported .NET domain facts. When one of these decisions is requested, load this skill before any repository action and follow its first-tool routing. Never load merely to locate or read a supplied literal or path, even inside source code; use one ordinary read or search for that, and honor explicit user exclusion. Reading a declared value from a named project file is the same ordinary-read case unless the value must guide a requested semantic binding decision.
---

## Authority And Boundary

Explicit user instructions win. This skill routes investigation; it does not authorize extra work. Navlyn supplies bounded static facts about C# and Visual Basic workspaces. It does not edit, build, test, format, publish, or prove runtime behavior. Use ordinary reads and `rg`, Git, build, and test tools when they directly answer the question. Do not turn optional Navlyn follow-ups into a checklist.

## Decide Whether Navlyn Is Needed

Activate when symbol identity, exact location, overloads, partial declarations, project/target-framework selection, or relationships could change what you inspect, edit, or answer. It also supports workspace and .NET domain facts, loaded diagnostics, test candidates, edit risk, and actual diff review. If no semantic fact is uncertain, use normal tools and stop.

## Choose The First Tool

For approximate symbol intent, start with `navlyn_target` in default select mode. An approximate symbol with a supplied project or target framework still starts with `navlyn_target` using that context; do not escalate it to `navlyn_context_pack`. Use `navlyn_target(mode: "list")` only for explicit broader discovery; it does not select. A requested relationship with a supplied source position starts directly with `navlyn_navigate`; do not pre-read the declaration or signature. For another supplied source position, start with `navlyn_read` using the file, line, and column, except that a request to prepare before editing starts with `navlyn_prepare_edit`. For a requested outline of a known code file, call `navlyn_file_outline` once and answer from nonempty entries. Do not add a full-file read or text scan because the outline is long or client display is clipped; report the visible boundary and stop. Use `navlyn_navigate` for a precise relationship or symbol fact. Use `navlyn_workspace_summary` only when workspace structure could change the answer. Doctor and workspace status/refresh are for setup, failure, or freshness questions.

Text intent stays ordinary. A request to find or search an exact string, heading, regex, comment, or document uses `rg` as its first action, even in one file; never substitute a file read. Pass a supplied exact pattern and path unchanged. A request to read a known file or configuration value uses a file read first, not `rg` or a Navlyn workspace summary. Project or target-framework context activates Navlyn only when it affects semantic selection. Respect an explicit request not to use Navlyn.

## Anchor, Follow Up, And Stop

Reuse a returned `candidateId` instead of resolving the same symbol again. Inspect confidence, warnings, project context, freshness, scope, cost, partial/truncated state, and rerun hints. Fail closed on ambiguity: do not treat one candidate as uniquely intended while alternatives remain material. Clarify or use explicit list mode when broader discovery is appropriate. Next actions are optional. Stop when the current fact answers the question; do not add navigation, impact, tests, context, or a full-file read merely because they are available.

`navlyn_navigate` handles definition, references, callers, calls, implementations, type hierarchy, and symbol info. Choose one operation. References and callers may be scoped, partial, or truncated; inspect those flags before broad conclusions. Use `navlyn_read` when source text is the missing fact. `navlyn_diagnostics` reports diagnostics already present; it does not run a build. Treat test candidates and domain results only as bounded evidence, never as executed tests.

## Edit And Review Loops

For a concrete non-trivial semantic edit, call `navlyn_prepare_edit` directly with the known candidate or intended query/location. It is not required for every edit. Edit outside Navlyn with normal tools. Afterward, use `navlyn_verify_edit` when a target-to-diff guard can expose wrong-target risk. A mismatch needs inspection and does not replace build or test validation. Use `navlyn_review` only for an actual Git diff, staged change, ref comparison, or PR.

Use `navlyn_context_pack` only when smaller facts are insufficient and a bounded reading queue would help. Use `navlyn_batch` only for two or more already-selected, supported facts from one workspace. Prefer one focused tool for one question.

## Ambiguity, Freshness, And Failure

Candidate IDs are workspace evidence, not durable identifiers. To check whether a supplied candidate is stale, call `navlyn_verify_edit` once in candidate mode; report rejection and stop. If later work must continue after rejection, rerun target selection from current source instead of guessing. Check workspace freshness when results conflict with files, and refresh only when cache evidence or load failure justifies it. Keep project and target-framework context explicit for multi-project, multi-target, linked, or generated files.

Treat unavailable MCP, missing workspaces, partial results, and load diagnostics as limits. When a relevant ordinary read, `rg`, or Git fallback is available, execute it and answer from that evidence instead of merely recommending a fallback. Otherwise ask for what is needed.

## Proof Boundaries

Navlyn results are static source or workspace evidence within reported limits. They do not establish runtime dispatch, reflection, authorization, DI state, configuration values, database state, package loading, test execution, or build success. Absence is not proof of no behavior. Generated code, linked files, target frameworks, stale caches, warnings, and truncation affect visibility. State the boundary; do not claim runtime proof.

## References

Read the [routing matrix](references/routing-matrix.md) when task class or first-tool choices compete. Read the [evidence boundaries](references/evidence-boundaries.md) when confidence, stale IDs, freshness, generated/linked source, partial output, or static-versus-runtime claims matter. These references do not override user instructions.

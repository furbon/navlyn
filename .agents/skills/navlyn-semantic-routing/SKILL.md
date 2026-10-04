---
name: navlyn-semantic-routing
description: Use Navlyn when C# or Visual Basic symbol identity, binding, relationships, compilation context, or workspace diagnostics need semantic evidence. Ordinary file reading, literal search, and locally clear edits use normal tools; honor the user's tool choice.
---

## Choose What Resolves The Uncertainty

Use ordinary reads and `rg` for supplied paths, strings, comments, configuration values, and source that directly answers the question. A C# file, an overload, or an edit does not by itself require Navlyn. Use semantic evidence when binding, project/target-framework context, partial/linked source, or relationships remain uncertain or the user requests compiler-resolved facts. Text can locate an anchor; it does not establish unresolved binding or complete callers.

For an exact search, pass the supplied pattern and path to `rg`; for a requested file read, read that file. Do not load workspace summaries or preparation evidence as a preamble. Prefer the smallest sufficient inspection, including a bounded text read before semantic investigation when it supplies a missing location.

## Focused Semantic Calls

- Approximate symbol intent: `navlyn_target` in default select mode. Use list mode for an actual candidate-discovery task, not a prerequisite to selection. Reuse returned `candidateId` values with their project/framework context.
- Known candidate or source position plus a relationship: call `navlyn_navigate` directly with the requested operation. Source/signature missing: `navlyn_read`. For a referenced-library body, use `navlyn_read` at the known call position with `externalSource: "decompiled"` and `view: "body"` when static implementation evidence is needed; it selects the bound overload without writing an IL inspector. Do not resolve or reread an already known target.
- Requested semantic outline of one file: `navlyn_file_outline`. Use its entries when sufficient; if output is clipped or the missing fact is source text, obtain only the needed text rather than stopping an unfinished task or repeating a large outline.
- Workspace structure or load/freshness failure: summary, doctor, status, or refresh only when that is the missing fact. Refresh requires a reason.

The focused surface and compact results are the default. Request `resultProfile: "full"` only when an omitted structured field is needed; signatures and selectors are retained. Advanced tools require a full surface or the CLI.

Queries accept simple or qualified declaration names, such as `Formatter.Format`; material overload ambiguity remains explicit. Use `typeKind` to filter class/interface/struct/record candidates independently of ranking hints. Do not discard qualifiers or choose an overload from rank alone. Fuzzy query options such as `assumeKind` and `explainSelection` do not belong in source-position target mode. Navigate `scope` and `maxDocuments` apply only to references/callers.

## Edits, Limits, And Stopping

For an unambiguous local literal or body edit, inspect the source, edit with normal tools, and check the actual diff. Use `navlyn_prepare_edit` when target identity or semantic consequences need preparation, and `navlyn_verify_edit` when a diff-to-intent guard answers a real risk. Impact, tests, review, and context packs are optional investigations, not an edit checklist. Batch only already chosen supported facts.

Stop when the question is answered. Inspect warnings, confidence, scope, and partial/truncated flags that affect the conclusion; do not chase optional next actions. Resolve material ambiguity using available source and project context before asking the user. A rejected stale candidate must be reselected from current source before continued semantic work; do not substitute another symbol silently.

When MCP or a relevant tool is unavailable, use an available Navlyn CLI for the needed semantic fact, or perform useful ordinary inspection and state the remaining uncertainty. Do not repeatedly retry a failed route or require extra installation for a text task. Navlyn supplies static facts; use normal build/test/runtime tools for execution evidence. User instructions take precedence.

Read [advanced routing](references/routing-matrix.md) only for the relevant advanced task, or [evidence boundaries](references/evidence-boundaries.md) when a returned limit needs interpretation. Do not read both references routinely.

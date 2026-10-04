---
name: navlyn-semantic-routing
description: Use Navlyn for uncertain C# or Visual Basic binding, relationships, compilation context, or referenced-library implementation. Ordinary reads, literal search and clear local edits use normal tools.
---

Use semantic evidence when binding, project/framework context, partial or linked declarations, or relationship completeness could change the answer. A C# file or an overload alone is not a reason to use Navlyn. Honor the user's tool choice.

For directly readable values, supplied text and locally clear edits, use ordinary reads, `rg`, and edit tools. A known source position needs no file inventory, workspace summary, outline, or target-selection preamble. Read text to obtain a missing anchor; stop reacquiring it once known.

## Choose One Missing Fact

- Known candidate or position: `navlyn_read` for bounded source; `navlyn_navigate` for one relationship. Reuse candidateId and its project/framework context.
- Referenced DLL implementation: call `navlyn_read` at the existing call position with `externalSource: "decompiled"`, `view: "body"`. It resolves the bound overload and returns static implementation evidence in one call; an IL-inspector script is not a prerequisite.
- Approximate declaration intent: `navlyn_target`, normally select mode. Use list only for requested candidate discovery. Qualified names constrain containers; overload ambiguity still requires evidence. `typeKind` filters actual types, while `assumeKind` is a ranking hint.
- Requested semantic structure of one file: `navlyn_file_outline`. Respect page bounds; retrieve only missing entries or text.

When using an installed CLI instead of MCP, the same referenced-member shortcut is:

```text
navlyn read --workspace <project> --file <source> --line <line> --column <column> --view body --external-source decompiled
```

CLI access needs no running MCP server. Ordinary work needs neither a Navlyn call nor routine loading of this skill's references. Do not install tools or retry a broken route merely to read text.

## Evidence And Edits

Use compact results normally: signature/body views retain the complete bounded source in either profile. Request full only for a needed omitted structured field. Carry project and targetFramework when they affect binding. Fuzzy query controls do not belong in source-position mode; navigation scope/maxDocuments apply only to references/callers.

Inspect scope, warnings, ambiguity, freshness and truncation before drawing a conclusion. Reselect a rejected stale candidate from current source. Static facts do not prove runtime behavior; use normal builds/tests for execution evidence.

Edit clear local literals/bodies directly and inspect the diff. Preparation, verification, impact and test-candidate tools answer particular uncertainties; they are not an edit checklist. Stop when the requested evidence is complete.

Read [advanced routing](references/routing-matrix.md) only for a needed advanced capability, or [evidence boundaries](references/evidence-boundaries.md) when interpreting a returned limit.

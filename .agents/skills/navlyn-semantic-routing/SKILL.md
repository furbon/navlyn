---
name: navlyn-semantic-routing
description: Use for referenced DLL bodies, unresolved C#/VB compiler binding or project/framework context, and relationships whose completeness is unclear from readable source. Skip routine reading, search and clear local edits.
---

Use Navlyn when compiler binding, project/framework context or relationship completeness could change the answer. Honor the user's tool choice. For readable source, settings and clear local edits, use ordinary reads, scoped `rg` and edit tools, including across many files. Codebase size alone does not require semantic tools. Reuse a known position; no inventory, summary, outline or target preamble is needed.

## Choose One Missing Fact

- Known candidate or position: `navlyn_read` for bounded source; `navlyn_navigate` for one relationship. Retain candidateId and project/framework context.
- DLL implementation: `navlyn_read` at the source call with `externalSource: "decompiled", view: "body"` selects its bound overload, including constructors at constructed type tokens. For internal follow-through, retain that anchor: `view: "members"` lists the containing type's exact IDs; `externalMember: "T:..."` lists another type, `"M:..."` reads its exact body, `"F:..."` reads a constant with declaration view. Returned `externalAssembly` identifies the selected member and PE; root symbol remains the source anchor. No reflection/IL script is a prerequisite.
- Approximate source intent: `navlyn_target`, normally select mode. It does not search DLL internals. Qualified names constrain containers; resolve overload ambiguity from evidence. `typeKind` filters actual types; `assumeKind` ranks them.
- Requested file structure: `navlyn_file_outline`. Respect page bounds; retrieve only missing evidence.

When using an installed CLI instead of MCP, the same referenced-member shortcut is:

```text
navlyn read --workspace <project> --file <source> --line <line> --column <column> --view body --external-source decompiled
```

CLI needs no MCP server. Use `--view members` and `--external-member <ID>` for the same DLL follow-through. Ordinary work needs neither a Navlyn call nor loading these references.

## Evidence And Edits

Use compact normally; bounded signature/body text is retained. Request full for a needed omitted field. Keep relevant project/targetFramework. Query controls do not belong in position mode; scope/maxDocuments apply only to references/callers.

Inspect scope, warnings, ambiguity, freshness and truncation. Reselect rejected stale candidates. Static facts do not prove execution; use normal builds/tests. Edit clear local bodies directly and inspect the diff. Advanced preparation/impact/test tools answer specific uncertainties, not a mandatory checklist. Stop when the requested evidence is complete.

Read [advanced routing](references/routing-matrix.md) only for a needed advanced capability, or [evidence boundaries](references/evidence-boundaries.md) when interpreting a returned limit.

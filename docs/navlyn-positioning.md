# Navlyn Positioning

Navlyn is read-only C#-first .NET semantic evidence for coding agents before they edit the wrong symbol, with Roslyn-backed Visual Basic support.

It is intentionally narrower than an editor, analyzer platform, hosted index, or review bot. That narrowness is the point: Navlyn gives agents and automation stable local facts, then leaves editing, testing, publishing, and judgment to the surrounding workflow.

## The Practical Difference

Most tools answer one of these questions:

- "Where does this text appear?"
- "What can my editor navigate to?"
- "What rule diagnostics should be reported?"
- "What comment should a bot publish?"

Navlyn answers a different question:

```text
Before this agent edits, what source-level C# or Visual Basic facts prove it is looking at the intended symbol and the relevant nearby relationships?
```

That is why the core loop is anchor-first:

1. `resolve-target` turns fuzzy intent into a selected Roslyn symbol and a reusable `candidateId`.
2. File-first and selected-symbol commands return bounded source, references, callers, implementations, tests, DI, route, or impact facts.
3. The edit happens outside Navlyn.
4. `post-edit-guard`, `wrong-symbol-guard`, or `review-diff` checks what actually changed.

The value is not a larger checklist. The value is keeping the agent attached to one semantic target until the returned facts justify expanding the scope.

## Category Fit

| Alternative | Use It For | Navlyn's Job |
| --- | --- | --- |
| `rg` and file reads | Comments, strings, docs, config, quick text probes | C# or Visual Basic symbol identity, project context, and source-level relationships when text search is ambiguous. |
| LSP or IDE | Interactive navigation, rename, diagnostics while a human edits | Stable JSON facts for agents, scripts, CI, and MCP clients. |
| Roslyn APIs and analyzers | Custom compiler tooling and rule diagnostics | Ready-to-run read-only evidence workflows without writing an analyzer. |
| Generic code-search MCP | Cross-language or text-level repository search | C# and Visual Basic MSBuild/Roslyn facts: target frameworks, symbols, references, DI, routes, tests, and review evidence. |
| Editing-capable MCP | Inspecting and modifying code from one client | Client-neutral evidence with no edit surface, no shell, and no network listener. |
| CI review bot | Publishing comments or pass/fail checks | Local review facts a human or agent can inspect before deciding what to say. |
| Hosted code search | Cross-repository hosted indexing | Repository-local analysis without sending source to a hosted service. |

## What To Say

Short:

```text
Read-only C#-first .NET semantic evidence for coding agents before they edit the wrong symbol, with Visual Basic support.
```

Longer:

```text
Navlyn is a local CLI and stdio MCP server for C#-first .NET repositories, with Visual Basic support through Roslyn/MSBuild. It uses Roslyn/MSBuild to resolve fuzzy intent into reusable symbol anchors, return bounded source and relationship facts, and help agents verify changed symbols after an edit without giving Navlyn an edit or execution surface.
```

For C#/.NET teams, and Visual Basic projects that MSBuild/Roslyn can load:

```text
Navlyn helps agents reason about overloads, partial declarations, target frameworks, generated or linked files, DI registrations, ASP.NET routes, related tests, and public API changes before they touch code.
```

For agent-platform or MCP users:

```text
Navlyn gives an agent narrow, profile-gated semantic tools over stdio. The default reader profile supports setup checks, file outlines, target resolution, bounded source, and selected-symbol edges; edit and review profiles are opt-in.
```

## What Not To Claim

Do not describe Navlyn as:

- a runtime proof engine;
- a test runner;
- an editor, refactoring engine, or LSP replacement;
- a security scanner or secret scanner;
- a package compatibility oracle;
- a hosted code-search product;
- a review bot that publishes final comments.

Navlyn reports bounded source-level evidence. Routes, authorization, dependency injection, EF models, package usage, public API diffs, and impact facts are useful inputs for review, but they are not complete runtime, security, or compatibility proofs.

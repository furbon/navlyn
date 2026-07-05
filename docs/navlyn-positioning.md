# Navlyn Positioning

Navlyn is semantic evidence before edit for C#/.NET coding agents. It is intentionally narrower than an editor, analyzer platform, hosted index, or review bot.

## Category Fit

| Alternative | Use It For | Navlyn's Job |
| --- | --- | --- |
| `rg` and file reads | Comments, strings, docs, config, quick text probes | C# symbol identity, project context, and source-level relationships when text search is ambiguous. |
| LSP or IDE | Interactive navigation, rename, diagnostics while a human edits | Stable JSON facts for agents, scripts, CI, and MCP clients. |
| Roslyn APIs and analyzers | Custom compiler tooling and rule diagnostics | Ready-to-run read-only evidence workflows without writing an analyzer. |
| Generic code-search MCP | Cross-language or text-level repository search | C# MSBuild/Roslyn facts, target frameworks, symbols, references, DI, routes, tests, and review evidence. |
| Editing-capable MCP | Inspecting and modifying code from one client | Client-neutral evidence with no edit surface, no shell, and no network listener. |
| CI review bot | Publishing comments or pass/fail checks | Local review packs a human or agent can inspect before deciding what to say. |
| Hosted code search | Cross-repository hosted indexing | Repository-local analysis without sending source to a hosted service. |

## What To Say

Short:

```text
Navlyn gives C# coding agents Roslyn-backed semantic evidence before they edit.
```

Longer:

```text
Navlyn is a read-only CLI and MCP server for C#/.NET repositories. It resolves fuzzy intent into stable symbol anchors, returns bounded source and relationship facts, and helps agents verify changed symbols after an edit without taking an edit or execution dependency.
```

## What Not To Claim

Navlyn does not prove runtime behavior, execute tests, replace an IDE, scan secrets, decide SemVer, or publish review comments. It reports bounded source-level evidence that should guide human or agent judgment.

## The Agent Loop

Use Navlyn where a wrong symbol would matter:

1. Pre-edit: `resolve-target` anchors the intended symbol.
2. Read: `symbol-source`, `references`, `about`, `impact`, or `context-pack` provide bounded evidence.
3. Edit outside Navlyn.
4. Post-edit: `changed-symbols` and `review-diff` show what actually changed and what evidence should be inspected next.

The value is not a larger checklist. The value is keeping the agent anchored to one semantic target until the returned facts justify expanding the scope.

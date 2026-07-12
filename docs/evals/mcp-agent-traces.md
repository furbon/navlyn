# Navlyn MCP Agent Trace Eval

This eval scores replayable or live MCP-agent traces against Navlyn's agent-behavior contract. It is stricter than tool-selection scenarios because it measures aggregate behavior:

- Navlyn is used for semantic C# or Visual Basic work.
- Navlyn is not used for text-only Markdown/comment/config tasks.
- Agents do not run broad checklist workflows by default.
- Risky edits do not happen before a semantic anchor.
- Stop conditions are recorded.
- Canonical-loop output remains within an agent-friendly budget.

Run from the repository root:

```powershell
./scripts/test-mcp-agent-trace-eval.ps1
```

The committed trace file is replayable policy evidence, not a substitute for live agent traces. Fresh Codex, Claude, Copilot, or other client traces should use the same schema and be passed with `-TraceFile`.

Navlyn MCP currently exposes one unified read-only tool surface; older `reader`, `review`, `edit`, and `full` profiles are accepted as deprecated compatibility aliases. Surface discipline is enforced by placing the canonical loop first, marking compatibility aliases as advanced in tool descriptions, and scoring traces for broad-checklist overuse. The focused contract tests live in `NavlynMcpToolDescriptionTests`.

## Pass Gates

- total score >= 0.97;
- text-only false-positive Navlyn use <= 5%;
- broad checklist overuse <= 10%;
- risky edit before semantic anchor = 0;
- stop condition success >= 95%;
- canonical-loop p95 stdout <= 20,000 chars;
- canonical-loop max stdout <= 40,000 chars.

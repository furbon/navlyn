# Navlyn v0.7.0 External Review Packet

Navlyn is a local read-only C#/.NET semantic evidence tool for coding agents. It turns an edit intent into a selected symbol target, bounded source/context/test evidence, and a post-edit guard that checks whether the actual diff stayed on target.

## 15-Minute Path

Start with `docs/navlyn-first-15-minutes.md`.

Canonical CLI:

```powershell
navlyn doctor --workspace auto
navlyn target --workspace auto --query PaymentService --assume-kind NamedType
navlyn read --workspace auto --candidate-id sym:v1:...
navlyn prepare-edit --workspace auto --candidate-id sym:v1:... --goal modify --change-kind behavior
navlyn verify-edit --workspace auto --candidate-id sym:v1:... --fail-on-risk high
navlyn review --workspace auto --profile evidence
```

Canonical MCP tools:

```text
navlyn_target
navlyn_read
navlyn_prepare_edit
navlyn_verify_edit
navlyn_review
```

## Evidence

- Canonical CLI/MCP surface implemented and tested.
- Tool-selection eval: 21 scenarios, score 1.0.
- Agent evidence eval: 6/6 passed with score summary and output/tool metrics.
- MCP/Workspace/Contract/Generated/Symbol stress tests: 176/176 passed on net8.0 and net10.0.
- Package install smoke passed on net8.0 and net10.0.
- External local repos: SymbolNaming resolved a target with clean `doctor.ok: true` after setup; TagGroupJumper and BeltHell resolved targets but retained degraded restore/workspace warnings.
- Fresh Phase 0 baseline on 2026-07-12: restore, build, xUnit on net8.0/net10.0, quick checks, CLI contract all suite, and public readiness audit passed. The CLI contract all suite completed in about 399 seconds on this machine.

## Limitations

- Navlyn does not edit files, run tests, call the network, or prove runtime behavior.
- Candidate IDs are opaque and not guaranteed stable across edits or workspace changes.
- External validation is still local-clone evidence, not a clean-room third-party adoption corpus.
- The public packet does not yet include fresh live-agent MCP traces.
- Full performance `Scenario all` must be rerun for any new 9.7+ release claim; do not infer that result from this packet alone.

## Breaking / Migration

Prefer `target`, `read`, `prepare-edit`, `verify-edit`, and `review`. Existing advanced commands remain supported.

## Verification Commands

```powershell
dotnet restore navlyn.slnx
dotnet build navlyn.slnx
dotnet test navlyn.slnx --no-build
./scripts/test-quick.ps1 -NoBuild
./scripts/test-cli-contract.ps1 -NoBuild
./scripts/test-contract-schemas.ps1 -NoBuild
./scripts/test-tool-selection-eval.ps1 -UseBaselineTraces
./scripts/test-agent-evidence-eval.ps1 -NoBuild
./scripts/audit-public-readiness.ps1
```

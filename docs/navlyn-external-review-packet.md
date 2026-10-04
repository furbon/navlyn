# Navlyn v0.8.6 External Review Packet

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

## Current scope and evidence

The current public surface contains 25 read-only MCP tools and .NET 8/10 tool assets. v0.8.6 fixes common MCP deadlines, executable workflow reproduction, schema-wrapper failure propagation, and historical-version preservation. CLI/MCP/batch tests cover natural kind aliases, whitespace, casing, duplicates, and invalid inputs. Type aliases such as `interface` all mean `NamedType`; they do not exclude classes.

Release evidence comes from the exact merged-main three-OS CI run and its retained packages, setup bundle, manifests, TRX, logs, and stage timings. Protected publication verifies those immutable inputs; annotated-tag verification reuses their evidence and independently checks the public packages. Consult the v0.8.6 release links for the completed run rather than treating old fixture counts as current results.

The evaluation scripts include recorded baseline traces and synthetic scenario checks. They establish regression coverage, not a demonstrated improvement in live-agent outcomes. Broader real-task comparisons and MCP SDK 2.0 migration belong in the next minor-release evaluation.

## Historical v0.7.0 evidence (2026-07-12)

- Canonical CLI/MCP surface implemented and tested.
- Tool-selection eval: 21 scenarios, score 1.0.
- Agent evidence eval: 6/6 passed with score summary and output/tool metrics.
- MCP/Workspace/Contract/Generated/Symbol stress tests: 176/176 passed on net8.0 and net10.0.
- Package install smoke passed on net8.0 and net10.0.
- External local repos: SymbolNaming resolved a target with clean `doctor.ok: true` after setup; TagGroupJumper and BeltHell resolved targets but retained degraded restore/workspace warnings.
- Fresh Phase 0 baseline on 2026-07-12: restore, build, xUnit on net8.0/net10.0, quick checks, CLI contract all suite, and public readiness audit passed. The CLI contract all suite completed in about 399 seconds on this machine.

## Limitations

- Navlyn's tool surface does not edit files or run tests, and static facts do not prove runtime behavior. Load trusted repositories: MSBuild evaluation and repository-supplied tasks, analyzers, and generators are not sandboxed. See [SECURITY.md](../SECURITY.md).
- Candidate IDs are opaque and not guaranteed stable across edits or workspace changes.
- External validation is still local-clone evidence, not a clean-room third-party adoption corpus.
- The public packet does not yet include fresh live-agent MCP traces.
- Performance claims require a dated report identifying version, commit, SDK, OS, workspace, scenario, profile, and warmup. Historical numbers do not establish current latency.

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

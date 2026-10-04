# Navlyn development workflow

## Product boundaries

Navlyn is a repository-local semantic navigation CLI and stdio MCP server. Keep public behavior in [CLI commands](navlyn-cli-commands.md) and [MCP server](navlyn-mcp-server.md) synchronized with implementation. Keep stdout for command JSON and stderr for diagnostics. Use Roslyn/MSBuild for semantic behavior and repository-relative normalized paths in output.

Read source before changing it. Use `rg` for literals, comments, documentation, and fallback investigation; use existing Navlyn commands for supported semantic questions. Review linked files, overloads, partial/generated declarations, project/framework selection, freshness, limits, and error envelopes when those concerns apply.

## Short feedback loop

Start with affected component tests and one manual command. After integrating code, run:

```powershell
./scripts/test-quick.ps1
```

This builds once, runs the .NET 10 product suite once, checks C# format, and exercises a small portable CLI fixture (paths with spaces, kind aliases, batch stdin, JSON, stderr, and exit codes). If the current build/tests already passed, reuse them:

```powershell
./scripts/test-quick.ps1 -NoBuild -SkipDotnetTest
./scripts/test-release.ps1 -NoBuild -SkipDotnetTest
```

Release checks cover version identity, packaging metadata, validated release selection, diagnostic capture, installer logic, publication recovery, and orchestration. They do not run every CLI fixture, install clients, repeat product tests, or run synthetic scoring campaigns. For a Release build, pass `-Configuration Release` consistently to both scripts. `pack-release.ps1` validates Release outputs before packing and builds/packages once.

Do not run a broad suite again because a late isolated check failed. Fix the cause and rerun its affected checks. Reuse passing results when their inputs did not change.

## Focused checks

```powershell
dotnet build navlyn.slnx
dotnet test navlyn.Tests/navlyn.Tests.csproj --no-build --framework net10.0 --filter 'FullyQualifiedName~RelevantTests'
./scripts/test-symbol-navigation.ps1 -NoBuild -Suite lookup
./scripts/test-symbol-navigation.ps1 -NoBuild -Suite definition
./scripts/test-symbol-navigation.ps1 -NoBuild -Suite references
./scripts/test-symbol-navigation.ps1 -NoBuild -Suite edges
./scripts/test-symbol-navigation.ps1 -NoBuild -Suite source
./scripts/test-fuzzy-discovery.ps1 -NoBuild
./scripts/test-multi-project-navigation.ps1 -NoBuild
./scripts/test-workspace-semantics.ps1 -NoBuild
./scripts/test-diagnostics.ps1 -NoBuild
./scripts/test-cli-contract.ps1 -NoBuild -Suite core
```

Choose the relevant script, rather than all scripts. CLI contract suites also include `navigation`, `workflow`, `domain`, `mcp-adjacent`, and `all`; the latter is an optional broad investigation, not a normal release gate. Resolver component tests avoid repeated workspace loading and should be the first choice for binding and ranking regressions. MCP tests use small actual workspaces for protocol checks. External-source tests share one prepared package fixture and persistent reader sessions, with separate consumers for mutation cases.

C# files use UTF-8 BOM, CRLF, and spaces. Run `normalize-csharp-files.ps1` if formatting drifts and verify with `test-csharp-file-format.ps1`. Envelope schemas and golden snapshots live in `docs/schemas` and `navlyn.Tests/Contracts/GoldenSnapshots`; change them only with intentional public contract changes.

## CI and publication

CI builds Release once on Windows, Linux, and macOS. Windows runs the full product suite on .NET 10 plus fast release checks; Linux/macOS run focused transport tests and portable CLI smoke. The v0.8 line still packages its existing .NET 8 assets until 0.9.0, without a separate .NET 8 test lane. The primary job budget is twelve minutes, with targets below ten minutes on Windows and five minutes on secondary platforms.

The Windows main run packs its tested outputs, checks package contents, runs installed-package smoke once on .NET 10, and retains publication inputs. Protected publication and tag verification reuse those bytes and successful source checks. Do not rebuild packages or rerun source tests during publication. Keep exact-main/artifact identity and recover from the reported state when publication is interrupted. See [distribution](navlyn-distribution.md) and [runtime support](navlyn-runtime-support.md).

All Actions are pinned to full commit SHAs; Dependabot can update them. CI retains logs, stage timings, and one TRX per tested platform under `navlyn-ci-diagnostics-<os>-<run>-<attempt>`. Inspect failed-stage output and the slowest tests before retrying or changing limits. Retained validated release inputs remain available for ninety days.

## Performance and real tasks

```powershell
./scripts/measure-navlyn-performance.ps1 -Workspace <project> -Scenario mcp -Iterations 3 -Warmup 1 -NoBuild -SourceFile <file> -SourceLine <line> -SourceColumn <column> -Output artifacts/performance/current.json
```

MCP measurements repeat calls within one server session. Reports separate cold/warm execution, record version, commit, dirty state, SDK, OS, processor count, iterations, and warmups. `-Baseline <report>` rejects incompatible workspace/scenario/profile/environment and compares per-command means. Keep functional checks distinct from repeated measurement. Live task comparisons should use a small fixed task set and report correctness, wrong-target edits, elapsed time, calls, and output size; no self-assigned score is a release requirement. See [performance](navlyn-performance.md) and [improvement review](navlyn-excellence-loop.md).

## Versions and environment failures

For an optional complete-task comparison, run `node scripts/measure-navlyn-adoption.mjs --client <codex-executable> --cli-dll <built-navlyn.dll> --server-dll <built-navlyn.Mcp.dll> --disable-server <unrelated-server-name> --routing-skill --attempts 2 --output artifacts/adoption/<fresh-directory>`. It needs Node, .NET 10 and an authenticated client; it is not a CI gate. Repeat `--disable-server` for configured unrelated servers. Default tasks cover a directly readable setting and a bound DLL body; `--tasks config,binary,interface` also covers the small caller fixture. No Navlyn invocation or skill read is required. Keep all attempts, including failures and no-call conditions; record model/client/commit, actual tool use, correctness, time, total/cached input and event timestamps. The MCP proxy enables opt-in server timing and records it separately from protocol stdout. Compare equivalent evidence/scope, separate cold/warm costs, and avoid tests/builds running alongside timed model trials.

`update-release-version.ps1 -Version <version>` updates current instructions and tool manifests while preserving historical/dates evidence and changelog history. Add current changelog entries and release notes separately.

The common process harness uses `ArgumentList`, concurrently drains both output streams, and applies a sixty-second per-process deadline by default. Build and product-test callers use explicit larger bounds. Timeouts kill/reap the owned process tree and report the command and available output. Do not increase limits to hide repeated setup or deadlocks.

Before retrying a timeout or DLL copy failure, inspect owned processes:

```powershell
Get-CimInstance Win32_Process -Filter "name = 'dotnet.exe'" | Select-Object ProcessId, CommandLine
```

Only stop clearly stale Navlyn processes from this checkout. If an idle build server holds output, run `dotnet build-server shutdown` once. Preserve user changes, avoid concurrent builds in the same checkout, and keep generated packages/logs/reports under ignored `artifacts/`. `git diff --check` detects whitespace errors; Git LF/CRLF conversion warnings alone are not failures.

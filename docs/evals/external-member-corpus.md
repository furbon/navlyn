# External member corpus

This corpus checks external-member reads against pinned, real NuGet packages. It complements the synthetic contract fixture and the public-repository adoption eval. It does not claim that every API in these packages can be decompiled.

## Run

On Windows with PowerShell 7, .NET SDK 10, and the .NET 8 and 10 targeting packs:

```powershell
dotnet restore navlyn.slnx
./scripts/measure-external-member-corpus.ps1
```

The first run copies the three pinned archives from the local NuGet cache. If an archive is absent, use `-Acquire` to download it from NuGet. Every archive is checked against `tests/corpora/ExternalMemberCorpus/packages.json` before restore. After the feed is populated, restore uses only that local feed and the runner's isolated package directory; it disables NuGet audit traffic. Use `-NoBuild` only when the Navlyn CLI binary is already current. The report is written to `artifacts/external-member-corpus/report.json` and is ignored by Git.

## Coverage

| Consumer | Language and target | Dependency path | What is checked |
| --- | --- | --- | --- |
| Simple8 | C# 12, net8.0 | Newtonsoft.Json package | Exact `SerializeObject(object, Formatting)` overload |
| Modern10 | C# 14, net10.0 | Microsoft.Extensions.Primitives package | `StringValues.IsNullOrEmpty` implementation |
| Multi | C# 12/14, net8.0/net10.0 | Central package versions; three project layers | Serilog generic overload and transitive `StringValues` constructor |
| Direct | C# 14, net10.0 | DLL extracted from the pinned Newtonsoft.Json package | Direct reference and different `SerializeObject(object)` overload |
| Scale | C# 12/14, net8.0/net10.0 | Microsoft.Extensions.Primitives package; 120 generated files | Read a member from the last generated file |

Each positive case checks the bound documentation ID, the target framework, the exact reference and implementation PE hashes, a body-specific text marker, and noneditable decompiled provenance. The runner also checks the default empty read, metadata-only declaration, a cursor on a type rather than a member, the deterministic unavailable result for a framework member, and a real-package MCP read against the CLI result. Each read has a process deadline and a 25-second evaluation budget. The runner verifies that tracked corpus sources did not change.

The package and scenario manifests are the reviewable baselines. When updating a package, replace its archive hash and recheck each expected asset, member ID, and body marker. Keep new versions in a separate baseline until their behavior is understood. This corpus includes one MCP parity check; the existing external-library source contract and protocol tests cover same-process stale-binary behavior more deeply.

Baseline Windows run (2026-09-27): 9/9 positive cases and 5/5 boundary checks passed. The isolated report recorded no findings. Results and timings apply to the pinned packages and the recorded SDK and Navlyn versions.

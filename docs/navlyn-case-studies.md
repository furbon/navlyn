# Navlyn Case Studies

These case studies are intentionally local and reproducible. They use this repository or committed fixtures so public evidence does not depend on an external clone, registry, or unpublished workspace.

Record the exact commit with `git rev-parse --short HEAD` when rerunning. The 2026-07-12 validation for this release-prep branch used base commit `d183494` plus the local release-preparation changes in this branch.

## Case Study 1: Pre-Edit Anchor In Navlyn Itself

Question: an agent is asked to change `CheckCommand`. Text search can find the name, but it does not prove which project/target-framework symbol should anchor the edit.

Workspace:

```text
navlyn.slnx
```

Commands:

```powershell
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- doctor --workspace navlyn.slnx
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- target --workspace navlyn.slnx --project "Navlyn.CommandLine(net10.0)" --query CheckCommand --assume-kind NamedType --limit 5
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- prepare-edit --workspace navlyn.slnx --project "Navlyn.CommandLine(net10.0)" --query CheckCommand --assume-kind NamedType --goal modify --change-kind behavior
```

Evidence to inspect:

- `doctor.ok` is `true` and the workspace project count matches the solution.
- `target.confidence` is `high`, `candidateCount` is `1`, and `candidateId` starts with `sym:v1:`.
- `selectedTarget.path` points at `Navlyn.CommandLine/Cli/Commands/CheckCommand.cs`.
- `prepare-edit` returns the anchor, bounded source/context/test evidence, known unknowns, limitations, and a post-edit guard command.

Observed 2026-07-12 performance smoke with `-Scenario quick -Iterations 1 -Warmup 0 -NoBuild`: 4/4 commands succeeded, median elapsed time was about 3.9 seconds, p95 about 8.0 seconds, maximum stdout was about 37.7K characters, and no output was truncated.

Limits:

- This is source-level evidence for an edit plan, not proof that a later implementation is correct.
- If `target` returns `ambiguitySummary`, the agent should ask or narrow before opening source.

## Case Study 2: Fixture-Backed Application Facts

Question: an agent reviews route, MediatR, EF, and package usage facts without claiming runtime proof.

Workspace:

```text
tests/fixtures/ApplicationDomainFixture/ApplicationDomainFixture.csproj
```

Commands:

```powershell
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- route-map --workspace tests/fixtures/ApplicationDomainFixture/ApplicationDomainFixture.csproj --profile compact
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- where-handled --workspace tests/fixtures/ApplicationDomainFixture/ApplicationDomainFixture.csproj --query CreateOrderCommand --assume-kind NamedType --profile compact
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- ef-model --workspace tests/fixtures/ApplicationDomainFixture/ApplicationDomainFixture.csproj --entity Order --profile compact
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- package-usage --workspace tests/fixtures/ApplicationDomainFixture/ApplicationDomainFixture.csproj --package Microsoft.EntityFrameworkCore --namespace Microsoft.EntityFrameworkCore --profile compact
```

Evidence to inspect:

- `route-map.highlights.routes` for normalized route patterns, handler symbols, and source-level auth attributes.
- `where-handled.highlights.handlers` for request/handler source locations.
- `ef-model.highlights.entities` / `dbSets` / query sites for bounded EF source facts.
- `package-usage.highlights.packageReferences` and usage counts for package evidence.

Limits:

- These commands do not produce runtime route tables, effective authorization proof, EF runtime models, or package compatibility decisions.
- Use them to decide what source to inspect next, not as a security or runtime validation report.

## External Local Validation: Partial / No-Go Evidence

The 2026-07-12 v0.7.0 run also checked three existing local clones outside this repository. These are not public reproducible fixtures, so they are recorded as adoption-risk evidence rather than release claims.

| Repository shape | Workspace | Result | Friction |
| --- | --- | --- | --- |
| Library with tests and benchmarks | `D:\Dev\furbon\SymbolNaming\SymbolNaming.slnx` | `doctor.ok` returned `true`; `target` resolved `RuleBasedSymbolTokenizer`; `prepare-edit` returned anchor, source/context, and 22 related test candidates. | Restore warned about unsupported `net6.0`; large preflight output. |
| Visual Studio extension | `D:\Dev\furbon\TagGroupJumper\TabGroupJumper.sln` | `target` resolved `Command1Package` with high confidence and attribute facts. | `doctor.ok` false due missing assets and null target framework. |
| Unity multi-project game | `D:\Dev\furbon\BeltHell\BeltHell.sln` | `target` resolved `ConveyorBelt2D` with high confidence and Unity attribute facts. | `doctor.ok` false due missing assets and unmatched project-reference metadata warning. |

Adoption conclusion: Navlyn has one clean external library adoption path and two useful partial/no-go cases. v0.7.0 should still avoid claiming frictionless adoption for Unity and legacy Visual Studio extension shapes until `doctor` gives more specific remediation.

## Failure-Mode Comparison

| Failure Mode | Text Search Risk | Navlyn Evidence | Agent Next Action |
| --- | --- | --- | --- |
| Same name in production/test | Opens the first text match. | `target` with `ambiguitySummary.groups` and project/target-framework facts. | Add `--project`, ask the user, or choose a returned `candidateId`. |
| Overload | Treats all methods with the same name as one target. | Source-position `target`, `signature`, `callers`, or `references` with one anchor. | Use file/line/column or exact `candidateId`. |
| Partial type | Reads only one declaration. | `read`, `about`, and `context-pack` can expose bounded source/context for the selected symbol. | Read the relevant partial declarations before editing. |
| Multi-target | Ignores target-framework-specific source. | `repo-graph`, `selector.targetFramework`, and project filters. | Specify the intended project/target framework. |
| DI registration | Mistakes direct references for construction behavior. | `where-registered`, `di-impact`, and constructor dependency facts. | Inspect registration and consumer evidence. |
| Route handler | Treats an endpoint as plain text. | `route-map`, `route-impact`, and handler/source auth facts. | Inspect handler and source-level policy evidence, then use runtime tests when needed. |

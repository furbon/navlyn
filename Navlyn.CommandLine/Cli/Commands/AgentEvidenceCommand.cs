using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Navlyn.ContextPacks;
using Navlyn.Diagnostics;
using Navlyn.Diffs;
using Navlyn.Symbols;
using Navlyn.Testing;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class AgentEvidenceCommand
{
    private const int DefaultCandidateLimit = 20;
    private const int DefaultReferenceLimit = 40;
    private const int DefaultTestLimit = 25;
    private const int DefaultSymbolLimit = 50;
    private const int DefaultContextBudgetTokens = 6000;
    private const int DefaultContextItemLimit = 12;

    public static Command CreateEditPreflight()
    {
        AgentTargetOptions target = CreateTargetOptions();
        Option<string> goalOption = CreateGoalOption("modify");
        Option<string?> changeKindOption = CreateChangeKindOption();
        Option<int?> budgetTokensOption = new("--budget-tokens") { Description = $"Context budget. Defaults to {DefaultContextBudgetTokens}." };
        Option<int?> itemLimitOption = new("--item-limit") { Description = $"Context item limit. Defaults to {DefaultContextItemLimit}." };
        Option<int?> referenceLimitOption = new("--reference-limit") { Description = $"Reference evidence limit. Defaults to {DefaultReferenceLimit}." };
        Option<int?> testLimitOption = new("--test-limit") { Description = $"Related test limit. Defaults to {DefaultTestLimit}." };

        return WorkspaceCommand.Create(
            "edit-preflight",
            "Create a semantic pre-edit evidence envelope for one intended C# or Visual Basic target.",
            [.. target.Options, goalOption, changeKindOption, budgetTokensOption, itemLimitOption, referenceLimitOption, testLimitOption],
            async (workspace, parseResult, cancellationToken) =>
            {
                AgentTargetInput input = target.Read(parseResult);
                string goal = parseResult.GetValue(goalOption)!;
                string? changeKind = parseResult.GetValue(changeKindOption);
                int budgetTokens = parseResult.GetValue(budgetTokensOption) ?? DefaultContextBudgetTokens;
                int itemLimit = parseResult.GetValue(itemLimitOption) ?? DefaultContextItemLimit;
                int referenceLimit = parseResult.GetValue(referenceLimitOption) ?? DefaultReferenceLimit;
                int testLimit = parseResult.GetValue(testLimitOption) ?? DefaultTestLimit;
                if (!ValidatePositive("--budget-tokens", budgetTokens, out int exitCode) ||
                    !ValidatePositive("--item-limit", itemLimit, out exitCode) ||
                    !ValidatePositive("--reference-limit", referenceLimit, out exitCode) ||
                    !ValidatePositive("--test-limit", testLimit, out exitCode))
                {
                    return exitCode;
                }

                AgentPreflightEnvelope envelope = await BuildPreflightAsync(
                    workspace,
                    input,
                    goal,
                    changeKind,
                    budgetTokens,
                    itemLimit,
                    referenceLimit,
                    testLimit,
                    cancellationToken);

                ConsoleJsonWriter.Write(envelope);
                return envelope.Anchor.SelectedTarget is null ? ExitCodes.UsageError : ExitCodes.Success;
            });
    }

    public static Command CreatePostEditGuard()
    {
        Option<string?> candidateIdOption = FuzzyCommandSupport.CreateCandidateIdOption();
        Option<FileInfo?> preflightOption = new("--preflight") { Description = "Path to a saved edit-preflight JSON file." };
        Option<string?> baseOption = DiffCommandSupport.CreateBaseOption();
        Option<string?> headOption = DiffCommandSupport.CreateHeadOption();
        Option<bool> stagedOption = DiffCommandSupport.CreateStagedOption();
        Option<bool> includeUnstagedOption = DiffCommandSupport.CreateIncludeUnstagedOption();
        Option<string[]> projectOption = SharedOptions.CreateProjectFiltersOption();
        Option<bool> excludeGeneratedOption = SharedOptions.CreateExcludeGeneratedOption();
        Option<int?> symbolLimitOption = DiffCommandSupport.CreateSymbolLimitOption(DefaultSymbolLimit);
        Option<string> failOnRiskOption = CreateRiskThresholdOption();

        return WorkspaceCommand.Create(
            "post-edit-guard",
            "Compare a pre-edit anchor with the current diff and report wrong-target risk.",
            [candidateIdOption, preflightOption, baseOption, headOption, stagedOption, includeUnstagedOption, projectOption, excludeGeneratedOption, symbolLimitOption, failOnRiskOption],
            async (workspace, parseResult, cancellationToken) =>
            {
                string? candidateId = parseResult.GetValue(candidateIdOption);
                FileInfo? preflight = parseResult.GetValue(preflightOption);
                if (string.IsNullOrWhiteSpace(candidateId) == (preflight is null))
                {
                    DiagnosticReporter.WriteError(DiagnosticIds.ParseError, "Specify exactly one anchor: --candidate-id or --preflight.");
                    return ExitCodes.UsageError;
                }

                AnchorSnapshot? anchor = preflight is not null
                    ? ReadAnchor(preflight)
                    : await ResolveCandidateAnchorAsync(workspace, candidateId!, cancellationToken);
                if (anchor is null)
                {
                    DiagnosticReporter.WriteError(DiagnosticIds.ParseError, "Could not read or resolve the requested guard anchor.");
                    return ExitCodes.UsageError;
                }

                GuardResult result = await BuildGuardAsync(
                    workspace,
                    "post-edit-guard",
                    anchor,
                    parseResult.GetValue(baseOption),
                    parseResult.GetValue(headOption),
                    parseResult.GetValue(stagedOption),
                    parseResult.GetValue(includeUnstagedOption),
                    parseResult.GetValue(projectOption) ?? [],
                    parseResult.GetValue(excludeGeneratedOption),
                    parseResult.GetValue(symbolLimitOption) ?? DefaultSymbolLimit,
                    parseResult.GetValue(failOnRiskOption)!,
                    cancellationToken);

                ConsoleJsonWriter.Write(result);
                return !result.Ok ? ExitCodes.UsageError : result.Policy.Passed ? ExitCodes.Success : ExitCodes.Failure;
            });
    }

    public static Command CreateWrongSymbolGuard()
    {
        AgentTargetOptions target = CreateTargetOptions();
        Option<string?> baseOption = DiffCommandSupport.CreateBaseOption();
        Option<string?> headOption = DiffCommandSupport.CreateHeadOption();
        Option<bool> stagedOption = DiffCommandSupport.CreateStagedOption();
        Option<bool> includeUnstagedOption = DiffCommandSupport.CreateIncludeUnstagedOption();
        Option<int?> symbolLimitOption = DiffCommandSupport.CreateSymbolLimitOption(DefaultSymbolLimit);
        Option<string> failOnRiskOption = CreateRiskThresholdOption();

        return WorkspaceCommand.Create(
            "wrong-symbol-guard",
            "Compare intended C# or Visual Basic target intent with changed symbols and report wrong-symbol risk.",
            [.. target.Options, baseOption, headOption, stagedOption, includeUnstagedOption, symbolLimitOption, failOnRiskOption],
            async (workspace, parseResult, cancellationToken) =>
            {
                TargetResolution resolution = await ResolveTargetAsync(workspace, target.Read(parseResult), cancellationToken);
                if (resolution.Result.SelectedTarget is null)
                {
                    ConsoleJsonWriter.Write(new
                    {
                        schemaVersion = "navlyn.agent-guard.v1",
                        command = "wrong-symbol-guard",
                        ok = false,
                        anchor = AnchorSnapshot.FromResolveTarget(resolution.Result),
                        risk = "high",
                        reasonCodes = new[] { "intended-target-not-resolved" },
                        warnings = resolution.Result.Warnings
                    });
                    return ExitCodes.UsageError;
                }

                GuardResult result = await BuildGuardAsync(
                    workspace,
                    "wrong-symbol-guard",
                    AnchorSnapshot.FromResolveTarget(resolution.Result),
                    parseResult.GetValue(baseOption),
                    parseResult.GetValue(headOption),
                    parseResult.GetValue(stagedOption),
                    parseResult.GetValue(includeUnstagedOption),
                    resolution.ProjectFilters,
                    resolution.Input.ExcludeGenerated,
                    parseResult.GetValue(symbolLimitOption) ?? DefaultSymbolLimit,
                    parseResult.GetValue(failOnRiskOption)!,
                    cancellationToken);

                ConsoleJsonWriter.Write(result);
                return !result.Ok ? ExitCodes.UsageError : result.Policy.Passed ? ExitCodes.Success : ExitCodes.Failure;
            });
    }

    public static Command CreateChangeIntentPack()
    {
        return CreatePreflightProjectionCommand(
            "change-intent-pack",
            "Create a compact machine-readable record of intended target, goal, known unknowns, and post-edit guard command.",
            projection: "intent");
    }

    public static Command CreateAgentHandoffPack()
    {
        return CreatePreflightProjectionCommand(
            "agent-handoff-pack",
            "Create a handoff pack with target anchors, reading queue, trusted evidence, open questions, and next checks.",
            projection: "handoff");
    }

    public static Command CreateConfidenceLedger()
    {
        return CreatePreflightProjectionCommand(
            "confidence-ledger",
            "Create a ledger explaining which evidence raised or lowered confidence for the intended target.",
            projection: "confidence");
    }

    private static Command CreatePreflightProjectionCommand(string name, string description, string projection)
    {
        AgentTargetOptions target = CreateTargetOptions();
        Option<string> goalOption = CreateGoalOption("modify");
        Option<string?> changeKindOption = CreateChangeKindOption();

        return WorkspaceCommand.Create(
            name,
            description,
            [.. target.Options, goalOption, changeKindOption],
            async (workspace, parseResult, cancellationToken) =>
            {
                AgentPreflightEnvelope preflight = await BuildPreflightAsync(
                    workspace,
                    target.Read(parseResult),
                    parseResult.GetValue(goalOption)!,
                    parseResult.GetValue(changeKindOption),
                    DefaultContextBudgetTokens,
                    DefaultContextItemLimit,
                    DefaultReferenceLimit,
                    DefaultTestLimit,
                    cancellationToken);

                object output = projection switch
                {
                    "intent" => new
                    {
                        schemaVersion = "navlyn.agent-intent.v1",
                        command = name,
                        preflight.Workspace,
                        preflight.Kind,
                        preflight.Intent,
                        preflight.Anchor,
                        preflight.Confidence,
                        preflight.KnownUnknowns,
                        recommendedPostEditGuard = preflight.NextCommands.First(command => command.Command == "post-edit-guard")
                    },
                    "handoff" => new
                    {
                        schemaVersion = "navlyn.agent-handoff.v1",
                        command = name,
                        preflight.Workspace,
                        preflight.Kind,
                        preflight.Intent,
                        anchors = new[] { preflight.Anchor },
                        readingQueue = preflight.Context?.Pack.Items ?? [],
                        commandsRun = preflight.CommandsRun,
                        evidenceTrusted = preflight.Confidence.Evidence,
                        unverifiedRisks = preflight.KnownUnknowns,
                        nextMinimalChecks = preflight.NextCommands,
                        stopConditions = preflight.StopConditions
                    },
                    _ => new
                    {
                        schemaVersion = "navlyn.confidence-ledger.v1",
                        command = name,
                        preflight.Workspace,
                        preflight.Kind,
                        preflight.Anchor,
                        preflight.Confidence,
                        evidence = preflight.Confidence.Evidence,
                        limitations = preflight.Limitations
                    }
                };

                ConsoleJsonWriter.Write(output);
                return preflight.Anchor.SelectedTarget is null ? ExitCodes.UsageError : ExitCodes.Success;
            });
    }

    private static async Task<AgentPreflightEnvelope> BuildPreflightAsync(
        LoadedWorkspace workspace,
        AgentTargetInput input,
        string goal,
        string? changeKind,
        int budgetTokens,
        int itemLimit,
        int referenceLimit,
        int testLimit,
        CancellationToken cancellationToken)
    {
        TargetResolution resolution = await ResolveTargetAsync(workspace, input, cancellationToken);
        ResolveTargetResult target = resolution.Result;
        AnchorSnapshot anchor = AnchorSnapshot.FromResolveTarget(target);
        SymbolSourceEvidence? source = await ResolveSourceEvidenceAsync(workspace, target, input.ExcludeGenerated, cancellationToken);
        ContextPackResult? context = await ResolveContextAsync(workspace, resolution, goal, changeKind, budgetTokens, itemLimit, referenceLimit, cancellationToken);
        TestEvidenceEnvelope? tests = await ResolveTestsAsync(workspace, target, resolution.Projects, testLimit, input.ExcludeGenerated, cancellationToken);

        AgentConfidenceLedger confidence = CreateConfidence(target, source, context, tests);
        IReadOnlyList<string> knownUnknowns = CreateKnownUnknowns(target, context, tests);
        return new AgentPreflightEnvelope(
            SchemaVersion: "navlyn.edit-preflight.v1",
            Workspace: workspace.DisplayPath,
            Kind: workspace.Kind,
            Command: "edit-preflight",
            Intent: new AgentIntent(goal, changeKind),
            Anchor: anchor,
            Confidence: confidence,
            Source: source,
            Context: context,
            Tests: tests,
            Risk: CreateRiskSummary(target, context, tests),
            KnownUnknowns: knownUnknowns,
            Limitations:
            [
                "Navlyn reports static source evidence only; it does not prove runtime behavior.",
                "Reflection, generated runtime routes, custom DI containers, dynamic dispatch, and external packages can be incomplete.",
                "Run the recommended tests and post-edit guard after editing."
            ],
            NextCommands: CreateNextCommands(anchor, goal, changeKind),
            StopConditions:
            [
                "Stop before editing if selectedTarget is null or confidence is none.",
                "Stop and ask for disambiguation if ambiguityReason is present.",
                "After editing, run post-edit-guard against this preflight anchor before widening scope."
            ],
            CommandsRun: CreateCommandsRun(input, goal, changeKind, budgetTokens, itemLimit, referenceLimit, testLimit));
    }

    private static async Task<TargetResolution> ResolveTargetAsync(LoadedWorkspace workspace, AgentTargetInput input, CancellationToken cancellationToken)
    {
        bool hasSourcePosition = input.File is not null || input.Line is not null || input.Column is not null;
        int targetModeCount = (string.IsNullOrWhiteSpace(input.Query) ? 0 : 1) +
            (string.IsNullOrWhiteSpace(input.CandidateId) ? 0 : 1) +
            (hasSourcePosition ? 1 : 0);
        if (targetModeCount != 1)
        {
            DiagnosticReporter.WriteError(DiagnosticIds.ParseError, "Specify exactly one target mode: --query, --candidate-id, or --file with --line and --column.");
            return TargetResolution.Failed(input);
        }

        if (hasSourcePosition)
        {
            if (input.File is null || input.Line is null || input.Column is null)
            {
                DiagnosticReporter.WriteError(DiagnosticIds.ParseError, "Source-position mode requires --file, --line, and --column.");
                return TargetResolution.Failed(input);
            }

            if (input.ProjectFilters.Count > 1)
            {
                DiagnosticReporter.WriteError(DiagnosticIds.ParseError, "Source-position mode accepts at most one --project filter.");
                return TargetResolution.Failed(input);
            }

            string? projectFilter = input.ProjectFilters.Count == 0 ? null : input.ProjectFilters[0];
            if (!ProjectFilterCommand.TryResolveSingleProject(workspace, projectFilter, out Project? project, out _, out _))
            {
                return TargetResolution.Failed(input);
            }

            ResolveTargetResult sourceResult = await new ResolveTargetResolver().ResolveSourcePositionAsync(
                workspace,
                input.File,
                input.Line.Value,
                input.Column.Value,
                project,
                input.ExcludeGenerated,
                cancellationToken);
            return new TargetResolution(input, sourceResult, [.. workspace.Solution.Projects], null, input.ProjectFilters);
        }

        if (!FuzzyCommandSupport.TryCreateSelection(
            workspace,
            input.Query,
            input.CandidateId,
            input.AssumeKinds,
            input.Match,
            input.CaseSensitive,
            input.ProjectFilters,
            input.ExcludeGenerated,
            input.CandidateLimit,
            input.CandidatePolicy,
            input.MinConfidence,
            input.ExplainSelection,
            allowGroupPolicy: false,
            out FuzzyQueryOptions options,
            out IReadOnlyList<Project> projects,
            out IReadOnlyList<FuzzyProjectFilter>? projectOutputs,
            out _))
        {
            return TargetResolution.Failed(input);
        }

        FuzzyDiscoveryResolver resolver = new();
        if (!await FuzzyCommandSupport.TryValidateSelectionAsync(resolver, projects, options, cancellationToken))
        {
            return TargetResolution.Failed(input);
        }

        ResolveTargetResult result = await new ResolveTargetResolver().ResolveFuzzyAsync(
            workspace,
            options,
            projects,
            projectOutputs,
            cancellationToken);
        return new TargetResolution(input, result, projects, projectOutputs, input.ProjectFilters, options);
    }

    private static async Task<SymbolSourceEvidence?> ResolveSourceEvidenceAsync(
        LoadedWorkspace workspace,
        ResolveTargetResult target,
        bool excludeGenerated,
        CancellationToken cancellationToken)
    {
        ResolveTargetSymbol? selected = target.SelectedTarget;
        if (selected?.Path is null || selected.Line is null || selected.Column is null)
        {
            return null;
        }

        SymbolSourceResolutionResult result = await new SymbolSourceResolver().ResolveAsync(
            workspace.Solution,
            new FileInfo(selected.Path),
            selected.Line.Value,
            selected.Column.Value,
            project: null,
            excludeGenerated,
            new SymbolSourceOptions("declaration", MaxLines: 80, BudgetTokens: 4000),
            cancellationToken);

        if (result.Error is not null)
        {
            return new SymbolSourceEvidence("error", result.Error.Message, Resolution: null);
        }

        return new SymbolSourceEvidence("ok", Error: null, result.Resolution);
    }

    private static async Task<ContextPackResult?> ResolveContextAsync(
        LoadedWorkspace workspace,
        TargetResolution resolution,
        string goal,
        string? changeKind,
        int budgetTokens,
        int itemLimit,
        int referenceLimit,
        CancellationToken cancellationToken)
    {
        FuzzyQueryOptions? options = resolution.QueryOptions;
        if (options is null)
        {
            ResolveTargetSymbol? target = resolution.Result.SelectedTarget;
            if (target?.Facts.FullyQualifiedName is null)
            {
                return null;
            }

            options = new FuzzyQueryOptions(
                target.Facts.FullyQualifiedName,
                [target.Kind],
                "exact",
                CaseSensitive: true,
                ExcludeGenerated: resolution.Input.ExcludeGenerated,
                Limit: DefaultCandidateLimit)
            {
                Selection = new FuzzySelectionOptions("select", "medium", ExplainSelection: false)
            };
        }

        ContextPackOptions contextOptions = new(
            Goal: goal,
            BudgetTokens: budgetTokens,
            ItemLimit: itemLimit,
            SnippetPolicy: "line",
            SnippetLines: 1,
            CandidateLimit: DefaultCandidateLimit,
            MemberLimit: 20,
            ReferenceLimit: referenceLimit,
            RelationLimit: 20,
            FileLimit: 20,
            QueryDiagnosticLimit: 20,
            SymbolLimit: DefaultSymbolLimit,
            ImpactLimit: referenceLimit,
            DiffDiagnosticLimit: 20,
            RelatedTestLimit: DefaultTestLimit,
            Depth: 2,
            ChangeKind: changeKind);

        return await new ContextPackResolver().ResolveQueryAsync(
            workspace,
            options,
            resolution.Projects,
            resolution.ProjectOutputs,
            resolution.Input.ExcludeGenerated,
            contextOptions,
            cancellationToken);
    }

    private static async Task<TestEvidenceEnvelope?> ResolveTestsAsync(
        LoadedWorkspace workspace,
        ResolveTargetResult target,
        IReadOnlyList<Project> projects,
        int testLimit,
        bool excludeGenerated,
        CancellationToken cancellationToken)
    {
        ResolveTargetSymbol? selected = target.SelectedTarget;
        if (selected is null)
        {
            return null;
        }

        TestSubject subject = new(
            selected.Name,
            selected.Kind,
            selected.Container,
            selected.Facts,
            selected.Path,
            selected.Line,
            selected.Column,
            selected.EndLine,
            selected.EndColumn);
        TestImpactResolution impact = await new TestImpactResolver().ResolveForSymbolAsync(
            workspace,
            projects,
            explicitTestProjects: null,
            subject,
            new TestImpactOptions(testLimit, ReferenceLimit: Math.Max(testLimit, DefaultReferenceLimit), IncludeSnippets: false, SnippetLines: 0, excludeGenerated),
            cancellationToken);
        return new TestEvidenceEnvelope(impact.TestProjects, impact.Tests, impact.Warnings);
    }

    private static async Task<GuardResult> BuildGuardAsync(
        LoadedWorkspace workspace,
        string command,
        AnchorSnapshot anchor,
        string? baseRef,
        string? headRef,
        bool staged,
        bool includeUnstaged,
        IReadOnlyList<string> projectFilters,
        bool excludeGenerated,
        int symbolLimit,
        string failOnRisk,
        CancellationToken cancellationToken)
    {
        if (!ValidatePositive("--symbol-limit", symbolLimit, out _) ||
            !DiffCommandSupport.TryCreateRequest(baseRef, headRef, staged, includeUnstaged, out DiffRequest request, out _) ||
            !DiffCommandSupport.TryCreateProjectContext(workspace, projectFilters, out IReadOnlyList<Project> projects, out IReadOnlyList<DiffProjectFilter>? projectOutputs, out _))
        {
            return GuardResult.Invalid(command, workspace, anchor, failOnRisk);
        }

        DiffWorkflowExecutionResult<ChangedSymbolsResult> changed = await new DiffWorkflowResolver().ResolveChangedSymbolsAsync(
            workspace,
            request,
            projects,
            projectOutputs,
            excludeGenerated,
            symbolLimit,
            cancellationToken);
        if (changed.Error is not null)
        {
            return GuardResult.Invalid(command, workspace, anchor, failOnRisk, changed.Error.Message);
        }

        IReadOnlyList<SymbolMatchScore> scores = [.. changed.Result!.ChangedSymbols.Symbols
            .Select(symbol => Score(anchor, symbol))
            .OrderByDescending(score => score.Score)
            .ThenBy(score => score.Symbol.Path, StringComparer.Ordinal)
            .ThenBy(score => score.Symbol.Line)];
        double bestScore = scores.FirstOrDefault()?.Score ?? 0;
        string risk = bestScore >= 0.75 ? "low" : bestScore >= 0.45 ? "medium" : "high";
        IReadOnlyList<string> reasons = CreateGuardReasonCodes(anchor, changed.Result.ChangedSymbols.Symbols, scores.FirstOrDefault(), risk);
        bool passed = RiskRank(risk) < RiskRank(failOnRisk);
        return new GuardResult(
            SchemaVersion: "navlyn.agent-guard.v1",
            Workspace: workspace.DisplayPath,
            Kind: workspace.Kind,
            Command: command,
            Ok: true,
            Anchor: anchor,
            Diff: changed.Result.Diff,
            ChangedSymbols: changed.Result.ChangedSymbols,
            ProjectFilters: projectOutputs,
            Scores: scores,
            BestScore: bestScore,
            Risk: risk,
            ReasonCodes: reasons,
            Policy: new GuardPolicy(failOnRisk, passed),
            Warnings: changed.Result.Warnings,
            RecommendedAction: risk switch
            {
                "low" => "Continue, then run focused tests related to the changed symbol.",
                "medium" => "Inspect score reasons before proceeding; rerun preflight if the edit intentionally broadened.",
                _ => "Stop and re-anchor the intended target before continuing."
            },
            ProofBoundary: "Static source diff comparison only; generated/runtime/reflection behavior is outside this guard.");
    }

    private static SymbolMatchScore Score(AnchorSnapshot anchor, DiffChangedSymbol symbol)
    {
        List<string> matches = [];
        List<string> mismatches = [];
        double score = 0;
        AddScore(anchor.Name, symbol.Name, 0.35, "name");
        AddScore(anchor.Kind, symbol.Kind, 0.20, "kind");
        AddScore(anchor.Container, symbol.Container, 0.20, "container");
        AddScore(anchor.Path, symbol.Path, 0.15, "path");
        AddScore(anchor.Project, symbol.Facts.Project, 0.10, "project");
        return new SymbolMatchScore(Math.Round(score, 3), symbol, matches, mismatches);

        void AddScore(string? expected, string? actual, double weight, string label)
        {
            if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(actual))
            {
                mismatches.Add($"{label}:unknown");
                return;
            }

            bool same = label == "path"
                ? string.Equals(NormalizePath(expected), NormalizePath(actual), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                : string.Equals(expected, actual, StringComparison.Ordinal);
            if (same)
            {
                score += weight;
                matches.Add(label);
            }
            else
            {
                mismatches.Add(label);
            }
        }
    }

    private static IReadOnlyList<string> CreateGuardReasonCodes(AnchorSnapshot anchor, IReadOnlyList<DiffChangedSymbol> changed, SymbolMatchScore? best, string risk)
    {
        List<string> reasons = [];
        if (changed.Count == 0)
        {
            reasons.Add("no-changed-symbols-found");
        }

        if (best is null)
        {
            reasons.Add("no-anchor-match");
        }
        else
        {
            reasons.AddRange(best.Matches.Select(match => $"matched-{match}"));
            reasons.AddRange(best.Mismatches.Select(mismatch => $"mismatch-{mismatch}"));
        }

        if (risk != "low")
        {
            reasons.Add(anchor.CandidateId is null ? "anchor-has-no-candidate-id" : "anchor-risk-requires-review");
        }

        return [.. reasons.Distinct(StringComparer.Ordinal).OrderBy(reason => reason, StringComparer.Ordinal)];
    }

    private static AgentConfidenceLedger CreateConfidence(ResolveTargetResult target, SymbolSourceEvidence? source, ContextPackResult? context, TestEvidenceEnvelope? tests)
    {
        List<ConfidenceEvidence> evidence = [];
        if (target.SelectedTarget is not null)
        {
            evidence.Add(new ConfidenceEvidence("target-selected", "raises", target.Confidence, ["selected-target-present"]));
        }
        else
        {
            evidence.Add(new ConfidenceEvidence("target-not-selected", "lowers", "high", [target.AmbiguityReason ?? "no-selected-target"]));
        }

        evidence.Add(new ConfidenceEvidence(source?.Status == "ok" ? "source-slice" : "source-slice-missing", source?.Status == "ok" ? "raises" : "lowers", source?.Status == "ok" ? "medium" : "high", source?.Status == "ok" ? ["bounded-source-returned"] : ["source-not-available"]));
        evidence.Add(new ConfidenceEvidence(context is not null && context.Pack.Items.Count > 0 ? "context-pack" : "context-pack-empty", context is not null && context.Pack.Items.Count > 0 ? "raises" : "neutral", "medium", context is null ? ["context-not-run"] : [$"context-items:{context.Pack.Items.Count}"]));
        evidence.Add(new ConfidenceEvidence(tests is not null && tests.Tests.TotalCandidates > 0 ? "related-tests" : "related-tests-empty", tests is not null && tests.Tests.TotalCandidates > 0 ? "raises" : "neutral", "medium", tests is null ? ["tests-not-run"] : [$"test-candidates:{tests.Tests.TotalCandidates}"]));

        string overall = target.SelectedTarget is null ? "none" : target.Confidence;
        return new AgentConfidenceLedger(overall, evidence);
    }

    private static AgentRiskSummary CreateRiskSummary(ResolveTargetResult target, ContextPackResult? context, TestEvidenceEnvelope? tests)
    {
        List<string> reasons = [];
        if (target.SelectedTarget is null)
        {
            reasons.Add("no-selected-target");
        }

        if (target.AmbiguityReason is not null)
        {
            reasons.Add(target.AmbiguityReason);
        }

        if (context?.Truncated == true)
        {
            reasons.Add("context-truncated");
        }

        if (tests is null || tests.Tests.TotalCandidates == 0)
        {
            reasons.Add("no-related-tests-found");
        }

        string level = reasons.Contains("no-selected-target", StringComparer.Ordinal) ? "high" :
            reasons.Contains("context-truncated", StringComparer.Ordinal) ? "medium" :
            "low";
        return new AgentRiskSummary(level, [.. reasons.Distinct(StringComparer.Ordinal).OrderBy(reason => reason, StringComparer.Ordinal)]);
    }

    private static IReadOnlyList<string> CreateKnownUnknowns(ResolveTargetResult target, ContextPackResult? context, TestEvidenceEnvelope? tests)
    {
        List<string> values = [];
        if (target.CandidateCount > 1)
        {
            values.Add("multiple-candidates-existed-before-selection");
        }

        if (context?.Truncated == true)
        {
            values.Add("context-pack-truncated-by-budget");
        }

        if (tests is null || tests.Tests.TotalCandidates == 0)
        {
            values.Add("related-tests-not-proven");
        }

        values.Add("runtime-behavior-not-proven");
        return [.. values.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal)];
    }

    private static IReadOnlyList<AgentCommandRun> CreateCommandsRun(AgentTargetInput input, string goal, string? changeKind, int budgetTokens, int itemLimit, int referenceLimit, int testLimit)
    {
        string targetArg = input.CandidateId is not null ? "--candidate-id" : input.File is not null ? "--file" : "--query";
        string targetValue = input.CandidateId ?? input.File?.ToString() ?? input.Query ?? "";
        return
        [
            new AgentCommandRun("resolve-target", [targetArg, targetValue]),
            new AgentCommandRun("symbol-source", ["--view", "declaration"]),
            new AgentCommandRun("context-pack", ["--goal", goal, "--budget-tokens", budgetTokens.ToString(System.Globalization.CultureInfo.InvariantCulture), "--item-limit", itemLimit.ToString(System.Globalization.CultureInfo.InvariantCulture), "--reference-limit", referenceLimit.ToString(System.Globalization.CultureInfo.InvariantCulture), "--change-kind", changeKind ?? ""]),
            new AgentCommandRun("tests-for-symbol", ["--test-limit", testLimit.ToString(System.Globalization.CultureInfo.InvariantCulture)])
        ];
    }

    private static IReadOnlyList<AgentNextCommand> CreateNextCommands(AnchorSnapshot anchor, string goal, string? changeKind)
    {
        return
        [
            new AgentNextCommand("post-edit-guard", anchor.CandidateId is null ? ["--preflight", "<saved-edit-preflight.json>"] : ["--candidate-id", anchor.CandidateId], "Run after editing to confirm the dirty diff still matches the anchor."),
            new AgentNextCommand("tests-for-symbol", anchor.CandidateId is null ? ["--query", anchor.Name ?? ""] : ["--candidate-id", anchor.CandidateId], "Inspect related tests before or after the edit."),
            new AgentNextCommand("change-intent-pack", ["--goal", goal, "--change-kind", changeKind ?? ""], "Persist compact intent when handing work to another agent.")
        ];
    }

    private static AnchorSnapshot? ReadAnchor(FileInfo file)
    {
        if (!file.Exists)
        {
            return null;
        }

        try
        {
            using FileStream stream = file.OpenRead();
            using JsonDocument document = JsonDocument.Parse(stream);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("anchor", out JsonElement anchor))
            {
                return null;
            }

            return new AnchorSnapshot(
                CandidateId: GetString(anchor, "candidateId"),
                Name: GetString(anchor, "name"),
                Kind: GetString(anchor, "kind"),
                Container: GetString(anchor, "container"),
                Project: GetString(anchor, "project"),
                Path: GetString(anchor, "path"),
                Line: GetInt(anchor, "line"),
                Column: GetInt(anchor, "column"),
                EndLine: GetInt(anchor, "endLine"),
                EndColumn: GetInt(anchor, "endColumn"),
                SelectedTarget: null,
                Confidence: GetString(anchor, "confidence") ?? "unknown",
                AmbiguityReason: GetString(anchor, "ambiguityReason"));
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<AnchorSnapshot?> ResolveCandidateAnchorAsync(LoadedWorkspace workspace, string candidateId, CancellationToken cancellationToken)
    {
        AgentTargetInput input = AgentTargetInput.Candidate(candidateId);
        TargetResolution result = await ResolveTargetAsync(workspace, input, cancellationToken);
        return result.Result.SelectedTarget is null ? null : AnchorSnapshot.FromResolveTarget(result.Result);
    }

    private static string? GetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static int? GetInt(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int result)
            ? result
            : null;
    }

    private static bool ValidatePositive(string option, int value, out int exitCode)
    {
        return FuzzyCommandSupport.TryCreatePositiveOption(option, value, out exitCode);
    }

    private static int RiskRank(string risk)
    {
        return risk switch
        {
            "low" => 1,
            "medium" => 2,
            "high" => 3,
            _ => 3
        };
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }

    private static Option<string> CreateGoalOption(string defaultValue)
    {
        Option<string> option = new("--goal")
        {
            Description = "Agent goal: review, modify, or understand.",
            DefaultValueFactory = _ => defaultValue
        };
        option.AcceptOnlyFromAmong("review", "modify", "understand");
        return option;
    }

    private static Option<string?> CreateChangeKindOption()
    {
        Option<string?> option = new("--change-kind") { Description = "Change kind hint: behavior, signature, rename, constructor, nullability, async, public-api, di-registration, or endpoint." };
        option.AcceptOnlyFromAmong("behavior", "signature", "rename", "constructor", "nullability", "async", "public-api", "di-registration", "endpoint");
        return option;
    }

    private static Option<string> CreateRiskThresholdOption()
    {
        Option<string> option = new("--fail-on-risk")
        {
            Description = "Return a non-zero exit code when risk is at or above this level: low, medium, or high.",
            DefaultValueFactory = _ => "high"
        };
        option.AcceptOnlyFromAmong("low", "medium", "high");
        return option;
    }

    private static AgentTargetOptions CreateTargetOptions()
    {
        return new AgentTargetOptions(
            new Option<string?>("--query") { Description = "Approximate symbol query." },
            FuzzyCommandSupport.CreateCandidateIdOption(),
            new Option<FileInfo?>("--file") { Description = "Path to a C# or Visual Basic source file in the workspace." },
            new Option<int?>("--line") { Description = "1-based source line." },
            new Option<int?>("--column") { Description = "1-based source column." },
            FuzzyCommandSupport.CreateAssumeKindOption(),
            FuzzyCommandSupport.CreateMatchOption(),
            SharedOptions.CreateCaseSensitiveOption(),
            SharedOptions.CreateProjectFiltersOption(),
            SharedOptions.CreateExcludeGeneratedOption(),
            new Option<int?>("--candidate-limit") { Description = $"Maximum fuzzy candidates. Defaults to {DefaultCandidateLimit}." },
            FuzzyCommandSupport.CreateCandidatePolicyOption("select"),
            FuzzyCommandSupport.CreateMinConfidenceOption("medium"),
            FuzzyCommandSupport.CreateExplainSelectionOption());
    }

    private sealed record AgentTargetOptions(
        Option<string?> Query,
        Option<string?> CandidateId,
        Option<FileInfo?> File,
        Option<int?> Line,
        Option<int?> Column,
        Option<string[]> AssumeKind,
        Option<string> Match,
        Option<bool> CaseSensitive,
        Option<string[]> Project,
        Option<bool> ExcludeGenerated,
        Option<int?> CandidateLimit,
        Option<string> CandidatePolicy,
        Option<string> MinConfidence,
        Option<bool> ExplainSelection)
    {
        public IReadOnlyList<Option> Options =>
        [
            Query,
            CandidateId,
            File,
            Line,
            Column,
            AssumeKind,
            Match,
            CaseSensitive,
            Project,
            ExcludeGenerated,
            CandidateLimit,
            CandidatePolicy,
            MinConfidence,
            ExplainSelection
        ];

        public AgentTargetInput Read(ParseResult parseResult)
        {
            return new AgentTargetInput(
                parseResult.GetValue(Query),
                parseResult.GetValue(CandidateId),
                parseResult.GetValue(File),
                parseResult.GetValue(Line),
                parseResult.GetValue(Column),
                parseResult.GetValue(AssumeKind) ?? [],
                parseResult.GetValue(Match)!,
                parseResult.GetValue(CaseSensitive),
                parseResult.GetValue(Project) ?? [],
                parseResult.GetValue(ExcludeGenerated),
                parseResult.GetValue(CandidateLimit) ?? DefaultCandidateLimit,
                parseResult.GetValue(CandidatePolicy)!,
                parseResult.GetValue(MinConfidence)!,
                parseResult.GetValue(ExplainSelection));
        }
    }

    private sealed record AgentTargetInput(
        string? Query,
        string? CandidateId,
        FileInfo? File,
        int? Line,
        int? Column,
        IReadOnlyList<string> AssumeKinds,
        string Match,
        bool CaseSensitive,
        IReadOnlyList<string> ProjectFilters,
        bool ExcludeGenerated,
        int CandidateLimit,
        string CandidatePolicy,
        string MinConfidence,
        bool ExplainSelection)
    {
        public static AgentTargetInput Candidate(string candidateId)
        {
            return new AgentTargetInput(Query: null, candidateId, File: null, Line: null, Column: null, AssumeKinds: [], Match: "smart", CaseSensitive: false, ProjectFilters: [], ExcludeGenerated: false, DefaultCandidateLimit, CandidatePolicy: "select", MinConfidence: "medium", ExplainSelection: false);
        }
    }

    private sealed record TargetResolution(
        AgentTargetInput Input,
        ResolveTargetResult Result,
        IReadOnlyList<Project> Projects,
        IReadOnlyList<FuzzyProjectFilter>? ProjectOutputs,
        IReadOnlyList<string> ProjectFilters,
        FuzzyQueryOptions? QueryOptions = null)
    {
        public static TargetResolution Failed(AgentTargetInput input)
        {
            return new TargetResolution(
                input,
                new ResolveTargetResult("", "", "resolve-target", new ResolveTargetInput("invalid", input.Query, input.CandidateId, input.File?.ToString(), input.Line, input.Column), SelectedTarget: null, CandidateId: null, Selector: null, Confidence: "none", AmbiguityReason: "invalid-input", CandidateCount: 0, TotalCandidates: 0, Candidates: null, RecommendedNextActions: [], Warnings: ["invalid-target-input"]),
                Projects: [],
                ProjectOutputs: null,
                input.ProjectFilters);
        }
    }

    private sealed record AgentPreflightEnvelope(
        string SchemaVersion,
        string Workspace,
        string Kind,
        string Command,
        AgentIntent Intent,
        AnchorSnapshot Anchor,
        AgentConfidenceLedger Confidence,
        SymbolSourceEvidence? Source,
        ContextPackResult? Context,
        TestEvidenceEnvelope? Tests,
        AgentRiskSummary Risk,
        IReadOnlyList<string> KnownUnknowns,
        IReadOnlyList<string> Limitations,
        IReadOnlyList<AgentNextCommand> NextCommands,
        IReadOnlyList<string> StopConditions,
        IReadOnlyList<AgentCommandRun> CommandsRun);

    private sealed record AgentIntent(string Goal, string? ChangeKind);

    private sealed record AnchorSnapshot(
        string? CandidateId,
        string? Name,
        string? Kind,
        string? Container,
        string? Project,
        string? Path,
        int? Line,
        int? Column,
        int? EndLine,
        int? EndColumn,
        ResolveTargetSymbol? SelectedTarget,
        string Confidence,
        string? AmbiguityReason)
    {
        public static AnchorSnapshot FromResolveTarget(ResolveTargetResult result)
        {
            ResolveTargetSymbol? target = result.SelectedTarget;
            return new AnchorSnapshot(
                result.CandidateId,
                target?.Name,
                target?.Kind,
                target?.Container,
                target?.Facts.Project,
                target?.Path,
                target?.Line,
                target?.Column,
                target?.EndLine,
                target?.EndColumn,
                target,
                result.Confidence,
                result.AmbiguityReason);
        }
    }

    private sealed record SymbolSourceEvidence(string Status, string? Error, SymbolSourceResolution? Resolution);

    private sealed record TestEvidenceEnvelope(IReadOnlyList<TestProjectInfo> TestProjects, TestCandidatesSection Tests, IReadOnlyList<string> Warnings);

    private sealed record AgentConfidenceLedger(string Overall, IReadOnlyList<ConfidenceEvidence> Evidence);

    private sealed record ConfidenceEvidence(string Kind, string Effect, string Confidence, IReadOnlyList<string> ReasonCodes);

    private sealed record AgentRiskSummary(string Level, IReadOnlyList<string> ReasonCodes);

    private sealed record AgentNextCommand(string Command, IReadOnlyList<string> Arguments, string Reason);

    private sealed record AgentCommandRun(string Command, IReadOnlyList<string> Arguments);

    private sealed record GuardResult(
        string SchemaVersion,
        string Workspace,
        string Kind,
        string Command,
        bool Ok,
        AnchorSnapshot Anchor,
        DiffSet? Diff,
        ChangedSymbolsSection? ChangedSymbols,
        IReadOnlyList<DiffProjectFilter>? ProjectFilters,
        IReadOnlyList<SymbolMatchScore> Scores,
        double BestScore,
        string Risk,
        IReadOnlyList<string> ReasonCodes,
        GuardPolicy Policy,
        IReadOnlyList<string> Warnings,
        string RecommendedAction,
        string ProofBoundary)
    {
        public static GuardResult Invalid(string command, LoadedWorkspace workspace, AnchorSnapshot anchor, string failOnRisk, string? warning = null)
        {
            return new GuardResult(
                "navlyn.agent-guard.v1",
                workspace.DisplayPath,
                workspace.Kind,
                command,
                Ok: false,
                anchor,
                Diff: null,
                ChangedSymbols: null,
                ProjectFilters: null,
                Scores: [],
                BestScore: 0,
                Risk: "high",
                ReasonCodes: ["guard-input-invalid"],
                Policy: new GuardPolicy(failOnRisk, Passed: false),
                Warnings: warning is null ? [] : [warning],
                RecommendedAction: "Fix guard inputs and rerun.",
                ProofBoundary: "No semantic guard comparison was completed.");
        }
    }

    private sealed record GuardPolicy(string FailOnRisk, bool Passed);

    private sealed record SymbolMatchScore(double Score, DiffChangedSymbol Symbol, IReadOnlyList<string> Matches, IReadOnlyList<string> Mismatches);
}

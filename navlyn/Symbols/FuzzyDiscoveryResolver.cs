using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Navlyn.Diagnostics;
using Navlyn.Entrypoints;
using Navlyn.Workspaces;

namespace Navlyn.Symbols;

internal sealed class FuzzyDiscoveryResolver
{
    public const int DefaultCandidateLimit = 20;
    public const int DefaultReferenceLimit = 100;
    public const int DefaultReferenceFileLimit = 25;
    public const int DefaultMemberLimit = 50;
    public const int DefaultRelationLimit = 25;
    public const int DefaultFileLimit = 50;
    public const int DefaultEntrypointLimit = 25;
    public const int DefaultDepth = 3;
    public const int DefaultSnippetLines = 1;

    public async Task<FuzzyFindResult> FindAsync(
        LoadedWorkspace workspace,
        FuzzyQueryOptions options,
        IReadOnlyList<Project> projects,
        IReadOnlyList<FuzzyProjectFilter>? projectFilters,
        CancellationToken cancellationToken)
    {
        FuzzyCandidateResolution resolution = await ResolveCandidatesAsync(
            projects,
            options,
            cancellationToken);

        return CreateFindResult(workspace.DisplayPath, "find", options, projectFilters, resolution);
    }

    public async Task<FuzzyWhereUsedResult> WhereUsedAsync(
        LoadedWorkspace workspace,
        FuzzyQueryOptions options,
        FuzzyLocationOptions locationOptions,
        IReadOnlyList<Project> projects,
        IReadOnlyList<FuzzyProjectFilter>? projectFilters,
        CancellationToken cancellationToken)
    {
        FuzzyCandidateResolution resolution = await ResolveCandidatesAsync(projects, options, cancellationToken);
        FuzzyFindResult envelope = CreateFindResult(workspace.DisplayPath, "where-used", options, projectFilters, resolution);

        if (resolution.SelectedCandidate is not null)
        {
            FuzzyReferenceSummary references = await ResolveReferenceSummaryAsync(
                workspace.Solution,
                projects,
                resolution.SelectedCandidate,
                locationOptions,
                cancellationToken);

            return new FuzzyWhereUsedResult(
                Query: envelope.Query,
                Intent: envelope.Intent,
                Match: envelope.Match,
                CaseSensitive: envelope.CaseSensitive,
                Assumptions: envelope.Assumptions,
                Confidence: envelope.Confidence,
                CandidateCount: envelope.CandidateCount,
                TotalCandidates: envelope.TotalCandidates,
                Projects: envelope.Projects,
                Candidates: envelope.Candidates,
                SelectedCandidate: envelope.SelectedCandidate,
                Alternatives: envelope.Alternatives,
                Warnings: envelope.Warnings,
                NextActions: envelope.NextActions,
                Limit: locationOptions.Limit,
                TotalMatches: references.TotalMatches,
                References: references.References,
                Files: references.Files,
                CandidateResults: null,
                Truncated: references.Truncated,
                SelectionInput: envelope.SelectionInput,
                SelectionExplanation: envelope.SelectionExplanation,
                UsageKinds: locationOptions.UsageKinds.Count == 0 ? null : locationOptions.UsageKinds,
                GroupBy: locationOptions.GroupBy.Count == 0 ? null : locationOptions.GroupBy,
                UsageKindCounts: references.UsageKindCounts,
                Groups: references.Groups);
        }

        IReadOnlyList<FuzzyCandidateReferenceSummary>? candidateResults = resolution.Confidence == "ambiguous"
            ? await ResolveCandidateReferenceSummariesAsync(
                workspace.Solution,
                projects,
                envelope.Candidates,
                locationOptions,
                cancellationToken)
            : null;

        return new FuzzyWhereUsedResult(
            Query: envelope.Query,
            Intent: envelope.Intent,
            Match: envelope.Match,
            CaseSensitive: envelope.CaseSensitive,
            Assumptions: envelope.Assumptions,
            Confidence: envelope.Confidence,
            CandidateCount: envelope.CandidateCount,
            TotalCandidates: envelope.TotalCandidates,
            Projects: envelope.Projects,
            Candidates: envelope.Candidates,
            SelectedCandidate: null,
            Alternatives: null,
            Warnings: envelope.Warnings,
            NextActions: envelope.NextActions,
            Limit: locationOptions.Limit,
            TotalMatches: null,
            References: null,
            Files: null,
            CandidateResults: candidateResults,
            Truncated: null,
            SelectionInput: envelope.SelectionInput,
            SelectionExplanation: envelope.SelectionExplanation,
            UsageKinds: locationOptions.UsageKinds.Count == 0 ? null : locationOptions.UsageKinds,
            GroupBy: locationOptions.GroupBy.Count == 0 ? null : locationOptions.GroupBy);
    }

    public async Task<FuzzyAboutResult> AboutAsync(
        LoadedWorkspace workspace,
        FuzzyQueryOptions options,
        FuzzyAboutOptions aboutOptions,
        IReadOnlyList<Project> projects,
        IReadOnlyList<FuzzyProjectFilter>? projectFilters,
        CancellationToken cancellationToken)
    {
        FuzzyCandidateResolution resolution = await ResolveCandidatesAsync(projects, options, cancellationToken);
        FuzzyFindResult envelope = CreateFindResult(workspace.DisplayPath, "about", options, projectFilters, resolution);

        if (resolution.SelectedCandidate is null)
        {
            return new FuzzyAboutResult(
                Query: envelope.Query,
                Intent: envelope.Intent,
                Match: envelope.Match,
                CaseSensitive: envelope.CaseSensitive,
                Assumptions: envelope.Assumptions,
                Confidence: envelope.Confidence,
                CandidateCount: envelope.CandidateCount,
                TotalCandidates: envelope.TotalCandidates,
                Projects: envelope.Projects,
                Candidates: envelope.Candidates,
                SelectedCandidate: null,
                Alternatives: null,
                Warnings: envelope.Warnings,
                NextActions: envelope.NextActions,
                Definition: null,
                Members: null,
                References: null,
                Relations: null,
                SelectionInput: envelope.SelectionInput,
                SelectionExplanation: envelope.SelectionExplanation);
        }

        FuzzySymbolCandidate candidate = resolution.SelectedCandidate;
        FuzzyMemberSummary? members = await ResolveMembersAsync(
            workspace.Solution,
            projects,
            candidate,
            aboutOptions.MemberLimit,
            aboutOptions.ExcludeGenerated,
            cancellationToken);

        FuzzyReferenceSummary references = await ResolveReferenceSummaryAsync(
            workspace.Solution,
            projects,
            candidate,
            new FuzzyLocationOptions(
                Limit: aboutOptions.ReferenceLimit,
                FileLimit: DefaultReferenceFileLimit,
                IncludeSnippets: aboutOptions.IncludeSnippets,
                SnippetLines: aboutOptions.SnippetLines),
            cancellationToken);

        FuzzyRelationSummary relations = await ResolveRelationSummaryAsync(
            workspace.Solution,
            projects,
            candidate,
            aboutOptions.RelationLimit,
            aboutOptions.ExcludeGenerated,
            cancellationToken);

        return new FuzzyAboutResult(
            Query: envelope.Query,
            Intent: envelope.Intent,
            Match: envelope.Match,
            CaseSensitive: envelope.CaseSensitive,
            Assumptions: envelope.Assumptions,
            Confidence: envelope.Confidence,
            CandidateCount: envelope.CandidateCount,
            TotalCandidates: envelope.TotalCandidates,
            Projects: envelope.Projects,
            Candidates: envelope.Candidates,
            SelectedCandidate: envelope.SelectedCandidate,
            Alternatives: envelope.Alternatives,
            Warnings: envelope.Warnings,
            NextActions: envelope.NextActions,
            Definition: new FuzzySourceLocation(
                Path: candidate.Path,
                Line: candidate.Line,
                Column: candidate.Column,
                EndLine: candidate.EndLine,
                EndColumn: candidate.EndColumn,
                ContainingSymbol: null,
                Snippet: aboutOptions.IncludeSnippets
                    ? FuzzySnippetReader.TryRead(candidate.Path, candidate.Line, aboutOptions.SnippetLines)
                    : null),
            Members: members,
            References: references,
            Relations: relations,
            SelectionInput: envelope.SelectionInput,
            SelectionExplanation: envelope.SelectionExplanation);
    }

    public async Task<FuzzyFilesResult> FilesAsync(
        LoadedWorkspace workspace,
        string intent,
        FuzzyQueryOptions options,
        FuzzyFilesOptions filesOptions,
        IReadOnlyList<Project> projects,
        IReadOnlyList<FuzzyProjectFilter>? projectFilters,
        CancellationToken cancellationToken)
    {
        FuzzyCandidateResolution resolution = await ResolveCandidatesAsync(projects, options, cancellationToken);
        FuzzyFindResult envelope = CreateFindResult(workspace.DisplayPath, intent, options, projectFilters, resolution);

        IReadOnlyList<FuzzyRelatedFile>? files = null;
        if (resolution.SelectedCandidate is not null)
        {
            files = await ResolveRelatedFilesAsync(
                workspace.Solution,
                projects,
                resolution.SelectedCandidate,
                filesOptions,
                intent == "impact",
                cancellationToken);
        }

        return new FuzzyFilesResult(
            Query: envelope.Query,
            Intent: envelope.Intent,
            Match: envelope.Match,
            CaseSensitive: envelope.CaseSensitive,
            Assumptions: envelope.Assumptions,
            Confidence: envelope.Confidence,
            CandidateCount: envelope.CandidateCount,
            TotalCandidates: envelope.TotalCandidates,
            Projects: envelope.Projects,
            Candidates: envelope.Candidates,
            SelectedCandidate: envelope.SelectedCandidate,
            Alternatives: envelope.Alternatives,
            Warnings: envelope.Warnings,
            NextActions: envelope.NextActions,
            Include: filesOptions.Include,
            Depth: filesOptions.Depth,
            Limit: filesOptions.Limit,
            TotalFiles: files?.Count,
            Files: files is null ? null : [.. files.Take(filesOptions.Limit)],
            Truncated: files is null ? null : files.Count > filesOptions.Limit,
            SelectionInput: envelope.SelectionInput,
            SelectionExplanation: envelope.SelectionExplanation);
    }

    public async Task<FuzzyEntrypointsResult> EntrypointsAsync(
        LoadedWorkspace workspace,
        FuzzyQueryOptions options,
        FuzzyEntrypointsOptions entrypointOptions,
        IReadOnlyList<Project> projects,
        IReadOnlyList<FuzzyProjectFilter>? projectFilters,
        CancellationToken cancellationToken)
    {
        FuzzyCandidateResolution resolution = await ResolveCandidatesAsync(projects, options, cancellationToken);
        FuzzyFindResult envelope = CreateFindResult(workspace.DisplayPath, "entrypoints", options, projectFilters, resolution);

        IReadOnlyList<FuzzyEntrypointChain>? chains = null;
        FrameworkEntrypointsSection? frameworkEntrypoints = null;
        if (resolution.SelectedCandidate is not null)
        {
            chains = await ResolveEntrypointChainsAsync(
                workspace.Solution,
                projects,
                resolution.SelectedCandidate,
                entrypointOptions,
                cancellationToken);

            if (entrypointOptions.FrameworkAware)
            {
                frameworkEntrypoints = await new FrameworkEntrypointDiscoveryResolver().DiscoverSectionAsync(
                    workspace,
                    projects,
                    new FrameworkEntrypointOptions(
                        entrypointOptions.Frameworks ?? [],
                        entrypointOptions.Limit,
                        EvidenceLimit: 5,
                        entrypointOptions.IncludeSnippets,
                        entrypointOptions.SnippetLines,
                        entrypointOptions.ExcludeGenerated),
                    cancellationToken);
                chains = AnnotateFrameworkEntrypointChains(chains, frameworkEntrypoints.Items);
            }
        }

        return new FuzzyEntrypointsResult(
            Query: envelope.Query,
            Intent: envelope.Intent,
            Match: envelope.Match,
            CaseSensitive: envelope.CaseSensitive,
            Assumptions: envelope.Assumptions,
            Confidence: envelope.Confidence,
            CandidateCount: envelope.CandidateCount,
            TotalCandidates: envelope.TotalCandidates,
            Projects: envelope.Projects,
            Candidates: envelope.Candidates,
            SelectedCandidate: envelope.SelectedCandidate,
            Alternatives: envelope.Alternatives,
            Warnings: envelope.Warnings,
            NextActions: envelope.NextActions,
            Depth: entrypointOptions.Depth,
            Limit: entrypointOptions.Limit,
            TotalChains: chains?.Count,
            Chains: chains is null ? null : [.. chains.Take(entrypointOptions.Limit)],
            Truncated: chains is null ? null : chains.Count > entrypointOptions.Limit,
            FrameworkAware: entrypointOptions.FrameworkAware ? true : null,
            Frameworks: entrypointOptions.FrameworkAware ? entrypointOptions.Frameworks : null,
            FrameworkEntrypoints: frameworkEntrypoints,
            SelectionInput: envelope.SelectionInput,
            SelectionExplanation: envelope.SelectionExplanation);
    }

    public async Task<FuzzyCandidateResolution> ResolveCandidatesForSelectionAsync(
        IReadOnlyList<Project> projects,
        FuzzyQueryOptions options,
        CancellationToken cancellationToken)
    {
        return await ResolveCandidatesAsync(projects, options, cancellationToken);
    }

    private static async Task<FuzzyCandidateResolution> ResolveCandidatesAsync(
        IReadOnlyList<Project> projects,
        FuzzyQueryOptions options,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SymbolDeclaration> declarations;
        if (options.CandidateId is not null && !FuzzyCandidateIdentity.TryParseCandidateId(options.CandidateId))
        {
            return FuzzyCandidateResolution.WithError(
                DiagnosticIds.InvalidCandidateId,
                $"Invalid candidate id: {options.CandidateId}.",
                ExitCodes.UsageError);
        }

        if (options.CandidateId is null && options.Match == "regex")
        {
            SymbolSearchOptions searchOptions = new(
                Query: options.Query,
                MatchMode: SymbolMatchMode.Regex,
                CaseSensitive: options.CaseSensitive ?? false);

            if (!SymbolNameMatcher.TryCreate(searchOptions, out SymbolNameMatcher matcher, out string? error))
            {
                return FuzzyCandidateResolution.WithWarning($"Invalid regex: {error}");
            }

            declarations = await new SymbolDeclarationFinder().FindAsync(
                projects,
                matcher,
                options.ExcludeGenerated,
                cancellationToken);
        }
        else
        {
            declarations = await new SymbolDeclarationFinder().FindAllAsync(
                projects,
                options.ExcludeGenerated,
                cancellationToken);
        }

        IReadOnlyList<string> assumedKinds = options.CandidateId is null
            ? NormalizeStrings(options.AssumeKinds)
            : [];
        IReadOnlyList<RankedCandidate> rankedCandidates = [.. declarations
            .Select(declaration => CreateRankedCandidate(declaration, options, assumedKinds, projects))
            .OfType<RankedCandidate>()
            .Where(candidate => options.CandidateId is null || candidate.Symbol.CandidateId == options.CandidateId)
            .OrderBy(candidate => candidate.Rank.MatchRank)
            .ThenBy(candidate => candidate.Rank.AssumedKindRank)
            .ThenBy(candidate => candidate.Rank.TypeLikeRank)
            .ThenBy(candidate => candidate.Symbol.Facts.FullyQualifiedName?.Length ?? int.MaxValue)
            .ThenBy(candidate => candidate.Symbol.Path, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Symbol.Line)
            .ThenBy(candidate => candidate.Symbol.Column)
            .ThenBy(candidate => candidate.Symbol.Kind, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Symbol.Name, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Symbol.Container, StringComparer.Ordinal)];

        if (options.CandidateId is not null)
        {
            if (rankedCandidates.Count == 0)
            {
                return FuzzyCandidateResolution.WithError(
                    DiagnosticIds.CandidateIdNotFound,
                    $"Candidate id was not found in the current workspace: {options.CandidateId}.",
                    ExitCodes.UsageError);
            }

            if (rankedCandidates.Count > 1)
            {
                return FuzzyCandidateResolution.WithError(
                    DiagnosticIds.CandidateIdAmbiguous,
                    $"Candidate id resolved to more than one declaration in the current workspace: {options.CandidateId}.",
                    ExitCodes.UsageError);
            }

            FuzzySymbolCandidate candidate = AddSelectionReason(rankedCandidates[0].Symbol, "candidate-id-match");
            return new FuzzyCandidateResolution(
                Confidence: "high",
                TotalCandidates: 1,
                Candidates: [candidate],
                SelectedCandidate: candidate,
                Alternatives: null,
                Warnings: [],
                Error: null,
                SelectionExplanation: CreateSelectionExplanation(options, "high", candidate, selected: true, []));
        }

        FuzzySymbolCandidate? selected = SelectCandidate(rankedCandidates, out string confidence);
        List<string> ambiguityReasons = [];
        if (selected is null && rankedCandidates.Count > 0)
        {
            ambiguityReasons.Add(
                confidence == "ambiguous"
                    ? "same-rank-candidates"
                    : "no-selected-candidate");
        }

        if (selected is not null && !MeetsMinConfidence(confidence, options.EffectiveSelection.MinConfidence))
        {
            ambiguityReasons.Add("confidence-below-minimum");
            selected = null;
        }

        IReadOnlyList<FuzzySymbolCandidate> candidates = [.. rankedCandidates.Select(candidate => candidate.Symbol)];
        IReadOnlyList<FuzzySymbolCandidate>? alternatives = selected is null
            ? null
            : [.. candidates.Where(candidate => !IsSameCandidate(candidate, selected)).Take(options.Limit ?? DefaultCandidateLimit)];

        return new FuzzyCandidateResolution(
            Confidence: confidence,
            TotalCandidates: candidates.Count,
            Candidates: candidates,
            SelectedCandidate: selected,
            Alternatives: alternatives is { Count: > 0 } ? alternatives : null,
            Warnings: [],
            Error: null,
            SelectionExplanation: CreateSelectionExplanation(options, confidence, selected, selected is not null, ambiguityReasons));
    }

    private static RankedCandidate? CreateRankedCandidate(
        SymbolDeclaration declaration,
        FuzzyQueryOptions options,
        IReadOnlyList<string> assumedKinds,
        IReadOnlyList<Project> projects)
    {
        List<string> reasons = [];
        int matchRank = options.CandidateId is null
            ? GetMatchRank(declaration.Name, options, reasons)
            : 0;
        if (matchRank < 0)
        {
            return null;
        }

        int assumedKindRank = 1;
        if (assumedKinds.Count > 0 && assumedKinds.Contains(declaration.Kind, StringComparer.Ordinal))
        {
            assumedKindRank = 0;
            reasons.Add("assumed-kind-match");
        }

        reasons.Add("source-declaration");

        FuzzySymbolCandidate candidate = new(
            Name: declaration.Name,
            Kind: declaration.Kind,
            Container: declaration.Container,
            FullyQualifiedName: declaration.Facts.FullyQualifiedName,
            DocumentationCommentId: declaration.Facts.DocumentationCommentId,
            Facts: declaration.Facts,
            Path: declaration.Path,
            Line: declaration.Line,
            Column: declaration.Column,
            EndLine: declaration.EndLine,
            EndColumn: declaration.EndColumn,
            ReasonCodes: [.. reasons.Distinct(StringComparer.Ordinal)]);
        FuzzyCandidateSelector selector = FuzzyCandidateIdentity.CreateSelector(
            candidate,
            FindProjectByName(projects, declaration.Facts.Project));
        candidate = candidate with
        {
            CandidateId = FuzzyCandidateIdentity.CreateCandidateId(selector),
            Selector = selector
        };

        int typeLikeRank = IsTypeLikeQuery(options.Query) && declaration.Kind == SymbolKind.NamedType.ToString()
            ? 0
            : 1;

        return new RankedCandidate(candidate, new CandidateRank(matchRank, assumedKindRank, typeLikeRank));
    }

    private static int GetMatchRank(string name, FuzzyQueryOptions options, List<string> reasons)
    {
        StringComparison comparison = options.CaseSensitive == true
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        if (options.Match == "exact")
        {
            if (!string.Equals(name, options.Query, comparison))
            {
                return -1;
            }

            reasons.Add(options.CaseSensitive == true || string.Equals(name, options.Query, StringComparison.Ordinal)
                ? "exact-name-match"
                : "case-insensitive-exact-name-match");
            return string.Equals(name, options.Query, StringComparison.Ordinal) ? 0 : 1;
        }

        if (options.Match == "contains")
        {
            if (!name.Contains(options.Query, comparison))
            {
                return -1;
            }

            reasons.Add("contains-name-match");
            return 2;
        }

        if (options.Match == "regex")
        {
            reasons.Add("regex-name-match");
            return 4;
        }

        if (string.Equals(name, options.Query, StringComparison.Ordinal))
        {
            reasons.Add("exact-name-match");
            return 0;
        }

        if (options.CaseSensitive != true && string.Equals(name, options.Query, StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("case-insensitive-exact-name-match");
            return 1;
        }

        if (name.Contains(options.Query, comparison))
        {
            reasons.Add("contains-name-match");
            return 2;
        }

        if (NormalizeName(name) == NormalizeName(options.Query))
        {
            reasons.Add("normalized-name-match");
            return 3;
        }

        return -1;
    }

    private static FuzzySymbolCandidate? SelectCandidate(
        IReadOnlyList<RankedCandidate> rankedCandidates,
        out string confidence)
    {
        confidence = "none";
        if (rankedCandidates.Count == 0)
        {
            return null;
        }

        IReadOnlyList<RankedCandidate> caseSensitiveExact = [.. rankedCandidates
            .Where(candidate => candidate.Symbol.ReasonCodes.Contains("exact-name-match", StringComparer.Ordinal))];
        if (caseSensitiveExact.Count == 1)
        {
            confidence = rankedCandidates.Count == 1 ? "high" : "medium";
            return AddSelectionReason(caseSensitiveExact[0].Symbol, rankedCandidates.Count == 1 ? "single-candidate" : "exact-preferred-over-weaker-matches");
        }

        if (caseSensitiveExact.Count > 1)
        {
            confidence = "ambiguous";
            return null;
        }

        IReadOnlyList<RankedCandidate> caseInsensitiveExact = [.. rankedCandidates
            .Where(candidate => candidate.Symbol.ReasonCodes.Contains("case-insensitive-exact-name-match", StringComparer.Ordinal))];
        if (caseInsensitiveExact.Count == 1)
        {
            confidence = rankedCandidates.Count == 1 ? "high" : "medium";
            return AddSelectionReason(caseInsensitiveExact[0].Symbol, rankedCandidates.Count == 1 ? "single-candidate" : "exact-preferred-over-weaker-matches");
        }

        if (caseInsensitiveExact.Count > 1)
        {
            confidence = "ambiguous";
            return null;
        }

        RankedCandidate best = rankedCandidates[0];
        IReadOnlyList<RankedCandidate> sameRank = [.. rankedCandidates.Where(candidate => candidate.Rank == best.Rank)];
        if (sameRank.Count == 1)
        {
            confidence = rankedCandidates.Count == 1 ? "high" : "low";
            return AddSelectionReason(best.Symbol, rankedCandidates.Count == 1 ? "single-candidate" : "dominant-ranked-candidate");
        }

        confidence = "ambiguous";
        return null;
    }

    private static FuzzySymbolCandidate AddSelectionReason(FuzzySymbolCandidate candidate, string reason)
    {
        return candidate with { ReasonCodes = [.. candidate.ReasonCodes.Append(reason).Distinct(StringComparer.Ordinal)] };
    }

    private static bool MeetsMinConfidence(string confidence, string minConfidence)
    {
        return ConfidenceRank(confidence) >= ConfidenceRank(minConfidence);
    }

    private static int ConfidenceRank(string confidence)
    {
        return confidence switch
        {
            "high" => 3,
            "medium" => 2,
            "low" => 1,
            _ => 0
        };
    }

    private static FuzzySelectionExplanation CreateSelectionExplanation(
        FuzzyQueryOptions options,
        string confidence,
        FuzzySymbolCandidate? selectedCandidate,
        bool selected,
        IReadOnlyList<string> ambiguityReasons)
    {
        return new FuzzySelectionExplanation(
            Policy: options.EffectiveSelection.CandidatePolicy,
            MinConfidence: options.EffectiveSelection.MinConfidence,
            Selected: selected,
            SelectedCandidateId: selectedCandidate?.CandidateId,
            ReasonCodes: selectedCandidate?.ReasonCodes ?? [],
            RankInputs:
            [
                "match-rank",
                "assumed-kind-rank",
                "type-like-rank",
                "fully-qualified-name-length",
                "source-location"
            ],
            AmbiguityReasons: ambiguityReasons);
    }

    private static FuzzyFindResult CreateFindResult(
        string workspace,
        string intent,
        FuzzyQueryOptions options,
        IReadOnlyList<FuzzyProjectFilter>? projectFilters,
        FuzzyCandidateResolution resolution)
    {
        int limit = options.Limit ?? DefaultCandidateLimit;
        IReadOnlyList<FuzzySymbolCandidate> candidates = [.. resolution.Candidates.Take(limit)];
        FuzzySymbolCandidate? selected = resolution.SelectedCandidate;
        IReadOnlyList<FuzzySymbolCandidate>? alternatives = selected is null || resolution.Alternatives is null
            ? null
            : [.. resolution.Alternatives.Take(limit)];

        return new FuzzyFindResult(
            Query: options.Query,
            Intent: intent,
            Match: options.Match,
            CaseSensitive: options.CaseSensitive,
            Assumptions: new FuzzyAssumptions(NormalizeStrings(options.AssumeKinds)),
            Confidence: resolution.Confidence,
            CandidateCount: candidates.Count,
            TotalCandidates: resolution.TotalCandidates,
            Projects: projectFilters,
            Candidates: candidates,
            SelectedCandidate: selected,
            Alternatives: alternatives is { Count: > 0 } ? alternatives : null,
            Warnings: resolution.Warnings,
            NextActions: CreateNextActions(workspace, selected),
            CandidateLimit: limit,
            CandidatesTruncated: resolution.TotalCandidates > candidates.Count,
            SelectionInput: options.CandidateId is null
                ? new FuzzySelectionInput("query", CandidateId: null)
                : new FuzzySelectionInput("candidateId", options.CandidateId),
            SelectionExplanation: options.EffectiveSelection.ExplainSelection
                ? resolution.SelectionExplanation
                : null);
    }

    private static IReadOnlyList<FuzzyNextAction> CreateNextActions(string workspace, FuzzySymbolCandidate? candidate)
    {
        if (candidate is null)
        {
            return [
                new FuzzyNextAction("find", workspace, Query: null, File: null, Line: null, Column: null, Reason: "try-broader-query"),
                new FuzzyNextAction("symbols", workspace, Query: null, File: null, Line: null, Column: null, Reason: "try-precise-symbol-search")
            ];
        }

        return [
            new FuzzyNextAction(
                "definition",
                workspace,
                Query: null,
                File: candidate.Path,
                Line: candidate.Line,
                Column: candidate.Column,
                Reason: "inspect-selected-definition",
                CandidateId: candidate.CandidateId,
                McpTool: "navlyn_exact_navigation",
                Arguments: CreateMcpArguments(("operation", "definition"), ("candidateId", candidate.CandidateId))),
            new FuzzyNextAction(
                "references",
                workspace,
                Query: null,
                File: candidate.Path,
                Line: candidate.Line,
                Column: candidate.Column,
                Reason: "inspect-selected-references",
                CandidateId: candidate.CandidateId,
                McpTool: "navlyn_exact_navigation",
                Arguments: CreateMcpArguments(("operation", "references"), ("candidateId", candidate.CandidateId))),
            new FuzzyNextAction(
                "about",
                workspace,
                Query: candidate.Name,
                File: null,
                Line: null,
                Column: null,
                Reason: "summarize-selected-symbol",
                CandidateId: candidate.CandidateId,
                McpTool: "navlyn_about_symbol",
                Arguments: CreateMcpArguments(("candidateId", candidate.CandidateId)))
        ];
    }

    private static IReadOnlyDictionary<string, object?>? CreateMcpArguments(
        params (string Key, object? Value)[] values)
    {
        Dictionary<string, object?> arguments = [];
        foreach ((string key, object? value) in values)
        {
            if (value is not null)
            {
                arguments[key] = value;
            }
        }

        return arguments.Count == 0 ? null : arguments;
    }

    private static async Task<IReadOnlyList<FuzzyCandidateReferenceSummary>> ResolveCandidateReferenceSummariesAsync(
        Solution solution,
        IReadOnlyList<Project> projects,
        IReadOnlyList<FuzzySymbolCandidate> candidates,
        FuzzyLocationOptions options,
        CancellationToken cancellationToken)
    {
        List<FuzzyCandidateReferenceSummary> summaries = [];
        foreach (FuzzySymbolCandidate candidate in candidates)
        {
            FuzzyReferenceSummary references = await ResolveReferenceSummaryAsync(
                solution,
                projects,
                candidate,
                options,
                cancellationToken);

            summaries.Add(new FuzzyCandidateReferenceSummary(candidate, references.TotalMatches, references.Files));
        }

        return summaries;
    }

    private static async Task<FuzzyReferenceSummary> ResolveReferenceSummaryAsync(
        Solution solution,
        IReadOnlyList<Project> projects,
        FuzzySymbolCandidate candidate,
        FuzzyLocationOptions options,
        CancellationToken cancellationToken)
    {
        ReferencesResolutionResult result = await new ReferencesResolver().ResolveAsync(
            solution,
            new FileInfo(candidate.Path),
            candidate.Line,
            candidate.Column,
            FindCandidateProject(projects, candidate),
            options.ExcludeGenerated,
            cancellationToken);

        if (result.Error is not null)
        {
            return new FuzzyReferenceSummary(0, options.Limit, false, [], []);
        }

        IReadOnlyList<SymbolReferenceLocation> filteredReferences = [.. result.Resolution!.References
            .Where(reference => options.UsageKinds.Count == 0 || options.UsageKinds.Contains(reference.UsageKind, StringComparer.Ordinal))];

        IReadOnlyList<FuzzySourceLocation> references = [.. filteredReferences
            .Take(options.Limit)
            .Select(reference => new FuzzySourceLocation(
                Path: reference.Path,
                Line: reference.Line,
                Column: reference.Column,
                EndLine: reference.EndLine,
                EndColumn: reference.EndColumn,
                ContainingSymbol: reference.ContainingSymbol is null
                    ? null
                    : new FuzzySymbolLocation(
                        Name: reference.ContainingSymbol.Name,
                        Kind: reference.ContainingSymbol.Kind,
                        Container: reference.ContainingSymbol.Container,
                        Facts: reference.ContainingSymbol.Facts,
                        Path: reference.ContainingSymbol.Path,
                        Line: reference.ContainingSymbol.Line,
                        Column: reference.ContainingSymbol.Column,
                        EndLine: reference.ContainingSymbol.EndLine,
                        EndColumn: reference.ContainingSymbol.EndColumn),
                Snippet: options.IncludeSnippets
                    ? FuzzySnippetReader.TryRead(reference.Path, reference.Line, options.SnippetLines)
                    : null,
                UsageKind: reference.UsageKind))];

        IReadOnlyList<FuzzyReferenceFileSummary> files = [.. filteredReferences
            .GroupBy(reference => reference.Path)
            .Select(group => new FuzzyReferenceFileSummary(
                Path: group.Key,
                ReferenceCount: group.Count(),
                FirstLine: group.Min(reference => reference.Line),
                Reasons: ["references-selected-symbol"],
                UsageKindCounts: ReferenceUsageTaxonomy.CreateCounts([.. group]),
                Snippet: options.IncludeSnippets
                    ? FuzzySnippetReader.TryRead(group.Key, group.Min(reference => reference.Line), options.SnippetLines)
                    : null))
            .OrderByDescending(file => file.ReferenceCount)
            .ThenBy(file => file.Path, StringComparer.Ordinal)
            .Take(options.FileLimit)];

        return new FuzzyReferenceSummary(
            filteredReferences.Count,
            options.Limit,
            filteredReferences.Count > references.Count,
            references,
            files,
            UsageKindCounts: ReferenceUsageTaxonomy.CreateCounts(filteredReferences),
            Groups: ReferenceUsageTaxonomy.CreateGroups(filteredReferences, options.GroupBy));
    }

    private static async Task<FuzzyMemberSummary?> ResolveMembersAsync(
        Solution solution,
        IReadOnlyList<Project> projects,
        FuzzySymbolCandidate candidate,
        int limit,
        bool excludeGenerated,
        CancellationToken cancellationToken)
    {
        if (candidate.Kind != SymbolKind.NamedType.ToString())
        {
            return null;
        }

        OutlineResolutionResult result = await new OutlineResolver().ResolveAsync(
            solution,
            new FileInfo(candidate.Path),
            FindCandidateProject(projects, candidate),
            excludeGenerated,
            cancellationToken);

        if (result.Error is not null)
        {
            return null;
        }

        string? fullName = candidate.FullyQualifiedName;
        IReadOnlyList<FuzzyMemberEntry> members = [.. result.Resolution!.Entries
            .Where(entry => fullName is not null && entry.Container is not null &&
                (entry.Container == fullName || entry.Container.StartsWith(fullName + ".", StringComparison.Ordinal)))
            .Where(entry => entry.Line != candidate.Line || entry.Column != candidate.Column)
            .OrderBy(entry => entry.Line)
            .ThenBy(entry => entry.Column)
            .Select(entry => new FuzzyMemberEntry(
                Name: entry.Name,
                Kind: entry.Kind,
                Container: entry.Container,
                Facts: entry.Facts,
                Path: entry.Path,
                Line: entry.Line,
                Column: entry.Column,
                EndLine: entry.EndLine,
                EndColumn: entry.EndColumn))];

        return new FuzzyMemberSummary(
            TotalMembers: members.Count,
            Limit: limit,
            Truncated: members.Count > limit,
            Members: [.. members.Take(limit)]);
    }

    private static async Task<FuzzyRelationSummary> ResolveRelationSummaryAsync(
        Solution solution,
        IReadOnlyList<Project> projects,
        FuzzySymbolCandidate candidate,
        int limit,
        bool excludeGenerated,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<FuzzySymbolLocation> callers = [];
        IReadOnlyList<FuzzySymbolLocation> calls = [];
        IReadOnlyList<FuzzySymbolLocation> implementations = [];
        FuzzyHierarchySummary? hierarchy = null;

        Project? project = FindCandidateProject(projects, candidate);
        if (candidate.Kind is "Method" or "Property" or "Event")
        {
            CallersResolutionResult callersResult = await new CallHierarchyResolver().ResolveCallersAsync(
                solution,
                new FileInfo(candidate.Path),
                candidate.Line,
                candidate.Column,
                project,
                excludeGenerated,
                cancellationToken);

            if (callersResult.Error is null)
            {
                callers = [.. callersResult.Resolution!.Callers
                    .Select(group => ToSymbolLocation(group.Symbol))
                    .Take(limit)];
            }

            CallsResolutionResult callsResult = await new CallHierarchyResolver().ResolveCallsAsync(
                solution,
                new FileInfo(candidate.Path),
                candidate.Line,
                candidate.Column,
                project,
                excludeGenerated,
                includeMetadata: false,
                cancellationToken);

            if (callsResult.Error is null)
            {
                calls = [.. callsResult.Resolution!.Calls
                    .Select(group => ToSymbolLocation(group.Symbol))
                    .Take(limit)];
            }
        }

        ImplementationsResolutionResult implementationsResult = await new ImplementationsResolver().ResolveAsync(
            solution,
            new FileInfo(candidate.Path),
            candidate.Line,
            candidate.Column,
            project,
            excludeGenerated,
            cancellationToken);

        if (implementationsResult.Error is null)
        {
            implementations = [.. implementationsResult.Resolution!.Implementations
                .Select(implementation => new FuzzySymbolLocation(
                    Name: implementation.Name,
                    Kind: implementation.Kind,
                    Container: implementation.Container,
                    Facts: implementation.Facts,
                    Path: implementation.Path,
                    Line: implementation.Line,
                    Column: implementation.Column,
                    EndLine: implementation.EndLine,
                    EndColumn: implementation.EndColumn))
                .Take(limit)];
        }

        TypeHierarchyResolutionResult hierarchyResult = await new TypeHierarchyResolver().ResolveAsync(
            solution,
            new FileInfo(candidate.Path),
            candidate.Line,
            candidate.Column,
            project,
            excludeGenerated,
            cancellationToken);

        if (hierarchyResult.Error is null)
        {
            TypeHierarchyResolution resolution = hierarchyResult.Resolution!;
            hierarchy = new FuzzyHierarchySummary(
                BaseTypes: [.. resolution.BaseTypes.Select(ToSymbolLocation).Take(limit)],
                Interfaces: [.. resolution.Interfaces.Select(ToSymbolLocation).Take(limit)],
                DerivedTypes: [.. resolution.DerivedTypes.Select(ToSymbolLocation).Take(limit)],
                ImplementingTypes: [.. resolution.ImplementingTypes.Select(ToSymbolLocation).Take(limit)],
                BaseMembers: [.. resolution.BaseMembers.Select(ToSymbolLocation).Take(limit)],
                OverridingMembers: [.. resolution.OverridingMembers.Select(ToSymbolLocation).Take(limit)],
                ImplementedMembers: [.. resolution.ImplementedMembers.Select(ToSymbolLocation).Take(limit)]);
        }

        return new FuzzyRelationSummary(callers, calls, implementations, hierarchy);
    }

    private static async Task<IReadOnlyList<FuzzyRelatedFile>> ResolveRelatedFilesAsync(
        Solution solution,
        IReadOnlyList<Project> projects,
        FuzzySymbolCandidate candidate,
        FuzzyFilesOptions options,
        bool impactMode,
        CancellationToken cancellationToken)
    {
        Dictionary<string, RelatedFileBuilder> files = new(StringComparer.Ordinal);
        Project? project = FindCandidateProject(projects, candidate);

        if (options.Include.Contains("declarations", StringComparer.Ordinal))
        {
            AddFile(files, candidate.Path, "declares-selected-symbol", candidate.Line, candidate.Column, candidate.EndLine, candidate.EndColumn, impactMode ? "direct" : null, options);
        }

        if (options.Include.Contains("references", StringComparer.Ordinal))
        {
            FuzzyReferenceSummary references = await ResolveReferenceSummaryAsync(
                solution,
                projects,
                candidate,
                new FuzzyLocationOptions(options.Limit, options.Limit, options.IncludeSnippets, options.SnippetLines, options.ExcludeGenerated),
                cancellationToken);

            foreach (FuzzySourceLocation reference in references.References)
            {
                AddFile(files, reference.Path, "references-selected-symbol", reference.Line, reference.Column, reference.EndLine, reference.EndColumn, impactMode ? "direct" : null, options);
            }
        }

        if (candidate.Kind is "Method" or "Property" or "Event")
        {
            if (options.Include.Contains("callers", StringComparer.Ordinal))
            {
                CallersResolutionResult result = await new CallHierarchyResolver().ResolveCallersAsync(
                    solution,
                    new FileInfo(candidate.Path),
                    candidate.Line,
                    candidate.Column,
                    project,
                    options.ExcludeGenerated,
                    cancellationToken);

                if (result.Error is null)
                {
                    foreach (CallHierarchyGroup group in result.Resolution!.Callers)
                    {
                        foreach (CallHierarchyLocation location in group.Locations)
                        {
                            AddFile(files, location.Path, "caller-of-selected-member", location.Line, location.Column, location.EndLine, location.EndColumn, impactMode ? "direct" : null, options);
                        }
                    }
                }
            }

            if (options.Include.Contains("calls", StringComparer.Ordinal))
            {
                CallsResolutionResult result = await new CallHierarchyResolver().ResolveCallsAsync(
                    solution,
                    new FileInfo(candidate.Path),
                    candidate.Line,
                    candidate.Column,
                    project,
                    options.ExcludeGenerated,
                    includeMetadata: false,
                    cancellationToken);

                if (result.Error is null)
                {
                    foreach (CallHierarchyGroup group in result.Resolution!.Calls)
                    {
                        if (group.Symbol.Path is not null &&
                            group.Symbol.Line is not null &&
                            group.Symbol.Column is not null &&
                            group.Symbol.EndLine is not null &&
                            group.Symbol.EndColumn is not null)
                        {
                            AddFile(files, group.Symbol.Path, "callee-of-selected-member", group.Symbol.Line.Value, group.Symbol.Column.Value, group.Symbol.EndLine.Value, group.Symbol.EndColumn.Value, impactMode ? "indirect" : null, options);
                        }
                    }
                }
            }
        }

        if (options.Include.Contains("implementations", StringComparer.Ordinal))
        {
            ImplementationsResolutionResult result = await new ImplementationsResolver().ResolveAsync(
                solution,
                new FileInfo(candidate.Path),
                candidate.Line,
                candidate.Column,
                project,
                options.ExcludeGenerated,
                cancellationToken);

            if (result.Error is null)
            {
                foreach (ImplementationLocation implementation in result.Resolution!.Implementations)
                {
                    AddFile(files, implementation.Path, "implements-selected-symbol", implementation.Line, implementation.Column, implementation.EndLine, implementation.EndColumn, impactMode ? "indirect" : null, options);
                }
            }
        }

        if (options.Include.Contains("hierarchy", StringComparer.Ordinal))
        {
            TypeHierarchyResolutionResult result = await new TypeHierarchyResolver().ResolveAsync(
                solution,
                new FileInfo(candidate.Path),
                candidate.Line,
                candidate.Column,
                project,
                options.ExcludeGenerated,
                cancellationToken);

            if (result.Error is null)
            {
                foreach (HierarchySymbol symbol in result.Resolution!.DerivedTypes.Concat(result.Resolution.ImplementingTypes))
                {
                    if (symbol.Path is not null &&
                        symbol.Line is not null &&
                        symbol.Column is not null &&
                        symbol.EndLine is not null &&
                        symbol.EndColumn is not null)
                    {
                        AddFile(files, symbol.Path, "hierarchy-related-symbol", symbol.Line.Value, symbol.Column.Value, symbol.EndLine.Value, symbol.EndColumn.Value, impactMode ? "indirect" : null, options);
                    }
                }
            }
        }

        return [.. files.Values
            .Select(builder => builder.Build())
            .OrderBy(file => ReasonPriority(file.Reasons))
            .ThenBy(file => file.ImpactLevel ?? "", StringComparer.Ordinal)
            .ThenBy(file => file.Path, StringComparer.Ordinal)
            .Take(options.Limit)];
    }

    private static async Task<IReadOnlyList<FuzzyEntrypointChain>> ResolveEntrypointChainsAsync(
        Solution solution,
        IReadOnlyList<Project> projects,
        FuzzySymbolCandidate candidate,
        FuzzyEntrypointsOptions options,
        CancellationToken cancellationToken)
    {
        if (candidate.Kind is not ("Method" or "Property" or "Event"))
        {
            return [];
        }

        List<FuzzyEntrypointChain> chains = [];
        await TraverseCallersAsync(
            solution,
            projects,
            ToSymbolLocation(candidate),
            [ToSymbolLocation(candidate)],
            options,
            chains,
            new HashSet<string>(StringComparer.Ordinal),
            cancellationToken);

        return [.. chains
            .OrderBy(chain => chain.Symbols.Count)
            .ThenBy(chain => chain.Symbols.Last().Path, StringComparer.Ordinal)
            .ThenBy(chain => chain.Symbols.Last().Line)
            .ThenBy(chain => chain.Symbols.Last().Column)
            .Take(options.Limit)];
    }

    private static IReadOnlyList<FuzzyEntrypointChain> AnnotateFrameworkEntrypointChains(
        IReadOnlyList<FuzzyEntrypointChain> chains,
        IReadOnlyList<FrameworkEntrypointItem> entrypoints)
    {
        if (entrypoints.Count == 0)
        {
            return chains;
        }

        return [.. chains.Select(chain =>
        {
            FuzzySymbolLocation terminal = chain.Symbols.Last();
            FrameworkEntrypointItem? entrypoint = entrypoints.FirstOrDefault(item => FrameworkEntrypointDiscoveryResolver.Matches(item, terminal));
            return entrypoint is null
                ? chain
                : chain with
                {
                    EndReason = "framework-entrypoint",
                    Entrypoint = entrypoint
                };
        })];
    }

    private static async Task TraverseCallersAsync(
        Solution solution,
        IReadOnlyList<Project> projects,
        FuzzySymbolLocation symbol,
        IReadOnlyList<FuzzySymbolLocation> chain,
        FuzzyEntrypointsOptions options,
        List<FuzzyEntrypointChain> chains,
        HashSet<string> visited,
        CancellationToken cancellationToken)
    {
        if (chains.Count >= options.Limit || symbol.Path is null || symbol.Line is null || symbol.Column is null)
        {
            return;
        }

        string key = $"{symbol.Path}:{symbol.Line}:{symbol.Column}";
        if (!visited.Add(key))
        {
            chains.Add(new FuzzyEntrypointChain(chain, "cycle-detected"));
            return;
        }

        if (chain.Count > options.Depth)
        {
            chains.Add(new FuzzyEntrypointChain(chain, "depth-limit"));
            return;
        }

        CallersResolutionResult result = await new CallHierarchyResolver().ResolveCallersAsync(
            solution,
            new FileInfo(symbol.Path),
            symbol.Line.Value,
            symbol.Column.Value,
            FindProjectByName(projects, symbol.Facts.Project),
            options.ExcludeGenerated,
            cancellationToken);

        if (result.Error is not null || result.Resolution!.Callers.Count == 0)
        {
            chains.Add(new FuzzyEntrypointChain(chain, "no-upstream-callers"));
            return;
        }

        foreach (CallHierarchyGroup caller in result.Resolution.Callers)
        {
            FuzzySymbolLocation callerSymbol = ToSymbolLocation(caller.Symbol);
            if (options.IncludeSnippets && callerSymbol.Path is not null && callerSymbol.Line is not null)
            {
                callerSymbol = callerSymbol with
                {
                    Snippet = FuzzySnippetReader.TryRead(callerSymbol.Path, callerSymbol.Line.Value, options.SnippetLines)
                };
            }

            await TraverseCallersAsync(
                solution,
                projects,
                callerSymbol,
                [.. chain, callerSymbol],
                options,
                chains,
                new HashSet<string>(visited, StringComparer.Ordinal),
                cancellationToken);
        }
    }

    private static void AddFile(
        Dictionary<string, RelatedFileBuilder> files,
        string path,
        string reason,
        int line,
        int column,
        int endLine,
        int endColumn,
        string? impactLevel,
        FuzzyFilesOptions options)
    {
        if (!files.TryGetValue(path, out RelatedFileBuilder? builder))
        {
            builder = new RelatedFileBuilder(path);
            files.Add(path, builder);
        }

        builder.Reasons.Add(reason);
        if (impactLevel is not null)
        {
            builder.ImpactLevels.Add(impactLevel);
        }

        builder.Locations.Add(new FuzzySourceLocation(
            Path: path,
            Line: line,
            Column: column,
            EndLine: endLine,
            EndColumn: endColumn,
            ContainingSymbol: null,
            Snippet: options.IncludeSnippets
                ? FuzzySnippetReader.TryRead(path, line, options.SnippetLines)
                : null));
    }

    private static int ReasonPriority(IReadOnlyList<string> reasons)
    {
        string[] priority =
        [
            "declares-selected-symbol",
            "references-selected-symbol",
            "caller-of-selected-member",
            "implements-selected-symbol",
            "hierarchy-related-symbol",
            "callee-of-selected-member"
        ];

        for (int i = 0; i < priority.Length; i++)
        {
            if (reasons.Contains(priority[i], StringComparer.Ordinal))
            {
                return i;
            }
        }

        return priority.Length;
    }

    private static FuzzySymbolLocation ToSymbolLocation(FuzzySymbolCandidate candidate)
    {
        return new FuzzySymbolLocation(
            Name: candidate.Name,
            Kind: candidate.Kind,
            Container: candidate.Container,
            Facts: candidate.Facts,
            Path: candidate.Path,
            Line: candidate.Line,
            Column: candidate.Column,
            EndLine: candidate.EndLine,
            EndColumn: candidate.EndColumn);
    }

    private static FuzzySymbolLocation ToSymbolLocation(CallHierarchySymbol symbol)
    {
        return new FuzzySymbolLocation(
            symbol.Name,
            symbol.Kind,
            symbol.Container,
            symbol.Facts,
            symbol.Path,
            symbol.Line,
            symbol.Column,
            symbol.EndLine,
            symbol.EndColumn);
    }

    private static FuzzySymbolLocation ToSymbolLocation(HierarchySymbol symbol)
    {
        return new FuzzySymbolLocation(
            symbol.Name,
            symbol.Kind,
            symbol.Container,
            symbol.Facts,
            symbol.Path,
            symbol.Line,
            symbol.Column,
            symbol.EndLine,
            symbol.EndColumn);
    }

    private static Project? FindCandidateProject(IReadOnlyList<Project> projects, FuzzySymbolCandidate candidate)
    {
        return FindProjectByName(projects, candidate.Facts.Project);
    }

    private static Project? FindProjectByName(IReadOnlyList<Project> projects, string? projectName)
    {
        return projectName is null
            ? null
            : projects.FirstOrDefault(project => project.Name == projectName);
    }

    private static IReadOnlyList<string> NormalizeStrings(IReadOnlyList<string> values)
    {
        return [.. values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)];
    }

    private static string NormalizeName(string value)
    {
        return string.Concat(value.Where(char.IsLetterOrDigit)).ToUpperInvariant();
    }

    private static bool IsTypeLikeQuery(string query)
    {
        return query.Length > 0 && char.IsUpper(query[0]) && !query.Contains(' ');
    }

    private static bool IsSameCandidate(FuzzySymbolCandidate left, FuzzySymbolCandidate right)
    {
        return left.Path == right.Path &&
            left.Line == right.Line &&
            left.Column == right.Column &&
            left.Kind == right.Kind &&
            left.Name == right.Name;
    }

    private sealed record CandidateRank(int MatchRank, int AssumedKindRank, int TypeLikeRank);

    private sealed record RankedCandidate(FuzzySymbolCandidate Symbol, CandidateRank Rank);

    private sealed class RelatedFileBuilder(string path)
    {
        public string Path { get; } = path;

        public HashSet<string> Reasons { get; } = new(StringComparer.Ordinal);

        public HashSet<string> ImpactLevels { get; } = new(StringComparer.Ordinal);

        public List<FuzzySourceLocation> Locations { get; } = [];

        public FuzzyRelatedFile Build()
        {
            IReadOnlyList<FuzzySourceLocation> locations = [.. Locations
                .GroupBy(location => (location.Path, location.Line, location.Column, location.EndLine, location.EndColumn))
                .Select(group => group.First())
                .OrderBy(location => location.Line)
                .ThenBy(location => location.Column)
                .ThenBy(location => location.EndLine)
                .ThenBy(location => location.EndColumn)];

            string? impactLevel = ImpactLevels.Contains("direct")
                ? "direct"
                : ImpactLevels.Contains("indirect")
                    ? "indirect"
                    : null;

            return new FuzzyRelatedFile(
                Path: Path,
                Reasons: [.. Reasons.OrderBy(ReasonPriorityValue).ThenBy(reason => reason, StringComparer.Ordinal)],
                ImpactLevel: impactLevel,
                Locations: locations);
        }

        private static int ReasonPriorityValue(string reason)
        {
            return ReasonPriority([reason]);
        }
    }
}

internal sealed record FuzzyQueryOptions(
    string Query,
    IReadOnlyList<string> AssumeKinds,
    string Match,
    bool? CaseSensitive,
    bool ExcludeGenerated,
    int? Limit,
    string? CandidateId = null,
    FuzzySelectionOptions? Selection = null)
{
    public FuzzySelectionOptions EffectiveSelection => Selection ?? FuzzySelectionOptions.Default;
}

internal sealed record FuzzySelectionOptions(
    string CandidatePolicy,
    string MinConfidence,
    bool ExplainSelection)
{
    public static FuzzySelectionOptions Default { get; } = new("select", "low", false);
}

internal sealed record FuzzyLocationOptions(
    int Limit,
    int FileLimit,
    bool IncludeSnippets,
    int SnippetLines,
    bool ExcludeGenerated = false,
    IReadOnlyList<string>? UsageKindFilters = null,
    IReadOnlyList<string>? GroupByValues = null)
{
    public IReadOnlyList<string> UsageKinds { get; } = UsageKindFilters ?? [];

    public IReadOnlyList<string> GroupBy { get; } = GroupByValues ?? [];
}

internal sealed record FuzzyAboutOptions(
    int MemberLimit,
    int ReferenceLimit,
    int RelationLimit,
    bool IncludeSnippets,
    int SnippetLines,
    bool ExcludeGenerated);

internal sealed record FuzzyFilesOptions(
    IReadOnlyList<string> Include,
    int Limit,
    int Depth,
    bool IncludeSnippets,
    int SnippetLines,
    bool ExcludeGenerated);

internal sealed record FuzzyEntrypointsOptions(
    int Depth,
    int Limit,
    bool IncludeSnippets,
    int SnippetLines,
    bool ExcludeGenerated,
    bool FrameworkAware = false,
    IReadOnlyList<string>? Frameworks = null);

internal sealed record FuzzyCandidateResolution(
    string Confidence,
    int TotalCandidates,
    IReadOnlyList<FuzzySymbolCandidate> Candidates,
    FuzzySymbolCandidate? SelectedCandidate,
    IReadOnlyList<FuzzySymbolCandidate>? Alternatives,
    IReadOnlyList<string> Warnings,
    SymbolNavigationError? Error = null,
    FuzzySelectionExplanation? SelectionExplanation = null)
{
    public static FuzzyCandidateResolution WithWarning(string warning)
    {
        return new FuzzyCandidateResolution("none", 0, [], null, null, [warning]);
    }

    public static FuzzyCandidateResolution WithError(int diagnosticId, string message, int exitCode)
    {
        return new FuzzyCandidateResolution(
            "none",
            0,
            [],
            null,
            null,
            [],
            new SymbolNavigationError(diagnosticId, message, exitCode));
    }
}

internal sealed record FuzzyFindResult(
    string Query,
    string Intent,
    string Match,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? CaseSensitive,
    FuzzyAssumptions Assumptions,
    string Confidence,
    int CandidateCount,
    int TotalCandidates,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<FuzzyProjectFilter>? Projects,
    IReadOnlyList<FuzzySymbolCandidate> Candidates,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FuzzySymbolCandidate? SelectedCandidate,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<FuzzySymbolCandidate>? Alternatives,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<FuzzyNextAction> NextActions,
    int CandidateLimit = FuzzyDiscoveryResolver.DefaultCandidateLimit,
    bool CandidatesTruncated = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FuzzySelectionInput? SelectionInput = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FuzzySelectionExplanation? SelectionExplanation = null);

internal sealed record FuzzyWhereUsedResult(
    string Query,
    string Intent,
    string Match,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? CaseSensitive,
    FuzzyAssumptions Assumptions,
    string Confidence,
    int CandidateCount,
    int TotalCandidates,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<FuzzyProjectFilter>? Projects,
    IReadOnlyList<FuzzySymbolCandidate> Candidates,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FuzzySymbolCandidate? SelectedCandidate,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<FuzzySymbolCandidate>? Alternatives,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<FuzzyNextAction> NextActions,
    int Limit,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? TotalMatches,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<FuzzySourceLocation>? References,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<FuzzyReferenceFileSummary>? Files,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<FuzzyCandidateReferenceSummary>? CandidateResults,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? Truncated = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FuzzySelectionInput? SelectionInput = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FuzzySelectionExplanation? SelectionExplanation = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? UsageKinds = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? GroupBy = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<ReferenceUsageCount>? UsageKindCounts = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<ReferenceUsageGroup>? Groups = null);

internal sealed record FuzzyAboutResult(
    string Query,
    string Intent,
    string Match,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? CaseSensitive,
    FuzzyAssumptions Assumptions,
    string Confidence,
    int CandidateCount,
    int TotalCandidates,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<FuzzyProjectFilter>? Projects,
    IReadOnlyList<FuzzySymbolCandidate> Candidates,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FuzzySymbolCandidate? SelectedCandidate,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<FuzzySymbolCandidate>? Alternatives,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<FuzzyNextAction> NextActions,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FuzzySourceLocation? Definition,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FuzzyMemberSummary? Members,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FuzzyReferenceSummary? References,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FuzzyRelationSummary? Relations,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FuzzySelectionInput? SelectionInput = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FuzzySelectionExplanation? SelectionExplanation = null);

internal sealed record FuzzyFilesResult(
    string Query,
    string Intent,
    string Match,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? CaseSensitive,
    FuzzyAssumptions Assumptions,
    string Confidence,
    int CandidateCount,
    int TotalCandidates,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<FuzzyProjectFilter>? Projects,
    IReadOnlyList<FuzzySymbolCandidate> Candidates,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FuzzySymbolCandidate? SelectedCandidate,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<FuzzySymbolCandidate>? Alternatives,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<FuzzyNextAction> NextActions,
    IReadOnlyList<string> Include,
    int Depth,
    int Limit,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? TotalFiles,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<FuzzyRelatedFile>? Files,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? Truncated = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FuzzySelectionInput? SelectionInput = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FuzzySelectionExplanation? SelectionExplanation = null);

internal sealed record FuzzyEntrypointsResult(
    string Query,
    string Intent,
    string Match,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? CaseSensitive,
    FuzzyAssumptions Assumptions,
    string Confidence,
    int CandidateCount,
    int TotalCandidates,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<FuzzyProjectFilter>? Projects,
    IReadOnlyList<FuzzySymbolCandidate> Candidates,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FuzzySymbolCandidate? SelectedCandidate,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<FuzzySymbolCandidate>? Alternatives,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<FuzzyNextAction> NextActions,
    int Depth,
    int Limit,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? TotalChains,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<FuzzyEntrypointChain>? Chains,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? Truncated = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? FrameworkAware = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? Frameworks = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FrameworkEntrypointsSection? FrameworkEntrypoints = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FuzzySelectionInput? SelectionInput = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FuzzySelectionExplanation? SelectionExplanation = null);

internal sealed record FuzzyAssumptions(IReadOnlyList<string> Kinds);

internal sealed record FuzzyProjectFilter(
    string Filter,
    string Name,
    string? Path,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? TargetFramework);

internal sealed record FuzzySymbolCandidate(
    string Name,
    string Kind,
    string? Container,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? FullyQualifiedName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? DocumentationCommentId,
    SymbolFacts Facts,
    string Path,
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    IReadOnlyList<string> ReasonCodes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CandidateId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FuzzyCandidateSelector? Selector = null);

internal sealed record FuzzySymbolLocation(
    string Name,
    string Kind,
    string? Container,
    SymbolFacts Facts,
    string? Path,
    int? Line,
    int? Column,
    int? EndLine,
    int? EndColumn,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FuzzySnippet? Snippet = null);

internal sealed record FuzzySourceLocation(
    string Path,
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FuzzySymbolLocation? ContainingSymbol,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FuzzySnippet? Snippet,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? UsageKind = null);

internal sealed record FuzzyReferenceSummary(
    int TotalMatches,
    int Limit,
    bool Truncated,
    IReadOnlyList<FuzzySourceLocation> References,
    IReadOnlyList<FuzzyReferenceFileSummary> Files,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<ReferenceUsageCount>? UsageKindCounts = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<ReferenceUsageGroup>? Groups = null);

internal sealed record FuzzyReferenceFileSummary(
    string Path,
    int ReferenceCount,
    int FirstLine,
    IReadOnlyList<string> Reasons,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<ReferenceUsageCount>? UsageKindCounts = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FuzzySnippet? Snippet = null);

internal sealed record FuzzyCandidateReferenceSummary(
    FuzzySymbolCandidate Candidate,
    int TotalMatches,
    IReadOnlyList<FuzzyReferenceFileSummary> Files);

internal sealed record FuzzyMemberSummary(
    int TotalMembers,
    int Limit,
    bool Truncated,
    IReadOnlyList<FuzzyMemberEntry> Members);

internal sealed record FuzzySelectionInput(
    string Mode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CandidateId);

internal sealed record FuzzySelectionExplanation(
    string Policy,
    string MinConfidence,
    bool Selected,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? SelectedCandidateId,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<string> RankInputs,
    IReadOnlyList<string> AmbiguityReasons);

internal sealed record FuzzyMemberEntry(
    string Name,
    string Kind,
    string? Container,
    SymbolFacts Facts,
    string Path,
    int Line,
    int Column,
    int EndLine,
    int EndColumn);

internal sealed record FuzzyRelationSummary(
    IReadOnlyList<FuzzySymbolLocation> Callers,
    IReadOnlyList<FuzzySymbolLocation> Calls,
    IReadOnlyList<FuzzySymbolLocation> Implementations,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FuzzyHierarchySummary? Hierarchy);

internal sealed record FuzzyHierarchySummary(
    IReadOnlyList<FuzzySymbolLocation> BaseTypes,
    IReadOnlyList<FuzzySymbolLocation> Interfaces,
    IReadOnlyList<FuzzySymbolLocation> DerivedTypes,
    IReadOnlyList<FuzzySymbolLocation> ImplementingTypes,
    IReadOnlyList<FuzzySymbolLocation> BaseMembers,
    IReadOnlyList<FuzzySymbolLocation> OverridingMembers,
    IReadOnlyList<FuzzySymbolLocation> ImplementedMembers);

internal sealed record FuzzyRelatedFile(
    string Path,
    IReadOnlyList<string> Reasons,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ImpactLevel,
    IReadOnlyList<FuzzySourceLocation> Locations);

internal sealed record FuzzyEntrypointChain(
    IReadOnlyList<FuzzySymbolLocation> Symbols,
    string EndReason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FrameworkEntrypointItem? Entrypoint = null);

internal sealed record FuzzyNextAction(
    string Command,
    string Workspace,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Query,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? File,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? Line,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? Column,
    string Reason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CandidateId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? McpTool = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyDictionary<string, object?>? Arguments = null);

internal sealed record FuzzySnippet(int StartLine, int EndLine, IReadOnlyList<string> Lines);

internal static class FuzzySnippetReader
{
    public static FuzzySnippet? TryRead(string path, int line, int contextLines)
    {
        try
        {
            string[] lines = File.ReadAllLines(path);
            if (line < 1 || line > lines.Length)
            {
                return null;
            }

            int safeContext = Math.Max(0, contextLines);
            int startLine = Math.Max(1, line - safeContext);
            int endLine = Math.Min(lines.Length, line + safeContext);
            return new FuzzySnippet(
                StartLine: startLine,
                EndLine: endLine,
                Lines: lines.Skip(startLine - 1).Take(endLine - startLine + 1).ToArray());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}

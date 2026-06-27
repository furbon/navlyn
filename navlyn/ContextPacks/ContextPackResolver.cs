using Microsoft.CodeAnalysis;
using Navlyn.Diagnostics;
using Navlyn.Diffs;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.ContextPacks;

internal sealed class ContextPackResolver
{
    public async Task<ContextPackResult> ResolveQueryAsync(
        LoadedWorkspace workspace,
        FuzzyQueryOptions queryOptions,
        IReadOnlyList<Project> projects,
        IReadOnlyList<FuzzyProjectFilter>? projectFilters,
        bool excludeGenerated,
        ContextPackOptions options,
        CancellationToken cancellationToken)
    {
        FuzzyDiscoveryResolver fuzzyResolver = new();
        FuzzyAboutResult about = await fuzzyResolver.AboutAsync(
            workspace,
            queryOptions,
            new FuzzyAboutOptions(
                options.MemberLimit,
                options.ReferenceLimit,
                options.RelationLimit,
                IncludeSnippets: false,
                SnippetLines: 0,
                excludeGenerated),
            projects,
            projectFilters,
            cancellationToken);

        ContextPackQuery query = new(
            Text: queryOptions.Query,
            Match: queryOptions.Match,
            CaseSensitive: queryOptions.CaseSensitive,
            Assumptions: about.Assumptions);

        ContextPackSelection selection = new(
            Confidence: about.Confidence,
            CandidateCount: about.CandidateCount,
            TotalCandidates: about.TotalCandidates,
            Candidates: about.Candidates,
            SelectedCandidate: about.SelectedCandidate,
            Alternatives: about.Alternatives,
            SelectionInput: about.SelectionInput,
            SelectionExplanation: about.SelectionExplanation);

        if (about.SelectedCandidate is null)
        {
            ContextPackBudgetResult emptyBudget = new ContextPackBudgeter().Apply(
                [],
                options.BudgetTokens,
                options.ItemLimit,
                CreateQueryOmittedNextAction(workspace.DisplayPath, queryOptions.Query));

            return CreateResult(
                workspace,
                mode: "query",
                options,
                projectFilters,
                excludeGenerated,
                query,
                selection,
                diff: null,
                new ContextPack(
                    Root: null,
                    Sections: new EmptyContextSections(),
                    Items: [],
                    Omitted: []),
                emptyBudget.Budget,
                truncated: false,
                warnings: about.Warnings,
                nextActions: CreateAmbiguousQueryNextActions(workspace.DisplayPath, queryOptions.Query));
        }

        FuzzyFilesResult related = await fuzzyResolver.FilesAsync(
            workspace,
            "related",
            queryOptions,
            new FuzzyFilesOptions(
                Include: ["declarations", "references", "callers", "calls", "implementations", "hierarchy"],
                Limit: options.FileLimit,
                Depth: options.Depth,
                IncludeSnippets: false,
                SnippetLines: 0,
                excludeGenerated),
            projects,
            projectFilters,
            cancellationToken);

        ContextPackDiagnosticsSection diagnostics = await ResolveQueryDiagnosticsAsync(
            projects,
            about,
            related,
            excludeGenerated,
            options.QueryDiagnosticLimit,
            cancellationToken);

        QueryContextSections sections = new(
            Definition: about.Definition,
            MemberOutline: about.Members is null
                ? null
                : new ContextMemberOutlineSection(
                    about.Members.TotalMembers,
                    about.Members.Limit,
                    about.Members.TotalMembers > about.Members.Members.Count,
                    about.Members.Members),
            References: about.References is null
                ? null
                : new ContextReferenceSection(
                    about.References.TotalMatches,
                    options.ReferenceLimit,
                    about.References.TotalMatches > about.References.References.Count,
                    about.References.References,
                    about.References.Files),
            RelatedFiles: related.Files is null
                ? null
                : new ContextRelatedFilesSection(
                    TotalFiles: related.TotalFiles ?? related.Files.Count,
                    Limit: options.FileLimit,
                    Truncated: (related.TotalFiles ?? related.Files.Count) > related.Files.Count,
                    Files: related.Files),
            CallRelations: about.Relations,
            Diagnostics: diagnostics);

        IReadOnlyList<ContextPackItem> rankedItems = RankAndDeduplicate(
            CreateQueryItems(about, related, diagnostics, options),
            options.Goal);
        ContextPackBudgetResult budget = new ContextPackBudgeter().Apply(
            rankedItems,
            options.BudgetTokens,
            options.ItemLimit,
            CreateQueryOmittedNextAction(workspace.DisplayPath, about.SelectedCandidate.Name));

        ContextPack pack = new(
            Root: new ContextPackRoot("symbol", about.SelectedCandidate, TotalChangedSymbols: null, TotalChangedFiles: null),
            Sections: sections,
            Items: budget.Items,
            Omitted: budget.Omitted);

        return CreateResult(
            workspace,
            mode: "query",
            options,
            projectFilters,
            excludeGenerated,
            query,
            selection,
            diff: null,
            pack,
            budget.Budget,
            truncated: budget.Truncated ||
                sections.MemberOutline?.Truncated == true ||
                sections.References?.Truncated == true ||
                sections.RelatedFiles?.Truncated == true ||
                sections.Diagnostics.Truncated,
            warnings: about.Warnings,
            nextActions: CreateQueryNextActions(workspace.DisplayPath, about.SelectedCandidate));
    }

    public async Task<DiffWorkflowExecutionResult<ContextPackResult>> ResolveDiffAsync(
        LoadedWorkspace workspace,
        DiffRequest request,
        IReadOnlyList<Project> projects,
        IReadOnlyList<DiffProjectFilter>? projectFilters,
        bool excludeGenerated,
        ContextPackOptions options,
        CancellationToken cancellationToken)
    {
        DiffWorkflowExecutionResult<ReviewDiffResult> reviewResult =
            await new DiffWorkflowResolver().ResolveReviewAsync(
                workspace,
                request,
                projects,
                projectFilters,
                excludeGenerated,
                options.SymbolLimit,
                options.ImpactLimit,
                options.DiffDiagnosticLimit,
                options.RelatedTestLimit,
                options.Depth,
                includeSnippets: false,
                snippetLines: 0,
                cancellationToken);

        if (reviewResult.Error is not null)
        {
            return DiffWorkflowExecutionResult<ContextPackResult>.Failed(reviewResult.Error);
        }

        ReviewDiffResult review = reviewResult.Result!;
        DiffContextSections sections = new(
            review.ChangedSymbols,
            review.UnresolvedChanges,
            review.PublicContractChanges,
            review.Impact,
            review.RelatedTests,
            review.DiagnosticsScope,
            review.Diagnostics,
            review.Findings);

        IReadOnlyList<ContextPackItem> rankedItems = RankAndDeduplicate(
            CreateDiffItems(review, options),
            options.Goal);
        ContextPackBudgetResult budget = new ContextPackBudgeter().Apply(
            rankedItems,
            options.BudgetTokens,
            options.ItemLimit,
            CreateDiffOmittedNextAction(workspace.DisplayPath));

        ContextPack pack = new(
            Root: new ContextPackRoot(
                Kind: "diff",
                Symbol: null,
                TotalChangedSymbols: review.ChangedSymbols.TotalSymbols,
                TotalChangedFiles: review.Diff.TotalFiles),
            Sections: sections,
            Items: budget.Items,
            Omitted: budget.Omitted);

        ContextPackResult result = CreateResult(
            workspace,
            mode: "diff",
            options,
            projectFilters,
            excludeGenerated,
            query: null,
            selection: null,
            review.Diff,
            pack,
            budget.Budget,
            truncated: budget.Truncated ||
                review.Truncated ||
                review.ChangedSymbols.Truncated ||
                review.PublicContractChanges.Truncated ||
                review.Impact.Truncated ||
                review.RelatedTests.Truncated ||
                review.Diagnostics.Truncated,
            warnings: review.Warnings,
            nextActions: ConvertNextActions(review.NextActions));

        return DiffWorkflowExecutionResult<ContextPackResult>.Succeeded(result);
    }

    private static ContextPackResult CreateResult(
        LoadedWorkspace workspace,
        string mode,
        ContextPackOptions options,
        object? projectFilters,
        bool excludeGenerated,
        ContextPackQuery? query,
        ContextPackSelection? selection,
        DiffSet? diff,
        ContextPack pack,
        ContextPackBudget budget,
        bool truncated,
        IReadOnlyList<string> warnings,
        IReadOnlyList<ContextPackNextAction> nextActions)
    {
        return new ContextPackResult(
            Workspace: workspace.DisplayPath,
            Kind: workspace.Kind,
            Command: "context-pack",
            Mode: mode,
            Goal: options.Goal,
            Projects: projectFilters,
            ExcludeGenerated: excludeGenerated,
            Query: query,
            Selection: selection,
            Diff: diff,
            Budget: budget,
            Limits: new ContextPackLimits(
                options.ItemLimit,
                options.CandidateLimit,
                options.MemberLimit,
                options.ReferenceLimit,
                options.RelationLimit,
                options.FileLimit,
                mode == "diff" ? options.DiffDiagnosticLimit : options.QueryDiagnosticLimit,
                options.SymbolLimit,
                options.ImpactLimit,
                options.RelatedTestLimit,
                options.Depth),
            Pack: pack,
            Truncated: truncated,
            Warnings: warnings,
            NextActions: nextActions);
    }

    private static async Task<ContextPackDiagnosticsSection> ResolveQueryDiagnosticsAsync(
        IReadOnlyList<Project> projects,
        FuzzyAboutResult about,
        FuzzyFilesResult related,
        bool excludeGenerated,
        int limit,
        CancellationToken cancellationToken)
    {
        HashSet<string> scopedPaths = new(StringComparer.Ordinal);
        if (about.Definition is not null)
        {
            scopedPaths.Add(about.Definition.Path);
        }

        foreach (FuzzySourceLocation reference in about.References?.References ?? [])
        {
            scopedPaths.Add(reference.Path);
        }

        foreach (FuzzyRelatedFile file in related.Files ?? [])
        {
            scopedPaths.Add(file.Path);
        }

        WorkspaceDiagnosticsResolution diagnostics = await new WorkspaceDiagnosticsResolver().ResolveAsync(
            projects,
            excludeGenerated,
            cancellationToken);

        IReadOnlyList<ContextPackDiagnosticItem> scoped = [.. diagnostics.Diagnostics
            .Where(diagnostic => diagnostic.Path is not null && scopedPaths.Contains(diagnostic.Path))
            .Select(diagnostic => new ContextPackDiagnosticItem(
                diagnostic.Project,
                diagnostic.Severity,
                diagnostic.Id,
                diagnostic.Message,
                diagnostic.Path,
                diagnostic.Line,
                diagnostic.Column,
                diagnostic.EndLine,
                diagnostic.EndColumn,
                ["diagnostic-in-context-scope"]))];

        return new ContextPackDiagnosticsSection(
            scoped.Count,
            limit,
            scoped.Count > limit,
            [.. scoped.Take(limit)]);
    }

    private static IEnumerable<ContextPackItem> CreateQueryItems(
        FuzzyAboutResult about,
        FuzzyFilesResult related,
        ContextPackDiagnosticsSection diagnostics,
        ContextPackOptions options)
    {
        if (about.SelectedCandidate is not null && about.Definition is not null)
        {
            yield return CreateItem(
                "definition",
                "selected-symbol-definition",
                ToSourceLocation(about.Definition),
                about.SelectedCandidate,
                options,
                about.SelectedCandidate.Facts.Signature);
        }

        foreach (FuzzyMemberEntry member in about.Members?.Members ?? [])
        {
            yield return CreateItem(
                "member",
                "member-outline-entry",
                ToSourceLocation(member),
                member,
                options,
                member.Facts.Signature);
        }

        foreach (FuzzySourceLocation reference in about.References?.References ?? [])
        {
            yield return CreateItem(
                "reference",
                "references-selected-symbol",
                ToSourceLocation(reference),
                reference.ContainingSymbol,
                options);
        }

        foreach (FuzzyRelatedFile file in related.Files ?? [])
        {
            FuzzySourceLocation? first = file.Locations.FirstOrDefault();
            if (first is null)
            {
                continue;
            }

            yield return CreateItem(
                "related-file",
                file.Reasons.FirstOrDefault() ?? "related-file",
                ToSourceLocation(first),
                null,
                options);
        }

        foreach (FuzzySymbolLocation symbol in about.Relations?.Callers ?? [])
        {
            if (TrySourceLocation(symbol, out ContextPackSourceLocation? location) && location is not null)
            {
                yield return CreateItem("caller", "caller-of-selected-symbol", location, symbol, options, symbol.Facts.Signature);
            }
        }

        foreach (FuzzySymbolLocation symbol in about.Relations?.Calls ?? [])
        {
            if (TrySourceLocation(symbol, out ContextPackSourceLocation? location) && location is not null)
            {
                yield return CreateItem("call", "callee-of-selected-symbol", location, symbol, options, symbol.Facts.Signature);
            }
        }

        foreach (FuzzySymbolLocation symbol in about.Relations?.Implementations ?? [])
        {
            if (TrySourceLocation(symbol, out ContextPackSourceLocation? location) && location is not null)
            {
                yield return CreateItem("implementation", "implements-selected-symbol", location, symbol, options, symbol.Facts.Signature);
            }
        }

        foreach (ContextPackDiagnosticItem diagnostic in diagnostics.Items)
        {
            ContextPackSourceLocation? location = TrySourceLocation(diagnostic);
            yield return new ContextPackItem(
                Id: location is null
                    ? $"diagnostic:{diagnostic.Project.Name}:{diagnostic.Id}:{diagnostic.Message}"
                    : StableId("diagnostic", location, diagnostic.Id),
                Kind: "diagnostic",
                Priority: 0,
                ReasonCodes: diagnostic.ReasonCodes,
                Symbol: null,
                SourceLocation: location,
                Content: new ContextPackItemContent("diagnostic", null, null, [diagnostic.Message]),
                EstimatedTokens: 0);
        }
    }

    private static IEnumerable<ContextPackItem> CreateDiffItems(ReviewDiffResult review, ContextPackOptions options)
    {
        foreach (DiffChangedSymbol symbol in review.ChangedSymbols.Symbols)
        {
            yield return CreateItem(
                "changed-symbol",
                "changed-symbol",
                ToSourceLocation(symbol),
                symbol,
                options,
                symbol.Facts.Signature);
        }

        foreach (ReviewFinding finding in review.Findings)
        {
            DiffSourceLocation? evidence = finding.Evidence.FirstOrDefault();
            ContextPackSourceLocation? location = evidence is null ? null : ToSourceLocation(evidence);
            yield return new ContextPackItem(
                Id: location is null ? $"finding:{finding.Code}:{finding.Claim}" : StableId("finding", location, finding.Code),
                Kind: "finding",
                Priority: 0,
                ReasonCodes: finding.ReasonCodes,
                Symbol: null,
                SourceLocation: location,
                Content: new ContextPackItemContent("finding", null, null, [finding.Claim]),
                EstimatedTokens: 0);
        }

        foreach (DiffDiagnosticItem diagnostic in review.Diagnostics.Items)
        {
            ContextPackSourceLocation? location = TrySourceLocation(diagnostic);
            yield return new ContextPackItem(
                Id: location is null
                    ? $"diagnostic:{diagnostic.Project.Name}:{diagnostic.Id}:{diagnostic.Message}"
                    : StableId("diagnostic", location, diagnostic.Id),
                Kind: "diagnostic",
                Priority: 0,
                ReasonCodes: diagnostic.ReasonCodes,
                Symbol: null,
                SourceLocation: location,
                Content: new ContextPackItemContent("diagnostic", null, null, [diagnostic.Message]),
                EstimatedTokens: 0);
        }

        foreach (DiffImpactItem item in review.Impact.Items)
        {
            foreach (DiffAffectedFile file in item.AffectedFiles)
            {
                DiffSourceLocation? first = file.Locations.FirstOrDefault();
                if (first is null)
                {
                    continue;
                }

                yield return CreateItem(
                    "diff-impact",
                    file.ReasonCodes.FirstOrDefault() ?? "affected-file",
                    ToSourceLocation(first),
                    item.ChangedSymbol,
                    options);
            }

            foreach (DiffCallGroup caller in item.Callers.Items)
            {
                if (TrySourceLocation(caller.Symbol, out ContextPackSourceLocation? location) && location is not null)
                {
                    yield return CreateItem("caller", "caller-of-changed-symbol", location, caller.Symbol, options, caller.Symbol.Facts.Signature);
                }
            }
        }

        foreach (RelatedTestCandidate test in review.RelatedTests.Candidates)
        {
            yield return new ContextPackItem(
                Id: $"related-test:{test.Path}",
                Kind: "related-test",
                Priority: 0,
                ReasonCodes: test.ReasonCodes,
                Symbol: null,
                SourceLocation: null,
                Content: new ContextPackItemContent("path", null, null, [test.Path]),
                EstimatedTokens: 0);
        }
    }

    private static IReadOnlyList<ContextPackItem> RankAndDeduplicate(
        IEnumerable<ContextPackItem> items,
        string goal)
    {
        return [.. items
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .Select(group => group.First() with { Priority = Priority(group.First().Kind, goal) })
            .OrderBy(item => item.Priority)
            .ThenBy(item => item.SourceLocation?.Path, StringComparer.Ordinal)
            .ThenBy(item => item.SourceLocation?.Line)
            .ThenBy(item => item.SourceLocation?.Column)
            .ThenBy(item => item.SourceLocation?.EndLine)
            .ThenBy(item => item.SourceLocation?.EndColumn)
            .ThenBy(item => item.Kind, StringComparer.Ordinal)
            .ThenBy(item => item.Id, StringComparer.Ordinal)];
    }

    private static int Priority(string kind, string goal)
    {
        return goal switch
        {
            "review" => kind switch
            {
                "changed-symbol" => 0,
                "finding" => 1,
                "diagnostic" => 2,
                "diff-impact" => 3,
                "caller" => 4,
                "related-test" => 5,
                _ => 20
            },
            "modify" => kind switch
            {
                "definition" => 0,
                "member" => 1,
                "caller" => 2,
                "call" => 3,
                "implementation" => 4,
                "reference" => 5,
                "diagnostic" => 6,
                "related-file" => 7,
                _ => 20
            },
            _ => kind switch
            {
                "definition" => 0,
                "member" => 1,
                "reference" => 2,
                "caller" => 3,
                "call" => 4,
                "implementation" => 5,
                "related-file" => 6,
                "diagnostic" => 7,
                _ => 20
            }
        };
    }

    private static ContextPackItem CreateItem(
        string kind,
        string reason,
        ContextPackSourceLocation location,
        object? symbol,
        ContextPackOptions options,
        string? signature = null)
    {
        ContextPackItemContent? content = CreateContent(location, options, signature);
        return new ContextPackItem(
            Id: StableId(kind, location, SymbolName(symbol)),
            Kind: kind,
            Priority: 0,
            ReasonCodes: [reason],
            Symbol: symbol,
            SourceLocation: location,
            Content: content,
            EstimatedTokens: 0);
    }

    private static ContextPackItemContent? CreateContent(
        ContextPackSourceLocation location,
        ContextPackOptions options,
        string? signature)
    {
        return options.SnippetPolicy switch
        {
            "none" => null,
            "signature" => string.IsNullOrWhiteSpace(signature)
                ? null
                : new ContextPackItemContent("signature", null, null, [signature]),
            "block" => TryReadLines(location.Path, location.Line, options.SnippetLines),
            _ => TryReadLines(location.Path, location.Line, contextLines: 0)
        };
    }

    private static ContextPackItemContent? TryReadLines(string path, int line, int contextLines)
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
            return new ContextPackItemContent(
                contextLines == 0 ? "line" : "block",
                startLine,
                endLine,
                [.. lines.Skip(startLine - 1).Take(endLine - startLine + 1)]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static ContextPackSourceLocation ToSourceLocation(FuzzySourceLocation location)
    {
        return new ContextPackSourceLocation(location.Path, location.Line, location.Column, location.EndLine, location.EndColumn);
    }

    private static ContextPackSourceLocation ToSourceLocation(FuzzyMemberEntry member)
    {
        return new ContextPackSourceLocation(member.Path, member.Line, member.Column, member.EndLine, member.EndColumn);
    }

    private static ContextPackSourceLocation ToSourceLocation(DiffChangedSymbol symbol)
    {
        return new ContextPackSourceLocation(symbol.Path, symbol.Line, symbol.Column, symbol.EndLine, symbol.EndColumn);
    }

    private static ContextPackSourceLocation ToSourceLocation(DiffSourceLocation location)
    {
        return new ContextPackSourceLocation(location.Path, location.Line, location.Column, location.EndLine, location.EndColumn);
    }

    private static bool TrySourceLocation(FuzzySymbolLocation symbol, out ContextPackSourceLocation? location)
    {
        if (symbol.Path is not null && symbol.Line is not null && symbol.Column is not null && symbol.EndLine is not null && symbol.EndColumn is not null)
        {
            location = new ContextPackSourceLocation(symbol.Path, symbol.Line.Value, symbol.Column.Value, symbol.EndLine.Value, symbol.EndColumn.Value);
            return true;
        }

        location = null;
        return false;
    }

    private static bool TrySourceLocation(DiffSymbolLocation symbol, out ContextPackSourceLocation? location)
    {
        if (symbol.Path is not null && symbol.Line is not null && symbol.Column is not null && symbol.EndLine is not null && symbol.EndColumn is not null)
        {
            location = new ContextPackSourceLocation(symbol.Path, symbol.Line.Value, symbol.Column.Value, symbol.EndLine.Value, symbol.EndColumn.Value);
            return true;
        }

        location = null;
        return false;
    }

    private static ContextPackSourceLocation? TrySourceLocation(ContextPackDiagnosticItem diagnostic)
    {
        return diagnostic.Path is null || diagnostic.Line is null || diagnostic.Column is null || diagnostic.EndLine is null || diagnostic.EndColumn is null
            ? null
            : new ContextPackSourceLocation(diagnostic.Path, diagnostic.Line.Value, diagnostic.Column.Value, diagnostic.EndLine.Value, diagnostic.EndColumn.Value);
    }

    private static ContextPackSourceLocation? TrySourceLocation(DiffDiagnosticItem diagnostic)
    {
        return diagnostic.Path is null || diagnostic.Line is null || diagnostic.Column is null || diagnostic.EndLine is null || diagnostic.EndColumn is null
            ? null
            : new ContextPackSourceLocation(diagnostic.Path, diagnostic.Line.Value, diagnostic.Column.Value, diagnostic.EndLine.Value, diagnostic.EndColumn.Value);
    }

    private static string StableId(string kind, ContextPackSourceLocation location, string? suffix)
    {
        return $"{kind}:{location.Path}:{location.Line}:{location.Column}:{location.EndLine}:{location.EndColumn}:{suffix}";
    }

    private static string? SymbolName(object? symbol)
    {
        return symbol switch
        {
            FuzzySymbolCandidate candidate => candidate.Name,
            FuzzySymbolLocation location => location.Name,
            FuzzyMemberEntry member => member.Name,
            DiffChangedSymbol changed => changed.Name,
            DiffSymbolLocation diffSymbol => diffSymbol.Name,
            _ => null
        };
    }

    private static IReadOnlyList<ContextPackNextAction> CreateQueryNextActions(
        string workspace,
        FuzzySymbolCandidate selected)
    {
        return
        [
            new ContextPackNextAction("about", workspace, selected.Name, null, null, null, "Inspect full semantic summary for the selected symbol."),
            new ContextPackNextAction("impact", workspace, selected.Name, null, null, null, "Inspect static impact for the selected symbol."),
            new ContextPackNextAction("where-used", workspace, selected.Name, null, null, null, "Inspect references omitted from the context pack.")
        ];
    }

    private static IReadOnlyList<ContextPackNextAction> CreateAmbiguousQueryNextActions(
        string workspace,
        string query)
    {
        return
        [
            new ContextPackNextAction("find", workspace, query, null, null, null, "Resolve ambiguity before requesting a context pack.")
        ];
    }

    private static ContextPackNextAction CreateQueryOmittedNextAction(string workspace, string query)
    {
        return new ContextPackNextAction("about", workspace, query, null, null, null, "Inspect context material omitted from the context pack budget.");
    }

    private static ContextPackNextAction CreateDiffOmittedNextAction(string workspace)
    {
        return new ContextPackNextAction("review-diff", workspace, Query: null, File: null, Line: null, Column: null, "Inspect review facts omitted from the context pack budget.");
    }

    private static IReadOnlyList<ContextPackNextAction> ConvertNextActions(IReadOnlyList<DiffNextAction> actions)
    {
        return [.. actions.Select(action => new ContextPackNextAction(
            action.Command,
            action.Workspace,
            action.Query,
            action.File,
            action.Line,
            action.Column,
            action.Reason))];
    }
}

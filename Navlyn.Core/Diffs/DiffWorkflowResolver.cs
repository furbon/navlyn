using Navlyn.Diagnostics;
using Navlyn.Paths;
using Navlyn.Symbols;
using Navlyn.Testing;
using Navlyn.Workspaces;
using Microsoft.CodeAnalysis;
using Navlyn.Languages;

namespace Navlyn.Diffs;

internal sealed class DiffWorkflowResolver(IDiffProvider? diffProvider = null)
{
    private readonly IDiffProvider diffProvider = diffProvider ?? new GitDiffProvider();

    public async Task<DiffWorkflowExecutionResult<ChangedSymbolsResult>> ResolveChangedSymbolsAsync(
        LoadedWorkspace workspace,
        DiffRequest request,
        IReadOnlyList<Project> projects,
        IReadOnlyList<DiffProjectFilter>? projectFilters,
        bool excludeGenerated,
        int symbolLimit,
        CancellationToken cancellationToken)
    {
        DiffReadResult diffResult = await ReadDiffAsync(workspace, request, cancellationToken);
        if (diffResult.Error is not null)
        {
            return DiffWorkflowExecutionResult<ChangedSymbolsResult>.Failed(diffResult.Error);
        }

        ChangedSymbolsResolution changedSymbols = await new ChangedSymbolResolver().ResolveAsync(
            workspace,
            diffResult.Diff!,
            projects,
            excludeGenerated,
            symbolLimit,
            cancellationToken);

        ChangedSymbolsResult result = new(
            Workspace: workspace.DisplayPath,
            Kind: workspace.Kind,
            Command: "changed-symbols",
            Diff: diffResult.Diff!,
            Projects: projectFilters,
            ExcludeGenerated: excludeGenerated,
            Limits: new DiffWorkflowLimits(symbolLimit, ImpactLimit: null, DiagnosticLimit: null, RelatedTestLimit: null, Depth: null),
            Truncated: changedSymbols.ChangedSymbols.Truncated,
            Warnings: [],
            NextActions: CreateBaseNextActions(workspace.DisplayPath, changedSymbols.ChangedSymbols.Symbols),
            ChangedSymbols: changedSymbols.ChangedSymbols,
            UnresolvedChanges: changedSymbols.UnresolvedChanges);

        return DiffWorkflowExecutionResult<ChangedSymbolsResult>.Succeeded(result);
    }

    public async Task<DiffWorkflowExecutionResult<ImpactDiffResult>> ResolveImpactAsync(
        LoadedWorkspace workspace,
        DiffRequest request,
        IReadOnlyList<Project> projects,
        IReadOnlyList<DiffProjectFilter>? projectFilters,
        bool excludeGenerated,
        int symbolLimit,
        int impactLimit,
        int depth,
        IReadOnlyList<string> includeModes,
        bool includeSnippets,
        int snippetLines,
        CancellationToken cancellationToken)
    {
        DiffReadResult diffResult = await ReadDiffAsync(workspace, request, cancellationToken);
        if (diffResult.Error is not null)
        {
            return DiffWorkflowExecutionResult<ImpactDiffResult>.Failed(diffResult.Error);
        }

        ChangedSymbolsResolution changedSymbols = await new ChangedSymbolResolver().ResolveAsync(
            workspace,
            diffResult.Diff!,
            projects,
            excludeGenerated,
            symbolLimit,
            cancellationToken);

        DiffImpactSection impact = await ResolveImpactSectionAsync(
            workspace,
            projects,
            changedSymbols.ChangedSymbols.Symbols,
            impactLimit,
            depth,
            includeModes,
            excludeGenerated,
            includeSnippets,
            snippetLines,
            cancellationToken);

        ImpactDiffResult result = new(
            Workspace: workspace.DisplayPath,
            Kind: workspace.Kind,
            Command: "impact-diff",
            Diff: diffResult.Diff!,
            Projects: projectFilters,
            ExcludeGenerated: excludeGenerated,
            Limits: new DiffWorkflowLimits(symbolLimit, impactLimit, DiagnosticLimit: null, RelatedTestLimit: null, depth),
            Truncated: changedSymbols.ChangedSymbols.Truncated || impact.Truncated,
            Warnings: [],
            NextActions: CreateBaseNextActions(workspace.DisplayPath, changedSymbols.ChangedSymbols.Symbols),
            ChangedSymbols: changedSymbols.ChangedSymbols,
            UnresolvedChanges: changedSymbols.UnresolvedChanges,
            Impact: impact);

        return DiffWorkflowExecutionResult<ImpactDiffResult>.Succeeded(result);
    }

    public async Task<DiffWorkflowExecutionResult<DiagnosticsDiffResult>> ResolveDiagnosticsAsync(
        LoadedWorkspace workspace,
        DiffRequest request,
        IReadOnlyList<Project> projects,
        IReadOnlyList<DiffProjectFilter>? projectFilters,
        IReadOnlyList<string> severities,
        IReadOnlyList<string> ids,
        bool excludeGenerated,
        int symbolLimit,
        int impactLimit,
        int diagnosticLimit,
        CancellationToken cancellationToken)
    {
        DiffReadResult diffResult = await ReadDiffAsync(workspace, request, cancellationToken);
        if (diffResult.Error is not null)
        {
            return DiffWorkflowExecutionResult<DiagnosticsDiffResult>.Failed(diffResult.Error);
        }

        ChangedSymbolsResolution changedSymbols = await new ChangedSymbolResolver().ResolveAsync(
            workspace,
            diffResult.Diff!,
            projects,
            excludeGenerated,
            symbolLimit,
            cancellationToken);

        DiffImpactSection impact = await ResolveImpactSectionAsync(
            workspace,
            projects,
            changedSymbols.ChangedSymbols.Symbols,
            impactLimit,
            depth: 1,
            ["references", "callers", "calls", "implementations"],
            excludeGenerated,
            includeSnippets: false,
            snippetLines: 0,
            cancellationToken);

        DiagnosticsScopeSection scope = CreateDiagnosticsScope(diffResult.Diff!, impact);
        DiffDiagnosticsSection diagnostics = await ResolveDiagnosticsSectionAsync(
            projects,
            scope,
            severities,
            ids,
            excludeGenerated,
            diagnosticLimit,
            cancellationToken);

        DiagnosticsDiffResult result = new(
            Workspace: workspace.DisplayPath,
            Kind: workspace.Kind,
            Command: "diagnostics-diff",
            Diff: diffResult.Diff!,
            Projects: projectFilters,
            Severities: severities.Count == 0 ? null : severities,
            Ids: ids.Count == 0 ? null : ids,
            ExcludeGenerated: excludeGenerated,
            Limits: new DiffWorkflowLimits(symbolLimit, impactLimit, diagnosticLimit, RelatedTestLimit: null, Depth: 1),
            Truncated: changedSymbols.ChangedSymbols.Truncated || impact.Truncated || diagnostics.Truncated,
            Warnings: ["Diagnostics are current scoped diagnostics, not before/after diagnostic deltas."],
            NextActions: CreateBaseNextActions(workspace.DisplayPath, changedSymbols.ChangedSymbols.Symbols),
            ChangedSymbols: changedSymbols.ChangedSymbols,
            UnresolvedChanges: changedSymbols.UnresolvedChanges,
            DiagnosticsScope: scope,
            Diagnostics: diagnostics);

        return DiffWorkflowExecutionResult<DiagnosticsDiffResult>.Succeeded(result);
    }

    public async Task<DiffWorkflowExecutionResult<ReviewDiffResult>> ResolveReviewAsync(
        LoadedWorkspace workspace,
        DiffRequest request,
        IReadOnlyList<Project> projects,
        IReadOnlyList<DiffProjectFilter>? projectFilters,
        bool excludeGenerated,
        int symbolLimit,
        int impactLimit,
        int diagnosticLimit,
        int relatedTestLimit,
        int depth,
        bool includeSnippets,
        int snippetLines,
        CancellationToken cancellationToken)
    {
        DiffReadResult diffResult = await ReadDiffAsync(workspace, request, cancellationToken);
        if (diffResult.Error is not null)
        {
            return DiffWorkflowExecutionResult<ReviewDiffResult>.Failed(diffResult.Error);
        }

        ChangedSymbolsResolution changedSymbols = await new ChangedSymbolResolver().ResolveAsync(
            workspace,
            diffResult.Diff!,
            projects,
            excludeGenerated,
            symbolLimit,
            cancellationToken);

        DiffImpactSection impact = await ResolveImpactSectionAsync(
            workspace,
            projects,
            changedSymbols.ChangedSymbols.Symbols,
            impactLimit,
            depth,
            ["references", "callers", "calls", "implementations"],
            excludeGenerated,
            includeSnippets,
            snippetLines,
            cancellationToken);

        DiagnosticsScopeSection scope = CreateDiagnosticsScope(diffResult.Diff!, impact);
        DiffDiagnosticsSection diagnostics = await ResolveDiagnosticsSectionAsync(
            projects,
            scope,
            severities: [],
            ids: [],
            excludeGenerated,
            diagnosticLimit,
            cancellationToken);

        PublicContractChangesSection publicChanges = CreatePublicContractChanges(changedSymbols.ChangedSymbols.Symbols, symbolLimit);
        RelatedTestsSection relatedTests = await ResolveRelatedTestsSectionAsync(
            workspace,
            projects,
            changedSymbols.ChangedSymbols.Symbols,
            relatedTestLimit,
            excludeGenerated,
            includeSnippets,
            snippetLines,
            cancellationToken);
        IReadOnlyList<ReviewFinding> findings = CreateFindings(publicChanges, impact, diagnostics);

        ReviewDiffResult result = new(
            Workspace: workspace.DisplayPath,
            Kind: workspace.Kind,
            Command: "review-diff",
            Diff: diffResult.Diff!,
            Projects: projectFilters,
            ExcludeGenerated: excludeGenerated,
            Limits: new DiffWorkflowLimits(symbolLimit, impactLimit, diagnosticLimit, relatedTestLimit, depth),
            Truncated: changedSymbols.ChangedSymbols.Truncated ||
                impact.Truncated ||
                diagnostics.Truncated ||
                publicChanges.Truncated ||
                relatedTests.Truncated,
            Warnings:
            [
                "Public contract changes are current-workspace heuristics; this review-diff pack does not include before/after public API diff.",
                "Diagnostics are current scoped diagnostics, not before/after diagnostic deltas."
            ],
            NextActions: CreateBaseNextActions(workspace.DisplayPath, changedSymbols.ChangedSymbols.Symbols),
            ChangedSymbols: changedSymbols.ChangedSymbols,
            UnresolvedChanges: changedSymbols.UnresolvedChanges,
            PublicContractChanges: publicChanges,
            Impact: impact,
            RelatedTests: relatedTests,
            DiagnosticsScope: scope,
            Diagnostics: diagnostics,
            Findings: findings);

        return DiffWorkflowExecutionResult<ReviewDiffResult>.Succeeded(result);
    }

    private async Task<DiffReadResult> ReadDiffAsync(
        LoadedWorkspace workspace,
        DiffRequest request,
        CancellationToken cancellationToken)
    {
        string? anchor = workspace.Solution.FilePath ??
            workspace.Solution.Projects.Select(project => project.FilePath).FirstOrDefault(path => path is not null);
        string? repositoryRoot = PathDisplay.FindRepositoryRoot(anchor ?? Directory.GetCurrentDirectory());
        if (repositoryRoot is null)
        {
            return DiffReadResult.Failed(
                DiagnosticIds.GitRepositoryNotFound,
                "Git repository root was not found for diff workflow.",
                ExitCodes.UsageError);
        }

        return await diffProvider.ReadAsync(repositoryRoot, request, cancellationToken);
    }

    private static async Task<DiffImpactSection> ResolveImpactSectionAsync(
        LoadedWorkspace workspace,
        IReadOnlyList<Project> projects,
        IReadOnlyList<DiffChangedSymbol> changedSymbols,
        int limit,
        int depth,
        IReadOnlyList<string> includeModes,
        bool excludeGenerated,
        bool includeSnippets,
        int snippetLines,
        CancellationToken cancellationToken)
    {
        List<DiffImpactItem> items = [];
        foreach (DiffChangedSymbol symbol in changedSymbols)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Project? project = FindProject(projects, symbol.Facts.Project);
            LimitedList<DiffSourceLocation> references = includeModes.Contains("references", StringComparer.Ordinal)
                ? await ResolveReferencesAsync(workspace, symbol, project, excludeGenerated, includeSnippets, snippetLines, limit, cancellationToken)
                : EmptyLimited<DiffSourceLocation>(limit);
            LimitedList<DiffCallGroup> callers = includeModes.Contains("callers", StringComparer.Ordinal)
                ? await ResolveCallersAsync(workspace, symbol, project, excludeGenerated, includeSnippets, snippetLines, limit, cancellationToken)
                : EmptyLimited<DiffCallGroup>(limit);
            LimitedList<DiffCallGroup> calls = includeModes.Contains("calls", StringComparer.Ordinal)
                ? await ResolveCallsAsync(workspace, symbol, project, excludeGenerated, includeSnippets, snippetLines, limit, cancellationToken)
                : EmptyLimited<DiffCallGroup>(limit);
            LimitedList<DiffSymbolLocation> implementations = includeModes.Contains("implementations", StringComparer.Ordinal)
                ? await ResolveImplementationsAsync(workspace, symbol, project, excludeGenerated, includeSnippets, snippetLines, limit, cancellationToken)
                : EmptyLimited<DiffSymbolLocation>(limit);

            LimitedList<DiffEntrypointChain> entrypoints = includeModes.Contains("entrypoints", StringComparer.Ordinal)
                ? await ResolveEntrypointChainsAsync(workspace, projects, symbol, depth, limit, excludeGenerated, includeSnippets, snippetLines, cancellationToken)
                : EmptyLimited<DiffEntrypointChain>(limit);

            IReadOnlyList<DiffAffectedFile> affectedFiles = CreateAffectedFiles(symbol, references, callers, calls, implementations);
            IReadOnlyList<DiffRiskReason> riskReasons = CreateRiskReasons(symbol, callers, diagnostics: null);
            items.Add(new DiffImpactItem(symbol, references, callers, calls, implementations, entrypoints, affectedFiles, riskReasons));
        }

        IReadOnlyList<DiffImpactItem> ordered = [.. items
            .OrderBy(item => item.ChangedSymbol.Path, StringComparer.Ordinal)
            .ThenBy(item => item.ChangedSymbol.Line)
            .ThenBy(item => item.ChangedSymbol.Column)
            .ThenBy(item => item.ChangedSymbol.Name, StringComparer.Ordinal)];

        return new DiffImpactSection(
            TotalItems: ordered.Count,
            Limit: limit,
            Truncated: ordered.Count > limit,
            Items: [.. ordered.Take(limit)]);
    }

    private static async Task<LimitedList<DiffSourceLocation>> ResolveReferencesAsync(
        LoadedWorkspace workspace,
        DiffChangedSymbol symbol,
        Project? project,
        bool excludeGenerated,
        bool includeSnippets,
        int snippetLines,
        int limit,
        CancellationToken cancellationToken)
    {
        ReferencesResolutionResult result = await new ReferencesResolver().ResolveAsync(
            workspace.Solution,
            new FileInfo(symbol.Path),
            symbol.Line,
            symbol.Column,
            project,
            excludeGenerated,
            cancellationToken);
        if (result.Error is not null)
        {
            return EmptyLimited<DiffSourceLocation>(limit);
        }

        IReadOnlyList<DiffSourceLocation> locations = [.. result.Resolution!.References
            .Select(reference => new DiffSourceLocation(
                reference.Path,
                reference.Line,
                reference.Column,
                reference.EndLine,
                reference.EndColumn,
                reference.ContainingSymbol is null ? null : new DiffSymbolLocation(
                    reference.ContainingSymbol.Name,
                    reference.ContainingSymbol.Kind,
                    reference.ContainingSymbol.Container,
                    reference.ContainingSymbol.Facts,
                    reference.ContainingSymbol.Path,
                    reference.ContainingSymbol.Line,
                    reference.ContainingSymbol.Column,
                    reference.ContainingSymbol.EndLine,
                    reference.ContainingSymbol.EndColumn),
                includeSnippets ? TryReadSnippet(reference.Path, reference.Line, snippetLines) : null))];

        return Limited(locations, limit);
    }

    private static async Task<LimitedList<DiffCallGroup>> ResolveCallersAsync(
        LoadedWorkspace workspace,
        DiffChangedSymbol symbol,
        Project? project,
        bool excludeGenerated,
        bool includeSnippets,
        int snippetLines,
        int limit,
        CancellationToken cancellationToken)
    {
        CallersResolutionResult result = await new CallHierarchyResolver().ResolveCallersAsync(
            workspace.Solution,
            new FileInfo(symbol.Path),
            symbol.Line,
            symbol.Column,
            project,
            excludeGenerated,
            cancellationToken);
        if (result.Error is not null)
        {
            return EmptyLimited<DiffCallGroup>(limit);
        }

        return Limited([.. result.Resolution!.Callers.Select(group => ToCallGroup(group, includeSnippets, snippetLines))], limit);
    }

    private static async Task<LimitedList<DiffCallGroup>> ResolveCallsAsync(
        LoadedWorkspace workspace,
        DiffChangedSymbol symbol,
        Project? project,
        bool excludeGenerated,
        bool includeSnippets,
        int snippetLines,
        int limit,
        CancellationToken cancellationToken)
    {
        CallsResolutionResult result = await new CallHierarchyResolver().ResolveCallsAsync(
            workspace.Solution,
            new FileInfo(symbol.Path),
            symbol.Line,
            symbol.Column,
            project,
            excludeGenerated,
            includeMetadata: false,
            cancellationToken);
        if (result.Error is not null)
        {
            return EmptyLimited<DiffCallGroup>(limit);
        }

        return Limited([.. result.Resolution!.Calls.Select(group => ToCallGroup(group, includeSnippets, snippetLines))], limit);
    }

    private static async Task<LimitedList<DiffSymbolLocation>> ResolveImplementationsAsync(
        LoadedWorkspace workspace,
        DiffChangedSymbol symbol,
        Project? project,
        bool excludeGenerated,
        bool includeSnippets,
        int snippetLines,
        int limit,
        CancellationToken cancellationToken)
    {
        ImplementationsResolutionResult result = await new ImplementationsResolver().ResolveAsync(
            workspace.Solution,
            new FileInfo(symbol.Path),
            symbol.Line,
            symbol.Column,
            project,
            excludeGenerated,
            cancellationToken);
        if (result.Error is not null)
        {
            return EmptyLimited<DiffSymbolLocation>(limit);
        }

        return Limited([.. result.Resolution!.Implementations.Select(implementation => new DiffSymbolLocation(
            implementation.Name,
            implementation.Kind,
            implementation.Container,
            implementation.Facts,
            implementation.Path,
            implementation.Line,
            implementation.Column,
            implementation.EndLine,
            implementation.EndColumn,
            includeSnippets ? TryReadSnippet(implementation.Path, implementation.Line, snippetLines) : null))], limit);
    }

    private static async Task<LimitedList<DiffEntrypointChain>> ResolveEntrypointChainsAsync(
        LoadedWorkspace workspace,
        IReadOnlyList<Project> projects,
        DiffChangedSymbol symbol,
        int depth,
        int limit,
        bool excludeGenerated,
        bool includeSnippets,
        int snippetLines,
        CancellationToken cancellationToken)
    {
        if (symbol.Kind is not ("Method" or "Property" or "Event"))
        {
            return EmptyLimited<DiffEntrypointChain>(limit);
        }

        List<DiffEntrypointChain> chains = [];
        DiffSymbolLocation start = ToSymbolLocation(symbol, includeSnippets, snippetLines);
        await TraverseCallersAsync(
            workspace,
            projects,
            start,
            [start],
            depth,
            limit,
            excludeGenerated,
            includeSnippets,
            snippetLines,
            chains,
            new HashSet<string>(StringComparer.Ordinal),
            cancellationToken);

        IReadOnlyList<DiffEntrypointChain> ordered = [.. chains
            .OrderBy(chain => chain.Symbols.Count)
            .ThenBy(chain => chain.Symbols.LastOrDefault()?.Path, StringComparer.Ordinal)
            .ThenBy(chain => chain.Symbols.LastOrDefault()?.Line)
            .ThenBy(chain => chain.Symbols.LastOrDefault()?.Column)
            .ThenBy(chain => chain.EndReason, StringComparer.Ordinal)];

        return Limited(ordered, limit);
    }

    private static async Task TraverseCallersAsync(
        LoadedWorkspace workspace,
        IReadOnlyList<Project> projects,
        DiffSymbolLocation symbol,
        IReadOnlyList<DiffSymbolLocation> chain,
        int depth,
        int limit,
        bool excludeGenerated,
        bool includeSnippets,
        int snippetLines,
        List<DiffEntrypointChain> chains,
        HashSet<string> visited,
        CancellationToken cancellationToken)
    {
        if (chains.Count >= limit || symbol.Path is null || symbol.Line is null || symbol.Column is null)
        {
            return;
        }

        string key = $"{symbol.Path}:{symbol.Line}:{symbol.Column}";
        if (!visited.Add(key))
        {
            chains.Add(new DiffEntrypointChain(chain, "cycle-detected"));
            return;
        }

        if (chain.Count > depth)
        {
            chains.Add(new DiffEntrypointChain(chain, "depth-limit"));
            return;
        }

        CallersResolutionResult result = await new CallHierarchyResolver().ResolveCallersAsync(
            workspace.Solution,
            new FileInfo(symbol.Path),
            symbol.Line.Value,
            symbol.Column.Value,
            FindProject(projects, symbol.Facts.Project),
            excludeGenerated,
            cancellationToken);

        if (result.Error is not null || result.Resolution!.Callers.Count == 0)
        {
            chains.Add(new DiffEntrypointChain(chain, "no-upstream-callers"));
            return;
        }

        foreach (CallHierarchyGroup caller in result.Resolution.Callers)
        {
            DiffSymbolLocation callerSymbol = ToSymbolLocation(caller.Symbol, includeSnippets, snippetLines);
            await TraverseCallersAsync(
                workspace,
                projects,
                callerSymbol,
                [.. chain, callerSymbol],
                depth,
                limit,
                excludeGenerated,
                includeSnippets,
                snippetLines,
                chains,
                new HashSet<string>(visited, StringComparer.Ordinal),
                cancellationToken);
        }
    }

    private static DiffCallGroup ToCallGroup(CallHierarchyGroup group, bool includeSnippets, int snippetLines)
    {
        return new DiffCallGroup(
            Symbol: ToSymbolLocation(group.Symbol, includeSnippets, snippetLines),
            Locations: [.. group.Locations.Select(location => new DiffSourceLocation(
                location.Path,
                location.Line,
                location.Column,
                location.EndLine,
                location.EndColumn,
                ContainingSymbol: null,
                includeSnippets ? TryReadSnippet(location.Path, location.Line, snippetLines) : null))]);
    }

    private static DiffSymbolLocation ToSymbolLocation(
        CallHierarchySymbol symbol,
        bool includeSnippets,
        int snippetLines)
    {
        return new DiffSymbolLocation(
            symbol.Name,
            symbol.Kind,
            symbol.Container,
            symbol.Facts,
            symbol.Path,
            symbol.Line,
            symbol.Column,
            symbol.EndLine,
            symbol.EndColumn,
            includeSnippets && symbol.Path is not null && symbol.Line is not null
                ? TryReadSnippet(symbol.Path, symbol.Line.Value, snippetLines)
                : null);
    }

    private static DiffSymbolLocation ToSymbolLocation(
        DiffChangedSymbol symbol,
        bool includeSnippets,
        int snippetLines)
    {
        return new DiffSymbolLocation(
            symbol.Name,
            symbol.Kind,
            symbol.Container,
            symbol.Facts,
            symbol.Path,
            symbol.Line,
            symbol.Column,
            symbol.EndLine,
            symbol.EndColumn,
            includeSnippets ? TryReadSnippet(symbol.Path, symbol.Line, snippetLines) : null);
    }

    private static DiagnosticsScopeSection CreateDiagnosticsScope(DiffSet diff, DiffImpactSection impact)
    {
        Dictionary<string, HashSet<string>> paths = new(StringComparer.Ordinal);
        foreach (DiffFile file in diff.Files.Where(file => SourceLanguageFacts.IsSupportedSourceFile(file.Path)))
        {
            AddScope(paths, file.Path, "changed-file");
        }

        foreach (DiffAffectedFile file in impact.Items.SelectMany(item => item.AffectedFiles))
        {
            AddScope(paths, file.Path, "affected-file");
        }

        return new DiagnosticsScopeSection([.. paths
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new DiagnosticsScopePath(item.Key, [.. item.Value.OrderBy(reason => reason, StringComparer.Ordinal)]))]);
    }

    private static async Task<DiffDiagnosticsSection> ResolveDiagnosticsSectionAsync(
        IReadOnlyList<Project> projects,
        DiagnosticsScopeSection scope,
        IReadOnlyList<string> severities,
        IReadOnlyList<string> ids,
        bool excludeGenerated,
        int limit,
        CancellationToken cancellationToken)
    {
        HashSet<string> scopedPaths = new(scope.Paths.Select(path => path.Path), StringComparer.Ordinal);
        HashSet<string> scopedProjectNames = new(projects
            .Where(project => project.Documents.Any(document =>
                document.FilePath is not null &&
                scopedPaths.Contains(PathDisplay.FromCurrentDirectory(document.FilePath))))
            .Select(project => project.Name), StringComparer.Ordinal);
        WorkspaceDiagnosticsResolution resolution = await new WorkspaceDiagnosticsResolver().ResolveAsync(
            projects,
            excludeGenerated,
            cancellationToken);

        IReadOnlyList<DiffDiagnosticItem> diagnostics = [.. resolution.Diagnostics
            .Where(diagnostic =>
                (severities.Count == 0 || severities.Contains(diagnostic.Severity, StringComparer.Ordinal)) &&
                (ids.Count == 0 || ids.Contains(diagnostic.Id, StringComparer.Ordinal)) &&
                (diagnostic.Path is not null && scopedPaths.Contains(diagnostic.Path) ||
                    diagnostic.Path is null && scopedProjectNames.Contains(diagnostic.Project.Name)))
            .Select(diagnostic => new DiffDiagnosticItem(
                diagnostic.Project,
                diagnostic.Severity,
                diagnostic.Id,
                diagnostic.Message,
                diagnostic.Path,
                diagnostic.Line,
                diagnostic.Column,
                diagnostic.EndLine,
                diagnostic.EndColumn,
                diagnostic.Path is null
                    ? ["project-diagnostic-in-touched-project"]
                    : ["diagnostic-in-diff-scope"]))];

        return new DiffDiagnosticsSection(
            TotalDiagnostics: diagnostics.Count,
            Limit: limit,
            Truncated: diagnostics.Count > limit,
            Items: [.. diagnostics.Take(limit)]);
    }

    private static PublicContractChangesSection CreatePublicContractChanges(
        IReadOnlyList<DiffChangedSymbol> changedSymbols,
        int limit)
    {
        IReadOnlyList<PublicContractChange> changes = [.. changedSymbols
            .Where(symbol => symbol.Facts.Accessibility is "Public" or "Protected" or "ProtectedOrInternal")
            .Select(symbol => new PublicContractChange(
                Code: "changed-public-symbol",
                Confidence: "medium",
                Symbol: symbol,
                ReasonCodes: ["current-public-symbol-changed", "public-accessibility"]))];

        return new PublicContractChangesSection(changes.Count, limit, changes.Count > limit, [.. changes.Take(limit)]);
    }

    private static async Task<RelatedTestsSection> ResolveRelatedTestsSectionAsync(
        LoadedWorkspace workspace,
        IReadOnlyList<Project> projects,
        IReadOnlyList<DiffChangedSymbol> changedSymbols,
        int limit,
        bool excludeGenerated,
        bool includeSnippets,
        int snippetLines,
        CancellationToken cancellationToken)
    {
        TestImpactResolution resolution = await new TestImpactResolver().ResolveForChangedSymbolsAsync(
            workspace,
            projects,
            explicitTestProjects: null,
            changedSymbols,
            new TestImpactOptions(
                TestLimit: limit,
                ReferenceLimit: limit,
                IncludeSnippets: includeSnippets,
                SnippetLines: snippetLines,
                ExcludeGenerated: excludeGenerated),
            cancellationToken);

        IReadOnlyList<RelatedTestCandidate> candidates = [.. resolution.Tests.Candidates
            .Select(candidate => new RelatedTestCandidate(
                Path: candidate.Path,
                Confidence: candidate.Confidence,
                ReasonCodes: candidate.ReasonCodes,
                Evidence: [.. candidate.Evidence.Select(evidence => new DiffSourceLocation(
                    evidence.Path,
                    evidence.Line,
                    evidence.Column,
                    evidence.EndLine,
                    evidence.EndColumn,
                    ContainingSymbol: null,
                    Snippet: null))]))
            .OrderBy(candidate => candidate.Path, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Confidence, StringComparer.Ordinal)];

        return new RelatedTestsSection(
            TotalCandidates: resolution.Tests.TotalCandidates,
            Limit: limit,
            Truncated: resolution.Tests.Truncated,
            Candidates: candidates);
    }

    private static IReadOnlyList<ReviewFinding> CreateFindings(
        PublicContractChangesSection publicChanges,
        DiffImpactSection impact,
        DiffDiagnosticsSection diagnostics)
    {
        List<ReviewFinding> findings = [];
        foreach (DiffDiagnosticItem diagnostic in diagnostics.Items)
        {
            findings.Add(new ReviewFinding(
                Kind: "fact",
                Code: "changed-scope-has-diagnostic",
                Severity: diagnostic.Severity == "Error" ? "error" : "warning",
                Confidence: "high",
                Claim: "A current compiler diagnostic is in the changed or affected scope.",
                Evidence: diagnostic.Path is null || diagnostic.Line is null || diagnostic.Column is null || diagnostic.EndLine is null || diagnostic.EndColumn is null
                    ? []
                    : [new DiffSourceLocation(diagnostic.Path, diagnostic.Line.Value, diagnostic.Column.Value, diagnostic.EndLine.Value, diagnostic.EndColumn.Value, ContainingSymbol: null, Snippet: null)],
                SourceLocations: [],
                SymbolIds: [],
                ReasonCodes: ["diagnostic-in-diff-scope"]));
        }

        foreach (PublicContractChange change in publicChanges.Changes)
        {
            findings.Add(new ReviewFinding(
                Kind: "risk",
                Code: "changed-public-symbol",
                Severity: "info",
                Confidence: change.Confidence,
                Claim: "A public or protected source symbol changed in the current diff.",
                Evidence: [ToSourceLocation(change.Symbol)],
                SourceLocations: [],
                SymbolIds: SymbolIds(change.Symbol),
                ReasonCodes: change.ReasonCodes));
        }

        foreach (DiffImpactItem item in impact.Items.Where(item => item.Callers.TotalItems > 0))
        {
            findings.Add(new ReviewFinding(
                Kind: "risk",
                Code: "changed-symbol-has-callers",
                Severity: "info",
                Confidence: "high",
                Claim: "A changed symbol has source callers.",
                Evidence: [ToSourceLocation(item.ChangedSymbol)],
                SourceLocations: item.Callers.Items.SelectMany(group => group.Locations).Take(5).ToArray(),
                SymbolIds: SymbolIds(item.ChangedSymbol),
                ReasonCodes: ["source-callers-found"]));
        }

        return [.. findings
            .OrderBy(finding => FindingPriority(finding.Code))
            .ThenBy(finding => finding.Code, StringComparer.Ordinal)
            .ThenBy(finding => finding.Evidence.FirstOrDefault()?.Path, StringComparer.Ordinal)
            .ThenBy(finding => finding.Evidence.FirstOrDefault()?.Line)];
    }

    private static IReadOnlyList<DiffAffectedFile> CreateAffectedFiles(
        DiffChangedSymbol symbol,
        LimitedList<DiffSourceLocation> references,
        LimitedList<DiffCallGroup> callers,
        LimitedList<DiffCallGroup> calls,
        LimitedList<DiffSymbolLocation> implementations)
    {
        Dictionary<string, AffectedFileBuilder> files = new(StringComparer.Ordinal);
        AddAffected(files, symbol.Path, "direct", "contains-changed-symbol", ToSourceLocation(symbol));
        foreach (DiffSourceLocation reference in references.Items)
        {
            AddAffected(files, reference.Path, "direct", "references-changed-symbol", reference);
        }

        foreach (DiffCallGroup group in callers.Items)
        {
            if (group.Symbol.Path is not null && group.Symbol.Line is not null && group.Symbol.Column is not null && group.Symbol.EndLine is not null && group.Symbol.EndColumn is not null)
            {
                AddAffected(files, group.Symbol.Path, "indirect", "caller-of-changed-symbol", new DiffSourceLocation(group.Symbol.Path, group.Symbol.Line.Value, group.Symbol.Column.Value, group.Symbol.EndLine.Value, group.Symbol.EndColumn.Value, ContainingSymbol: null, group.Symbol.Snippet));
            }
        }

        foreach (DiffCallGroup group in calls.Items)
        {
            if (group.Symbol.Path is not null && group.Symbol.Line is not null && group.Symbol.Column is not null && group.Symbol.EndLine is not null && group.Symbol.EndColumn is not null)
            {
                AddAffected(files, group.Symbol.Path, "indirect", "callee-of-changed-symbol", new DiffSourceLocation(group.Symbol.Path, group.Symbol.Line.Value, group.Symbol.Column.Value, group.Symbol.EndLine.Value, group.Symbol.EndColumn.Value, ContainingSymbol: null, group.Symbol.Snippet));
            }
        }

        foreach (DiffSymbolLocation implementation in implementations.Items)
        {
            if (implementation.Path is not null && implementation.Line is not null && implementation.Column is not null && implementation.EndLine is not null && implementation.EndColumn is not null)
            {
                AddAffected(files, implementation.Path, "indirect", "implements-changed-symbol", new DiffSourceLocation(implementation.Path, implementation.Line.Value, implementation.Column.Value, implementation.EndLine.Value, implementation.EndColumn.Value, ContainingSymbol: null, implementation.Snippet));
            }
        }

        return [.. files.Values
            .Select(builder => builder.Build())
            .OrderBy(file => file.ImpactLevel == "direct" ? 0 : 1)
            .ThenBy(file => file.Path, StringComparer.Ordinal)];
    }

    private static IReadOnlyList<DiffRiskReason> CreateRiskReasons(
        DiffChangedSymbol symbol,
        LimitedList<DiffCallGroup> callers,
        DiffDiagnosticsSection? diagnostics)
    {
        List<DiffRiskReason> reasons = [];
        if (callers.TotalItems > 0)
        {
            reasons.Add(new DiffRiskReason("changed-symbol-has-callers", "info", "high", [ToSourceLocation(symbol)]));
        }

        if (diagnostics is { TotalDiagnostics: > 0 })
        {
            reasons.Add(new DiffRiskReason("changed-symbol-has-diagnostics", "warning", "medium", [ToSourceLocation(symbol)]));
        }

        return reasons;
    }

    private static void AddScope(Dictionary<string, HashSet<string>> paths, string path, string reason)
    {
        if (!paths.TryGetValue(path, out HashSet<string>? reasons))
        {
            reasons = new HashSet<string>(StringComparer.Ordinal);
            paths.Add(path, reasons);
        }

        reasons.Add(reason);
    }

    private static void AddAffected(
        Dictionary<string, AffectedFileBuilder> files,
        string path,
        string impactLevel,
        string reason,
        DiffSourceLocation location)
    {
        if (!files.TryGetValue(path, out AffectedFileBuilder? builder))
        {
            builder = new AffectedFileBuilder(path);
            files.Add(path, builder);
        }

        builder.ImpactLevels.Add(impactLevel);
        builder.Reasons.Add(reason);
        builder.Locations.Add(location);
    }

    private static LimitedList<T> Limited<T>(IReadOnlyList<T> items, int limit)
    {
        return new LimitedList<T>(items.Count, limit, items.Count > limit, [.. items.Take(limit)]);
    }

    private static LimitedList<T> EmptyLimited<T>(int limit)
    {
        return new LimitedList<T>(0, limit, Truncated: false, Items: []);
    }

    private static Project? FindProject(IReadOnlyList<Project> projects, string? name)
    {
        return name is null ? null : projects.FirstOrDefault(project => project.Name == name);
    }

    private static DiffSourceLocation ToSourceLocation(DiffChangedSymbol symbol)
    {
        return new DiffSourceLocation(symbol.Path, symbol.Line, symbol.Column, symbol.EndLine, symbol.EndColumn, ContainingSymbol: null, Snippet: null);
    }

    private static IReadOnlyList<string> SymbolIds(DiffChangedSymbol symbol)
    {
        return symbol.Facts.DocumentationCommentId is null ? [] : [symbol.Facts.DocumentationCommentId];
    }

    private static int FindingPriority(string code)
    {
        return code switch
        {
            "changed-scope-has-diagnostic" => 0,
            "changed-public-symbol" => 1,
            "changed-symbol-has-callers" => 2,
            _ => 10
        };
    }

    private static IReadOnlyList<DiffNextAction> CreateBaseNextActions(
        string workspace,
        IReadOnlyList<DiffChangedSymbol> symbols)
    {
        DiffChangedSymbol? first = symbols.FirstOrDefault();
        List<DiffNextAction> actions =
        [
            new DiffNextAction("changed-symbols", workspace, Query: null, File: null, Line: null, Column: null, "Inspect raw changed symbol extraction."),
            new DiffNextAction("diagnostics-diff", workspace, Query: null, File: null, Line: null, Column: null, "Inspect current diagnostics scoped to changed and affected files.")
        ];

        if (first is not null)
        {
            actions.Insert(0, new DiffNextAction("impact", workspace, first.Name, first.Path, first.Line, first.Column, "Inspect fuzzy impact for a changed symbol."));
        }

        if (symbols.Any(symbol => symbol.Facts.Accessibility is "Public" or "Protected" or "ProtectedOrInternal"))
        {
            actions.Add(new DiffNextAction("public-api-diff", workspace, Query: null, File: null, Line: null, Column: null, "Compare source-level public API changes against a base ref."));
        }

        return actions;
    }

    private static DiffSnippet? TryReadSnippet(string path, int line, int contextLines)
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
            return new DiffSnippet(startLine, endLine, [.. lines.Skip(startLine - 1).Take(endLine - startLine + 1)]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private sealed class AffectedFileBuilder(string path)
    {
        public string Path { get; } = path;

        public HashSet<string> ImpactLevels { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Reasons { get; } = new(StringComparer.Ordinal);

        public List<DiffSourceLocation> Locations { get; } = [];

        public DiffAffectedFile Build()
        {
            return new DiffAffectedFile(
                Path,
                ImpactLevels.Contains("direct") ? "direct" : "indirect",
                [.. Reasons.OrderBy(reason => reason, StringComparer.Ordinal)],
                [.. Locations
                    .GroupBy(location => (location.Path, location.Line, location.Column, location.EndLine, location.EndColumn))
                    .Select(group => group.First())
                    .OrderBy(location => location.Path, StringComparer.Ordinal)
                    .ThenBy(location => location.Line)
                    .ThenBy(location => location.Column)]);
        }
    }
}

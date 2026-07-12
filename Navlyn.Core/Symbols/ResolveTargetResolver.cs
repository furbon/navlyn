using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Navlyn.GeneratedCode;
using Navlyn.Workspaces;

namespace Navlyn.Symbols;

internal sealed class ResolveTargetResolver
{
    public async Task<ResolveTargetResult> ResolveFuzzyAsync(
        LoadedWorkspace workspace,
        FuzzyQueryOptions options,
        IReadOnlyList<Project> projects,
        IReadOnlyList<FuzzyProjectFilter>? projectFilters,
        CancellationToken cancellationToken)
    {
        FuzzyFindResult find = await new FuzzyDiscoveryResolver().FindAsync(
            workspace,
            options,
            projects,
            projectFilters,
            cancellationToken);

        string mode = options.CandidateId is null ? "query" : "candidateId";
        FuzzySymbolCandidate? selected = find.SelectedCandidate;
        return new ResolveTargetResult(
            Workspace: workspace.DisplayPath,
            Kind: workspace.Kind,
            Command: "resolve-target",
            SelectionInput: new ResolveTargetInput(mode, Query: options.CandidateId is null ? options.Query : null, CandidateId: options.CandidateId, File: null, Line: null, Column: null),
            SelectedTarget: selected is null ? null : ResolveTargetSymbol.FromCandidate(selected),
            CandidateId: selected?.CandidateId,
            Selector: selected?.Selector,
            Confidence: find.Confidence,
            AmbiguityReason: selected is null ? GetAmbiguityReason(find) : null,
            AmbiguitySummary: CreateAmbiguitySummary(find, selected),
            CandidateCount: find.CandidateCount,
            TotalCandidates: find.TotalCandidates,
            Candidates: selected is null ? find.Candidates : null,
            RecommendedNextActions: find.NextActions,
            Warnings: find.Warnings);
    }

    public async Task<ResolveTargetResult> ResolveSourcePositionAsync(
        LoadedWorkspace workspace,
        FileInfo file,
        int line,
        int column,
        Project? project,
        bool excludeGenerated,
        CancellationToken cancellationToken)
    {
        SymbolAtResolutionResult result = await new SymbolAtResolver().ResolveAsync(
            workspace.Solution,
            file,
            line,
            column,
            project,
            excludeGenerated,
            cancellationToken);

        if (result.Error is not null)
        {
            return ResolveTargetResult.Failed(workspace, new ResolveTargetInput("sourcePosition", Query: null, CandidateId: null, File: file.ToString(), Line: line, Column: column), result.Error.Message);
        }

        SymbolAtResolution resolution = result.Resolution!;
        ResolveTargetSymbol selected = ResolveTargetSymbol.FromSymbolAt(resolution.Symbol);
        return new ResolveTargetResult(
            Workspace: workspace.DisplayPath,
            Kind: workspace.Kind,
            Command: "resolve-target",
            SelectionInput: new ResolveTargetInput("sourcePosition", Query: null, CandidateId: null, File: resolution.File, Line: resolution.Line, Column: resolution.Column),
            SelectedTarget: selected,
            CandidateId: null,
            Selector: null,
            Confidence: "high",
            AmbiguityReason: null,
            AmbiguitySummary: null,
            CandidateCount: 1,
            TotalCandidates: 1,
            Candidates: null,
            RecommendedNextActions: CreateSourcePositionNextActions(workspace.DisplayPath, resolution),
            Warnings: []);
    }

    private static string? GetAmbiguityReason(FuzzyFindResult find)
    {
        if (find.TotalCandidates == 0)
        {
            return "no-candidates";
        }

        if (find.Confidence == "ambiguous")
        {
            return "ambiguous-candidates";
        }

        return "no-selected-target";
    }

    private static ResolveTargetAmbiguitySummary? CreateAmbiguitySummary(FuzzyFindResult find, FuzzySymbolCandidate? selected)
    {
        if (selected is not null && find.TotalCandidates <= 1)
        {
            return null;
        }

        List<string> reasonCodes = [];
        List<ResolveTargetAmbiguityGroup> groups = [];

        if (find.TotalCandidates == 0)
        {
            reasonCodes.Add("no-candidates");
            return new ResolveTargetAmbiguitySummary(
                IsAmbiguous: true,
                PrimaryReason: "no-candidates",
                ReasonCodes: reasonCodes,
                Groups: groups,
                RecommendedAction: "Check the query, source position, workspace, project filter, or assume-kind before retrying.");
        }

        if (find.Confidence == "ambiguous" || selected is null)
        {
            reasonCodes.Add("ambiguous-candidates");
        }

        AddGroup(
            groups,
            reasonCodes,
            "multiple-projects",
            find.Candidates
                .GroupBy(candidate => candidate.Selector?.Project ?? candidate.Facts.Project ?? "")
                .Where(group => !string.IsNullOrWhiteSpace(group.Key))
                .Select(group => (Name: group.Key, Count: group.Count()))
                .ToArray());

        AddGroup(
            groups,
            reasonCodes,
            "multiple-target-frameworks",
            find.Candidates
                .GroupBy(candidate => candidate.Selector?.TargetFramework ?? "")
                .Where(group => !string.IsNullOrWhiteSpace(group.Key))
                .Select(group => (Name: group.Key, Count: group.Count()))
                .ToArray());

        AddGroup(
            groups,
            reasonCodes,
            "same-file-duplicates",
            find.Candidates
                .GroupBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => (Name: group.Key, Count: group.Count()))
                .ToArray());

        AddGroup(
            groups,
            reasonCodes,
            "test-project-candidates",
            find.Candidates
                .Where(IsTestCandidate)
                .GroupBy(candidate => candidate.Selector?.Project ?? candidate.Facts.Project ?? candidate.Path)
                .Select(group => (Name: group.Key, Count: group.Count()))
                .ToArray());

        AddGroup(
            groups,
            reasonCodes,
            "generated-candidates",
            find.Candidates
                .Where(candidate => GeneratedCodeFacts.IsGeneratedPath(candidate.Path))
                .GroupBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
                .Select(group => (Name: group.Key, Count: group.Count()))
                .ToArray());

        AddGroup(
            groups,
            reasonCodes,
            "metadata-candidates",
            find.Candidates
                .Where(candidate => candidate.Facts.IsMetadata)
                .GroupBy(candidate => candidate.Facts.Assembly ?? "metadata")
                .Select(group => (Name: group.Key, Count: group.Count()))
                .ToArray());

        if (find.CandidatesTruncated)
        {
            reasonCodes.Add("candidate-limit-reached");
        }

        string primaryReason = reasonCodes.FirstOrDefault() ?? "multiple-candidates";
        if (reasonCodes.Count == 0 && find.TotalCandidates > 1)
        {
            reasonCodes.Add(primaryReason);
        }

        return new ResolveTargetAmbiguitySummary(
            IsAmbiguous: selected is null || find.Confidence == "ambiguous",
            PrimaryReason: primaryReason,
            ReasonCodes: reasonCodes,
            Groups: groups,
            RecommendedAction: "Narrow the target with --project, --assume-kind, a source position, or a returned candidateId before reading or editing source.");
    }

    private static void AddGroup(
        List<ResolveTargetAmbiguityGroup> groups,
        List<string> reasonCodes,
        string reason,
        IReadOnlyList<(string Name, int Count)> entries)
    {
        if (entries.Count <= 1 && reason is "multiple-projects" or "multiple-target-frameworks")
        {
            return;
        }

        if (entries.Count == 0)
        {
            return;
        }

        reasonCodes.Add(reason);
        groups.Add(new ResolveTargetAmbiguityGroup(
            Reason: reason,
            Count: entries.Sum(entry => entry.Count),
            Examples: [.. entries
                .OrderByDescending(entry => entry.Count)
                .ThenBy(entry => entry.Name, StringComparer.Ordinal)
                .Take(5)
                .Select(entry => entry.Name)]));
    }

    private static bool IsTestCandidate(FuzzySymbolCandidate candidate)
    {
        string project = candidate.Selector?.Project ?? candidate.Facts.Project ?? "";
        return project.Contains("Test", StringComparison.OrdinalIgnoreCase) ||
            candidate.Path.Contains("/tests/", StringComparison.OrdinalIgnoreCase) ||
            candidate.Path.Contains("\\tests\\", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<FuzzyNextAction> CreateSourcePositionNextActions(string workspace, SymbolAtResolution resolution)
    {
        return [
            new FuzzyNextAction("definition", workspace, Query: null, File: resolution.File, Line: resolution.Line, Column: resolution.Column, Reason: "inspect-selected-definition"),
            new FuzzyNextAction("references", workspace, Query: null, File: resolution.File, Line: resolution.Line, Column: resolution.Column, Reason: "inspect-selected-references"),
            new FuzzyNextAction("symbol-info", workspace, Query: null, File: resolution.File, Line: resolution.Line, Column: resolution.Column, Reason: "inspect-selected-symbol-info")
        ];
    }
}

internal sealed record ResolveTargetResult(
    string Workspace,
    string Kind,
    string Command,
    ResolveTargetInput SelectionInput,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ResolveTargetSymbol? SelectedTarget,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CandidateId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FuzzyCandidateSelector? Selector,
    string Confidence,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? AmbiguityReason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ResolveTargetAmbiguitySummary? AmbiguitySummary,
    int CandidateCount,
    int TotalCandidates,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<FuzzySymbolCandidate>? Candidates,
    IReadOnlyList<FuzzyNextAction> RecommendedNextActions,
    IReadOnlyList<string> Warnings)
{
    public static ResolveTargetResult Failed(LoadedWorkspace workspace, ResolveTargetInput selectionInput, string warning)
    {
        return new ResolveTargetResult(
            Workspace: workspace.DisplayPath,
            Kind: workspace.Kind,
            Command: "resolve-target",
            SelectionInput: selectionInput,
            SelectedTarget: null,
            CandidateId: null,
            Selector: null,
            Confidence: "none",
            AmbiguityReason: "source-position-not-resolved",
            AmbiguitySummary: new ResolveTargetAmbiguitySummary(
                IsAmbiguous: true,
                PrimaryReason: "source-position-not-resolved",
                ReasonCodes: ["source-position-not-resolved"],
                Groups: [],
                RecommendedAction: "Check that the file, line, column, generated-code filter, and project filter point at a resolvable source symbol."),
            CandidateCount: 0,
            TotalCandidates: 0,
            Candidates: null,
            RecommendedNextActions: [],
            Warnings: [warning]);
    }
}

internal sealed record ResolveTargetInput(
    string Mode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Query,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CandidateId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? File,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? Line,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? Column);

internal sealed record ResolveTargetAmbiguitySummary(
    bool IsAmbiguous,
    string PrimaryReason,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<ResolveTargetAmbiguityGroup> Groups,
    string RecommendedAction);

internal sealed record ResolveTargetAmbiguityGroup(
    string Reason,
    int Count,
    IReadOnlyList<string> Examples);

internal sealed record ResolveTargetSymbol(
    string Name,
    string Kind,
    string? Container,
    SymbolFacts Facts,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Path,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? Line,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? Column,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? EndLine,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? EndColumn)
{
    public static ResolveTargetSymbol FromCandidate(FuzzySymbolCandidate candidate)
    {
        return new ResolveTargetSymbol(
            candidate.Name,
            candidate.Kind,
            candidate.Container,
            candidate.Facts,
            candidate.Path,
            candidate.Line,
            candidate.Column,
            candidate.EndLine,
            candidate.EndColumn);
    }

    public static ResolveTargetSymbol FromSymbolAt(SymbolAtSymbol symbol)
    {
        return new ResolveTargetSymbol(
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
}

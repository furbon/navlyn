using System.CommandLine;
using System.Text.Json.Serialization;
using Navlyn.Diagnostics;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class ReferencesCommand
{
    public static Command Create()
    {
        Option<string[]> resultProjectOption = NavigationResultOptions.CreateResultProjectOption();
        Option<string[]> resultPathOption = NavigationResultOptions.CreateResultPathOption();
        Option<string[]> resultKindOption = NavigationResultOptions.CreateResultKindOption();
        Option<string[]> usageKindOption = CreateUsageKindOption();
        Option<string[]> groupByOption = CreateGroupByOption();
        Option<int?> limitOption = SharedOptions.CreateLimitOption();
        Option<string> scopeOption = SharedOptions.CreateSearchScopeOption();
        Option<int?> maxDocumentsOption = SharedOptions.CreateMaxDocumentsOption();

        return SourcePositionCommand.Create(
            "references",
            "Find source references for the C# symbol at a source position.",
            [resultProjectOption, resultPathOption, resultKindOption, usageKindOption, groupByOption, limitOption, scopeOption, maxDocumentsOption],
            (workspace, options, parseResult, cancellationToken) => ExecuteAsync(
                workspace,
                options,
                parseResult.GetValue(resultProjectOption) ?? [],
                parseResult.GetValue(resultPathOption) ?? [],
                parseResult.GetValue(resultKindOption) ?? [],
                parseResult.GetValue(usageKindOption) ?? [],
                parseResult.GetValue(groupByOption) ?? [],
                parseResult.GetValue(limitOption),
                parseResult.GetValue(scopeOption)!,
                parseResult.GetValue(maxDocumentsOption),
                cancellationToken));
    }

    private static async Task<int> ExecuteAsync(
        LoadedWorkspace loadedWorkspace,
        SourcePositionOptions options,
        IReadOnlyList<string> resultProjectFilters,
        IReadOnlyList<string> resultPaths,
        IReadOnlyList<string> resultKinds,
        IReadOnlyList<string> usageKindValues,
        IReadOnlyList<string> groupByValues,
        int? limit,
        string scope,
        int? maxDocuments,
        CancellationToken cancellationToken)
    {
        if (!FuzzyCommandSupport.TryCreatePositiveOption("--max-documents", maxDocuments ?? SymbolNavigationSearchOptions.DefaultMaxDocuments, out int exitCode))
        {
            return exitCode;
        }

        if (!NavigationResultOptions.TryCreate(
            loadedWorkspace,
            resultProjectFilters,
            resultPaths,
            resultKinds,
            limit,
            out NavigationResultFilter resultFilter,
            out int filterExitCode))
        {
            return filterExitCode;
        }

        if (!TryNormalizeUsageOptions(
            usageKindValues,
            groupByValues,
            out IReadOnlyList<string> usageKinds,
            out IReadOnlyList<string> groupBy,
            out int usageExitCode))
        {
            return usageExitCode;
        }

        ReferencesResolutionResult result = await new ReferencesResolver().ResolveAsync(
            loadedWorkspace.Solution,
            options.File,
            options.Line,
            options.Column,
            options.Project,
            options.ExcludeGenerated,
            SymbolNavigationSearchOptions.Create(scope, maxDocuments),
            cancellationToken);

        if (result.Error is not null)
        {
            DiagnosticReporter.WriteError(result.Error.DiagnosticId, result.Error.Message);
            return result.Error.ExitCode;
        }

        ReferencesResolution resolution = result.Resolution!;
        IReadOnlyList<SymbolReferenceLocation> filteredReferences = [.. resolution.References
            .Where(reference => NavigationResultOptions.MatchesSymbol(resultFilter, reference.Path, resolution.Symbol.Kind))
            .Where(reference => usageKinds.Count == 0 || usageKinds.Contains(reference.UsageKind, StringComparer.Ordinal))];
        IReadOnlyList<SymbolReferenceLocation> limitedReferences =
            NavigationResultOptions.ApplyLimit(filteredReferences, resultFilter.Limit);
        IReadOnlyList<ReferenceUsageGroup> groups = ReferenceUsageTaxonomy.CreateGroups(filteredReferences, groupBy);

        ConsoleJsonWriter.Write(new ReferencesResult(
            File: resolution.File,
            Line: resolution.Line,
            Column: resolution.Column,
            Project: options.ProjectFilter is null ? null : ProjectFilterOutput.FromAppliedFilter(options.ProjectFilter),
            SelectionInput: options.SelectionInput,
            ResultProjects: resultFilter.AppliedProjectFilters.Count == 0
                ? null
                : resultFilter.AppliedProjectFilters.Select(ProjectFilterOutput.FromAppliedFilter).ToArray(),
            ResultPaths: resultFilter.PathFilters.Count == 0 ? null : resultFilter.PathFilters,
            ResultKinds: resultFilter.KindFilters.Count == 0 ? null : resultFilter.KindFilters,
            UsageKinds: usageKinds.Count == 0 ? null : usageKinds,
            GroupBy: groupBy.Count == 0 ? null : groupBy,
            ExcludeGenerated: options.ExcludeGenerated,
            Limit: resultFilter.Limit,
            TotalMatches: filteredReferences.Count,
            Search: resolution.Search,
            UsageKindCounts: ReferenceUsageTaxonomy.CreateCounts(filteredReferences),
            Symbol: new ReferencesSymbolResult(
                Name: resolution.Symbol.Name,
                Kind: resolution.Symbol.Kind,
                Container: resolution.Symbol.Container,
                Facts: resolution.Symbol.Facts),
            References: limitedReferences.Select(reference => new ReferenceLocationResult(
                Path: reference.Path,
                Line: reference.Line,
                Column: reference.Column,
                EndLine: reference.EndLine,
                EndColumn: reference.EndColumn,
                UsageKind: reference.UsageKind,
                ContainingSymbol: reference.ContainingSymbol is null
                    ? null
                    : new ReferencesContainingSymbolResult(
                        Name: reference.ContainingSymbol.Name,
                        Kind: reference.ContainingSymbol.Kind,
                        Container: reference.ContainingSymbol.Container,
                        Facts: reference.ContainingSymbol.Facts,
                        Path: reference.ContainingSymbol.Path,
                        Line: reference.ContainingSymbol.Line,
                        Column: reference.ContainingSymbol.Column,
                        EndLine: reference.ContainingSymbol.EndLine,
                        EndColumn: reference.ContainingSymbol.EndColumn))).ToArray(),
            Groups: groups.Count == 0 ? null : groups));

        return ExitCodes.Success;
    }

    internal static Option<string[]> CreateUsageKindOption()
    {
        return new Option<string[]>("--usage-kind")
        {
            Description = $"Restrict references by usage kind. Values: {string.Join(", ", ReferenceUsageTaxonomy.UsageKinds)}.",
            AllowMultipleArgumentsPerToken = true
        };
    }

    internal static Option<string[]> CreateGroupByOption()
    {
        return new Option<string[]>("--group-by")
        {
            Description = $"Add grouped reference summaries. Values: {string.Join(", ", ReferenceUsageTaxonomy.GroupKinds)}.",
            AllowMultipleArgumentsPerToken = true
        };
    }

    internal static bool TryNormalizeUsageOptions(
        IReadOnlyList<string> usageKindValues,
        IReadOnlyList<string> groupByValues,
        out IReadOnlyList<string> usageKinds,
        out IReadOnlyList<string> groupBy,
        out int exitCode)
    {
        if (!ReferenceUsageTaxonomy.TryNormalizeUsageKinds(usageKindValues, out usageKinds, out string? usageError))
        {
            DiagnosticReporter.WriteError(DiagnosticIds.ParseError, usageError!);
            groupBy = [];
            exitCode = ExitCodes.UsageError;
            return false;
        }

        if (!ReferenceUsageTaxonomy.TryNormalizeGroupKinds(groupByValues, out groupBy, out string? groupError))
        {
            DiagnosticReporter.WriteError(DiagnosticIds.ParseError, groupError!);
            exitCode = ExitCodes.UsageError;
            return false;
        }

        exitCode = ExitCodes.Success;
        return true;
    }

    private sealed record ReferencesResult(
        string File,
        int Line,
        int Column,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        ProjectFilterOutput? Project,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        CandidateSelectionInput? SelectionInput,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<ProjectFilterOutput>? ResultProjects,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<string>? ResultPaths,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<string>? ResultKinds,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<string>? UsageKinds,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<string>? GroupBy,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool ExcludeGenerated,
        int? Limit,
        int TotalMatches,
        SymbolNavigationSearchMetadata Search,
        IReadOnlyList<ReferenceUsageCount> UsageKindCounts,
        ReferencesSymbolResult Symbol,
        IReadOnlyList<ReferenceLocationResult> References,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<ReferenceUsageGroup>? Groups);

    private sealed record ReferencesSymbolResult(string Name, string Kind, string? Container, SymbolFacts Facts);

    private sealed record ReferenceLocationResult(
        string Path,
        int Line,
        int Column,
        int EndLine,
        int EndColumn,
        string UsageKind,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        ReferencesContainingSymbolResult? ContainingSymbol);

    private sealed record ReferencesContainingSymbolResult(
        string Name,
        string Kind,
        string? Container,
        SymbolFacts Facts,
        string? Path,
        int? Line,
        int? Column,
        int? EndLine,
        int? EndColumn);
}

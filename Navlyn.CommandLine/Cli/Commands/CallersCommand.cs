using System.CommandLine;
using System.Text.Json.Serialization;
using Navlyn.Diagnostics;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class CallersCommand
{
    public static Command Create()
    {
        Option<string[]> resultProjectOption = NavigationResultOptions.CreateResultProjectOption();
        Option<string[]> resultPathOption = NavigationResultOptions.CreateResultPathOption();
        Option<string[]> resultKindOption = NavigationResultOptions.CreateResultKindOption();
        Option<int?> limitOption = SharedOptions.CreateLimitOption();
        Option<string> scopeOption = SharedOptions.CreateSearchScopeOption();
        Option<int?> maxDocumentsOption = SharedOptions.CreateMaxDocumentsOption();

        return SourcePositionCommand.Create(
            "callers",
            "Find source callers for the C# or Visual Basic symbol at a source position.",
            [resultProjectOption, resultPathOption, resultKindOption, limitOption, scopeOption, maxDocumentsOption],
            (workspace, options, parseResult, cancellationToken) => ExecuteAsync(
                workspace,
                options,
                parseResult.GetValue(resultProjectOption) ?? [],
                parseResult.GetValue(resultPathOption) ?? [],
                parseResult.GetValue(resultKindOption) ?? [],
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

        CallersResolutionResult result = await new CallHierarchyResolver().ResolveCallersAsync(
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

        CallersResolution resolution = result.Resolution!;
        IReadOnlyList<CallHierarchyGroup> filteredCallers = [.. resolution.Callers
            .Where(group => group.Symbol.Path is not null &&
                NavigationResultOptions.MatchesSymbol(resultFilter, group.Symbol.Path, group.Symbol.Kind))];
        IReadOnlyList<CallHierarchyGroup> limitedCallers =
            NavigationResultOptions.ApplyLimit(filteredCallers, resultFilter.Limit);

        ConsoleJsonWriter.Write(new CallersResult(
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
            ExcludeGenerated: options.ExcludeGenerated,
            Limit: resultFilter.Limit,
            TotalGroups: filteredCallers.Count,
            Search: resolution.Search,
            Symbol: CallHierarchySymbolResult.FromSymbol(resolution.Symbol),
            Callers: limitedCallers.Select(CallHierarchyGroupResult.FromGroup).ToArray()));

        return ExitCodes.Success;
    }

    private sealed record CallersResult(
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
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool ExcludeGenerated,
        int? Limit,
        int TotalGroups,
        SymbolNavigationSearchMetadata Search,
        CallHierarchySymbolResult Symbol,
        IReadOnlyList<CallHierarchyGroupResult> Callers);
}

internal sealed record CallHierarchySymbolResult(
    string Name,
    string Kind,
    string? Container,
    SymbolFacts Facts,
    string? Path,
    int? Line,
    int? Column,
    int? EndLine,
    int? EndColumn)
{
    public static CallHierarchySymbolResult FromSymbol(CallHierarchySymbol symbol)
    {
        return new CallHierarchySymbolResult(
            Name: symbol.Name,
            Kind: symbol.Kind,
            Container: symbol.Container,
            Facts: symbol.Facts,
            Path: symbol.Path,
            Line: symbol.Line,
            Column: symbol.Column,
            EndLine: symbol.EndLine,
            EndColumn: symbol.EndColumn);
    }
}

internal sealed record CallHierarchyGroupResult(
    CallHierarchySymbolResult Symbol,
    IReadOnlyList<CallHierarchyLocationResult> Locations)
{
    public static CallHierarchyGroupResult FromGroup(CallHierarchyGroup group)
    {
        return new CallHierarchyGroupResult(
            Symbol: CallHierarchySymbolResult.FromSymbol(group.Symbol),
            Locations: group.Locations.Select(CallHierarchyLocationResult.FromLocation).ToArray());
    }
}

internal sealed record CallHierarchyLocationResult(string Path, int Line, int Column, int EndLine, int EndColumn)
{
    public static CallHierarchyLocationResult FromLocation(CallHierarchyLocation location)
    {
        return new CallHierarchyLocationResult(
            Path: location.Path,
            Line: location.Line,
            Column: location.Column,
            EndLine: location.EndLine,
            EndColumn: location.EndColumn);
    }
}

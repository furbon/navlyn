using System.CommandLine;
using System.Text.Json.Serialization;
using Navlyn.Diagnostics;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class CallsCommand
{
    public static Command Create()
    {
        Option<string[]> resultProjectOption = NavigationResultOptions.CreateResultProjectOption();
        Option<string[]> resultPathOption = NavigationResultOptions.CreateResultPathOption();
        Option<string[]> resultKindOption = NavigationResultOptions.CreateResultKindOption();
        Option<int?> limitOption = SharedOptions.CreateLimitOption();
        Option<bool> includeMetadataOption = SharedOptions.CreateIncludeMetadataOption();

        return SourcePositionCommand.Create(
            "calls",
            "Find source callees from the containing C# member at a source position.",
            [resultProjectOption, resultPathOption, resultKindOption, limitOption, includeMetadataOption],
            (workspace, options, parseResult, cancellationToken) => ExecuteAsync(
                workspace,
                options,
                parseResult.GetValue(resultProjectOption) ?? [],
                parseResult.GetValue(resultPathOption) ?? [],
                parseResult.GetValue(resultKindOption) ?? [],
                parseResult.GetValue(limitOption),
                parseResult.GetValue(includeMetadataOption),
                cancellationToken));
    }

    private static async Task<int> ExecuteAsync(
        LoadedWorkspace loadedWorkspace,
        SourcePositionOptions options,
        IReadOnlyList<string> resultProjectFilters,
        IReadOnlyList<string> resultPaths,
        IReadOnlyList<string> resultKinds,
        int? limit,
        bool includeMetadata,
        CancellationToken cancellationToken)
    {
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

        CallsResolutionResult result = await new CallHierarchyResolver().ResolveCallsAsync(
            loadedWorkspace.Solution,
            options.File,
            options.Line,
            options.Column,
            options.Project,
            options.ExcludeGenerated,
            includeMetadata,
            cancellationToken);

        if (result.Error is not null)
        {
            DiagnosticReporter.WriteError(result.Error.DiagnosticId, result.Error.Message);
            return result.Error.ExitCode;
        }

        CallsResolution resolution = result.Resolution!;
        IReadOnlyList<CallHierarchyGroup> filteredCalls = [.. resolution.Calls
            .Where(group => NavigationResultOptions.MatchesSymbolOrMetadata(
                resultFilter,
                group.Symbol.Path,
                group.Symbol.Kind))];
        IReadOnlyList<CallHierarchyGroup> limitedCalls =
            NavigationResultOptions.ApplyLimit(filteredCalls, resultFilter.Limit);

        ConsoleJsonWriter.Write(new CallsResult(
            File: resolution.File,
            Line: resolution.Line,
            Column: resolution.Column,
            Project: options.ProjectFilter is null ? null : ProjectFilterOutput.FromAppliedFilter(options.ProjectFilter),
            ResultProjects: resultFilter.AppliedProjectFilters.Count == 0
                ? null
                : resultFilter.AppliedProjectFilters.Select(ProjectFilterOutput.FromAppliedFilter).ToArray(),
            ResultPaths: resultFilter.PathFilters.Count == 0 ? null : resultFilter.PathFilters,
            ResultKinds: resultFilter.KindFilters.Count == 0 ? null : resultFilter.KindFilters,
            ExcludeGenerated: options.ExcludeGenerated,
            IncludeMetadata: includeMetadata,
            Limit: resultFilter.Limit,
            TotalGroups: filteredCalls.Count,
            Caller: CallHierarchySymbolResult.FromSymbol(resolution.Caller),
            Calls: limitedCalls.Select(CallHierarchyGroupResult.FromGroup).ToArray()));

        return ExitCodes.Success;
    }

    private sealed record CallsResult(
        string File,
        int Line,
        int Column,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        ProjectFilterOutput? Project,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<ProjectFilterOutput>? ResultProjects,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<string>? ResultPaths,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<string>? ResultKinds,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool ExcludeGenerated,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool IncludeMetadata,
        int? Limit,
        int TotalGroups,
        CallHierarchySymbolResult Caller,
        IReadOnlyList<CallHierarchyGroupResult> Calls);
}

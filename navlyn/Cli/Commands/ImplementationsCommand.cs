using System.CommandLine;
using System.Text.Json.Serialization;
using Navlyn.Diagnostics;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class ImplementationsCommand
{
    public static Command Create()
    {
        Option<string[]> resultProjectOption = NavigationResultOptions.CreateResultProjectOption();
        Option<string[]> resultPathOption = NavigationResultOptions.CreateResultPathOption();
        Option<string[]> resultKindOption = NavigationResultOptions.CreateResultKindOption();
        Option<int?> limitOption = SharedOptions.CreateLimitOption();

        return SourcePositionCommand.Create(
            "implementations",
            "Find source implementations for the C# symbol at a source position.",
            [resultProjectOption, resultPathOption, resultKindOption, limitOption],
            (workspace, options, parseResult, cancellationToken) => ExecuteAsync(
                workspace,
                options,
                parseResult.GetValue(resultProjectOption) ?? [],
                parseResult.GetValue(resultPathOption) ?? [],
                parseResult.GetValue(resultKindOption) ?? [],
                parseResult.GetValue(limitOption),
                cancellationToken));
    }

    private static async Task<int> ExecuteAsync(
        LoadedWorkspace loadedWorkspace,
        SourcePositionOptions options,
        IReadOnlyList<string> resultProjectFilters,
        IReadOnlyList<string> resultPaths,
        IReadOnlyList<string> resultKinds,
        int? limit,
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

        ImplementationsResolutionResult result = await new ImplementationsResolver().ResolveAsync(
            loadedWorkspace.Solution,
            options.File,
            options.Line,
            options.Column,
            options.Project,
            options.ExcludeGenerated,
            cancellationToken);

        if (result.Error is not null)
        {
            DiagnosticReporter.WriteError(result.Error.DiagnosticId, result.Error.Message);
            return result.Error.ExitCode;
        }

        ImplementationsResolution resolution = result.Resolution!;
        IReadOnlyList<ImplementationLocation> filteredImplementations = [.. resolution.Implementations
            .Where(implementation => NavigationResultOptions.MatchesSymbol(resultFilter, implementation.Path, implementation.Kind))];
        IReadOnlyList<ImplementationLocation> limitedImplementations =
            NavigationResultOptions.ApplyLimit(filteredImplementations, resultFilter.Limit);

        ConsoleJsonWriter.Write(new ImplementationsResult(
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
            Limit: resultFilter.Limit,
            TotalMatches: filteredImplementations.Count,
            Symbol: new ImplementationsSymbolResult(
                Name: resolution.Symbol.Name,
                Kind: resolution.Symbol.Kind,
                Container: resolution.Symbol.Container,
                Facts: resolution.Symbol.Facts),
            Implementations: limitedImplementations.Select(implementation => new ImplementationLocationResult(
                Name: implementation.Name,
                Kind: implementation.Kind,
                Container: implementation.Container,
                Facts: implementation.Facts,
                Path: implementation.Path,
                Line: implementation.Line,
                Column: implementation.Column,
                EndLine: implementation.EndLine,
                EndColumn: implementation.EndColumn)).ToArray()));

        return ExitCodes.Success;
    }

    private sealed record ImplementationsResult(
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
        int? Limit,
        int TotalMatches,
        ImplementationsSymbolResult Symbol,
        IReadOnlyList<ImplementationLocationResult> Implementations);

    private sealed record ImplementationsSymbolResult(string Name, string Kind, string? Container, SymbolFacts Facts);

    private sealed record ImplementationLocationResult(
        string Name,
        string Kind,
        string? Container,
        SymbolFacts Facts,
        string Path,
        int Line,
        int Column,
        int EndLine,
        int EndColumn);
}

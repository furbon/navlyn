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
        Option<int?> limitOption = SharedOptions.CreateLimitOption();

        return SourcePositionCommand.Create(
            "references",
            "Find source references for the C# symbol at a source position.",
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

        ReferencesResolutionResult result = await new ReferencesResolver().ResolveAsync(
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

        ReferencesResolution resolution = result.Resolution!;
        IReadOnlyList<SymbolReferenceLocation> filteredReferences = [.. resolution.References
            .Where(reference => NavigationResultOptions.MatchesSymbol(resultFilter, reference.Path, resolution.Symbol.Kind))];
        IReadOnlyList<SymbolReferenceLocation> limitedReferences =
            NavigationResultOptions.ApplyLimit(filteredReferences, resultFilter.Limit);

        ConsoleJsonWriter.Write(new ReferencesResult(
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
            TotalMatches: filteredReferences.Count,
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
                        EndColumn: reference.ContainingSymbol.EndColumn))).ToArray()));

        return ExitCodes.Success;
    }

    private sealed record ReferencesResult(
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
        ReferencesSymbolResult Symbol,
        IReadOnlyList<ReferenceLocationResult> References);

    private sealed record ReferencesSymbolResult(string Name, string Kind, string? Container, SymbolFacts Facts);

    private sealed record ReferenceLocationResult(
        string Path,
        int Line,
        int Column,
        int EndLine,
        int EndColumn,
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

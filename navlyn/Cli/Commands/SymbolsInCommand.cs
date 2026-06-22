using System.CommandLine;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Navlyn.Diagnostics;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class SymbolsInCommand
{
    public static Command Create()
    {
        Option<FileInfo> fileOption = SharedOptions.CreateFileOption();
        Option<int> lineOption = SharedOptions.CreateLineOption();
        Option<int?> startColumnOption = new("--start-column")
        {
            Description = "1-based inclusive start column. Defaults to the start of the line."
        };
        Option<int?> endColumnOption = new("--end-column")
        {
            Description = "1-based exclusive end column. Defaults to the end of the line."
        };
        Option<string?> projectOption = SharedOptions.CreateProjectFilterOption();
        Option<bool> excludeGeneratedOption = SharedOptions.CreateExcludeGeneratedOption();

        return WorkspaceCommand.Create(
            "symbols-in",
            "List C# identifier symbols in a source line or column span.",
            [fileOption, lineOption, startColumnOption, endColumnOption, projectOption, excludeGeneratedOption],
            (workspace, parseResult, cancellationToken) => ExecuteAsync(
                workspace,
                parseResult.GetValue(fileOption)!,
                parseResult.GetValue(lineOption),
                parseResult.GetValue(startColumnOption),
                parseResult.GetValue(endColumnOption),
                parseResult.GetValue(projectOption),
                parseResult.GetValue(excludeGeneratedOption),
                cancellationToken));
    }

    private static async Task<int> ExecuteAsync(
        LoadedWorkspace loadedWorkspace,
        FileInfo file,
        int line,
        int? startColumn,
        int? endColumn,
        string? projectFilter,
        bool excludeGenerated,
        CancellationToken cancellationToken)
    {
        if (!ProjectFilterCommand.TryResolveSingleProject(
            loadedWorkspace,
            projectFilter,
            out Project? project,
            out AppliedProjectFilter? appliedProjectFilter,
            out int exitCode))
        {
            return exitCode;
        }

        SymbolsInResolutionResult result = await new SymbolsInResolver().ResolveAsync(
            loadedWorkspace.Solution,
            file,
            line,
            startColumn,
            endColumn,
            project,
            excludeGenerated,
            cancellationToken);

        if (result.Error is not null)
        {
            DiagnosticReporter.WriteError(result.Error.DiagnosticId, result.Error.Message);
            return result.Error.ExitCode;
        }

        SymbolsInResolution resolution = result.Resolution!;
        ConsoleJsonWriter.Write(new SymbolsInResult(
            File: resolution.File,
            Line: resolution.Line,
            StartColumn: resolution.StartColumn,
            EndColumn: resolution.EndColumn,
            Project: appliedProjectFilter is null ? null : ProjectFilterOutput.FromAppliedFilter(appliedProjectFilter),
            ExcludeGenerated: excludeGenerated,
            Symbols: resolution.Symbols.Select(symbol => new SymbolsInSymbolResult(
                Name: symbol.Name,
                Kind: symbol.Kind,
                Container: symbol.Container,
                Facts: symbol.Facts,
                Line: symbol.Line,
                Column: symbol.Column,
                EndLine: symbol.EndLine,
                EndColumn: symbol.EndColumn)).ToArray()));

        return ExitCodes.Success;
    }

    private sealed record SymbolsInResult(
        string File,
        int Line,
        int StartColumn,
        int EndColumn,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        ProjectFilterOutput? Project,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool ExcludeGenerated,
        IReadOnlyList<SymbolsInSymbolResult> Symbols);

    private sealed record SymbolsInSymbolResult(
        string Name,
        string Kind,
        string? Container,
        SymbolFacts Facts,
        int Line,
        int Column,
        int EndLine,
        int EndColumn);
}

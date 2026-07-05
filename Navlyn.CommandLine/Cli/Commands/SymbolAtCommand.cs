using System.CommandLine;
using System.Text.Json.Serialization;
using Navlyn.Diagnostics;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class SymbolAtCommand
{
    public static Command Create()
    {
        return SourcePositionCommand.Create(
            "symbol-at",
            "Resolve the C# or Visual Basic symbol at a source position.",
            ExecuteAsync);
    }

    private static async Task<int> ExecuteAsync(
        LoadedWorkspace loadedWorkspace,
        SourcePositionOptions options,
        CancellationToken cancellationToken)
    {
        SymbolAtResolutionResult result = await new SymbolAtResolver().ResolveAsync(
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

        SymbolAtResolution resolution = result.Resolution!;
        ConsoleJsonWriter.Write(new SymbolAtResult(
            File: resolution.File,
            Line: resolution.Line,
            Column: resolution.Column,
            Project: options.ProjectFilter is null ? null : ProjectFilterOutput.FromAppliedFilter(options.ProjectFilter),
            SelectionInput: options.SelectionInput,
            ExcludeGenerated: options.ExcludeGenerated,
            Symbol: new SymbolAtSymbolResult(
                Name: resolution.Symbol.Name,
                Kind: resolution.Symbol.Kind,
                Container: resolution.Symbol.Container,
                Facts: resolution.Symbol.Facts,
                Path: resolution.Symbol.Path,
                Line: resolution.Symbol.Line,
                Column: resolution.Symbol.Column,
                EndLine: resolution.Symbol.EndLine,
                EndColumn: resolution.Symbol.EndColumn)));

        return ExitCodes.Success;
    }

    private sealed record SymbolAtResult(
        string File,
        int Line,
        int Column,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        ProjectFilterOutput? Project,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        CandidateSelectionInput? SelectionInput,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool ExcludeGenerated,
        SymbolAtSymbolResult Symbol);

    private sealed record SymbolAtSymbolResult(
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

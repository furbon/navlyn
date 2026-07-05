using System.CommandLine;
using System.Text.Json.Serialization;
using Navlyn.Diagnostics;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class DefinitionCommand
{
    public static Command Create()
    {
        Option<bool> includeMetadataOption = SharedOptions.CreateIncludeMetadataOption();

        return SourcePositionCommand.Create(
            "definition",
            "Resolve source definitions for the C# or Visual Basic symbol at a source position.",
            [includeMetadataOption],
            (workspace, options, parseResult, cancellationToken) => ExecuteAsync(
                workspace,
                options,
                parseResult.GetValue(includeMetadataOption),
                cancellationToken));
    }

    private static async Task<int> ExecuteAsync(
        LoadedWorkspace loadedWorkspace,
        SourcePositionOptions options,
        bool includeMetadata,
        CancellationToken cancellationToken)
    {
        DefinitionResolutionResult result = await new DefinitionResolver().ResolveAsync(
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

        DefinitionResolution resolution = result.Resolution!;
        ConsoleJsonWriter.Write(new DefinitionResult(
            File: resolution.File,
            Line: resolution.Line,
            Column: resolution.Column,
            Project: options.ProjectFilter is null ? null : ProjectFilterOutput.FromAppliedFilter(options.ProjectFilter),
            SelectionInput: options.SelectionInput,
            ExcludeGenerated: options.ExcludeGenerated,
            IncludeMetadata: includeMetadata,
            Symbol: new DefinitionSymbolResult(
                Name: resolution.Symbol.Name,
                Kind: resolution.Symbol.Kind,
                Container: resolution.Symbol.Container,
                Facts: resolution.Symbol.Facts),
            Definitions: resolution.Definitions.Select(definition => new DefinitionLocationResult(
                Path: definition.Path,
                Line: definition.Line,
                Column: definition.Column,
                EndLine: definition.EndLine,
                EndColumn: definition.EndColumn)).ToArray()));

        return ExitCodes.Success;
    }

    private sealed record DefinitionResult(
        string File,
        int Line,
        int Column,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        ProjectFilterOutput? Project,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        CandidateSelectionInput? SelectionInput,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool ExcludeGenerated,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool IncludeMetadata,
        DefinitionSymbolResult Symbol,
        IReadOnlyList<DefinitionLocationResult> Definitions);

    private sealed record DefinitionSymbolResult(string Name, string Kind, string? Container, SymbolFacts Facts);

    private sealed record DefinitionLocationResult(string Path, int Line, int Column, int EndLine, int EndColumn);
}

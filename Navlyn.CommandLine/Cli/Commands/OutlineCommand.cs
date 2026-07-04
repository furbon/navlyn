using System.CommandLine;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Navlyn.Diagnostics;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class OutlineCommand
{
    public static Command Create()
    {
        Option<FileInfo> fileOption = SharedOptions.CreateFileOption();
        Option<string?> projectOption = SharedOptions.CreateProjectFilterOption();
        Option<bool> excludeGeneratedOption = SharedOptions.CreateExcludeGeneratedOption();

        return WorkspaceCommand.Create(
            "outline",
            "Return a semantic outline for a C# source file.",
            [fileOption, projectOption, excludeGeneratedOption],
            (workspace, parseResult, cancellationToken) => ExecuteAsync(
                workspace,
                parseResult.GetValue(fileOption)!,
                parseResult.GetValue(projectOption),
                parseResult.GetValue(excludeGeneratedOption),
                cancellationToken));
    }

    private static async Task<int> ExecuteAsync(
        LoadedWorkspace loadedWorkspace,
        FileInfo file,
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

        OutlineResolutionResult result = await new OutlineResolver().ResolveAsync(
            loadedWorkspace.Solution,
            file,
            project,
            excludeGenerated,
            cancellationToken);

        if (result.Error is not null)
        {
            DiagnosticReporter.WriteError(result.Error.DiagnosticId, result.Error.Message);
            return result.Error.ExitCode;
        }

        OutlineResolution resolution = result.Resolution!;
        ConsoleJsonWriter.Write(new OutlineResult(
            File: resolution.File,
            Project: appliedProjectFilter is null ? null : ProjectFilterOutput.FromAppliedFilter(appliedProjectFilter),
            ExcludeGenerated: excludeGenerated,
            Entries: resolution.Entries.Select(entry => new OutlineEntryResult(
                Name: entry.Name,
                Kind: entry.Kind,
                Container: entry.Container,
                Facts: entry.Facts,
                CandidateId: entry.CandidateId,
                Path: entry.Path,
                Line: entry.Line,
                Column: entry.Column,
                EndLine: entry.EndLine,
                EndColumn: entry.EndColumn)).ToArray()));

        return ExitCodes.Success;
    }

    private sealed record OutlineResult(
        string File,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        ProjectFilterOutput? Project,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool ExcludeGenerated,
        IReadOnlyList<OutlineEntryResult> Entries);

    private sealed record OutlineEntryResult(
        string Name,
        string Kind,
        string? Container,
        SymbolFacts Facts,
        string CandidateId,
        string Path,
        int Line,
        int Column,
        int EndLine,
        int EndColumn);
}

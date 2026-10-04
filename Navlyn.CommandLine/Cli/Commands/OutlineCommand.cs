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
        Option<int?> entryLimitOption = new("--entry-limit") { Description = "Page size, 1..1000. Omit for all entries." };
        Option<int> entryOffsetOption = new("--entry-offset") { Description = "Nonnegative entry offset in unchanged source/context." };

        return WorkspaceCommand.Create(
            "outline",
            "Return a semantic outline for a C# or Visual Basic source file.",
            [fileOption, projectOption, excludeGeneratedOption, entryLimitOption, entryOffsetOption],
            (workspace, parseResult, cancellationToken) => ExecuteAsync(
                workspace,
                parseResult.GetValue(fileOption)!,
                parseResult.GetValue(projectOption),
                parseResult.GetValue(excludeGeneratedOption),
                parseResult.GetValue(entryLimitOption),
                parseResult.GetValue(entryOffsetOption),
                cancellationToken));
    }

    private static async Task<int> ExecuteAsync(
        LoadedWorkspace loadedWorkspace,
        FileInfo file,
        string? projectFilter,
        bool excludeGenerated,
        int? entryLimit,
        int entryOffset,
        CancellationToken cancellationToken)
    {
        if (entryLimit is < 1 or > 1000 || entryOffset < 0)
        {
            DiagnosticReporter.WriteError(DiagnosticIds.InvalidLimit, "entry-limit must be 1..1000 and entry-offset must be nonnegative.");
            return ExitCodes.UsageError;
        }
        OutlinePage? page = entryLimit is not null || entryOffset != 0
            ? new OutlinePage(entryOffset, entryLimit ?? int.MaxValue) : null;
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
            cancellationToken,
            page);

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
                EndColumn: entry.EndColumn)).ToArray(),
            EntriesTotal: page is null ? null : resolution.EntriesTotal,
            EntryOffset: page?.Offset,
            EntriesTruncated: page is null ? null : page.Offset > 0 || resolution.Entries.Count < resolution.EntriesTotal,
            NextEntryOffset: page is not null && (long)page.Offset + page.Limit < resolution.EntriesTotal
                ? page.Offset + page.Limit : null));

        return ExitCodes.Success;
    }

    private sealed record OutlineResult(
        string File,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        ProjectFilterOutput? Project,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool ExcludeGenerated,
        IReadOnlyList<OutlineEntryResult> Entries,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? EntriesTotal,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? EntryOffset,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? EntriesTruncated,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? NextEntryOffset);

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

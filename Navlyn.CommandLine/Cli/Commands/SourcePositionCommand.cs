using System.CommandLine;
using System.CommandLine.Parsing;
using Microsoft.CodeAnalysis;
using Navlyn.Diagnostics;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class SourcePositionCommand
{
    public static Command Create(
        string name,
        string description,
        Func<LoadedWorkspace, SourcePositionOptions, CancellationToken, Task<int>> executeAsync)
    {
        return Create(
            name,
            description,
            [],
            (workspace, options, _, cancellationToken) => executeAsync(workspace, options, cancellationToken));
    }

    public static Command Create(
        string name,
        string description,
        IReadOnlyList<Option> additionalOptions,
        Func<LoadedWorkspace, SourcePositionOptions, ParseResult, CancellationToken, Task<int>> executeAsync)
    {
        Option<FileInfo?> fileOption = CreateOptionalFileOption();
        Option<int?> lineOption = CreateOptionalLineOption();
        Option<int?> columnOption = CreateOptionalColumnOption();
        Option<string?> candidateIdOption = FuzzyCommandSupport.CreateCandidateIdOption();
        Option<string?> projectOption = SharedOptions.CreateProjectFilterOption();
        Option<bool> excludeGeneratedOption = SharedOptions.CreateExcludeGeneratedOption();

        return WorkspaceCommand.Create(
            name,
            description,
            [fileOption, lineOption, columnOption, candidateIdOption, projectOption, excludeGeneratedOption, .. additionalOptions],
            async (workspace, parseResult, cancellationToken) =>
            {
                FileInfo? file = parseResult.GetValue(fileOption);
                int? line = parseResult.GetValue(lineOption);
                int? column = parseResult.GetValue(columnOption);
                string? candidateId = parseResult.GetValue(candidateIdOption);
                bool hasCandidateId = !string.IsNullOrWhiteSpace(candidateId);
                bool hasAnySourcePosition = file is not null || line is not null || column is not null;
                bool hasCompleteSourcePosition = file is not null && line is not null && column is not null;
                if (hasCandidateId && hasAnySourcePosition || !hasCandidateId && !hasCompleteSourcePosition)
                {
                    DiagnosticReporter.WriteError(
                        DiagnosticIds.ParseError,
                        "Specify exactly one source-position input: --candidate-id or --file with --line and --column.");
                    return ExitCodes.UsageError;
                }

                if (!ProjectFilterCommand.TryResolveSingleProject(
                    workspace,
                    parseResult.GetValue(projectOption),
                    out Project? project,
                    out AppliedProjectFilter? appliedProjectFilter,
                    out int exitCode))
                {
                    return exitCode;
                }

                CandidateSelectionInput? selectionInput = null;
                if (hasCandidateId)
                {
                    IReadOnlyList<Project> projects = project is null
                        ? workspace.Solution.Projects.ToArray()
                        : [project];
                    CandidateTargetResolutionResult targetResult = await new CandidateTargetResolver().ResolveAsync(
                        workspace.Solution,
                        projects,
                        candidateId!,
                        parseResult.GetValue(excludeGeneratedOption),
                        cancellationToken);

                    if (targetResult.Error is not null)
                    {
                        DiagnosticReporter.WriteError(targetResult.Error.DiagnosticId, targetResult.Error.Message);
                        return targetResult.Error.ExitCode;
                    }

                    CandidateTargetResolution target = targetResult.Resolution!;
                    file = target.File;
                    line = target.Line;
                    column = target.Column;
                    project ??= target.Project;
                    selectionInput = new CandidateSelectionInput("candidateId", target.CandidateId);
                }

                return await executeAsync(
                    workspace,
                    new SourcePositionOptions(
                        File: file!,
                        Line: line!.Value,
                        Column: column!.Value,
                        Project: project,
                        ProjectFilter: appliedProjectFilter,
                        ExcludeGenerated: parseResult.GetValue(excludeGeneratedOption),
                        SelectionInput: selectionInput),
                    parseResult,
                    cancellationToken);
            });
    }

    private static Option<FileInfo?> CreateOptionalFileOption()
    {
        return new Option<FileInfo?>("--file")
        {
            Description = "Path to a C# source file in the workspace."
        };
    }

    private static Option<int?> CreateOptionalLineOption()
    {
        return new Option<int?>("--line")
        {
            Description = "1-based source line."
        };
    }

    private static Option<int?> CreateOptionalColumnOption()
    {
        return new Option<int?>("--column")
        {
            Description = "1-based source column."
        };
    }
}

internal sealed record SourcePositionOptions(
    FileInfo File,
    int Line,
    int Column,
    Project? Project,
    AppliedProjectFilter? ProjectFilter,
    bool ExcludeGenerated,
    CandidateSelectionInput? SelectionInput);

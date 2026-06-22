using System.CommandLine;
using System.CommandLine.Parsing;
using Microsoft.CodeAnalysis;
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
        Option<FileInfo> fileOption = SharedOptions.CreateFileOption();
        Option<int> lineOption = SharedOptions.CreateLineOption();
        Option<int> columnOption = SharedOptions.CreateColumnOption();
        Option<string?> projectOption = SharedOptions.CreateProjectFilterOption();
        Option<bool> excludeGeneratedOption = SharedOptions.CreateExcludeGeneratedOption();

        return WorkspaceCommand.Create(
            name,
            description,
            [fileOption, lineOption, columnOption, projectOption, excludeGeneratedOption, .. additionalOptions],
            (workspace, parseResult, cancellationToken) =>
            {
                if (!ProjectFilterCommand.TryResolveSingleProject(
                    workspace,
                    parseResult.GetValue(projectOption),
                    out Project? project,
                    out AppliedProjectFilter? appliedProjectFilter,
                    out int exitCode))
                {
                    return Task.FromResult(exitCode);
                }

                return executeAsync(
                    workspace,
                    new SourcePositionOptions(
                        File: parseResult.GetValue(fileOption)!,
                        Line: parseResult.GetValue(lineOption),
                        Column: parseResult.GetValue(columnOption),
                        Project: project,
                        ProjectFilter: appliedProjectFilter,
                        ExcludeGenerated: parseResult.GetValue(excludeGeneratedOption)),
                    parseResult,
                    cancellationToken);
            });
    }
}

internal sealed record SourcePositionOptions(
    FileInfo File,
    int Line,
    int Column,
    Project? Project,
    AppliedProjectFilter? ProjectFilter,
    bool ExcludeGenerated);

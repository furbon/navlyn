using System.CommandLine;
using Microsoft.CodeAnalysis;
using Navlyn.Diagnostics;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class ResolveTargetCommand
{
    public static Command Create(string commandName = "resolve-target", string? description = null)
    {
        Option<string?> queryOption = new("--query")
        {
            Description = "Approximate symbol query."
        };
        Option<string?> candidateIdOption = FuzzyCommandSupport.CreateCandidateIdOption();
        Option<FileInfo?> fileOption = new("--file")
        {
            Description = "Path to a C# or Visual Basic source file in the workspace."
        };
        Option<int?> lineOption = new("--line")
        {
            Description = "1-based source line."
        };
        Option<int?> columnOption = new("--column")
        {
            Description = "1-based source column."
        };
        Option<string[]> assumeKindOption = FuzzyCommandSupport.CreateAssumeKindOption();
        Option<string> matchOption = FuzzyCommandSupport.CreateMatchOption();
        Option<bool> caseSensitiveOption = SharedOptions.CreateCaseSensitiveOption();
        Option<string[]> projectOption = SharedOptions.CreateProjectFiltersOption();
        Option<bool> excludeGeneratedOption = SharedOptions.CreateExcludeGeneratedOption();
        Option<int?> limitOption = SharedOptions.CreateLimitOption();
        Option<string> candidatePolicyOption = FuzzyCommandSupport.CreateCandidatePolicyOption("select");
        Option<string> minConfidenceOption = FuzzyCommandSupport.CreateMinConfidenceOption("medium");
        Option<bool> explainSelectionOption = FuzzyCommandSupport.CreateExplainSelectionOption();

        return WorkspaceCommand.Create(
            commandName,
            description ?? "Resolve an approximate, candidate-id, or source-position input into a small target envelope.",
            [queryOption, candidateIdOption, fileOption, lineOption, columnOption, assumeKindOption, matchOption, caseSensitiveOption, projectOption, excludeGeneratedOption, limitOption, candidatePolicyOption, minConfidenceOption, explainSelectionOption],
            (workspace, parseResult, cancellationToken) => ExecuteAsync(
                commandName,
                workspace,
                parseResult.GetValue(queryOption),
                parseResult.GetValue(candidateIdOption),
                parseResult.GetValue(fileOption),
                parseResult.GetValue(lineOption),
                parseResult.GetValue(columnOption),
                parseResult.GetValue(assumeKindOption) ?? [],
                parseResult.GetValue(matchOption)!,
                parseResult.GetValue(caseSensitiveOption),
                parseResult.GetValue(projectOption) ?? [],
                parseResult.GetValue(excludeGeneratedOption),
                parseResult.GetValue(limitOption),
                parseResult.GetValue(candidatePolicyOption)!,
                parseResult.GetValue(minConfidenceOption)!,
                parseResult.GetValue(explainSelectionOption),
                cancellationToken));
    }

    private static async Task<int> ExecuteAsync(
        string commandName,
        LoadedWorkspace loadedWorkspace,
        string? query,
        string? candidateId,
        FileInfo? file,
        int? line,
        int? column,
        IReadOnlyList<string> assumeKinds,
        string match,
        bool caseSensitive,
        IReadOnlyList<string> projectFilters,
        bool excludeGenerated,
        int? limit,
        string candidatePolicy,
        string minConfidence,
        bool explainSelection,
        CancellationToken cancellationToken)
    {
        bool hasQuery = !string.IsNullOrWhiteSpace(query);
        bool hasCandidateId = !string.IsNullOrWhiteSpace(candidateId);
        bool hasAnySourcePosition = file is not null || line is not null || column is not null;
        bool hasCompleteSourcePosition = file is not null && line is not null && column is not null;
        int inputModeCount = (hasQuery ? 1 : 0) + (hasCandidateId ? 1 : 0) + (hasAnySourcePosition ? 1 : 0);
        if (inputModeCount != 1 || hasAnySourcePosition && !hasCompleteSourcePosition)
        {
            DiagnosticReporter.WriteError(
                DiagnosticIds.ParseError,
                "Specify exactly one resolve-target input mode: --query, --candidate-id, or --file with --line and --column.");
            return ExitCodes.UsageError;
        }

        if (hasAnySourcePosition)
        {
            if (assumeKinds.Count > 0 || match != "smart" || caseSensitive || limit is not null || candidatePolicy != "select" || minConfidence != "medium" || explainSelection)
            {
                DiagnosticReporter.WriteError(
                    DiagnosticIds.ParseError,
                    "Source-position mode cannot be combined with fuzzy query options.");
                return ExitCodes.UsageError;
            }

            if (projectFilters.Count > 1)
            {
                DiagnosticReporter.WriteError(DiagnosticIds.ParseError, "Source-position mode accepts at most one --project filter.");
                return ExitCodes.UsageError;
            }

            string? projectFilter = projectFilters.Count == 0 ? null : projectFilters[0];
            if (!ProjectFilterCommand.TryResolveSingleProject(
                loadedWorkspace,
                projectFilter,
                out Project? project,
                out _,
                out int sourceExitCode))
            {
                return sourceExitCode;
            }

            ResolveTargetResult sourceResult = await new ResolveTargetResolver().ResolveSourcePositionAsync(
                loadedWorkspace,
                file!,
                line!.Value,
                column!.Value,
                project,
                excludeGenerated,
                cancellationToken);

            if (sourceResult.SelectedTarget is null)
            {
                DiagnosticReporter.WriteError(DiagnosticIds.SymbolNotFoundAtPosition, sourceResult.Warnings.FirstOrDefault() ?? "No symbol was found at the source position.");
                return ExitCodes.UsageError;
            }

            ConsoleJsonWriter.Write(sourceResult with { Command = commandName });
            return ExitCodes.Success;
        }

        if (!FuzzyCommandSupport.TryCreateSelection(
            loadedWorkspace,
            query,
            candidateId,
            assumeKinds,
            match,
            caseSensitive,
            projectFilters,
            excludeGenerated,
            limit,
            candidatePolicy,
            minConfidence,
            explainSelection,
            allowGroupPolicy: false,
            out FuzzyQueryOptions options,
            out IReadOnlyList<Project> projects,
            out IReadOnlyList<FuzzyProjectFilter>? projectOutputs,
            out int exitCode))
        {
            return exitCode;
        }

        FuzzyDiscoveryResolver fuzzyResolver = new();
        if (!await FuzzyCommandSupport.TryValidateSelectionAsync(fuzzyResolver, projects, options, cancellationToken))
        {
            return ExitCodes.UsageError;
        }

        ResolveTargetResult result = await new ResolveTargetResolver().ResolveFuzzyAsync(
            loadedWorkspace,
            options,
            projects,
            projectOutputs,
            cancellationToken);

        ConsoleJsonWriter.Write(result with { Command = commandName });
        return ExitCodes.Success;
    }
}

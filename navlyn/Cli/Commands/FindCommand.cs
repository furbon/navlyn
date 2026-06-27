using System.CommandLine;
using Microsoft.CodeAnalysis;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class FindCommand
{
    public static Command Create()
    {
        Option<string> queryOption = SharedOptions.CreateQueryOption();
        Option<string[]> assumeKindOption = FuzzyCommandSupport.CreateAssumeKindOption();
        Option<string> matchOption = FuzzyCommandSupport.CreateMatchOption();
        Option<bool> caseSensitiveOption = SharedOptions.CreateCaseSensitiveOption();
        Option<string[]> projectOption = SharedOptions.CreateProjectFiltersOption();
        Option<bool> excludeGeneratedOption = SharedOptions.CreateExcludeGeneratedOption();
        Option<int?> limitOption = SharedOptions.CreateLimitOption();
        Option<string> candidatePolicyOption = FuzzyCommandSupport.CreateCandidatePolicyOption("group");
        Option<string> minConfidenceOption = FuzzyCommandSupport.CreateMinConfidenceOption("low");
        Option<bool> explainSelectionOption = FuzzyCommandSupport.CreateExplainSelectionOption();

        return WorkspaceCommand.Create(
            "find",
            "Find source symbols that plausibly match a fuzzy query.",
            [queryOption, assumeKindOption, matchOption, caseSensitiveOption, projectOption, excludeGeneratedOption, limitOption, candidatePolicyOption, minConfidenceOption, explainSelectionOption],
            (workspace, parseResult, cancellationToken) => ExecuteAsync(
                workspace,
                parseResult.GetValue(queryOption)!,
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
        LoadedWorkspace loadedWorkspace,
        string query,
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
        if (!FuzzyCommandSupport.TryCreateSelection(
            loadedWorkspace,
            query,
            candidateId: null,
            assumeKinds,
            match,
            caseSensitive,
            projectFilters,
            excludeGenerated,
            limit,
            candidatePolicy,
            minConfidence,
            explainSelection,
            allowGroupPolicy: true,
            out FuzzyQueryOptions options,
            out IReadOnlyList<Project> projects,
            out IReadOnlyList<FuzzyProjectFilter>? projectOutputs,
            out int exitCode))
        {
            return exitCode;
        }

        FuzzyFindResult result = await new FuzzyDiscoveryResolver().FindAsync(
            loadedWorkspace,
            options,
            projects,
            projectOutputs,
            cancellationToken);

        ConsoleJsonWriter.Write(result);
        return ExitCodes.Success;
    }
}

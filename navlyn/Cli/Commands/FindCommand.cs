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

        return WorkspaceCommand.Create(
            "find",
            "Find source symbols that plausibly match a fuzzy query.",
            [queryOption, assumeKindOption, matchOption, caseSensitiveOption, projectOption, excludeGeneratedOption, limitOption],
            (workspace, parseResult, cancellationToken) => ExecuteAsync(
                workspace,
                parseResult.GetValue(queryOption)!,
                parseResult.GetValue(assumeKindOption) ?? [],
                parseResult.GetValue(matchOption)!,
                parseResult.GetValue(caseSensitiveOption),
                parseResult.GetValue(projectOption) ?? [],
                parseResult.GetValue(excludeGeneratedOption),
                parseResult.GetValue(limitOption),
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
        CancellationToken cancellationToken)
    {
        if (!FuzzyCommandSupport.TryCreateQuery(
            loadedWorkspace,
            query,
            assumeKinds,
            match,
            caseSensitive,
            projectFilters,
            excludeGenerated,
            limit,
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

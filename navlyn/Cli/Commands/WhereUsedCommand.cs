using System.CommandLine;
using Microsoft.CodeAnalysis;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class WhereUsedCommand
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
        Option<bool> includeSnippetsOption = FuzzyCommandSupport.CreateIncludeSnippetsOption();
        Option<int?> snippetLinesOption = FuzzyCommandSupport.CreateSnippetLinesOption();

        return WorkspaceCommand.Create(
            "where-used",
            "Resolve a fuzzy symbol query and list source references.",
            [queryOption, assumeKindOption, matchOption, caseSensitiveOption, projectOption, excludeGeneratedOption, limitOption, includeSnippetsOption, snippetLinesOption],
            (workspace, parseResult, cancellationToken) => ExecuteAsync(
                workspace,
                parseResult.GetValue(queryOption)!,
                parseResult.GetValue(assumeKindOption) ?? [],
                parseResult.GetValue(matchOption)!,
                parseResult.GetValue(caseSensitiveOption),
                parseResult.GetValue(projectOption) ?? [],
                parseResult.GetValue(excludeGeneratedOption),
                parseResult.GetValue(limitOption),
                parseResult.GetValue(includeSnippetsOption),
                parseResult.GetValue(snippetLinesOption),
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
        bool includeSnippets,
        int? snippetLines,
        CancellationToken cancellationToken)
    {
        int referenceLimit = limit ?? FuzzyDiscoveryResolver.DefaultReferenceLimit;
        int snippetContext = snippetLines ?? FuzzyDiscoveryResolver.DefaultSnippetLines;
        if (!FuzzyCommandSupport.TryCreatePositiveOption("--limit", referenceLimit, out int exitCode) ||
            !FuzzyCommandSupport.TryCreateNonNegativeOption("--snippet-lines", snippetContext, out exitCode))
        {
            return exitCode;
        }

        if (!FuzzyCommandSupport.TryCreateQuery(
            loadedWorkspace,
            query,
            assumeKinds,
            match,
            caseSensitive,
            projectFilters,
            excludeGenerated,
            limit: null,
            out FuzzyQueryOptions options,
            out IReadOnlyList<Project> projects,
            out IReadOnlyList<FuzzyProjectFilter>? projectOutputs,
            out exitCode))
        {
            return exitCode;
        }

        FuzzyWhereUsedResult result = await new FuzzyDiscoveryResolver().WhereUsedAsync(
            loadedWorkspace,
            options,
            new FuzzyLocationOptions(referenceLimit, FuzzyDiscoveryResolver.DefaultReferenceFileLimit, includeSnippets, snippetContext, excludeGenerated),
            projects,
            projectOutputs,
            cancellationToken);

        ConsoleJsonWriter.Write(result);
        return ExitCodes.Success;
    }
}

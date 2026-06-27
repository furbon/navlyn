using System.CommandLine;
using Microsoft.CodeAnalysis;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class WhereUsedCommand
{
    public static Command Create()
    {
        Option<string?> queryOption = new("--query")
        {
            Description = "Symbol name query."
        };
        Option<string?> candidateIdOption = FuzzyCommandSupport.CreateCandidateIdOption();
        Option<string[]> assumeKindOption = FuzzyCommandSupport.CreateAssumeKindOption();
        Option<string> matchOption = FuzzyCommandSupport.CreateMatchOption();
        Option<bool> caseSensitiveOption = SharedOptions.CreateCaseSensitiveOption();
        Option<string[]> projectOption = SharedOptions.CreateProjectFiltersOption();
        Option<bool> excludeGeneratedOption = SharedOptions.CreateExcludeGeneratedOption();
        Option<int?> limitOption = SharedOptions.CreateLimitOption();
        Option<bool> includeSnippetsOption = FuzzyCommandSupport.CreateIncludeSnippetsOption();
        Option<int?> snippetLinesOption = FuzzyCommandSupport.CreateSnippetLinesOption();
        Option<string> candidatePolicyOption = FuzzyCommandSupport.CreateCandidatePolicyOption("group");
        Option<string> minConfidenceOption = FuzzyCommandSupport.CreateMinConfidenceOption("medium");
        Option<bool> explainSelectionOption = FuzzyCommandSupport.CreateExplainSelectionOption();

        return WorkspaceCommand.Create(
            "where-used",
            "Resolve a fuzzy symbol query and list source references.",
            [queryOption, candidateIdOption, assumeKindOption, matchOption, caseSensitiveOption, projectOption, excludeGeneratedOption, limitOption, includeSnippetsOption, snippetLinesOption, candidatePolicyOption, minConfidenceOption, explainSelectionOption],
            (workspace, parseResult, cancellationToken) => ExecuteAsync(
                workspace,
                parseResult.GetValue(queryOption),
                parseResult.GetValue(candidateIdOption),
                parseResult.GetValue(assumeKindOption) ?? [],
                parseResult.GetValue(matchOption)!,
                parseResult.GetValue(caseSensitiveOption),
                parseResult.GetValue(projectOption) ?? [],
                parseResult.GetValue(excludeGeneratedOption),
                parseResult.GetValue(limitOption),
                parseResult.GetValue(includeSnippetsOption),
                parseResult.GetValue(snippetLinesOption),
                parseResult.GetValue(candidatePolicyOption)!,
                parseResult.GetValue(minConfidenceOption)!,
                parseResult.GetValue(explainSelectionOption),
                cancellationToken));
    }

    private static async Task<int> ExecuteAsync(
        LoadedWorkspace loadedWorkspace,
        string? query,
        string? candidateId,
        IReadOnlyList<string> assumeKinds,
        string match,
        bool caseSensitive,
        IReadOnlyList<string> projectFilters,
        bool excludeGenerated,
        int? limit,
        bool includeSnippets,
        int? snippetLines,
        string candidatePolicy,
        string minConfidence,
        bool explainSelection,
        CancellationToken cancellationToken)
    {
        int referenceLimit = limit ?? FuzzyDiscoveryResolver.DefaultReferenceLimit;
        int snippetContext = snippetLines ?? FuzzyDiscoveryResolver.DefaultSnippetLines;
        if (!FuzzyCommandSupport.TryCreatePositiveOption("--limit", referenceLimit, out int exitCode) ||
            !FuzzyCommandSupport.TryCreateNonNegativeOption("--snippet-lines", snippetContext, out exitCode))
        {
            return exitCode;
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
            limit: null,
            candidatePolicy,
            minConfidence,
            explainSelection,
            allowGroupPolicy: true,
            out FuzzyQueryOptions options,
            out IReadOnlyList<Project> projects,
            out IReadOnlyList<FuzzyProjectFilter>? projectOutputs,
            out exitCode))
        {
            return exitCode;
        }

        FuzzyDiscoveryResolver resolver = new();
        if (!await FuzzyCommandSupport.TryValidateSelectionAsync(resolver, projects, options, cancellationToken))
        {
            return ExitCodes.UsageError;
        }

        FuzzyWhereUsedResult result = await resolver.WhereUsedAsync(
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

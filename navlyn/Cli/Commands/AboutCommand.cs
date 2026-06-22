using System.CommandLine;
using Microsoft.CodeAnalysis;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class AboutCommand
{
    public static Command Create()
    {
        Option<string> queryOption = SharedOptions.CreateQueryOption();
        Option<string[]> assumeKindOption = FuzzyCommandSupport.CreateAssumeKindOption();
        Option<string> matchOption = FuzzyCommandSupport.CreateMatchOption();
        Option<bool> caseSensitiveOption = SharedOptions.CreateCaseSensitiveOption();
        Option<string[]> projectOption = SharedOptions.CreateProjectFiltersOption();
        Option<bool> excludeGeneratedOption = SharedOptions.CreateExcludeGeneratedOption();
        Option<int?> memberLimitOption = new("--member-limit")
        {
            Description = "Maximum number of member outline entries to return."
        };
        Option<int?> referenceLimitOption = new("--reference-limit")
        {
            Description = "Maximum number of reference locations to return."
        };
        Option<int?> relationLimitOption = new("--relation-limit")
        {
            Description = "Maximum number of relation entries per relation kind to return."
        };
        Option<bool> includeSnippetsOption = FuzzyCommandSupport.CreateIncludeSnippetsOption();
        Option<int?> snippetLinesOption = FuzzyCommandSupport.CreateSnippetLinesOption();

        return WorkspaceCommand.Create(
            "about",
            "Resolve a fuzzy symbol query and return a compact semantic summary.",
            [queryOption, assumeKindOption, matchOption, caseSensitiveOption, projectOption, excludeGeneratedOption, memberLimitOption, referenceLimitOption, relationLimitOption, includeSnippetsOption, snippetLinesOption],
            (workspace, parseResult, cancellationToken) => ExecuteAsync(
                workspace,
                parseResult.GetValue(queryOption)!,
                parseResult.GetValue(assumeKindOption) ?? [],
                parseResult.GetValue(matchOption)!,
                parseResult.GetValue(caseSensitiveOption),
                parseResult.GetValue(projectOption) ?? [],
                parseResult.GetValue(excludeGeneratedOption),
                parseResult.GetValue(memberLimitOption),
                parseResult.GetValue(referenceLimitOption),
                parseResult.GetValue(relationLimitOption),
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
        int? memberLimit,
        int? referenceLimit,
        int? relationLimit,
        bool includeSnippets,
        int? snippetLines,
        CancellationToken cancellationToken)
    {
        int effectiveMemberLimit = memberLimit ?? FuzzyDiscoveryResolver.DefaultMemberLimit;
        int effectiveReferenceLimit = referenceLimit ?? FuzzyDiscoveryResolver.DefaultReferenceLimit;
        int effectiveRelationLimit = relationLimit ?? FuzzyDiscoveryResolver.DefaultRelationLimit;
        int snippetContext = snippetLines ?? FuzzyDiscoveryResolver.DefaultSnippetLines;
        if (!FuzzyCommandSupport.TryCreatePositiveOption("--member-limit", effectiveMemberLimit, out int exitCode) ||
            !FuzzyCommandSupport.TryCreatePositiveOption("--reference-limit", effectiveReferenceLimit, out exitCode) ||
            !FuzzyCommandSupport.TryCreatePositiveOption("--relation-limit", effectiveRelationLimit, out exitCode) ||
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

        FuzzyAboutResult result = await new FuzzyDiscoveryResolver().AboutAsync(
            loadedWorkspace,
            options,
            new FuzzyAboutOptions(effectiveMemberLimit, effectiveReferenceLimit, effectiveRelationLimit, includeSnippets, snippetContext, excludeGenerated),
            projects,
            projectOutputs,
            cancellationToken);

        ConsoleJsonWriter.Write(result);
        return ExitCodes.Success;
    }
}

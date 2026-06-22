using System.CommandLine;
using Microsoft.CodeAnalysis;
using Navlyn.Diagnostics;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class RelatedCommand
{
    public static Command Create()
    {
        Option<string> queryOption = SharedOptions.CreateQueryOption();
        Option<string[]> assumeKindOption = FuzzyCommandSupport.CreateAssumeKindOption();
        Option<string> matchOption = FuzzyCommandSupport.CreateMatchOption();
        Option<bool> caseSensitiveOption = SharedOptions.CreateCaseSensitiveOption();
        Option<string[]> projectOption = SharedOptions.CreateProjectFiltersOption();
        Option<bool> excludeGeneratedOption = SharedOptions.CreateExcludeGeneratedOption();
        Option<string?> includeOption = new("--include")
        {
            Description = "Comma-separated include modes: declarations,references,callers,calls,implementations,hierarchy."
        };
        Option<int?> limitOption = SharedOptions.CreateLimitOption();
        Option<bool> includeSnippetsOption = FuzzyCommandSupport.CreateIncludeSnippetsOption();
        Option<int?> snippetLinesOption = FuzzyCommandSupport.CreateSnippetLinesOption();

        return WorkspaceCommand.Create(
            "related",
            "Resolve a fuzzy symbol query and return files likely related to it.",
            [queryOption, assumeKindOption, matchOption, caseSensitiveOption, projectOption, excludeGeneratedOption, includeOption, limitOption, includeSnippetsOption, snippetLinesOption],
            (workspace, parseResult, cancellationToken) => ExecuteAsync(
                workspace,
                parseResult.GetValue(queryOption)!,
                parseResult.GetValue(assumeKindOption) ?? [],
                parseResult.GetValue(matchOption)!,
                parseResult.GetValue(caseSensitiveOption),
                parseResult.GetValue(projectOption) ?? [],
                parseResult.GetValue(excludeGeneratedOption),
                parseResult.GetValue(includeOption),
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
        string? include,
        int? limit,
        bool includeSnippets,
        int? snippetLines,
        CancellationToken cancellationToken)
    {
        int effectiveLimit = limit ?? FuzzyDiscoveryResolver.DefaultFileLimit;
        int snippetContext = snippetLines ?? FuzzyDiscoveryResolver.DefaultSnippetLines;
        if (!FuzzyCommandSupport.TryCreatePositiveOption("--limit", effectiveLimit, out int exitCode) ||
            !FuzzyCommandSupport.TryCreateNonNegativeOption("--snippet-lines", snippetContext, out exitCode))
        {
            return exitCode;
        }

        IReadOnlyList<string> includeModes = FuzzyCommandSupport.ParseInclude(
            include,
            ["declarations", "references", "callers", "calls", "implementations", "hierarchy"]);
        if (!FuzzyCommandSupport.ValidateInclude(includeModes, out string? includeError))
        {
            DiagnosticReporter.WriteError(DiagnosticIds.ParseError, includeError!);
            return ExitCodes.UsageError;
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

        FuzzyFilesResult result = await new FuzzyDiscoveryResolver().FilesAsync(
            loadedWorkspace,
            "related",
            options,
            new FuzzyFilesOptions(includeModes, effectiveLimit, FuzzyDiscoveryResolver.DefaultDepth, includeSnippets, snippetContext, excludeGenerated),
            projects,
            projectOutputs,
            cancellationToken);

        ConsoleJsonWriter.Write(result);
        return ExitCodes.Success;
    }
}

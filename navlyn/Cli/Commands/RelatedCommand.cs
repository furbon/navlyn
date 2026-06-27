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
        Option<string?> includeOption = new("--include")
        {
            Description = "Comma-separated include modes: declarations,references,callers,calls,implementations,hierarchy."
        };
        Option<int?> limitOption = SharedOptions.CreateLimitOption();
        Option<bool> includeSnippetsOption = FuzzyCommandSupport.CreateIncludeSnippetsOption();
        Option<int?> snippetLinesOption = FuzzyCommandSupport.CreateSnippetLinesOption();
        Option<string> candidatePolicyOption = FuzzyCommandSupport.CreateCandidatePolicyOption("fail");
        Option<string> minConfidenceOption = FuzzyCommandSupport.CreateMinConfidenceOption("medium");
        Option<bool> explainSelectionOption = FuzzyCommandSupport.CreateExplainSelectionOption();

        return WorkspaceCommand.Create(
            "related",
            "Resolve a fuzzy symbol query and return files likely related to it.",
            [queryOption, candidateIdOption, assumeKindOption, matchOption, caseSensitiveOption, projectOption, excludeGeneratedOption, includeOption, limitOption, includeSnippetsOption, snippetLinesOption, candidatePolicyOption, minConfidenceOption, explainSelectionOption],
            (workspace, parseResult, cancellationToken) => ExecuteAsync(
                workspace,
                parseResult.GetValue(queryOption),
                parseResult.GetValue(candidateIdOption),
                parseResult.GetValue(assumeKindOption) ?? [],
                parseResult.GetValue(matchOption)!,
                parseResult.GetValue(caseSensitiveOption),
                parseResult.GetValue(projectOption) ?? [],
                parseResult.GetValue(excludeGeneratedOption),
                parseResult.GetValue(includeOption),
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
        string? include,
        int? limit,
        bool includeSnippets,
        int? snippetLines,
        string candidatePolicy,
        string minConfidence,
        bool explainSelection,
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
            allowGroupPolicy: false,
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

        FuzzyFilesResult result = await resolver.FilesAsync(
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

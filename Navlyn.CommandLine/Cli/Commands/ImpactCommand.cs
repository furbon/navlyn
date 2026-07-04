using System.CommandLine;
using Microsoft.CodeAnalysis;
using Navlyn.Diagnostics;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class ImpactCommand
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
            Description = "Comma-separated include modes: references,callers,calls,implementations,hierarchy."
        };
        Option<int?> depthOption = new("--depth")
        {
            Description = "Bounded traversal depth for impact exploration. Defaults to 3."
        };
        Option<int?> limitOption = SharedOptions.CreateLimitOption();
        Option<bool> includeSnippetsOption = FuzzyCommandSupport.CreateIncludeSnippetsOption();
        Option<int?> snippetLinesOption = FuzzyCommandSupport.CreateSnippetLinesOption();
        Option<string> candidatePolicyOption = FuzzyCommandSupport.CreateCandidatePolicyOption("fail");
        Option<string> minConfidenceOption = FuzzyCommandSupport.CreateMinConfidenceOption("medium");
        Option<bool> explainSelectionOption = FuzzyCommandSupport.CreateExplainSelectionOption();
        Option<string> scopeOption = SharedOptions.CreateSearchScopeOption();
        Option<int?> maxDocumentsOption = SharedOptions.CreateMaxDocumentsOption();
        Option<string> profileOption = FuzzyCommandSupport.CreateWorkflowProfileOption();

        return WorkspaceCommand.Create(
            "impact",
            "Resolve a fuzzy symbol query and estimate static source areas affected by changes.",
            [queryOption, candidateIdOption, assumeKindOption, matchOption, caseSensitiveOption, projectOption, excludeGeneratedOption, includeOption, depthOption, limitOption, includeSnippetsOption, snippetLinesOption, scopeOption, maxDocumentsOption, profileOption, candidatePolicyOption, minConfidenceOption, explainSelectionOption],
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
                parseResult.GetValue(depthOption),
                parseResult.GetValue(limitOption),
                parseResult.GetValue(includeSnippetsOption),
                parseResult.GetValue(snippetLinesOption),
                parseResult.GetValue(scopeOption)!,
                parseResult.GetValue(maxDocumentsOption),
                parseResult.GetValue(profileOption)!,
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
        int? depth,
        int? limit,
        bool includeSnippets,
        int? snippetLines,
        string scope,
        int? maxDocuments,
        string profile,
        string candidatePolicy,
        string minConfidence,
        bool explainSelection,
        CancellationToken cancellationToken)
    {
        int effectiveDepth = depth ?? FuzzyDiscoveryResolver.DefaultDepth;
        int effectiveLimit = limit ?? FuzzyDiscoveryResolver.DefaultFileLimit;
        int snippetContext = snippetLines ?? FuzzyDiscoveryResolver.DefaultSnippetLines;
        if (!FuzzyCommandSupport.TryCreateNonNegativeOption("--depth", effectiveDepth, out int exitCode) ||
            !FuzzyCommandSupport.TryCreatePositiveOption("--limit", effectiveLimit, out exitCode) ||
            !FuzzyCommandSupport.TryCreatePositiveOption("--max-documents", maxDocuments ?? SymbolNavigationSearchOptions.DefaultMaxDocuments, out exitCode) ||
            !FuzzyCommandSupport.TryCreateNonNegativeOption("--snippet-lines", snippetContext, out exitCode))
        {
            return exitCode;
        }

        IReadOnlyList<string> defaultInclude = profile == "light"
            ? ["declarations", "calls"]
            : ["references", "callers", "calls", "implementations", "hierarchy"];
        IReadOnlyList<string> includeModes = FuzzyCommandSupport.ParseInclude(
            include,
            defaultInclude);
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
            "impact",
            options,
            new FuzzyFilesOptions(
                includeModes,
                effectiveLimit,
                effectiveDepth,
                includeSnippets,
                snippetContext,
                excludeGenerated,
                SymbolNavigationSearchOptions.Create(scope, maxDocuments),
                profile),
            projects,
            projectOutputs,
            cancellationToken);

        ConsoleJsonWriter.Write(result);
        return ExitCodes.Success;
    }
}

using System.CommandLine;
using Microsoft.CodeAnalysis;
using Navlyn.Diagnostics;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class EntrypointsCommand
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
        Option<int?> depthOption = new("--depth")
        {
            Description = "Maximum caller-chain depth. Defaults to 3."
        };
        Option<int?> limitOption = SharedOptions.CreateLimitOption();
        Option<bool> includeSnippetsOption = FuzzyCommandSupport.CreateIncludeSnippetsOption();
        Option<int?> snippetLinesOption = FuzzyCommandSupport.CreateSnippetLinesOption();
        Option<bool> frameworkAwareOption = new("--framework-aware")
        {
            Description = "Annotate caller chains with framework-aware entrypoint facts."
        };
        Option<string[]> frameworkOption = new("--framework")
        {
            Description = "Framework to inspect when --framework-aware is set: aspnetcore, test, or worker. Can be specified more than once."
        };
        frameworkOption.AllowMultipleArgumentsPerToken = true;
        Option<string> candidatePolicyOption = FuzzyCommandSupport.CreateCandidatePolicyOption("fail");
        Option<string> minConfidenceOption = FuzzyCommandSupport.CreateMinConfidenceOption("medium");
        Option<bool> explainSelectionOption = FuzzyCommandSupport.CreateExplainSelectionOption();

        return WorkspaceCommand.Create(
            "entrypoints",
            "Resolve a fuzzy symbol query and trace bounded static caller chains.",
            [queryOption, candidateIdOption, assumeKindOption, matchOption, caseSensitiveOption, projectOption, excludeGeneratedOption, depthOption, limitOption, includeSnippetsOption, snippetLinesOption, frameworkAwareOption, frameworkOption, candidatePolicyOption, minConfidenceOption, explainSelectionOption],
            (workspace, parseResult, cancellationToken) => ExecuteAsync(
                workspace,
                parseResult.GetValue(queryOption),
                parseResult.GetValue(candidateIdOption),
                parseResult.GetValue(assumeKindOption) ?? [],
                parseResult.GetValue(matchOption)!,
                parseResult.GetValue(caseSensitiveOption),
                parseResult.GetValue(projectOption) ?? [],
                parseResult.GetValue(excludeGeneratedOption),
                parseResult.GetValue(depthOption),
                parseResult.GetValue(limitOption),
                parseResult.GetValue(includeSnippetsOption),
                parseResult.GetValue(snippetLinesOption),
                parseResult.GetValue(frameworkAwareOption),
                parseResult.GetValue(frameworkOption) ?? [],
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
        int? depth,
        int? limit,
        bool includeSnippets,
        int? snippetLines,
        bool frameworkAware,
        IReadOnlyList<string> frameworkFilters,
        string candidatePolicy,
        string minConfidence,
        bool explainSelection,
        CancellationToken cancellationToken)
    {
        int effectiveDepth = depth ?? FuzzyDiscoveryResolver.DefaultDepth;
        int effectiveLimit = limit ?? FuzzyDiscoveryResolver.DefaultEntrypointLimit;
        int snippetContext = snippetLines ?? FuzzyDiscoveryResolver.DefaultSnippetLines;
        if (!FuzzyCommandSupport.TryCreateNonNegativeOption("--depth", effectiveDepth, out int exitCode) ||
            !FuzzyCommandSupport.TryCreatePositiveOption("--limit", effectiveLimit, out exitCode) ||
            !FuzzyCommandSupport.TryCreateNonNegativeOption("--snippet-lines", snippetContext, out exitCode))
        {
            return exitCode;
        }

        IReadOnlyList<string> frameworks = [];
        if (frameworkAware)
        {
            if (!FrameworkEntrypointsCommand.TryNormalizeFrameworks(frameworkFilters, out frameworks))
            {
                return ExitCodes.UsageError;
            }
        }
        else if (frameworkFilters.Count > 0)
        {
            DiagnosticReporter.WriteError(DiagnosticIds.ParseError, "--framework requires --framework-aware.");
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

        FuzzyEntrypointsResult result = await resolver.EntrypointsAsync(
            loadedWorkspace,
            options,
            new FuzzyEntrypointsOptions(effectiveDepth, effectiveLimit, includeSnippets, snippetContext, excludeGenerated, frameworkAware, frameworks),
            projects,
            projectOutputs,
            cancellationToken);

        ConsoleJsonWriter.Write(result);
        return ExitCodes.Success;
    }
}

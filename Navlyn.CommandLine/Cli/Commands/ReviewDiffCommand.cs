using System.CommandLine;
using Navlyn.Cli.OutputProfiles;
using Navlyn.Diffs;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class ReviewDiffCommand
{
    private const int DefaultSymbolLimit = 50;
    private const int DefaultImpactLimit = 100;
    private const int DefaultDiagnosticLimit = 100;
    private const int DefaultRelatedTestLimit = 50;
    private const int DefaultDepth = 2;
    private const int DefaultSnippetLines = 1;

    public static Command Create(string commandName = "review-diff", string? description = null)
    {
        Option<string?> baseOption = DiffCommandSupport.CreateBaseOption();
        Option<string?> headOption = DiffCommandSupport.CreateHeadOption();
        Option<bool> stagedOption = DiffCommandSupport.CreateStagedOption();
        Option<bool> includeUnstagedOption = DiffCommandSupport.CreateIncludeUnstagedOption();
        Option<string[]> projectOption = SharedOptions.CreateProjectFiltersOption();
        Option<bool> excludeGeneratedOption = SharedOptions.CreateExcludeGeneratedOption();
        Option<int?> symbolLimitOption = DiffCommandSupport.CreateSymbolLimitOption(DefaultSymbolLimit);
        Option<int?> impactLimitOption = DiffCommandSupport.CreateImpactLimitOption(DefaultImpactLimit);
        Option<int?> diagnosticLimitOption = DiffCommandSupport.CreateDiagnosticLimitOption(DefaultDiagnosticLimit);
        Option<int?> relatedTestLimitOption = DiffCommandSupport.CreateRelatedTestLimitOption(DefaultRelatedTestLimit);
        Option<int?> depthOption = DiffCommandSupport.CreateDepthOption(DefaultDepth);
        Option<bool> includeSnippetsOption = DiffCommandSupport.CreateIncludeSnippetsOption();
        Option<int?> snippetLinesOption = DiffCommandSupport.CreateSnippetLinesOption();
        Option<string> profileOption = OutputProfile.CreateOption();

        return WorkspaceCommand.Create(
            commandName,
            description ?? "Create a deterministic review facts pack for the current diff.",
            [baseOption, headOption, stagedOption, includeUnstagedOption, projectOption, excludeGeneratedOption, symbolLimitOption, impactLimitOption, diagnosticLimitOption, relatedTestLimitOption, depthOption, includeSnippetsOption, snippetLinesOption, profileOption],
            (workspace, parseResult, cancellationToken) => ExecuteAsync(
                commandName,
                workspace,
                parseResult.GetValue(baseOption),
                parseResult.GetValue(headOption),
                parseResult.GetValue(stagedOption),
                parseResult.GetValue(includeUnstagedOption),
                parseResult.GetValue(projectOption) ?? [],
                parseResult.GetValue(excludeGeneratedOption),
                parseResult.GetValue(symbolLimitOption),
                parseResult.GetValue(impactLimitOption),
                parseResult.GetValue(diagnosticLimitOption),
                parseResult.GetValue(relatedTestLimitOption),
                parseResult.GetValue(depthOption),
                parseResult.GetValue(includeSnippetsOption),
                parseResult.GetValue(snippetLinesOption),
                parseResult.GetValue(profileOption)!,
                cancellationToken));
    }

    private static async Task<int> ExecuteAsync(
        string commandName,
        LoadedWorkspace workspace,
        string? baseRef,
        string? headRef,
        bool staged,
        bool includeUnstaged,
        IReadOnlyList<string> projectFilters,
        bool excludeGenerated,
        int? symbolLimit,
        int? impactLimit,
        int? diagnosticLimit,
        int? relatedTestLimit,
        int? depth,
        bool includeSnippets,
        int? snippetLines,
        string profile,
        CancellationToken cancellationToken)
    {
        int effectiveSymbolLimit = symbolLimit ?? DefaultSymbolLimit;
        int effectiveImpactLimit = impactLimit ?? DefaultImpactLimit;
        int effectiveDiagnosticLimit = diagnosticLimit ?? DefaultDiagnosticLimit;
        int effectiveRelatedTestLimit = relatedTestLimit ?? DefaultRelatedTestLimit;
        int effectiveDepth = depth ?? DefaultDepth;
        int effectiveSnippetLines = snippetLines ?? DefaultSnippetLines;
        if (!DiffCommandSupport.TryCreatePositiveOption("--symbol-limit", effectiveSymbolLimit, out int exitCode) ||
            !DiffCommandSupport.TryCreatePositiveOption("--impact-limit", effectiveImpactLimit, out exitCode) ||
            !DiffCommandSupport.TryCreatePositiveOption("--diagnostic-limit", effectiveDiagnosticLimit, out exitCode) ||
            !DiffCommandSupport.TryCreatePositiveOption("--related-test-limit", effectiveRelatedTestLimit, out exitCode) ||
            !DiffCommandSupport.TryCreateNonNegativeOption("--depth", effectiveDepth, out exitCode) ||
            !DiffCommandSupport.TryCreateNonNegativeOption("--snippet-lines", effectiveSnippetLines, out exitCode) ||
            !DiffCommandSupport.TryCreateRequest(baseRef, headRef, staged, includeUnstaged, out DiffRequest request, out exitCode) ||
            !DiffCommandSupport.TryCreateProjectContext(workspace, projectFilters, out var projects, out var projectOutputs, out exitCode))
        {
            return exitCode;
        }

        DiffWorkflowExecutionResult<ReviewDiffResult> result =
            await new DiffWorkflowResolver().ResolveReviewAsync(
                workspace,
                request,
                projects,
                projectOutputs,
                excludeGenerated,
                effectiveSymbolLimit,
                effectiveImpactLimit,
                effectiveDiagnosticLimit,
                effectiveRelatedTestLimit,
                effectiveDepth,
                includeSnippets,
                effectiveSnippetLines,
                cancellationToken);

        if (result.Error is not null)
        {
            return DiffCommandSupport.WriteError(result.Error);
        }

        ConsoleJsonWriter.Write(OutputProfile.Format(workspace, commandName, profile, result.Result!, new
        {
            baseRef,
            headRef,
            staged,
            includeUnstaged,
            projectFilters,
            excludeGenerated,
            symbolLimit = effectiveSymbolLimit,
            impactLimit = effectiveImpactLimit,
            diagnosticLimit = effectiveDiagnosticLimit,
            relatedTestLimit = effectiveRelatedTestLimit,
            depth = effectiveDepth,
            includeSnippets,
            snippetLines = effectiveSnippetLines
        }));
        return ExitCodes.Success;
    }
}

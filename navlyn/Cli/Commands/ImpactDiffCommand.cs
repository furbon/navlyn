using System.CommandLine;
using Navlyn.Cli.OutputProfiles;
using Navlyn.Diagnostics;
using Navlyn.Diffs;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class ImpactDiffCommand
{
    private const int DefaultSymbolLimit = 50;
    private const int DefaultImpactLimit = 100;
    private const int DefaultDepth = 2;
    private const int DefaultSnippetLines = 1;

    public static Command Create()
    {
        Option<string?> baseOption = DiffCommandSupport.CreateBaseOption();
        Option<string?> headOption = DiffCommandSupport.CreateHeadOption();
        Option<bool> stagedOption = DiffCommandSupport.CreateStagedOption();
        Option<bool> includeUnstagedOption = DiffCommandSupport.CreateIncludeUnstagedOption();
        Option<string[]> projectOption = SharedOptions.CreateProjectFiltersOption();
        Option<bool> excludeGeneratedOption = SharedOptions.CreateExcludeGeneratedOption();
        Option<int?> symbolLimitOption = DiffCommandSupport.CreateSymbolLimitOption(DefaultSymbolLimit);
        Option<int?> impactLimitOption = DiffCommandSupport.CreateImpactLimitOption(DefaultImpactLimit);
        Option<int?> depthOption = DiffCommandSupport.CreateDepthOption(DefaultDepth);
        Option<string?> includeOption = DiffCommandSupport.CreateIncludeOption();
        Option<bool> includeSnippetsOption = DiffCommandSupport.CreateIncludeSnippetsOption();
        Option<int?> snippetLinesOption = DiffCommandSupport.CreateSnippetLinesOption();
        Option<string> profileOption = OutputProfile.CreateOption();

        return WorkspaceCommand.Create(
            "impact-diff",
            "Estimate static source impact for changed symbols in a diff.",
            [baseOption, headOption, stagedOption, includeUnstagedOption, projectOption, excludeGeneratedOption, symbolLimitOption, impactLimitOption, depthOption, includeOption, includeSnippetsOption, snippetLinesOption, profileOption],
            (workspace, parseResult, cancellationToken) => ExecuteAsync(
                workspace,
                parseResult.GetValue(baseOption),
                parseResult.GetValue(headOption),
                parseResult.GetValue(stagedOption),
                parseResult.GetValue(includeUnstagedOption),
                parseResult.GetValue(projectOption) ?? [],
                parseResult.GetValue(excludeGeneratedOption),
                parseResult.GetValue(symbolLimitOption),
                parseResult.GetValue(impactLimitOption),
                parseResult.GetValue(depthOption),
                parseResult.GetValue(includeOption),
                parseResult.GetValue(includeSnippetsOption),
                parseResult.GetValue(snippetLinesOption),
                parseResult.GetValue(profileOption)!,
                cancellationToken));
    }

    private static async Task<int> ExecuteAsync(
        LoadedWorkspace workspace,
        string? baseRef,
        string? headRef,
        bool staged,
        bool includeUnstaged,
        IReadOnlyList<string> projectFilters,
        bool excludeGenerated,
        int? symbolLimit,
        int? impactLimit,
        int? depth,
        string? include,
        bool includeSnippets,
        int? snippetLines,
        string profile,
        CancellationToken cancellationToken)
    {
        int effectiveSymbolLimit = symbolLimit ?? DefaultSymbolLimit;
        int effectiveImpactLimit = impactLimit ?? DefaultImpactLimit;
        int effectiveDepth = depth ?? DefaultDepth;
        int effectiveSnippetLines = snippetLines ?? DefaultSnippetLines;
        IReadOnlyList<string> includeModes = DiffCommandSupport.ParseInclude(include, ["references", "callers", "calls", "implementations"]);
        if (!DiffCommandSupport.TryCreatePositiveOption("--symbol-limit", effectiveSymbolLimit, out int exitCode) ||
            !DiffCommandSupport.TryCreatePositiveOption("--impact-limit", effectiveImpactLimit, out exitCode) ||
            !DiffCommandSupport.TryCreateNonNegativeOption("--depth", effectiveDepth, out exitCode) ||
            !DiffCommandSupport.TryCreateNonNegativeOption("--snippet-lines", effectiveSnippetLines, out exitCode) ||
            !DiffCommandSupport.TryCreateRequest(baseRef, headRef, staged, includeUnstaged, out DiffRequest request, out exitCode) ||
            !DiffCommandSupport.TryCreateProjectContext(workspace, projectFilters, out var projects, out var projectOutputs, out exitCode))
        {
            return exitCode;
        }

        if (!DiffCommandSupport.ValidateInclude(includeModes, out string? includeError))
        {
            DiagnosticReporter.WriteError(DiagnosticIds.ParseError, includeError!);
            return ExitCodes.UsageError;
        }

        DiffWorkflowExecutionResult<ImpactDiffResult> result =
            await new DiffWorkflowResolver().ResolveImpactAsync(
                workspace,
                request,
                projects,
                projectOutputs,
                excludeGenerated,
                effectiveSymbolLimit,
                effectiveImpactLimit,
                effectiveDepth,
                includeModes,
                includeSnippets,
                effectiveSnippetLines,
                cancellationToken);

        if (result.Error is not null)
        {
            return DiffCommandSupport.WriteError(result.Error);
        }

        ConsoleJsonWriter.Write(OutputProfile.Format(workspace, "impact-diff", profile, result.Result!, new
        {
            baseRef,
            headRef,
            staged,
            includeUnstaged,
            projectFilters,
            excludeGenerated,
            symbolLimit = effectiveSymbolLimit,
            impactLimit = effectiveImpactLimit,
            depth = effectiveDepth,
            includeModes,
            includeSnippets,
            snippetLines = effectiveSnippetLines
        }));
        return ExitCodes.Success;
    }
}

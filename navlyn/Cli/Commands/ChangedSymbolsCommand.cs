using System.CommandLine;
using Navlyn.Cli.OutputProfiles;
using Navlyn.Diffs;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class ChangedSymbolsCommand
{
    private const int DefaultSymbolLimit = 100;

    public static Command Create()
    {
        Option<string?> baseOption = DiffCommandSupport.CreateBaseOption();
        Option<string?> headOption = DiffCommandSupport.CreateHeadOption();
        Option<bool> stagedOption = DiffCommandSupport.CreateStagedOption();
        Option<bool> includeUnstagedOption = DiffCommandSupport.CreateIncludeUnstagedOption();
        Option<string[]> projectOption = SharedOptions.CreateProjectFiltersOption();
        Option<bool> excludeGeneratedOption = SharedOptions.CreateExcludeGeneratedOption();
        Option<int?> symbolLimitOption = DiffCommandSupport.CreateSymbolLimitOption(DefaultSymbolLimit);
        Option<string> profileOption = OutputProfile.CreateOption();

        return WorkspaceCommand.Create(
            "changed-symbols",
            "Extract source symbols touched by the current diff.",
            [baseOption, headOption, stagedOption, includeUnstagedOption, projectOption, excludeGeneratedOption, symbolLimitOption, profileOption],
            (workspace, parseResult, cancellationToken) => ExecuteAsync(
                workspace,
                parseResult.GetValue(baseOption),
                parseResult.GetValue(headOption),
                parseResult.GetValue(stagedOption),
                parseResult.GetValue(includeUnstagedOption),
                parseResult.GetValue(projectOption) ?? [],
                parseResult.GetValue(excludeGeneratedOption),
                parseResult.GetValue(symbolLimitOption),
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
        string profile,
        CancellationToken cancellationToken)
    {
        int effectiveSymbolLimit = symbolLimit ?? DefaultSymbolLimit;
        if (!DiffCommandSupport.TryCreatePositiveOption("--symbol-limit", effectiveSymbolLimit, out int exitCode) ||
            !DiffCommandSupport.TryCreateRequest(baseRef, headRef, staged, includeUnstaged, out DiffRequest request, out exitCode) ||
            !DiffCommandSupport.TryCreateProjectContext(workspace, projectFilters, out var projects, out var projectOutputs, out exitCode))
        {
            return exitCode;
        }

        DiffWorkflowExecutionResult<ChangedSymbolsResult> result =
            await new DiffWorkflowResolver().ResolveChangedSymbolsAsync(
                workspace,
                request,
                projects,
                projectOutputs,
                excludeGenerated,
                effectiveSymbolLimit,
                cancellationToken);

        if (result.Error is not null)
        {
            return DiffCommandSupport.WriteError(result.Error);
        }

        ConsoleJsonWriter.Write(OutputProfile.Format(workspace, "changed-symbols", profile, result.Result!, new
        {
            baseRef,
            headRef,
            staged,
            includeUnstaged,
            projectFilters,
            excludeGenerated,
            symbolLimit = effectiveSymbolLimit
        }));
        return ExitCodes.Success;
    }
}

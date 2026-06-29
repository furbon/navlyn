using System.CommandLine;
using Navlyn.Cli.OutputProfiles;
using Navlyn.Diffs;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class DiagnosticsDiffCommand
{
    private const int DefaultSymbolLimit = 50;
    private const int DefaultImpactLimit = 100;
    private const int DefaultDiagnosticLimit = 100;

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
        Option<int?> diagnosticLimitOption = DiffCommandSupport.CreateDiagnosticLimitOption(DefaultDiagnosticLimit);
        Option<string[]> severityOption = DiffCommandSupport.CreateSeverityOption();
        Option<string[]> idOption = DiffCommandSupport.CreateDiagnosticIdOption();
        Option<string> profileOption = OutputProfile.CreateOption();

        return WorkspaceCommand.Create(
            "diagnostics-diff",
            "Report current compiler diagnostics scoped to changed and affected files.",
            [baseOption, headOption, stagedOption, includeUnstagedOption, projectOption, excludeGeneratedOption, symbolLimitOption, impactLimitOption, diagnosticLimitOption, severityOption, idOption, profileOption],
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
                parseResult.GetValue(diagnosticLimitOption),
                parseResult.GetValue(severityOption) ?? [],
                parseResult.GetValue(idOption) ?? [],
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
        int? diagnosticLimit,
        IReadOnlyList<string> severities,
        IReadOnlyList<string> ids,
        string profile,
        CancellationToken cancellationToken)
    {
        int effectiveSymbolLimit = symbolLimit ?? DefaultSymbolLimit;
        int effectiveImpactLimit = impactLimit ?? DefaultImpactLimit;
        int effectiveDiagnosticLimit = diagnosticLimit ?? DefaultDiagnosticLimit;
        if (!DiffCommandSupport.TryCreatePositiveOption("--symbol-limit", effectiveSymbolLimit, out int exitCode) ||
            !DiffCommandSupport.TryCreatePositiveOption("--impact-limit", effectiveImpactLimit, out exitCode) ||
            !DiffCommandSupport.TryCreatePositiveOption("--diagnostic-limit", effectiveDiagnosticLimit, out exitCode) ||
            !DiffCommandSupport.TryNormalizeSeverities(severities, out IReadOnlyList<string> normalizedSeverities, out exitCode) ||
            !DiffCommandSupport.TryCreateRequest(baseRef, headRef, staged, includeUnstaged, out DiffRequest request, out exitCode) ||
            !DiffCommandSupport.TryCreateProjectContext(workspace, projectFilters, out var projects, out var projectOutputs, out exitCode))
        {
            return exitCode;
        }

        DiffWorkflowExecutionResult<DiagnosticsDiffResult> result =
            await new DiffWorkflowResolver().ResolveDiagnosticsAsync(
                workspace,
                request,
                projects,
                projectOutputs,
                normalizedSeverities,
                DiffCommandSupport.NormalizeStrings(ids),
                excludeGenerated,
                effectiveSymbolLimit,
                effectiveImpactLimit,
                effectiveDiagnosticLimit,
                cancellationToken);

        if (result.Error is not null)
        {
            return DiffCommandSupport.WriteError(result.Error);
        }

        IReadOnlyList<string> normalizedIds = DiffCommandSupport.NormalizeStrings(ids);
        ConsoleJsonWriter.Write(OutputProfile.Format(workspace, "diagnostics-diff", profile, result.Result!, new
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
            severities = normalizedSeverities,
            ids = normalizedIds
        }));
        return ExitCodes.Success;
    }
}

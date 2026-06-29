using System.CommandLine;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Navlyn.Diagnostics;
using Navlyn.Paths;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class DiagnosticPackCommand
{
    private const int DefaultLimit = 20;
    private const int DefaultBudgetTokens = 3000;

    public static Command Create()
    {
        Option<string?> idOption = new("--id")
        {
            Description = "Exact compiler diagnostic id to pack, such as CS0103."
        };
        Option<FileInfo?> fileOption = new("--file")
        {
            Description = "Path to a C# source file containing the diagnostic."
        };
        Option<int?> lineOption = new("--line")
        {
            Description = "1-based diagnostic source line."
        };
        Option<int?> columnOption = new("--column")
        {
            Description = "1-based diagnostic source column."
        };
        Option<string[]> projectOption = SharedOptions.CreateProjectFiltersOption();
        Option<bool> excludeGeneratedOption = SharedOptions.CreateExcludeGeneratedOption();
        Option<string[]> severityOption = new("--severity")
        {
            Description = "Restrict diagnostics to severity values: Hidden, Info, Warning, or Error. Can be specified more than once.",
            AllowMultipleArgumentsPerToken = true
        };
        Option<int?> limitOption = SharedOptions.CreateLimitOption();
        Option<int?> budgetTokensOption = new("--budget-tokens")
        {
            Description = $"Approximate source context budget. Defaults to {DefaultBudgetTokens} tokens."
        };

        return WorkspaceCommand.Create(
            "diagnostic-pack",
            "Create a bounded facts pack around compiler diagnostics.",
            [idOption, fileOption, lineOption, columnOption, projectOption, excludeGeneratedOption, severityOption, limitOption, budgetTokensOption],
            (workspace, parseResult, cancellationToken) => ExecuteAsync(
                workspace,
                parseResult.GetValue(idOption),
                parseResult.GetValue(fileOption),
                parseResult.GetValue(lineOption),
                parseResult.GetValue(columnOption),
                parseResult.GetValue(projectOption) ?? [],
                parseResult.GetValue(excludeGeneratedOption),
                parseResult.GetValue(severityOption) ?? [],
                parseResult.GetValue(limitOption),
                parseResult.GetValue(budgetTokensOption),
                cancellationToken));
    }

    private static async Task<int> ExecuteAsync(
        LoadedWorkspace workspace,
        string? id,
        FileInfo? file,
        int? line,
        int? column,
        IReadOnlyList<string> projectFilters,
        bool excludeGenerated,
        IReadOnlyList<string> severities,
        int? limit,
        int? budgetTokens,
        CancellationToken cancellationToken)
    {
        bool hasId = !string.IsNullOrWhiteSpace(id);
        bool hasAnyPosition = file is not null || line is not null || column is not null;
        bool hasCompletePosition = file is not null && line is not null && column is not null;
        if (hasId == hasAnyPosition || hasAnyPosition && !hasCompletePosition)
        {
            DiagnosticReporter.WriteError(DiagnosticIds.ParseError, "Specify exactly one diagnostic-pack input mode: --id or --file with --line and --column.");
            return ExitCodes.UsageError;
        }

        int effectiveLimit = limit ?? DefaultLimit;
        int effectiveBudgetTokens = budgetTokens ?? DefaultBudgetTokens;
        if (effectiveLimit <= 0)
        {
            DiagnosticReporter.WriteError(DiagnosticIds.InvalidLimit, "--limit must be 1 or greater.");
            return ExitCodes.UsageError;
        }

        if (effectiveBudgetTokens <= 0)
        {
            DiagnosticReporter.WriteError(DiagnosticIds.InvalidLimit, "--budget-tokens must be 1 or greater.");
            return ExitCodes.UsageError;
        }

        if (!TryNormalizeSeverities(severities, out IReadOnlyList<string> normalizedSeverities, out string? severityError))
        {
            DiagnosticReporter.WriteError(DiagnosticIds.InvalidDiagnosticSeverity, severityError!);
            return ExitCodes.UsageError;
        }

        ProjectFilterResolutionResult projectResult =
            new ProjectFilterResolver().ResolveMany(workspace.Solution, projectFilters);
        if (projectResult.Error is not null)
        {
            DiagnosticReporter.WriteError(projectResult.Error.DiagnosticId, projectResult.Error.Message);
            return projectResult.Error.ExitCode;
        }

        DiagnosticPackInput input = hasId
            ? new DiagnosticPackInput("id", id!.Trim(), File: null, Line: null, Column: null)
            : new DiagnosticPackInput(
                "sourcePosition",
                Id: null,
                File: PathDisplay.FromCurrentDirectory(file!.ToString()),
                line,
                column);

        DiagnosticPackResolution resolution = await new DiagnosticPackResolver().ResolveAsync(
            workspace,
            projectResult.Projects,
            input,
            excludeGenerated,
            normalizedSeverities,
            effectiveLimit,
            effectiveBudgetTokens,
            cancellationToken);

        ConsoleJsonWriter.Write(new DiagnosticPackResult(
            Workspace: resolution.Workspace,
            Kind: resolution.Kind,
            Projects: projectResult.AppliedFilters.Count == 0
                ? null
                : projectResult.AppliedFilters.Select(ProjectFilterOutput.FromAppliedFilter).ToArray(),
            Input: resolution.Input,
            ExcludeGenerated: resolution.ExcludeGenerated,
            Filters: resolution.Filters,
            TotalDiagnostics: resolution.TotalDiagnostics,
            Truncated: resolution.Truncated,
            Diagnostics: resolution.Diagnostics,
            Context: resolution.Context,
            Warnings: resolution.Warnings,
            NextActions: resolution.NextActions));

        return ExitCodes.Success;
    }

    private static bool TryNormalizeSeverities(
        IReadOnlyList<string> severities,
        out IReadOnlyList<string> normalizedSeverities,
        out string? error)
    {
        normalizedSeverities = [];
        error = null;

        List<string> values = [];
        foreach (string severity in severities)
        {
            if (string.IsNullOrWhiteSpace(severity))
            {
                error = "Diagnostic severity must not be empty.";
                return false;
            }

            string trimmed = severity.Trim();
            if (!Enum.GetNames<DiagnosticSeverity>().Contains(trimmed, StringComparer.Ordinal))
            {
                error = $"Unknown diagnostic severity: {trimmed}.";
                return false;
            }

            values.Add(trimmed);
        }

        normalizedSeverities = [.. values
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)];
        return true;
    }

    private sealed record DiagnosticPackResult(
        string Workspace,
        string Kind,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<ProjectFilterOutput>? Projects,
        DiagnosticPackInput Input,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool ExcludeGenerated,
        DiagnosticPackFilters Filters,
        int TotalDiagnostics,
        bool Truncated,
        IReadOnlyList<DiagnosticPackDiagnostic> Diagnostics,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        DiagnosticPackContext? Context,
        IReadOnlyList<string> Warnings,
        IReadOnlyList<DiagnosticPackNextAction> NextActions);
}

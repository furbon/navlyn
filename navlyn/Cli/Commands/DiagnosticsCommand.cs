using System.CommandLine;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Navlyn.Diagnostics;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class DiagnosticsCommand
{
    public static Command Create()
    {
        Option<string[]> projectOption = SharedOptions.CreateProjectFiltersOption();
        Option<bool> excludeGeneratedOption = SharedOptions.CreateExcludeGeneratedOption();
        Option<string[]> severityOption = new("--severity")
        {
            Description = "Restrict diagnostics to severity values: Hidden, Info, Warning, or Error. Can be specified more than once.",
            AllowMultipleArgumentsPerToken = true
        };
        Option<string[]> idOption = new("--id")
        {
            Description = "Restrict diagnostics to an exact compiler diagnostic id. Can be specified more than once.",
            AllowMultipleArgumentsPerToken = true
        };
        Option<int?> limitOption = SharedOptions.CreateLimitOption();

        return WorkspaceCommand.Create(
            "diagnostics",
            "Report compiler diagnostics for a workspace.",
            [projectOption, excludeGeneratedOption, severityOption, idOption, limitOption],
            (workspace, parseResult, cancellationToken) => ExecuteAsync(
                workspace,
                parseResult.GetValue(projectOption) ?? [],
                parseResult.GetValue(excludeGeneratedOption),
                parseResult.GetValue(severityOption) ?? [],
                parseResult.GetValue(idOption) ?? [],
                parseResult.GetValue(limitOption),
                cancellationToken));
    }

    private static async Task<int> ExecuteAsync(
        LoadedWorkspace loadedWorkspace,
        IReadOnlyList<string> projectFilters,
        bool excludeGenerated,
        IReadOnlyList<string> severities,
        IReadOnlyList<string> ids,
        int? limit,
        CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            DiagnosticReporter.WriteError(DiagnosticIds.InvalidLimit, "--limit must be 1 or greater.");
            return ExitCodes.UsageError;
        }

        if (!TryNormalizeSeverities(severities, out IReadOnlyList<string> normalizedSeverities, out string? severityError))
        {
            DiagnosticReporter.WriteError(DiagnosticIds.InvalidDiagnosticSeverity, severityError!);
            return ExitCodes.UsageError;
        }

        IReadOnlyList<string> normalizedIds = NormalizeStrings(ids);

        ProjectFilterResolutionResult projectResult =
            new ProjectFilterResolver().ResolveMany(loadedWorkspace.Solution, projectFilters);
        if (projectResult.Error is not null)
        {
            DiagnosticReporter.WriteError(projectResult.Error.DiagnosticId, projectResult.Error.Message);
            return projectResult.Error.ExitCode;
        }

        WorkspaceDiagnosticsResolution resolution =
            await new WorkspaceDiagnosticsResolver().ResolveAsync(projectResult.Projects, excludeGenerated, cancellationToken);

        IReadOnlyList<WorkspaceDiagnosticResult> filteredDiagnostics = FilterDiagnostics(
            resolution.Diagnostics,
            normalizedSeverities,
            normalizedIds);
        IReadOnlyList<WorkspaceDiagnosticResult> limitedDiagnostics = limit is null
            ? filteredDiagnostics
            : [.. filteredDiagnostics.Take(limit.Value)];

        ConsoleJsonWriter.Write(new DiagnosticsResult(
            Workspace: loadedWorkspace.DisplayPath,
            Kind: loadedWorkspace.Kind,
            Projects: projectResult.AppliedFilters.Count == 0
                ? null
                : projectResult.AppliedFilters.Select(ProjectFilterOutput.FromAppliedFilter).ToArray(),
            Severities: normalizedSeverities.Count == 0 ? null : normalizedSeverities,
            Ids: normalizedIds.Count == 0 ? null : normalizedIds,
            ExcludeGenerated: excludeGenerated,
            Limit: limit,
            TotalDiagnostics: filteredDiagnostics.Count,
            Diagnostics: limitedDiagnostics.Select(diagnostic => new DiagnosticResult(
                Project: new DiagnosticProjectResult(
                    Name: diagnostic.Project.Name,
                    Path: diagnostic.Project.Path,
                    TargetFramework: diagnostic.Project.TargetFramework),
                Severity: diagnostic.Severity,
                Id: diagnostic.Id,
                Message: diagnostic.Message,
                Path: diagnostic.Path,
                Line: diagnostic.Line,
                Column: diagnostic.Column,
                EndLine: diagnostic.EndLine,
                EndColumn: diagnostic.EndColumn)).ToArray()));

        return ExitCodes.Success;
    }

    private static IReadOnlyList<WorkspaceDiagnosticResult> FilterDiagnostics(
        IReadOnlyList<WorkspaceDiagnosticResult> diagnostics,
        IReadOnlyList<string> severities,
        IReadOnlyList<string> ids)
    {
        HashSet<string> severitySet = [.. severities];
        HashSet<string> idSet = [.. ids];
        return [.. diagnostics.Where(diagnostic =>
            (severitySet.Count == 0 || severitySet.Contains(diagnostic.Severity)) &&
            (idSet.Count == 0 || idSet.Contains(diagnostic.Id)))];
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

    private static IReadOnlyList<string> NormalizeStrings(IReadOnlyList<string> values)
    {
        return [.. values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)];
    }

    private sealed record DiagnosticsResult(
        string Workspace,
        string Kind,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<ProjectFilterOutput>? Projects,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<string>? Severities,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<string>? Ids,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool ExcludeGenerated,
        int? Limit,
        int TotalDiagnostics,
        IReadOnlyList<DiagnosticResult> Diagnostics);

    private sealed record DiagnosticResult(
        DiagnosticProjectResult Project,
        string Severity,
        string Id,
        string Message,
        string? Path,
        int? Line,
        int? Column,
        int? EndLine,
        int? EndColumn);

    private sealed record DiagnosticProjectResult(
        string Name,
        string? Path,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? TargetFramework);
}

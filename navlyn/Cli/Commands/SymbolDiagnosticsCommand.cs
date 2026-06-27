using System.CommandLine;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Navlyn.Diagnostics;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class SymbolDiagnosticsCommand
{
    public static Command Create()
    {
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

        return SourcePositionCommand.Create(
            "symbol-diagnostics",
            "Return compiler diagnostics scoped to the C# symbol at a source position.",
            [severityOption, idOption, limitOption],
            (workspace, options, parseResult, cancellationToken) => ExecuteAsync(
                workspace,
                options,
                parseResult.GetValue(severityOption) ?? [],
                parseResult.GetValue(idOption) ?? [],
                parseResult.GetValue(limitOption),
                cancellationToken));
    }

    private static async Task<int> ExecuteAsync(
        LoadedWorkspace loadedWorkspace,
        SourcePositionOptions options,
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
        SymbolDiagnosticsResolutionResult result = await new SymbolDiagnosticsResolver().ResolveAsync(
            loadedWorkspace.Solution,
            options.File,
            options.Line,
            options.Column,
            options.Project,
            options.ExcludeGenerated,
            normalizedSeverities,
            normalizedIds,
            limit,
            cancellationToken);

        if (result.Error is not null)
        {
            DiagnosticReporter.WriteError(result.Error.DiagnosticId, result.Error.Message);
            return result.Error.ExitCode;
        }

        SymbolDiagnosticsResolution resolution = result.Resolution!;
        ConsoleJsonWriter.Write(new SymbolDiagnosticsResult(
            File: resolution.File,
            Line: resolution.Line,
            Column: resolution.Column,
            Project: options.ProjectFilter is null ? null : ProjectFilterOutput.FromAppliedFilter(options.ProjectFilter),
            SelectionInput: options.SelectionInput,
            ExcludeGenerated: options.ExcludeGenerated,
            Symbol: resolution.Symbol,
            Filters: resolution.Filters,
            TotalDiagnostics: resolution.TotalDiagnostics,
            Truncated: resolution.Truncated,
            Diagnostics: resolution.Diagnostics));

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

    private static IReadOnlyList<string> NormalizeStrings(IReadOnlyList<string> values)
    {
        return [.. values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)];
    }

    private sealed record SymbolDiagnosticsResult(
        string File,
        int Line,
        int Column,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        ProjectFilterOutput? Project,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        CandidateSelectionInput? SelectionInput,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool ExcludeGenerated,
        SymbolDiagnosticsSymbol Symbol,
        SymbolDiagnosticsFilters Filters,
        int TotalDiagnostics,
        bool Truncated,
        IReadOnlyList<SymbolDiagnosticItem> Diagnostics);
}

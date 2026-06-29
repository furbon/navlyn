using System.CommandLine;
using Microsoft.CodeAnalysis;
using Navlyn.Diagnostics;
using Navlyn.Diffs;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class DiffCommandSupport
{
    public static Option<string?> CreateBaseOption()
    {
        return new Option<string?>("--base")
        {
            Description = "Git ref to compare from. With --head, compares base to head; without --head, compares base to the working tree."
        };
    }

    public static Option<string?> CreateHeadOption()
    {
        return new Option<string?>("--head")
        {
            Description = "Git ref to compare to. Requires --base."
        };
    }

    public static Option<bool> CreateStagedOption()
    {
        return new Option<bool>("--staged")
        {
            Description = "Use staged changes."
        };
    }

    public static Option<bool> CreateIncludeUnstagedOption()
    {
        return new Option<bool>("--include-unstaged")
        {
            Description = "Use unstaged working tree changes. This is the default when --base, --head, and --staged are omitted.",
            DefaultValueFactory = _ => true
        };
    }

    public static Option<int?> CreateSymbolLimitOption(int defaultValue)
    {
        return new Option<int?>("--symbol-limit")
        {
            Description = $"Maximum number of changed symbols to return. Defaults to {defaultValue}."
        };
    }

    public static Option<int?> CreateImpactLimitOption(int defaultValue)
    {
        return new Option<int?>("--impact-limit")
        {
            Description = $"Maximum number of impact items or affected facts to return. Defaults to {defaultValue}."
        };
    }

    public static Option<int?> CreateDiagnosticLimitOption(int defaultValue)
    {
        return new Option<int?>("--diagnostic-limit")
        {
            Description = $"Maximum number of diagnostics to return. Defaults to {defaultValue}."
        };
    }

    public static Option<int?> CreateRelatedTestLimitOption(int defaultValue)
    {
        return new Option<int?>("--related-test-limit")
        {
            Description = $"Maximum number of related test candidates to return. Defaults to {defaultValue}."
        };
    }

    public static Option<int?> CreateDepthOption(int defaultValue)
    {
        return new Option<int?>("--depth")
        {
            Description = $"Bounded traversal depth for diff impact. Defaults to {defaultValue}."
        };
    }

    public static Option<string[]> CreateSeverityOption()
    {
        return new Option<string[]>("--severity")
        {
            Description = "Restrict diagnostics to severity values: Hidden, Info, Warning, or Error. Can be specified more than once.",
            AllowMultipleArgumentsPerToken = true
        };
    }

    public static Option<string[]> CreateDiagnosticIdOption()
    {
        return new Option<string[]>("--id")
        {
            Description = "Restrict diagnostics to an exact compiler diagnostic id. Can be specified more than once.",
            AllowMultipleArgumentsPerToken = true
        };
    }

    public static Option<bool> CreateIncludeSnippetsOption()
    {
        return new Option<bool>("--include-snippets")
        {
            Description = "Include bounded source snippets in returned locations."
        };
    }

    public static Option<int?> CreateSnippetLinesOption()
    {
        return new Option<int?>("--snippet-lines")
        {
            Description = "Source snippet context lines before and after the matched line. Defaults to 1."
        };
    }

    public static Option<string?> CreateIncludeOption()
    {
        return new Option<string?>("--include")
        {
            Description = "Comma-separated include modes: references,callers,calls,implementations,entrypoints."
        };
    }

    public static bool TryCreateRequest(
        string? baseRef,
        string? headRef,
        bool staged,
        bool includeUnstaged,
        out DiffRequest request,
        out int exitCode)
    {
        request = default!;
        exitCode = ExitCodes.Success;

        if (!string.IsNullOrWhiteSpace(headRef) && string.IsNullOrWhiteSpace(baseRef))
        {
            DiagnosticReporter.WriteError(DiagnosticIds.InvalidDiffOptions, "--head requires --base.");
            exitCode = ExitCodes.UsageError;
            return false;
        }

        if (staged && (!string.IsNullOrWhiteSpace(baseRef) || !string.IsNullOrWhiteSpace(headRef)))
        {
            DiagnosticReporter.WriteError(DiagnosticIds.InvalidDiffOptions, "--staged cannot be combined with --base or --head.");
            exitCode = ExitCodes.UsageError;
            return false;
        }

        string? normalizedBase = string.IsNullOrWhiteSpace(baseRef) ? null : baseRef.Trim();
        string? normalizedHead = string.IsNullOrWhiteSpace(headRef) ? null : headRef.Trim();
        string mode = normalizedBase is not null && normalizedHead is not null
            ? "range"
            : normalizedBase is not null
                ? "baseToWorkingTree"
                : staged
                    ? "staged"
                    : "workingTree";

        request = new DiffRequest(
            Mode: mode,
            Base: normalizedBase,
            Head: normalizedHead,
            Staged: staged,
            IncludeUnstaged: !staged && normalizedBase is null && normalizedHead is null && includeUnstaged);
        return true;
    }

    public static bool TryCreateProjectContext(
        LoadedWorkspace workspace,
        IReadOnlyList<string> projectFilters,
        out IReadOnlyList<Project> projects,
        out IReadOnlyList<DiffProjectFilter>? projectOutputs,
        out int exitCode)
    {
        ProjectFilterResolutionResult result = new ProjectFilterResolver().ResolveMany(workspace.Solution, projectFilters);
        if (result.Error is not null)
        {
            DiagnosticReporter.WriteError(result.Error.DiagnosticId, result.Error.Message);
            projects = [];
            projectOutputs = null;
            exitCode = result.Error.ExitCode;
            return false;
        }

        projects = result.Projects;
        projectOutputs = result.AppliedFilters.Count == 0
            ? null
            : result.AppliedFilters.Select(filter => new DiffProjectFilter(
                Filter: filter.Filter,
                Name: filter.Name,
                Path: filter.Path,
                TargetFramework: filter.TargetFramework)).ToArray();
        exitCode = ExitCodes.Success;
        return true;
    }

    public static bool TryCreatePositiveOption(string optionName, int value, out int exitCode)
    {
        exitCode = ExitCodes.Success;
        if (value > 0)
        {
            return true;
        }

        DiagnosticReporter.WriteError(DiagnosticIds.InvalidLimit, $"{optionName} must be 1 or greater.");
        exitCode = ExitCodes.UsageError;
        return false;
    }

    public static bool TryCreateNonNegativeOption(string optionName, int value, out int exitCode)
    {
        exitCode = ExitCodes.Success;
        if (value >= 0)
        {
            return true;
        }

        DiagnosticReporter.WriteError(DiagnosticIds.InvalidLimit, $"{optionName} must be 0 or greater.");
        exitCode = ExitCodes.UsageError;
        return false;
    }

    public static bool TryNormalizeSeverities(
        IReadOnlyList<string> severities,
        out IReadOnlyList<string> normalizedSeverities,
        out int exitCode)
    {
        normalizedSeverities = [];
        exitCode = ExitCodes.Success;
        List<string> values = [];
        foreach (string severity in severities)
        {
            if (string.IsNullOrWhiteSpace(severity))
            {
                DiagnosticReporter.WriteError(DiagnosticIds.InvalidDiagnosticSeverity, "Diagnostic severity must not be empty.");
                exitCode = ExitCodes.UsageError;
                return false;
            }

            string trimmed = severity.Trim();
            if (!Enum.GetNames<DiagnosticSeverity>().Contains(trimmed, StringComparer.Ordinal))
            {
                DiagnosticReporter.WriteError(DiagnosticIds.InvalidDiagnosticSeverity, $"Unknown diagnostic severity: {trimmed}.");
                exitCode = ExitCodes.UsageError;
                return false;
            }

            values.Add(trimmed);
        }

        normalizedSeverities = [.. values.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal)];
        return true;
    }

    public static IReadOnlyList<string> NormalizeStrings(IReadOnlyList<string> values)
    {
        return [.. values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)];
    }

    public static IReadOnlyList<string> ParseInclude(string? include, IReadOnlyList<string> defaultModes)
    {
        return string.IsNullOrWhiteSpace(include)
            ? defaultModes
            : [.. include
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(mode => mode, StringComparer.Ordinal)];
    }

    public static bool ValidateInclude(IReadOnlyList<string> modes, out string? error)
    {
        string[] allowed = ["references", "callers", "calls", "implementations", "entrypoints"];
        string? unknown = modes.FirstOrDefault(mode => !allowed.Contains(mode, StringComparer.Ordinal));
        if (unknown is not null)
        {
            error = $"Unknown include mode: {unknown}.";
            return false;
        }

        error = null;
        return true;
    }

    public static int WriteError(DiffWorkflowError error)
    {
        DiagnosticReporter.WriteError(error.DiagnosticId, error.Message);
        return error.ExitCode;
    }
}

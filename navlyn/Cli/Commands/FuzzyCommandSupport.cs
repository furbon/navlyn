using System.CommandLine;
using Microsoft.CodeAnalysis;
using Navlyn.Diagnostics;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class FuzzyCommandSupport
{
    public static Option<string[]> CreateAssumeKindOption()
    {
        return new Option<string[]>("--assume-kind")
        {
            Description = "Assume one or more symbol kinds when ranking fuzzy candidates.",
            AllowMultipleArgumentsPerToken = true
        };
    }

    public static Option<string> CreateMatchOption()
    {
        Option<string> option = new("--match")
        {
            Description = "Fuzzy match mode: smart, exact, contains, or regex.",
            DefaultValueFactory = _ => "smart"
        };

        option.AcceptOnlyFromAmong("smart", "exact", "contains", "regex");
        return option;
    }

    public static Option<bool> CreateIncludeSnippetsOption()
    {
        return new Option<bool>("--include-snippets")
        {
            Description = "Include bounded source snippets for returned source locations."
        };
    }

    public static Option<int?> CreateSnippetLinesOption()
    {
        return new Option<int?>("--snippet-lines")
        {
            Description = "Number of context lines before and after a snippet location. Defaults to 1."
        };
    }

    public static bool TryCreateQuery(
        LoadedWorkspace loadedWorkspace,
        string query,
        IReadOnlyList<string> assumeKinds,
        string match,
        bool caseSensitive,
        IReadOnlyList<string> projectFilters,
        bool excludeGenerated,
        int? limit,
        out FuzzyQueryOptions options,
        out IReadOnlyList<Project> projects,
        out IReadOnlyList<FuzzyProjectFilter>? projectOutputs,
        out int exitCode)
    {
        options = new FuzzyQueryOptions(query, [], "smart", null, false, null);
        projects = [];
        projectOutputs = null;
        exitCode = ExitCodes.Success;

        if (string.IsNullOrWhiteSpace(query))
        {
            DiagnosticReporter.WriteError(DiagnosticIds.ParseError, "--query must not be empty.");
            exitCode = ExitCodes.UsageError;
            return false;
        }

        if (limit <= 0)
        {
            DiagnosticReporter.WriteError(DiagnosticIds.InvalidLimit, "--limit must be 1 or greater.");
            exitCode = ExitCodes.UsageError;
            return false;
        }

        string? kindError = GetKindError(assumeKinds);
        if (kindError is not null)
        {
            DiagnosticReporter.WriteError(DiagnosticIds.InvalidSymbolKind, kindError);
            exitCode = ExitCodes.UsageError;
            return false;
        }

        ProjectFilterResolutionResult projectResult =
            new ProjectFilterResolver().ResolveMany(loadedWorkspace.Solution, projectFilters);
        if (projectResult.Error is not null)
        {
            DiagnosticReporter.WriteError(projectResult.Error.DiagnosticId, projectResult.Error.Message);
            exitCode = projectResult.Error.ExitCode;
            return false;
        }

        options = new FuzzyQueryOptions(
            Query: query.Trim(),
            AssumeKinds: NormalizeStrings(assumeKinds),
            Match: match,
            CaseSensitive: caseSensitive ? true : null,
            ExcludeGenerated: excludeGenerated,
            Limit: limit);

        if (match == "regex")
        {
            SymbolSearchOptions searchOptions = new(
                Query: options.Query,
                MatchMode: SymbolMatchMode.Regex,
                CaseSensitive: options.CaseSensitive ?? false);

            if (!SymbolNameMatcher.TryCreate(searchOptions, out _, out string? regexError))
            {
                DiagnosticReporter.WriteError(DiagnosticIds.InvalidRegex, regexError!);
                exitCode = ExitCodes.UsageError;
                return false;
            }
        }

        projects = projectResult.Projects;
        projectOutputs = projectResult.AppliedFilters.Count == 0
            ? null
            : projectResult.AppliedFilters.Select(filter => new FuzzyProjectFilter(
                Filter: filter.Filter,
                Name: filter.Name,
                Path: filter.Path,
                TargetFramework: filter.TargetFramework)).ToArray();
        return true;
    }

    public static bool TryCreatePositiveOption(string name, int value, out int exitCode)
    {
        exitCode = ExitCodes.Success;
        if (value >= 1)
        {
            return true;
        }

        DiagnosticReporter.WriteError(DiagnosticIds.InvalidLimit, $"{name} must be 1 or greater.");
        exitCode = ExitCodes.UsageError;
        return false;
    }

    public static bool TryCreateNonNegativeOption(string name, int value, out int exitCode)
    {
        exitCode = ExitCodes.Success;
        if (value >= 0)
        {
            return true;
        }

        DiagnosticReporter.WriteError(DiagnosticIds.InvalidLimit, $"{name} must be 0 or greater.");
        exitCode = ExitCodes.UsageError;
        return false;
    }

    public static IReadOnlyList<string> ParseInclude(string? include, IReadOnlyList<string> defaults)
    {
        if (string.IsNullOrWhiteSpace(include))
        {
            return defaults;
        }

        return [.. include
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)];
    }

    public static bool ValidateInclude(IReadOnlyList<string> include, out string? error)
    {
        string[] known = ["declarations", "references", "callers", "calls", "implementations", "hierarchy"];
        string? unknown = include.FirstOrDefault(value => !known.Contains(value, StringComparer.Ordinal));
        error = unknown is null ? null : $"Unknown include mode: {unknown}.";
        return unknown is null;
    }

    private static IReadOnlyList<string> NormalizeStrings(IReadOnlyList<string> values)
    {
        return [.. values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)];
    }

    private static string? GetKindError(IReadOnlyList<string> kinds)
    {
        foreach (string kind in kinds)
        {
            if (string.IsNullOrWhiteSpace(kind))
            {
                return "Symbol kind must not be empty.";
            }

            if (!Enum.GetNames<SymbolKind>().Contains(kind, StringComparer.Ordinal))
            {
                return $"Unknown symbol kind: {kind}.";
            }
        }

        return null;
    }
}

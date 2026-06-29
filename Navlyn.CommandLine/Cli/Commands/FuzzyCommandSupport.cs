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

    public static Option<string?> CreateCandidateIdOption()
    {
        return new Option<string?>("--candidate-id")
        {
            Description = "Select a fuzzy candidate returned by a previous command."
        };
    }

    public static Option<string> CreateCandidatePolicyOption(string defaultValue)
    {
        Option<string> option = new("--candidate-policy")
        {
            Description = "Candidate selection policy: fail, select, or group.",
            DefaultValueFactory = _ => defaultValue
        };

        option.AcceptOnlyFromAmong("fail", "select", "group");
        return option;
    }

    public static Option<string> CreateMinConfidenceOption(string defaultValue)
    {
        Option<string> option = new("--min-confidence")
        {
            Description = "Minimum confidence required before returning selected-candidate facts.",
            DefaultValueFactory = _ => defaultValue
        };

        option.AcceptOnlyFromAmong("high", "medium", "low");
        return option;
    }

    public static Option<bool> CreateExplainSelectionOption()
    {
        return new Option<bool>("--explain-selection")
        {
            Description = "Include structured candidate selection explanation."
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

    public static bool TryCreateSelection(
        LoadedWorkspace loadedWorkspace,
        string? query,
        string? candidateId,
        IReadOnlyList<string> assumeKinds,
        string match,
        bool caseSensitive,
        IReadOnlyList<string> projectFilters,
        bool excludeGenerated,
        int? limit,
        string candidatePolicy,
        string minConfidence,
        bool explainSelection,
        bool allowGroupPolicy,
        out FuzzyQueryOptions options,
        out IReadOnlyList<Project> projects,
        out IReadOnlyList<FuzzyProjectFilter>? projectOutputs,
        out int exitCode)
    {
        options = new FuzzyQueryOptions("", [], "smart", null, false, null);
        projects = [];
        projectOutputs = null;
        exitCode = ExitCodes.Success;

        bool hasQuery = !string.IsNullOrWhiteSpace(query);
        bool hasCandidateId = !string.IsNullOrWhiteSpace(candidateId);
        if (hasQuery == hasCandidateId)
        {
            DiagnosticReporter.WriteError(DiagnosticIds.ParseError, "Specify exactly one fuzzy selection input: --query or --candidate-id.");
            exitCode = ExitCodes.UsageError;
            return false;
        }

        if (!allowGroupPolicy && candidatePolicy == "group")
        {
            DiagnosticReporter.WriteError(DiagnosticIds.InvalidCandidatePolicy, "--candidate-policy group is not supported by this command.");
            exitCode = ExitCodes.UsageError;
            return false;
        }

        if (hasCandidateId)
        {
            string normalizedCandidateId = candidateId!.Trim();
            if (assumeKinds.Count > 0 || match != "smart" || caseSensitive)
            {
                DiagnosticReporter.WriteError(DiagnosticIds.ParseError, "--candidate-id cannot be combined with --assume-kind, --match, or --case-sensitive.");
                exitCode = ExitCodes.UsageError;
                return false;
            }

            if (!FuzzyCandidateIdentity.TryParseCandidateId(normalizedCandidateId))
            {
                DiagnosticReporter.WriteError(DiagnosticIds.InvalidCandidateId, $"Invalid candidate id: {normalizedCandidateId}.");
                exitCode = ExitCodes.UsageError;
                return false;
            }

            query = normalizedCandidateId;
            candidateId = normalizedCandidateId;
        }

        if (!TryCreateQuery(
            loadedWorkspace,
            query!,
            hasCandidateId ? [] : assumeKinds,
            hasCandidateId ? "smart" : match,
            hasCandidateId ? false : caseSensitive,
            projectFilters,
            excludeGenerated,
            limit,
            out options,
            out projects,
            out projectOutputs,
            out exitCode))
        {
            return false;
        }

        options = options with
        {
            CandidateId = candidateId,
            Selection = new FuzzySelectionOptions(candidatePolicy, minConfidence, explainSelection)
        };
        return true;
    }

    public static async Task<bool> TryValidateSelectionAsync(
        FuzzyDiscoveryResolver resolver,
        IReadOnlyList<Project> projects,
        FuzzyQueryOptions options,
        CancellationToken cancellationToken)
    {
        FuzzyCandidateResolution resolution = await resolver.ResolveCandidatesForSelectionAsync(
            projects,
            options,
            cancellationToken);

        if (resolution.Error is null)
        {
            return true;
        }

        DiagnosticReporter.WriteError(resolution.Error.DiagnosticId, resolution.Error.Message);
        return false;
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

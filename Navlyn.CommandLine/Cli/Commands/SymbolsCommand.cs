using System.CommandLine;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Navlyn.Diagnostics;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class SymbolsCommand
{
    public static Command Create()
    {
        Option<string> queryOption = SharedOptions.CreateQueryOption();
        Option<string> matchOption = SharedOptions.CreateMatchOption();
        Option<bool> caseSensitiveOption = SharedOptions.CreateCaseSensitiveOption();
        Option<int?> limitOption = SharedOptions.CreateLimitOption();
        Option<string[]> kindOption = SharedOptions.CreateKindOption();
        Option<string[]> projectOption = SharedOptions.CreateProjectFiltersOption();
        Option<bool> excludeGeneratedOption = SharedOptions.CreateExcludeGeneratedOption();
        Option<string[]> namespaceOption = new("--namespace")
        {
            Description = "Restrict matches to containing namespaces. Can be specified more than once.",
            AllowMultipleArgumentsPerToken = true
        };
        Option<string> namespaceMatchOption = CreateFilterMatchOption("--namespace-match", "Namespace match mode: contains, exact, or regex.");
        Option<string[]> containerOption = new("--container")
        {
            Description = "Restrict matches to containing symbol display strings. Can be specified more than once.",
            AllowMultipleArgumentsPerToken = true
        };
        Option<string> containerMatchOption = CreateFilterMatchOption("--container-match", "Container match mode: contains, exact, or regex.");
        Option<string[]> accessibilityOption = new("--accessibility")
        {
            Description = "Restrict matches to a Roslyn accessibility string. Can be specified more than once.",
            AllowMultipleArgumentsPerToken = true
        };

        return WorkspaceCommand.Create(
            "symbols",
            "Find C# symbol declarations by name.",
            [
                queryOption,
                matchOption,
                caseSensitiveOption,
                limitOption,
                kindOption,
                projectOption,
                excludeGeneratedOption,
                namespaceOption,
                namespaceMatchOption,
                containerOption,
                containerMatchOption,
                accessibilityOption
            ],
            (workspace, parseResult, cancellationToken) => ExecuteAsync(
                workspace,
                parseResult.GetValue(queryOption)!,
                parseResult.GetValue(matchOption)!,
                parseResult.GetValue(caseSensitiveOption),
                parseResult.GetValue(limitOption),
                parseResult.GetValue(kindOption) ?? [],
                parseResult.GetValue(projectOption) ?? [],
                parseResult.GetValue(excludeGeneratedOption),
                parseResult.GetValue(namespaceOption) ?? [],
                parseResult.GetValue(namespaceMatchOption)!,
                parseResult.GetValue(containerOption) ?? [],
                parseResult.GetValue(containerMatchOption)!,
                parseResult.GetValue(accessibilityOption) ?? [],
                cancellationToken));
    }

    private static async Task<int> ExecuteAsync(
        LoadedWorkspace loadedWorkspace,
        string query,
        string match,
        bool caseSensitive,
        int? limit,
        IReadOnlyList<string> kinds,
        IReadOnlyList<string> projectFilters,
        bool excludeGenerated,
        IReadOnlyList<string> namespaceFilters,
        string namespaceMatch,
        IReadOnlyList<string> containerFilters,
        string containerMatch,
        IReadOnlyList<string> accessibilityFilters,
        CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            DiagnosticReporter.WriteError(DiagnosticIds.InvalidLimit, "--limit must be 1 or greater.");
            return ExitCodes.UsageError;
        }

        string? kindError = GetKindError(kinds);
        if (kindError is not null)
        {
            DiagnosticReporter.WriteError(
                DiagnosticIds.InvalidSymbolKind,
                kindError);
            return ExitCodes.UsageError;
        }

        string? accessibilityError = GetAccessibilityError(accessibilityFilters);
        if (accessibilityError is not null)
        {
            DiagnosticReporter.WriteError(
                DiagnosticIds.InvalidSymbolKind,
                accessibilityError);
            return ExitCodes.UsageError;
        }

        ProjectFilterResolutionResult projectResult =
            new ProjectFilterResolver().ResolveMany(loadedWorkspace.Solution, projectFilters);
        if (projectResult.Error is not null)
        {
            DiagnosticReporter.WriteError(projectResult.Error.DiagnosticId, projectResult.Error.Message);
            return projectResult.Error.ExitCode;
        }

        IReadOnlyList<string> normalizedKinds = NormalizeKinds(kinds);
        IReadOnlyList<string> normalizedNamespaces = NormalizeStrings(namespaceFilters);
        IReadOnlyList<string> normalizedContainers = NormalizeStrings(containerFilters);
        IReadOnlyList<string> normalizedAccessibilities = NormalizeStrings(accessibilityFilters);

        SymbolSearchOptions options = new(
            Query: query,
            MatchMode: ParseMatchMode(match),
            CaseSensitive: caseSensitive);

        if (!SymbolNameMatcher.TryCreate(options, out SymbolNameMatcher matcher, out string? errorMessage))
        {
            DiagnosticReporter.WriteError(DiagnosticIds.InvalidRegex, errorMessage!);
            return ExitCodes.UsageError;
        }

        if (!TryCreateMatchers(normalizedNamespaces, namespaceMatch, caseSensitive, out IReadOnlyList<SymbolNameMatcher> namespaceMatchers, out errorMessage) ||
            !TryCreateMatchers(normalizedContainers, containerMatch, caseSensitive, out IReadOnlyList<SymbolNameMatcher> containerMatchers, out errorMessage))
        {
            DiagnosticReporter.WriteError(DiagnosticIds.InvalidRegex, errorMessage!);
            return ExitCodes.UsageError;
        }

        IReadOnlyList<SymbolDeclaration> declarations =
            await new SymbolDeclarationFinder().FindAsync(projectResult.Projects, matcher, excludeGenerated, cancellationToken);

        IReadOnlyList<SymbolDeclaration> filteredDeclarations = FilterDeclarations(
            declarations,
            normalizedKinds,
            namespaceMatchers,
            containerMatchers,
            normalizedAccessibilities);
        IReadOnlyList<SymbolDeclaration> limitedDeclarations = limit is null
            ? filteredDeclarations
            : [.. filteredDeclarations.Take(limit.Value)];

        ConsoleJsonWriter.Write(new SymbolsResult(
            Query: query,
            Match: match,
            CaseSensitive: caseSensitive,
            Kinds: normalizedKinds,
            Namespaces: normalizedNamespaces.Count == 0 ? null : normalizedNamespaces,
            NamespaceMatch: normalizedNamespaces.Count == 0 ? null : namespaceMatch,
            Containers: normalizedContainers.Count == 0 ? null : normalizedContainers,
            ContainerMatch: normalizedContainers.Count == 0 ? null : containerMatch,
            Accessibilities: normalizedAccessibilities.Count == 0 ? null : normalizedAccessibilities,
            Projects: projectResult.AppliedFilters.Count == 0
                ? null
                : projectResult.AppliedFilters.Select(project => new ProjectFilterResult(
                    Filter: project.Filter,
                    Name: project.Name,
                    Path: project.Path,
                    TargetFramework: project.TargetFramework)).ToArray(),
            ExcludeGenerated: excludeGenerated,
            Limit: limit,
            TotalMatches: filteredDeclarations.Count,
            Matches: limitedDeclarations.Select(declaration => new SymbolMatch(
                Name: declaration.Name,
                Kind: declaration.Kind,
                Container: declaration.Container,
                Facts: declaration.Facts,
                Path: declaration.Path,
                Line: declaration.Line,
                Column: declaration.Column,
                EndLine: declaration.EndLine,
                EndColumn: declaration.EndColumn)).ToArray()));

        return ExitCodes.Success;
    }

    private static IReadOnlyList<SymbolDeclaration> FilterDeclarations(
        IReadOnlyList<SymbolDeclaration> declarations,
        IReadOnlyList<string> kinds,
        IReadOnlyList<SymbolNameMatcher> namespaceMatchers,
        IReadOnlyList<SymbolNameMatcher> containerMatchers,
        IReadOnlyList<string> accessibilities)
    {
        HashSet<string> kindSet = [.. kinds];
        HashSet<string> accessibilitySet = [.. accessibilities];
        return [.. declarations.Where(declaration =>
            (kindSet.Count == 0 || kindSet.Contains(declaration.Kind)) &&
            MatchesAny(namespaceMatchers, declaration.Facts.Namespace) &&
            MatchesAny(containerMatchers, declaration.Container) &&
            (accessibilitySet.Count == 0 ||
                (declaration.Facts.Accessibility is not null && accessibilitySet.Contains(declaration.Facts.Accessibility))))];
    }

    private static IReadOnlyList<string> NormalizeKinds(IReadOnlyList<string> kinds)
    {
        return [.. kinds
            .Where(kind => !string.IsNullOrWhiteSpace(kind))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(kind => kind, StringComparer.Ordinal)];
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

            if (!IsKnownSymbolKind(kind))
            {
                return $"Unknown symbol kind: {kind}.";
            }
        }

        return null;
    }

    private static string? GetAccessibilityError(IReadOnlyList<string> accessibilities)
    {
        foreach (string accessibility in accessibilities)
        {
            if (string.IsNullOrWhiteSpace(accessibility))
            {
                return "Accessibility filter must not be empty.";
            }

            if (!Enum.GetNames<Accessibility>().Contains(accessibility, StringComparer.Ordinal))
            {
                return $"Unknown accessibility: {accessibility}.";
            }
        }

        return null;
    }

    private static bool IsKnownSymbolKind(string kind)
    {
        return Enum.GetNames<SymbolKind>().Contains(kind, StringComparer.Ordinal);
    }

    private static SymbolMatchMode ParseMatchMode(string match)
    {
        return match switch
        {
            "contains" => SymbolMatchMode.Contains,
            "exact" => SymbolMatchMode.Exact,
            "regex" => SymbolMatchMode.Regex,
            _ => throw new InvalidOperationException($"Unexpected match mode: {match}")
        };
    }

    private static Option<string> CreateFilterMatchOption(string name, string description)
    {
        Option<string> option = new(name)
        {
            Description = description,
            DefaultValueFactory = _ => "contains"
        };

        option.AcceptOnlyFromAmong("contains", "exact", "regex");
        return option;
    }

    private static bool TryCreateMatchers(
        IReadOnlyList<string> values,
        string match,
        bool caseSensitive,
        out IReadOnlyList<SymbolNameMatcher> matchers,
        out string? errorMessage)
    {
        List<SymbolNameMatcher> createdMatchers = [];
        foreach (string value in values)
        {
            SymbolSearchOptions options = new(
                Query: value,
                MatchMode: ParseMatchMode(match),
                CaseSensitive: caseSensitive);

            if (!SymbolNameMatcher.TryCreate(options, out SymbolNameMatcher matcher, out errorMessage))
            {
                matchers = [];
                return false;
            }

            createdMatchers.Add(matcher);
        }

        matchers = createdMatchers;
        errorMessage = null;
        return true;
    }

    private static bool MatchesAny(IReadOnlyList<SymbolNameMatcher> matchers, string? value)
    {
        return matchers.Count == 0 ||
            (value is not null && matchers.Any(matcher => matcher.IsMatch(value)));
    }

    private sealed record SymbolsResult(
        string Query,
        string Match,
        bool CaseSensitive,
        IReadOnlyList<string> Kinds,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<string>? Namespaces,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? NamespaceMatch,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<string>? Containers,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? ContainerMatch,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<string>? Accessibilities,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<ProjectFilterResult>? Projects,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool ExcludeGenerated,
        int? Limit,
        int TotalMatches,
        IReadOnlyList<SymbolMatch> Matches);

    private sealed record ProjectFilterResult(
        string Filter,
        string Name,
        string? Path,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? TargetFramework);

    private sealed record SymbolMatch(
        string Name,
        string Kind,
        string? Container,
        SymbolFacts Facts,
        string Path,
        int Line,
        int Column,
        int EndLine,
        int EndColumn);
}

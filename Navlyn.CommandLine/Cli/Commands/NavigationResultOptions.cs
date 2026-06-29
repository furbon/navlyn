using System.CommandLine;
using Microsoft.CodeAnalysis;
using Navlyn.Diagnostics;
using Navlyn.Paths;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class NavigationResultOptions
{
    public static Option<string[]> CreateResultProjectOption()
    {
        return new Option<string[]>("--result-project")
        {
            Description = "Restrict result locations to a project name or repository-relative .csproj path. Can be specified more than once.",
            AllowMultipleArgumentsPerToken = true
        };
    }

    public static Option<string[]> CreateResultPathOption()
    {
        return new Option<string[]>("--result-path")
        {
            Description = "Restrict result locations to paths containing this repository-relative path fragment. Can be specified more than once.",
            AllowMultipleArgumentsPerToken = true
        };
    }

    public static Option<string[]> CreateResultKindOption()
    {
        return new Option<string[]>("--result-kind")
        {
            Description = "Restrict result symbols to a case-sensitive symbol kind string. Can be specified more than once.",
            AllowMultipleArgumentsPerToken = true
        };
    }

    public static bool TryCreate(
        LoadedWorkspace loadedWorkspace,
        IReadOnlyList<string> resultProjectFilters,
        IReadOnlyList<string> resultPaths,
        IReadOnlyList<string> resultKinds,
        int? limit,
        out NavigationResultFilter filter,
        out int exitCode)
    {
        filter = default!;
        exitCode = ExitCodes.Success;

        if (limit <= 0)
        {
            DiagnosticReporter.WriteError(DiagnosticIds.InvalidLimit, "--limit must be 1 or greater.");
            exitCode = ExitCodes.UsageError;
            return false;
        }

        string? kindError = GetKindError(resultKinds);
        if (kindError is not null)
        {
            DiagnosticReporter.WriteError(DiagnosticIds.InvalidSymbolKind, kindError);
            exitCode = ExitCodes.UsageError;
            return false;
        }

        ProjectFilterResolutionResult projectResult =
            new ProjectFilterResolver().ResolveMany(loadedWorkspace.Solution, resultProjectFilters);
        if (projectResult.Error is not null)
        {
            DiagnosticReporter.WriteError(projectResult.Error.DiagnosticId, projectResult.Error.Message);
            exitCode = projectResult.Error.ExitCode;
            return false;
        }

        IReadOnlyList<string> normalizedPaths = NormalizePaths(resultPaths);
        IReadOnlyList<string> normalizedKinds = NormalizeKinds(resultKinds);

        filter = new NavigationResultFilter(
            Projects: projectResult.Projects,
            AppliedProjectFilters: projectResult.AppliedFilters,
            PathFilters: normalizedPaths,
            KindFilters: normalizedKinds,
            Limit: limit);
        return true;
    }

    public static IReadOnlyList<T> ApplyLimit<T>(IReadOnlyList<T> items, int? limit)
    {
        return limit is null ? items : [.. items.Take(limit.Value)];
    }

    public static bool MatchesLocation(NavigationResultFilter filter, string path)
    {
        return MatchesProject(filter, path) && MatchesPath(filter, path);
    }

    public static bool MatchesSymbol(NavigationResultFilter filter, string path, string kind)
    {
        return MatchesLocation(filter, path) && MatchesKind(filter, kind);
    }

    public static bool MatchesSymbolOrMetadata(NavigationResultFilter filter, string? path, string kind)
    {
        if (path is not null)
        {
            return MatchesSymbol(filter, path, kind);
        }

        return filter.AppliedProjectFilters.Count == 0 &&
            filter.PathFilters.Count == 0 &&
            MatchesKind(filter, kind);
    }

    private static bool MatchesProject(NavigationResultFilter filter, string path)
    {
        if (filter.AppliedProjectFilters.Count == 0)
        {
            return true;
        }

        return filter.Projects.Any(project => ProjectContainsPath(project, path));
    }

    private static bool ProjectContainsPath(Project project, string path)
    {
        return project.Documents.Any(document =>
            document.FilePath is not null &&
            string.Equals(
                PathDisplay.FromCurrentDirectory(document.FilePath),
                path,
                GetPathStringComparison()));
    }

    private static bool MatchesPath(NavigationResultFilter filter, string path)
    {
        if (filter.PathFilters.Count == 0)
        {
            return true;
        }

        string normalizedPath = NormalizePath(path);
        return filter.PathFilters.Any(filterPath =>
            normalizedPath.Contains(filterPath, GetPathStringComparison()));
    }

    private static bool MatchesKind(NavigationResultFilter filter, string kind)
    {
        return filter.KindFilters.Count == 0 || filter.KindFilters.Contains(kind);
    }

    private static IReadOnlyList<string> NormalizePaths(IReadOnlyList<string> paths)
    {
        return [.. paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizePath)
            .Distinct(GetPathStringComparer())
            .OrderBy(path => path, GetPathStringComparer())];
    }

    private static string NormalizePath(string path)
    {
        return path.Trim()
            .Replace('\\', '/')
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static StringComparison GetPathStringComparison()
    {
        return OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }

    private static StringComparer GetPathStringComparer()
    {
        return OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
    }

    private static IReadOnlyList<string> NormalizeKinds(IReadOnlyList<string> kinds)
    {
        return [.. kinds
            .Where(kind => !string.IsNullOrWhiteSpace(kind))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(kind => kind, StringComparer.Ordinal)];
    }

    private static string? GetKindError(IReadOnlyList<string> kinds)
    {
        foreach (string kind in kinds)
        {
            if (string.IsNullOrWhiteSpace(kind))
            {
                return "Result symbol kind must not be empty.";
            }

            if (!Enum.GetNames<SymbolKind>().Contains(kind, StringComparer.Ordinal))
            {
                return $"Unknown result symbol kind: {kind}.";
            }
        }

        return null;
    }
}

internal sealed record NavigationResultFilter(
    IReadOnlyList<Project> Projects,
    IReadOnlyList<AppliedProjectFilter> AppliedProjectFilters,
    IReadOnlyList<string> PathFilters,
    IReadOnlyList<string> KindFilters,
    int? Limit);

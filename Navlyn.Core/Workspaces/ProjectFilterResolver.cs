using Microsoft.CodeAnalysis;
using Navlyn.Diagnostics;
using Navlyn.Paths;

namespace Navlyn.Workspaces;

internal sealed class ProjectFilterResolver
{
    public ProjectFilterResolutionResult ResolveMany(
        Solution solution,
        IReadOnlyList<string> filters)
    {
        IReadOnlyList<Project> orderedProjects = GetProjects(solution);
        if (filters.Count == 0)
        {
            return ProjectFilterResolutionResult.Succeeded(orderedProjects, appliedFilters: []);
        }

        List<Project> selectedProjects = [];
        List<AppliedProjectFilter> appliedFilters = [];

        foreach (string filter in filters)
        {
            ProjectFilterResolutionResult filterResult = ResolveOne(orderedProjects, filter);
            if (filterResult.Error is not null)
            {
                return filterResult;
            }

            Project project = filterResult.Projects.Single();
            if (!selectedProjects.Any(existing => existing.Id == project.Id))
            {
                selectedProjects.Add(project);
            }

            appliedFilters.Add(filterResult.AppliedFilters.Single());
        }

        return ProjectFilterResolutionResult.Succeeded(selectedProjects, appliedFilters);
    }

    public ProjectFilterResolutionResult ResolveSingle(Solution solution, string? filter)
    {
        IReadOnlyList<Project> orderedProjects = GetProjects(solution);
        return string.IsNullOrWhiteSpace(filter)
            ? ProjectFilterResolutionResult.Succeeded(orderedProjects, appliedFilters: [])
            : ResolveOne(orderedProjects, filter);
    }

    private static ProjectFilterResolutionResult ResolveOne(
        IReadOnlyList<Project> projects,
        string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return ProjectFilterResolutionResult.Failed(
                DiagnosticIds.InvalidProjectFilter,
                "Project filter must not be empty.",
                ExitCodes.UsageError);
        }

        string trimmedFilter = filter.Trim();
        IReadOnlyList<Project> pathMatches = FindPathMatches(projects, trimmedFilter);
        if (pathMatches.Count == 1)
        {
            Project project = pathMatches[0];
            return ProjectFilterResolutionResult.Succeeded(
                [project],
                [CreateAppliedFilter(trimmedFilter, project)]);
        }

        if (pathMatches.Count > 1)
        {
            return ProjectFilterResolutionResult.Failed(
                DiagnosticIds.AmbiguousProjectFilter,
                $"Project filter is ambiguous: {trimmedFilter}",
                ExitCodes.UsageError);
        }

        if (LooksLikeProjectPath(trimmedFilter))
        {
            return ProjectFilterResolutionResult.Failed(
                DiagnosticIds.UnknownProjectFilter,
                $"Project filter did not match any project: {trimmedFilter}",
                ExitCodes.UsageError);
        }

        IReadOnlyList<Project> nameMatches = [.. projects
            .Where(project => string.Equals(project.Name, trimmedFilter, StringComparison.Ordinal))];

        if (nameMatches.Count == 1)
        {
            Project project = nameMatches[0];
            return ProjectFilterResolutionResult.Succeeded(
                [project],
                [CreateAppliedFilter(trimmedFilter, project)]);
        }

        if (nameMatches.Count > 1)
        {
            return ProjectFilterResolutionResult.Failed(
                DiagnosticIds.AmbiguousProjectFilter,
                $"Project name matched multiple projects: {trimmedFilter}",
                ExitCodes.UsageError);
        }

        return ProjectFilterResolutionResult.Failed(
            DiagnosticIds.UnknownProjectFilter,
            $"Project filter did not match any project: {trimmedFilter}",
            ExitCodes.UsageError);
    }

    private static IReadOnlyList<Project> GetProjects(Solution solution)
    {
        return [.. solution.Projects
            .OrderBy(project => project.FilePath, StringComparer.Ordinal)
            .ThenBy(project => project.Name, StringComparer.Ordinal)];
    }

    private static IReadOnlyList<Project> FindPathMatches(
        IReadOnlyList<Project> projects,
        string filter)
    {
        IReadOnlyList<string> filterFullPaths;
        try
        {
            filterFullPaths = PathDisplay.GetInputPathCandidates(filter, GetWorkspaceAnchorPath(projects));
        }
        catch
        {
            return [];
        }

        StringComparer pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        return [.. projects.Where(project =>
            project.FilePath is not null &&
            filterFullPaths.Any(filterFullPath => pathComparer.Equals(Path.GetFullPath(project.FilePath), filterFullPath)))];
    }

    private static bool LooksLikeProjectPath(string filter)
    {
        return filter.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            filter.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal) ||
            string.Equals(Path.GetExtension(filter), ".csproj", StringComparison.OrdinalIgnoreCase);
    }

    private static AppliedProjectFilter CreateAppliedFilter(string filter, Project project)
    {
        return new AppliedProjectFilter(
            Filter: filter,
            Name: project.Name,
            Path: project.FilePath is null ? null : PathDisplay.FromCurrentDirectory(project.FilePath),
            TargetFramework: ProjectContextFacts.GetTargetFramework(project));
    }

    private static string? GetWorkspaceAnchorPath(IReadOnlyList<Project> projects)
    {
        return projects
            .Select(project => project.Solution.FilePath ?? project.FilePath)
            .FirstOrDefault(path => path is not null);
    }
}

internal sealed record ProjectFilterResolutionResult(
    IReadOnlyList<Project> Projects,
    IReadOnlyList<AppliedProjectFilter> AppliedFilters,
    ProjectFilterResolutionError? Error)
{
    public static ProjectFilterResolutionResult Succeeded(
        IReadOnlyList<Project> projects,
        IReadOnlyList<AppliedProjectFilter> appliedFilters)
    {
        return new ProjectFilterResolutionResult(projects, appliedFilters, Error: null);
    }

    public static ProjectFilterResolutionResult Failed(int diagnosticId, string message, int exitCode)
    {
        return new ProjectFilterResolutionResult(
            Projects: [],
            AppliedFilters: [],
            Error: new ProjectFilterResolutionError(diagnosticId, message, exitCode));
    }
}

internal sealed record AppliedProjectFilter(string Filter, string Name, string? Path, string? TargetFramework);

internal sealed record ProjectFilterResolutionError(int DiagnosticId, string Message, int ExitCode);

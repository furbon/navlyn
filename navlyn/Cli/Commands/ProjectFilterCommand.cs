using Microsoft.CodeAnalysis;
using Navlyn.Diagnostics;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class ProjectFilterCommand
{
    public static bool TryResolveSingleProject(
        LoadedWorkspace loadedWorkspace,
        string? filter,
        out Project? project,
        out AppliedProjectFilter? appliedFilter,
        out int exitCode)
    {
        ProjectFilterResolutionResult result =
            new ProjectFilterResolver().ResolveSingle(loadedWorkspace.Solution, filter);

        if (result.Error is not null)
        {
            DiagnosticReporter.WriteError(result.Error.DiagnosticId, result.Error.Message);
            project = null;
            appliedFilter = null;
            exitCode = result.Error.ExitCode;
            return false;
        }

        if (string.IsNullOrWhiteSpace(filter))
        {
            project = null;
            appliedFilter = null;
            exitCode = ExitCodes.Success;
            return true;
        }

        project = result.Projects.Single();
        appliedFilter = result.AppliedFilters.Single();
        exitCode = ExitCodes.Success;
        return true;
    }
}

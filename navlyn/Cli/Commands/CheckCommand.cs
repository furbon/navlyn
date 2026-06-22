using System.CommandLine;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class CheckCommand
{
    public static Command Create()
    {
        return WorkspaceCommand.Create(
            "check",
            "Validate that a workspace can be loaded.",
            ExecuteAsync);
    }

    private static Task<int> ExecuteAsync(
        LoadedWorkspace loadedWorkspace,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        ConsoleJsonWriter.Write(new CheckResult(
            Ok: true,
            Workspace: loadedWorkspace.DisplayPath,
            Kind: loadedWorkspace.Kind,
            Projects: loadedWorkspace.ProjectCount));

        return Task.FromResult(ExitCodes.Success);
    }

    private sealed record CheckResult(bool Ok, string Workspace, string Kind, int Projects);
}

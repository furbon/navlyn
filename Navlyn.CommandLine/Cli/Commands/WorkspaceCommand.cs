using System.CommandLine;
using System.CommandLine.Parsing;
using Navlyn.Diagnostics;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class WorkspaceCommand
{
    public static Command Create(
        string name,
        string description,
        Func<LoadedWorkspace, CancellationToken, Task<int>> executeAsync)
    {
        return Create(name, description, [], executeAsync);
    }

    public static Command Create(
        string name,
        string description,
        IEnumerable<Option> options,
        Func<LoadedWorkspace, ParseResult, CancellationToken, Task<int>> executeAsync)
    {
        return CreateWithWorkspaceInput(
            name,
            description,
            options,
            (workspace, _, parseResult, cancellationToken) => executeAsync(workspace, parseResult, cancellationToken));
    }

    public static Command CreateWithWorkspaceInput(
        string name,
        string description,
        IEnumerable<Option> options,
        Func<LoadedWorkspace, FileInfo, ParseResult, CancellationToken, Task<int>> executeAsync)
    {
        Option<FileInfo> workspaceOption = SharedOptions.CreateWorkspaceOption();
        Option<string?> workspaceRootPolicyOption = SharedOptions.CreateWorkspaceRootPolicyOption();

        Command command = new(name, description)
        {
            workspaceOption,
            workspaceRootPolicyOption
        };

        foreach (Option option in options)
        {
            command.Options.Add(option);
        }

        command.SetAction((ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            FileInfo workspace = parseResult.GetValue(workspaceOption)!;
            string? workspaceRootPolicy = parseResult.GetValue(workspaceRootPolicyOption);
            return ExecuteWithWorkspaceAsync(workspace, workspaceRootPolicy, parseResult, executeAsync, cancellationToken);
        });

        return command;
    }

    public static Command Create(
        string name,
        string description,
        IEnumerable<Option> options,
        Func<LoadedWorkspace, CancellationToken, Task<int>> executeAsync)
    {
        return Create(
            name,
            description,
            options,
            (workspace, _, cancellationToken) => executeAsync(workspace, cancellationToken));
    }

    private static async Task<int> ExecuteWithWorkspaceAsync(
        FileInfo workspace,
        string? workspaceRootPolicy,
        ParseResult parseResult,
        Func<LoadedWorkspace, FileInfo, ParseResult, CancellationToken, Task<int>> executeAsync,
        CancellationToken cancellationToken)
    {
        WorkspaceLoadOptions options = new(ParseWorkspaceRootPolicy(workspaceRootPolicy));
        WorkspaceLoadResult loadResult = await new WorkspaceLoader().LoadAsync(workspace, options, cancellationToken);

        foreach (WorkspaceLoadDiagnostic diagnostic in loadResult.Diagnostics)
        {
            DiagnosticReporter.WriteError(DiagnosticIds.WorkspaceDiagnostic, diagnostic.Kind, diagnostic.Message);
        }

        if (loadResult.Error is not null)
        {
            DiagnosticReporter.WriteError(loadResult.Error.DiagnosticId, loadResult.Error.Message);
            return loadResult.Error.ExitCode;
        }

        using LoadedWorkspace workspaceHandle = loadResult.Workspace!;
        return await executeAsync(workspaceHandle, workspace, parseResult, cancellationToken);
    }

    private static WorkspaceRootPolicy? ParseWorkspaceRootPolicy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return WorkspaceLoader.TryParseWorkspaceRootPolicy(value, out WorkspaceRootPolicy policy)
            ? policy
            : null;
    }
}

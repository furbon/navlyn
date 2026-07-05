using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.Json;
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
            return ExecuteWithWorkspaceAsync(name, workspace, workspaceRootPolicy, parseResult, executeAsync, cancellationToken);
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
        string commandName,
        FileInfo workspace,
        string? workspaceRootPolicy,
        ParseResult parseResult,
        Func<LoadedWorkspace, FileInfo, ParseResult, CancellationToken, Task<int>> executeAsync,
        CancellationToken cancellationToken)
    {
        WorkspaceTimingCollector? timing = IsTimingEnabled()
            ? new WorkspaceTimingCollector()
            : null;
        WorkspaceLoadOptions options = new(ParseWorkspaceRootPolicy(workspaceRootPolicy), timing);
        WorkspaceLoadResult loadResult;
        using (timing?.Measure("workspace.total-load"))
        {
            loadResult = await new WorkspaceLoader().LoadAsync(workspace, options, cancellationToken);
        }

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
        int exitCode;
        using (timing?.Measure("command.execute-and-serialize"))
        {
            exitCode = await executeAsync(workspaceHandle, workspace, parseResult, cancellationToken);
        }

        WriteTimingIfEnabled(commandName, timing);
        return exitCode;
    }

    private static bool IsTimingEnabled()
    {
        string? value = Environment.GetEnvironmentVariable("NAVLYN_PROFILE_TIMINGS");
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteTimingIfEnabled(string commandName, WorkspaceTimingCollector? timing)
    {
        if (timing is null)
        {
            return;
        }

        var payload = new
        {
            schemaVersion = "navlyn.timing.v1",
            command = commandName,
            stages = timing.Stages.Select(stage => new
            {
                name = stage.Name,
                elapsedMs = stage.ElapsedMs
            }).ToArray()
        };
        Console.Error.WriteLine($"NAVLYN_TIMING {JsonSerializer.Serialize(payload)}");
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

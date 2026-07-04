using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Navlyn.Cli;
using Navlyn.Diagnostics;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class ServeCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static Command Create()
    {
        Option<FileInfo> workspaceOption = SharedOptions.CreateWorkspaceOption();
        Option<string?> workspaceRootPolicyOption = SharedOptions.CreateWorkspaceRootPolicyOption();
        Option<string?> pipeOption = new("--pipe")
        {
            Description = "Local named pipe to serve. Omit to serve newline-delimited JSON on stdin/stdout."
        };
        Option<string> cacheOption = WorkspaceStatusCommand.CreateCacheModeOption();
        Option<string?> cacheDirectoryOption = WorkspaceStatusCommand.CreateCacheDirectoryOption();

        Command command = new("serve", "Run an opt-in local read-only workspace daemon for status and refresh requests.")
        {
            workspaceOption,
            workspaceRootPolicyOption,
            pipeOption,
            cacheOption,
            cacheDirectoryOption
        };

        command.SetAction((ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            return ExecuteAsync(
                parseResult.GetValue(workspaceOption)!,
                parseResult.GetValue(workspaceRootPolicyOption),
                parseResult.GetValue(pipeOption),
                parseResult.GetValue(cacheOption)!,
                parseResult.GetValue(cacheDirectoryOption),
                cancellationToken);
        });

        return command;
    }

    private static async Task<int> ExecuteAsync(
        FileInfo workspace,
        string? workspaceRootPolicy,
        string? pipeName,
        string cacheMode,
        string? cacheDirectory,
        CancellationToken cancellationToken)
    {
        WorkspaceLoadOptions options = new(ParseWorkspaceRootPolicy(workspaceRootPolicy));
        using WorkspaceSnapshotManager manager = new();

        if (string.IsNullOrWhiteSpace(pipeName))
        {
            return await RunStdioAsync(manager, workspace, options, cacheMode, cacheDirectory, cancellationToken);
        }

        Console.Error.WriteLine($"navlyn serve listening on local pipe '{pipeName}'.");
        while (!cancellationToken.IsCancellationRequested)
        {
            await using NamedPipeServerStream pipe = new(
                pipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            await pipe.WaitForConnectionAsync(cancellationToken);
            bool shutdown = await HandleDuplexStreamAsync(
                pipe,
                manager,
                workspace,
                options,
                cacheMode,
                cacheDirectory,
                cancellationToken);
            if (shutdown)
            {
                return ExitCodes.Success;
            }
        }

        return ExitCodes.Success;
    }

    private static async Task<int> RunStdioAsync(
        WorkspaceSnapshotManager manager,
        FileInfo workspace,
        WorkspaceLoadOptions options,
        string cacheMode,
        string? cacheDirectory,
        CancellationToken cancellationToken)
    {
        bool shutdown = await HandleStreamsAsync(
            Console.OpenStandardInput(),
            Console.OpenStandardOutput(),
            manager,
            workspace,
            options,
            cacheMode,
            cacheDirectory,
            cancellationToken);
        return shutdown ? ExitCodes.Success : ExitCodes.Success;
    }

    private static Task<bool> HandleDuplexStreamAsync(
        Stream stream,
        WorkspaceSnapshotManager manager,
        FileInfo workspace,
        WorkspaceLoadOptions options,
        string cacheMode,
        string? cacheDirectory,
        CancellationToken cancellationToken)
    {
        return HandleStreamsAsync(stream, stream, manager, workspace, options, cacheMode, cacheDirectory, cancellationToken);
    }

    private static async Task<bool> HandleStreamsAsync(
        Stream input,
        Stream output,
        WorkspaceSnapshotManager manager,
        FileInfo workspace,
        WorkspaceLoadOptions options,
        string cacheMode,
        string? cacheDirectory,
        CancellationToken cancellationToken)
    {
        using StreamReader reader = new(input, Utf8NoBom, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        await using StreamWriter writer = new(output, Utf8NoBom, bufferSize: 1024, leaveOpen: true) { AutoFlush = true };

        while (!cancellationToken.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            WorkspaceDaemonResponse response;
            bool shutdown = false;
            try
            {
                WorkspaceDaemonRequest? request = WorkspaceDaemonProtocol.DeserializeRequest(line);
                if (request is null)
                {
                    response = CreateError(null, "NAVLYN_DAEMON_INVALID_REQUEST", "Request must be a JSON object.");
                }
                else if (request.Method == WorkspaceDaemonProtocol.ShutdownMethod)
                {
                    response = CreateSuccess(request.Id, new { status = "shutdown" });
                    shutdown = true;
                }
                else
                {
                    response = await ExecuteRequestAsync(
                        manager,
                        workspace,
                        options,
                        cacheMode,
                        cacheDirectory,
                        request,
                        cancellationToken);
                }
            }
            catch (JsonException ex)
            {
                response = CreateError(null, "NAVLYN_DAEMON_INVALID_JSON", ex.Message);
            }
            catch (OperationCanceledException)
            {
                response = CreateError(null, "NAVLYN_DAEMON_CANCELED", "Request was canceled.");
            }
            catch (Exception ex)
            {
                response = CreateError(null, "NAVLYN_DAEMON_ERROR", ex.Message);
            }

            await writer.WriteLineAsync(WorkspaceDaemonProtocol.SerializeResponse(response));
            if (shutdown)
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<WorkspaceDaemonResponse> ExecuteRequestAsync(
        WorkspaceSnapshotManager manager,
        FileInfo workspace,
        WorkspaceLoadOptions options,
        string cacheMode,
        string? cacheDirectory,
        WorkspaceDaemonRequest request,
        CancellationToken cancellationToken)
    {
        bool refresh = request.Method == WorkspaceDaemonProtocol.RefreshMethod;
        if (!refresh && request.Method != WorkspaceDaemonProtocol.StatusMethod)
        {
            return CreateError(request.Id, "NAVLYN_DAEMON_UNKNOWN_METHOD", $"Unknown daemon method: {request.Method}.");
        }

        WorkspaceSnapshotManagerResult loadResult = refresh
            ? await manager.RefreshAsync(workspace, options, cancellationToken)
            : await manager.GetAsync(workspace, options, cancellationToken);
        if (loadResult.Error is not null)
        {
            return CreateError(
                request.Id,
                $"{DiagnosticIds.Prefix}{loadResult.Error.DiagnosticId.ToString("D4", System.Globalization.CultureInfo.InvariantCulture)}",
                loadResult.Error.Message);
        }

        WorkspaceDiskCacheRequest cacheRequest = request.Cache ?? new WorkspaceDiskCacheRequest(
            cacheMode,
            Write: false,
            Clear: false,
            DirectoryOverride: cacheDirectory);
        WorkspaceStatusResult status = await WorkspaceDiskCache.CreateStatusAsync(
            refresh ? "workspace-refresh" : "workspace-status",
            loadResult.Snapshot!.Workspace,
            loadResult.Snapshot,
            workspace,
            cacheRequest,
            cancellationToken);
        return CreateSuccess(request.Id, status);
    }

    private static WorkspaceDaemonResponse CreateSuccess<T>(string? id, T result)
    {
        JsonElement element = JsonSerializer.SerializeToElement(result, JsonOptions);
        return new WorkspaceDaemonResponse(id, Ok: true, element, Error: null);
    }

    private static WorkspaceDaemonResponse CreateError(string? id, string code, string message)
    {
        return new WorkspaceDaemonResponse(id, Ok: false, Result: null, Error: new WorkspaceDaemonError(code, message));
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

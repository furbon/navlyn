using System.CommandLine;
using System.CommandLine.Parsing;
using Navlyn.Cli;
using Navlyn.Diagnostics;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class WorkspaceStatusCommand
{
    public static Command Create()
    {
        Option<string> cacheOption = CreateCacheModeOption();
        Option<string?> cacheDirectoryOption = CreateCacheDirectoryOption();
        Option<string?> daemonPipeOption = CreateDaemonPipeOption();

        return WorkspaceCommand.CreateWithWorkspaceInput(
            "workspace-status",
            "Report workspace snapshot, freshness, and optional on-disk cache status.",
            [cacheOption, cacheDirectoryOption, daemonPipeOption],
            async (workspace, workspaceInput, parseResult, cancellationToken) =>
            {
                return await ExecuteAsync(
                    workspace,
                    workspaceInput,
                    parseResult.GetValue(cacheOption)!,
                    parseResult.GetValue(cacheDirectoryOption),
                    parseResult.GetValue(daemonPipeOption),
                    cancellationToken);
            });
    }

    internal static Option<string> CreateCacheModeOption()
    {
        Option<string> option = new("--cache")
        {
            Description = "On-disk cache mode: auto, on, or off. Auto honors navlyn.workspace.json cacheHints.",
            DefaultValueFactory = _ => WorkspaceDiskCache.DefaultCacheMode
        };

        option.AcceptOnlyFromAmong("auto", "on", "off");
        return option;
    }

    internal static Option<string?> CreateCacheDirectoryOption()
    {
        return new Option<string?>("--cache-directory")
        {
            Description = "Override the on-disk cache directory."
        };
    }

    internal static Option<string?> CreateDaemonPipeOption()
    {
        return new Option<string?>("--daemon-pipe")
        {
            Description = "Connect to an opt-in local navlyn serve named pipe before using stateless fallback."
        };
    }

    internal static async Task<int> ExecuteAsync(
        LoadedWorkspace workspace,
        FileInfo workspaceInput,
        string cacheMode,
        string? cacheDirectory,
        string? daemonPipe,
        CancellationToken cancellationToken)
    {
        WorkspaceDiskCacheRequest request = new(cacheMode, Write: false, Clear: false, DirectoryOverride: cacheDirectory);
        if (!string.IsNullOrWhiteSpace(daemonPipe))
        {
            WorkspaceDaemonClientResult daemonResult = await WorkspaceDaemonProtocol.SendAsync(
                daemonPipe,
                new WorkspaceDaemonRequest(null, WorkspaceDaemonProtocol.StatusMethod, request),
                WorkspaceDaemonProtocol.DefaultConnectTimeoutMilliseconds,
                cancellationToken);
            if (daemonResult.Response?.Ok == true)
            {
                ConsoleJsonWriter.Write(CreateDaemonEnvelope(daemonResult.Response.Result, connected: true, error: null));
                return ExitCodes.Success;
            }

            DiagnosticReporter.WriteError(
                DiagnosticIds.WorkspaceDiagnostic,
                "Warning",
                daemonResult.Error ?? daemonResult.Response?.Error?.Message ?? "Daemon returned an error; using stateless fallback.");
        }

        WorkspaceStatusResult result = await WorkspaceDiskCache.CreateStatusAsync(
            "workspace-status",
            workspace,
            snapshot: null,
            workspaceInput,
            request,
            cancellationToken);
        ConsoleJsonWriter.Write(CreateDaemonEnvelope(
            result,
            configured: !string.IsNullOrWhiteSpace(daemonPipe),
            connected: false,
            error: null));
        return ExitCodes.Success;
    }

    private static object CreateDaemonEnvelope<T>(T result, bool connected, string? error)
    {
        return CreateDaemonEnvelope(result, configured: connected || error is not null, connected, error);
    }

    private static object CreateDaemonEnvelope<T>(T result, bool configured, bool connected, string? error)
    {
        return new
        {
            daemon = new
            {
                configured,
                connected,
                fallback = connected ? null : "stateless",
                error
            },
            result
        };
    }
}

internal static class WorkspaceRefreshCommand
{
    public static Command Create()
    {
        Option<string> cacheOption = WorkspaceStatusCommand.CreateCacheModeOption();
        Option<string?> cacheDirectoryOption = WorkspaceStatusCommand.CreateCacheDirectoryOption();
        Option<string?> daemonPipeOption = WorkspaceStatusCommand.CreateDaemonPipeOption();
        Option<bool> clearCacheOption = new("--clear-cache")
        {
            Description = "Remove the current on-disk workspace cache manifest before reporting or writing cache state."
        };
        Option<bool> writeCacheOption = new("--write-cache")
        {
            Description = "Write a fresh on-disk workspace cache manifest. Use --cache on unless cacheHints enables it."
        };

        return WorkspaceCommand.CreateWithWorkspaceInput(
            "workspace-refresh",
            "Force a fresh workspace load and optionally refresh the on-disk cache manifest.",
            [cacheOption, cacheDirectoryOption, daemonPipeOption, clearCacheOption, writeCacheOption],
            async (workspace, workspaceInput, parseResult, cancellationToken) =>
            {
                return await ExecuteAsync(
                    workspace,
                    workspaceInput,
                    parseResult.GetValue(cacheOption)!,
                    parseResult.GetValue(cacheDirectoryOption),
                    parseResult.GetValue(daemonPipeOption),
                    parseResult.GetValue(clearCacheOption),
                    parseResult.GetValue(writeCacheOption),
                    cancellationToken);
            });
    }

    private static async Task<int> ExecuteAsync(
        LoadedWorkspace workspace,
        FileInfo workspaceInput,
        string cacheMode,
        string? cacheDirectory,
        string? daemonPipe,
        bool clearCache,
        bool writeCache,
        CancellationToken cancellationToken)
    {
        WorkspaceDiskCacheRequest request = new(cacheMode, writeCache, clearCache, cacheDirectory);
        if (!string.IsNullOrWhiteSpace(daemonPipe))
        {
            WorkspaceDaemonClientResult daemonResult = await WorkspaceDaemonProtocol.SendAsync(
                daemonPipe,
                new WorkspaceDaemonRequest(null, WorkspaceDaemonProtocol.RefreshMethod, request),
                WorkspaceDaemonProtocol.DefaultConnectTimeoutMilliseconds,
                cancellationToken);
            if (daemonResult.Response?.Ok == true)
            {
                ConsoleJsonWriter.Write(new { daemon = new { configured = true, connected = true }, result = daemonResult.Response.Result });
                return ExitCodes.Success;
            }

            DiagnosticReporter.WriteError(
                DiagnosticIds.WorkspaceDiagnostic,
                "Warning",
                daemonResult.Error ?? daemonResult.Response?.Error?.Message ?? "Daemon returned an error; using stateless fallback.");
        }

        WorkspaceStatusResult result = await WorkspaceDiskCache.CreateStatusAsync(
            "workspace-refresh",
            workspace,
            snapshot: null,
            workspaceInput,
            request,
            cancellationToken);
        ConsoleJsonWriter.Write(new
        {
            daemon = new
            {
                configured = !string.IsNullOrWhiteSpace(daemonPipe),
                connected = false,
                fallback = "stateless"
            },
            result
        });
        return ExitCodes.Success;
    }
}

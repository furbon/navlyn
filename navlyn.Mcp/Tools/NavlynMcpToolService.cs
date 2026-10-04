using Navlyn.Mcp.Execution;
using Navlyn.Mcp.Configuration;
using System.Diagnostics;

namespace Navlyn.Mcp.Tools;

internal sealed class NavlynMcpToolService(
    INavlynCommandAdapter commandAdapter,
    NavlynMcpDirectToolRunner directToolRunner,
    NavlynMcpServerOptions options)
{
    public async Task<NavlynToolResult> RunAsync(
        string toolName,
        CommandBuildResult command,
        CancellationToken cancellationToken)
    {
        if (!command.IsValid)
        {
            return NavlynToolResult.Failed(
                toolName,
                sourceCommand: null,
                workspace: options.WorkspaceArgument,
                new NavlynToolError("NAVLYN_MCP_INVALID_ARGUMENT", command.Error ?? "Invalid tool arguments."));
        }

        if (Navlyn.Workspaces.WorkspaceSelectionScope.CurrentTargetFramework is string framework &&
            !command.Arguments.Contains("--target-framework"))
        {
            command = CommandBuildResult.Valid(command.Command!, [.. command.Arguments, "--target-framework", framework], command.StandardInput);
        }
        if (Navlyn.Workspaces.WorkspaceSelectionScope.CurrentTypeKind is string typeKind && command.Arguments.Contains("--query"))
            command = CommandBuildResult.Valid(command.Command!, [.. command.Arguments, "--type-kind", typeKind], command.StandardInput);
        if (command.Command == "outline" && NavlynMcpResponseScope.CurrentOutlinePage is { } page)
        {
            List<string> boundedArguments = [.. command.Arguments];
            if (page.Limit != int.MaxValue)
                boundedArguments.AddRange(["--entry-limit", page.Limit.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
            boundedArguments.AddRange(["--entry-offset", page.Offset.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
            command = CommandBuildResult.Valid(command.Command, boundedArguments, command.StandardInput);
        }
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.TimeoutMilliseconds);
        long started = Stopwatch.GetTimestamp();
        NavlynToolResult? result = null;
        try
        {
            deadline.Token.ThrowIfCancellationRequested();
            result = !options.UseExternalCli && directToolRunner.CanRun(command)
                ? await directToolRunner.RunAsync(toolName, command, deadline.Token)
                : await commandAdapter.RunAsync(toolName, command.Command!, command.Arguments,
                    command.StandardInput, deadline.Token);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            // Await completion so canceled calls release workspace leases and console state.
        }

        bool canceled = cancellationToken.IsCancellationRequested;
        bool timedOut = deadline.IsCancellationRequested ||
            Stopwatch.GetElapsedTime(started).TotalMilliseconds >= options.TimeoutMilliseconds;
        if (canceled || timedOut)
        {
            NavlynSourceCommand source = result?.SourceCommand ?? directToolRunner.CreateSourceCommand(command);
            return NavlynToolResult.Failed(toolName, source, options.WorkspaceArgument,
                new NavlynToolError(canceled ? "NAVLYN_MCP_CANCELED" : "NAVLYN_MCP_TIMEOUT",
                    canceled ? "Tool call was canceled." : $"Tool call timed out after {options.TimeoutMilliseconds} ms.",
                    result?.Error?.ExitCode, result?.Error?.Stderr));
        }

        return result!;
    }

    public NavlynToolResult CreateInvalidArgumentResult(
        string toolName,
        string source,
        string message)
    {
        return NavlynToolResult.Failed(
            toolName,
            new NavlynSourceCommand(source, []),
            options.WorkspaceArgument,
            new NavlynToolError("NAVLYN_MCP_INVALID_ARGUMENT", message));
    }
}

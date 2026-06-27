using Navlyn.Mcp.Execution;
using Navlyn.Mcp.Configuration;

namespace Navlyn.Mcp.Tools;

internal sealed class NavlynMcpToolService(
    INavlynCommandAdapter commandAdapter,
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

        return await commandAdapter.RunAsync(
            toolName,
            command.Command!,
            command.Arguments,
            command.StandardInput,
            cancellationToken);
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

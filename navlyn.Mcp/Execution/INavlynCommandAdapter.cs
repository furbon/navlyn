namespace Navlyn.Mcp.Execution;

using Navlyn.Mcp.Tools;

internal interface INavlynCommandAdapter
{
    Task<NavlynToolResult> RunAsync(
        string toolName,
        string cliCommand,
        IReadOnlyList<string> arguments,
        string? standardInput,
        CancellationToken cancellationToken);
}

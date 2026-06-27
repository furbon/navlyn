namespace Navlyn.Mcp.Tools;

internal sealed record NavlynToolError(
    string Code,
    string Message,
    int? ExitCode = null,
    string? Stderr = null);

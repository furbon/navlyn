namespace Navlyn.Mcp.Tools;

internal sealed record NavlynSourceCommand(
    string Command,
    IReadOnlyList<string> Arguments);

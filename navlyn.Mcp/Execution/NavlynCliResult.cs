namespace Navlyn.Mcp.Execution;

internal sealed record NavlynCliResult(
    int ExitCode,
    string Stdout,
    string Stderr,
    bool TimedOut);

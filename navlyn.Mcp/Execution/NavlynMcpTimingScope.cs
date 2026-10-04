using System.Diagnostics;
using System.Text.Json;
using Navlyn.Workspaces;

namespace Navlyn.Mcp.Execution;

internal sealed class NavlynMcpTimingScope : IDisposable
{
    private static readonly AsyncLocal<NavlynMcpTimingScope?> Slot = new();
    private readonly NavlynMcpTimingScope? previous;
    private readonly string command;
    private readonly TextWriter diagnostics;
    private static TextWriter diagnosticWriter = Console.Error;
    private readonly long started = Stopwatch.GetTimestamp();
    private bool disposed;

    internal NavlynMcpTimingScope(string command, TextWriter? diagnostics = null)
    {
        this.command = command;
        this.diagnostics = diagnostics ?? diagnosticWriter;
        previous = Slot.Value;
        Slot.Value = this;
    }

    public WorkspaceTimingCollector Collector { get; } = new();
    public static WorkspaceTimingCollector? CurrentCollector => Slot.Value?.Collector;
    public static IDisposable? Measure(string name) => CurrentCollector?.Measure(name);
    public static void ConfigureDiagnostics(TextWriter writer) => diagnosticWriter = writer;

    public static NavlynMcpTimingScope? Begin(string command)
    {
        string? enabled = Environment.GetEnvironmentVariable("NAVLYN_PROFILE_TIMINGS");
        return enabled is "1" || string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase)
            ? new NavlynMcpTimingScope(command) : null;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Slot.Value = previous;
        var payload = new
        {
            schemaVersion = "navlyn.mcp.timing.v1",
            command,
            elapsedMs = Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 3),
            stagesAreInclusive = true,
            stages = Collector.Stages.Select(stage => new { name = stage.Name, elapsedMs = stage.ElapsedMs }).ToArray()
        };
        try { diagnostics.WriteLine($"NAVLYN_MCP_TIMING {JsonSerializer.Serialize(payload)}"); }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // Diagnostics must not change a completed tool result.
        }
    }
}

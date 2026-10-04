using System.Text;
using System.Text.Json;
using Navlyn.Mcp.Execution;

namespace Navlyn.Tests.Mcp;

public sealed class NavlynMcpTimingScopeTests
{
    [Fact]
    public async Task ConcurrentDiagnostics_IsolateCollectorsAndRestoreOuterContextAfterFailure()
    {
        using StringWriter output = new();
        TextWriter synchronized = TextWriter.Synchronized(output);
        using NavlynMcpTimingScope outer = new("outer", synchronized);
        async Task Run(string command)
        {
            using NavlynMcpTimingScope scope = new(command, synchronized);
            Assert.NotSame(outer.Collector, NavlynMcpTimingScope.CurrentCollector);
            using (NavlynMcpTimingScope.Measure(command + ".stage")) await Task.Yield();
            throw new InvalidOperationException(command);
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => Task.WhenAll(Run("first"), Run("second")));
        Assert.Same(outer.Collector, NavlynMcpTimingScope.CurrentCollector);
        string[] lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        foreach (string line in lines)
        {
            using JsonDocument document = JsonDocument.Parse(line["NAVLYN_MCP_TIMING ".Length..]);
            JsonElement root = document.RootElement;
            string command = root.GetProperty("command").GetString()!;
            Assert.Equal("navlyn.mcp.timing.v1", root.GetProperty("schemaVersion").GetString());
            Assert.True(root.GetProperty("stagesAreInclusive").GetBoolean());
            Assert.Equal(command + ".stage", Assert.Single(root.GetProperty("stages").EnumerateArray()).GetProperty("name").GetString());
        }
    }

    [Fact]
    public void ClosedDiagnosticStream_DoesNotFailACompletedCall()
    {
        using FailingWriter output = new();
        NavlynMcpTimingScope scope = new("read", output);
        Assert.Null(Record.Exception(scope.Dispose));
        Assert.Null(NavlynMcpTimingScope.CurrentCollector);
    }

    private sealed class FailingWriter : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
        public override void WriteLine(string? value) => throw new IOException("closed diagnostic pipe");
    }
}

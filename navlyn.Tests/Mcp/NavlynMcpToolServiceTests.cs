using System.Text.Json;
using Navlyn.Mcp.Configuration;
using Navlyn.Mcp.Execution;
using Navlyn.Mcp.Tools;

namespace Navlyn.Tests.Mcp;

public sealed class NavlynMcpToolServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Deadline_RejectsCanceledAndNonCooperativeLateResults(bool ignoresCancellation)
    {
        NavlynMcpServerOptions options = Options() with { TimeoutMilliseconds = 20 };
        using NavlynMcpWorkspaceCache cache = new(options);
        Adapter adapter = new(async token =>
        {
            await Task.Delay(ignoresCancellation ? 60 : Timeout.Infinite, ignoresCancellation ? CancellationToken.None : token);
            return Success();
        });
        NavlynMcpToolService service = new(adapter, new(options, cache), options);
        NavlynToolResult result = await service.RunAsync("test", CommandBuildResult.Valid("diagnostics", []), CancellationToken.None);
        Assert.False(result.Ok);
        Assert.Equal("NAVLYN_MCP_TIMEOUT", result.Error?.Code);
    }

    [Fact]
    public async Task CallerCancellation_IsDistinctAndDoesNotExecuteAnAlreadyCanceledCall()
    {
        NavlynMcpServerOptions options = Options();
        using NavlynMcpWorkspaceCache cache = new(options);
        Adapter adapter = new(_ => Task.FromResult(Success()));
        NavlynMcpToolService service = new(adapter, new(options, cache), options);
        using CancellationTokenSource canceled = new();
        canceled.Cancel();
        NavlynToolResult result = await service.RunAsync("test", CommandBuildResult.Valid("diagnostics", []), canceled.Token);
        Assert.Equal("NAVLYN_MCP_CANCELED", result.Error?.Code);
        NavlynSourceCommand source = Assert.IsType<NavlynSourceCommand>(result.SourceCommand);
        Assert.Contains("--workspace-root-policy", source.Arguments);
        Assert.Contains("repo-relative", source.Arguments);
        Assert.Equal(0, adapter.Calls);
        Assert.True((await service.RunAsync("test", CommandBuildResult.Valid("diagnostics", []), CancellationToken.None)).Ok);
    }

    [Fact]
    public async Task CallerCancellation_DuringExecutionIsNotReportedAsDeadlineExpiry()
    {
        NavlynMcpServerOptions options = Options();
        using NavlynMcpWorkspaceCache cache = new(options);
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Adapter adapter = new(async token => { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); return Success(); });
        NavlynMcpToolService service = new(adapter, new(options, cache), options);
        using CancellationTokenSource canceled = new();
        Task<NavlynToolResult> call = service.RunAsync("test", CommandBuildResult.Valid("diagnostics", []), canceled.Token);
        await entered.Task;
        canceled.Cancel();
        Assert.Equal("NAVLYN_MCP_CANCELED", (await call).Error?.Code);
    }

    private static NavlynMcpServerOptions Options() => new("unused.csproj", "unused.csproj", null, [],
        Directory.GetCurrentDirectory(), 120000, 4000000, null, NavlynMcpToolProfile.Full,
        NavlynMcpServerOptions.DefaultWorkspaceRootPolicy);

    private static NavlynToolResult Success() => NavlynToolResult.Succeeded("test", new("diagnostics", []),
        "unused.csproj", JsonSerializer.SerializeToElement(new { command = "diagnostics" }));

    private sealed class Adapter(Func<CancellationToken, Task<NavlynToolResult>> execute) : INavlynCommandAdapter
    {
        public int Calls { get; private set; }
        public Task<NavlynToolResult> RunAsync(string toolName, string command, IReadOnlyList<string> arguments,
            string? input, CancellationToken cancellationToken)
        {
            Calls++;
            return execute(cancellationToken);
        }
    }
}

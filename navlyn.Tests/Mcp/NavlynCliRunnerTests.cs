using Navlyn.Mcp.Configuration;
using Navlyn.Mcp.Execution;
using Navlyn.Mcp.Tools;
using System.Diagnostics;
using Navlyn.Workspaces;

namespace Navlyn.Tests.Mcp;

public sealed class NavlynCliRunnerTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Cancellation_ReapsProcessTreeIncludingBlockedStdin(bool callerCancellation, bool blockedInput)
    {
        using RunnerFixture directory = new();
        string marker = Path.Combine(directory.Path, "parent.pid");
        string childMarker = Path.Combine(directory.Path, "child.pid");
        string script = Path.Combine(directory.Path, "child.ps1");
        await File.WriteAllTextAsync(script, $$"""
            $PID | Set-Content -LiteralPath '{{marker.Replace("'", "''")}}'
            if ($args[0] -eq 'success') { [Console]::WriteLine('{"command":"check"}'); exit 0 }
            $info = [Diagnostics.ProcessStartInfo]::new('pwsh')
            $info.UseShellExecute = $false
            $info.CreateNoWindow = $true
            $info.ArgumentList.Add('-NoProfile')
            $info.ArgumentList.Add('-Command')
            $info.ArgumentList.Add("`$PID | Set-Content -LiteralPath '{{childMarker.Replace("'", "''")}}'; Start-Sleep -Seconds 30")
            [Diagnostics.Process]::Start($info) | Out-Null
            Start-Sleep -Seconds 30
            """);
        NavlynMcpServerOptions options = new("unused.csproj", "unused.csproj", "pwsh",
            ["-NoProfile", "-File", script], directory.Path, callerCancellation ? 10000 : 1500, 1000,
            null, NavlynMcpToolProfile.Full, NavlynMcpServerOptions.DefaultWorkspaceRootPolicy);
        using NavlynMcpWorkspaceCache cache = new(options);
        NavlynMcpToolService service = new(new NavlynCliRunner(options), new(options, cache), options);
        using CancellationTokenSource caller = new();
        Task<NavlynToolResult> call = service.RunAsync("test",
            CommandBuildResult.Valid("sleep", [], blockedInput ? new string('x', 2_000_000) : null), caller.Token);
        try
        {
            using CancellationTokenSource startup = new(TimeSpan.FromSeconds(5));
            while (!File.Exists(childMarker)) { await Task.Delay(20, startup.Token); }
            if (callerCancellation) { caller.Cancel(); }
            NavlynToolResult result = await call.WaitAsync(TimeSpan.FromSeconds(8));
            Assert.Equal(callerCancellation ? "NAVLYN_MCP_CANCELED" : "NAVLYN_MCP_TIMEOUT", result.Error?.Code);
            AssertStopped(marker);
            AssertStopped(childMarker);
            Assert.True((await service.RunAsync("test", CommandBuildResult.Valid("success", []), CancellationToken.None)).Ok);
        }
        finally
        {
            caller.Cancel();
            foreach (string path in new[] { marker, childMarker })
            {
                if (!File.Exists(path)) { continue; }
                try { using Process process = Process.GetProcessById(int.Parse(File.ReadAllText(path).Trim())); process.Kill(true); }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
            }
        }
    }

    [Fact]
    public async Task OversizedOutput_DrainsBothPipesAndRetainsBoundedError()
    {
        using RunnerFixture directory = new();
        string script = Path.Combine(directory.Path, "output.ps1");
        await File.WriteAllTextAsync(script, "[Console]::Error.Write(('e' * 100000)); [Console]::Write(('o' * 100000))");
        NavlynMcpServerOptions options = new("unused", "unused", "pwsh", ["-NoProfile", "-File", script],
            directory.Path, 5000, 1000, null, NavlynMcpToolProfile.Full, NavlynMcpServerOptions.DefaultWorkspaceRootPolicy);
        NavlynToolResult result = await new NavlynCliRunner(options).RunAsync("test", "check", [], null, CancellationToken.None);
        Assert.Equal("NAVLYN_MCP_OUTPUT_TOO_LARGE", result.Error?.Code);
        Assert.EndsWith("[stderr truncated]", result.Error!.Stderr);
        Assert.True(result.Error.Stderr!.Length < 17000);
    }

    [Fact]
    public async Task PreCanceledCall_DoesNotStartAnExecutable()
    {
        NavlynMcpServerOptions options = new("unused", "unused", "does-not-exist", [],
            Directory.GetCurrentDirectory(), 1000, 1000, null, NavlynMcpToolProfile.Full,
            NavlynMcpServerOptions.DefaultWorkspaceRootPolicy);
        using CancellationTokenSource canceled = new();
        canceled.Cancel();
        Assert.Equal("NAVLYN_MCP_CANCELED", (await new NavlynCliRunner(options)
            .RunAsync("test", "check", [], null, canceled.Token)).Error?.Code);
        Assert.Equal("NAVLYN_MCP_CLI_NOT_FOUND", (await new NavlynCliRunner(options)
            .RunAsync("test", "check", [], null, CancellationToken.None)).Error?.Code);
    }

    private static void AssertStopped(string marker)
    {
        int pid = int.Parse(File.ReadAllText(marker).Trim());
        try { using Process process = Process.GetProcessById(pid); Assert.True(process.HasExited, $"Process {pid} survived cancellation."); }
        catch (ArgumentException) { }
    }

    private sealed class RunnerFixture : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "navlyn-runner-" + Guid.NewGuid().ToString("N"));
        public RunnerFixture() => Directory.CreateDirectory(Path);
        public void Dispose() => TemporaryDirectoryCleanup.Delete(Path);
    }

    [Fact]
    public void BuildArguments_PrependsConfiguredNavlynArgsAndWorkspace()
    {
        NavlynMcpServerOptions options = new(
            Workspace: @"D:\repo\navlyn.slnx",
            WorkspaceArgument: "navlyn.slnx",
            NavlynExecutable: "dotnet",
            NavlynArguments: ["navlyn.dll"],
            WorkingDirectory: @"D:\repo",
            TimeoutMilliseconds: NavlynMcpServerOptions.DefaultTimeoutMilliseconds,
            MaxJsonChars: NavlynMcpServerOptions.DefaultMaxJsonChars,
            DaemonPipe: null,
            ToolProfile: NavlynMcpServerOptions.DefaultToolProfile,
            WorkspaceRootPolicy: NavlynMcpServerOptions.DefaultWorkspaceRootPolicy);
        NavlynCliRunner runner = new(options);

        IReadOnlyList<string> arguments = runner.BuildArguments("find", ["--query", "WorkspaceLoader"]);

        Assert.Equal(
            [
                "navlyn.dll",
                "find",
                "--workspace",
                "navlyn.slnx",
                "--workspace-root-policy",
                WorkspaceLoader.FormatWorkspaceRootPolicy(NavlynMcpServerOptions.DefaultWorkspaceRootPolicy),
                "--query",
                "WorkspaceLoader"
            ],
            arguments);
    }
}

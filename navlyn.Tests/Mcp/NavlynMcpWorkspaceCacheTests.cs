using Navlyn.Mcp.Configuration;
using Navlyn.Mcp.Execution;
using Navlyn.Mcp.Tools;
using Navlyn.Workspaces;
using System.IO.Pipes;
using System.Text;

namespace Navlyn.Tests.Mcp;

public sealed class NavlynMcpWorkspaceCacheTests
{
    [Fact]
    public async Task Refresh_KeepsLeasedGenerationUsableUntilReleaseAndValidatesOnlyNewGeneration()
    {
        using TemporaryDirectory directory = TemporaryDirectory.Create();
        string projectPath = CreateProject(directory.Path);
        string sourcePath = Path.Combine(directory.Path, "Fixture.cs");
        await File.WriteAllTextAsync(sourcePath, "namespace Fixture; public sealed class Alpha { }\n");
        using NavlynMcpWorkspaceCache cache = new(CreateOptions(projectPath));

        NavlynMcpWorkspaceCacheResult initial = await cache.GetAsync(CancellationToken.None);
        NavlynMcpWorkspaceCache.WorkspaceLease oldLease = Assert.IsType<NavlynMcpWorkspaceCache.WorkspaceLease>(initial.Lease);
        Assert.True(await cache.ValidateAsync(oldLease, CancellationToken.None));
        Microsoft.CodeAnalysis.Document oldDocument = Assert.Single(Assert.Single(oldLease.CachedWorkspace.Workspace.Solution.Projects).Documents, document => document.Name == "Fixture.cs");
        Microsoft.CodeAnalysis.SyntaxTree oldTree = await oldDocument.GetSyntaxTreeAsync() ?? throw new InvalidOperationException("Old leased document has no syntax tree.");

        await File.WriteAllTextAsync(sourcePath, "namespace Fixture; public sealed class Bravo { }\n");
        NavlynMcpWorkspaceCacheResult refreshed = await cache.RefreshAsync(CancellationToken.None);
        NavlynMcpWorkspaceCache.WorkspaceLease newLease = Assert.IsType<NavlynMcpWorkspaceCache.WorkspaceLease>(refreshed.Lease);
        try
        {
            Assert.False(await cache.ValidateAsync(oldLease, CancellationToken.None));
            Assert.True(await cache.ValidateAsync(newLease, CancellationToken.None));

            string oldText = (await oldTree.GetTextAsync()).ToString();
            Assert.Contains("Alpha", oldText, StringComparison.Ordinal);
            Assert.DoesNotContain("Bravo", oldText, StringComparison.Ordinal);

            Microsoft.CodeAnalysis.Document newDocument = Assert.Single(Assert.Single(newLease.CachedWorkspace.Workspace.Solution.Projects).Documents, document => document.Name == "Fixture.cs");
            Microsoft.CodeAnalysis.SyntaxTree newTree = await newDocument.GetSyntaxTreeAsync() ?? throw new InvalidOperationException("New leased document has no syntax tree.");
            string newText = (await newTree.GetTextAsync()).ToString();
            Assert.Contains("Bravo", newText, StringComparison.Ordinal);
        }
        finally
        {
            await newLease.DisposeAsync();
            await oldLease.DisposeAsync();
        }
    }

    [Fact]
    public async Task FailedReplacement_DoesNotReturnRetiredGenerationAsSuccess()
    {
        using TemporaryDirectory directory = TemporaryDirectory.Create();
        string projectPath = CreateProject(directory.Path);
        string sourcePath = Path.Combine(directory.Path, "Fixture.cs");
        await File.WriteAllTextAsync(sourcePath, "namespace Fixture; public sealed class Alpha { }\n");
        using NavlynMcpWorkspaceCache cache = new(CreateOptions(projectPath));

        NavlynMcpWorkspaceCacheResult initial = await cache.GetAsync(CancellationToken.None);
        NavlynMcpWorkspaceCache.WorkspaceLease oldLease = Assert.IsType<NavlynMcpWorkspaceCache.WorkspaceLease>(initial.Lease);
        await File.WriteAllTextAsync(projectPath, "<Project>");

        NavlynMcpWorkspaceCacheResult replacement = await cache.RefreshAsync(CancellationToken.None);
        Assert.Null(replacement.Lease);
        Assert.NotNull(replacement.Error);

        NavlynMcpWorkspaceCacheResult subsequent = await cache.GetAsync(CancellationToken.None);
        Assert.Null(subsequent.Lease);
        Assert.NotNull(subsequent.Error);
        Assert.False(await cache.ValidateAsync(oldLease, CancellationToken.None));
        await oldLease.DisposeAsync();
    }

    [Fact]
    public async Task DaemonRefresh_AlsoReplacesTheLocalDirectSnapshot()
    {
        using TemporaryDirectory directory = TemporaryDirectory.Create();
        string projectPath = CreateProject(directory.Path);
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "Fixture.cs"), "namespace Fixture; public sealed class Alpha { }\n");
        string pipeName = $"navlyn-mcp-refresh-{Guid.NewGuid():N}";
        await using NamedPipeServerStream server = new(
            pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        Task daemonTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync(timeout.Token);
            using StreamReader reader = new(server, Encoding.UTF8, leaveOpen: true);
            await using StreamWriter writer = new(server, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            Assert.NotNull(await reader.ReadLineAsync(timeout.Token));
            await writer.WriteLineAsync("{\"id\":null,\"ok\":true,\"result\":{\"command\":\"workspace-refresh\"}}");
        }, timeout.Token);

        NavlynMcpServerOptions options = CreateOptions(projectPath) with { DaemonPipe = pipeName };
        using NavlynMcpWorkspaceCache cache = new(options);
        NavlynMcpWorkspaceCache.WorkspaceLease oldLease = Assert.IsType<NavlynMcpWorkspaceCache.WorkspaceLease>(
            (await cache.GetAsync(timeout.Token)).Lease);
        NavlynMcpDirectToolRunner runner = new(options, cache);
        NavlynToolResult refresh = await runner.RunAsync(
            NavlynMcpTools.WorkspaceRefreshTool,
            NavlynToolCommandBuilder.WorkspaceRefresh(null, null, null, null),
            timeout.Token);
        await daemonTask;

        Assert.True(refresh.Ok, refresh.Error?.Message);
        Assert.Equal("daemon", refresh.Metadata?.ExecutionPath);
        NavlynMcpWorkspaceCacheResult after = await cache.GetAsync(timeout.Token);
        NavlynMcpWorkspaceCache.WorkspaceLease newLease = Assert.IsType<NavlynMcpWorkspaceCache.WorkspaceLease>(after.Lease);
        try
        {
            Assert.True(after.CacheHit);
            Assert.NotEqual(oldLease.Generation, newLease.Generation);
            Assert.False(await cache.ValidateAsync(oldLease, timeout.Token));
            Assert.True(await cache.ValidateAsync(newLease, timeout.Token));
        }
        finally
        {
            await newLease.DisposeAsync();
            await oldLease.DisposeAsync();
        }
    }

    [Fact]
    public async Task DirectCall_RetriesOnceAfterInCallEditAndFailsClosedWhenEditsContinue()
    {
        using TemporaryDirectory directory = TemporaryDirectory.Create();
        string projectPath = CreateProject(directory.Path);
        string sourcePath = Path.Combine(directory.Path, "Fixture.cs");
        await File.WriteAllTextAsync(sourcePath, "namespace Fixture; public sealed class Alpha { }\n");
        NavlynMcpServerOptions options = CreateOptions(projectPath);
        using NavlynMcpWorkspaceCache cache = new(options);
        NavlynMcpDirectToolRunner runner = new(options, cache, attempt =>
        {
            if (attempt == 0)
            {
                File.WriteAllText(sourcePath, "namespace Fixture; public sealed class Bravo { }\n");
            }
        });
        NavlynToolResult result = await runner.RunAsync(
            NavlynMcpTools.FileOutlineTool,
            NavlynToolCommandBuilder.FileOutline(sourcePath, null, null),
            CancellationToken.None);

        Assert.True(result.Ok, result.Error?.Message);
        Assert.Contains(result.Result!.Value.GetProperty("entries").EnumerateArray(),
            entry => entry.GetProperty("name").GetString() == "Bravo");
        Assert.DoesNotContain(result.Result.Value.GetProperty("entries").EnumerateArray(),
            entry => entry.GetProperty("name").GetString() == "Alpha");

        using NavlynMcpWorkspaceCache unstableCache = new(options);
        NavlynMcpDirectToolRunner unstableRunner = new(options, unstableCache, attempt =>
            File.WriteAllText(sourcePath, attempt == 0
                ? "namespace Fixture; public sealed class Delta { }\n"
                : "namespace Fixture; public sealed class Gamma { }\n"));
        NavlynToolResult unstable = await unstableRunner.RunAsync(
            NavlynMcpTools.FileOutlineTool,
            NavlynToolCommandBuilder.FileOutline(sourcePath, null, null),
            CancellationToken.None);

        Assert.False(unstable.Ok);
        Assert.Equal("NAVLYN_MCP_STALE_WORKSPACE", unstable.Error?.Code);
        Assert.Null(unstable.Result);
        Assert.Null(unstable.Metadata);
    }

    [Fact]
    public async Task CanceledRefresh_DoesNotPublishPartiallyLoadedWorkspaceOrReuseOldGeneration()
    {
        using TemporaryDirectory directory = TemporaryDirectory.Create();
        string projectPath = CreateProject(directory.Path);
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "Fixture.cs"),
            "namespace Fixture; public sealed class Alpha { }\n");
        using CancellationTokenSource cancellation = new();
        using NavlynMcpWorkspaceCache cache = new(CreateOptions(projectPath),
            () => cancellation.Cancel());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.GetAsync(cancellation.Token));
        Assert.True(cancellation.IsCancellationRequested);

        NavlynMcpWorkspaceCacheResult after = await cache.GetAsync(CancellationToken.None);
        Assert.NotNull(after.Lease);
        Assert.False(after.CacheHit);
        await after.Lease!.DisposeAsync();
    }

    [Fact]
    public async Task DirectCall_FailsClosedWhenAReparseDirectoryAppearsAfterInitialSuccess()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TemporaryDirectory directory = TemporaryDirectory.Create();
        using TemporaryDirectory external = TemporaryDirectory.Create();
        string projectPath = CreateProject(directory.Path);
        string sourcePath = Path.Combine(directory.Path, "Fixture.cs");
        await File.WriteAllTextAsync(sourcePath, "namespace Fixture; public sealed class Alpha { }\n");
        Directory.CreateDirectory(external.Path);
        await File.WriteAllTextAsync(Path.Combine(external.Path, "Added.cs"), "namespace Fixture; public sealed class Added { }\n");
        NavlynMcpServerOptions options = CreateOptions(projectPath);
        using NavlynMcpWorkspaceCache cache = new(options);
        NavlynMcpDirectToolRunner runner = new(options, cache);
        NavlynToolResult before = await runner.RunAsync(
            NavlynMcpTools.FileOutlineTool,
            NavlynToolCommandBuilder.FileOutline(sourcePath, null, null),
            CancellationToken.None);
        Assert.True(before.Ok, before.Error?.Message);

        string junction = Path.Combine(directory.Path, "Linked");
        CreateJunction(junction, external.Path);
        try
        {
            NavlynToolResult after = await runner.RunAsync(
                NavlynMcpTools.FileOutlineTool,
                NavlynToolCommandBuilder.FileOutline(sourcePath, null, null),
                CancellationToken.None);
            Assert.False(after.Ok);
            Assert.Equal("NAVLYN_MCP_STALE_WORKSPACE", after.Error?.Code);
            Assert.Null(after.Result);
            Assert.Null(after.Metadata);
        }
        finally
        {
            Assert.True((File.GetAttributes(junction) & FileAttributes.ReparsePoint) != 0);
            Directory.Delete(junction);
        }
    }

    private static string CreateProject(string root)
    {
        string projectPath = Path.Combine(root, "Fixture.csproj");
        File.WriteAllText(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        return projectPath;
    }

    private static NavlynMcpServerOptions CreateOptions(string projectPath) => new(
        Workspace: projectPath,
        WorkspaceArgument: projectPath,
        NavlynExecutable: null,
        NavlynArguments: [],
        WorkingDirectory: Path.GetDirectoryName(projectPath)!,
        TimeoutMilliseconds: NavlynMcpServerOptions.DefaultTimeoutMilliseconds,
        MaxJsonChars: NavlynMcpServerOptions.DefaultMaxJsonChars,
        DaemonPipe: null,
        ToolProfile: NavlynMcpServerOptions.DefaultToolProfile,
        WorkspaceRootPolicy: NavlynMcpServerOptions.DefaultWorkspaceRootPolicy);

    private static void CreateJunction(string link, string target)
    {
        using System.Diagnostics.Process process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            ArgumentList = { "/c", "mklink", "/J", link, target },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("Could not start mklink.");
        Assert.True(process.WaitForExit(10000), "Junction creation timed out.");
        Assert.Equal(0, process.ExitCode);
        Assert.True((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path) => Path = path;

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"navlyn-cache-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}

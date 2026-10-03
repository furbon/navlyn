using Navlyn.Mcp.Configuration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Navlyn.Mcp.Execution;
using Navlyn.Mcp.Tools;
using Navlyn.Workspaces;
using Navlyn.Symbols;
using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;

namespace Navlyn.Tests.Mcp;

public sealed class NavlynMcpWorkspaceCacheTests
{
    [Fact]
    public async Task DirectTarget_SimpleQueryMatchesCliAndRefreshesAfterSourceEdit()
    {
        using TemporaryDirectory directory = TemporaryDirectory.Create();
        Directory.CreateDirectory(Path.Combine(directory.Path, ".git"));
        string projectPath = CreateProject(directory.Path);
        string sourcePath = Path.Combine(directory.Path, "Fixture.cs");
        await File.WriteAllTextAsync(sourcePath, "namespace Fixture; public sealed class Alpha { }\n");
        NavlynMcpServerOptions options = CreateOptions(projectPath);
        using NavlynMcpWorkspaceCache cache = new(options);
        NavlynMcpDirectToolRunner runner = new(options, cache);
        NavlynInProcessCommandAdapter cli = new(options);
        CommandBuildResult command = CommandBuildResult.Valid("target", ["--query", "Alpha"]);
        Assert.True(runner.CanRun(command));
        Assert.False(runner.CanRun(CommandBuildResult.Valid("target", ["--query", "Alpha", "--match", "exact"])));

        NavlynToolResult direct = await runner.RunAsync(NavlynMcpTools.TargetTool, command, CancellationToken.None);
        NavlynToolResult original = await cli.RunAsync(
            NavlynMcpTools.TargetTool, "target", command.Arguments, null, CancellationToken.None);
        Assert.True(direct.Ok, direct.Error?.Message);
        Assert.True(original.Ok, original.Error?.Message);
        Assert.Equal("direct", direct.Metadata?.ExecutionPath);
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse(direct.Result!.Value.GetRawText()),
            JsonNode.Parse(original.Result!.Value.GetRawText())),
            $"Direct: {direct.Result.Value.GetRawText()}\nCLI: {original.Result.Value.GetRawText()}");

        await File.WriteAllTextAsync(sourcePath, "namespace Fixture; public sealed class Bravo { }\n");
        NavlynToolResult staleName = await runner.RunAsync(NavlynMcpTools.TargetTool, command, CancellationToken.None);
        Assert.True(staleName.Ok, staleName.Error?.Message);
        Assert.False(staleName.Result!.Value.TryGetProperty("selectedTarget", out _));
        CommandBuildResult replacement = CommandBuildResult.Valid("target", ["--query", "Bravo"]);
        NavlynToolResult newName = await runner.RunAsync(NavlynMcpTools.TargetTool, replacement, CancellationToken.None);
        Assert.True(newName.Ok, newName.Error?.Message);
        Assert.Equal("Bravo", newName.Result!.Value.GetProperty("selectedTarget").GetProperty("name").GetString());
    }

    [Fact]
    public async Task DirectTarget_LinkedSourceInMultipleProjectsMatchesCliAmbiguityEnvelope()
    {
        using TemporaryDirectory directory = TemporaryDirectory.Create();
        Directory.CreateDirectory(Path.Combine(directory.Path, ".git"));
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "Shared.cs"),
            "namespace Fixture; public sealed class SharedName { }\n");
        foreach (string project in new[] { "First", "Second" })
        {
            string root = Path.Combine(directory.Path, project);
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root, $"{project}.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>" +
                "<ItemGroup><Compile Include=\"../Shared.cs\" Link=\"Shared.cs\" /></ItemGroup></Project>");
        }

        string solutionPath = Path.Combine(directory.Path, "Fixture.slnx");
        await File.WriteAllTextAsync(solutionPath,
            "<Solution><Project Path=\"First/First.csproj\" /><Project Path=\"Second/Second.csproj\" /></Solution>");
        NavlynMcpServerOptions options = CreateOptions(solutionPath);
        using NavlynMcpWorkspaceCache cache = new(options);
        NavlynMcpDirectToolRunner runner = new(options, cache);
        NavlynInProcessCommandAdapter cli = new(options);
        CommandBuildResult command = CommandBuildResult.Valid("target", ["--query", "SharedName"]);
        NavlynToolResult direct = await runner.RunAsync(NavlynMcpTools.TargetTool, command, CancellationToken.None);
        NavlynToolResult original = await cli.RunAsync(
            NavlynMcpTools.TargetTool, "target", command.Arguments, null, CancellationToken.None);
        Assert.True(direct.Ok, direct.Error?.Message);
        Assert.True(original.Ok, original.Error?.Message);
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse(direct.Result!.Value.GetRawText()),
            JsonNode.Parse(original.Result!.Value.GetRawText())),
            $"Direct: {direct.Result.Value.GetRawText()}\nCLI: {original.Result.Value.GetRawText()}");
    }

    [Fact]
    public void DirectTarget_WithoutRepositoryDisplayRootUsesCliFallback()
    {
        using TemporaryDirectory directory = TemporaryDirectory.Create();
        string projectPath = CreateProject(directory.Path);
        NavlynMcpServerOptions options = CreateOptions(projectPath);
        using NavlynMcpWorkspaceCache cache = new(options);
        NavlynMcpDirectToolRunner runner = new(options, cache);
        Assert.False(runner.CanRun(CommandBuildResult.Valid("target", ["--query", "Alpha"])));
    }

    [Theory]
    [InlineData("candidate")]
    [InlineData("source-position")]
    [InlineData("outline")]
    public async Task DirectReader_NestedRepository_ReturnsTheSelectedDeclaration(string selectionMode)
    {
        using TemporaryDirectory directory = TemporaryDirectory.Create();
        Directory.CreateDirectory(Path.Combine(directory.Path, ".git"));
        string projectRoot = Path.Combine(directory.Path, "src");
        Directory.CreateDirectory(projectRoot);
        string projectPath = CreateProject(projectRoot);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "Fixture.cs"),
            "namespace Fixture;\npublic sealed class Alpha { public string Name => \"alpha\"; }\n");
        NavlynMcpServerOptions options = CreateOptions(projectPath);
        using NavlynMcpWorkspaceCache cache = new(options);
        NavlynMcpDirectToolRunner runner = new(options, cache);
        FuzzySymbolCandidate candidate;
        await using (NavlynMcpWorkspaceCache.WorkspaceLease lease = Assert.IsType<NavlynMcpWorkspaceCache.WorkspaceLease>(
            (await cache.GetAsync(CancellationToken.None)).Lease))
        {
            FuzzyFindResult find = await new FuzzyDiscoveryResolver().FindAsync(
                lease.CachedWorkspace.Workspace,
                new FuzzyQueryOptions("Alpha", ["NamedType"], "exact", null, false, null),
                lease.CachedWorkspace.Workspace.Solution.Projects.ToArray(),
                projectFilters: null,
                CancellationToken.None);
            candidate = Assert.IsType<FuzzySymbolCandidate>(find.SelectedCandidate);
        }

        string? candidateId = selectionMode == "source-position" ? null : candidate.CandidateId;
        if (selectionMode == "outline")
        {
            NavlynToolResult outline = await runner.RunAsync(NavlynMcpTools.FileOutlineTool,
                NavlynToolCommandBuilder.FileOutline("src/Fixture.cs", null, null), CancellationToken.None);
            Assert.True(outline.Ok, outline.Error?.Message);
            candidateId = Assert.Single(outline.Result!.Value.GetProperty("entries").EnumerateArray(),
                entry => entry.GetProperty("name").GetString() == "Alpha").GetProperty("candidateId").GetString();
        }

        CommandBuildResult read = NavlynToolCommandBuilder.Read(
            candidateId,
            candidateId is null ? "src/Fixture.cs" : null,
            candidateId is null ? candidate.Line : null,
            candidateId is null ? candidate.Column : null,
            null, null, "declaration", null, null);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            NavlynToolResult result = await runner.RunAsync(NavlynMcpTools.ReadTool, read, CancellationToken.None);
            Assert.True(result.Ok, result.Error?.Message);
            Assert.Equal("direct", result.Metadata!.ExecutionPath);
            Assert.Equal("src/Fixture.cs", result.Result!.Value.GetProperty("file").GetString());
            Assert.Equal("Alpha", result.Result.Value.GetProperty("symbol").GetProperty("name").GetString());
            Assert.Equal("Fixture", result.Result.Value.GetProperty("symbol").GetProperty("facts").GetProperty("project").GetString());
            Assert.Contains("public sealed class Alpha", result.Result.Value.GetProperty("slices")[0]
                .GetProperty("lines")[0].GetString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task DirectReader_NestedRepository_PreservesAbsoluteInputAndRejectsUnloadedSource()
    {
        using TemporaryDirectory directory = TemporaryDirectory.Create();
        Directory.CreateDirectory(Path.Combine(directory.Path, ".git"));
        string projectRoot = Path.Combine(directory.Path, "src");
        Directory.CreateDirectory(projectRoot);
        string projectPath = CreateProject(projectRoot);
        string sourcePath = Path.Combine(projectRoot, "Fixture.cs");
        await File.WriteAllTextAsync(sourcePath, "namespace Fixture;\npublic sealed class Alpha { }\n");
        string unloadedPath = Path.Combine(directory.Path, "Outside.cs");
        await File.WriteAllTextAsync(unloadedPath, "namespace Fixture;\npublic sealed class Alpha { }\n");
        using NavlynMcpWorkspaceCache cache = new(CreateOptions(projectPath));
        NavlynMcpDirectToolRunner runner = new(CreateOptions(projectPath), cache);
        NavlynToolResult loaded = await runner.RunAsync(NavlynMcpTools.ReadTool,
            NavlynToolCommandBuilder.Read(null, sourcePath, 2, 21, null, null, "declaration", null, null), CancellationToken.None);
        Assert.True(loaded.Ok, loaded.Error?.Message);
        Assert.Equal("Alpha", loaded.Result!.Value.GetProperty("symbol").GetProperty("name").GetString());

        foreach (string path in new[] { unloadedPath, "../Outside.cs", "Missing.cs" })
        {
            NavlynToolResult unloaded = await runner.RunAsync(NavlynMcpTools.ReadTool,
                NavlynToolCommandBuilder.Read(null, path, 2, 21, null, null, "declaration", null, null), CancellationToken.None);
            Assert.False(unloaded.Ok);
            Assert.Equal("NAVLYN1302", unloaded.Error!.Code);
        }
    }

    [Fact]
    public async Task DirectReader_NestedRepository_RetainsSelectedProjectForLinkedSource()
    {
        using TemporaryDirectory directory = TemporaryDirectory.Create();
        Directory.CreateDirectory(Path.Combine(directory.Path, ".git"));
        string sharedPath = Path.Combine(directory.Path, "Shared.cs");
        await File.WriteAllTextAsync(sharedPath, "namespace Fixture;\npublic sealed class Alpha { }\n");
        foreach (string name in new[] { "First", "Second" })
        {
            string projectRoot = Path.Combine(directory.Path, name);
            Directory.CreateDirectory(projectRoot);
            await File.WriteAllTextAsync(Path.Combine(projectRoot, $"{name}.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>" +
                "<ItemGroup><Compile Include=\"../Shared.cs\" Link=\"Shared.cs\" /></ItemGroup></Project>");
        }
        string firstOnlyPath = Path.Combine(directory.Path, "First", "OnlyFirst.cs");
        await File.WriteAllTextAsync(firstOnlyPath, "namespace Fixture;\npublic sealed class FirstOnly { }\n");
        string solutionPath = Path.Combine(directory.Path, "Fixture.slnx");
        await File.WriteAllTextAsync(solutionPath,
            "<Solution><Project Path=\"First/First.csproj\" /><Project Path=\"Second/Second.csproj\" /></Solution>");
        NavlynMcpServerOptions options = CreateOptions(solutionPath) with { WorkingDirectory = Path.Combine(directory.Path, "First") };
        using NavlynMcpWorkspaceCache cache = new(options);
        NavlynMcpDirectToolRunner runner = new(options, cache);
        foreach (string project in new[] { "First", "Second" })
        {
            NavlynToolResult linked = await runner.RunAsync(NavlynMcpTools.ReadTool,
                NavlynToolCommandBuilder.Read(null, "Shared.cs", 2, 21, project, null, "declaration", null, null), CancellationToken.None);
            Assert.True(linked.Ok, linked.Error?.Message);
            Assert.Equal(project, linked.Result!.Value.GetProperty("symbol").GetProperty("facts").GetProperty("project").GetString());
        }

        NavlynToolResult wrongProject = await runner.RunAsync(NavlynMcpTools.ReadTool,
            NavlynToolCommandBuilder.Read(null, "First/OnlyFirst.cs", 2, 21, "Second", null, "declaration", null, null), CancellationToken.None);
        Assert.False(wrongProject.Ok);
        Assert.Equal("NAVLYN1306", wrongProject.Error!.Code);
    }

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
        Assert.True(initial.Error is null, initial.Error?.Message);
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
    public async Task EditAtPublicationBoundary_DoesNotPublishOldSourceAsCurrent()
    {
        using TemporaryDirectory directory = TemporaryDirectory.Create();
        string projectPath = CreateProject(directory.Path);
        string sourcePath = Path.Combine(directory.Path, "Fixture.cs");
        await File.WriteAllTextAsync(sourcePath, "namespace Fixture; public sealed class Alpha { }\n");
        int publicationAttempts = 0;
        using NavlynMcpWorkspaceCache cache = new(CreateOptions(projectPath), () =>
        {
            if (Interlocked.Increment(ref publicationAttempts) == 1)
            {
                File.WriteAllText(sourcePath, "namespace Fixture; public sealed class Bravo { }\n");
            }
        });

        NavlynMcpWorkspaceCacheResult result = await cache.GetAsync(CancellationToken.None);
        NavlynMcpWorkspaceCache.WorkspaceLease lease = Assert.IsType<NavlynMcpWorkspaceCache.WorkspaceLease>(result.Lease);
        try
        {
            Assert.True(await cache.ValidateAsync(lease, CancellationToken.None));
            Microsoft.CodeAnalysis.Document document = Assert.Single(Assert.Single(lease.CachedWorkspace.Workspace.Solution.Projects).Documents,
                item => item.Name == "Fixture.cs");
            Assert.Contains("Bravo", (await document.GetTextAsync()).ToString(), StringComparison.Ordinal);
            Assert.True(publicationAttempts >= 2);
        }
        finally
        {
            await lease.DisposeAsync();
        }
    }

    [Fact]
    public async Task SolutionCache_TracksProjectSourceAndAncestorConfigWithoutScanningUnrelatedArtifacts()
    {
        using TemporaryDirectory directory = TemporaryDirectory.Create();
        string projectRoot = Path.Combine(directory.Path, "src");
        Directory.CreateDirectory(projectRoot);
        string projectPath = CreateProject(projectRoot);
        string solutionPath = Path.Combine(directory.Path, "Fixture.slnx");
        await File.WriteAllTextAsync(solutionPath, "<Solution><Project Path=\"src/Fixture.csproj\" /></Solution>");
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "Alpha.cs"), "namespace Fixture; public sealed class Alpha { }\n");
        string artifactRoot = Path.Combine(directory.Path, "artifacts");
        Directory.CreateDirectory(artifactRoot);
        using NavlynMcpWorkspaceCache cache = new(CreateOptions(solutionPath));

        NavlynMcpWorkspaceCache.WorkspaceLease first = Assert.IsType<NavlynMcpWorkspaceCache.WorkspaceLease>(
            (await cache.GetAsync(CancellationToken.None)).Lease);
        await File.WriteAllTextAsync(Path.Combine(artifactRoot, "Unrelated.cs"), "class Unrelated {}\n");
        NavlynMcpWorkspaceCacheResult unchanged = await cache.GetAsync(CancellationToken.None);
        NavlynMcpWorkspaceCache.WorkspaceLease second = Assert.IsType<NavlynMcpWorkspaceCache.WorkspaceLease>(unchanged.Lease);
        try
        {
            Assert.True(unchanged.CacheHit);
            Assert.Equal(first.Generation, second.Generation);

            await File.WriteAllTextAsync(Path.Combine(projectRoot, "Bravo.cs"), "namespace Fixture; public sealed class Bravo { }\n");
            NavlynMcpWorkspaceCacheResult sourceChanged = await cache.GetAsync(CancellationToken.None);
            NavlynMcpWorkspaceCache.WorkspaceLease third = Assert.IsType<NavlynMcpWorkspaceCache.WorkspaceLease>(sourceChanged.Lease);
            try
            {
                Assert.False(sourceChanged.CacheHit);
                Assert.NotEqual(second.Generation, third.Generation);
                Assert.Contains(Assert.Single(third.CachedWorkspace.Workspace.Solution.Projects).Documents,
                    document => document.Name == "Bravo.cs");

                await File.WriteAllTextAsync(Path.Combine(directory.Path, "Directory.Build.props"),
                    "<Project><PropertyGroup><DefineConstants>CHANGED</DefineConstants></PropertyGroup></Project>");
                NavlynMcpWorkspaceCacheResult configChanged = await cache.GetAsync(CancellationToken.None);
                NavlynMcpWorkspaceCache.WorkspaceLease fourth = Assert.IsType<NavlynMcpWorkspaceCache.WorkspaceLease>(configChanged.Lease);
                try
                {
                    Assert.False(configChanged.CacheHit);
                    Assert.NotEqual(third.Generation, fourth.Generation);
                }
                finally
                {
                    await fourth.DisposeAsync();
                }
            }
            finally
            {
                await third.DisposeAsync();
            }
        }
        finally
        {
            await second.DisposeAsync();
            await first.DisposeAsync();
        }
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

    [Fact]
    public async Task ReplacedMetadataReference_RetiresOldLeaseAndLoadsNewBindings()
    {
        using TemporaryDirectory directory = TemporaryDirectory.Create();
        string projectPath = Path.Combine(directory.Path, "Fixture.csproj");
        await File.WriteAllTextAsync(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup><Reference Include="FixtureDependency"><HintPath>Dependency.dll</HintPath></Reference></ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "Fixture.cs"), "public sealed class Consumer : ExternalBase { }\n");
        string dependencyPath = Path.Combine(directory.Path, "Dependency.dll");
        EmitDependency(dependencyPath, "public class ExternalBase { public int Before; }");
        using NavlynMcpWorkspaceCache cache = new(CreateOptions(projectPath));

        NavlynMcpWorkspaceCache.WorkspaceLease first = Assert.IsType<NavlynMcpWorkspaceCache.WorkspaceLease>(
            (await cache.GetAsync(CancellationToken.None)).Lease);
        try
        {
            Project firstProject = Assert.Single(first.CachedWorkspace.Workspace.Solution.Projects);
            Assert.Contains(firstProject.MetadataReferences, reference =>
                string.Equals(reference.Display, dependencyPath, StringComparison.OrdinalIgnoreCase));
            Compilation firstCompilation = await firstProject.GetCompilationAsync()
                ?? throw new InvalidOperationException("No initial compilation was loaded.");
            INamedTypeSymbol firstType = firstCompilation.GetTypeByMetadataName("ExternalBase")
                ?? throw new InvalidOperationException("Initial dependency was not loaded.");
            Assert.Contains(firstType.GetMembers(), member => member.Name == "Before");
            Assert.DoesNotContain(firstType.GetMembers(), member => member.Name == "After");

            string replacementPath = Path.Combine(directory.Path, "Replacement.dll");
            EmitDependency(replacementPath, "public class ExternalBase { public int After; }");
            File.Move(replacementPath, dependencyPath, overwrite: true);

            Assert.False(await cache.ValidateAsync(first, CancellationToken.None));
            NavlynMcpWorkspaceCacheResult updated = await cache.GetAsync(CancellationToken.None);
            NavlynMcpWorkspaceCache.WorkspaceLease second = Assert.IsType<NavlynMcpWorkspaceCache.WorkspaceLease>(updated.Lease);
            try
            {
                Assert.False(updated.CacheHit);
                Assert.NotEqual(first.Generation, second.Generation);
                Compilation compilation = await Assert.Single(second.CachedWorkspace.Workspace.Solution.Projects).GetCompilationAsync()
                    ?? throw new InvalidOperationException("No compilation was loaded.");
                INamedTypeSymbol type = compilation.GetTypeByMetadataName("ExternalBase")
                    ?? throw new InvalidOperationException("Replaced dependency was not loaded.");
                Assert.Contains(type.GetMembers(), member => member.Name == "After");
                Assert.DoesNotContain(type.GetMembers(), member => member.Name == "Before");
            }
            finally
            {
                await second.DisposeAsync();
            }
        }
        finally
        {
            await first.DisposeAsync();
        }
    }

    [Fact]
    public async Task ChangedProjectAssets_RetiresOldLease()
    {
        using TemporaryDirectory directory = TemporaryDirectory.Create();
        string projectPath = CreateProject(directory.Path);
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "Fixture.cs"), "public sealed class Fixture { }\n");
        using NavlynMcpWorkspaceCache cache = new(CreateOptions(projectPath));
        NavlynMcpWorkspaceCache.WorkspaceLease first = Assert.IsType<NavlynMcpWorkspaceCache.WorkspaceLease>(
            (await cache.GetAsync(CancellationToken.None)).Lease);
        try
        {
            string obj = Path.Combine(directory.Path, "obj");
            Directory.CreateDirectory(obj);
            await File.WriteAllTextAsync(Path.Combine(obj, "project.assets.json"), "{\"version\":3,\"test\":\"changed\"}");
            Assert.False(await cache.ValidateAsync(first, CancellationToken.None));
        }
        finally
        {
            await first.DisposeAsync();
        }
    }

    [Fact]
    public async Task NestedProjectAssetsInSolution_RetireOldLease()
    {
        using TemporaryDirectory directory = TemporaryDirectory.Create();
        string rootProjectPath = Path.Combine(directory.Path, "Root.csproj");
        await File.WriteAllTextAsync(rootProjectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "Root.cs"), "public sealed class Root { }\n");
        string projectRoot = Path.Combine(directory.Path, "src", "App");
        Directory.CreateDirectory(projectRoot);
        CreateProject(projectRoot);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "Fixture.cs"), "public sealed class Fixture { }\n");
        string solutionPath = Path.Combine(directory.Path, "Fixture.slnx");
        await File.WriteAllTextAsync(solutionPath,
            "<Solution><Project Path=\"Root.csproj\" /><Project Path=\"src/App/Fixture.csproj\" /></Solution>");
        using NavlynMcpWorkspaceCache cache = new(CreateOptions(solutionPath));
        NavlynMcpWorkspaceCache.WorkspaceLease first = Assert.IsType<NavlynMcpWorkspaceCache.WorkspaceLease>(
            (await cache.GetAsync(CancellationToken.None)).Lease);
        try
        {
            Assert.Contains(first.CachedWorkspace.InputSpec.ProjectDirectories,
                path => string.Equals(path, projectRoot, StringComparison.OrdinalIgnoreCase));
            string obj = Path.Combine(projectRoot, "obj");
            Directory.CreateDirectory(obj);
            await File.WriteAllTextAsync(Path.Combine(obj, "project.assets.json"), "{\"version\":3,\"test\":\"nested\"}");
            Assert.False(await cache.ValidateAsync(first, CancellationToken.None));
        }
        finally
        {
            await first.DisposeAsync();
        }
    }

    private static void EmitDependency(string path, string source)
    {
        CSharpCompilation compilation = CSharpCompilation.Create("FixtureDependency",
            [CSharpSyntaxTree.ParseText(source)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Microsoft.CodeAnalysis.Emit.EmitResult result = compilation.Emit(path);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
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
                TemporaryDirectoryCleanup.Delete(Path);
            }
        }
    }
}

using Navlyn.Mcp.Configuration;
using Navlyn.Mcp.Execution;
using Navlyn.Mcp.Tools;

namespace Navlyn.Tests.Mcp;

public sealed class NavlynInProcessCommandAdapterTests
{
    [Fact]
    public void BuildArguments_AddsLogicalWorkspaceCommand()
    {
        NavlynInProcessCommandAdapter adapter = new(CreateOptions(maxJsonChars: NavlynMcpServerOptions.DefaultMaxJsonChars));

        IReadOnlyList<string> arguments = adapter.BuildArguments("find", ["--query", "WorkspaceLoader"]);

        Assert.Equal(["find", "--workspace", "navlyn.slnx", "--query", "WorkspaceLoader"], arguments);
    }

    [Fact]
    public async Task RunAsync_ExecutesWithoutExternalNavlynCli()
    {
        NavlynInProcessCommandAdapter adapter = new(CreateOptions(maxJsonChars: NavlynMcpServerOptions.DefaultMaxJsonChars));

        NavlynToolResult result = await adapter.RunAsync(
            NavlynMcpTools.WorkspaceSummaryTool,
            "repo-graph",
            ["--project", "Navlyn.Core", "--relationship-limit", "10", "--profile", "compact"],
            standardInput: null,
            CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("repo-graph", result.SourceCommand?.Command);
        Assert.NotNull(result.Result);
        Assert.Equal("repo-graph", result.Result.Value.GetProperty("command").GetString());
    }

    [Fact]
    public async Task RunAsync_MapsCliDiagnosticFailures()
    {
        NavlynInProcessCommandAdapter adapter = new(CreateOptions(maxJsonChars: NavlynMcpServerOptions.DefaultMaxJsonChars));

        NavlynToolResult result = await adapter.RunAsync(
            NavlynMcpTools.FindSymbolTool,
            "find",
            ["--query", "CheckCommand", "--assume-kind", "NotAKind"],
            standardInput: null,
            CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("NAVLYN1004", result.Error?.Code);
        Assert.NotNull(result.Error?.Stderr);
    }

    [Fact]
    public async Task RunAsync_EnforcesMaxJsonChars()
    {
        NavlynInProcessCommandAdapter adapter = new(CreateOptions(maxJsonChars: 10));

        NavlynToolResult result = await adapter.RunAsync(
            NavlynMcpTools.WorkspaceSummaryTool,
            "repo-graph",
            ["--project", "Navlyn.Core", "--relationship-limit", "10"],
            standardInput: null,
            CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("NAVLYN_MCP_OUTPUT_TOO_LARGE", result.Error?.Code);
    }

    private static NavlynMcpServerOptions CreateOptions(int maxJsonChars)
    {
        string repoRoot = FindRepositoryRoot();
        return new NavlynMcpServerOptions(
            Workspace: Path.Combine(repoRoot, "navlyn.slnx"),
            WorkspaceArgument: "navlyn.slnx",
            NavlynExecutable: null,
            NavlynArguments: [],
            WorkingDirectory: repoRoot,
            TimeoutMilliseconds: NavlynMcpServerOptions.DefaultTimeoutMilliseconds,
            MaxJsonChars: maxJsonChars);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "navlyn.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}

using Navlyn.Mcp.Configuration;

namespace Navlyn.Tests.Mcp;

public sealed class NavlynMcpServerOptionsTests
{
    [Fact]
    public void TryParse_RequiredWorkspace_UsesRepositoryRootAsWorkingDirectory()
    {
        string repoRoot = FindRepositoryRoot();
        string workspace = Path.Combine(repoRoot, "navlyn.slnx");

        bool valid = NavlynMcpServerOptions.TryParse(
            ["--workspace", workspace],
            out NavlynMcpServerOptions options,
            out string? error,
            out bool showHelp);

        Assert.True(valid);
        Assert.Null(error);
        Assert.False(showHelp);
        Assert.Equal(workspace, options.Workspace);
        Assert.Equal("navlyn.slnx", options.WorkspaceArgument);
        Assert.Equal(repoRoot, options.WorkingDirectory);
        Assert.Equal("navlyn", options.NavlynExecutable);
    }

    [Fact]
    public void TryParse_RepeatableNavlynArgs_PreservesOrder()
    {
        string repoRoot = FindRepositoryRoot();

        bool valid = NavlynMcpServerOptions.TryParse(
            [
                "--workspace", Path.Combine(repoRoot, "navlyn.slnx"),
                "--navlyn-executable", "dotnet",
                "--navlyn-arg", "navlyn.dll",
                "--navlyn-arg", "--no-build",
                "--timeout-ms", "30000",
                "--max-json-chars", "1000"
            ],
            out NavlynMcpServerOptions options,
            out string? error,
            out _);

        Assert.True(valid);
        Assert.Null(error);
        Assert.Equal("dotnet", options.NavlynExecutable);
        Assert.Equal(["navlyn.dll", "--no-build"], options.NavlynArguments);
        Assert.Equal(30000, options.TimeoutMilliseconds);
        Assert.Equal(1000, options.MaxJsonChars);
    }

    [Fact]
    public void TryParse_MissingWorkspace_ReturnsUsageError()
    {
        bool valid = NavlynMcpServerOptions.TryParse(
            [],
            out _,
            out string? error,
            out bool showHelp);

        Assert.False(valid);
        Assert.Equal("--workspace is required.", error);
        Assert.False(showHelp);
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

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
    public void TryParse_WorkspaceAuto_SelectsSingleTopLevelSlnx()
    {
        using TemporaryDirectory temp = TemporaryDirectory.Create();
        string workspace = Path.Combine(temp.Path, "sample.slnx");
        File.WriteAllText(workspace, "");

        bool valid = NavlynMcpServerOptions.TryParse(
            ["--workspace", "auto", "--working-directory", temp.Path],
            out NavlynMcpServerOptions options,
            out string? error,
            out bool showHelp);

        Assert.True(valid);
        Assert.Null(error);
        Assert.False(showHelp);
        Assert.Equal(workspace, options.Workspace);
        Assert.Equal("sample.slnx", options.WorkspaceArgument);
        Assert.Equal(temp.Path, options.WorkingDirectory);
    }

    [Fact]
    public void TryParse_WorkspaceAuto_PrefersSingleSlnxOverCsproj()
    {
        using TemporaryDirectory temp = TemporaryDirectory.Create();
        string workspace = Path.Combine(temp.Path, "sample.slnx");
        File.WriteAllText(workspace, "");
        File.WriteAllText(Path.Combine(temp.Path, "sample.csproj"), "");

        bool valid = NavlynMcpServerOptions.TryParse(
            ["--workspace", "auto", "--working-directory", temp.Path],
            out NavlynMcpServerOptions options,
            out string? error,
            out _);

        Assert.True(valid);
        Assert.Null(error);
        Assert.Equal(workspace, options.Workspace);
        Assert.Equal("sample.slnx", options.WorkspaceArgument);
    }

    [Fact]
    public void TryParse_WorkspaceAuto_NoCandidatesReturnsUsageError()
    {
        using TemporaryDirectory temp = TemporaryDirectory.Create();

        bool valid = NavlynMcpServerOptions.TryParse(
            ["--workspace", "auto", "--working-directory", temp.Path],
            out _,
            out string? error,
            out bool showHelp);

        Assert.False(valid);
        Assert.Contains("--workspace auto could not find", error);
        Assert.False(showHelp);
    }

    [Fact]
    public void TryParse_WorkspaceAuto_MultipleBestCandidatesReturnsUsageError()
    {
        using TemporaryDirectory temp = TemporaryDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "a.slnx"), "");
        File.WriteAllText(Path.Combine(temp.Path, "b.slnx"), "");

        bool valid = NavlynMcpServerOptions.TryParse(
            ["--workspace", "auto", "--working-directory", temp.Path],
            out _,
            out string? error,
            out _);

        Assert.False(valid);
        Assert.Contains("a.slnx, b.slnx", error);
        Assert.Contains("Pass --workspace explicitly.", error);
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

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "navlyn-mcp-options-" + Guid.NewGuid().ToString("N"));
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

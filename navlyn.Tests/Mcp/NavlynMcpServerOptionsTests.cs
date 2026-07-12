using Navlyn.Mcp.Configuration;

using Navlyn.Workspaces;

namespace Navlyn.Tests.Mcp;

public sealed class NavlynMcpServerOptionsTests
{
    [Fact]
    public void TryParse_ExplicitWorkspace_UsesRepositoryRootAsWorkingDirectoryAndInProcessByDefault()
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
        Assert.Null(options.NavlynExecutable);
        Assert.False(options.UseExternalCli);
        Assert.Equal(NavlynMcpToolProfile.Full, options.ToolProfile);
        Assert.False(options.DeprecatedToolProfileSpecified);
        Assert.Null(options.DeprecatedToolProfileValue);
        Assert.Equal(WorkspaceRootPolicy.RepoRelative, options.WorkspaceRootPolicy);
    }

    [Fact]
    public void TryParse_MissingWorkspace_DefaultsToAutoDiscovery()
    {
        using TemporaryDirectory temp = TemporaryDirectory.Create();
        string workspace = Path.Combine(temp.Path, "sample.slnx");
        File.WriteAllText(workspace, "");

        bool valid = NavlynMcpServerOptions.TryParse(
            ["--working-directory", temp.Path],
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
        Assert.True(options.UseExternalCli);
        Assert.Equal(["navlyn.dll", "--no-build"], options.NavlynArguments);
        Assert.Equal(30000, options.TimeoutMilliseconds);
        Assert.Equal(1000, options.MaxJsonChars);
    }

    [Fact]
    public void TryParse_ToolProfile_IsAcceptedAsDeprecatedCompatibilityAlias()
    {
        string repoRoot = FindRepositoryRoot();

        bool valid = NavlynMcpServerOptions.TryParse(
            ["--workspace", Path.Combine(repoRoot, "navlyn.slnx"), "--tool-profile", "full"],
            out NavlynMcpServerOptions options,
            out string? error,
            out _);

        Assert.True(valid);
        Assert.Null(error);
        Assert.Equal(NavlynMcpToolProfile.Full, options.ToolProfile);
        Assert.True(options.DeprecatedToolProfileSpecified);
        Assert.Equal("full", options.DeprecatedToolProfileValue);
    }

    [Fact]
    public void TryParse_WorkspaceRootPolicy_OverridesDefault()
    {
        string repoRoot = FindRepositoryRoot();

        bool valid = NavlynMcpServerOptions.TryParse(
            ["--workspace", Path.Combine(repoRoot, "navlyn.slnx"), "--workspace-root-policy", "allow-listed"],
            out NavlynMcpServerOptions options,
            out string? error,
            out _);

        Assert.True(valid);
        Assert.Null(error);
        Assert.Equal(WorkspaceRootPolicy.AllowListed, options.WorkspaceRootPolicy);
    }

    [Fact]
    public void TryParse_DaemonPipe_StoresExplicitPipeName()
    {
        string repoRoot = FindRepositoryRoot();

        bool valid = NavlynMcpServerOptions.TryParse(
            ["--workspace", Path.Combine(repoRoot, "navlyn.slnx"), "--daemon-pipe", "navlyn-test"],
            out NavlynMcpServerOptions options,
            out string? error,
            out _);

        Assert.True(valid);
        Assert.Null(error);
        Assert.Equal("navlyn-test", options.DaemonPipe);
    }

    [Fact]
    public void TryParse_InvalidWorkspaceRootPolicy_ReturnsUsageError()
    {
        string repoRoot = FindRepositoryRoot();

        bool valid = NavlynMcpServerOptions.TryParse(
            ["--workspace", Path.Combine(repoRoot, "navlyn.slnx"), "--workspace-root-policy", "wide-open"],
            out _,
            out string? error,
            out _);

        Assert.False(valid);
        Assert.Equal("--workspace-root-policy must be one of: repo-relative, allow-listed, all.", error);
    }

    [Fact]
    public void TryParse_ToolProfileEnvironment_IsAcceptedAsDeprecatedCompatibilityAlias()
    {
        string repoRoot = FindRepositoryRoot();
        string? previous = Environment.GetEnvironmentVariable(NavlynMcpServerOptions.ToolProfileEnvironmentVariable);
        Environment.SetEnvironmentVariable(NavlynMcpServerOptions.ToolProfileEnvironmentVariable, "review");

        try
        {
            bool valid = NavlynMcpServerOptions.TryParse(
                ["--workspace", Path.Combine(repoRoot, "navlyn.slnx")],
                out NavlynMcpServerOptions options,
                out string? error,
                out _);

            Assert.True(valid);
            Assert.Null(error);
            Assert.Equal(NavlynMcpToolProfile.Review, options.ToolProfile);
            Assert.True(options.DeprecatedToolProfileSpecified);
            Assert.Equal("review", options.DeprecatedToolProfileValue);
        }
        finally
        {
            Environment.SetEnvironmentVariable(NavlynMcpServerOptions.ToolProfileEnvironmentVariable, previous);
        }
    }

    [Fact]
    public void TryParse_CommandLineToolProfile_OverridesEnvironmentForCompatibilityWarning()
    {
        string repoRoot = FindRepositoryRoot();
        string? previous = Environment.GetEnvironmentVariable(NavlynMcpServerOptions.ToolProfileEnvironmentVariable);
        Environment.SetEnvironmentVariable(NavlynMcpServerOptions.ToolProfileEnvironmentVariable, "review");

        try
        {
            bool valid = NavlynMcpServerOptions.TryParse(
                ["--workspace", Path.Combine(repoRoot, "navlyn.slnx"), "--tool-profile", "edit"],
                out NavlynMcpServerOptions options,
                out string? error,
                out _);

            Assert.True(valid);
            Assert.Null(error);
            Assert.Equal(NavlynMcpToolProfile.Edit, options.ToolProfile);
            Assert.True(options.DeprecatedToolProfileSpecified);
            Assert.Equal("edit", options.DeprecatedToolProfileValue);
        }
        finally
        {
            Environment.SetEnvironmentVariable(NavlynMcpServerOptions.ToolProfileEnvironmentVariable, previous);
        }
    }

    [Fact]
    public void TryParse_InvalidToolProfile_ReturnsUsageError()
    {
        string repoRoot = FindRepositoryRoot();

        bool valid = NavlynMcpServerOptions.TryParse(
            ["--workspace", Path.Combine(repoRoot, "navlyn.slnx"), "--tool-profile", "everything"],
            out _,
            out string? error,
            out _);

        Assert.False(valid);
        Assert.Equal("--tool-profile must be one of: reader, review, edit, full.", error);
    }

    [Fact]
    public void TryParse_NavlynArgWithoutExternalExecutableReturnsUsageError()
    {
        string repoRoot = FindRepositoryRoot();

        bool valid = NavlynMcpServerOptions.TryParse(
            ["--workspace", Path.Combine(repoRoot, "navlyn.slnx"), "--navlyn-arg", "navlyn.dll"],
            out _,
            out string? error,
            out _);

        Assert.False(valid);
        Assert.Equal("--navlyn-arg requires --navlyn-executable.", error);
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
    public void TryParse_WorkspaceAuto_PrefersCodeWorkspaceOverSlnx()
    {
        using TemporaryDirectory temp = TemporaryDirectory.Create();
        string workspace = Path.Combine(temp.Path, "sample.code-workspace");
        File.WriteAllText(workspace, """{"folders":[{"path":"."}]}""");
        File.WriteAllText(Path.Combine(temp.Path, "sample.slnx"), "");

        bool valid = NavlynMcpServerOptions.TryParse(
            ["--workspace", "auto", "--working-directory", temp.Path],
            out NavlynMcpServerOptions options,
            out string? error,
            out _);

        Assert.True(valid);
        Assert.Null(error);
        Assert.Equal(workspace, options.Workspace);
        Assert.Equal("sample.code-workspace", options.WorkspaceArgument);
    }

    [Fact]
    public void TryParse_WorkspaceAuto_PrefersNavlynWorkspaceOverCodeWorkspace()
    {
        using TemporaryDirectory temp = TemporaryDirectory.Create();
        string workspace = Path.Combine(temp.Path, "navlyn.workspace.json");
        File.WriteAllText(workspace, """{"primaryWorkspace":"sample.slnx"}""");
        File.WriteAllText(Path.Combine(temp.Path, "sample.code-workspace"), """{"folders":[{"path":"."}]}""");

        bool valid = NavlynMcpServerOptions.TryParse(
            ["--workspace", "auto", "--working-directory", temp.Path],
            out NavlynMcpServerOptions options,
            out string? error,
            out _);

        Assert.True(valid);
        Assert.Null(error);
        Assert.Equal(workspace, options.Workspace);
        Assert.Equal("navlyn.workspace.json", options.WorkspaceArgument);
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
    public void TryParse_MissingWorkspaceWithoutAutoCandidatesReturnsUsageError()
    {
        using TemporaryDirectory temp = TemporaryDirectory.Create();

        bool valid = NavlynMcpServerOptions.TryParse(
            ["--working-directory", temp.Path],
            out _,
            out string? error,
            out bool showHelp);

        Assert.False(valid);
        Assert.Contains("--workspace auto could not find", error);
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

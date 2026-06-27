using Navlyn.Paths;

namespace Navlyn.Tests.Paths;

public sealed class PathDisplayTests
{
    [Fact]
    public void FromRepositoryRoot_ReturnsRepositoryRelativePathForRepoFiles()
    {
        string repoRoot = FindRepositoryRoot();
        string projectPath = Path.Combine(repoRoot, "navlyn", "navlyn.csproj");

        string displayPath = PathDisplay.FromRepositoryRoot(projectPath);

        Assert.Equal("navlyn/navlyn.csproj", displayPath);
    }

    [Fact]
    public void GetInputPathCandidates_AcceptsForwardSlashRelativePath()
    {
        string repoRoot = FindRepositoryRoot();

        IReadOnlyList<string> candidates = PathDisplay.GetInputPathCandidates(
            "navlyn/Cli/NavlynCli.cs",
            anchorPath: null);

        Assert.Contains(Path.Combine(repoRoot, "navlyn", "Cli", "NavlynCli.cs"), candidates);
    }

    [Fact]
    public void GetInputPathCandidates_AcceptsBackslashRelativePath()
    {
        string repoRoot = FindRepositoryRoot();

        IReadOnlyList<string> candidates = PathDisplay.GetInputPathCandidates(
            @"navlyn\Cli\NavlynCli.cs",
            anchorPath: null);

        Assert.Contains(Path.Combine(repoRoot, "navlyn", "Cli", "NavlynCli.cs"), candidates);
    }

    [Fact]
    public void FindRepositoryRoot_FindsRootFromNestedFile()
    {
        string repoRoot = FindRepositoryRoot();
        string nestedPath = Path.Combine(repoRoot, "navlyn", "Cli", "NavlynCli.cs");

        string? actualRoot = PathDisplay.FindRepositoryRoot(nestedPath);

        Assert.Equal(repoRoot, actualRoot);
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

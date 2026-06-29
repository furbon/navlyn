using System.Xml.Linq;

namespace Navlyn.Tests.Mcp;

public sealed class NavlynArchitectureGuardTests
{
    [Fact]
    public void McpProjectReferencesSharedCommandLineButNotNavlynExecutableProject()
    {
        string repoRoot = FindRepositoryRoot();
        string projectPath = Path.Combine(repoRoot, "navlyn.Mcp", "navlyn.Mcp.csproj");
        IReadOnlyList<string> projectReferences = ReadProjectReferences(projectPath);

        Assert.Contains("../Navlyn.CommandLine/Navlyn.CommandLine.csproj", projectReferences);
        Assert.DoesNotContain("../navlyn/navlyn.csproj", projectReferences);
    }

    [Fact]
    public void CliAndMcpShareCoreThroughCommandLineAssembly()
    {
        string repoRoot = FindRepositoryRoot();

        IReadOnlyList<string> cliReferences = ReadProjectReferences(Path.Combine(repoRoot, "navlyn", "navlyn.csproj"));
        IReadOnlyList<string> mcpReferences = ReadProjectReferences(Path.Combine(repoRoot, "navlyn.Mcp", "navlyn.Mcp.csproj"));
        IReadOnlyList<string> commandLineReferences = ReadProjectReferences(Path.Combine(repoRoot, "Navlyn.CommandLine", "Navlyn.CommandLine.csproj"));

        Assert.Contains("../Navlyn.CommandLine/Navlyn.CommandLine.csproj", cliReferences);
        Assert.Contains("../Navlyn.CommandLine/Navlyn.CommandLine.csproj", mcpReferences);
        Assert.Contains("../Navlyn.Core/Navlyn.Core.csproj", commandLineReferences);
    }

    private static IReadOnlyList<string> ReadProjectReferences(string projectPath)
    {
        XDocument document = XDocument.Load(projectPath);
        return [.. document
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value.Replace('\\', '/'))
            .Where(value => value is not null)
            .Select(value => value!)];
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

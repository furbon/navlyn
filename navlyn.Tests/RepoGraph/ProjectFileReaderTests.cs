using Navlyn.RepoGraph;

namespace Navlyn.Tests.RepoGraph;

public sealed class ProjectFileReaderTests
{
    [Fact]
    public void Discovery_PrunesGeneratedTreesAndHonorsDirectoryBoundaries()
    {
        string root = Path.Combine(Path.GetTempPath(), $"navlyn-graph-{Guid.NewGuid():N}");
        try
        {
            foreach (string directory in new[] { "App", "Application", "artifacts/fixture", "App/obj", "node_modules/package" })
            {
                Directory.CreateDirectory(Path.Combine(root, directory));
                File.WriteAllText(Path.Combine(root, directory, "Directory.Build.props"), "<Project />");
            }
            ProjectFileReader reader = new();
            ProjectWithFacts[] projects = [CreateProject("App"), CreateProject("Application")];
            IReadOnlyList<RepoGraphMsbuildFile> files = reader.DiscoverMsbuildFiles(root, projects);
            Assert.Equal(2, files.Count);
            Assert.All(files, file => Assert.Single(file.AppliesToProjectIds));
            Assert.Equal("App", Assert.Single(files.Single(file => file.Path.EndsWith("/App/Directory.Build.props", StringComparison.Ordinal)).AppliesToProjectIds));

            ProjectWithFacts CreateProject(string name)
            {
                string path = Path.Combine(root, name, name + ".csproj");
                File.WriteAllText(path, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
                return new(new RepoGraphProject(name, name, path, "C#", name, "net10.0", ["net10.0"],
                    null, null, null, null, null, null, null), reader.Read(path, root), path);
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}

using Navlyn.Diagnostics;
using Navlyn.Workspaces;

namespace Navlyn.Tests.Workspaces;

public sealed class WorkspaceLoaderCodeWorkspaceTests
{
    [Fact]
    public async Task LoadAsync_CodeWorkspaceWithSingleCandidate_LoadsSelectedWorkspace()
    {
        string repoRoot = FindRepositoryRoot();
        using TemporaryDirectory temp = TemporaryDirectory.Create();
        string codeWorkspace = Path.Combine(temp.Path, "sample.code-workspace");
        string folder = Path.Combine(repoRoot, "tests", "fixtures", "SymbolNavigationFixture");
        File.WriteAllText(codeWorkspace, $$"""
            {
                "folders": [
                    { "path": "{{EscapeJson(folder)}}" }
                ]
            }
            """);

        WorkspaceLoadResult result = await new WorkspaceLoader().LoadAsync(new FileInfo(codeWorkspace), CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Workspace);
        using LoadedWorkspace workspace = result.Workspace;
        Assert.Equal("project", workspace.Kind);
        Assert.Equal("tests/fixtures/SymbolNavigationFixture/SymbolNavigationFixture.csproj", workspace.DisplayPath);
    }

    [Fact]
    public async Task LoadAsync_CodeWorkspaceWithMultipleBestCandidates_ReturnsAmbiguousError()
    {
        using TemporaryDirectory temp = TemporaryDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "a.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        File.WriteAllText(Path.Combine(temp.Path, "b.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        string codeWorkspace = Path.Combine(temp.Path, "ambiguous.code-workspace");
        File.WriteAllText(codeWorkspace, """
            {
                "folders": [
                    { "path": "." }
                ]
            }
            """);

        WorkspaceLoadResult result = await new WorkspaceLoader().LoadAsync(new FileInfo(codeWorkspace), CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.Equal(DiagnosticIds.AmbiguousCodeWorkspace, result.Error.DiagnosticId);
        Assert.Contains("a.csproj, b.csproj", result.Error.Message);
    }

    [Fact]
    public async Task LoadAsync_CodeWorkspaceWithOutsideFolder_AllowsAndWarns()
    {
        string repoRoot = FindRepositoryRoot();
        using TemporaryDirectory codeWorkspaceDirectory = TemporaryDirectory.CreateUnder(repoRoot);
        using TemporaryDirectory externalDirectory = TemporaryDirectory.Create();
        string externalProject = Path.Combine(externalDirectory.Path, "Sample.csproj");
        File.WriteAllText(externalProject, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        string codeWorkspace = Path.Combine(codeWorkspaceDirectory.Path, "outside.code-workspace");
        File.WriteAllText(codeWorkspace, $$"""
            {
                "folders": [
                    { "path": "{{EscapeJson(externalDirectory.Path)}}" }
                ]
            }
            """);

        WorkspaceLoadResult result = await new WorkspaceLoader().LoadAsync(new FileInfo(codeWorkspace), CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Kind == "Warning" &&
            diagnostic.Message.StartsWith("VS Code workspace folder is outside repository root:", StringComparison.Ordinal));
        Assert.NotNull(result.Workspace);
        result.Workspace.Dispose();
    }

    private static string EscapeJson(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
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
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "navlyn-code-workspace-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public static TemporaryDirectory CreateUnder(string parentDirectory)
        {
            string path = System.IO.Path.Combine(parentDirectory, "artifacts", "tests", "code-workspace-" + Guid.NewGuid().ToString("N"));
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

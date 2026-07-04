using Navlyn.Diagnostics;
using Navlyn.Tests.TestSupport;
using Navlyn.Workspaces;

namespace Navlyn.Tests.Workspaces;

[Collection(ResolverComponentTestCollection.Name)]
public sealed class WorkspaceLoaderNavlynWorkspaceTests
{
    [Fact]
    public async Task LoadAsync_NavlynWorkspacePrimaryWorkspace_LoadsSelectedWorkspace()
    {
        string repoRoot = FindRepositoryRoot();
        using TemporaryDirectory temp = TemporaryDirectory.CreateUnder(repoRoot);
        string fixtureProject = Path.Combine(repoRoot, "tests", "fixtures", "SymbolNavigationFixture", "SymbolNavigationFixture.csproj");
        string navlynWorkspace = Path.Combine(temp.Path, "navlyn.workspace.json");
        File.WriteAllText(navlynWorkspace, $$"""
            {
                "primaryWorkspace": "{{EscapeJson(fixtureProject)}}"
            }
            """);

        WorkspaceLoadResult result = await new WorkspaceLoader().LoadAsync(new FileInfo(navlynWorkspace), CancellationToken.None);

        Assert.Null(result.Error);
        Assert.NotNull(result.Workspace);
        using LoadedWorkspace workspace = result.Workspace;
        Assert.Equal("project", workspace.Kind);
        Assert.Equal("tests/fixtures/SymbolNavigationFixture/SymbolNavigationFixture.csproj", workspace.DisplayPath);
    }

    [Fact]
    public async Task LoadAsync_NavlynWorkspaceInvalidSchema_ReturnsInvalidWorkspaceError()
    {
        using TemporaryDirectory temp = TemporaryDirectory.Create();
        string navlynWorkspace = Path.Combine(temp.Path, "navlyn.workspace.json");
        File.WriteAllText(navlynWorkspace, """{"workspaceCandidates":"src"}""");

        WorkspaceLoadResult result = await new WorkspaceLoader().LoadAsync(new FileInfo(navlynWorkspace), CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.Equal(DiagnosticIds.InvalidNavlynWorkspace, result.Error.DiagnosticId);
        Assert.Contains("workspaceCandidates must be an array of strings", result.Error.Message);
    }

    [Fact]
    public async Task LoadAsync_NavlynWorkspaceMultipleBestCandidates_ReturnsAmbiguousError()
    {
        using TemporaryDirectory temp = TemporaryDirectory.Create();
        WriteProject(Path.Combine(temp.Path, "a.csproj"));
        WriteProject(Path.Combine(temp.Path, "b.csproj"));
        string navlynWorkspace = Path.Combine(temp.Path, "navlyn.workspace.json");
        File.WriteAllText(navlynWorkspace, """
            {
                "workspaceCandidates": [ "." ]
            }
            """);

        WorkspaceLoadResult result = await new WorkspaceLoader().LoadAsync(new FileInfo(navlynWorkspace), CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.Equal(DiagnosticIds.AmbiguousNavlynWorkspace, result.Error.DiagnosticId);
        Assert.Contains("a.csproj, b.csproj", result.Error.Message);
    }

    [Fact]
    public async Task LoadAsync_NavlynWorkspaceDefaultRootPolicy_BlocksOutsidePrimaryWorkspace()
    {
        string repoRoot = FindRepositoryRoot();
        using TemporaryDirectory configDirectory = TemporaryDirectory.CreateUnder(repoRoot);
        using TemporaryDirectory externalDirectory = TemporaryDirectory.Create();
        string externalProject = Path.Combine(externalDirectory.Path, "External.csproj");
        WriteProject(externalProject);
        string navlynWorkspace = Path.Combine(configDirectory.Path, "navlyn.workspace.json");
        File.WriteAllText(navlynWorkspace, $$"""
            {
                "primaryWorkspace": "{{EscapeJson(externalProject)}}",
                "defaultRootPolicy": "repo-relative"
            }
            """);

        WorkspaceLoadResult result = await new WorkspaceLoader().LoadAsync(new FileInfo(navlynWorkspace), CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.Equal(DiagnosticIds.WorkspaceRootPolicyViolation, result.Error.DiagnosticId);
    }

    [Fact]
    public async Task LoadAsync_NavlynWorkspaceAllowListedPolicy_AllowsOutsidePrimaryWorkspace()
    {
        string repoRoot = FindRepositoryRoot();
        using TemporaryDirectory configDirectory = TemporaryDirectory.CreateUnder(repoRoot);
        using TemporaryDirectory externalDirectory = TemporaryDirectory.Create();
        string externalProject = Path.Combine(externalDirectory.Path, "External.csproj");
        WriteProject(externalProject);
        string navlynWorkspace = Path.Combine(configDirectory.Path, "navlyn.workspace.json");
        File.WriteAllText(navlynWorkspace, $$"""
            {
                "primaryWorkspace": "{{EscapeJson(externalProject)}}",
                "defaultRootPolicy": "allow-listed",
                "allowRoots": [ "{{EscapeJson(externalDirectory.Path)}}" ]
            }
            """);

        WorkspaceLoadResult result = await new WorkspaceLoader().LoadAsync(new FileInfo(navlynWorkspace), CancellationToken.None);

        Assert.Null(result.Error);
        Assert.NotNull(result.Workspace);
        result.Workspace.Dispose();
    }

    [Fact]
    public async Task LoadAsync_NavlynWorkspaceExcludesGeneratedAndTests_SelectsProductionCandidate()
    {
        using TemporaryDirectory temp = TemporaryDirectory.Create();
        string srcDirectory = Path.Combine(temp.Path, "src");
        string testsDirectory = Path.Combine(temp.Path, "tests");
        string generatedDirectory = Path.Combine(temp.Path, "generated");
        Directory.CreateDirectory(srcDirectory);
        Directory.CreateDirectory(testsDirectory);
        Directory.CreateDirectory(generatedDirectory);
        WriteProject(Path.Combine(srcDirectory, "App.csproj"));
        WriteProject(Path.Combine(testsDirectory, "App.Tests.csproj"));
        WriteProject(Path.Combine(generatedDirectory, "Generated.csproj"));
        string navlynWorkspace = Path.Combine(temp.Path, "navlyn.workspace.json");
        File.WriteAllText(navlynWorkspace, """
            {
                "workspaceCandidates": [ "src", "tests", "generated" ],
                "generatedFolders": [ "generated" ],
                "tests": { "include": false }
            }
            """);

        WorkspaceLoadResult result = await new WorkspaceLoader().LoadAsync(new FileInfo(navlynWorkspace), CancellationToken.None);

        Assert.Null(result.Error);
        Assert.NotNull(result.Workspace);
        using LoadedWorkspace workspace = result.Workspace;
        Assert.Equal("project", workspace.Kind);
        Assert.EndsWith("src/App.csproj", workspace.DisplayPath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_NavlynWorkspacePrimaryCodeWorkspace_ResolvesCodeWorkspaceCandidate()
    {
        using TemporaryDirectory temp = TemporaryDirectory.Create();
        string srcDirectory = Path.Combine(temp.Path, "src");
        Directory.CreateDirectory(srcDirectory);
        WriteProject(Path.Combine(srcDirectory, "App.csproj"));
        string codeWorkspace = Path.Combine(temp.Path, "sample.code-workspace");
        File.WriteAllText(codeWorkspace, """
            {
                "folders": [
                    { "path": "src" }
                ]
            }
            """);
        string navlynWorkspace = Path.Combine(temp.Path, "navlyn.workspace.json");
        File.WriteAllText(navlynWorkspace, """
            {
                "primaryWorkspace": "sample.code-workspace"
            }
            """);

        WorkspaceLoadResult result = await new WorkspaceLoader().LoadAsync(new FileInfo(navlynWorkspace), CancellationToken.None);

        Assert.Null(result.Error);
        Assert.NotNull(result.Workspace);
        using LoadedWorkspace workspace = result.Workspace;
        Assert.Equal("project", workspace.Kind);
        Assert.EndsWith("src/App.csproj", workspace.DisplayPath, StringComparison.Ordinal);
    }

    private static void WriteProject(string path)
    {
        File.WriteAllText(path, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
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
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "navlyn-workspace-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public static TemporaryDirectory CreateUnder(string parentDirectory)
        {
            string path = System.IO.Path.Combine(parentDirectory, "artifacts", "tests", "navlyn-workspace-" + Guid.NewGuid().ToString("N"));
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

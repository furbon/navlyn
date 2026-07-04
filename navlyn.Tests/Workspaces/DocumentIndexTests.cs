using Navlyn.Paths;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.Tests.Workspaces;

public sealed class DocumentIndexTests
{
    [Fact]
    public async Task LoadAsync_BuildsDocumentIndexWithPathAndDocumentMetadata()
    {
        string repoRoot = FindRepositoryRoot();
        string workspacePath = Path.Combine(repoRoot, "tests", "fixtures", "SymbolNavigationFixture", "SymbolNavigationFixture.csproj");

        WorkspaceLoadResult loadResult = await new WorkspaceLoader().LoadAsync(new FileInfo(workspacePath), CancellationToken.None);

        Assert.Null(loadResult.Error);
        Assert.NotNull(loadResult.Workspace);
        using LoadedWorkspace workspace = loadResult.Workspace;
        DocumentIndex index = Assert.IsType<DocumentIndex>(workspace.DocumentIndex);
        Assert.Same(index, DocumentIndexProvider.GetOrCreate(workspace.Solution));
        Assert.True(index.DocumentCount >= 2);
        Assert.True(index.EstimatedMemoryBytes > 0);

        string absoluteFile = Path.Combine(repoRoot, "tests", "fixtures", "SymbolNavigationFixture", "FixtureCode.cs");
        DocumentIndexLookupResult absoluteResult = index.Find([absoluteFile], project: null);
        Assert.NotNull(absoluteResult.Entry);
        Assert.Equal(Path.GetFullPath(absoluteFile), absoluteResult.Entry.FullPath);
        Assert.Equal("FixtureCode.cs", absoluteResult.Entry.FileName);
        Assert.Equal("tests/fixtures/SymbolNavigationFixture/FixtureCode.cs", absoluteResult.Entry.RepositoryRelativePath);
        Assert.False(string.IsNullOrWhiteSpace(absoluteResult.Entry.DocumentId));
        Assert.False(string.IsNullOrWhiteSpace(absoluteResult.Entry.ProjectId));

        DocumentIndexLookupResult relativeResult = index.Find(["tests/fixtures/SymbolNavigationFixture/FixtureCode.cs"], project: null);
        Assert.Same(absoluteResult.Entry.Document, relativeResult.Entry?.Document);
        Assert.NotEmpty(index.FindByFileName("FixtureCode.cs"));
    }

    [Fact]
    public async Task SourceDocumentResolver_UsesIndexedPathResolutionForRepositoryRelativeInput()
    {
        string repoRoot = FindRepositoryRoot();
        string workspacePath = Path.Combine(repoRoot, "tests", "fixtures", "SymbolNavigationFixture", "SymbolNavigationFixture.csproj");

        WorkspaceLoadResult loadResult = await new WorkspaceLoader().LoadAsync(new FileInfo(workspacePath), CancellationToken.None);

        Assert.Null(loadResult.Error);
        Assert.NotNull(loadResult.Workspace);
        using LoadedWorkspace workspace = loadResult.Workspace;
        SourceDocumentResolutionResult result = await new SourceDocumentResolver().ResolveAsync(
            workspace.Solution,
            new FileInfo("tests/fixtures/SymbolNavigationFixture/FixtureCode.cs"),
            project: null,
            excludeGenerated: false,
            CancellationToken.None);

        Assert.Null(result.Error);
        Assert.NotNull(result.Resolution);
        Assert.Equal(
            PathDisplay.FromCurrentDirectory(Path.Combine(repoRoot, "tests", "fixtures", "SymbolNavigationFixture", "FixtureCode.cs")),
            result.Resolution.DisplayPath);
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

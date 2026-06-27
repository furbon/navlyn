using Navlyn.RepoGraph;
using Navlyn.Tests.TestSupport;
using Navlyn.Workspaces;

namespace Navlyn.Tests.RepoGraph;

[Collection(ResolverComponentTestCollection.Name)]
public sealed class RepoGraphResolverComponentTests(ResolverComponentTestFixture fixture)
{
    [Fact]
    public async Task Resolve_NavlynSolution_ReturnsPackageReferencesAndTestRelationship()
    {
        string workspacePath = Path.Combine(fixture.RepoRoot, "navlyn.slnx");
        WorkspaceLoadResult loadResult = await new WorkspaceLoader().LoadAsync(new FileInfo(workspacePath), CancellationToken.None);
        Assert.Null(loadResult.Error);

        using LoadedWorkspace workspace = loadResult.Workspace!;
        RepoGraphResult result = new RepoGraphResolver().Resolve(
            workspace,
            workspace.Solution.Projects.ToArray(),
            new RepoGraphOptions(
                IncludePackages: true,
                IncludeMsbuildFiles: true,
                IncludePreprocessorSymbols: true,
                IncludeClassification: true,
                RelationshipLimit: 200));

        Assert.Equal("repo-graph", result.Command);
        Assert.Contains(result.Projects.Items, project => project.Name == "navlyn" && project.Classification?.Kind == "tooling");
        Assert.Contains(result.Projects.Items, project => project.Name == "navlyn.Tests" && project.Classification?.Kind == "test");
        Assert.Contains(result.Edges.PackageReferences, package => package.ProjectId.Contains("navlyn/navlyn.csproj", StringComparison.Ordinal) && package.Name == "System.CommandLine");
        Assert.Contains(result.Edges.ProjectReferences, edge => edge.FromProjectId.Contains("navlyn.Tests/navlyn.Tests.csproj", StringComparison.Ordinal) && edge.ToProjectId is not null && edge.ToProjectId.Contains("navlyn/navlyn.csproj", StringComparison.Ordinal));
        Assert.Contains(result.Relationships.Items, relationship => relationship.Kind == "tests" && relationship.FromProjectId.Contains("navlyn.Tests/navlyn.Tests.csproj", StringComparison.Ordinal) && relationship.ToProjectId.Contains("navlyn/navlyn.csproj", StringComparison.Ordinal));
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task Resolve_RelationshipLimit_TruncatesRelationships()
    {
        string workspacePath = Path.Combine(fixture.RepoRoot, "navlyn.slnx");
        WorkspaceLoadResult loadResult = await new WorkspaceLoader().LoadAsync(new FileInfo(workspacePath), CancellationToken.None);
        Assert.Null(loadResult.Error);

        using LoadedWorkspace workspace = loadResult.Workspace!;
        RepoGraphResult result = new RepoGraphResolver().Resolve(
            workspace,
            workspace.Solution.Projects.ToArray(),
            new RepoGraphOptions(
                IncludePackages: true,
                IncludeMsbuildFiles: false,
                IncludePreprocessorSymbols: false,
                IncludeClassification: true,
                RelationshipLimit: 1));

        Assert.True(result.Relationships.TotalItems >= result.Relationships.Items.Count);
        Assert.True(result.Relationships.Truncated);
        Assert.Single(result.Relationships.Items);
    }
}


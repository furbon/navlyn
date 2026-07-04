using Navlyn.Testing;
using Navlyn.Tests.TestSupport;
using Navlyn.Workspaces;

namespace Navlyn.Tests.Testing;

[Collection(ResolverComponentTestCollection.Name)]
public sealed class TestDiscoveryResolverComponentTests(ResolverComponentTestFixture fixture)
{
    [Fact]
    public async Task Discover_NavlynSolution_FindsXunitTests()
    {
        string workspacePath = Path.Combine(fixture.RepoRoot, "navlyn.slnx");
        WorkspaceLoadResult loadResult = await new WorkspaceLoader().LoadAsync(new FileInfo(workspacePath), CancellationToken.None);
        Assert.Null(loadResult.Error);

        using LoadedWorkspace workspace = loadResult.Workspace!;
        TestDiscoveryResult result = await new TestDiscoveryResolver().DiscoverAsync(
            workspace.Solution.Projects.ToArray(),
            explicitTestProjects: null,
            excludeGenerated: false,
            includeSnippets: false,
            snippetLines: 1,
            CancellationToken.None);

        Assert.Contains(result.TestProjects, project => project.Path == "navlyn.Tests/navlyn.Tests.csproj");
        Assert.Contains(result.Candidates, candidate => candidate.Framework == "xunit" && candidate.Name == "Discover_NavlynSolution_FindsXunitTests");
    }
}


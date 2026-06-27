using Navlyn.Entrypoints;
using Navlyn.Tests.TestSupport;
using Navlyn.Workspaces;

namespace Navlyn.Tests.Entrypoints;

[Collection(ResolverComponentTestCollection.Name)]
public sealed class FrameworkEntrypointDiscoveryResolverComponentTests(ResolverComponentTestFixture fixture)
{
    [Fact]
    public async Task Discover_Fixture_FindsAspNetCoreAndWorkerEntrypoints()
    {
        string workspacePath = Path.Combine(fixture.RepoRoot, "tests", "fixtures", "FrameworkEntrypointsFixture", "FrameworkEntrypointsFixture.csproj");
        WorkspaceLoadResult loadResult = await new WorkspaceLoader().LoadAsync(new FileInfo(workspacePath), CancellationToken.None);
        Assert.Null(loadResult.Error);

        using LoadedWorkspace workspace = loadResult.Workspace!;
        FrameworkEntrypointsSection result = await new FrameworkEntrypointDiscoveryResolver().DiscoverSectionAsync(
            workspace,
            workspace.Solution.Projects.ToArray(),
            new FrameworkEntrypointOptions(
                Frameworks: ["aspnetcore", "worker"],
                Limit: 20,
                EvidenceLimit: 5,
                IncludeSnippets: false,
                SnippetLines: 1,
                ExcludeGenerated: false),
            CancellationToken.None);

        Assert.Contains(result.Items, item => item.EntrypointKind == "aspnetcore-controller-action" && item.Name == "Get" && item.Confidence == "high");
        Assert.Contains(result.Items, item => item.EntrypointKind == "aspnetcore-minimal-api-handler" && item.Name == "Handle");
        Assert.DoesNotContain(result.Items, item => item.Name == "Helper");
        Assert.Contains(result.Items, item => item.EntrypointKind == "worker-backgroundservice-execute" && item.Name == "ExecuteAsync");
        Assert.Contains(result.Items, item => item.EntrypointKind == "worker-ihostedservice-start" && item.Name == "StartAsync");
    }
}

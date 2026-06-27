using Navlyn.ReviewPacks;
using Navlyn.Tests.TestSupport;
using Navlyn.Workspaces;

namespace Navlyn.Tests.ReviewPacks;

[Collection(ResolverComponentTestCollection.Name)]
public sealed class ReviewPackResolverComponentTests(ResolverComponentTestFixture fixture)
{
    [Fact]
    public async Task ResolveAsync_WorkspaceScope_ReturnsRepresentativePackFindings()
    {
        string workspacePath = Path.Combine(fixture.RepoRoot, "tests", "fixtures", "ReviewPacksFixture", "ReviewPacksFixture.csproj");
        WorkspaceLoadResult loadResult = await new WorkspaceLoader().LoadAsync(new FileInfo(workspacePath), CancellationToken.None);
        Assert.Null(loadResult.Error);

        using LoadedWorkspace workspace = loadResult.Workspace!;
        string architectureConfig = Path.Combine(fixture.RepoRoot, "tests", "fixtures", "ReviewPacksFixture", ".navlyn.yml");
        ReviewPackExecutionResult result = await new ReviewPackResolver().ResolveAsync(
            workspace,
            workspace.Solution.Projects.ToArray(),
            projectFilters: null,
            new ReviewPackOptions(
                ReviewPackNames.Ordered,
                "workspace",
                DiffRequest: null,
                ProjectFilters: [],
                ExcludeGenerated: false,
                FindingLimit: 100,
                EvidenceLimit: 5,
                SymbolLimit: 100,
                FileLimit: 20,
                IncludeSnippets: false,
                SnippetLines: 1,
                ArchitectureConfig: architectureConfig),
            CancellationToken.None);

        Assert.Null(result.Error);
        ReviewPackResult review = result.Result!;
        Assert.Contains(review.Findings, finding => finding.RuleId == "async.sync-over-async");
        Assert.Contains(review.Findings, finding => finding.RuleId == "async.async-void");
        Assert.Contains(review.Findings, finding => finding.RuleId == "disposal.created-disposable-not-disposed");
        Assert.Contains(review.Findings, finding => finding.RuleId == "nullability.null-forgiving");
        Assert.Contains(review.Findings, finding => finding.RuleId == "security.auth-surface-signal");
        Assert.Contains(review.Findings, finding => finding.RuleId == "architecture.namespace-dependency-violation");
    }

    [Fact]
    public async Task ResolveAsync_ArchitecturePack_UsesWorkspaceAnchoredDefaultConfig()
    {
        string workspacePath = Path.Combine(fixture.RepoRoot, "tests", "fixtures", "ReviewPacksFixture", "ReviewPacksFixture.csproj");
        WorkspaceLoadResult loadResult = await new WorkspaceLoader().LoadAsync(new FileInfo(workspacePath), CancellationToken.None);
        Assert.Null(loadResult.Error);

        using LoadedWorkspace workspace = loadResult.Workspace!;
        ReviewPackExecutionResult result = await new ReviewPackResolver().ResolveAsync(
            workspace,
            workspace.Solution.Projects.ToArray(),
            projectFilters: null,
            new ReviewPackOptions(
                ["architecture"],
                "workspace",
                DiffRequest: null,
                ProjectFilters: [],
                ExcludeGenerated: false,
                FindingLimit: 100,
                EvidenceLimit: 5,
                SymbolLimit: 100,
                FileLimit: 20,
                IncludeSnippets: false,
                SnippetLines: 1,
                ArchitectureConfig: null),
            CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Contains(result.Result!.Findings, finding => finding.RuleId == "architecture.namespace-dependency-violation");
    }
}

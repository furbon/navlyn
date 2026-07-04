using Navlyn.Symbols;
using Navlyn.Testing;
using Navlyn.Tests.TestSupport;
using Navlyn.Workspaces;

namespace Navlyn.Tests.Testing;

[Collection(ResolverComponentTestCollection.Name)]
public sealed class TestImpactResolverComponentTests(ResolverComponentTestFixture fixture)
{
    [Fact]
    public async Task ResolveForSymbol_RepoGraphResolver_ReturnsRelatedComponentTests()
    {
        string workspacePath = Path.Combine(fixture.RepoRoot, "navlyn.slnx");
        WorkspaceLoadResult loadResult = await new WorkspaceLoader().LoadAsync(new FileInfo(workspacePath), CancellationToken.None);
        Assert.Null(loadResult.Error);

        using LoadedWorkspace workspace = loadResult.Workspace!;
        FuzzyQueryOptions query = new(
            Query: "RepoGraphResolver",
            AssumeKinds: ["NamedType"],
            Match: "smart",
            CaseSensitive: null,
            ExcludeGenerated: false,
            Limit: 20,
            CandidateId: null,
            Selection: new FuzzySelectionOptions("fail", "medium", ExplainSelection: false));
        var subjectProjects = workspace.Solution.Projects.Where(project => project.Name == "Navlyn.Core(net10.0)").ToArray();
        FuzzyCandidateResolution resolution = await new FuzzyDiscoveryResolver().ResolveCandidatesForSelectionAsync(
            subjectProjects,
            query,
            CancellationToken.None);
        Assert.NotNull(resolution.SelectedCandidate);

        FuzzySymbolCandidate candidate = resolution.SelectedCandidate!;
        TestSubject subject = new(
            candidate.Name,
            candidate.Kind,
            candidate.Container,
            candidate.Facts,
            candidate.Path,
            candidate.Line,
            candidate.Column,
            candidate.EndLine,
            candidate.EndColumn);
        TestImpactResolution impact = await new TestImpactResolver().ResolveForSymbolAsync(
            workspace,
            subjectProjects,
            explicitTestProjects: workspace.Solution.Projects.Where(project => project.Name == "navlyn.Tests").ToArray(),
            subject,
            new TestImpactOptions(TestLimit: 10, ReferenceLimit: 200, IncludeSnippets: false, SnippetLines: 1, ExcludeGenerated: false),
            CancellationToken.None);

        Assert.Contains(impact.Tests.Candidates, test => test.Path == "navlyn.Tests/RepoGraph/RepoGraphResolverComponentTests.cs");
        Assert.Contains(impact.Tests.Candidates, test => test.ReasonCodes.Contains("direct-reference-to-symbol"));
    }
}


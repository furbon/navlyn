using Navlyn.Symbols;
using Navlyn.Tests.TestSupport;

namespace Navlyn.Tests.Symbols;

[Collection(ResolverComponentTestCollection.Name)]
public sealed class ResolveTargetResolverComponentTests(ResolverComponentTestFixture fixture)
{
    [Fact]
    public async Task ResolveFuzzyAsync_Query_ReturnsSelectedTargetAndCandidateId()
    {
        ResolveTargetResult result = await new ResolveTargetResolver().ResolveFuzzyAsync(
            fixture.FuzzyDiscoveryWorkspace,
            new FuzzyQueryOptions(
                Query: "EnemyManagerTools",
                AssumeKinds: ["NamedType"],
                Match: "smart",
                CaseSensitive: null,
                ExcludeGenerated: true,
                Limit: 5,
                CandidateId: null,
                Selection: new FuzzySelectionOptions("select", "medium", false)),
            fixture.FuzzyDiscoveryWorkspace.Solution.Projects.ToArray(),
            projectFilters: null,
            CancellationToken.None);

        Assert.Equal("resolve-target", result.Command);
        Assert.Equal("query", result.SelectionInput.Mode);
        Assert.Equal("high", result.Confidence);
        Assert.NotNull(result.SelectedTarget);
        Assert.Equal("EnemyManagerTools", result.SelectedTarget.Name);
        Assert.NotNull(result.CandidateId);
        Assert.Null(result.AmbiguityReason);
        Assert.Contains(result.RecommendedNextActions, action => action.Command == "definition");
    }

    [Fact]
    public async Task ResolveSourcePositionAsync_ReturnsSelectedTargetWithoutCandidateId()
    {
        SourcePosition position = fixture.SymbolNavigationSource.Position("IWidgetFormatter formatter", "IWidgetFormatter");

        ResolveTargetResult result = await new ResolveTargetResolver().ResolveSourcePositionAsync(
            fixture.SymbolNavigationWorkspace,
            fixture.SymbolNavigationSource.File,
            position.Line,
            position.Column,
            project: null,
            excludeGenerated: true,
            CancellationToken.None);

        Assert.Equal("sourcePosition", result.SelectionInput.Mode);
        Assert.Equal("high", result.Confidence);
        Assert.NotNull(result.SelectedTarget);
        Assert.Equal("IWidgetFormatter", result.SelectedTarget.Name);
        Assert.Null(result.CandidateId);
        Assert.Contains(result.RecommendedNextActions, action => action.Command == "symbol-info");
    }
}

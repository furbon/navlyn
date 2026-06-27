using Navlyn.ContextPacks;
using Navlyn.Symbols;
using Navlyn.Tests.TestSupport;

namespace Navlyn.Tests.ContextPacks;

[Collection(ResolverComponentTestCollection.Name)]
public sealed class ContextPackResolverComponentTests(ResolverComponentTestFixture fixture)
{
    [Fact]
    public async Task ResolveQueryAsync_UniqueQuery_ReturnsRankedDefinitionContext()
    {
        ContextPackResult result = await ResolveQueryAsync("EnemyManagerTools", ["NamedType"]);

        Assert.Equal("context-pack", result.Command);
        Assert.Equal("query", result.Mode);
        Assert.Equal("understand", result.Goal);
        Assert.Equal("high", result.Selection!.Confidence);
        Assert.Equal("EnemyManagerTools", result.Selection.SelectedCandidate!.Name);
        Assert.NotNull(result.Pack.Root);
        ContextPackItem first = result.Pack.Items[0];
        Assert.Equal("definition", first.Kind);
        Assert.Equal("selected-symbol-definition", Assert.Single(first.ReasonCodes));
        Assert.NotNull(first.Content);
    }

    [Fact]
    public async Task ResolveQueryAsync_AmbiguousQuery_DoesNotMixCandidateContext()
    {
        ContextPackResult result = await ResolveQueryAsync("EnemyManager", ["NamedType"]);

        Assert.Equal("ambiguous", result.Selection!.Confidence);
        Assert.Null(result.Selection.SelectedCandidate);
        Assert.Null(result.Pack.Root);
        Assert.Empty(result.Pack.Items);
        Assert.Contains(result.NextActions, action => action.Command == "find");
    }

    private async Task<ContextPackResult> ResolveQueryAsync(string query, IReadOnlyList<string> assumeKinds)
    {
        ContextPackOptions options = new(
            Goal: "understand",
            BudgetTokens: 8000,
            ItemLimit: 20,
            SnippetPolicy: "signature",
            SnippetLines: 1,
            CandidateLimit: 20,
            MemberLimit: 20,
            ReferenceLimit: 20,
            RelationLimit: 10,
            FileLimit: 10,
            QueryDiagnosticLimit: 10,
            SymbolLimit: 10,
            ImpactLimit: 10,
            DiffDiagnosticLimit: 10,
            RelatedTestLimit: 10,
            Depth: 2);

        return await new ContextPackResolver().ResolveQueryAsync(
            fixture.FuzzyDiscoveryWorkspace,
            new FuzzyQueryOptions(
                Query: query,
                AssumeKinds: assumeKinds,
                Match: "smart",
                CaseSensitive: null,
                ExcludeGenerated: true,
                Limit: options.CandidateLimit),
            fixture.FuzzyDiscoveryWorkspace.Solution.Projects.ToArray(),
            projectFilters: null,
            excludeGenerated: true,
            options,
            CancellationToken.None);
    }
}

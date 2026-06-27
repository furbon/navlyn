using Microsoft.CodeAnalysis;
using Navlyn.Symbols;
using Navlyn.Tests.TestSupport;

namespace Navlyn.Tests.Symbols;

[Collection(ResolverComponentTestCollection.Name)]
public sealed class FuzzyDiscoveryResolverComponentTests(ResolverComponentTestFixture fixture)
{
    [Fact]
    public async Task FindAsync_UniqueType_SelectsHighConfidenceCandidate()
    {
        FuzzyFindResult result = await new FuzzyDiscoveryResolver().FindAsync(
            fixture.FuzzyDiscoveryWorkspace,
            new FuzzyQueryOptions(
                Query: "EnemyManagerTools",
                AssumeKinds: ["NamedType"],
                Match: "auto",
                CaseSensitive: null,
                ExcludeGenerated: true,
                Limit: null),
            Projects(),
            projectFilters: null,
            CancellationToken.None);

        Assert.Equal("high", result.Confidence);
        Assert.Equal("EnemyManagerTools", result.SelectedCandidate!.Name);
        Assert.Equal("NamedType", result.SelectedCandidate.Kind);
        Assert.Contains("single-candidate", result.SelectedCandidate.ReasonCodes);
        Assert.StartsWith("sym:v1:", result.SelectedCandidate.CandidateId);
        Assert.Equal("EnemyManagerTools", result.SelectedCandidate.Selector!.Name);
        Assert.Equal("net10.0", result.SelectedCandidate.Selector.TargetFramework);
        ResolverAssert.PathEndsWith(result.SelectedCandidate.Path, fixture.FuzzyDiscoverySource);

        FuzzyNextAction referencesAction = Assert.Single(result.NextActions, action => action.Command == "references");
        Assert.Equal(result.SelectedCandidate.CandidateId, referencesAction.CandidateId);
        Assert.Equal("navlyn_exact_navigation", referencesAction.McpTool);
        Assert.NotNull(referencesAction.Arguments);
        Assert.Equal("references", referencesAction.Arguments["operation"]);
        Assert.Equal(result.SelectedCandidate.CandidateId, referencesAction.Arguments["candidateId"]);
    }

    [Fact]
    public async Task FindAsync_DuplicateExactTypes_ReturnsAmbiguousResult()
    {
        FuzzyFindResult result = await new FuzzyDiscoveryResolver().FindAsync(
            fixture.FuzzyDiscoveryWorkspace,
            new FuzzyQueryOptions(
                Query: "EnemyManager",
                AssumeKinds: ["NamedType"],
                Match: "auto",
                CaseSensitive: null,
                ExcludeGenerated: true,
                Limit: null),
            Projects(),
            projectFilters: null,
            CancellationToken.None);

        Assert.Equal("ambiguous", result.Confidence);
        Assert.Null(result.SelectedCandidate);
        Assert.True(result.CandidateCount >= 2);
        Assert.All(
            result.Candidates.Where(candidate => candidate.Name == "EnemyManager"),
            candidate => Assert.Contains("exact-name-match", candidate.ReasonCodes));
    }

    [Fact]
    public async Task WhereUsedAsync_SelectedMethod_ReturnsReferenceSummaryWithContainingSymbols()
    {
        FuzzyWhereUsedResult result = await new FuzzyDiscoveryResolver().WhereUsedAsync(
            fixture.FuzzyDiscoveryWorkspace,
            new FuzzyQueryOptions(
                Query: "Spawn",
                AssumeKinds: ["Method"],
                Match: "auto",
                CaseSensitive: null,
                ExcludeGenerated: true,
                Limit: null),
            new FuzzyLocationOptions(
                Limit: 10,
                FileLimit: 10,
                IncludeSnippets: false,
                SnippetLines: 0,
                ExcludeGenerated: true),
            Projects(),
            projectFilters: null,
            CancellationToken.None);

        Assert.Equal("medium", result.Confidence);
        Assert.Equal("Spawn", result.SelectedCandidate!.Name);
        Assert.Equal(2, result.TotalMatches);
        Assert.All(
            result.References!,
            reference =>
            {
                Assert.NotNull(reference.ContainingSymbol);
                Assert.True(reference.EndColumn > reference.Column);
                Assert.Equal("invoke", reference.UsageKind);
            });
        Assert.Equal("invoke", Assert.Single(result.UsageKindCounts!).UsageKind);
    }

    [Fact]
    public async Task AboutAsync_CandidateId_RoundTripsSelectedCandidate()
    {
        FuzzyFindResult find = await new FuzzyDiscoveryResolver().FindAsync(
            fixture.FuzzyDiscoveryWorkspace,
            new FuzzyQueryOptions(
                Query: "EnemyManagerTools",
                AssumeKinds: ["NamedType"],
                Match: "auto",
                CaseSensitive: null,
                ExcludeGenerated: true,
                Limit: null),
            Projects(),
            projectFilters: null,
            CancellationToken.None);

        string candidateId = find.SelectedCandidate!.CandidateId!;
        FuzzyAboutResult about = await new FuzzyDiscoveryResolver().AboutAsync(
            fixture.FuzzyDiscoveryWorkspace,
            new FuzzyQueryOptions(
                Query: candidateId,
                AssumeKinds: [],
                Match: "smart",
                CaseSensitive: null,
                ExcludeGenerated: true,
                Limit: null,
                CandidateId: candidateId),
            new FuzzyAboutOptions(
                MemberLimit: 10,
                ReferenceLimit: 10,
                RelationLimit: 10,
                IncludeSnippets: false,
                SnippetLines: 0,
                ExcludeGenerated: true),
            Projects(),
            projectFilters: null,
            CancellationToken.None);

        Assert.Equal("high", about.Confidence);
        Assert.Equal(candidateId, about.SelectedCandidate!.CandidateId);
        Assert.Equal("candidateId", about.SelectionInput!.Mode);
    }

    [Fact]
    public async Task FindAsync_MinConfidenceHigh_DoesNotSelectMediumCandidate()
    {
        FuzzyFindResult result = await new FuzzyDiscoveryResolver().FindAsync(
            fixture.FuzzyDiscoveryWorkspace,
            new FuzzyQueryOptions(
                Query: "Spawn",
                AssumeKinds: ["Method"],
                Match: "auto",
                CaseSensitive: null,
                ExcludeGenerated: true,
                Limit: null,
                Selection: new FuzzySelectionOptions("group", "high", ExplainSelection: true)),
            Projects(),
            projectFilters: null,
            CancellationToken.None);

        Assert.Equal("medium", result.Confidence);
        Assert.Null(result.SelectedCandidate);
        Assert.Contains("confidence-below-minimum", result.SelectionExplanation!.AmbiguityReasons);
    }

    [Theory]
    [InlineData("bad")]
    [InlineData("sym:v1:not-hex")]
    public void TryParseCandidateId_RejectsMalformedIds(string candidateId)
    {
        Assert.False(FuzzyCandidateIdentity.TryParseCandidateId(candidateId));
    }

    private IReadOnlyList<Project> Projects()
    {
        return fixture.FuzzyDiscoveryWorkspace.Solution.Projects.ToArray();
    }
}

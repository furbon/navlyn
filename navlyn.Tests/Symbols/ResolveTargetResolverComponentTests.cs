using Navlyn.Symbols;
using Navlyn.Tests.TestSupport;

namespace Navlyn.Tests.Symbols;

[Collection(ResolverComponentTestCollection.Name)]
public sealed class ResolveTargetResolverComponentTests(ResolverComponentTestFixture fixture)
{
    [Theory]
    [InlineData("class", true)]
    [InlineData("interface", false)]
    [InlineData("record", false)]
    public async Task TypeKindFiltersQueryCandidatesIndependentlyOfRankingHints(string kind, bool selected)
    {
        using var selection = Navlyn.Workspaces.WorkspaceSelectionScope.Begin(null, kind);
        ResolveTargetResult result = await new ResolveTargetResolver().ResolveFuzzyAsync(
            fixture.FuzzyDiscoveryWorkspace, new FuzzyQueryOptions("EnemyManagerTools", ["interface"], "exact", null, true, 20),
            fixture.FuzzyDiscoveryWorkspace.Solution.Projects.ToArray(), null, CancellationToken.None);
        Assert.Equal(selected, result.SelectedTarget is not null);
        Assert.Equal(kind, result.SelectionInput.TypeKind);
    }

    [Theory]
    [InlineData("record", "R", true)]
    [InlineData("record-class", "R", true)]
    [InlineData("record-struct", "R", false)]
    [InlineData("record-struct", "S", true)]
    [InlineData("class", "R", true)]
    [InlineData("struct", "S", true)]
    [InlineData("interface", "I", true)]
    public void TypeKindUsesRoslynRecordAndTypeFacts(string filter, string name, bool expected)
    {
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("public record R {} public record struct S {} public interface I {}");
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("Kinds", [tree],
            [Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
        var symbol = compilation.GetTypeByMetadataName(name)!;
        using var selection = Navlyn.Workspaces.WorkspaceSelectionScope.Begin(null, filter);
        Assert.Equal(expected, Navlyn.Workspaces.WorkspaceSelectionScope.IncludesType(SymbolFactsBuilder.Create(symbol)));
    }

    [Theory]
    [InlineData("EnemyManagerTools.Use", "exact", "Use")]
    [InlineData("Alpha.EnemyManagerTools.Use", "smart", "Use")]
    [InlineData("global::Alpha.EnemyManagerTools.Use", "contains", "Use")]
    [InlineData("Alpha.EnemyManager.NestedEnemy", "exact", "NestedEnemy")]
    public async Task QualifiedQuery_ConstrainsTheSemanticContainer(string query, string match, string expectedName)
    {
        ResolveTargetResult result = await new ResolveTargetResolver().ResolveFuzzyAsync(
            fixture.FuzzyDiscoveryWorkspace, new FuzzyQueryOptions(query, [], match, null, true, 20),
            fixture.FuzzyDiscoveryWorkspace.Solution.Projects.ToArray(), null, CancellationToken.None);
        Assert.NotNull(result.SelectedTarget);
        Assert.Equal(expectedName, result.SelectedTarget.Name);
        Assert.StartsWith("Alpha.", result.SelectedTarget.Container, StringComparison.Ordinal);
        Assert.NotNull(result.CandidateId);
    }

    [Fact]
    public async Task QualifiedQuery_DoesNotFallBackToAnotherContainer()
    {
        ResolveTargetResult result = await new ResolveTargetResolver().ResolveFuzzyAsync(
            fixture.FuzzyDiscoveryWorkspace, new FuzzyQueryOptions("Missing.EnemyManagerTools", [], "exact", null, true, 20),
            fixture.FuzzyDiscoveryWorkspace.Solution.Projects.ToArray(), null, CancellationToken.None);
        Assert.Null(result.SelectedTarget);
        Assert.Equal(0, result.TotalCandidates);
        Assert.Contains("External DLL declarations are not searched", Assert.Single(result.Warnings), StringComparison.Ordinal);
    }

    [Fact]
    public async Task QualifiedQuery_PreservesOverloadAmbiguity()
    {
        ResolveTargetResult result = await new ResolveTargetResolver().ResolveFuzzyAsync(
            fixture.SymbolNavigationWorkspace, new FuzzyQueryOptions("Widget.Format", ["Method"], "exact", null, true, 20),
            fixture.SymbolNavigationWorkspace.Solution.Projects.ToArray(), null, CancellationToken.None);
        Assert.Null(result.SelectedTarget);
        Assert.Equal("ambiguous", result.Confidence);
        Assert.Equal(2, result.TotalCandidates);
    }

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

    [Fact]
    public async Task ResolveFuzzyAsync_AmbiguousQuery_ReturnsAmbiguitySummary()
    {
        ResolveTargetResult result = await new ResolveTargetResolver().ResolveFuzzyAsync(
            fixture.FuzzyDiscoveryWorkspace,
            new FuzzyQueryOptions(
                Query: "EnemyManager",
                AssumeKinds: ["NamedType"],
                Match: "smart",
                CaseSensitive: null,
                ExcludeGenerated: true,
                Limit: 10,
                CandidateId: null,
                Selection: new FuzzySelectionOptions("select", "medium", false)),
            fixture.FuzzyDiscoveryWorkspace.Solution.Projects.ToArray(),
            projectFilters: null,
            CancellationToken.None);

        Assert.Equal("ambiguous", result.Confidence);
        Assert.Null(result.SelectedTarget);
        Assert.Equal("ambiguous-candidates", result.AmbiguityReason);
        Assert.NotNull(result.AmbiguitySummary);
        Assert.True(result.AmbiguitySummary.IsAmbiguous);
        Assert.Equal("ambiguous-candidates", result.AmbiguitySummary.PrimaryReason);
        Assert.Contains("ambiguous-candidates", result.AmbiguitySummary.ReasonCodes);
        Assert.Contains("same-file-duplicates", result.AmbiguitySummary.ReasonCodes);
        Assert.Contains(result.AmbiguitySummary.Groups, group => group.Reason == "same-file-duplicates");
        Assert.Contains("--project", result.AmbiguitySummary.RecommendedAction, StringComparison.Ordinal);
        Assert.Equal(result.Candidates!.Count, result.RecommendedNextActions.Count);
        Assert.All(result.RecommendedNextActions, action =>
        {
            Assert.Equal("target", action.Command);
            Assert.Equal("select-explicit-candidate", action.Reason);
            Assert.Equal("navlyn_target", action.McpTool);
            Assert.Contains(result.Candidates, candidate => candidate.CandidateId == action.CandidateId);
            Assert.Equal(action.CandidateId, action.Arguments!["candidateId"]);
        });
    }
}

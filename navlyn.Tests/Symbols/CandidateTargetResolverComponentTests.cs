using Navlyn.Diagnostics;
using Navlyn.Symbols;
using Navlyn.Tests.TestSupport;

namespace Navlyn.Tests.Symbols;

[Collection(ResolverComponentTestCollection.Name)]
public sealed class CandidateTargetResolverComponentTests(ResolverComponentTestFixture fixture)
{
    [Fact]
    public async Task ResolveAsync_CandidateId_ReturnsSelectedDeclarationPosition()
    {
        FuzzyFindResult find = await new FuzzyDiscoveryResolver().FindAsync(
            fixture.FuzzyDiscoveryWorkspace,
            new FuzzyQueryOptions(
                Query: "EnemyManagerTools",
                AssumeKinds: ["NamedType"],
                Match: "smart",
                CaseSensitive: null,
                ExcludeGenerated: true,
                Limit: null),
            fixture.FuzzyDiscoveryWorkspace.Solution.Projects.ToArray(),
            projectFilters: null,
            CancellationToken.None);

        string candidateId = find.SelectedCandidate!.CandidateId!;
        CandidateTargetResolutionResult result = await new CandidateTargetResolver().ResolveAsync(
            fixture.FuzzyDiscoveryWorkspace.Solution,
            fixture.FuzzyDiscoveryWorkspace.Solution.Projects.ToArray(),
            candidateId,
            excludeGenerated: true,
            CancellationToken.None);

        CandidateTargetResolution resolution = ResolverAssert.NoError(result.Resolution, result.Error);
        Assert.Equal(candidateId, resolution.CandidateId);
        ResolverAssert.PathEndsWith(resolution.File.ToString(), fixture.FuzzyDiscoverySource);
        Assert.Equal(find.SelectedCandidate.Line, resolution.Line);
        Assert.Equal(find.SelectedCandidate.Column, resolution.Column);
        Assert.Equal("FuzzyDiscoveryFixture", resolution.Project!.Name);
    }

    [Fact]
    public async Task ResolveAsync_CandidateId_UsesRecordedCandidateWithoutAdditionalEnrichment()
    {
        DeclarationIndex declarationIndex = await DeclarationIndexProvider.GetOrCreateAsync(
            fixture.FuzzyDiscoveryWorkspace.Solution,
            CancellationToken.None);
        FuzzyFindResult find = await new FuzzyDiscoveryResolver().FindAsync(
            fixture.FuzzyDiscoveryWorkspace,
            new FuzzyQueryOptions(
                Query: "EnemyManagerTools",
                AssumeKinds: ["NamedType"],
                Match: "auto",
                CaseSensitive: null,
                ExcludeGenerated: true,
                Limit: null),
            fixture.FuzzyDiscoveryWorkspace.Solution.Projects.ToArray(),
            projectFilters: null,
            CancellationToken.None);
        int enrichmentCount = declarationIndex.SemanticEnrichmentCount;

        CandidateTargetResolutionResult result = await new CandidateTargetResolver().ResolveAsync(
            fixture.FuzzyDiscoveryWorkspace.Solution,
            fixture.FuzzyDiscoveryWorkspace.Solution.Projects.ToArray(),
            find.SelectedCandidate!.CandidateId!,
            excludeGenerated: true,
            CancellationToken.None);

        ResolverAssert.NoError(result.Resolution, result.Error);
        Assert.Equal(enrichmentCount, declarationIndex.SemanticEnrichmentCount);
    }

    [Fact]
    public async Task ResolveAsync_StaleCandidateIdAfterSourceRename_ReturnsNotFound()
    {
        FuzzyFindResult find = await new FuzzyDiscoveryResolver().FindAsync(
            fixture.FuzzyDiscoveryWorkspace,
            new FuzzyQueryOptions(
                Query: "EnemyManagerTools",
                AssumeKinds: ["NamedType"],
                Match: "smart",
                CaseSensitive: null,
                ExcludeGenerated: true,
                Limit: null),
            fixture.FuzzyDiscoveryWorkspace.Solution.Projects.ToArray(),
            projectFilters: null,
            CancellationToken.None);

        string staleCandidateId = find.SelectedCandidate!.CandidateId!;
        Microsoft.CodeAnalysis.Solution solution = fixture.FuzzyDiscoveryWorkspace.Solution;
        Microsoft.CodeAnalysis.Document document = solution.Projects
            .SelectMany(project => project.Documents)
            .Single(document => string.Equals(
                Path.GetFullPath(document.FilePath!),
                fixture.FuzzyDiscoverySource.FullPath,
                StringComparison.OrdinalIgnoreCase));
        Microsoft.CodeAnalysis.Text.SourceText sourceText = await document.GetTextAsync(CancellationToken.None);
        Microsoft.CodeAnalysis.Text.SourceText renamedText = Microsoft.CodeAnalysis.Text.SourceText.From(
            sourceText.ToString().Replace("EnemyManagerTools", "EnemyManagerToolkit", StringComparison.Ordinal),
            sourceText.Encoding);
        Microsoft.CodeAnalysis.Solution staleSolution = document.WithText(renamedText).Project.Solution;

        CandidateTargetResolutionResult result = await new CandidateTargetResolver().ResolveAsync(
            staleSolution,
            staleSolution.Projects.ToArray(),
            staleCandidateId,
            excludeGenerated: true,
            CancellationToken.None);

        Assert.Null(result.Resolution);
        Assert.NotNull(result.Error);
        Assert.Equal(DiagnosticIds.CandidateIdNotFound, result.Error.DiagnosticId);
        Assert.Equal(ExitCodes.UsageError, result.Error.ExitCode);
        Assert.Contains("current workspace", result.Error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("bad", DiagnosticIds.InvalidCandidateId)]
    [InlineData("sym:v1:00000000000000000000000000000000", DiagnosticIds.CandidateIdNotFound)]
    public async Task ResolveAsync_InvalidOrMissingCandidateId_ReturnsCandidateDiagnostic(
        string candidateId,
        int expectedDiagnosticId)
    {
        CandidateTargetResolutionResult result = await new CandidateTargetResolver().ResolveAsync(
            fixture.FuzzyDiscoveryWorkspace.Solution,
            fixture.FuzzyDiscoveryWorkspace.Solution.Projects.ToArray(),
            candidateId,
            excludeGenerated: true,
            CancellationToken.None);

        Assert.Null(result.Resolution);
        Assert.NotNull(result.Error);
        Assert.Equal(expectedDiagnosticId, result.Error.DiagnosticId);
        Assert.Equal(ExitCodes.UsageError, result.Error.ExitCode);
    }
}

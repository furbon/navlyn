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

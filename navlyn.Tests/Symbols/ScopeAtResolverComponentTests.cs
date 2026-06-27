using Navlyn.Symbols;
using Navlyn.Tests.TestSupport;

namespace Navlyn.Tests.Symbols;

[Collection(ResolverComponentTestCollection.Name)]
public sealed class ScopeAtResolverComponentTests(ResolverComponentTestFixture fixture)
{
    [Fact]
    public async Task ResolveAsync_LocalFunction_ReturnsOuterToInnerScopeStack()
    {
        SourcePosition position = fixture.SymbolNavigationSource.Position(
            "return value;",
            "value");

        ScopeAtResolutionResult result = await new ScopeAtResolver().ResolveAsync(
            fixture.SymbolNavigationWorkspace.Solution,
            fixture.SymbolNavigationSource.File,
            position.Line,
            position.Column,
            project: null,
            excludeGenerated: true,
            CancellationToken.None);

        ScopeAtResolution resolution = ResolverAssert.NoError(result.Resolution, result.Error);
        Assert.Equal("BuildLocal", resolution.ContainingSymbol!.Name);
        Assert.Collection(
            resolution.Scopes.Select(scope => scope.Kind),
            kind => Assert.Equal("Namespace", kind),
            kind => Assert.Equal("Type", kind),
            kind => Assert.Equal("Member", kind),
            kind => Assert.Equal("LocalFunction", kind));
        Assert.Equal("BuildLocal", resolution.Scopes.Last().Symbol!.Name);
        Assert.Equal("SymbolNavigationFixture", resolution.ProjectContext.Name);
    }
}

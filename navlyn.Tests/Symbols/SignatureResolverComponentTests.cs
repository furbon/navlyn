using Navlyn.Symbols;
using Navlyn.Tests.TestSupport;

namespace Navlyn.Tests.Symbols;

[Collection(ResolverComponentTestCollection.Name)]
public sealed class SignatureResolverComponentTests(ResolverComponentTestFixture fixture)
{
    [Fact]
    public async Task ResolveAsync_GenericMethod_ReturnsApiShape()
    {
        SourcePosition position = fixture.SymbolNavigationSource.Position(
            "public T Echo<T>(T value)",
            "Echo");

        SignatureResolutionResult result = await new SignatureResolver().ResolveAsync(
            fixture.SymbolNavigationWorkspace.Solution,
            fixture.SymbolNavigationSource.File,
            position.Line,
            position.Column,
            project: null,
            excludeGenerated: true,
            CancellationToken.None);

        SignatureResolution resolution = ResolverAssert.NoError(result.Resolution, result.Error);
        Assert.Equal("Echo", resolution.Symbol.Name);
        Assert.Equal("Public", resolution.ApiShape.Accessibility);
        Assert.Equal("T", Assert.Single(resolution.ApiShape.TypeParameters!));
        SignatureParameterShape parameter = Assert.Single(resolution.ApiShape.Parameters!);
        Assert.Equal("value", parameter.Name);
        Assert.Equal("T", resolution.ApiShape.ReturnType!.Name);
    }
}

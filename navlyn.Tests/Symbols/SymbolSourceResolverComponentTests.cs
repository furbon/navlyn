using Navlyn.Symbols;
using Navlyn.Tests.TestSupport;

namespace Navlyn.Tests.Symbols;

[Collection(ResolverComponentTestCollection.Name)]
public sealed class SymbolSourceResolverComponentTests(ResolverComponentTestFixture fixture)
{
    [Fact]
    public async Task ResolveAsync_DeclarationView_ReturnsBoundedSourceSlice()
    {
        SourcePosition position = fixture.SymbolNavigationSource.Position(
            "public interface IWidgetFormatter",
            "IWidgetFormatter");

        SymbolSourceResolutionResult result = await new SymbolSourceResolver().ResolveAsync(
            fixture.SymbolNavigationWorkspace.Solution,
            fixture.SymbolNavigationSource.File,
            position.Line,
            position.Column,
            project: null,
            excludeGenerated: true,
            new SymbolSourceOptions("declaration", MaxLines: 2, BudgetTokens: 4000),
            CancellationToken.None);

        SymbolSourceResolution resolution = ResolverAssert.NoError(result.Resolution, result.Error);
        SymbolSourceSlice slice = Assert.Single(resolution.Slices);
        Assert.Equal("declaration", slice.TextKind);
        Assert.True(slice.Truncated);
        Assert.Equal(2, slice.Lines.Count);
        Assert.Contains("IWidgetFormatter", slice.Lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveAsync_SignatureView_ReturnsSignatureText()
    {
        SourcePosition position = fixture.SymbolNavigationSource.Position(
            "public string FormatWidget(Widget widget)",
            "FormatWidget");

        SymbolSourceResolutionResult result = await new SymbolSourceResolver().ResolveAsync(
            fixture.SymbolNavigationWorkspace.Solution,
            fixture.SymbolNavigationSource.File,
            position.Line,
            position.Column,
            project: null,
            excludeGenerated: true,
            new SymbolSourceOptions("signature", MaxLines: 5, BudgetTokens: 4000),
            CancellationToken.None);

        SymbolSourceResolution resolution = ResolverAssert.NoError(result.Resolution, result.Error);
        SymbolSourceSlice slice = Assert.Single(resolution.Slices);
        Assert.Equal("signature", slice.TextKind);
        Assert.Contains("FormatWidget", slice.Lines[0], StringComparison.Ordinal);
    }
}

using Navlyn.Symbols;
using Navlyn.Tests.TestSupport;

namespace Navlyn.Tests.Symbols;

[Collection(ResolverComponentTestCollection.Name)]
public sealed class SourcePositionResolverComponentTests(ResolverComponentTestFixture fixture)
{
    [Fact]
    public async Task DefinitionResolver_OverloadedInvocation_ReturnsSelectedSourceDefinition()
    {
        SourcePosition query = fixture.SymbolNavigationSource.Position(
            "string formatted = widget.Format(3);",
            "Format");
        SourcePosition expectedDefinition = fixture.SymbolNavigationSource.Position(
            "public string Format(int count)",
            "Format");

        DefinitionResolutionResult result = await new DefinitionResolver().ResolveAsync(
            fixture.SymbolNavigationWorkspace.Solution,
            fixture.SymbolNavigationSource.File,
            query.Line,
            query.Column,
            project: null,
            excludeGenerated: true,
            includeMetadata: false,
            CancellationToken.None);

        DefinitionResolution resolution = ResolverAssert.NoError(result.Resolution, result.Error);
        Assert.Equal("Format", resolution.Symbol.Name);
        Assert.Equal("Method", resolution.Symbol.Kind);
        Assert.Equal("SymbolNavigationFixture.Widget", resolution.Symbol.Container);
        Assert.Equal("SymbolNavigationFixture.Widget.Format(int)", resolution.Symbol.Facts.DisplayName);

        SymbolSourceLocation definition = Assert.Single(resolution.Definitions);
        ResolverAssert.PathEndsWith(definition.Path, fixture.SymbolNavigationSource);
        ResolverAssert.Location(
            expectedDefinition,
            definition.Line,
            definition.Column,
            definition.EndLine,
            definition.EndColumn);
    }

    [Fact]
    public async Task ReferencesResolver_StaticFactory_ReturnsContainingSymbolForUsage()
    {
        SourcePosition query = fixture.SymbolNavigationSource.Position(
            "public static Widget CreateDefault()",
            "CreateDefault");
        SourcePosition expectedReference = fixture.SymbolNavigationSource.Position(
            "Widget widget = Widget.CreateDefault();",
            "CreateDefault");

        ReferencesResolutionResult result = await new ReferencesResolver().ResolveAsync(
            fixture.SymbolNavigationWorkspace.Solution,
            fixture.SymbolNavigationSource.File,
            query.Line,
            query.Column,
            project: null,
            excludeGenerated: true,
            CancellationToken.None);

        ReferencesResolution resolution = ResolverAssert.NoError(result.Resolution, result.Error);
        Assert.Equal("CreateDefault", resolution.Symbol.Name);
        Assert.Equal("Method", resolution.Symbol.Kind);

        SymbolReferenceLocation reference = resolution.References.Single(
            item => item.Line == expectedReference.Line && item.Column == expectedReference.Column);
        ResolverAssert.PathEndsWith(reference.Path, fixture.SymbolNavigationSource);
        Assert.NotNull(reference.ContainingSymbol);
        Assert.Equal("Run", reference.ContainingSymbol.Name);
        Assert.Equal("Method", reference.ContainingSymbol.Kind);
        Assert.Equal("SymbolNavigationFixture.Runner", reference.ContainingSymbol.Container);
    }

    [Fact]
    public async Task SymbolAtResolver_StaticImport_ReturnsImportedMethodDeclaration()
    {
        SourcePosition query = fixture.SymbolNavigationSource.Position(
            "string staticFormatted = Label(widget);",
            "Label");
        SourcePosition expectedDefinition = fixture.SymbolNavigationSource.Position(
            "public static string Label(Widget widget)",
            "Label");

        SymbolAtResolutionResult result = await new SymbolAtResolver().ResolveAsync(
            fixture.SymbolNavigationWorkspace.Solution,
            fixture.SymbolNavigationSource.File,
            query.Line,
            query.Column,
            project: null,
            excludeGenerated: true,
            CancellationToken.None);

        SymbolAtResolution resolution = ResolverAssert.NoError(result.Resolution, result.Error);
        Assert.Equal("Label", resolution.Symbol.Name);
        Assert.Equal("Method", resolution.Symbol.Kind);
        Assert.Equal("SymbolNavigationFixture.WidgetText", resolution.Symbol.Container);
        ResolverAssert.PathEndsWith(resolution.Symbol.Path, fixture.SymbolNavigationSource);
        ResolverAssert.Location(
            expectedDefinition,
            resolution.Symbol.Line!.Value,
            resolution.Symbol.Column!.Value,
            resolution.Symbol.EndLine!.Value,
            resolution.Symbol.EndColumn!.Value);
    }

    [Fact]
    public async Task SymbolsInResolver_SourceLine_ReturnsIdentifiersInSourceOrder()
    {
        int line = fixture.SymbolNavigationSource.Line("Widget widget = Widget.CreateDefault();");

        SymbolsInResolutionResult result = await new SymbolsInResolver().ResolveAsync(
            fixture.SymbolNavigationWorkspace.Solution,
            fixture.SymbolNavigationSource.File,
            line,
            startColumn: null,
            endColumn: null,
            project: null,
            excludeGenerated: true,
            CancellationToken.None);

        SymbolsInResolution resolution = ResolverAssert.NoError(result.Resolution, result.Error);
        Assert.Equal(line, resolution.Line);
        Assert.Equal(1, resolution.StartColumn);
        Assert.Equal(fixture.SymbolNavigationSource.EndColumn(line), resolution.EndColumn);

        Assert.Collection(
            resolution.Symbols,
            symbol =>
            {
                Assert.Equal("Widget", symbol.Name);
                Assert.Equal("NamedType", symbol.Kind);
            },
            symbol =>
            {
                Assert.Equal("widget", symbol.Name);
                Assert.Equal("Local", symbol.Kind);
            },
            symbol =>
            {
                Assert.Equal("Widget", symbol.Name);
                Assert.Equal("NamedType", symbol.Kind);
            },
            symbol =>
            {
                Assert.Equal("CreateDefault", symbol.Name);
                Assert.Equal("Method", symbol.Kind);
            });
    }
}

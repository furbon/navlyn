using Navlyn.Symbols;
using Navlyn.Tests.TestSupport;

namespace Navlyn.Tests.Symbols;

[Collection(ResolverComponentTestCollection.Name)]
public sealed class SymbolInfoResolverComponentTests(ResolverComponentTestFixture fixture)
{
    [Fact]
    public async Task ResolveAsync_OverloadedInvocation_ReturnsTargetAndArgumentBinding()
    {
        SourcePosition query = fixture.SymbolNavigationSource.Position(
            "string formatted = widget.Format(3);",
            "Format");

        SymbolInfoResolutionResult result = await new SymbolInfoResolver().ResolveAsync(
            fixture.SymbolNavigationWorkspace.Solution,
            fixture.SymbolNavigationSource.File,
            query.Line,
            query.Column,
            project: null,
            excludeGenerated: true,
            CancellationToken.None);

        SymbolInfoResolution resolution = ResolverAssert.NoError(result.Resolution, result.Error);
        Assert.Equal("Format", resolution.Symbol.Name);
        Assert.Equal("SymbolNavigationFixture.Widget.Format(int)", resolution.Invocation!.Target!.DisplayName);
        Assert.Equal("string", resolution.Invocation.Target.ReturnType!.Name);

        SymbolArgumentInfo argument = Assert.Single(resolution.Invocation.Arguments);
        Assert.Equal("Explicit", argument.ArgumentKind);
        Assert.Equal("count", argument.Parameter!.Name);
        Assert.Equal(0, argument.Parameter.Ordinal);
        Assert.Equal("int", argument.Parameter.Type!.Name);
        Assert.False(argument.IsImplicit);
    }

    [Fact]
    public async Task ResolveAsync_TargetTypedNew_ReturnsInferredConstructedType()
    {
        SourcePosition query = fixture.SymbolNavigationSource.Position(
            "Widget targetTypedWidget = new(\"target\");",
            "new");

        SymbolInfoResolutionResult result = await new SymbolInfoResolver().ResolveAsync(
            fixture.SymbolNavigationWorkspace.Solution,
            fixture.SymbolNavigationSource.File,
            query.Line,
            query.Column,
            project: null,
            excludeGenerated: true,
            CancellationToken.None);

        SymbolInfoResolution resolution = ResolverAssert.NoError(result.Resolution, result.Error);
        Assert.Equal("ObjectCreation", resolution.Invocation!.Kind);
        Assert.Equal("SymbolNavigationFixture.Widget", resolution.Invocation.ConstructedType!.Name);
        Assert.Equal("SymbolNavigationFixture.Widget.Widget(string)", resolution.Invocation.Target!.DisplayName);
    }

    [Fact]
    public async Task ResolveAsync_LambdaBody_ReturnsLambdaTargetAndReturnTypes()
    {
        SourcePosition query = fixture.SymbolNavigationSource.Position(
            "Func<string, string> normalize = input => input.Trim();",
            "input",
            occurrence: 2);

        SymbolInfoResolutionResult result = await new SymbolInfoResolver().ResolveAsync(
            fixture.SymbolNavigationWorkspace.Solution,
            fixture.SymbolNavigationSource.File,
            query.Line,
            query.Column,
            project: null,
            excludeGenerated: true,
            CancellationToken.None);

        SymbolInfoResolution resolution = ResolverAssert.NoError(result.Resolution, result.Error);
        Assert.Equal("input", resolution.Symbol.Name);
        Assert.Equal("Parameter", resolution.Symbol.Kind);
        Assert.Equal("System.Func<string, string>", resolution.Lambda!.TargetType!.Name);
        Assert.Equal("string", resolution.Lambda.ReturnType!.Name);
        Assert.Equal("input", Assert.Single(resolution.Lambda.Parameters!).Name);
    }
}

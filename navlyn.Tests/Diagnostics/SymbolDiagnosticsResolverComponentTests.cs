using Navlyn.Diagnostics;
using Navlyn.Tests.TestSupport;

namespace Navlyn.Tests.Diagnostics;

[Collection(ResolverComponentTestCollection.Name)]
public sealed class SymbolDiagnosticsResolverComponentTests(ResolverComponentTestFixture fixture)
{
    [Fact]
    public async Task ResolveAsync_MethodWithMissingType_ReturnsIntersectingDiagnostic()
    {
        SourcePosition position = fixture.DiagnosticSource.Position(
            "public MissingType Create()",
            "Create");

        SymbolDiagnosticsResolutionResult result = await new SymbolDiagnosticsResolver().ResolveAsync(
            fixture.DiagnosticWorkspace.Solution,
            fixture.DiagnosticSource.File,
            position.Line,
            position.Column,
            project: null,
            excludeGenerated: true,
            severities: [],
            ids: [],
            limit: 10,
            CancellationToken.None);

        SymbolDiagnosticsResolution resolution = ResolverAssert.NoError(result.Resolution, result.Error);
        SymbolDiagnosticItem diagnostic = Assert.Single(resolution.Diagnostics);
        Assert.Equal("CS0246", diagnostic.Id);
        Assert.Contains("diagnostic-intersects-symbol-span", diagnostic.ReasonCodes);
    }
}

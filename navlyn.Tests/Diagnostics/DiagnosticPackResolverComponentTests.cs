using Navlyn.Diagnostics;
using Navlyn.Tests.TestSupport;

namespace Navlyn.Tests.Diagnostics;

[Collection(ResolverComponentTestCollection.Name)]
public sealed class DiagnosticPackResolverComponentTests(ResolverComponentTestFixture fixture)
{
    [Fact]
    public async Task ResolveAsync_IdMode_ReturnsDiagnosticsAndRepresentativeContext()
    {
        DiagnosticPackResolution result = await new DiagnosticPackResolver().ResolveAsync(
            fixture.DiagnosticWorkspace,
            fixture.DiagnosticWorkspace.Solution.Projects.ToArray(),
            new DiagnosticPackInput("id", "CS0246", File: null, Line: null, Column: null),
            excludeGenerated: true,
            severities: [],
            limit: 5,
            budgetTokens: 1000,
            CancellationToken.None);

        Assert.Equal(2, result.TotalDiagnostics);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal("CS0246", diagnostic.Id));
        Assert.NotNull(result.Context);
        Assert.NotNull(result.Context!.Scope);
        Assert.NotNull(result.Context.Signature);
        Assert.Contains(result.NextActions, action => action.Command == "symbol-source");
    }
}

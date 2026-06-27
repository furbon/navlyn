using Navlyn.Diagnostics;
using Navlyn.Tests.TestSupport;

namespace Navlyn.Tests.Diagnostics;

[Collection(ResolverComponentTestCollection.Name)]
public sealed class WorkspaceDiagnosticsResolverComponentTests(ResolverComponentTestFixture fixture)
{
    [Fact]
    public async Task ResolveAsync_IncludesGeneratedDiagnosticsByDefault()
    {
        WorkspaceDiagnosticsResolution resolution = await new WorkspaceDiagnosticsResolver().ResolveAsync(
            fixture.DiagnosticWorkspace.Solution.Projects.ToArray(),
            excludeGenerated: false,
            CancellationToken.None);

        IReadOnlyList<WorkspaceDiagnosticResult> missingTypeDiagnostics =
            resolution.Diagnostics.Where(diagnostic => diagnostic.Id == "CS0246").ToArray();

        Assert.Equal(4, missingTypeDiagnostics.Count);
        Assert.Contains(missingTypeDiagnostics, diagnostic => PathMatches(diagnostic.Path, fixture.DiagnosticSource));
        Assert.Contains(missingTypeDiagnostics, diagnostic => PathMatches(diagnostic.Path, fixture.GeneratedDiagnosticSource));
        Assert.All(
            missingTypeDiagnostics,
            diagnostic =>
            {
                Assert.Equal("DiagnosticFixture", diagnostic.Project.Name);
                Assert.Equal("Error", diagnostic.Severity);
                Assert.True(diagnostic.EndColumn > diagnostic.Column);
            });
    }

    [Fact]
    public async Task ResolveAsync_ExcludeGenerated_RemovesGeneratedDiagnostics()
    {
        WorkspaceDiagnosticsResolution resolution = await new WorkspaceDiagnosticsResolver().ResolveAsync(
            fixture.DiagnosticWorkspace.Solution.Projects.ToArray(),
            excludeGenerated: true,
            CancellationToken.None);

        IReadOnlyList<WorkspaceDiagnosticResult> missingTypeDiagnostics =
            resolution.Diagnostics.Where(diagnostic => diagnostic.Id == "CS0246").ToArray();

        Assert.Equal(2, missingTypeDiagnostics.Count);
        Assert.All(missingTypeDiagnostics, diagnostic => ResolverAssert.PathEndsWith(diagnostic.Path, fixture.DiagnosticSource));
        Assert.DoesNotContain(missingTypeDiagnostics, diagnostic => PathMatches(diagnostic.Path, fixture.GeneratedDiagnosticSource));
    }

    private static bool PathMatches(string? actual, SourceFixtureFile expected)
    {
        return !string.IsNullOrWhiteSpace(actual) &&
            actual.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .EndsWith(
                    expected.RelativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
    }
}

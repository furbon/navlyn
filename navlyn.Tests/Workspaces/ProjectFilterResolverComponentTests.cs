using Navlyn.Diagnostics;
using Navlyn.Tests.TestSupport;
using Navlyn.Workspaces;

namespace Navlyn.Tests.Workspaces;

[Collection(ResolverComponentTestCollection.Name)]
public sealed class ProjectFilterResolverComponentTests(ResolverComponentTestFixture fixture)
{
    [Fact]
    public void ResolveSingle_ProjectDirectorySuggestsLoadedProjectFile()
    {
        ProjectFilterResolutionResult result = new ProjectFilterResolver().ResolveSingle(
            fixture.FuzzyDiscoveryWorkspace.Solution,
            "tests/fixtures/FuzzyDiscoveryFixture");

        Assert.Equal(DiagnosticIds.UnknownProjectFilter, result.Error?.DiagnosticId);
        Assert.Contains(
            "tests/fixtures/FuzzyDiscoveryFixture/FuzzyDiscoveryFixture.csproj",
            result.Error!.Message,
            StringComparison.Ordinal);
    }
}

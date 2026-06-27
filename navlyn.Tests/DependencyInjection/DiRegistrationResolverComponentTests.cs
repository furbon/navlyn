using Navlyn.DependencyInjection;
using Navlyn.Tests.TestSupport;
using Navlyn.Workspaces;

namespace Navlyn.Tests.DependencyInjection;

[Collection(ResolverComponentTestCollection.Name)]
public sealed class DiRegistrationResolverComponentTests(ResolverComponentTestFixture fixture)
{
    [Fact]
    public async Task Resolve_Fixture_ReturnsRegistrationsDependenciesAndRisks()
    {
        string workspacePath = Path.Combine(fixture.RepoRoot, "tests", "fixtures", "DependencyInjectionFixture", "DependencyInjectionFixture.csproj");
        WorkspaceLoadResult loadResult = await new WorkspaceLoader().LoadAsync(new FileInfo(workspacePath), CancellationToken.None);
        Assert.Null(loadResult.Error);

        using LoadedWorkspace workspace = loadResult.Workspace!;
        DiGraphResolution result = await new DiRegistrationResolver().ResolveAsync(
            workspace,
            workspace.Solution.Projects.ToArray(),
            new DiGraphOptions(
                RegistrationLimit: 50,
                DependencyLimit: 100,
                RiskLimit: 50,
                IncludeOptions: true,
                IncludeHostedServices: true,
                IncludeRisks: true,
                IncludeSnippets: false,
                SnippetLines: 1,
                ExcludeGenerated: false),
            CancellationToken.None);

        Assert.Contains(result.Registrations, item =>
            item.Lifetime == "scoped" &&
            item.ServiceType?.Name == "IWidgetStore" &&
            item.ImplementationType?.Name == "SqlWidgetStore");
        Assert.Contains(result.Registrations, item => item.RegistrationKind == "hostedService" && item.ImplementationType?.Name == "Worker");
        Assert.Contains(result.Registrations, item => item.RegistrationKind == "options" && item.ServiceType?.Name == "WidgetOptions");
        Assert.Contains(result.Dependencies, item => item.ImplementationType.Name == "WidgetService" && item.DependencyType.Name == "IWidgetStore");
        Assert.Contains(result.Risks, item => item.RiskKind == "captive-dependency");
    }
}

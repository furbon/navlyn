using Microsoft.CodeAnalysis;
using Navlyn.ApplicationDomains;
using Navlyn.Tests.TestSupport;
using Navlyn.Workspaces;

namespace Navlyn.Tests.ApplicationDomains;

[Collection(ResolverComponentTestCollection.Name)]
public sealed class ApplicationDomainResolverComponentTests(ResolverComponentTestFixture fixture)
{
    [Fact]
    public async Task ResolveRouteMap_FindsControllerMinimalApiAndAuthFacts()
    {
        using LoadedWorkspace workspace = await LoadApplicationDomainWorkspaceAsync();

        RouteMapResult result = await new ApplicationDomainResolver().ResolveRouteMapAsync(
            workspace,
            workspace.Solution.Projects.ToArray(),
            projectFilters: null,
            new RouteMapOptions(
                RouteLimit: 20,
                EvidenceLimit: 5,
                RouteFilters: [],
                EndpointKinds: [],
                AuthFilter: "any",
                IncludeSnippets: false,
                SnippetLines: 1,
                ExcludeGenerated: false),
            CancellationToken.None);

        Assert.Contains(result.Routes.Items, item =>
            item.EndpointKind == "controller-action" &&
            item.Handler.Name == "Get" &&
            item.NormalizedRoutePattern == "/orders/{id}" &&
            item.Auth.Kind == "required");
        Assert.Contains(result.Routes.Items, item =>
            item.EndpointKind == "controller-action" &&
            item.Handler.Name == "Create" &&
            item.Auth.Kind == "anonymous");
        Assert.Contains(result.Routes.Items, item =>
            item.EndpointKind == "minimal-api" &&
            item.NormalizedRoutePattern == "/orders" &&
            item.Auth.Kind == "required");
    }

    [Fact]
    public async Task ResolveOptionsGraph_FindsBindingsConsumersAndValidations()
    {
        using LoadedWorkspace workspace = await LoadApplicationDomainWorkspaceAsync();

        OptionsGraphResult result = await new ApplicationDomainResolver().ResolveOptionsGraphAsync(
            workspace,
            workspace.Solution.Projects.ToArray(),
            projectFilters: null,
            new OptionsGraphOptions(
                Query: "PaymentOptions",
                OptionLimit: 20,
                ConsumerLimit: 20,
                BindingLimit: 20,
                EvidenceLimit: 5,
                IncludeSnippets: false,
                SnippetLines: 1,
                ExcludeGenerated: false),
            CancellationToken.None);

        Assert.Contains(result.Options.Items, item => item.Type.Name == "PaymentOptions");
        Assert.Contains(result.Bindings.Items, item => item.OptionType.Name == "PaymentOptions" && item.ConfigurationKey == "Payments");
        Assert.Contains(result.Consumers.Items, item => item.ConsumerType.Name == "PaymentService");
        Assert.Contains(result.Validations.Items, item => item.OptionType.Name == "PaymentOptions" && item.ValidationKind == "ValidateOnStart");
    }

    [Fact]
    public async Task ResolveMessageFlow_FindsMediatRHandlersAndCallSites()
    {
        using LoadedWorkspace workspace = await LoadApplicationDomainWorkspaceAsync();
        ApplicationDomainSymbol subject = await FindTypeAsync(workspace, "CreateOrderCommand");

        MessageFlowResult result = await new ApplicationDomainResolver().ResolveMessageFlowAsync(
            workspace,
            workspace.Solution.Projects.ToArray(),
            new ApplicationDomainSelectionInput("query", "CreateOrderCommand", CandidateId: null, File: null, Line: null, Column: null),
            selection: null,
            subject,
            new MessageFlowOptions(
                HandlerLimit: 20,
                CallSiteLimit: 20,
                EvidenceLimit: 5,
                IncludeSnippets: false,
                SnippetLines: 1,
                ExcludeGenerated: false),
            candidateLimit: 20,
            commandName: "message-flow",
            CancellationToken.None);

        Assert.Contains(result.Handlers.Items, item => item.HandlerType.Name == "CreateOrderHandler");
        Assert.Contains(result.CallSites.Items, item => item.CallKind == "send" && item.MessageType.Name == "CreateOrderCommand");
    }

    [Fact]
    public async Task ResolveEfModel_FindsDbContextDbSetConfigurationAndQuerySites()
    {
        using LoadedWorkspace workspace = await LoadApplicationDomainWorkspaceAsync();

        EfModelResult result = await new ApplicationDomainResolver().ResolveEfModelAsync(
            workspace,
            workspace.Solution.Projects.ToArray(),
            projectFilters: null,
            new EfModelOptions(
                EntityQuery: null,
                DbContextQuery: null,
                EntityLimit: 20,
                QuerySiteLimit: 20,
                EvidenceLimit: 5,
                IncludeSnippets: false,
                SnippetLines: 1,
                ExcludeGenerated: false),
            CancellationToken.None);

        Assert.Contains(result.DbContexts.Items, item => item.Type.Name == "OrdersDbContext");
        Assert.Contains(result.DbSets.Items, item => item.EntityType.Name == "Order" && item.PropertyName == "Orders");
        Assert.Contains(result.Configurations.Items, item => item.ConfigurationType.Name == "OrderConfiguration");
        Assert.Contains(result.QuerySites.Items, item => item.EntityType.Name == "Order" && item.QueryKind == "FirstOrDefault");
    }

    [Fact]
    public async Task ResolvePackageUsage_FindsPackageReferenceAndNamespaceUsage()
    {
        using LoadedWorkspace workspace = await LoadApplicationDomainWorkspaceAsync();

        PackageUsageResult result = await new ApplicationDomainResolver().ResolvePackageUsageAsync(
            workspace,
            workspace.Solution.Projects.ToArray(),
            projectFilters: null,
            new PackageUsageOptions(
                Package: "Microsoft.Extensions.Options",
                NamespaceHints: ["Microsoft.Extensions.Options"],
                UsageLimit: 20,
                ReferenceLimit: 20,
                IncludeTests: true,
                ExcludeGenerated: false),
            CancellationToken.None);

        Assert.Contains(result.PackageReferences.Items, item => item.Name == "Microsoft.Extensions.Options");
        Assert.Contains(result.Usages.Items, item => item.UsageKind == "using-directive" && item.NamespaceOrAssembly == "Microsoft.Extensions.Options");
    }

    private async Task<LoadedWorkspace> LoadApplicationDomainWorkspaceAsync()
    {
        string workspacePath = Path.Combine(
            fixture.RepoRoot,
            "tests",
            "fixtures",
            "ApplicationDomainFixture",
            "ApplicationDomainFixture.csproj");
        WorkspaceLoadResult result = await new WorkspaceLoader().LoadAsync(new FileInfo(workspacePath), CancellationToken.None);
        Assert.Null(result.Error);
        return Assert.IsType<LoadedWorkspace>(result.Workspace);
    }

    private static async Task<ApplicationDomainSymbol> FindTypeAsync(LoadedWorkspace workspace, string name)
    {
        foreach (Microsoft.CodeAnalysis.Project project in workspace.Solution.Projects)
        {
            foreach (Microsoft.CodeAnalysis.Document document in project.Documents)
            {
                Microsoft.CodeAnalysis.SyntaxNode? root = await document.GetSyntaxRootAsync(CancellationToken.None);
                Microsoft.CodeAnalysis.SemanticModel? semanticModel = await document.GetSemanticModelAsync(CancellationToken.None);
                if (root is null || semanticModel is null)
                {
                    continue;
                }

                foreach (Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax type in root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax>())
                {
                    if (type.Identifier.ValueText == name && semanticModel.GetDeclaredSymbol(type) is { } symbol)
                    {
                        return ApplicationDomainResolver.CreateSymbol(symbol, project.Name, excludeGenerated: false);
                    }
                }
            }
        }

        throw new InvalidOperationException($"Could not find type {name}.");
    }
}

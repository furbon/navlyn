using Microsoft.CodeAnalysis;
using Navlyn.ApplicationDomains;
using Navlyn.DependencyInjection;
using Navlyn.Entrypoints;
using Navlyn.PublicApi;
using Navlyn.ReviewPacks;
using Navlyn.Symbols;
using Navlyn.Testing;
using Navlyn.Tests.TestSupport;
using Navlyn.Workspaces;

namespace Navlyn.Tests.VisualBasic;

[Collection(ResolverComponentTestCollection.Name)]
public sealed class VisualBasicSupportComponentTests(ResolverComponentTestFixture fixture)
{
    [Fact]
    public async Task SourceNavigation_VisualBasicProject_ResolvesDefinitionsReferencesAndSource()
    {
        using LoadedWorkspace workspace = await LoadWorkspaceAsync();
        SourceFixtureFile source = Source();
        SourcePosition call = source.Position("Return service.Format(3)", "Format");
        SourcePosition definition = source.Position("Public Function Format(count As Integer) As String", "Format");

        DefinitionResolutionResult definitionResult = await new DefinitionResolver().ResolveAsync(
            workspace.Solution,
            source.File,
            call.Line,
            call.Column,
            project: null,
            excludeGenerated: true,
            includeMetadata: false,
            CancellationToken.None);
        DefinitionResolution definitionResolution = ResolverAssert.NoError(definitionResult.Resolution, definitionResult.Error);

        Assert.Equal("Format", definitionResolution.Symbol.Name);
        ResolverAssert.PathEndsWith(Assert.Single(definitionResolution.Definitions).Path, source);

        ReferencesResolutionResult referencesResult = await new ReferencesResolver().ResolveAsync(
            workspace.Solution,
            source.File,
            definition.Line,
            definition.Column,
            project: null,
            excludeGenerated: true,
            CancellationToken.None);
        ReferencesResolution references = ResolverAssert.NoError(referencesResult.Resolution, referencesResult.Error);

        Assert.Contains(references.References, reference => reference.Line == call.Line && reference.Column == call.Column);

        SymbolSourceResolutionResult symbolSourceResult = await new SymbolSourceResolver().ResolveAsync(
            workspace.Solution,
            source.File,
            definition.Line,
            definition.Column,
            project: null,
            excludeGenerated: true,
            new SymbolSourceOptions("signature", MaxLines: 5, BudgetTokens: 4000),
            CancellationToken.None);
        SymbolSourceResolution symbolSource = ResolverAssert.NoError(symbolSourceResult.Resolution, symbolSourceResult.Error);

        SymbolSourceSlice slice = Assert.Single(symbolSource.Slices);
        Assert.Contains("Format", string.Join("\n", slice.Lines), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiAndTestDiscovery_VisualBasicProject_ReturnSemanticFacts()
    {
        using LoadedWorkspace workspace = await LoadWorkspaceAsync();
        Project[] projects = workspace.Solution.Projects.ToArray();

        DiGraphResolution di = await new DiRegistrationResolver().ResolveAsync(
            workspace,
            projects,
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

        Assert.Contains(di.Registrations, item => item.Lifetime == "scoped" && item.ServiceType?.Name == "IWidgetStore" && item.ImplementationType?.Name == "SqlWidgetStore");
        Assert.Contains(di.Registrations, item => item.RegistrationKind == "hostedService" && item.ImplementationType?.Name == "Worker");
        Assert.Contains(di.Registrations, item => item.RegistrationKind == "options" && item.ServiceType?.Name == "PaymentOptions");
        Assert.Contains(di.Dependencies, item => item.ImplementationType.Name == "RootSingleton" && item.DependencyType.Name == "ScopedThing");

        TestDiscoveryResult tests = await new TestDiscoveryResolver().DiscoverAsync(
            projects,
            explicitTestProjects: projects,
            excludeGenerated: false,
            includeSnippets: false,
            snippetLines: 1,
            CancellationToken.None);

        Assert.Contains(tests.Candidates, candidate => candidate.Name == "GetWidget_ReturnsWidget" && candidate.Framework == "xunit");
    }

    [Fact]
    public async Task EntrypointsAndApplicationDomains_VisualBasicProject_ReturnFrameworkFacts()
    {
        using LoadedWorkspace workspace = await LoadWorkspaceAsync();
        Project[] projects = workspace.Solution.Projects.ToArray();

        FrameworkEntrypointsSection entrypoints = await new FrameworkEntrypointDiscoveryResolver().DiscoverSectionAsync(
            workspace,
            projects,
            new FrameworkEntrypointOptions(
                Frameworks: ["aspnetcore", "worker"],
                Limit: 30,
                EvidenceLimit: 5,
                IncludeSnippets: false,
                SnippetLines: 1,
                ExcludeGenerated: false),
            CancellationToken.None);

        Assert.Contains(entrypoints.Items, item => item.EntrypointKind == "aspnetcore-controller-action" && item.Name == "GetWidget");
        Assert.Contains(entrypoints.Items, item => item.EntrypointKind == "aspnetcore-minimal-api-handler" && item.Name == "Handle");
        Assert.Contains(entrypoints.Items, item => item.EntrypointKind == "worker-backgroundservice-execute" && item.Name == "ExecuteAsync");

        RouteMapResult routes = await new ApplicationDomainResolver().ResolveRouteMapAsync(
            workspace,
            projects,
            projectFilters: null,
            new RouteMapOptions(
                RouteLimit: 30,
                EvidenceLimit: 5,
                RouteFilters: [],
                EndpointKinds: [],
                AuthFilter: "any",
                IncludeSnippets: false,
                SnippetLines: 1,
                ExcludeGenerated: false),
            CancellationToken.None);

        Assert.Contains(routes.Routes.Items, item => item.EndpointKind == "controller-action" && item.Handler.Name == "GetWidget" && item.NormalizedRoutePattern == "/widgets/{id}");
        Assert.Contains(routes.Routes.Items, item => item.EndpointKind == "minimal-api" && item.NormalizedRoutePattern == "/widgets" && item.Auth.Kind == "required");

        OptionsGraphResult options = await new ApplicationDomainResolver().ResolveOptionsGraphAsync(
            workspace,
            projects,
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

        Assert.Contains(options.Options.Items, item => item.Type.Name == "PaymentOptions");
        Assert.Contains(options.Bindings.Items, item => item.OptionType.Name == "PaymentOptions" && item.ConfigurationKey == "Payments");
        Assert.Contains(options.Consumers.Items, item => item.ConsumerType.Name == "PaymentService");
        Assert.Contains(options.Validations.Items, item => item.OptionType.Name == "PaymentOptions" && item.ValidationKind == "ValidateOnStart");

        ApplicationDomainSymbol subject = await FindTypeAsync(workspace, "CreateOrderCommand");
        MessageFlowResult messages = await new ApplicationDomainResolver().ResolveMessageFlowAsync(
            workspace,
            projects,
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

        Assert.Contains(messages.Handlers.Items, item => item.HandlerType.Name == "CreateOrderHandler");
        Assert.Contains(messages.CallSites.Items, item => item.MessageType.Name == "CreateOrderCommand");

        EfModelResult ef = await new ApplicationDomainResolver().ResolveEfModelAsync(
            workspace,
            projects,
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

        Assert.Contains(ef.DbContexts.Items, item => item.Type.Name == "OrdersDbContext");
        Assert.Contains(ef.DbSets.Items, item => item.EntityType.Name == "Order" && item.PropertyName == "Orders");
        Assert.Contains(ef.Configurations.Items, item => item.ConfigurationType.Name == "OrderConfiguration");
        Assert.Contains(ef.QuerySites.Items, item => item.EntityType.Name == "Order" && item.QueryKind == "FirstOrDefault");
    }

    [Fact]
    public async Task PublicApiAndReviewPack_VisualBasicProject_ReturnVbFacts()
    {
        SourceFixtureFile source = Source();
        string text = await File.ReadAllTextAsync(source.File.FullName);
        IReadOnlyList<PublicApiSymbol> api = new PublicApiSymbolExtractor().Extract(
            text,
            source.RelativePath.Replace('\\', '/'),
            targetFramework: null,
            excludeGenerated: false);

        Assert.Contains(api, symbol => symbol.Kind == "NamedType" && symbol.Name == "WidgetService");
        Assert.Contains(api, symbol => symbol.Kind == "Method" && symbol.Name == "Format");

        using LoadedWorkspace workspace = await LoadWorkspaceAsync();
        ReviewPackExecutionResult reviewResult = await new ReviewPackResolver().ResolveAsync(
            workspace,
            workspace.Solution.Projects.ToArray(),
            projectFilters: null,
            new ReviewPackOptions(
                Packs: ["async", "security"],
                Scope: "workspace",
                DiffRequest: null,
                ProjectFilters: [],
                ArchitectureConfig: null,
                FindingLimit: 50,
                EvidenceLimit: 5,
                SymbolLimit: 50,
                FileLimit: 20,
                IncludeSnippets: false,
                SnippetLines: 1,
                ExcludeGenerated: false),
            CancellationToken.None);

        ReviewPackResult review = Assert.IsType<ReviewPackResult>(reviewResult.Result);
        Assert.Contains(review.Findings, finding => finding.RuleId == "async.async-void");
        Assert.Contains(review.Findings, finding => finding.RuleId == "security.sql-string-construction");
        Assert.DoesNotContain(review.Findings, finding => finding.Evidence.Any(evidence => evidence.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)));
    }

    private async Task<LoadedWorkspace> LoadWorkspaceAsync()
    {
        string workspacePath = Path.Combine(fixture.RepoRoot, "tests", "fixtures", "VisualBasicFixture", "VisualBasicFixture.vbproj");
        WorkspaceLoadResult result = await new WorkspaceLoader().LoadAsync(new FileInfo(workspacePath), CancellationToken.None);
        Assert.Null(result.Error);
        return Assert.IsType<LoadedWorkspace>(result.Workspace);
    }

    private SourceFixtureFile Source()
    {
        return fixture.SourceFile("tests", "fixtures", "VisualBasicFixture", "FixtureCode.vb");
    }

    private static async Task<ApplicationDomainSymbol> FindTypeAsync(LoadedWorkspace workspace, string name)
    {
        foreach (Project project in workspace.Solution.Projects)
        {
            foreach (Document document in project.Documents)
            {
                SyntaxNode? root = await document.GetSyntaxRootAsync(CancellationToken.None);
                SemanticModel? semanticModel = await document.GetSemanticModelAsync(CancellationToken.None);
                if (root is null || semanticModel is null)
                {
                    continue;
                }

                foreach (SyntaxNode node in root.DescendantNodes())
                {
                    if (semanticModel.GetDeclaredSymbol(node) is INamedTypeSymbol symbol &&
                        symbol.Name == name)
                    {
                        return ApplicationDomainResolver.CreateSymbol(symbol, project.Name, excludeGenerated: false);
                    }
                }
            }
        }

        throw new InvalidOperationException($"Could not find type {name}.");
    }
}

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Navlyn.GeneratedCode;
using Navlyn.Paths;
using Navlyn.RepoGraph;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.ApplicationDomains;

internal sealed class ApplicationDomainResolver
{
    public async Task<RouteMapResult> ResolveRouteMapAsync(
        LoadedWorkspace workspace,
        IReadOnlyList<Project> projects,
        IReadOnlyList<ApplicationDomainProjectFilter>? projectFilters,
        RouteMapOptions options,
        CancellationToken cancellationToken)
    {
        DomainScan scan = await ScanAsync(projects, options.ExcludeGenerated, includePackageUsage: false, packageOptions: null, cancellationToken);
        IReadOnlyList<RouteEndpointFact> routes = FilterRoutes(scan.Routes, options);
        ApplicationDomainSection<RouteEndpointFact> section = CreateSection(routes, options.RouteLimit, OrderRoutes);

        return new RouteMapResult(
            workspace.DisplayPath,
            workspace.Kind,
            "route-map",
            projectFilters,
            new RouteMapFilters(options.RouteFilters, options.EndpointKinds, options.AuthFilter),
            new RouteMapLimits(options.RouteLimit, options.EvidenceLimit),
            section,
            section.Truncated,
            section.TotalItems == 0 ? ["no-routes-found"] : [],
            []);
    }

    public async Task<RouteImpactResult> ResolveRouteImpactAsync(
        LoadedWorkspace workspace,
        IReadOnlyList<Project> projects,
        IReadOnlyList<ApplicationDomainProjectFilter>? projectFilters,
        string route,
        RouteMapOptions options,
        CancellationToken cancellationToken)
    {
        RouteMapOptions routeOptions = options with { RouteFilters = [route] };
        DomainScan scan = await ScanAsync(projects, options.ExcludeGenerated, includePackageUsage: false, packageOptions: null, cancellationToken);
        IReadOnlyList<RouteEndpointFact> routes = FilterRoutes(scan.Routes, routeOptions);
        ApplicationDomainSection<RouteEndpointFact> section = CreateSection(routes, options.RouteLimit, OrderRoutes);

        return new RouteImpactResult(
            workspace.DisplayPath,
            workspace.Kind,
            "route-impact",
            route,
            projectFilters,
            new RouteMapLimits(options.RouteLimit, options.EvidenceLimit),
            section,
            section.Truncated,
            section.TotalItems == 0 ? ["no-routes-found"] : [],
            []);
    }

    public async Task<OptionsGraphResult> ResolveOptionsGraphAsync(
        LoadedWorkspace workspace,
        IReadOnlyList<Project> projects,
        IReadOnlyList<ApplicationDomainProjectFilter>? projectFilters,
        OptionsGraphOptions options,
        CancellationToken cancellationToken)
    {
        DomainScan scan = await ScanAsync(projects, options.ExcludeGenerated, includePackageUsage: false, packageOptions: null, cancellationToken);
        DomainScan filtered = FilterOptions(scan, options.Query);
        ApplicationDomainSection<OptionTypeFact> optionSection = CreateSection(filtered.Options, options.OptionLimit, OrderOptions);
        ApplicationDomainSection<OptionBindingFact> bindingSection = CreateSection(filtered.OptionBindings, options.BindingLimit, OrderBindings);
        ApplicationDomainSection<OptionConsumerFact> consumerSection = CreateSection(filtered.OptionConsumers, options.ConsumerLimit, OrderConsumers);
        ApplicationDomainSection<OptionValidationFact> validationSection = CreateSection(filtered.OptionValidations, options.BindingLimit, OrderValidations);

        return new OptionsGraphResult(
            workspace.DisplayPath,
            workspace.Kind,
            "options-graph",
            projectFilters,
            string.IsNullOrWhiteSpace(options.Query) ? null : options.Query.Trim(),
            new OptionsGraphLimits(options.OptionLimit, options.ConsumerLimit, options.BindingLimit, options.EvidenceLimit),
            optionSection,
            bindingSection,
            consumerSection,
            validationSection,
            optionSection.Truncated || bindingSection.Truncated || consumerSection.Truncated || validationSection.Truncated,
            optionSection.TotalItems == 0 && bindingSection.TotalItems == 0 && consumerSection.TotalItems == 0 ? ["no-options-found"] : [],
            []);
    }

    public async Task<ConfigImpactResult> ResolveConfigImpactAsync(
        LoadedWorkspace workspace,
        IReadOnlyList<Project> projects,
        IReadOnlyList<ApplicationDomainProjectFilter>? projectFilters,
        string query,
        OptionsGraphOptions options,
        CancellationToken cancellationToken)
    {
        DomainScan scan = await ScanAsync(projects, options.ExcludeGenerated, includePackageUsage: false, packageOptions: null, cancellationToken);
        DomainScan filtered = FilterOptions(scan, query);
        ApplicationDomainSection<OptionTypeFact> optionSection = CreateSection(filtered.Options, options.OptionLimit, OrderOptions);
        ApplicationDomainSection<OptionBindingFact> bindingSection = CreateSection(filtered.OptionBindings, options.BindingLimit, OrderBindings);
        ApplicationDomainSection<OptionConsumerFact> consumerSection = CreateSection(filtered.OptionConsumers, options.ConsumerLimit, OrderConsumers);
        ApplicationDomainSection<OptionValidationFact> validationSection = CreateSection(filtered.OptionValidations, options.BindingLimit, OrderValidations);

        return new ConfigImpactResult(
            workspace.DisplayPath,
            workspace.Kind,
            "config-impact",
            query,
            projectFilters,
            new OptionsGraphLimits(options.OptionLimit, options.ConsumerLimit, options.BindingLimit, options.EvidenceLimit),
            optionSection,
            bindingSection,
            consumerSection,
            validationSection,
            optionSection.Truncated || bindingSection.Truncated || consumerSection.Truncated || validationSection.Truncated,
            optionSection.TotalItems == 0 && bindingSection.TotalItems == 0 && consumerSection.TotalItems == 0 ? ["no-options-found"] : [],
            []);
    }

    public async Task<MessageFlowResult> ResolveMessageFlowAsync(
        LoadedWorkspace workspace,
        IReadOnlyList<Project> projects,
        ApplicationDomainSelectionInput selectionInput,
        ApplicationDomainSelectionSection? selection,
        ApplicationDomainSymbol? subject,
        MessageFlowOptions options,
        int candidateLimit,
        string commandName,
        CancellationToken cancellationToken)
    {
        DomainScan scan = await ScanAsync(projects, options.ExcludeGenerated, includePackageUsage: false, packageOptions: null, cancellationToken);
        IReadOnlyList<MessageHandlerFact> handlers = subject is null
            ? []
            : [.. scan.MessageHandlers.Where(handler => SameSymbol(handler.MessageType, subject))];
        IReadOnlyList<MessageCallSiteFact> callSites = subject is null || options.CallSiteLimit == 0
            ? []
            : [.. scan.MessageCallSites.Where(site => SameSymbol(site.MessageType, subject))];

        ApplicationDomainSection<MessageHandlerFact> handlerSection = CreateSection(handlers, options.HandlerLimit, OrderHandlers);
        ApplicationDomainSection<MessageCallSiteFact> callSiteSection = CreateSection(callSites, options.CallSiteLimit, OrderCallSites);

        List<string> warnings = [];
        if (subject is null)
        {
            warnings.Add("no-selected-message");
        }
        else if (handlerSection.TotalItems == 0)
        {
            warnings.Add("no-message-handlers-found");
        }

        return new MessageFlowResult(
            workspace.DisplayPath,
            workspace.Kind,
            commandName,
            selectionInput,
            selection,
            subject,
            new MessageFlowLimits(candidateLimit, options.HandlerLimit, options.CallSiteLimit, options.EvidenceLimit),
            handlerSection,
            callSiteSection,
            handlerSection.Truncated || callSiteSection.Truncated,
            warnings,
            []);
    }

    public async Task<EfModelResult> ResolveEfModelAsync(
        LoadedWorkspace workspace,
        IReadOnlyList<Project> projects,
        IReadOnlyList<ApplicationDomainProjectFilter>? projectFilters,
        EfModelOptions options,
        CancellationToken cancellationToken)
    {
        DomainScan scan = await ScanAsync(projects, options.ExcludeGenerated, includePackageUsage: false, packageOptions: null, cancellationToken);
        DomainScan filtered = FilterEf(scan, options.EntityQuery, options.DbContextQuery);
        ApplicationDomainSection<EfDbContextFact> contextSection = CreateSection(filtered.DbContexts, options.EntityLimit, OrderDbContexts);
        ApplicationDomainSection<EfEntityFact> entitySection = CreateSection(filtered.Entities, options.EntityLimit, OrderEntities);
        ApplicationDomainSection<EfDbSetFact> dbSetSection = CreateSection(filtered.DbSets, options.EntityLimit, OrderDbSets);
        ApplicationDomainSection<EfConfigurationFact> configurationSection = CreateSection(filtered.EfConfigurations, options.EntityLimit, OrderConfigurations);
        ApplicationDomainSection<EfQuerySiteFact> querySection = CreateSection(filtered.QuerySites, options.QuerySiteLimit, OrderQuerySites);

        return new EfModelResult(
            workspace.DisplayPath,
            workspace.Kind,
            "ef-model",
            projectFilters,
            new EfModelFilters(options.EntityQuery, options.DbContextQuery),
            new EfModelLimits(CandidateLimit: 0, options.EntityLimit, options.QuerySiteLimit, options.EvidenceLimit),
            contextSection,
            entitySection,
            dbSetSection,
            configurationSection,
            querySection,
            contextSection.Truncated || entitySection.Truncated || dbSetSection.Truncated || configurationSection.Truncated || querySection.Truncated,
            contextSection.TotalItems == 0 && entitySection.TotalItems == 0 ? ["no-ef-model-facts-found"] : [],
            []);
    }

    public async Task<EntityImpactResult> ResolveEntityImpactAsync(
        LoadedWorkspace workspace,
        IReadOnlyList<Project> projects,
        ApplicationDomainSelectionInput selectionInput,
        ApplicationDomainSelectionSection? selection,
        ApplicationDomainSymbol? subject,
        EfModelOptions options,
        int candidateLimit,
        CancellationToken cancellationToken)
    {
        DomainScan scan = await ScanAsync(projects, options.ExcludeGenerated, includePackageUsage: false, packageOptions: null, cancellationToken);
        IReadOnlyList<EfDbSetFact> dbSets = subject is null ? [] : [.. scan.DbSets.Where(item => SameSymbol(item.EntityType, subject))];
        IReadOnlyList<EfConfigurationFact> configurations = subject is null ? [] : [.. scan.EfConfigurations.Where(item => SameSymbol(item.EntityType, subject))];
        IReadOnlyList<EfQuerySiteFact> querySites = subject is null ? [] : [.. scan.QuerySites.Where(item => SameSymbol(item.EntityType, subject))];

        ApplicationDomainSection<EfDbSetFact> dbSetSection = CreateSection(dbSets, options.EntityLimit, OrderDbSets);
        ApplicationDomainSection<EfConfigurationFact> configurationSection = CreateSection(configurations, options.EntityLimit, OrderConfigurations);
        ApplicationDomainSection<EfQuerySiteFact> querySection = CreateSection(querySites, options.QuerySiteLimit, OrderQuerySites);

        return new EntityImpactResult(
            workspace.DisplayPath,
            workspace.Kind,
            "entity-impact",
            selectionInput,
            selection,
            subject,
            new EfModelLimits(candidateLimit, options.EntityLimit, options.QuerySiteLimit, options.EvidenceLimit),
            dbSetSection,
            configurationSection,
            querySection,
            dbSetSection.Truncated || configurationSection.Truncated || querySection.Truncated,
            subject is null ? ["no-selected-entity"] : dbSetSection.TotalItems == 0 && configurationSection.TotalItems == 0 && querySection.TotalItems == 0 ? ["no-entity-impact-found"] : [],
            []);
    }

    public async Task<PackageUsageResult> ResolvePackageUsageAsync(
        LoadedWorkspace workspace,
        IReadOnlyList<Project> projects,
        IReadOnlyList<ApplicationDomainProjectFilter>? projectFilters,
        PackageUsageOptions options,
        CancellationToken cancellationToken)
    {
        DomainScan scan = await ScanAsync(projects, options.ExcludeGenerated, includePackageUsage: true, options, cancellationToken);
        ApplicationDomainSection<PackageReferenceFact> referenceSection = CreateSection(scan.PackageReferences, options.ReferenceLimit, OrderPackageReferences);
        ApplicationDomainSection<PackageSourceUsageFact> usageSection = CreateSection(scan.PackageUsages, options.UsageLimit, OrderPackageUsages);

        return new PackageUsageResult(
            workspace.DisplayPath,
            workspace.Kind,
            "package-usage",
            options.Package,
            projectFilters,
            new PackageUsageLimits(options.UsageLimit, options.ReferenceLimit),
            referenceSection,
            usageSection,
            referenceSection.Truncated || usageSection.Truncated,
            referenceSection.TotalItems == 0 && usageSection.TotalItems == 0 ? ["no-package-usage-found"] : [],
            []);
    }

    public async Task<PackageImpactResult> ResolvePackageImpactAsync(
        LoadedWorkspace workspace,
        IReadOnlyList<Project> projects,
        IReadOnlyList<ApplicationDomainProjectFilter>? projectFilters,
        PackageUsageOptions options,
        CancellationToken cancellationToken)
    {
        DomainScan scan = await ScanAsync(projects, options.ExcludeGenerated, includePackageUsage: true, options, cancellationToken);
        ApplicationDomainSection<PackageReferenceFact> referenceSection = CreateSection(scan.PackageReferences, options.ReferenceLimit, OrderPackageReferences);
        ApplicationDomainSection<PackageSourceUsageFact> usageSection = CreateSection(scan.PackageUsages, options.UsageLimit, OrderPackageUsages);

        return new PackageImpactResult(
            workspace.DisplayPath,
            workspace.Kind,
            "package-impact",
            options.Package,
            projectFilters,
            new PackageUsageLimits(options.UsageLimit, options.ReferenceLimit),
            referenceSection,
            usageSection,
            referenceSection.Truncated || usageSection.Truncated,
            referenceSection.TotalItems == 0 && usageSection.TotalItems == 0 ? ["no-package-usage-found"] : [],
            []);
    }

    private static async Task<DomainScan> ScanAsync(
        IReadOnlyList<Project> projects,
        bool excludeGenerated,
        bool includePackageUsage,
        PackageUsageOptions? packageOptions,
        CancellationToken cancellationToken)
    {
        List<RouteEndpointFact> routes = [];
        List<OptionTypeFact> options = [];
        List<OptionBindingFact> optionBindings = [];
        List<OptionConsumerFact> optionConsumers = [];
        List<OptionValidationFact> optionValidations = [];
        List<MessageHandlerFact> messageHandlers = [];
        List<MessageCallSiteFact> messageCallSites = [];
        List<EfDbContextFact> dbContexts = [];
        List<EfEntityFact> entities = [];
        List<EfDbSetFact> dbSets = [];
        List<EfConfigurationFact> efConfigurations = [];
        List<EfQuerySiteFact> querySites = [];
        List<PackageReferenceFact> packageReferences = [];
        List<PackageSourceUsageFact> packageUsages = [];

        string repositoryRoot = PathDisplay.FindRepositoryRoot(projects.Select(project => project.FilePath).FirstOrDefault(path => path is not null) ?? Directory.GetCurrentDirectory()) ??
            Directory.GetCurrentDirectory();
        ProjectFileReader projectFileReader = new();

        Dictionary<string, ApplicationDomainSymbol> knownEfEntities = new(StringComparer.Ordinal);

        foreach (Project project in projects.OrderBy(project => project.FilePath, StringComparer.Ordinal).ThenBy(project => project.Name, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ApplicationDomainProject projectInfo = CreateProject(project);

            if (includePackageUsage && packageOptions is not null)
            {
                packageReferences.AddRange(DiscoverPackageReferences(project, projectInfo, repositoryRoot, projectFileReader, packageOptions));
            }

            foreach (Document document in project.Documents
                .Where(document => document.FilePath is not null)
                .Where(document => !excludeGenerated || !GeneratedCodeFacts.IsGeneratedPath(document.FilePath))
                .OrderBy(document => document.FilePath, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken);
                SemanticModel? semanticModel = await document.GetSemanticModelAsync(cancellationToken);
                if (root is null || semanticModel is null)
                {
                    continue;
                }

                routes.AddRange(DiscoverRoutes(root, semanticModel, projectInfo, excludeGenerated));
                DiscoverOptions(root, semanticModel, projectInfo, excludeGenerated, options, optionBindings, optionConsumers, optionValidations);
                DiscoverMessages(root, semanticModel, projectInfo, excludeGenerated, messageHandlers, messageCallSites);
                DiscoverEf(root, semanticModel, projectInfo, excludeGenerated, dbContexts, entities, dbSets, efConfigurations, querySites, knownEfEntities);

                if (includePackageUsage && packageOptions is not null)
                {
                    packageUsages.AddRange(DiscoverPackageUsages(root, semanticModel, projectInfo, excludeGenerated, packageOptions));
                }
            }
        }

        return new DomainScan(
            Deduplicate(routes, route => $"{route.EndpointKind}|{route.Path}|{route.Line}|{route.Column}|{route.NormalizedRoutePattern}"),
            Deduplicate(options, item => Identity(item.Type)),
            Deduplicate(optionBindings, item => $"{Identity(item.OptionType)}|{item.Path}|{item.Line}|{item.Column}|{item.BindingKind}|{item.ConfigurationKey}"),
            Deduplicate(optionConsumers, item => $"{Identity(item.ConsumerType)}|{Identity(item.OptionType)}|{item.Path}|{item.Line}|{item.Column}"),
            Deduplicate(optionValidations, item => $"{Identity(item.OptionType)}|{item.Path}|{item.Line}|{item.Column}|{item.ValidationKind}"),
            Deduplicate(messageHandlers, item => $"{Identity(item.MessageType)}|{Identity(item.HandlerType)}|{item.MessageKind}"),
            Deduplicate(messageCallSites, item => $"{Identity(item.MessageType)}|{item.Path}|{item.Line}|{item.Column}|{item.CallKind}"),
            Deduplicate(dbContexts, item => Identity(item.Type)),
            Deduplicate(entities, item => Identity(item.Type)),
            Deduplicate(dbSets, item => $"{Identity(item.DbContext)}|{Identity(item.EntityType)}|{item.PropertyName}"),
            Deduplicate(efConfigurations, item => $"{Identity(item.EntityType)}|{Identity(item.ConfigurationType)}"),
            Deduplicate(querySites, item => $"{Identity(item.EntityType)}|{item.Path}|{item.Line}|{item.Column}|{item.QueryKind}"),
            Deduplicate(packageReferences, item => $"{item.Project.Path}|{item.Name}|{item.Version}"),
            Deduplicate(packageUsages, item => $"{item.Project.Path}|{item.Path}|{item.Line}|{item.Column}|{item.NamespaceOrAssembly}|{item.UsageKind}"));
    }

    private static IEnumerable<RouteEndpointFact> DiscoverRoutes(
        SyntaxNode root,
        SemanticModel semanticModel,
        ApplicationDomainProject project,
        bool excludeGenerated)
    {
        foreach (ClassDeclarationSyntax type in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            INamedTypeSymbol? typeSymbol = semanticModel.GetDeclaredSymbol(type);
            if (typeSymbol is null || !IsControllerType(type, typeSymbol))
            {
                continue;
            }

            IReadOnlyList<string> typeRoutes = GetRouteTemplates(type.AttributeLists);
            foreach (MethodDeclarationSyntax method in type.Members.OfType<MethodDeclarationSyntax>())
            {
                if (HasAttribute(method.AttributeLists, "NonAction"))
                {
                    continue;
                }

                IMethodSymbol? methodSymbol = semanticModel.GetDeclaredSymbol(method);
                if (methodSymbol is null || methodSymbol.DeclaredAccessibility != Accessibility.Public || methodSymbol.IsStatic)
                {
                    continue;
                }

                IReadOnlyList<string> methods = GetHttpMethods(method.AttributeLists);
                IReadOnlyList<string> methodRoutes = GetRouteTemplates(method.AttributeLists);
                bool hasRouteEvidence = typeRoutes.Count > 0 || methodRoutes.Count > 0 || methods.Count > 0;
                if (!hasRouteEvidence && !type.Identifier.ValueText.EndsWith("Controller", StringComparison.Ordinal))
                {
                    continue;
                }

                SymbolSourceLocation? location = SymbolNavigationFacts.GetSourceLocations(methodSymbol, excludeGenerated).FirstOrDefault();
                if (location is null)
                {
                    continue;
                }

                IReadOnlyList<ApplicationDomainEvidence> evidence = CreateAttributeEvidence(type.AttributeLists, ["controller-route-or-auth"], excludeGenerated)
                    .Concat(CreateAttributeEvidence(method.AttributeLists, ["action-route-or-auth"], excludeGenerated))
                    .ToArray();

                yield return new RouteEndpointFact(
                    "controller-action",
                    methods.Count == 0 ? ["ANY"] : methods,
                    CombineRoutePatterns(typeRoutes, methodRoutes),
                    NormalizeRoutePattern(CombineRoutePatterns(typeRoutes, methodRoutes)),
                    CreateSymbol(methodSymbol, project.Name, excludeGenerated),
                    CreateAuth(type.AttributeLists, method.AttributeLists, excludeGenerated),
                    project,
                    location.Path,
                    location.Line,
                    location.Column,
                    location.EndLine,
                    location.EndColumn,
                    methods.Count > 0 || methodRoutes.Count > 0 ? "high" : "medium",
                    Ordered(["aspnetcore-controller-type", "controller-action", .. methods.Count > 0 ? ["http-method-attribute"] : Array.Empty<string>()]),
                    evidence,
                    null);
            }
        }

        foreach (InvocationExpressionSyntax invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            string? name = GetInvocationName(invocation);
            if (name is not ("MapGet" or "MapPost" or "MapPut" or "MapDelete" or "MapPatch" or "MapMethods" or "MapFallback" or "Map"))
            {
                continue;
            }

            ExpressionSyntax? handlerExpression = invocation.ArgumentList.Arguments.LastOrDefault()?.Expression;
            ApplicationDomainSymbol? handler = CreateHandlerSymbol(handlerExpression, semanticModel, project.Name, excludeGenerated);
            SymbolSourceLocation? location = SymbolNavigationFacts.CreateSourceLocation((handlerExpression ?? invocation).GetLocation(), excludeGenerated);
            if (handler is null || location is null)
            {
                continue;
            }

            string? routePattern = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax literal &&
                literal.IsKind(SyntaxKind.StringLiteralExpression)
                ? literal.Token.ValueText
                : null;

            yield return new RouteEndpointFact(
                "minimal-api",
                GetMinimalApiHttpMethods(name, invocation),
                routePattern,
                NormalizeRoutePattern(routePattern),
                handler,
                CreateMinimalApiAuth(invocation, excludeGenerated),
                project,
                location.Path,
                location.Line,
                location.Column,
                location.EndLine,
                location.EndColumn,
                routePattern is null ? "medium" : "high",
                Ordered(["minimal-api-map-call", $"{name.ToLowerInvariant()}-call"]),
                [CreateEvidence(invocation.GetLocation(), "invocation", ["minimal-api-map-call"], excludeGenerated)],
                null);
        }
    }

    private static void DiscoverOptions(
        SyntaxNode root,
        SemanticModel semanticModel,
        ApplicationDomainProject project,
        bool excludeGenerated,
        List<OptionTypeFact> options,
        List<OptionBindingFact> bindings,
        List<OptionConsumerFact> consumers,
        List<OptionValidationFact> validations)
    {
        foreach (InvocationExpressionSyntax invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            string? name = GetInvocationName(invocation);
            if (name is not ("Configure" or "AddOptions" or "Bind" or "Validate" or "ValidateDataAnnotations" or "ValidateOnStart"))
            {
                continue;
            }

            INamedTypeSymbol? optionType = GetOptionsTypeFromInvocation(invocation, semanticModel);
            if (optionType is null)
            {
                continue;
            }

            ApplicationDomainSymbol optionSymbol = CreateSymbol(optionType, project.Name, excludeGenerated);
            options.Add(new OptionTypeFact(optionSymbol, project, "high", Ordered(["options-type"]), [CreateEvidence(invocation.GetLocation(), "invocation", ["options-registration-or-binding"], excludeGenerated)]));

            SymbolSourceLocation? location = SymbolNavigationFacts.CreateSourceLocation(invocation.GetLocation(), excludeGenerated);
            if (location is null)
            {
                continue;
            }

            if (name is "Configure" or "AddOptions" or "Bind")
            {
                bindings.Add(new OptionBindingFact(
                    optionSymbol,
                    FindConfigurationKey(invocation),
                    name == "Bind" ? "bind" : name == "Configure" ? "configure" : "add-options",
                    project,
                    location.Path,
                    location.Line,
                    location.Column,
                    location.EndLine,
                    location.EndColumn,
                    "high",
                    Ordered(["options-binding", $"{name.ToLowerInvariant()}-call"]),
                    [CreateEvidence(invocation.GetLocation(), "invocation", ["options-binding"], excludeGenerated)]));
            }
            else
            {
                validations.Add(new OptionValidationFact(
                    optionSymbol,
                    name,
                    project,
                    location.Path,
                    location.Line,
                    location.Column,
                    location.EndLine,
                    location.EndColumn,
                    "medium",
                    Ordered(["options-validation", $"{name.ToLowerInvariant()}-call"]),
                    [CreateEvidence(invocation.GetLocation(), "invocation", ["options-validation"], excludeGenerated)]));
            }
        }

        foreach (ConstructorDeclarationSyntax constructor in root.DescendantNodes().OfType<ConstructorDeclarationSyntax>())
        {
            IMethodSymbol? constructorSymbol = semanticModel.GetDeclaredSymbol(constructor);
            INamedTypeSymbol? consumerType = constructorSymbol?.ContainingType;
            if (consumerType is null)
            {
                continue;
            }

            foreach (ParameterSyntax parameter in constructor.ParameterList.Parameters)
            {
                ITypeSymbol? parameterType = semanticModel.GetTypeInfo(parameter.Type!).Type;
                INamedTypeSymbol? optionType = GetOptionsTypeArgument(parameterType);
                if (optionType is null)
                {
                    continue;
                }

                SymbolSourceLocation? location = SymbolNavigationFacts.CreateSourceLocation(parameter.GetLocation(), excludeGenerated);
                if (location is null)
                {
                    continue;
                }

                ApplicationDomainSymbol optionSymbol = CreateSymbol(optionType, project.Name, excludeGenerated);
                options.Add(new OptionTypeFact(optionSymbol, project, "high", Ordered(["options-type"]), [CreateEvidence(parameter.GetLocation(), "parameter", ["options-consumer"], excludeGenerated)]));
                consumers.Add(new OptionConsumerFact(
                    CreateSymbol(consumerType, project.Name, excludeGenerated),
                    optionSymbol,
                    parameterType!.Name,
                    project,
                    location.Path,
                    location.Line,
                    location.Column,
                    location.EndLine,
                    location.EndColumn,
                    "high",
                    Ordered(["options-constructor-consumer", parameterType.Name.ToLowerInvariant()]),
                    [CreateEvidence(parameter.GetLocation(), "parameter", ["options-constructor-consumer"], excludeGenerated)]));
            }
        }

        foreach (ClassDeclarationSyntax type in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            if (type.ParameterList is null || semanticModel.GetDeclaredSymbol(type) is not INamedTypeSymbol consumerType)
            {
                continue;
            }

            foreach (ParameterSyntax parameter in type.ParameterList.Parameters)
            {
                ITypeSymbol? parameterType = semanticModel.GetTypeInfo(parameter.Type!).Type;
                INamedTypeSymbol? optionType = GetOptionsTypeArgument(parameterType);
                if (optionType is null)
                {
                    continue;
                }

                SymbolSourceLocation? location = SymbolNavigationFacts.CreateSourceLocation(parameter.GetLocation(), excludeGenerated);
                if (location is null)
                {
                    continue;
                }

                ApplicationDomainSymbol optionSymbol = CreateSymbol(optionType, project.Name, excludeGenerated);
                options.Add(new OptionTypeFact(optionSymbol, project, "high", Ordered(["options-type"]), [CreateEvidence(parameter.GetLocation(), "parameter", ["options-consumer"], excludeGenerated)]));
                consumers.Add(new OptionConsumerFact(
                    CreateSymbol(consumerType, project.Name, excludeGenerated),
                    optionSymbol,
                    parameterType!.Name,
                    project,
                    location.Path,
                    location.Line,
                    location.Column,
                    location.EndLine,
                    location.EndColumn,
                    "high",
                    Ordered(["options-primary-constructor-consumer", parameterType.Name.ToLowerInvariant()]),
                    [CreateEvidence(parameter.GetLocation(), "parameter", ["options-primary-constructor-consumer"], excludeGenerated)]));
            }
        }
    }

    private static void DiscoverMessages(
        SyntaxNode root,
        SemanticModel semanticModel,
        ApplicationDomainProject project,
        bool excludeGenerated,
        List<MessageHandlerFact> handlers,
        List<MessageCallSiteFact> callSites)
    {
        foreach (ClassDeclarationSyntax type in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            INamedTypeSymbol? typeSymbol = semanticModel.GetDeclaredSymbol(type);
            if (typeSymbol is null)
            {
                continue;
            }

            foreach (INamedTypeSymbol iface in typeSymbol.AllInterfaces)
            {
                string ifaceName = iface.Name;
                if (ifaceName is not ("IRequestHandler" or "INotificationHandler"))
                {
                    continue;
                }

                INamedTypeSymbol? messageType = iface.TypeArguments.FirstOrDefault() as INamedTypeSymbol;
                if (messageType is null)
                {
                    continue;
                }

                IMethodSymbol? handleMethod = typeSymbol.GetMembers("Handle").OfType<IMethodSymbol>().FirstOrDefault();
                ApplicationDomainSymbol? responseType = iface.TypeArguments.ElementAtOrDefault(1) is INamedTypeSymbol response
                    ? CreateSymbol(response, project.Name, excludeGenerated)
                    : null;

                handlers.Add(new MessageHandlerFact(
                    ifaceName == "INotificationHandler" ? "notification" : "request",
                    CreateSymbol(messageType, project.Name, excludeGenerated),
                    CreateSymbol(typeSymbol, project.Name, excludeGenerated),
                    handleMethod is null ? null : CreateSymbol(handleMethod, project.Name, excludeGenerated),
                    responseType,
                    project,
                    "high",
                    Ordered(["mediatr-handler-interface", ifaceName.ToLowerInvariant()]),
                    [CreateEvidence(type.GetLocation(), "type", ["mediatr-handler-interface"], excludeGenerated)]));
            }
        }

        foreach (InvocationExpressionSyntax invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            string? name = GetInvocationName(invocation);
            if (name is not ("Send" or "Publish"))
            {
                continue;
            }

            ExpressionSyntax? messageExpression = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
            INamedTypeSymbol? messageType = messageExpression is null
                ? null
                : semanticModel.GetTypeInfo(messageExpression).Type as INamedTypeSymbol;
            if (messageType is null || IsFrameworkType(messageType))
            {
                continue;
            }

            SymbolSourceLocation? location = SymbolNavigationFacts.CreateSourceLocation(invocation.GetLocation(), excludeGenerated);
            if (location is null)
            {
                continue;
            }

            ISymbol? containing = semanticModel.GetEnclosingSymbol(invocation.SpanStart);
            callSites.Add(new MessageCallSiteFact(
                name == "Publish" ? "publish" : "send",
                CreateSymbol(messageType, project.Name, excludeGenerated),
                containing is null ? null : CreateSymbol(containing, project.Name, excludeGenerated),
                project,
                location.Path,
                location.Line,
                location.Column,
                location.EndLine,
                location.EndColumn,
                "medium",
                Ordered(["mediatr-send-or-publish", $"{name.ToLowerInvariant()}-call"]),
                [CreateEvidence(invocation.GetLocation(), "invocation", ["mediatr-send-or-publish"], excludeGenerated)]));
        }
    }

    private static void DiscoverEf(
        SyntaxNode root,
        SemanticModel semanticModel,
        ApplicationDomainProject project,
        bool excludeGenerated,
        List<EfDbContextFact> dbContexts,
        List<EfEntityFact> entities,
        List<EfDbSetFact> dbSets,
        List<EfConfigurationFact> configurations,
        List<EfQuerySiteFact> querySites,
        Dictionary<string, ApplicationDomainSymbol> knownEntities)
    {
        foreach (ClassDeclarationSyntax type in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            INamedTypeSymbol? typeSymbol = semanticModel.GetDeclaredSymbol(type);
            if (typeSymbol is null)
            {
                continue;
            }

            if (InheritsTypeNamed(typeSymbol, "DbContext"))
            {
                ApplicationDomainSymbol contextSymbol = CreateSymbol(typeSymbol, project.Name, excludeGenerated);
                dbContexts.Add(new EfDbContextFact(contextSymbol, project, "high", Ordered(["ef-dbcontext-base-type"]), [CreateEvidence(type.GetLocation(), "type", ["ef-dbcontext-base-type"], excludeGenerated)]));

                foreach (PropertyDeclarationSyntax property in type.Members.OfType<PropertyDeclarationSyntax>())
                {
                    if (semanticModel.GetDeclaredSymbol(property) is not IPropertySymbol propertySymbol ||
                        propertySymbol.Type is not INamedTypeSymbol propertyType ||
                        propertyType.Name != "DbSet" ||
                        propertyType.TypeArguments.FirstOrDefault() is not INamedTypeSymbol entityType)
                    {
                        continue;
                    }

                    SymbolSourceLocation? location = SymbolNavigationFacts.GetSourceLocations(propertySymbol, excludeGenerated).FirstOrDefault();
                    if (location is null)
                    {
                        continue;
                    }

                    ApplicationDomainSymbol entitySymbol = CreateSymbol(entityType, project.Name, excludeGenerated);
                    knownEntities.TryAdd(Identity(entitySymbol), entitySymbol);
                    entities.Add(new EfEntityFact(entitySymbol, project, "high", Ordered(["ef-dbset-entity"]), [CreateEvidence(property.GetLocation(), "property", ["ef-dbset-property"], excludeGenerated)]));
                    dbSets.Add(new EfDbSetFact(
                        contextSymbol,
                        entitySymbol,
                        propertySymbol.Name,
                        project,
                        location.Path,
                        location.Line,
                        location.Column,
                        location.EndLine,
                        location.EndColumn,
                        "high",
                        Ordered(["ef-dbset-property"]),
                        [CreateEvidence(property.GetLocation(), "property", ["ef-dbset-property"], excludeGenerated)]));
                }
            }

            foreach (INamedTypeSymbol iface in typeSymbol.AllInterfaces.Where(iface => iface.Name == "IEntityTypeConfiguration"))
            {
                if (iface.TypeArguments.FirstOrDefault() is not INamedTypeSymbol entityType)
                {
                    continue;
                }

                ApplicationDomainSymbol entitySymbol = CreateSymbol(entityType, project.Name, excludeGenerated);
                knownEntities.TryAdd(Identity(entitySymbol), entitySymbol);
                entities.Add(new EfEntityFact(entitySymbol, project, "high", Ordered(["ef-configuration-entity"]), [CreateEvidence(type.GetLocation(), "type", ["ef-entitytypeconfiguration-interface"], excludeGenerated)]));
                configurations.Add(new EfConfigurationFact(
                    entitySymbol,
                    CreateSymbol(typeSymbol, project.Name, excludeGenerated),
                    project,
                    "high",
                    Ordered(["ef-entitytypeconfiguration-interface"]),
                    [CreateEvidence(type.GetLocation(), "type", ["ef-entitytypeconfiguration-interface"], excludeGenerated)]));
            }
        }

        foreach (InvocationExpressionSyntax invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            ExpressionSyntax queryExpression = invocation.Expression is MemberAccessExpressionSyntax memberAccess
                ? memberAccess.Expression
                : invocation.Expression;
            ITypeSymbol? expressionType = semanticModel.GetTypeInfo(queryExpression).Type;
            INamedTypeSymbol? entityType = GetQueryableEntityType(expressionType);
            if (entityType is null)
            {
                continue;
            }

            SymbolSourceLocation? location = SymbolNavigationFacts.CreateSourceLocation(invocation.GetLocation(), excludeGenerated);
            if (location is null)
            {
                continue;
            }

            ApplicationDomainSymbol entitySymbol = CreateSymbol(entityType, project.Name, excludeGenerated);
            knownEntities.TryAdd(Identity(entitySymbol), entitySymbol);
            ISymbol? containing = semanticModel.GetEnclosingSymbol(invocation.SpanStart);
            querySites.Add(new EfQuerySiteFact(
                entitySymbol,
                containing is null ? null : CreateSymbol(containing, project.Name, excludeGenerated),
                GetInvocationName(invocation) ?? "query",
                project,
                location.Path,
                location.Line,
                location.Column,
                location.EndLine,
                location.EndColumn,
                "medium",
                Ordered(["ef-queryable-call"]),
                [CreateEvidence(invocation.GetLocation(), "invocation", ["ef-queryable-call"], excludeGenerated)]));
        }
    }

    private static IEnumerable<PackageReferenceFact> DiscoverPackageReferences(
        Project project,
        ApplicationDomainProject projectInfo,
        string repositoryRoot,
        ProjectFileReader reader,
        PackageUsageOptions options)
    {
        string? projectPath = project.FilePath is null ? null : PathDisplay.FromCurrentDirectory(project.FilePath);
        ProjectFileFacts facts = reader.Read(projectPath, repositoryRoot);
        return facts.PackageReferences
            .Where(reference => string.Equals(reference.Name, options.Package, StringComparison.OrdinalIgnoreCase))
            .Select(reference => new PackageReferenceFact(
                reference.Name,
                reference.Version,
                reference.IsCentralVersion,
                projectInfo,
                "high",
                Ordered(["package-reference"])));
    }

    private static IEnumerable<PackageSourceUsageFact> DiscoverPackageUsages(
        SyntaxNode root,
        SemanticModel semanticModel,
        ApplicationDomainProject project,
        bool excludeGenerated,
        PackageUsageOptions options)
    {
        IReadOnlyList<string> hints = options.NamespaceHints.Count == 0
            ? GuessPackageNamespaceHints(options.Package)
            : options.NamespaceHints;
        if (hints.Count == 0)
        {
            yield break;
        }

        foreach (UsingDirectiveSyntax usingDirective in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
        {
            string namespaceName = usingDirective.Name?.ToString() ?? "";
            if (!MatchesAnyHint(namespaceName, hints))
            {
                continue;
            }

            SymbolSourceLocation? location = SymbolNavigationFacts.CreateSourceLocation(usingDirective.GetLocation(), excludeGenerated);
            if (location is null)
            {
                continue;
            }

            yield return new PackageSourceUsageFact(
                "using-directive",
                namespaceName,
                Symbol: null,
                ContainingSymbol: null,
                project,
                location.Path,
                location.Line,
                location.Column,
                location.EndLine,
                location.EndColumn,
                options.NamespaceHints.Count == 0 ? "low" : "medium",
                Ordered(options.NamespaceHints.Count == 0 ? ["package-namespace-guess"] : ["package-namespace-hint"]),
                [CreateEvidence(usingDirective.GetLocation(), "using", ["package-source-usage"], excludeGenerated)]);
        }

        foreach (SyntaxNode node in root.DescendantNodes().Where(node => node is IdentifierNameSyntax or QualifiedNameSyntax or MemberAccessExpressionSyntax))
        {
            SymbolInfo symbolInfo = semanticModel.GetSymbolInfo(node);
            ISymbol? symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
            if (symbol is null)
            {
                continue;
            }

            string namespaceOrAssembly = symbol.ContainingNamespace?.ToDisplayString() ??
                symbol.ContainingAssembly?.Name ??
                symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            if (!MatchesAnyHint(namespaceOrAssembly, hints))
            {
                continue;
            }

            SymbolSourceLocation? location = SymbolNavigationFacts.CreateSourceLocation(node.GetLocation(), excludeGenerated);
            if (location is null)
            {
                continue;
            }

            ISymbol? containing = semanticModel.GetEnclosingSymbol(node.SpanStart);
            yield return new PackageSourceUsageFact(
                "symbol-reference",
                namespaceOrAssembly,
                CreateSymbol(symbol, project.Name, excludeGenerated),
                containing is null ? null : CreateSymbol(containing, project.Name, excludeGenerated),
                project,
                location.Path,
                location.Line,
                location.Column,
                location.EndLine,
                location.EndColumn,
                options.NamespaceHints.Count == 0 ? "low" : "medium",
                Ordered(options.NamespaceHints.Count == 0 ? ["package-namespace-guess"] : ["package-namespace-hint"]),
                [CreateEvidence(node.GetLocation(), "syntax", ["package-source-usage"], excludeGenerated)]);
        }
    }

    private static IReadOnlyList<RouteEndpointFact> FilterRoutes(IReadOnlyList<RouteEndpointFact> routes, RouteMapOptions options)
    {
        IEnumerable<RouteEndpointFact> query = routes;
        if (options.RouteFilters.Count > 0)
        {
            query = query.Where(route => options.RouteFilters.Any(filter =>
                (route.NormalizedRoutePattern ?? route.RoutePattern ?? "").Contains(filter, StringComparison.OrdinalIgnoreCase)));
        }

        if (options.EndpointKinds.Count > 0 && !options.EndpointKinds.Contains("any", StringComparer.Ordinal))
        {
            query = query.Where(route => options.EndpointKinds.Contains(route.EndpointKind, StringComparer.Ordinal));
        }

        query = options.AuthFilter switch
        {
            "required" => query.Where(route => route.Auth.Kind == "required"),
            "anonymous" => query.Where(route => route.Auth.Kind == "anonymous"),
            "unknown" => query.Where(route => route.Auth.Kind == "unknown"),
            _ => query
        };

        return [.. query];
    }

    private static DomainScan FilterOptions(DomainScan scan, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return scan;
        }

        string value = query.Trim();
        bool Matches(ApplicationDomainSymbol symbol) => MatchesSymbol(symbol, value);
        bool MatchesKey(string? key) => key?.Contains(value, StringComparison.OrdinalIgnoreCase) == true;

        return scan with
        {
            Options = [.. scan.Options.Where(item => Matches(item.Type))],
            OptionBindings = [.. scan.OptionBindings.Where(item => Matches(item.OptionType) || MatchesKey(item.ConfigurationKey))],
            OptionConsumers = [.. scan.OptionConsumers.Where(item => Matches(item.OptionType) || Matches(item.ConsumerType))],
            OptionValidations = [.. scan.OptionValidations.Where(item => Matches(item.OptionType))]
        };
    }

    private static DomainScan FilterEf(DomainScan scan, string? entityQuery, string? dbContextQuery)
    {
        IEnumerable<EfDbContextFact> contexts = scan.DbContexts;
        IEnumerable<EfEntityFact> entities = scan.Entities;
        IEnumerable<EfDbSetFact> dbSets = scan.DbSets;
        IEnumerable<EfConfigurationFact> configurations = scan.EfConfigurations;
        IEnumerable<EfQuerySiteFact> querySites = scan.QuerySites;

        if (!string.IsNullOrWhiteSpace(entityQuery))
        {
            string value = entityQuery.Trim();
            entities = entities.Where(item => MatchesSymbol(item.Type, value));
            dbSets = dbSets.Where(item => MatchesSymbol(item.EntityType, value));
            configurations = configurations.Where(item => MatchesSymbol(item.EntityType, value));
            querySites = querySites.Where(item => MatchesSymbol(item.EntityType, value));
        }

        if (!string.IsNullOrWhiteSpace(dbContextQuery))
        {
            string value = dbContextQuery.Trim();
            contexts = contexts.Where(item => MatchesSymbol(item.Type, value));
            dbSets = dbSets.Where(item => MatchesSymbol(item.DbContext, value));
        }

        return scan with
        {
            DbContexts = [.. contexts],
            Entities = [.. entities],
            DbSets = [.. dbSets],
            EfConfigurations = [.. configurations],
            QuerySites = [.. querySites]
        };
    }

    private static ApplicationDomainProject CreateProject(Project project)
    {
        return new ApplicationDomainProject(
            project.Name,
            project.FilePath is null ? null : PathDisplay.FromCurrentDirectory(project.FilePath),
            ProjectContextFacts.GetTargetFramework(project));
    }

    internal static ApplicationDomainSymbol CreateSymbol(ISymbol symbol, string? projectName, bool excludeGenerated)
    {
        SymbolSourceLocation? location = SymbolNavigationFacts.GetSourceLocations(symbol, excludeGenerated).FirstOrDefault();
        return new ApplicationDomainSymbol(
            symbol.Name,
            symbol.Kind.ToString(),
            SymbolNavigationFacts.GetContainer(symbol),
            SymbolFactsBuilder.Create(symbol, projectName),
            location?.Path,
            location?.Line,
            location?.Column,
            location?.EndLine,
            location?.EndColumn);
    }

    private static ApplicationDomainSymbol? CreateHandlerSymbol(ExpressionSyntax? expression, SemanticModel semanticModel, string projectName, bool excludeGenerated)
    {
        if (expression is null)
        {
            return null;
        }

        if (expression is LambdaExpressionSyntax lambda)
        {
            SymbolSourceLocation? location = SymbolNavigationFacts.CreateSourceLocation(lambda.GetLocation(), excludeGenerated);
            ISymbol? enclosing = semanticModel.GetEnclosingSymbol(lambda.SpanStart);
            return new ApplicationDomainSymbol(
                "<lambda>",
                "Lambda",
                enclosing is null ? null : SymbolNavigationFacts.GetContainer(enclosing) ?? enclosing.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                CreateSyntheticFacts("<lambda>", projectName),
                location?.Path,
                location?.Line,
                location?.Column,
                location?.EndLine,
                location?.EndColumn);
        }

        SymbolInfo info = semanticModel.GetSymbolInfo(expression);
        ISymbol? symbol = info.Symbol ?? info.CandidateSymbols.OrderBy(symbol => symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), StringComparer.Ordinal).FirstOrDefault();
        return symbol is null ? null : CreateSymbol(symbol, projectName, excludeGenerated);
    }

    private static SymbolFacts CreateSyntheticFacts(string displayName, string? project)
    {
        return new SymbolFacts(
            displayName,
            FullyQualifiedName: null,
            Signature: null,
            DocumentationCommentId: null,
            Namespace: null,
            ContainingType: null,
            Project: project,
            Assembly: null,
            Accessibility: null,
            IsSource: true,
            IsMetadata: false,
            IsStatic: false,
            IsAbstract: false,
            IsVirtual: false,
            IsOverride: false,
            IsAsync: false,
            IsExtensionMethod: false,
            IsConstructor: false,
            IsOperator: false,
            IsIndexer: false,
            Arity: null,
            TypeParameters: null,
            TypeArguments: null,
            ConstructedFrom: null,
            Parameters: null,
            ReturnType: null,
            PropertyType: null,
            EventType: null,
            FieldType: null,
            Attributes: null);
    }

    private static ApplicationDomainEvidence CreateEvidence(Location location, string kind, IReadOnlyList<string> reasons, bool excludeGenerated)
    {
        SymbolSourceLocation? source = SymbolNavigationFacts.CreateSourceLocation(location, excludeGenerated);
        return source is null
            ? new ApplicationDomainEvidence(kind, "", 0, 0, 0, 0, reasons)
            : new ApplicationDomainEvidence(kind, source.Path, source.Line, source.Column, source.EndLine, source.EndColumn, reasons);
    }

    private static IReadOnlyList<ApplicationDomainEvidence> CreateAttributeEvidence(SyntaxList<AttributeListSyntax> attributeLists, IReadOnlyList<string> reasons, bool excludeGenerated)
    {
        return [.. attributeLists.SelectMany(list => list.Attributes).Select(attribute => CreateEvidence(attribute.GetLocation(), "attribute", reasons, excludeGenerated))];
    }

    private static RouteAuthFact CreateAuth(SyntaxList<AttributeListSyntax> typeAttributes, SyntaxList<AttributeListSyntax> methodAttributes, bool excludeGenerated)
    {
        IReadOnlyList<AttributeSyntax> attributes = [.. typeAttributes.Concat(methodAttributes).SelectMany(list => list.Attributes)];
        return CreateAuthFromAttributes(attributes, excludeGenerated);
    }

    private static RouteAuthFact CreateMinimalApiAuth(InvocationExpressionSyntax mapInvocation, bool excludeGenerated)
    {
        List<AttributeSyntax> attributes = [];
        List<ApplicationDomainEvidence> evidence = [];
        bool required = false;
        bool anonymous = false;

        SyntaxNode? node = mapInvocation.Parent;
        while (node is not null)
        {
            if (node is InvocationExpressionSyntax invocation)
            {
                string? name = GetInvocationName(invocation);
                if (name == "RequireAuthorization")
                {
                    required = true;
                    evidence.Add(CreateEvidence(invocation.GetLocation(), "invocation", ["requireauthorization-call"], excludeGenerated));
                }
                else if (name == "AllowAnonymous")
                {
                    anonymous = true;
                    evidence.Add(CreateEvidence(invocation.GetLocation(), "invocation", ["allowanonymous-call"], excludeGenerated));
                }
            }

            node = node.Parent is MemberAccessExpressionSyntax or InvocationExpressionSyntax ? node.Parent : null;
        }

        if (anonymous)
        {
            return new RouteAuthFact("anonymous", [], [], [], evidence);
        }

        if (required)
        {
            return new RouteAuthFact("required", [], [], [], evidence);
        }

        return attributes.Count == 0
            ? new RouteAuthFact("unknown", [], [], [], [])
            : CreateAuthFromAttributes(attributes, excludeGenerated);
    }

    private static RouteAuthFact CreateAuthFromAttributes(IReadOnlyList<AttributeSyntax> attributes, bool excludeGenerated)
    {
        bool anonymous = attributes.Any(attribute => ShortAttributeName(attribute.Name.ToString()) == "AllowAnonymous");
        IReadOnlyList<AttributeSyntax> authorize = [.. attributes.Where(attribute => ShortAttributeName(attribute.Name.ToString()) == "Authorize")];
        if (anonymous)
        {
            return new RouteAuthFact(
                "anonymous",
                [],
                [],
                [],
                [.. attributes.Where(attribute => ShortAttributeName(attribute.Name.ToString()) == "AllowAnonymous").Select(attribute => CreateEvidence(attribute.GetLocation(), "attribute", ["allowanonymous-attribute"], excludeGenerated))]);
        }

        return authorize.Count == 0
            ? new RouteAuthFact("unknown", [], [], [], [])
            : new RouteAuthFact(
                "required",
                [.. authorize.Select(attribute => GetNamedString(attribute, "Policy") ?? GetFirstStringArgument(attribute)).Where(value => !string.IsNullOrWhiteSpace(value))!],
                [.. authorize.Select(attribute => GetNamedString(attribute, "Roles")).Where(value => !string.IsNullOrWhiteSpace(value))!],
                [.. authorize.Select(attribute => GetNamedString(attribute, "AuthenticationSchemes")).Where(value => !string.IsNullOrWhiteSpace(value))!],
                [.. authorize.Select(attribute => CreateEvidence(attribute.GetLocation(), "attribute", ["authorize-attribute"], excludeGenerated))]);
    }

    private static IReadOnlyList<string> GetHttpMethods(SyntaxList<AttributeListSyntax> attributes)
    {
        return [.. attributes
            .SelectMany(list => list.Attributes)
            .Select(attribute => ShortAttributeName(attribute.Name.ToString()))
            .Select(name => name switch
            {
                "HttpGet" => "GET",
                "HttpPost" => "POST",
                "HttpPut" => "PUT",
                "HttpDelete" => "DELETE",
                "HttpPatch" => "PATCH",
                "HttpHead" => "HEAD",
                "HttpOptions" => "OPTIONS",
                _ => null
            })
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)];
    }

    private static IReadOnlyList<string> GetMinimalApiHttpMethods(string name, InvocationExpressionSyntax invocation)
    {
        return name switch
        {
            "MapGet" => ["GET"],
            "MapPost" => ["POST"],
            "MapPut" => ["PUT"],
            "MapDelete" => ["DELETE"],
            "MapPatch" => ["PATCH"],
            "MapFallback" => ["ANY"],
            "MapMethods" => GetMapMethods(invocation),
            _ => ["ANY"]
        };
    }

    private static IReadOnlyList<string> GetMapMethods(InvocationExpressionSyntax invocation)
    {
        ArgumentSyntax? methodsArgument = invocation.ArgumentList.Arguments.ElementAtOrDefault(1);
        if (methodsArgument?.Expression is not InitializerExpressionSyntax initializer)
        {
            return ["ANY"];
        }

        IReadOnlyList<string> methods = [.. initializer.Expressions
            .OfType<LiteralExpressionSyntax>()
            .Where(literal => literal.IsKind(SyntaxKind.StringLiteralExpression))
            .Select(literal => literal.Token.ValueText.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)];
        return methods.Count == 0 ? ["ANY"] : methods;
    }

    private static IReadOnlyList<string> GetRouteTemplates(SyntaxList<AttributeListSyntax> attributes)
    {
        return [.. attributes
            .SelectMany(list => list.Attributes)
            .Where(attribute => IsRouteAttribute(attribute) || IsHttpMethodAttribute(attribute))
            .Select(GetFirstStringArgument)
            .Where(value => value is not null)
            .Select(value => value!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)];
    }

    private static string? CombineRoutePatterns(IReadOnlyList<string> typeRoutes, IReadOnlyList<string> methodRoutes)
    {
        string? typeRoute = typeRoutes.FirstOrDefault();
        string? methodRoute = methodRoutes.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(typeRoute))
        {
            return methodRoute;
        }

        if (string.IsNullOrWhiteSpace(methodRoute))
        {
            return typeRoute;
        }

        return $"{typeRoute.TrimEnd('/')}/{methodRoute.TrimStart('/')}";
    }

    private static string? NormalizeRoutePattern(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return null;
        }

        string normalized = pattern.Trim();
        normalized = normalized.StartsWith("/", StringComparison.Ordinal) ? normalized : "/" + normalized;
        while (normalized.Contains("//", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        }

        return normalized;
    }

    private static bool IsRouteAttribute(AttributeSyntax attribute)
    {
        return ShortAttributeName(attribute.Name.ToString()) == "Route";
    }

    private static bool IsHttpMethodAttribute(AttributeSyntax attribute)
    {
        return ShortAttributeName(attribute.Name.ToString()) is "HttpGet" or "HttpPost" or "HttpPut" or "HttpDelete" or "HttpPatch" or "HttpHead" or "HttpOptions";
    }

    private static bool IsControllerType(ClassDeclarationSyntax type, INamedTypeSymbol symbol)
    {
        return type.Identifier.ValueText.EndsWith("Controller", StringComparison.Ordinal) ||
            HasAttribute(type.AttributeLists, "ApiController") ||
            InheritsTypeNamed(symbol, "ControllerBase") ||
            InheritsTypeNamed(symbol, "Controller");
    }

    private static INamedTypeSymbol? GetOptionsTypeFromInvocation(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        IReadOnlyList<ITypeSymbol> genericTypes = GetGenericTypeArguments(invocation, semanticModel);
        if (genericTypes.FirstOrDefault() is INamedTypeSymbol genericType)
        {
            return genericType;
        }

        ExpressionSyntax? target = invocation.Expression is MemberAccessExpressionSyntax memberAccess ? memberAccess.Expression : null;
        ITypeSymbol? targetType = target is null ? null : semanticModel.GetTypeInfo(target).Type;
        return GetOptionsTypeArgument(targetType);
    }

    private static INamedTypeSymbol? GetOptionsTypeArgument(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol named)
        {
            return null;
        }

        if (named.Name is "IOptions" or "IOptionsSnapshot" or "IOptionsMonitor" or "OptionsBuilder" &&
            named.TypeArguments.FirstOrDefault() is INamedTypeSymbol optionType)
        {
            return optionType;
        }

        return null;
    }

    private static string? FindConfigurationKey(InvocationExpressionSyntax invocation)
    {
        foreach (InvocationExpressionSyntax nested in invocation.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
        {
            if (GetInvocationName(nested) == "GetSection" &&
                nested.ArgumentList.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax literal &&
                literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                return literal.Token.ValueText;
            }
        }

        return null;
    }

    private static INamedTypeSymbol? GetQueryableEntityType(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol named)
        {
            return null;
        }

        if (named.Name is "DbSet" or "IQueryable" or "IEnumerable" && named.TypeArguments.FirstOrDefault() is INamedTypeSymbol entityType)
        {
            return entityType;
        }

        return named.AllInterfaces
            .Where(iface => iface.Name is "IQueryable" or "IEnumerable")
            .Select(iface => iface.TypeArguments.FirstOrDefault())
            .OfType<INamedTypeSymbol>()
            .FirstOrDefault();
    }

    private static IReadOnlyList<ITypeSymbol> GetGenericTypeArguments(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        GenericNameSyntax? genericName = invocation.Expression switch
        {
            GenericNameSyntax generic => generic,
            MemberAccessExpressionSyntax { Name: GenericNameSyntax generic } => generic,
            _ => null
        };

        return genericName is null
            ? []
            : [.. genericName.TypeArgumentList.Arguments.Select(type => semanticModel.GetTypeInfo(type).Type).OfType<ITypeSymbol>()];
    }

    private static string? GetInvocationName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            GenericNameSyntax generic => generic.Identifier.ValueText,
            MemberAccessExpressionSyntax { Name: IdentifierNameSyntax identifier } => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax { Name: GenericNameSyntax generic } => generic.Identifier.ValueText,
            _ => null
        };
    }

    private static string? GetFirstStringArgument(AttributeSyntax attribute)
    {
        return attribute.ArgumentList?.Arguments
            .Select(argument => argument.Expression as LiteralExpressionSyntax)
            .Where(literal => literal?.IsKind(SyntaxKind.StringLiteralExpression) == true)
            .Select(literal => literal!.Token.ValueText)
            .FirstOrDefault();
    }

    private static string? GetNamedString(AttributeSyntax attribute, string name)
    {
        return attribute.ArgumentList?.Arguments
            .Where(argument => argument.NameEquals?.Name.Identifier.ValueText == name)
            .Select(argument => argument.Expression as LiteralExpressionSyntax)
            .Where(literal => literal?.IsKind(SyntaxKind.StringLiteralExpression) == true)
            .Select(literal => literal!.Token.ValueText)
            .FirstOrDefault();
    }

    private static bool HasAttribute(SyntaxList<AttributeListSyntax> attributeLists, string expectedShortName)
    {
        return attributeLists
            .SelectMany(list => list.Attributes)
            .Select(attribute => ShortAttributeName(attribute.Name.ToString()))
            .Any(name => name == expectedShortName || name.EndsWith($".{expectedShortName}", StringComparison.Ordinal));
    }

    private static string ShortAttributeName(string name)
    {
        string shortName = name.EndsWith("Attribute", StringComparison.Ordinal) ? name[..^"Attribute".Length] : name;
        int dot = shortName.LastIndexOf('.');
        return dot >= 0 ? shortName[(dot + 1)..] : shortName;
    }

    private static bool InheritsTypeNamed(INamedTypeSymbol symbol, string name)
    {
        for (INamedTypeSymbol? current = symbol.BaseType; current is not null; current = current.BaseType)
        {
            if (current.Name == name || current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).EndsWith($".{name}", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsFrameworkType(INamedTypeSymbol symbol)
    {
        string display = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return display.StartsWith("global::System.", StringComparison.Ordinal) ||
            display.StartsWith("global::Microsoft.", StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> GuessPackageNamespaceHints(string package)
    {
        return package.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) is { Length: >= 2 } parts
            ? [string.Join('.', parts.Take(2))]
            : [];
    }

    private static bool MatchesAnyHint(string value, IReadOnlyList<string> hints)
    {
        return hints.Any(hint => value.StartsWith(hint, StringComparison.Ordinal) || value.Contains($".{hint}.", StringComparison.Ordinal));
    }

    private static bool MatchesSymbol(ApplicationDomainSymbol symbol, string query)
    {
        return symbol.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            symbol.Container?.Contains(query, StringComparison.OrdinalIgnoreCase) == true ||
            symbol.Facts.FullyQualifiedName?.Contains(query, StringComparison.OrdinalIgnoreCase) == true ||
            symbol.Facts.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SameSymbol(ApplicationDomainSymbol left, ApplicationDomainSymbol right)
    {
        return Identity(left) == Identity(right);
    }

    internal static string Identity(ApplicationDomainSymbol symbol)
    {
        return symbol.Facts.DocumentationCommentId ??
            symbol.Facts.FullyQualifiedName ??
            symbol.Facts.DisplayName ??
            $"{symbol.Container}.{symbol.Name}";
    }

    private static ApplicationDomainSection<T> CreateSection<T>(
        IReadOnlyList<T> items,
        int limit,
        Func<IReadOnlyList<T>, IReadOnlyList<T>> order)
    {
        IReadOnlyList<T> ordered = order(items);
        return new ApplicationDomainSection<T>(ordered.Count, limit, ordered.Count > limit, [.. ordered.Take(limit)]);
    }

    private static IReadOnlyList<T> Deduplicate<T>(IEnumerable<T> items, Func<T, string> key)
    {
        return [.. items.GroupBy(key, StringComparer.Ordinal).Select(group => group.First())];
    }

    private static IReadOnlyList<string> Ordered(IReadOnlyList<string> values)
    {
        return [.. values.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal)];
    }

    private static IReadOnlyList<RouteEndpointFact> OrderRoutes(IReadOnlyList<RouteEndpointFact> items)
    {
        return [.. items
            .OrderBy(item => item.Project.Path, StringComparer.Ordinal)
            .ThenBy(item => item.Path, StringComparer.Ordinal)
            .ThenBy(item => item.Line)
            .ThenBy(item => item.Column)
            .ThenBy(item => item.EndpointKind, StringComparer.Ordinal)
            .ThenBy(item => item.NormalizedRoutePattern, StringComparer.Ordinal)];
    }

    private static IReadOnlyList<OptionTypeFact> OrderOptions(IReadOnlyList<OptionTypeFact> items)
    {
        return [.. items.OrderBy(item => Identity(item.Type), StringComparer.Ordinal)];
    }

    private static IReadOnlyList<OptionBindingFact> OrderBindings(IReadOnlyList<OptionBindingFact> items)
    {
        return [.. items.OrderBy(item => item.Path, StringComparer.Ordinal).ThenBy(item => item.Line).ThenBy(item => item.Column)];
    }

    private static IReadOnlyList<OptionConsumerFact> OrderConsumers(IReadOnlyList<OptionConsumerFact> items)
    {
        return [.. items.OrderBy(item => Identity(item.OptionType), StringComparer.Ordinal).ThenBy(item => Identity(item.ConsumerType), StringComparer.Ordinal)];
    }

    private static IReadOnlyList<OptionValidationFact> OrderValidations(IReadOnlyList<OptionValidationFact> items)
    {
        return [.. items.OrderBy(item => Identity(item.OptionType), StringComparer.Ordinal).ThenBy(item => item.Path, StringComparer.Ordinal).ThenBy(item => item.Line)];
    }

    private static IReadOnlyList<MessageHandlerFact> OrderHandlers(IReadOnlyList<MessageHandlerFact> items)
    {
        return [.. items.OrderBy(item => Identity(item.MessageType), StringComparer.Ordinal).ThenBy(item => Identity(item.HandlerType), StringComparer.Ordinal)];
    }

    private static IReadOnlyList<MessageCallSiteFact> OrderCallSites(IReadOnlyList<MessageCallSiteFact> items)
    {
        return [.. items.OrderBy(item => item.Path, StringComparer.Ordinal).ThenBy(item => item.Line).ThenBy(item => item.Column)];
    }

    private static IReadOnlyList<EfDbContextFact> OrderDbContexts(IReadOnlyList<EfDbContextFact> items)
    {
        return [.. items.OrderBy(item => Identity(item.Type), StringComparer.Ordinal)];
    }

    private static IReadOnlyList<EfEntityFact> OrderEntities(IReadOnlyList<EfEntityFact> items)
    {
        return [.. items.OrderBy(item => Identity(item.Type), StringComparer.Ordinal)];
    }

    private static IReadOnlyList<EfDbSetFact> OrderDbSets(IReadOnlyList<EfDbSetFact> items)
    {
        return [.. items.OrderBy(item => Identity(item.DbContext), StringComparer.Ordinal).ThenBy(item => item.PropertyName, StringComparer.Ordinal)];
    }

    private static IReadOnlyList<EfConfigurationFact> OrderConfigurations(IReadOnlyList<EfConfigurationFact> items)
    {
        return [.. items.OrderBy(item => Identity(item.EntityType), StringComparer.Ordinal).ThenBy(item => Identity(item.ConfigurationType), StringComparer.Ordinal)];
    }

    private static IReadOnlyList<EfQuerySiteFact> OrderQuerySites(IReadOnlyList<EfQuerySiteFact> items)
    {
        return [.. items.OrderBy(item => item.Path, StringComparer.Ordinal).ThenBy(item => item.Line).ThenBy(item => item.Column)];
    }

    private static IReadOnlyList<PackageReferenceFact> OrderPackageReferences(IReadOnlyList<PackageReferenceFact> items)
    {
        return [.. items.OrderBy(item => item.Project.Path, StringComparer.Ordinal).ThenBy(item => item.Name, StringComparer.Ordinal)];
    }

    private static IReadOnlyList<PackageSourceUsageFact> OrderPackageUsages(IReadOnlyList<PackageSourceUsageFact> items)
    {
        return [.. items.OrderBy(item => item.Project.Path, StringComparer.Ordinal).ThenBy(item => item.Path, StringComparer.Ordinal).ThenBy(item => item.Line).ThenBy(item => item.Column)];
    }

    private sealed record DomainScan(
        IReadOnlyList<RouteEndpointFact> Routes,
        IReadOnlyList<OptionTypeFact> Options,
        IReadOnlyList<OptionBindingFact> OptionBindings,
        IReadOnlyList<OptionConsumerFact> OptionConsumers,
        IReadOnlyList<OptionValidationFact> OptionValidations,
        IReadOnlyList<MessageHandlerFact> MessageHandlers,
        IReadOnlyList<MessageCallSiteFact> MessageCallSites,
        IReadOnlyList<EfDbContextFact> DbContexts,
        IReadOnlyList<EfEntityFact> Entities,
        IReadOnlyList<EfDbSetFact> DbSets,
        IReadOnlyList<EfConfigurationFact> EfConfigurations,
        IReadOnlyList<EfQuerySiteFact> QuerySites,
        IReadOnlyList<PackageReferenceFact> PackageReferences,
        IReadOnlyList<PackageSourceUsageFact> PackageUsages);
}

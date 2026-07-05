using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Immutable;
using Navlyn.GeneratedCode;
using Navlyn.Languages;
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
                .Where(SourceLanguageFacts.IsSupportedDocument)
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

                routes.AddRange(DiscoverRoutes(root, semanticModel, projectInfo, excludeGenerated, cancellationToken));
                DiscoverOptions(root, semanticModel, projectInfo, excludeGenerated, options, optionBindings, optionConsumers, optionValidations, cancellationToken);
                DiscoverMessages(root, semanticModel, projectInfo, excludeGenerated, messageHandlers, messageCallSites, cancellationToken);
                DiscoverEf(root, semanticModel, projectInfo, excludeGenerated, dbContexts, entities, dbSets, efConfigurations, querySites, knownEfEntities, cancellationToken);

                if (includePackageUsage && packageOptions is not null)
                {
                    packageUsages.AddRange(DiscoverPackageUsages(root, semanticModel, projectInfo, excludeGenerated, packageOptions, cancellationToken));
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
        bool excludeGenerated,
        CancellationToken cancellationToken)
    {
        foreach (INamedTypeSymbol typeSymbol in SourceLanguageFacts.EnumerateNamedTypes(root, semanticModel, cancellationToken)
            .Where(symbol => symbol.TypeKind.ToString() == "Class"))
        {
            if (!IsControllerType(typeSymbol))
            {
                continue;
            }

            IReadOnlyList<string> typeRoutes = GetRouteTemplates(typeSymbol.GetAttributes());
            foreach (IMethodSymbol methodSymbol in typeSymbol.GetMembers()
                .OfType<IMethodSymbol>()
                .Where(method => method.MethodKind == MethodKind.Ordinary)
                .Where(method => !method.IsImplicitlyDeclared)
                .Where(method => method.Locations.Any(location => location.IsInSource)))
            {
                if (SourceLanguageFacts.HasAttribute(methodSymbol, "NonAction"))
                {
                    continue;
                }

                if (methodSymbol.DeclaredAccessibility != Accessibility.Public || methodSymbol.IsStatic)
                {
                    continue;
                }

                IReadOnlyList<string> methods = GetHttpMethods(methodSymbol.GetAttributes());
                IReadOnlyList<string> methodRoutes = GetRouteTemplates(methodSymbol.GetAttributes());
                bool hasRouteEvidence = typeRoutes.Count > 0 || methodRoutes.Count > 0 || methods.Count > 0;
                if (!hasRouteEvidence && !typeSymbol.Name.EndsWith("Controller", StringComparison.Ordinal))
                {
                    continue;
                }

                SymbolSourceLocation? location = SymbolNavigationFacts.GetSourceLocations(methodSymbol, excludeGenerated).FirstOrDefault();
                if (location is null)
                {
                    continue;
                }

                IReadOnlyList<ApplicationDomainEvidence> evidence = CreateAttributeEvidence(typeSymbol.GetAttributes(), ["controller-route-or-auth"], excludeGenerated)
                    .Concat(CreateAttributeEvidence(methodSymbol.GetAttributes(), ["action-route-or-auth"], excludeGenerated))
                    .ToArray();

                yield return new RouteEndpointFact(
                    "controller-action",
                    methods.Count == 0 ? ["ANY"] : methods,
                    CombineRoutePatterns(typeRoutes, methodRoutes),
                    NormalizeRoutePattern(CombineRoutePatterns(typeRoutes, methodRoutes)),
                    CreateSymbol(methodSymbol, project.Name, excludeGenerated),
                    CreateAuth(typeSymbol.GetAttributes(), methodSymbol.GetAttributes(), excludeGenerated),
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

        foreach (IInvocationOperation invocation in SourceLanguageFacts.EnumerateInvocations(root, semanticModel, cancellationToken))
        {
            string? name = GetInvocationName(invocation);
            if (name is not ("MapGet" or "MapPost" or "MapPut" or "MapDelete" or "MapPatch" or "MapMethods" or "MapFallback" or "Map"))
            {
                continue;
            }

            IOperation? handlerOperation = SourceLanguageFacts.GetOrderedArguments(invocation).LastOrDefault()?.Value;
            ApplicationDomainSymbol? handler = CreateHandlerSymbol(handlerOperation, semanticModel, project.Name, excludeGenerated);
            SymbolSourceLocation? location = SymbolNavigationFacts.CreateSourceLocation((handlerOperation?.Syntax ?? invocation.Syntax).GetLocation(), excludeGenerated);
            if (handler is null || location is null)
            {
                continue;
            }

            string? routePattern = SourceLanguageFacts.GetStringArgument(invocation, 0);

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
                [CreateEvidence(invocation.Syntax.GetLocation(), "invocation", ["minimal-api-map-call"], excludeGenerated)],
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
        List<OptionValidationFact> validations,
        CancellationToken cancellationToken)
    {
        foreach (IInvocationOperation invocation in SourceLanguageFacts.EnumerateInvocations(root, semanticModel, cancellationToken))
        {
            string? name = GetInvocationName(invocation);
            if (name is not ("Configure" or "AddOptions" or "Bind" or "Validate" or "ValidateDataAnnotations" or "ValidateOnStart"))
            {
                continue;
            }

            INamedTypeSymbol? optionType = GetOptionsTypeFromInvocation(invocation);
            if (optionType is null)
            {
                continue;
            }

            ApplicationDomainSymbol optionSymbol = CreateSymbol(optionType, project.Name, excludeGenerated);
            options.Add(new OptionTypeFact(optionSymbol, project, "high", Ordered(["options-type"]), [CreateEvidence(invocation.Syntax.GetLocation(), "invocation", ["options-registration-or-binding"], excludeGenerated)]));

            SymbolSourceLocation? location = SymbolNavigationFacts.CreateSourceLocation(invocation.Syntax.GetLocation(), excludeGenerated);
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
                    [CreateEvidence(invocation.Syntax.GetLocation(), "invocation", ["options-binding"], excludeGenerated)]));
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
                    [CreateEvidence(invocation.Syntax.GetLocation(), "invocation", ["options-validation"], excludeGenerated)]));
            }
        }

        foreach (IMethodSymbol constructorSymbol in EnumerateConstructorSymbols(root, semanticModel, cancellationToken))
        {
            INamedTypeSymbol? consumerType = constructorSymbol.ContainingType;
            if (consumerType is null)
            {
                continue;
            }

            foreach (IParameterSymbol parameter in constructorSymbol.Parameters)
            {
                ITypeSymbol? parameterType = parameter.Type;
                INamedTypeSymbol? optionType = GetOptionsTypeArgument(parameterType);
                if (optionType is null)
                {
                    continue;
                }

                Location? parameterLocation = parameter.Locations.FirstOrDefault(location => location.IsInSource);
                if (parameterLocation is null)
                {
                    continue;
                }

                SymbolSourceLocation? location = SymbolNavigationFacts.CreateSourceLocation(parameterLocation, excludeGenerated);
                if (location is null)
                {
                    continue;
                }

                ApplicationDomainSymbol optionSymbol = CreateSymbol(optionType, project.Name, excludeGenerated);
                options.Add(new OptionTypeFact(optionSymbol, project, "high", Ordered(["options-type"]), [CreateEvidence(parameterLocation, "parameter", ["options-consumer"], excludeGenerated)]));
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
                    Ordered(["options-constructor-consumer", (parameterType?.Name ?? "options").ToLowerInvariant()]),
                    [CreateEvidence(parameterLocation, "parameter", ["options-constructor-consumer"], excludeGenerated)]));
            }
        }
    }

    private static void DiscoverMessages(
        SyntaxNode root,
        SemanticModel semanticModel,
        ApplicationDomainProject project,
        bool excludeGenerated,
        List<MessageHandlerFact> handlers,
        List<MessageCallSiteFact> callSites,
        CancellationToken cancellationToken)
    {
        foreach (INamedTypeSymbol typeSymbol in SourceLanguageFacts.EnumerateNamedTypes(root, semanticModel, cancellationToken)
            .Where(symbol => symbol.TypeKind.ToString() == "Class"))
        {
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
                    CreateSymbolEvidence(typeSymbol, "type", ["mediatr-handler-interface"], excludeGenerated)));
            }
        }

        foreach (IInvocationOperation invocation in SourceLanguageFacts.EnumerateInvocations(root, semanticModel, cancellationToken))
        {
            string? name = GetInvocationName(invocation);
            if (name is not ("Send" or "Publish"))
            {
                continue;
            }

            INamedTypeSymbol? messageType = SourceLanguageFacts.GetOrderedArguments(invocation)
                .Select(argument => GetMessageTypeFromArgument(argument, semanticModel))
                .OfType<INamedTypeSymbol>()
                .FirstOrDefault(messageType => !IsFrameworkType(messageType));
            if (messageType is null || IsFrameworkType(messageType))
            {
                continue;
            }

            SymbolSourceLocation? location = SymbolNavigationFacts.CreateSourceLocation(invocation.Syntax.GetLocation(), excludeGenerated);
            if (location is null)
            {
                continue;
            }

            ISymbol? containing = semanticModel.GetEnclosingSymbol(invocation.Syntax.SpanStart);
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
                [CreateEvidence(invocation.Syntax.GetLocation(), "invocation", ["mediatr-send-or-publish"], excludeGenerated)]));
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
        Dictionary<string, ApplicationDomainSymbol> knownEntities,
        CancellationToken cancellationToken)
    {
        foreach (INamedTypeSymbol typeSymbol in SourceLanguageFacts.EnumerateNamedTypes(root, semanticModel, cancellationToken)
            .Where(symbol => symbol.TypeKind.ToString() == "Class"))
        {
            if (InheritsTypeNamed(typeSymbol, "DbContext"))
            {
                ApplicationDomainSymbol contextSymbol = CreateSymbol(typeSymbol, project.Name, excludeGenerated);
                dbContexts.Add(new EfDbContextFact(contextSymbol, project, "high", Ordered(["ef-dbcontext-base-type"]), CreateSymbolEvidence(typeSymbol, "type", ["ef-dbcontext-base-type"], excludeGenerated)));

                foreach (IPropertySymbol propertySymbol in typeSymbol.GetMembers().OfType<IPropertySymbol>())
                {
                    if (propertySymbol.Type is not INamedTypeSymbol propertyType ||
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
                    IReadOnlyList<ApplicationDomainEvidence> propertyEvidence = CreateSymbolEvidence(propertySymbol, "property", ["ef-dbset-property"], excludeGenerated);
                    entities.Add(new EfEntityFact(entitySymbol, project, "high", Ordered(["ef-dbset-entity"]), propertyEvidence));
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
                        propertyEvidence));
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
                IReadOnlyList<ApplicationDomainEvidence> typeEvidence = CreateSymbolEvidence(typeSymbol, "type", ["ef-entitytypeconfiguration-interface"], excludeGenerated);
                entities.Add(new EfEntityFact(entitySymbol, project, "high", Ordered(["ef-configuration-entity"]), typeEvidence));
                configurations.Add(new EfConfigurationFact(
                    entitySymbol,
                    CreateSymbol(typeSymbol, project.Name, excludeGenerated),
                    project,
                    "high",
                    Ordered(["ef-entitytypeconfiguration-interface"]),
                    typeEvidence));
            }
        }

        foreach (IInvocationOperation invocation in SourceLanguageFacts.EnumerateInvocations(root, semanticModel, cancellationToken))
        {
            ITypeSymbol? expressionType = invocation.Instance?.Type ??
                invocation.Arguments.OrderBy(argument => argument.Syntax.SpanStart).FirstOrDefault()?.Value.Type;
            INamedTypeSymbol? entityType = GetQueryableEntityType(expressionType);
            if (entityType is null)
            {
                continue;
            }

            SymbolSourceLocation? location = SymbolNavigationFacts.CreateSourceLocation(invocation.Syntax.GetLocation(), excludeGenerated);
            if (location is null)
            {
                continue;
            }

            ApplicationDomainSymbol entitySymbol = CreateSymbol(entityType, project.Name, excludeGenerated);
            knownEntities.TryAdd(Identity(entitySymbol), entitySymbol);
            ISymbol? containing = semanticModel.GetEnclosingSymbol(invocation.Syntax.SpanStart);
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
                [CreateEvidence(invocation.Syntax.GetLocation(), "invocation", ["ef-queryable-call"], excludeGenerated)]));
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
        PackageUsageOptions options,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> hints = options.NamespaceHints.Count == 0
            ? GuessPackageNamespaceHints(options.Package)
            : options.NamespaceHints;
        if (hints.Count == 0)
        {
            yield break;
        }

        foreach (SyntaxNode import in root.DescendantNodes().Where(IsImportNode))
        {
            string namespaceName = GetImportedNamespace(import) ?? "";
            if (!MatchesAnyHint(namespaceName, hints))
            {
                continue;
            }

            SymbolSourceLocation? location = SymbolNavigationFacts.CreateSourceLocation(import.GetLocation(), excludeGenerated);
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
                [CreateEvidence(import.GetLocation(), "using", ["package-source-usage"], excludeGenerated)]);
        }

        foreach (SyntaxNode node in root.DescendantNodes().Where(IsSymbolReferenceCandidate))
        {
            cancellationToken.ThrowIfCancellationRequested();
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

    private static ApplicationDomainSymbol? CreateHandlerSymbol(IOperation? operation, SemanticModel semanticModel, string projectName, bool excludeGenerated)
    {
        if (operation is null)
        {
            return null;
        }

        if (SourceLanguageFacts.FindOperation<IAnonymousFunctionOperation>(operation) is IAnonymousFunctionOperation lambda)
        {
            SymbolSourceLocation? location = SymbolNavigationFacts.CreateSourceLocation(lambda.Syntax.GetLocation(), excludeGenerated);
            ISymbol? enclosing = semanticModel.GetEnclosingSymbol(lambda.Syntax.SpanStart);
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

        SymbolInfo info = semanticModel.GetSymbolInfo(operation.Syntax);
        ISymbol? symbol = info.Symbol ??
            SourceLanguageFacts.FindOperation<IMethodReferenceOperation>(operation)?.Method ??
            info.CandidateSymbols.OrderBy(symbol => symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), StringComparer.Ordinal).FirstOrDefault();
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

    private static IReadOnlyList<ApplicationDomainEvidence> CreateSymbolEvidence(ISymbol symbol, string kind, IReadOnlyList<string> reasons, bool excludeGenerated)
    {
        return [.. symbol.Locations
            .Where(location => location.IsInSource)
            .Take(1)
            .Select(location => CreateEvidence(location, kind, reasons, excludeGenerated))];
    }

    private static IReadOnlyList<ApplicationDomainEvidence> CreateAttributeEvidence(IEnumerable<AttributeData> attributes, IReadOnlyList<string> reasons, bool excludeGenerated)
    {
        return [.. attributes
            .Select(attribute => attribute.ApplicationSyntaxReference?.GetSyntax())
            .Where(node => node is not null)
            .Select(node => CreateEvidence(node!.GetLocation(), "attribute", reasons, excludeGenerated))];
    }

    private static RouteAuthFact CreateAuth(IEnumerable<AttributeData> typeAttributes, IEnumerable<AttributeData> methodAttributes, bool excludeGenerated)
    {
        return CreateAuthFromAttributes([.. typeAttributes.Concat(methodAttributes)], excludeGenerated);
    }

    private static RouteAuthFact CreateMinimalApiAuth(IInvocationOperation mapInvocation, bool excludeGenerated)
    {
        List<ApplicationDomainEvidence> evidence = [];
        bool required = false;
        bool anonymous = false;

        SyntaxNode? node = mapInvocation.Syntax.Parent;
        while (node is not null)
        {
            if (node.GetType().Name == "InvocationExpressionSyntax")
            {
                string text = node.ToString();
                if (LooksLikeInvocationName(text, "RequireAuthorization"))
                {
                    required = true;
                    evidence.Add(CreateEvidence(node.GetLocation(), "invocation", ["requireauthorization-call"], excludeGenerated));
                }
                else if (LooksLikeInvocationName(text, "AllowAnonymous"))
                {
                    anonymous = true;
                    evidence.Add(CreateEvidence(node.GetLocation(), "invocation", ["allowanonymous-call"], excludeGenerated));
                }
            }

            node = node.Parent is not null && (node.Parent.GetType().Name is "MemberAccessExpressionSyntax" or "InvocationExpressionSyntax")
                ? node.Parent
                : null;
        }

        if (anonymous)
        {
            return new RouteAuthFact("anonymous", [], [], [], evidence);
        }

        if (required)
        {
            return new RouteAuthFact("required", [], [], [], evidence);
        }

        return new RouteAuthFact("unknown", [], [], [], []);
    }

    private static RouteAuthFact CreateAuthFromAttributes(IReadOnlyList<AttributeData> attributes, bool excludeGenerated)
    {
        bool anonymous = attributes.Any(attribute => SourceLanguageFacts.ShortAttributeName(attribute) == "AllowAnonymous");
        IReadOnlyList<AttributeData> authorize = [.. attributes.Where(attribute => SourceLanguageFacts.ShortAttributeName(attribute) == "Authorize")];
        if (anonymous)
        {
            return new RouteAuthFact(
                "anonymous",
                [],
                [],
                [],
                CreateAttributeEvidence([.. attributes.Where(attribute => SourceLanguageFacts.ShortAttributeName(attribute) == "AllowAnonymous")], ["allowanonymous-attribute"], excludeGenerated));
        }

        return authorize.Count == 0
            ? new RouteAuthFact("unknown", [], [], [], [])
            : new RouteAuthFact(
                "required",
                [.. authorize.Select(attribute => SourceLanguageFacts.GetNamedStringArgument(attribute, "Policy") ?? SourceLanguageFacts.GetFirstStringArgument(attribute)).Where(value => !string.IsNullOrWhiteSpace(value))!],
                [.. authorize.Select(attribute => SourceLanguageFacts.GetNamedStringArgument(attribute, "Roles")).Where(value => !string.IsNullOrWhiteSpace(value))!],
                [.. authorize.Select(attribute => SourceLanguageFacts.GetNamedStringArgument(attribute, "AuthenticationSchemes")).Where(value => !string.IsNullOrWhiteSpace(value))!],
                CreateAttributeEvidence(authorize, ["authorize-attribute"], excludeGenerated));
    }

    private static IReadOnlyList<string> GetHttpMethods(IEnumerable<AttributeData> attributes)
    {
        return [.. attributes
            .Select(SourceLanguageFacts.ShortAttributeName)
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

    private static IReadOnlyList<string> GetMinimalApiHttpMethods(string name, IInvocationOperation invocation)
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

    private static IReadOnlyList<string> GetMapMethods(IInvocationOperation invocation)
    {
        foreach (IArgumentOperation argument in SourceLanguageFacts.GetOrderedArguments(invocation))
        {
            if (argument.Value.Type?.SpecialType == SpecialType.System_String)
            {
                continue;
            }

            IReadOnlyList<string> methods = SourceLanguageFacts.GetStringArguments(argument);
            if (methods.Count > 0)
            {
                return methods;
            }
        }

        return ["ANY"];
    }

    private static IReadOnlyList<string> GetRouteTemplates(IEnumerable<AttributeData> attributes)
    {
        return [.. attributes
            .Where(attribute => IsRouteAttribute(attribute) || IsHttpMethodAttribute(attribute))
            .Select(SourceLanguageFacts.GetFirstStringArgument)
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

    private static bool IsRouteAttribute(AttributeData attribute)
    {
        return SourceLanguageFacts.ShortAttributeName(attribute) == "Route";
    }

    private static bool IsHttpMethodAttribute(AttributeData attribute)
    {
        return SourceLanguageFacts.ShortAttributeName(attribute) is "HttpGet" or "HttpPost" or "HttpPut" or "HttpDelete" or "HttpPatch" or "HttpHead" or "HttpOptions";
    }

    private static bool IsControllerType(INamedTypeSymbol symbol)
    {
        return symbol.Name.EndsWith("Controller", StringComparison.Ordinal) ||
            SourceLanguageFacts.HasAttribute(symbol, "ApiController") ||
            InheritsTypeNamed(symbol, "ControllerBase") ||
            InheritsTypeNamed(symbol, "Controller");
    }

    private static INamedTypeSymbol? GetOptionsTypeFromInvocation(IInvocationOperation invocation)
    {
        return GetOptionsTypeFromInvocation(invocation, includeNestedInvocations: true);
    }

    private static INamedTypeSymbol? GetOptionsTypeFromInvocation(IInvocationOperation invocation, bool includeNestedInvocations)
    {
        IReadOnlyList<ITypeSymbol> genericTypes = GetGenericTypeArguments(invocation);
        if (genericTypes.FirstOrDefault() is INamedTypeSymbol genericType)
        {
            return genericType;
        }

        INamedTypeSymbol? instanceOptionType = GetOptionsTypeArgument(invocation.Instance?.Type);
        if (instanceOptionType is not null)
        {
            return instanceOptionType;
        }

        INamedTypeSymbol? argumentOptionType = SourceLanguageFacts.GetOrderedArguments(invocation)
            .Select(argument => GetOptionsTypeArgument(argument.Value.Type))
            .FirstOrDefault(type => type is not null);
        if (argumentOptionType is not null)
        {
            return argumentOptionType;
        }

        if (!includeNestedInvocations)
        {
            return null;
        }

        return EnumerateInvocationOperations(invocation)
            .Where(nested => !ReferenceEquals(nested, invocation))
            .Select(nested => GetOptionsTypeFromInvocation(nested, includeNestedInvocations: false))
            .FirstOrDefault(type => type is not null);
    }

    private static INamedTypeSymbol? GetMessageTypeFromArgument(IArgumentOperation argument, SemanticModel semanticModel)
    {
        foreach (INamedTypeSymbol candidate in GetMessageTypeCandidates(argument, semanticModel))
        {
            if (candidate.Name is "ISender" or "IPublisher" or "IRequest" or "INotification" or "IRequestHandler" or "INotificationHandler" ||
                IsFrameworkType(candidate))
            {
                continue;
            }

            return candidate;
        }

        return null;
    }

    private static IEnumerable<INamedTypeSymbol> GetMessageTypeCandidates(IArgumentOperation argument, SemanticModel semanticModel)
    {
        if (SourceLanguageFacts.FindOperation<IObjectCreationOperation>(argument.Value)?.Type is INamedTypeSymbol createdType)
        {
            yield return createdType;
        }

        if (argument.Value is IConversionOperation conversion && conversion.Operand.Type is INamedTypeSymbol operandType)
        {
            yield return operandType;
        }

        TypeInfo typeInfo = semanticModel.GetTypeInfo(argument.Value.Syntax);
        if (typeInfo.Type is INamedTypeSymbol syntaxType)
        {
            yield return syntaxType;
        }

        if (argument.Value.Type is INamedTypeSymbol valueType)
        {
            yield return valueType;
        }

        if (typeInfo.ConvertedType is INamedTypeSymbol convertedType)
        {
            yield return convertedType;
        }
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

    private static string? FindConfigurationKey(IInvocationOperation invocation)
    {
        foreach (IInvocationOperation nested in EnumerateInvocationOperations(invocation))
        {
            if (GetInvocationName(nested) == "GetSection")
            {
                return SourceLanguageFacts.GetStringArgument(nested, 0);
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

    private static IReadOnlyList<ITypeSymbol> GetGenericTypeArguments(IInvocationOperation invocation)
    {
        return [.. invocation.TargetMethod.TypeArguments];
    }

    private static string? GetInvocationName(IInvocationOperation invocation)
    {
        return invocation.TargetMethod.Name;
    }

    private static IEnumerable<IInvocationOperation> EnumerateInvocationOperations(IOperation operation)
    {
        if (operation is IInvocationOperation invocation)
        {
            yield return invocation;
        }

        foreach (IOperation child in operation.ChildOperations)
        {
            foreach (IInvocationOperation nested in EnumerateInvocationOperations(child))
            {
                yield return nested;
            }
        }
    }

    private static IEnumerable<IMethodSymbol> EnumerateConstructorSymbols(
        SyntaxNode root,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (IMethodSymbol constructor in SourceLanguageFacts.EnumerateMethods(root, semanticModel, cancellationToken)
            .Where(method => method.MethodKind == MethodKind.Constructor))
        {
            if (seen.Add(ConstructorIdentity(constructor)))
            {
                yield return constructor;
            }
        }

        foreach (INamedTypeSymbol type in SourceLanguageFacts.EnumerateNamedTypes(root, semanticModel, cancellationToken))
        {
            foreach (IMethodSymbol constructor in type.InstanceConstructors
                .Where(constructor => constructor.Parameters.Length > 0)
                .Where(constructor => constructor.Locations.Any(location => location.IsInSource) ||
                    constructor.Parameters.Any(parameter => parameter.Locations.Any(location => location.IsInSource)))
                .OrderBy(constructor => GetSymbolLocation(constructor)?.SourceTree?.FilePath, StringComparer.Ordinal)
                .ThenBy(constructor => GetSymbolLocation(constructor)?.SourceSpan.Start ?? int.MaxValue))
            {
                if (seen.Add(ConstructorIdentity(constructor)))
                {
                    yield return constructor;
                }
            }
        }
    }

    private static string ConstructorIdentity(IMethodSymbol constructor)
    {
        Location? location = GetSymbolLocation(constructor);
        string parameters = string.Join(",", constructor.Parameters.Select(parameter => parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
        return string.Join(
            ":",
            constructor.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            constructor.MethodKind.ToString(),
            parameters,
            location?.SourceTree?.FilePath,
            location?.SourceSpan.Start.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static Location? GetSymbolLocation(IMethodSymbol symbol)
    {
        return symbol.Locations.FirstOrDefault(location => location.IsInSource) ??
            symbol.Parameters.SelectMany(parameter => parameter.Locations).FirstOrDefault(location => location.IsInSource);
    }

    private static bool LooksLikeInvocationName(string expressionText, string expectedName)
    {
        string trimmed = expressionText.TrimStart();
        return trimmed.StartsWith(expectedName + "(", StringComparison.Ordinal) ||
            trimmed.Contains("." + expectedName + "(", StringComparison.Ordinal);
    }

    private static bool IsImportNode(SyntaxNode node)
    {
        return node.GetType().Name is "UsingDirectiveSyntax" or "SimpleImportsClauseSyntax";
    }

    private static string? GetImportedNamespace(SyntaxNode node)
    {
        object? name = node.GetType().GetProperty("Name")?.GetValue(node);
        return name?.ToString();
    }

    private static bool IsSymbolReferenceCandidate(SyntaxNode node)
    {
        return node.GetType().Name is "IdentifierNameSyntax" or "QualifiedNameSyntax" or "MemberAccessExpressionSyntax" or "SimpleNameSyntax";
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

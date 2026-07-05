using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Navlyn.GeneratedCode;
using Navlyn.Languages;
using Navlyn.Paths;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.DependencyInjection;

internal sealed class DiRegistrationResolver
{
    public async Task<DiGraphResolution> ResolveAsync(
        LoadedWorkspace workspace,
        IReadOnlyList<Project> projects,
        DiGraphOptions options,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<RegistrationWithSymbols> registrations = await DiscoverRegistrationsAsync(projects, options, cancellationToken);
        IReadOnlyList<DiDependencyEdge> dependencies = DiscoverDependencies(registrations, options.DependencyLimit);
        IReadOnlyList<DiRiskFact> risks = options.IncludeRisks ? DiscoverRisks(registrations, dependencies, options.RiskLimit) : [];
        IReadOnlyList<string> warnings = registrations.Count == 0 ? ["no-di-registrations-found"] : [];
        _ = workspace;
        return new DiGraphResolution(
            Registrations: [.. registrations.Select(registration => registration.Item)],
            Dependencies: dependencies,
            Risks: risks,
            Warnings: warnings);
    }

    public static DiRegistrationsSection CreateRegistrationsSection(IReadOnlyList<DiRegistrationItem> registrations, int limit)
    {
        IReadOnlyList<DiRegistrationItem> ordered = OrderRegistrations(registrations);
        return new DiRegistrationsSection(ordered.Count, limit, ordered.Count > limit, [.. ordered.Take(limit)]);
    }

    public static DiDependenciesSection CreateDependenciesSection(IReadOnlyList<DiDependencyEdge> dependencies, int limit)
    {
        IReadOnlyList<DiDependencyEdge> ordered = [.. dependencies
            .OrderBy(edge => Identity(edge.ImplementationType), StringComparer.Ordinal)
            .ThenBy(edge => edge.ParameterOrdinal)
            .ThenBy(edge => Identity(edge.DependencyType), StringComparer.Ordinal)
            .ThenBy(edge => edge.ParameterName, StringComparer.Ordinal)];
        return new DiDependenciesSection(ordered.Count, limit, ordered.Count > limit, [.. ordered.Take(limit)]);
    }

    public static DiRisksSection CreateRisksSection(IReadOnlyList<DiRiskFact> risks, int limit)
    {
        IReadOnlyList<DiRiskFact> ordered = [.. risks
            .OrderBy(risk => SeverityPriority(risk.Severity))
            .ThenBy(risk => ConfidencePriority(risk.Confidence))
            .ThenBy(risk => risk.RiskKind, StringComparer.Ordinal)
            .ThenBy(risk => Identity(risk.ServiceType), StringComparer.Ordinal)
            .ThenBy(risk => Identity(risk.ImplementationType), StringComparer.Ordinal)
            .ThenBy(risk => Identity(risk.DependencyType), StringComparer.Ordinal)];
        return new DiRisksSection(ordered.Count, limit, ordered.Count > limit, [.. ordered.Take(limit)]);
    }

    public static bool Matches(DiTypeInfo? candidate, DiTypeInfo subject)
    {
        return candidate is not null && Identity(candidate) == Identity(subject);
    }

    public static string Identity(DiTypeInfo? type)
    {
        return type?.Facts.DocumentationCommentId ??
            type?.Facts.FullyQualifiedName ??
            type?.Facts.DisplayName ??
            type?.Name ??
            "";
    }

    private static async Task<IReadOnlyList<RegistrationWithSymbols>> DiscoverRegistrationsAsync(
        IReadOnlyList<Project> projects,
        DiGraphOptions options,
        CancellationToken cancellationToken)
    {
        List<RegistrationWithSymbols> registrations = [];

        foreach (Project project in projects.OrderBy(project => project.FilePath, StringComparer.Ordinal).ThenBy(project => project.Name, StringComparer.Ordinal))
        {
            DiProjectInfo projectInfo = new(
                project.Name,
                project.FilePath is null ? null : PathDisplay.FromCurrentDirectory(project.FilePath),
                ProjectContextFacts.GetTargetFramework(project));

            foreach (Document document in project.Documents
                .Where(document => document.FilePath is not null)
                .Where(SourceLanguageFacts.IsSupportedDocument)
                .Where(document => !options.ExcludeGenerated || !GeneratedCodeFacts.IsGeneratedPath(document.FilePath))
                .OrderBy(document => document.FilePath, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken);
                SemanticModel? semanticModel = await document.GetSemanticModelAsync(cancellationToken);
                if (root is null || semanticModel is null)
                {
                    continue;
                }

                foreach (IInvocationOperation invocation in EnumerateInvocations(root, semanticModel, cancellationToken))
                {
                    RegistrationWithSymbols? registration = TryCreateRegistration(invocation, projectInfo, options);
                    if (registration is not null)
                    {
                        registrations.Add(registration);
                    }
                }
            }
        }

        return [.. registrations
            .GroupBy(registration => (
                registration.Item.Project.Path,
                registration.Item.Path,
                registration.Item.Line,
                registration.Item.Column,
                registration.Item.Lifetime,
                Service: Identity(registration.Item.ServiceType),
                Implementation: Identity(registration.Item.ImplementationType),
                registration.Item.RegistrationKind))
            .Select(group => group.First())];
    }

    private static IReadOnlyList<IInvocationOperation> EnumerateInvocations(
        SyntaxNode root,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        List<IInvocationOperation> invocations = [];
        foreach (SyntaxNode node in root.DescendantNodesAndSelf())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (semanticModel.GetOperation(node, cancellationToken) is IInvocationOperation invocation &&
                invocation.Syntax == node)
            {
                invocations.Add(invocation);
            }
        }

        return [.. invocations
            .GroupBy(invocation => (invocation.Syntax.SyntaxTree.FilePath, invocation.Syntax.SpanStart, invocation.Syntax.Span.End))
            .Select(group => group.First())
            .OrderBy(invocation => invocation.Syntax.SyntaxTree.FilePath, StringComparer.Ordinal)
            .ThenBy(invocation => invocation.Syntax.SpanStart)];
    }

    private static RegistrationWithSymbols? TryCreateRegistration(
        IInvocationOperation invocation,
        DiProjectInfo project,
        DiGraphOptions options)
    {
        string? name = GetInvocationName(invocation);
        if (name is null)
        {
            return null;
        }

        if (name == "Add" && GetInvocationArgument(invocation, 0) is IInvocationOperation descriptorInvocation)
        {
            return TryCreateServiceDescriptorRegistration(invocation, descriptorInvocation, project, options);
        }

        string lifetime = LifetimeFromMethodName(name);
        bool tryAdd = name.StartsWith("TryAdd", StringComparison.Ordinal);
        if (lifetime == "unknown" && name is not ("AddHostedService" or "Configure" or "AddOptions"))
        {
            return null;
        }

        if (name == "AddHostedService" && !options.IncludeHostedServices)
        {
            return null;
        }

        if (name is "Configure" or "AddOptions" && !options.IncludeOptions)
        {
            return null;
        }

        IReadOnlyList<ITypeSymbol> genericTypes = GetGenericTypeArguments(invocation);
        List<string> reasons = ["iservicecollection-registration-call"];
        string registrationKind = "service";
        INamedTypeSymbol? serviceType = null;
        INamedTypeSymbol? implementationType = null;
        DiFactoryInfo? factory = null;
        DiInstanceInfo? instance = null;

        if (name == "AddHostedService")
        {
            registrationKind = "hostedService";
            lifetime = "singleton";
            implementationType = genericTypes.OfType<INamedTypeSymbol>().FirstOrDefault();
            serviceType = implementationType;
            reasons.Add("hosted-service-registration");
        }
        else if (name is "Configure" or "AddOptions")
        {
            registrationKind = "options";
            lifetime = "unknown";
            serviceType = genericTypes.OfType<INamedTypeSymbol>().FirstOrDefault();
            reasons.Add("options-registration");
        }
        else
        {
            reasons.Add($"{name.ToLowerInvariant()}-call");
            if (tryAdd)
            {
                reasons.Add("tryadd-call");
            }

            (serviceType, implementationType) = ResolveRegistrationTypes(invocation, genericTypes);
            IArgumentOperation? factoryArgument = invocation.Arguments.FirstOrDefault(argument => FindOperation<IAnonymousFunctionOperation>(argument.Value) is not null);
            if (factoryArgument is not null)
            {
                factory = new DiFactoryInfo("lambda", [CreateEvidence(factoryArgument.Syntax.GetLocation(), "factory", ["factory-registration"])]);
                reasons.Add("factory-registration");
            }

            if (implementationType is null &&
                invocation.Arguments.FirstOrDefault(argument =>
                    FindOperation<ITypeOfOperation>(argument.Value) is null &&
                    FindOperation<IAnonymousFunctionOperation>(argument.Value) is null) is { } instanceArgument)
            {
                ITypeSymbol? instanceType = instanceArgument.Value.Type;
                instance = new DiInstanceInfo(instanceType is null ? null : CreateTypeInfo(instanceType, project.Name, options.ExcludeGenerated), [CreateEvidence(instanceArgument.Syntax.GetLocation(), "instance", ["instance-registration"])]);
                reasons.Add("instance-registration");
            }
        }

        if (serviceType is null && implementationType is null)
        {
            return null;
        }

        SymbolSourceLocation? location = SymbolNavigationFacts.CreateSourceLocation(invocation.Syntax.GetLocation(), options.ExcludeGenerated);
        if (location is null)
        {
            return null;
        }

        DiRegistrationItem item = new(
            RegistrationKind: registrationKind,
            Lifetime: lifetime,
            ServiceType: serviceType is null ? null : CreateTypeInfo(serviceType, project.Name, options.ExcludeGenerated),
            ImplementationType: implementationType is null ? null : CreateTypeInfo(implementationType, project.Name, options.ExcludeGenerated),
            Factory: factory,
            Instance: instance,
            Path: location.Path,
            Line: location.Line,
            Column: location.Column,
            EndLine: location.EndLine,
            EndColumn: location.EndColumn,
            Project: project,
            Confidence: "high",
            ReasonCodes: OrderedReasons(reasons),
            Evidence: [CreateEvidence(invocation.Syntax.GetLocation(), "invocation", OrderedReasons(reasons))],
            Snippet: options.IncludeSnippets ? FuzzySnippetReader.TryRead(location.Path, location.Line, options.SnippetLines) : null);

        return new RegistrationWithSymbols(item, serviceType, implementationType);
    }

    private static RegistrationWithSymbols? TryCreateServiceDescriptorRegistration(
        IInvocationOperation outerInvocation,
        IInvocationOperation descriptorInvocation,
        DiProjectInfo project,
        DiGraphOptions options)
    {
        string? descriptorName = GetInvocationName(descriptorInvocation);
        string lifetime = LifetimeFromMethodName(descriptorName ?? "");
        if (lifetime == "unknown")
        {
            return null;
        }

        IReadOnlyList<ITypeSymbol> genericTypes = GetGenericTypeArguments(descriptorInvocation);
        (INamedTypeSymbol? serviceType, INamedTypeSymbol? implementationType) = ResolveRegistrationTypes(descriptorInvocation, genericTypes);
        if (serviceType is null && implementationType is null)
        {
            return null;
        }

        SymbolSourceLocation? location = SymbolNavigationFacts.CreateSourceLocation(outerInvocation.Syntax.GetLocation(), options.ExcludeGenerated);
        if (location is null)
        {
            return null;
        }

        List<string> reasons = ["iservicecollection-registration-call", "servicedescriptor-call", $"{descriptorName!.ToLowerInvariant()}-call"];
        DiRegistrationItem item = new(
            RegistrationKind: "service",
            Lifetime: lifetime,
            ServiceType: serviceType is null ? null : CreateTypeInfo(serviceType, project.Name, options.ExcludeGenerated),
            ImplementationType: implementationType is null ? null : CreateTypeInfo(implementationType, project.Name, options.ExcludeGenerated),
            Factory: null,
            Instance: null,
            Path: location.Path,
            Line: location.Line,
            Column: location.Column,
            EndLine: location.EndLine,
            EndColumn: location.EndColumn,
            Project: project,
            Confidence: "high",
            ReasonCodes: OrderedReasons(reasons),
            Evidence: [CreateEvidence(descriptorInvocation.Syntax.GetLocation(), "invocation", ["servicedescriptor-call"])],
            Snippet: options.IncludeSnippets ? FuzzySnippetReader.TryRead(location.Path, location.Line, options.SnippetLines) : null);
        return new RegistrationWithSymbols(item, serviceType, implementationType);
    }

    private static IReadOnlyList<DiDependencyEdge> DiscoverDependencies(
        IReadOnlyList<RegistrationWithSymbols> registrations,
        int limit)
    {
        Dictionary<string, INamedTypeSymbol> implementationSymbols = new(StringComparer.Ordinal);
        Dictionary<string, DiTypeInfo> implementationInfos = new(StringComparer.Ordinal);
        foreach (RegistrationWithSymbols registration in registrations)
        {
            if (registration.ImplementationSymbol is null || registration.Item.ImplementationType is null)
            {
                continue;
            }

            string identity = Identity(registration.Item.ImplementationType);
            implementationSymbols.TryAdd(identity, registration.ImplementationSymbol);
            implementationInfos.TryAdd(identity, registration.Item.ImplementationType);
        }

        List<DiDependencyEdge> dependencies = [];
        foreach ((string identity, INamedTypeSymbol symbol) in implementationSymbols.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            IMethodSymbol? constructor = SelectConstructor(symbol);
            if (constructor is null || !implementationInfos.TryGetValue(identity, out DiTypeInfo? implementationInfo))
            {
                continue;
            }

            List<string> constructorReasons = ["constructor-parameter"];
            if (symbol.InstanceConstructors.Count(constructor => constructor.DeclaredAccessibility == Accessibility.Public) > 1)
            {
                constructorReasons.Add("constructor-selection-heuristic");
            }

            foreach (IParameterSymbol parameter in constructor.Parameters.OrderBy(parameter => parameter.Ordinal))
            {
                DiTypeInfo dependency = CreateTypeInfo(parameter.Type, implementationInfo.Facts.Project, excludeGenerated: false);
                dependencies.Add(new DiDependencyEdge(
                    ImplementationType: implementationInfo,
                    DependencyType: dependency,
                    ParameterName: parameter.Name,
                    ParameterOrdinal: parameter.Ordinal,
                    IsOptional: parameter.IsOptional || parameter.HasExplicitDefaultValue || parameter.NullableAnnotation == NullableAnnotation.Annotated,
                    IsEnumerable: IsNamedType(parameter.Type, "IEnumerable"),
                    IsFactoryLike: IsNamedType(parameter.Type, "Func") || IsNamedType(parameter.Type, "Lazy") || IsNamedType(parameter.Type, "IServiceProvider"),
                    Confidence: "high",
                    ReasonCodes: OrderedReasons(constructorReasons),
                    Evidence: CreateConstructorEvidence(constructor)));
            }
        }

        _ = limit;
        return [.. dependencies
            .GroupBy(edge => (Implementation: Identity(edge.ImplementationType), Dependency: Identity(edge.DependencyType), edge.ParameterName, edge.ParameterOrdinal))
            .Select(group => group.First())];
    }

    private static IReadOnlyList<DiRiskFact> DiscoverRisks(
        IReadOnlyList<RegistrationWithSymbols> registrations,
        IReadOnlyList<DiDependencyEdge> dependencies,
        int riskLimit)
    {
        List<DiRiskFact> risks = [];
        Dictionary<string, List<DiRegistrationItem>> byService = registrations
            .Select(registration => registration.Item)
            .Where(item => item.ServiceType is not null)
            .GroupBy(item => Identity(item.ServiceType), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        foreach (List<DiRegistrationItem> group in byService.Values.Where(group => group.Count > 1))
        {
            DiRegistrationItem first = OrderRegistrations(group).First();
            risks.Add(new DiRiskFact(
                RiskKind: "multiple-registrations",
                Severity: "info",
                Confidence: "high",
                Claim: "Service type has multiple source registrations.",
                ServiceType: first.ServiceType,
                ImplementationType: null,
                DependencyType: null,
                ReasonCodes: ["multiple-registrations"],
                Evidence: [.. group.Select(item => CreateEvidence(item.Path, item.Line, item.Column, item.EndLine, item.EndColumn, "registration", ["multiple-registrations"]))]));
        }

        Dictionary<string, DiRegistrationItem> registrationByImplementation = registrations
            .Select(registration => registration.Item)
            .Where(item => item.ImplementationType is not null)
            .GroupBy(item => Identity(item.ImplementationType), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        HashSet<string> registeredServices = new(byService.Keys, StringComparer.Ordinal);
        registeredServices.UnionWith(registrations.Select(registration => Identity(registration.Item.ImplementationType)).Where(identity => identity.Length > 0));

        foreach (DiDependencyEdge dependency in dependencies)
        {
            string dependencyIdentity = Identity(dependency.DependencyType);
            if (registrationByImplementation.TryGetValue(Identity(dependency.ImplementationType), out DiRegistrationItem? implementationRegistration) &&
                implementationRegistration.Lifetime == "singleton" &&
                TryFindRegistration(byService, dependencyIdentity, out DiRegistrationItem? dependencyRegistration) &&
                dependencyRegistration.Lifetime == "scoped")
            {
                risks.Add(new DiRiskFact(
                    RiskKind: "captive-dependency",
                    Severity: "warning",
                    Confidence: "medium",
                    Claim: "Singleton service depends on scoped service.",
                    ServiceType: implementationRegistration.ServiceType,
                    ImplementationType: implementationRegistration.ImplementationType,
                    DependencyType: dependency.DependencyType,
                    ReasonCodes: ["singleton-depends-on-scoped"],
                    Evidence: dependency.Evidence));
            }

            if (!registeredServices.Contains(dependencyIdentity) && !IsKnownFrameworkService(dependency.DependencyType))
            {
                risks.Add(new DiRiskFact(
                    RiskKind: "unresolved-service-candidate",
                    Severity: "warning",
                    Confidence: "medium",
                    Claim: "Constructor dependency has no matching source registration.",
                    ServiceType: null,
                    ImplementationType: dependency.ImplementationType,
                    DependencyType: dependency.DependencyType,
                    ReasonCodes: ["unresolved-service-candidate"],
                    Evidence: dependency.Evidence));
            }
        }

        _ = riskLimit;
        return risks;
    }

    private static bool TryFindRegistration(Dictionary<string, List<DiRegistrationItem>> byService, string identity, out DiRegistrationItem registration)
    {
        if (byService.TryGetValue(identity, out List<DiRegistrationItem>? registrations))
        {
            registration = registrations.First();
            return true;
        }

        registration = null!;
        return false;
    }

    private static (INamedTypeSymbol? ServiceType, INamedTypeSymbol? ImplementationType) ResolveRegistrationTypes(
        IInvocationOperation invocation,
        IReadOnlyList<ITypeSymbol> genericTypes)
    {
        INamedTypeSymbol? serviceType = genericTypes.ElementAtOrDefault(0) as INamedTypeSymbol;
        INamedTypeSymbol? implementationType = genericTypes.ElementAtOrDefault(1) as INamedTypeSymbol;

        IReadOnlyList<INamedTypeSymbol> typeofTypes = [.. invocation.Arguments
            .Select(argument => FindOperation<ITypeOfOperation>(argument.Value)?.TypeOperand)
            .OfType<INamedTypeSymbol>()];
        serviceType ??= typeofTypes.ElementAtOrDefault(0);
        implementationType ??= typeofTypes.ElementAtOrDefault(1);

        if (serviceType is not null && implementationType is null && genericTypes.Count == 1)
        {
            implementationType = serviceType;
        }

        return (serviceType, implementationType);
    }

    private static IReadOnlyList<ITypeSymbol> GetGenericTypeArguments(IInvocationOperation invocation)
    {
        return [.. invocation.TargetMethod.TypeArguments];
    }

    private static string? GetInvocationName(IInvocationOperation invocation)
    {
        return invocation.TargetMethod.Name;
    }

    private static IInvocationOperation? GetInvocationArgument(IInvocationOperation invocation, int index)
    {
        IArgumentOperation? argument = invocation.Arguments
            .OrderBy(argument => argument.Syntax.SpanStart)
            .ElementAtOrDefault(index);
        return argument is null ? null : FindOperation<IInvocationOperation>(argument.Value);
    }

    private static TOperation? FindOperation<TOperation>(IOperation operation)
        where TOperation : class, IOperation
    {
        if (operation is TOperation typed)
        {
            return typed;
        }

        foreach (IOperation child in operation.ChildOperations)
        {
            TOperation? found = FindOperation<TOperation>(child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static string LifetimeFromMethodName(string name)
    {
        if (name.Contains("Singleton", StringComparison.Ordinal))
        {
            return "singleton";
        }

        if (name.Contains("Scoped", StringComparison.Ordinal))
        {
            return "scoped";
        }

        if (name.Contains("Transient", StringComparison.Ordinal))
        {
            return "transient";
        }

        return "unknown";
    }

    private static IMethodSymbol? SelectConstructor(INamedTypeSymbol type)
    {
        IReadOnlyList<IMethodSymbol> instanceConstructors = [.. type.InstanceConstructors
            .Where(constructor => !constructor.IsImplicitlyDeclared)
            .OrderBy(constructor => constructor.Locations.FirstOrDefault()?.SourceSpan.Start ?? int.MaxValue)];
        IMethodSymbol? activatorConstructor = instanceConstructors.FirstOrDefault(constructor =>
            constructor.GetAttributes().Any(attribute => attribute.AttributeClass?.Name == "ActivatorUtilitiesConstructorAttribute"));
        if (activatorConstructor is not null)
        {
            return activatorConstructor;
        }

        IReadOnlyList<IMethodSymbol> publicConstructors = [.. instanceConstructors.Where(constructor => constructor.DeclaredAccessibility == Accessibility.Public)];
        if (publicConstructors.Count == 1)
        {
            return publicConstructors[0];
        }

        if (publicConstructors.Count > 1)
        {
            return publicConstructors
                .OrderByDescending(constructor => constructor.Parameters.Length)
                .ThenBy(constructor => constructor.Locations.FirstOrDefault()?.SourceSpan.Start ?? int.MaxValue)
                .First();
        }

        return instanceConstructors.Count == 1 ? instanceConstructors[0] : null;
    }

    private static DiTypeInfo CreateTypeInfo(ITypeSymbol symbol, string? project, bool excludeGenerated)
    {
        ISymbol normalized = symbol;
        SymbolSourceLocation? location = SymbolNavigationFacts.GetSourceLocations(normalized, excludeGenerated).FirstOrDefault();
        return new DiTypeInfo(
            Name: symbol.Name,
            Kind: symbol.Kind.ToString(),
            Container: SymbolNavigationFacts.GetContainer(normalized),
            Facts: SymbolFactsBuilder.Create(normalized, project),
            Path: location?.Path,
            Line: location?.Line,
            Column: location?.Column,
            EndLine: location?.EndLine,
            EndColumn: location?.EndColumn);
    }

    private static IReadOnlyList<DiEvidence> CreateConstructorEvidence(IMethodSymbol constructor)
    {
        SymbolSourceLocation? location = SymbolNavigationFacts.GetSourceLocations(constructor).FirstOrDefault();
        return location is null
            ? []
            : [CreateEvidence(location.Path, location.Line, location.Column, location.EndLine, location.EndColumn, "constructor", ["constructor-parameter"])];
    }

    private static DiEvidence CreateEvidence(Location location, string kind, IReadOnlyList<string> reasons)
    {
        SymbolSourceLocation? source = SymbolNavigationFacts.CreateSourceLocation(location);
        return source is null
            ? new DiEvidence(kind, "", 0, 0, 0, 0, reasons)
            : CreateEvidence(source.Path, source.Line, source.Column, source.EndLine, source.EndColumn, kind, reasons);
    }

    private static DiEvidence CreateEvidence(string path, int line, int column, int endLine, int endColumn, string kind, IReadOnlyList<string> reasons)
    {
        return new DiEvidence(kind, path, line, column, endLine, endColumn, reasons);
    }

    private static IReadOnlyList<DiRegistrationItem> OrderRegistrations(IReadOnlyList<DiRegistrationItem> registrations)
    {
        return [.. registrations
            .OrderBy(item => item.Project.Path, StringComparer.Ordinal)
            .ThenBy(item => item.Path, StringComparer.Ordinal)
            .ThenBy(item => item.Line)
            .ThenBy(item => item.Column)
            .ThenBy(item => LifetimePriority(item.Lifetime))
            .ThenBy(item => Identity(item.ServiceType), StringComparer.Ordinal)
            .ThenBy(item => Identity(item.ImplementationType), StringComparer.Ordinal)];
    }

    private static IReadOnlyList<string> OrderedReasons(IReadOnlyList<string> reasons)
    {
        return [.. reasons
            .Distinct(StringComparer.Ordinal)
            .OrderBy(reason => reason, StringComparer.Ordinal)];
    }

    private static bool IsNamedType(ITypeSymbol type, string name)
    {
        return type.Name == name ||
            type.OriginalDefinition.Name == name;
    }

    private static bool IsKnownFrameworkService(DiTypeInfo type)
    {
        string name = type.Facts.FullyQualifiedName ?? type.Facts.DisplayName;
        return name.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal) ||
            name.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal) ||
            name.StartsWith("System.IServiceProvider", StringComparison.Ordinal) ||
            name.StartsWith("System.Func<", StringComparison.Ordinal) ||
            name.StartsWith("System.Collections.Generic.IEnumerable<", StringComparison.Ordinal);
    }

    private static int LifetimePriority(string lifetime)
    {
        return lifetime switch
        {
            "singleton" => 0,
            "scoped" => 1,
            "transient" => 2,
            _ => 3
        };
    }

    private static int SeverityPriority(string severity)
    {
        return severity switch
        {
            "error" => 0,
            "warning" => 1,
            _ => 2
        };
    }

    private static int ConfidencePriority(string confidence)
    {
        return confidence switch
        {
            "high" => 0,
            "medium" => 1,
            _ => 2
        };
    }

    private sealed record RegistrationWithSymbols(
        DiRegistrationItem Item,
        INamedTypeSymbol? ServiceSymbol,
        INamedTypeSymbol? ImplementationSymbol);
}

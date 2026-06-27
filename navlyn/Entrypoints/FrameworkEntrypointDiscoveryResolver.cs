using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Navlyn.GeneratedCode;
using Navlyn.Paths;
using Navlyn.RepoGraph;
using Navlyn.Symbols;
using Navlyn.Testing;
using Navlyn.Workspaces;

namespace Navlyn.Entrypoints;

internal sealed class FrameworkEntrypointDiscoveryResolver
{
    private static readonly string[] FrameworkOrder = ["aspnetcore", "test", "worker", "azure-functions", "grpc", "mediatr", "messaging"];

    public async Task<FrameworkEntrypointsResult> DiscoverAsync(
        LoadedWorkspace workspace,
        IReadOnlyList<Project> projects,
        IReadOnlyList<FrameworkEntrypointProjectFilter>? projectFilters,
        FrameworkEntrypointOptions options,
        CancellationToken cancellationToken)
    {
        FrameworkEntrypointsSection section = await DiscoverSectionAsync(workspace, projects, options, cancellationToken);
        return new FrameworkEntrypointsResult(
            Workspace: workspace.DisplayPath,
            Kind: workspace.Kind,
            Command: "framework-entrypoints",
            Frameworks: options.Frameworks,
            Projects: projectFilters,
            Limits: new FrameworkEntrypointLimits(options.Limit, options.EvidenceLimit),
            Entrypoints: section,
            Truncated: section.Truncated,
            Warnings: section.TotalEntrypoints == 0 ? ["no-framework-entrypoints-found"] : [],
            NextActions: []);
    }

    public async Task<FrameworkEntrypointsSection> DiscoverSectionAsync(
        LoadedWorkspace workspace,
        IReadOnlyList<Project> projects,
        FrameworkEntrypointOptions options,
        CancellationToken cancellationToken)
    {
        List<FrameworkEntrypointItem> items = [];
        HashSet<string> frameworks = new(options.Frameworks, StringComparer.Ordinal);

        if (frameworks.Contains("test"))
        {
            items.AddRange(await DiscoverTestEntrypointsAsync(workspace, projects, options, cancellationToken));
        }

        if (frameworks.Contains("aspnetcore") || frameworks.Contains("worker"))
        {
            items.AddRange(await DiscoverSourceEntrypointsAsync(projects, options, frameworks, cancellationToken));
        }

        IReadOnlyList<FrameworkEntrypointItem> ordered = [.. Deduplicate(items)
            .OrderBy(item => FrameworkPriority(item.Framework))
            .ThenBy(item => ConfidencePriority(item.Confidence))
            .ThenBy(item => item.Project.Path, StringComparer.Ordinal)
            .ThenBy(item => item.Path, StringComparer.Ordinal)
            .ThenBy(item => item.Line)
            .ThenBy(item => item.Column)
            .ThenBy(item => item.EntrypointKind, StringComparer.Ordinal)
            .ThenBy(item => item.Facts.FullyQualifiedName ?? item.Name, StringComparer.Ordinal)];

        return new FrameworkEntrypointsSection(
            TotalEntrypoints: ordered.Count,
            Limit: options.Limit,
            Truncated: ordered.Count > options.Limit,
            Items: [.. ordered.Take(options.Limit)]);
    }

    public static bool Matches(FrameworkEntrypointItem entrypoint, FuzzySymbolLocation symbol)
    {
        return symbol.Path == entrypoint.Path &&
            symbol.Line == entrypoint.Line &&
            symbol.Column == entrypoint.Column;
    }

    private static async Task<IReadOnlyList<FrameworkEntrypointItem>> DiscoverTestEntrypointsAsync(
        LoadedWorkspace workspace,
        IReadOnlyList<Project> projects,
        FrameworkEntrypointOptions options,
        CancellationToken cancellationToken)
    {
        TestDiscoveryResult discovery = await new TestDiscoveryResolver().DiscoverAsync(
            projects,
            explicitTestProjects: projects,
            options.ExcludeGenerated,
            options.IncludeSnippets,
            options.SnippetLines,
            cancellationToken);

        return [.. discovery.Candidates.Select(candidate => new FrameworkEntrypointItem(
            EntrypointKind: "test-method",
            Framework: candidate.Framework,
            Name: candidate.Name,
            Container: candidate.Container,
            Facts: candidate.Facts,
            Path: candidate.Path,
            Line: candidate.Line,
            Column: candidate.Column,
            EndLine: candidate.EndLine,
            EndColumn: candidate.EndColumn,
            Project: new FrameworkEntrypointProject(candidate.Project.Name, candidate.Project.Path, candidate.Project.TargetFramework),
            Confidence: "high",
            ReasonCodes: [.. candidate.ReasonCodes
                .Append("test-framework-entrypoint")
                .Distinct(StringComparer.Ordinal)
                .OrderBy(ReasonPriority)
                .ThenBy(reason => reason, StringComparer.Ordinal)],
            Evidence: [.. candidate.Evidence.Select(evidence => new FrameworkEntrypointEvidence(
                evidence.Kind,
                evidence.Path,
                evidence.Line,
                evidence.Column,
                evidence.EndLine,
                evidence.EndColumn,
                evidence.ReasonCodes))],
            Snippet: candidate.Snippet))];
    }

    private static async Task<IReadOnlyList<FrameworkEntrypointItem>> DiscoverSourceEntrypointsAsync(
        IReadOnlyList<Project> projects,
        FrameworkEntrypointOptions options,
        HashSet<string> frameworks,
        CancellationToken cancellationToken)
    {
        List<FrameworkEntrypointItem> items = [];
        string repositoryRoot = PathDisplay.FindRepositoryRoot(projects.Select(project => project.FilePath).FirstOrDefault(path => path is not null) ?? Directory.GetCurrentDirectory()) ??
            Directory.GetCurrentDirectory();
        ProjectFileReader reader = new();

        foreach (Project project in projects.OrderBy(project => project.FilePath, StringComparer.Ordinal).ThenBy(project => project.Name, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? projectPath = project.FilePath is null ? null : PathDisplay.FromCurrentDirectory(project.FilePath);
            ProjectFileFacts facts = reader.Read(projectPath, repositoryRoot);
            RepoGraphProjectClassification classification = ProjectClassifier.Classify(project.Name, projectPath, project.AssemblyName, facts);
            bool webProject = classification.ReasonCodes.Contains("sdk-web", StringComparer.Ordinal);
            FrameworkEntrypointProject projectInfo = new(project.Name, projectPath, ProjectContextFacts.GetTargetFramework(project));

            foreach (Document document in project.Documents
                .Where(document => document.FilePath is not null)
                .Where(document => !options.ExcludeGenerated || !GeneratedCodeFacts.IsGeneratedPath(document.FilePath))
                .OrderBy(document => document.FilePath, StringComparer.Ordinal))
            {
                SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken);
                SemanticModel? semanticModel = await document.GetSemanticModelAsync(cancellationToken);
                if (root is null || semanticModel is null)
                {
                    continue;
                }

                if (frameworks.Contains("aspnetcore"))
                {
                    items.AddRange(DiscoverAspNetCoreEntrypoints(root, semanticModel, projectInfo, webProject, options));
                }

                if (frameworks.Contains("worker"))
                {
                    items.AddRange(DiscoverWorkerEntrypoints(root, semanticModel, projectInfo, options));
                }
            }
        }

        return items;
    }

    private static IEnumerable<FrameworkEntrypointItem> DiscoverAspNetCoreEntrypoints(
        SyntaxNode root,
        SemanticModel semanticModel,
        FrameworkEntrypointProject project,
        bool webProject,
        FrameworkEntrypointOptions options)
    {
        foreach (ClassDeclarationSyntax type in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            INamedTypeSymbol? typeSymbol = semanticModel.GetDeclaredSymbol(type);
            if (typeSymbol is null)
            {
                continue;
            }

            bool controllerType = IsControllerType(type, typeSymbol);
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

                bool routeAttribute = HasRouteAttribute(method.AttributeLists);
                bool httpMethodAttribute = HasHttpMethodAttribute(method.AttributeLists);
                bool controllerConvention = type.Identifier.ValueText.EndsWith("Controller", StringComparison.Ordinal);
                if (!controllerType && !routeAttribute && !(controllerConvention && webProject))
                {
                    continue;
                }

                string confidence = controllerType && (httpMethodAttribute || routeAttribute)
                    ? "high"
                    : controllerConvention && webProject ? "medium" : "low";
                List<string> reasons = ["public-action-method"];
                if (webProject)
                {
                    reasons.Add("aspnetcore-web-project");
                }

                if (controllerType)
                {
                    reasons.Add("aspnetcore-controller-type");
                }

                if (controllerConvention)
                {
                    reasons.Add("aspnetcore-controller-name-convention");
                }

                if (httpMethodAttribute)
                {
                    reasons.Add("aspnetcore-http-method-attribute");
                }

                if (routeAttribute)
                {
                    reasons.Add("aspnetcore-route-attribute");
                }

                FrameworkEntrypointItem? item = CreateItem(
                    "aspnetcore-controller-action",
                    "aspnetcore",
                    methodSymbol,
                    project,
                    confidence,
                    reasons,
                    CreateAttributeEvidence(method.AttributeLists, options.EvidenceLimit),
                    options);
                if (item is not null)
                {
                    yield return item;
                }
            }
        }

        foreach (InvocationExpressionSyntax invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            string? invocationName = GetInvocationName(invocation);
            if (invocationName is not ("MapGet" or "MapPost" or "MapPut" or "MapDelete" or "MapPatch" or "MapMethods" or "MapFallback" or "Map"))
            {
                continue;
            }

            ExpressionSyntax? handlerExpression = invocation.ArgumentList.Arguments.LastOrDefault()?.Expression;
            FrameworkEntrypointItem? item = CreateHandlerItem(
                "aspnetcore-minimal-api-handler",
                "aspnetcore",
                handlerExpression,
                semanticModel,
                project,
                "high",
                ["aspnetcore-minimal-api-map-call"],
                invocation.GetLocation(),
                ["minimal-api-map-call"],
                options);
            if (item is not null)
            {
                yield return item;
            }
        }

        foreach (InvocationExpressionSyntax invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (GetInvocationName(invocation) != "UseMiddleware")
            {
                continue;
            }

            TypeSyntax? middlewareType = invocation.ArgumentList.Arguments.Count == 0
                ? null
                : invocation.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax genericName } ? genericName.TypeArgumentList.Arguments.FirstOrDefault() : null;
            ISymbol? symbol = middlewareType is null ? null : semanticModel.GetSymbolInfo(middlewareType).Symbol;
            if (symbol is INamedTypeSymbol typeSymbol)
            {
                IMethodSymbol? invokeMethod = typeSymbol.GetMembers()
                    .OfType<IMethodSymbol>()
                    .Where(method => method.Name is "Invoke" or "InvokeAsync")
                    .OrderBy(method => method.Name, StringComparer.Ordinal)
                    .FirstOrDefault();
                FrameworkEntrypointItem? item = invokeMethod is null
                    ? CreateItem("aspnetcore-middleware", "aspnetcore", typeSymbol, project, "medium", ["aspnetcore-middleware-registration"], [CreateEvidence(invocation.GetLocation(), "invocation", ["usemiddleware-call"])], options)
                    : CreateItem("aspnetcore-middleware", "aspnetcore", invokeMethod, project, "high", ["aspnetcore-middleware-registration", "aspnetcore-middleware-invoke-method"], [CreateEvidence(invocation.GetLocation(), "invocation", ["usemiddleware-call"])], options);
                if (item is not null)
                {
                    yield return item;
                }
            }
        }
    }

    private static IEnumerable<FrameworkEntrypointItem> DiscoverWorkerEntrypoints(
        SyntaxNode root,
        SemanticModel semanticModel,
        FrameworkEntrypointProject project,
        FrameworkEntrypointOptions options)
    {
        foreach (ClassDeclarationSyntax type in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            INamedTypeSymbol? typeSymbol = semanticModel.GetDeclaredSymbol(type);
            if (typeSymbol is null)
            {
                continue;
            }

            if (typeSymbol.ContainingNamespace?.ToDisplayString().StartsWith("Microsoft.Extensions.", StringComparison.Ordinal) == true)
            {
                continue;
            }

            bool backgroundService = InheritsTypeNamed(typeSymbol, "BackgroundService");
            bool hostedService = ImplementsTypeNamed(typeSymbol, "IHostedService");
            foreach (MethodDeclarationSyntax method in type.Members.OfType<MethodDeclarationSyntax>())
            {
                IMethodSymbol? methodSymbol = semanticModel.GetDeclaredSymbol(method);
                if (methodSymbol is null)
                {
                    continue;
                }

                if (backgroundService && methodSymbol.Name == "ExecuteAsync")
                {
                    FrameworkEntrypointItem? item = CreateItem(
                        "worker-backgroundservice-execute",
                        "worker",
                        methodSymbol,
                        project,
                        "high",
                        ["worker-backgroundservice-base", "worker-executeasync-override"],
                        [],
                        options);
                    if (item is not null)
                    {
                        yield return item;
                    }
                }

                if (hostedService && methodSymbol.Name == "StartAsync")
                {
                    FrameworkEntrypointItem? item = CreateItem(
                        "worker-ihostedservice-start",
                        "worker",
                        methodSymbol,
                        project,
                        "high",
                        ["worker-ihostedservice-implementation", "worker-startasync-implementation"],
                        [],
                        options);
                    if (item is not null)
                    {
                        yield return item;
                    }
                }
            }
        }

        foreach (InvocationExpressionSyntax invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (GetInvocationName(invocation) != "AddHostedService")
            {
                continue;
            }

            TypeSyntax? hostedType = invocation.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax genericName }
                ? genericName.TypeArgumentList.Arguments.FirstOrDefault()
                : null;
            ISymbol? symbol = hostedType is null ? null : semanticModel.GetSymbolInfo(hostedType).Symbol;
            if (symbol is INamedTypeSymbol typeSymbol)
            {
                FrameworkEntrypointItem? item = CreateItem(
                    "worker-hosted-service-registration",
                    "worker",
                    typeSymbol,
                    project,
                    "medium",
                    ["worker-addhostedservice-registration"],
                    [CreateEvidence(invocation.GetLocation(), "invocation", ["addhostedservice-call"])],
                    options);
                if (item is not null)
                {
                    yield return item;
                }
            }
        }
    }

    private static FrameworkEntrypointItem? CreateHandlerItem(
        string kind,
        string framework,
        ExpressionSyntax? expression,
        SemanticModel semanticModel,
        FrameworkEntrypointProject project,
        string confidence,
        IReadOnlyList<string> reasonCodes,
        Location evidenceLocation,
        IReadOnlyList<string> evidenceReasons,
        FrameworkEntrypointOptions options)
    {
        if (expression is null)
        {
            return null;
        }

        if (expression is LambdaExpressionSyntax lambda)
        {
            ISymbol? containingSymbol = semanticModel.GetEnclosingSymbol(lambda.SpanStart);
            SymbolSourceLocation? location = SymbolNavigationFacts.CreateSourceLocation(lambda.GetLocation(), options.ExcludeGenerated);
            if (location is null)
            {
                return null;
            }

            return new FrameworkEntrypointItem(
                EntrypointKind: kind,
                Framework: framework,
                Name: "<lambda>",
                Container: containingSymbol is null ? null : SymbolNavigationFacts.GetContainer(containingSymbol) ?? containingSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                Facts: containingSymbol is null ? CreateSyntheticFacts("<lambda>", project.Name) : SymbolFactsBuilder.Create(containingSymbol, project.Name),
                Path: location.Path,
                Line: location.Line,
                Column: location.Column,
                EndLine: location.EndLine,
                EndColumn: location.EndColumn,
                Project: project,
                Confidence: confidence,
                ReasonCodes: OrderedReasons(reasonCodes),
                Evidence: [LimitEvidence(CreateEvidence(evidenceLocation, "invocation", evidenceReasons), options.EvidenceLimit)],
                Snippet: options.IncludeSnippets ? FuzzySnippetReader.TryRead(location.Path, location.Line, options.SnippetLines) : null);
        }

        SymbolInfo symbolInfo = semanticModel.GetSymbolInfo(expression);
        ISymbol? symbol = symbolInfo.Symbol ??
            symbolInfo.CandidateSymbols
                .OrderBy(symbol => symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), StringComparer.Ordinal)
                .FirstOrDefault();
        if (symbol is IMethodSymbol methodSymbol)
        {
            return CreateItem(kind, framework, methodSymbol, project, confidence, reasonCodes, [CreateEvidence(evidenceLocation, "invocation", evidenceReasons)], options);
        }

        return null;
    }

    private static FrameworkEntrypointItem? CreateItem(
        string kind,
        string framework,
        ISymbol symbol,
        FrameworkEntrypointProject project,
        string confidence,
        IReadOnlyList<string> reasonCodes,
        IReadOnlyList<FrameworkEntrypointEvidence> evidence,
        FrameworkEntrypointOptions options)
    {
        SymbolSourceLocation? location = SymbolNavigationFacts.GetSourceLocations(symbol, options.ExcludeGenerated).FirstOrDefault();
        if (location is null)
        {
            return null;
        }

        IReadOnlyList<FrameworkEntrypointEvidence> limitedEvidence = [.. evidence
            .Where(item => item.Path.Length > 0)
            .GroupBy(item => (item.Kind, item.Path, item.Line, item.Column, item.EndLine, item.EndColumn))
            .Select(group => group.First())
            .OrderBy(item => item.Path, StringComparer.Ordinal)
            .ThenBy(item => item.Line)
            .ThenBy(item => item.Column)
            .ThenBy(item => item.Kind, StringComparer.Ordinal)
            .Take(options.EvidenceLimit)];

        return new FrameworkEntrypointItem(
            EntrypointKind: kind,
            Framework: framework,
            Name: symbol.Name,
            Container: SymbolNavigationFacts.GetContainer(symbol),
            Facts: SymbolFactsBuilder.Create(symbol, project.Name),
            Path: location.Path,
            Line: location.Line,
            Column: location.Column,
            EndLine: location.EndLine,
            EndColumn: location.EndColumn,
            Project: project,
            Confidence: confidence,
            ReasonCodes: OrderedReasons(reasonCodes),
            Evidence: limitedEvidence,
            Snippet: options.IncludeSnippets ? FuzzySnippetReader.TryRead(location.Path, location.Line, options.SnippetLines) : null);
    }

    private static FrameworkEntrypointEvidence LimitEvidence(FrameworkEntrypointEvidence evidence, int evidenceLimit)
    {
        _ = evidenceLimit;
        return evidence;
    }

    private static SymbolFacts CreateSyntheticFacts(string displayName, string? project)
    {
        return new SymbolFacts(
            DisplayName: displayName,
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

    private static IReadOnlyList<FrameworkEntrypointEvidence> CreateAttributeEvidence(SyntaxList<AttributeListSyntax> attributeLists, int limit)
    {
        return [.. attributeLists
            .SelectMany(list => list.Attributes)
            .Select(attribute => CreateEvidence(attribute.GetLocation(), "attribute", [NormalizeAttributeReason(attribute.Name.ToString())]))
            .Take(limit)];
    }

    private static FrameworkEntrypointEvidence CreateEvidence(Location location, string kind, IReadOnlyList<string> reasonCodes)
    {
        SymbolSourceLocation? source = SymbolNavigationFacts.CreateSourceLocation(location);
        return source is null
            ? new FrameworkEntrypointEvidence(kind, "", 0, 0, 0, 0, reasonCodes)
            : new FrameworkEntrypointEvidence(kind, source.Path, source.Line, source.Column, source.EndLine, source.EndColumn, reasonCodes);
    }

    private static bool IsControllerType(ClassDeclarationSyntax type, INamedTypeSymbol symbol)
    {
        return type.Identifier.ValueText.EndsWith("Controller", StringComparison.Ordinal) ||
            HasAttribute(type.AttributeLists, "ApiController") ||
            InheritsTypeNamed(symbol, "ControllerBase") ||
            InheritsTypeNamed(symbol, "Controller");
    }

    private static bool HasHttpMethodAttribute(SyntaxList<AttributeListSyntax> attributeLists)
    {
        return attributeLists
            .SelectMany(list => list.Attributes)
            .Select(attribute => ShortAttributeName(attribute.Name.ToString()))
            .Any(name => name is "HttpGet" or "HttpPost" or "HttpPut" or "HttpDelete" or "HttpPatch" or "HttpHead" or "HttpOptions");
    }

    private static bool HasRouteAttribute(SyntaxList<AttributeListSyntax> attributeLists)
    {
        return HasAttribute(attributeLists, "Route");
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

    private static string NormalizeAttributeReason(string name)
    {
        return $"{ShortAttributeName(name).ToLowerInvariant()}-attribute";
    }

    private static string? GetInvocationName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax { Name: IdentifierNameSyntax identifier } => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax { Name: GenericNameSyntax generic } => generic.Identifier.ValueText,
            _ => null
        };
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

    private static bool ImplementsTypeNamed(INamedTypeSymbol symbol, string name)
    {
        return symbol.AllInterfaces.Any(item =>
            item.Name == name ||
            item.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).EndsWith($".{name}", StringComparison.Ordinal));
    }

    private static IReadOnlyList<FrameworkEntrypointItem> Deduplicate(IReadOnlyList<FrameworkEntrypointItem> items)
    {
        return [.. items
            .GroupBy(item => (
                item.Framework,
                item.EntrypointKind,
                item.Project.Path,
                item.Facts.DocumentationCommentId,
                item.Path,
                item.Line,
                item.Column))
            .Select(group => group
                .OrderBy(item => ConfidencePriority(item.Confidence))
                .ThenBy(item => item.Path, StringComparer.Ordinal)
                .ThenBy(item => item.Line)
                .ThenBy(item => item.Column)
                .First())];
    }

    private static IReadOnlyList<string> OrderedReasons(IReadOnlyList<string> reasons)
    {
        return [.. reasons
            .Distinct(StringComparer.Ordinal)
            .OrderBy(ReasonPriority)
            .ThenBy(reason => reason, StringComparer.Ordinal)];
    }

    private static int FrameworkPriority(string framework)
    {
        int index = Array.IndexOf(FrameworkOrder, framework);
        if (index >= 0)
        {
            return index;
        }

        return framework is "xunit" or "nunit" or "mstest" ? FrameworkPriority("test") : 100;
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

    private static int ReasonPriority(string reason)
    {
        return reason switch
        {
            "aspnetcore-web-project" => 0,
            "aspnetcore-controller-type" => 1,
            "aspnetcore-http-method-attribute" => 2,
            "aspnetcore-route-attribute" => 3,
            "aspnetcore-minimal-api-map-call" => 4,
            "aspnetcore-middleware-registration" => 5,
            "aspnetcore-middleware-invoke-method" => 6,
            "public-action-method" => 7,
            "test-framework-entrypoint" => 8,
            "test-method-attribute" => 9,
            "worker-backgroundservice-base" => 10,
            "worker-ihostedservice-implementation" => 11,
            "worker-executeasync-override" => 12,
            "worker-startasync-implementation" => 13,
            "worker-addhostedservice-registration" => 14,
            _ => 100
        };
    }
}

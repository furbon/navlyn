using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Navlyn.GeneratedCode;

namespace Navlyn.Symbols;

internal sealed class TypeHierarchyResolver
{
    public async Task<TypeHierarchyResolutionResult> ResolveAsync(
        Solution solution,
        FileInfo file,
        int line,
        int column,
        Project? project,
        bool excludeGenerated,
        CancellationToken cancellationToken)
    {
        SourceSymbolResolutionResult result = await new SourceSymbolResolver().ResolveAsync(
            solution,
            file,
            line,
            column,
            project,
            excludeGenerated,
            cancellationToken);

        if (result.Error is not null)
        {
            return TypeHierarchyResolutionResult.Failed(result.Error);
        }

        SourceSymbolResolution resolution = result.Resolution!;
        ISymbol symbol = resolution.Symbol;

        IReadOnlyList<HierarchySymbol> baseTypes = [];
        IReadOnlyList<HierarchySymbol> interfaces = [];
        IReadOnlyList<HierarchySymbol> derivedTypes = [];
        IReadOnlyList<HierarchySymbol> implementingTypes = [];
        IReadOnlyList<HierarchySymbol> baseMembers = [];
        IReadOnlyList<HierarchySymbol> overridingMembers = [];
        IReadOnlyList<HierarchySymbol> implementedMembers = [];

        if (symbol is INamedTypeSymbol namedType)
        {
            baseTypes = [.. GetBaseTypes(namedType)
                .Select(type => CreateSymbol(type, resolution.ProjectName, excludeGenerated))];
            interfaces = [.. namedType.Interfaces
                .Select(type => CreateSymbol(type, resolution.ProjectName, excludeGenerated))];

            IReadOnlyList<INamedTypeSymbol> sourceTypes = await FindSourceTypesAsync(
                solution,
                excludeGenerated,
                cancellationToken);

            derivedTypes = [.. sourceTypes
                .Where(type => IsDerivedFrom(type, namedType))
                .Select(type => CreateSymbol(type, resolution.ProjectName, excludeGenerated))
                .OrderBy(symbol => symbol.Path, StringComparer.Ordinal)
                .ThenBy(symbol => symbol.Line)
                .ThenBy(symbol => symbol.Column)
                .ThenBy(symbol => symbol.Name, StringComparer.Ordinal)];

            implementingTypes = namedType.TypeKind == TypeKind.Interface
                ? [.. sourceTypes
                    .Where(type => ImplementsInterface(type, namedType))
                    .Select(type => CreateSymbol(type, resolution.ProjectName, excludeGenerated))
                    .OrderBy(symbol => symbol.Path, StringComparer.Ordinal)
                    .ThenBy(symbol => symbol.Line)
                    .ThenBy(symbol => symbol.Column)
                    .ThenBy(symbol => symbol.Name, StringComparer.Ordinal)]
                : [];
        }
        else if (symbol is IMethodSymbol or IPropertySymbol or IEventSymbol)
        {
            baseMembers = [.. GetBaseMembers(symbol)
                .Select(member => CreateSymbol(member, resolution.ProjectName, excludeGenerated))];

            IReadOnlyList<ISymbol> implementations = await FindMemberImplementationsAsync(
                solution,
                symbol,
                cancellationToken);

            implementedMembers = [.. implementations
                .Select(member => CreateSymbol(member, resolution.ProjectName, excludeGenerated))
                .OrderBy(member => member.Path, StringComparer.Ordinal)
                .ThenBy(member => member.Line)
                .ThenBy(member => member.Column)
                .ThenBy(member => member.Name, StringComparer.Ordinal)];

            IReadOnlyList<ISymbol> overrides = await FindMemberOverridesAsync(
                solution,
                symbol,
                cancellationToken);

            overridingMembers = [.. overrides
                .Select(member => CreateSymbol(member, resolution.ProjectName, excludeGenerated))
                .OrderBy(member => member.Path, StringComparer.Ordinal)
                .ThenBy(member => member.Line)
                .ThenBy(member => member.Column)
                .ThenBy(member => member.Name, StringComparer.Ordinal)];
        }

        return TypeHierarchyResolutionResult.Succeeded(new TypeHierarchyResolution(
            File: resolution.File,
            Line: resolution.Line,
            Column: resolution.Column,
            Symbol: CreateSymbol(symbol, resolution.ProjectName, excludeGenerated),
            BaseTypes: baseTypes,
            Interfaces: interfaces,
            DerivedTypes: derivedTypes,
            ImplementingTypes: implementingTypes,
            BaseMembers: baseMembers,
            OverridingMembers: overridingMembers,
            ImplementedMembers: implementedMembers));
    }

    private static IEnumerable<INamedTypeSymbol> GetBaseTypes(INamedTypeSymbol namedType)
    {
        for (INamedTypeSymbol? current = namedType.BaseType; current is not null; current = current.BaseType)
        {
            yield return current;
        }
    }

    private static async Task<IReadOnlyList<INamedTypeSymbol>> FindSourceTypesAsync(
        Solution solution,
        bool excludeGenerated,
        CancellationToken cancellationToken)
    {
        List<INamedTypeSymbol> types = [];
        foreach (Project project in solution.Projects.OrderBy(project => project.FilePath, StringComparer.Ordinal))
        {
            foreach (Document document in project.Documents
                .Where(document => document.SupportsSyntaxTree)
                .Where(document => !excludeGenerated || document.FilePath is null || !GeneratedCodeFacts.IsGeneratedPath(document.FilePath))
                .OrderBy(document => document.FilePath, StringComparer.Ordinal))
            {
                SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken);
                SemanticModel? semanticModel = await document.GetSemanticModelAsync(cancellationToken);
                if (root is null || semanticModel is null)
                {
                    continue;
                }

                foreach (BaseTypeDeclarationSyntax declaration in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
                {
                    if (semanticModel.GetDeclaredSymbol(declaration, cancellationToken) is INamedTypeSymbol type)
                    {
                        types.Add(type);
                    }
                }
            }
        }

        return [.. types.Distinct(SymbolEqualityComparer.Default).OfType<INamedTypeSymbol>()];
    }

    private static bool IsDerivedFrom(INamedTypeSymbol candidate, INamedTypeSymbol target)
    {
        for (INamedTypeSymbol? current = candidate.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, target.OriginalDefinition) ||
                SymbolEqualityComparer.Default.Equals(current, target))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ImplementsInterface(INamedTypeSymbol candidate, INamedTypeSymbol target)
    {
        return candidate.AllInterfaces.Any(type =>
            SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, target.OriginalDefinition) ||
            SymbolEqualityComparer.Default.Equals(type, target));
    }

    private static IEnumerable<ISymbol> GetBaseMembers(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol method => GetMethodBaseMembers(method),
            IPropertySymbol property => GetPropertyBaseMembers(property),
            IEventSymbol eventSymbol => GetEventBaseMembers(eventSymbol),
            _ => []
        };
    }

    private static IEnumerable<ISymbol> GetMethodBaseMembers(IMethodSymbol method)
    {
        for (IMethodSymbol? current = method.OverriddenMethod; current is not null; current = current.OverriddenMethod)
        {
            yield return current;
        }

        foreach (IMethodSymbol interfaceMember in method.ExplicitInterfaceImplementations)
        {
            yield return interfaceMember;
        }
    }

    private static IEnumerable<ISymbol> GetPropertyBaseMembers(IPropertySymbol property)
    {
        for (IPropertySymbol? current = property.OverriddenProperty; current is not null; current = current.OverriddenProperty)
        {
            yield return current;
        }

        foreach (IPropertySymbol interfaceMember in property.ExplicitInterfaceImplementations)
        {
            yield return interfaceMember;
        }
    }

    private static IEnumerable<ISymbol> GetEventBaseMembers(IEventSymbol eventSymbol)
    {
        for (IEventSymbol? current = eventSymbol.OverriddenEvent; current is not null; current = current.OverriddenEvent)
        {
            yield return current;
        }

        foreach (IEventSymbol interfaceMember in eventSymbol.ExplicitInterfaceImplementations)
        {
            yield return interfaceMember;
        }
    }

    private static async Task<IReadOnlyList<ISymbol>> FindMemberImplementationsAsync(
        Solution solution,
        ISymbol symbol,
        CancellationToken cancellationToken)
    {
        IEnumerable<ISymbol> implementations = await SymbolFinder.FindImplementationsAsync(
            symbol,
            solution,
            cancellationToken: cancellationToken);

        return [.. implementations.Distinct(SymbolEqualityComparer.Default)];
    }

    private static async Task<IReadOnlyList<ISymbol>> FindMemberOverridesAsync(
        Solution solution,
        ISymbol symbol,
        CancellationToken cancellationToken)
    {
        IEnumerable<ISymbol> overrides = await SymbolFinder.FindOverridesAsync(
            symbol,
            solution,
            cancellationToken: cancellationToken);

        return [.. overrides.Distinct(SymbolEqualityComparer.Default)];
    }

    private static HierarchySymbol CreateSymbol(ISymbol symbol, string? projectName, bool excludeGenerated)
    {
        SymbolSourceLocation? location = SymbolNavigationFacts.GetSourceLocations(symbol, excludeGenerated).FirstOrDefault();
        return new HierarchySymbol(
            Name: symbol.Name,
            Kind: symbol.Kind.ToString(),
            Container: SymbolNavigationFacts.GetContainer(symbol),
            Facts: SymbolFactsBuilder.Create(symbol, projectName),
            Path: location?.Path,
            Line: location?.Line,
            Column: location?.Column,
            EndLine: location?.EndLine,
            EndColumn: location?.EndColumn);
    }
}

internal sealed record TypeHierarchyResolutionResult(TypeHierarchyResolution? Resolution, TypeHierarchyResolutionError? Error)
{
    public static TypeHierarchyResolutionResult Succeeded(TypeHierarchyResolution resolution)
    {
        return new TypeHierarchyResolutionResult(resolution, Error: null);
    }

    public static TypeHierarchyResolutionResult Failed(int diagnosticId, string message, int exitCode)
    {
        return new TypeHierarchyResolutionResult(
            Resolution: null,
            Error: new TypeHierarchyResolutionError(diagnosticId, message, exitCode));
    }

    public static TypeHierarchyResolutionResult Failed(SymbolNavigationError error)
    {
        return Failed(error.DiagnosticId, error.Message, error.ExitCode);
    }
}

internal sealed record TypeHierarchyResolution(
    string File,
    int Line,
    int Column,
    HierarchySymbol Symbol,
    IReadOnlyList<HierarchySymbol> BaseTypes,
    IReadOnlyList<HierarchySymbol> Interfaces,
    IReadOnlyList<HierarchySymbol> DerivedTypes,
    IReadOnlyList<HierarchySymbol> ImplementingTypes,
    IReadOnlyList<HierarchySymbol> BaseMembers,
    IReadOnlyList<HierarchySymbol> OverridingMembers,
    IReadOnlyList<HierarchySymbol> ImplementedMembers);

internal sealed record HierarchySymbol(
    string Name,
    string Kind,
    string? Container,
    SymbolFacts Facts,
    string? Path,
    int? Line,
    int? Column,
    int? EndLine,
    int? EndColumn);

internal sealed record TypeHierarchyResolutionError(int DiagnosticId, string Message, int ExitCode);

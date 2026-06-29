using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Navlyn.Diagnostics;

namespace Navlyn.Symbols;

internal sealed class SignatureResolver
{
    public async Task<SignatureResolutionResult> ResolveAsync(
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
            return SignatureResolutionResult.Failed(result.Error);
        }

        SourceSymbolResolution source = result.Resolution!;
        IReadOnlyList<SymbolSourceLocation> declarations = SymbolNavigationFacts.GetSourceLocations(source.Symbol, excludeGenerated);
        IReadOnlyList<SyntaxNode> declarationNodes = [.. source.Symbol.Locations
            .Where(location => location.IsInSource && location.SourceTree is not null)
            .Select(location =>
            {
                SyntaxNode root = location.SourceTree!.GetRoot(cancellationToken);
                return FindDeclarationNode(root, source.Symbol, location);
            })
            .OfType<SyntaxNode>()];

        SignatureSymbol symbol = new(
            Name: source.Symbol.Name,
            Kind: source.Symbol.Kind.ToString(),
            Container: SymbolNavigationFacts.GetContainer(source.Symbol),
            Facts: SymbolFactsBuilder.Create(source.Symbol, source.ProjectName),
            Path: declarations.FirstOrDefault()?.Path,
            Line: declarations.FirstOrDefault()?.Line,
            Column: declarations.FirstOrDefault()?.Column,
            EndLine: declarations.FirstOrDefault()?.EndLine,
            EndColumn: declarations.FirstOrDefault()?.EndColumn);

        SignatureApiShape apiShape = new(
            DisplayName: source.Symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
            DocumentationCommentId: source.Symbol.GetDocumentationCommentId(),
            Accessibility: source.Symbol.DeclaredAccessibility == Accessibility.NotApplicable
                ? null
                : source.Symbol.DeclaredAccessibility.ToString(),
            Modifiers: GetModifiers(declarationNodes),
            TypeParameters: GetTypeParameters(source.Symbol),
            GenericConstraints: GetGenericConstraints(declarationNodes),
            Parameters: source.Symbol is IMethodSymbol method
                ? [.. method.Parameters.Select(CreateParameter)]
                : source.Symbol is IPropertySymbol { IsIndexer: true } indexer
                    ? [.. indexer.Parameters.Select(CreateParameter)]
                    : null,
            ReturnType: source.Symbol is IMethodSymbol returnMethod && returnMethod.MethodKind is not MethodKind.Constructor and not MethodKind.StaticConstructor
                ? SymbolFactsBuilder.CreateType(returnMethod.ReturnType)
                : null,
            PropertyType: source.Symbol is IPropertySymbol property ? SymbolFactsBuilder.CreateType(property.Type) : null,
            EventType: source.Symbol is IEventSymbol eventSymbol ? SymbolFactsBuilder.CreateType(eventSymbol.Type) : null,
            FieldType: source.Symbol is IFieldSymbol field ? SymbolFactsBuilder.CreateType(field.Type) : null,
            Attributes: source.Symbol.GetAttributes()
                .Select(attribute => attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat))
                .OfType<string>()
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray(),
            OverriddenSymbol: GetOverriddenSymbol(source.Symbol),
            ImplementedSymbols: GetImplementedSymbols(source.Symbol),
            InterfaceTypes: source.Symbol is INamedTypeSymbol namedType
                ? [.. namedType.Interfaces
                    .Select(item => item.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat))
                    .OrderBy(item => item, StringComparer.Ordinal)]
                : null,
            DeclarationCount: declarations.Count,
            IsPartial: declarationNodes.Any(IsPartialDeclaration) || declarations.Count > 1,
            Declarations: declarations);

        return SignatureResolutionResult.Succeeded(new SignatureResolution(
            source.File,
            source.Line,
            source.Column,
            symbol,
            apiShape));
    }

    private static SyntaxNode? FindDeclarationNode(SyntaxNode root, ISymbol symbol, Location location)
    {
        SyntaxNode node = root.FindNode(location.SourceSpan, getInnermostNodeForTie: true);
        IEnumerable<SyntaxNode> candidates = node.AncestorsAndSelf();
        return symbol switch
        {
            INamedTypeSymbol { TypeKind: TypeKind.Delegate } => candidates.OfType<DelegateDeclarationSyntax>().FirstOrDefault(),
            INamedTypeSymbol { TypeKind: TypeKind.Enum } => candidates.OfType<EnumDeclarationSyntax>().FirstOrDefault(),
            INamedTypeSymbol => candidates.OfType<TypeDeclarationSyntax>().FirstOrDefault(),
            IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor } => candidates.OfType<ConstructorDeclarationSyntax>().FirstOrDefault(),
            IMethodSymbol { MethodKind: MethodKind.LocalFunction } => candidates.OfType<LocalFunctionStatementSyntax>().FirstOrDefault(),
            IMethodSymbol => candidates.OfType<MethodDeclarationSyntax>().FirstOrDefault<SyntaxNode>() ?? candidates.OfType<OperatorDeclarationSyntax>().FirstOrDefault<SyntaxNode>(),
            IPropertySymbol { IsIndexer: true } => candidates.OfType<IndexerDeclarationSyntax>().FirstOrDefault(),
            IPropertySymbol => candidates.OfType<PropertyDeclarationSyntax>().FirstOrDefault(),
            IEventSymbol => candidates.OfType<EventDeclarationSyntax>().FirstOrDefault<SyntaxNode>() ?? candidates.OfType<EventFieldDeclarationSyntax>().FirstOrDefault<SyntaxNode>(),
            IFieldSymbol => candidates.OfType<FieldDeclarationSyntax>().FirstOrDefault(),
            _ => null
        };
    }

    private static IReadOnlyList<string> GetModifiers(IReadOnlyList<SyntaxNode> declarations)
    {
        return [.. declarations
            .SelectMany(node => GetModifierTokens(node).Select(token => token))
            .Select(token => token.ValueText)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)];
    }

    private static SyntaxTokenList GetModifierTokens(SyntaxNode node)
    {
        return node switch
        {
            TypeDeclarationSyntax declaration => declaration.Modifiers,
            DelegateDeclarationSyntax declaration => declaration.Modifiers,
            EnumDeclarationSyntax declaration => declaration.Modifiers,
            BaseMethodDeclarationSyntax declaration => declaration.Modifiers,
            PropertyDeclarationSyntax declaration => declaration.Modifiers,
            IndexerDeclarationSyntax declaration => declaration.Modifiers,
            EventDeclarationSyntax declaration => declaration.Modifiers,
            EventFieldDeclarationSyntax declaration => declaration.Modifiers,
            FieldDeclarationSyntax declaration => declaration.Modifiers,
            _ => default
        };
    }

    private static IReadOnlyList<string>? GetTypeParameters(ISymbol symbol)
    {
        IEnumerable<ITypeParameterSymbol> parameters = symbol switch
        {
            INamedTypeSymbol type => type.TypeParameters,
            IMethodSymbol method => method.TypeParameters,
            _ => []
        };

        string[] names = [.. parameters.Select(parameter => parameter.Name)];
        return names.Length == 0 ? null : names;
    }

    private static IReadOnlyList<string>? GetGenericConstraints(IReadOnlyList<SyntaxNode> declarations)
    {
        string[] constraints = [.. declarations
            .SelectMany(node => node switch
            {
                TypeDeclarationSyntax type => type.ConstraintClauses.Select(clause => clause.ToString()),
                MethodDeclarationSyntax method => method.ConstraintClauses.Select(clause => clause.ToString()),
                LocalFunctionStatementSyntax local => local.ConstraintClauses.Select(clause => clause.ToString()),
                _ => []
            })
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)];

        return constraints.Length == 0 ? null : constraints;
    }

    private static SignatureParameterShape CreateParameter(IParameterSymbol parameter)
    {
        return new SignatureParameterShape(
            Name: parameter.Name,
            Ordinal: parameter.Ordinal,
            Type: SymbolFactsBuilder.CreateType(parameter.Type),
            RefKind: parameter.RefKind == RefKind.None ? null : parameter.RefKind.ToString(),
            NullableAnnotation: parameter.NullableAnnotation == NullableAnnotation.None ? null : parameter.NullableAnnotation.ToString(),
            IsOptional: parameter.IsOptional,
            IsParams: parameter.IsParams,
            HasExplicitDefaultValue: parameter.HasExplicitDefaultValue,
            ExplicitDefaultValue: parameter.HasExplicitDefaultValue ? parameter.ExplicitDefaultValue?.ToString() : null);
    }

    private static string? GetOverriddenSymbol(ISymbol symbol)
    {
        ISymbol? overridden = symbol switch
        {
            IMethodSymbol method => method.OverriddenMethod,
            IPropertySymbol property => property.OverriddenProperty,
            IEventSymbol eventSymbol => eventSymbol.OverriddenEvent,
            _ => null
        };

        return overridden?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
    }

    private static IReadOnlyList<string>? GetImplementedSymbols(ISymbol symbol)
    {
        IEnumerable<ISymbol> implemented = symbol switch
        {
            IMethodSymbol method => method.ExplicitInterfaceImplementations,
            IPropertySymbol property => property.ExplicitInterfaceImplementations,
            IEventSymbol eventSymbol => eventSymbol.ExplicitInterfaceImplementations,
            _ => []
        };

        string[] values = [.. implemented
            .Select(item => item.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat))
            .OrderBy(value => value, StringComparer.Ordinal)];

        return values.Length == 0 ? null : values;
    }

    private static bool IsPartialDeclaration(SyntaxNode node)
    {
        return GetModifierTokens(node).Any(token => token.ValueText == "partial");
    }
}

internal sealed record SignatureResolutionResult(SignatureResolution? Resolution, SymbolNavigationError? Error)
{
    public static SignatureResolutionResult Succeeded(SignatureResolution resolution)
    {
        return new SignatureResolutionResult(resolution, Error: null);
    }

    public static SignatureResolutionResult Failed(SymbolNavigationError error)
    {
        return new SignatureResolutionResult(Resolution: null, error);
    }
}

internal sealed record SignatureResolution(
    string File,
    int Line,
    int Column,
    SignatureSymbol Symbol,
    SignatureApiShape ApiShape);

internal sealed record SignatureSymbol(
    string Name,
    string Kind,
    string? Container,
    SymbolFacts Facts,
    string? Path,
    int? Line,
    int? Column,
    int? EndLine,
    int? EndColumn);

internal sealed record SignatureApiShape(
    string DisplayName,
    string? DocumentationCommentId,
    string? Accessibility,
    IReadOnlyList<string> Modifiers,
    IReadOnlyList<string>? TypeParameters,
    IReadOnlyList<string>? GenericConstraints,
    IReadOnlyList<SignatureParameterShape>? Parameters,
    SymbolTypeFacts? ReturnType,
    SymbolTypeFacts? PropertyType,
    SymbolTypeFacts? EventType,
    SymbolTypeFacts? FieldType,
    IReadOnlyList<string> Attributes,
    string? OverriddenSymbol,
    IReadOnlyList<string>? ImplementedSymbols,
    IReadOnlyList<string>? InterfaceTypes,
    int DeclarationCount,
    bool IsPartial,
    IReadOnlyList<SymbolSourceLocation> Declarations);

internal sealed record SignatureParameterShape(
    string Name,
    int Ordinal,
    SymbolTypeFacts? Type,
    string? RefKind,
    string? NullableAnnotation,
    bool IsOptional,
    bool IsParams,
    bool HasExplicitDefaultValue,
    string? ExplicitDefaultValue);

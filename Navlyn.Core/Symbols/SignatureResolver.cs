using Microsoft.CodeAnalysis;
using Navlyn.Diagnostics;
using Navlyn.Languages;

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
                return SourceLanguageFacts.FindDeclarationNode(root, source.Symbol, location);
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
            GenericConstraints: GetGenericConstraints(source.Symbol),
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
            IsPartial: declarationNodes.Any(SourceLanguageFacts.IsPartialDeclaration) || declarations.Count > 1,
            Declarations: declarations);

        return SignatureResolutionResult.Succeeded(new SignatureResolution(
            source.File,
            source.Line,
            source.Column,
            symbol,
            apiShape));
    }

    private static IReadOnlyList<string> GetModifiers(IReadOnlyList<SyntaxNode> declarations)
    {
        return [.. declarations
            .SelectMany(SourceLanguageFacts.GetModifierTexts)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)];
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

    private static IReadOnlyList<string>? GetGenericConstraints(ISymbol symbol)
    {
        IEnumerable<ITypeParameterSymbol> parameters = symbol switch
        {
            INamedTypeSymbol type => type.TypeParameters,
            IMethodSymbol method => method.TypeParameters,
            _ => []
        };

        string[] constraints = [.. parameters
            .Select(CreateConstraintText)
            .Where(value => value is not null)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)];

        return constraints.Length == 0 ? null : constraints;
    }

    private static string? CreateConstraintText(ITypeParameterSymbol parameter)
    {
        List<string> constraints = [];
        if (parameter.HasReferenceTypeConstraint)
        {
            constraints.Add("class");
        }

        if (parameter.HasValueTypeConstraint)
        {
            constraints.Add("struct");
        }

        constraints.AddRange(parameter.ConstraintTypes
            .Select(type => type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat))
            .OrderBy(value => value, StringComparer.Ordinal));

        if (parameter.HasConstructorConstraint)
        {
            constraints.Add("new()");
        }

        return constraints.Count == 0 ? null : $"{parameter.Name}: {string.Join(", ", constraints)}";
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

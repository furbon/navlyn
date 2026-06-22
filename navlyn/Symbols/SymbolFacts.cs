using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;

namespace Navlyn.Symbols;

internal static class SymbolFactsBuilder
{
    private static readonly SymbolDisplayFormat QualifiedNameFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions:
            SymbolDisplayMemberOptions.IncludeContainingType |
            SymbolDisplayMemberOptions.IncludeParameters,
        parameterOptions:
            SymbolDisplayParameterOptions.IncludeType |
            SymbolDisplayParameterOptions.IncludeParamsRefOut,
        miscellaneousOptions:
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    private static readonly SymbolDisplayFormat DisplayFormat =
        SymbolDisplayFormat.CSharpErrorMessageFormat;

    private static readonly SymbolDisplayFormat SignatureFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions:
            SymbolDisplayMemberOptions.IncludeContainingType |
            SymbolDisplayMemberOptions.IncludeParameters |
            SymbolDisplayMemberOptions.IncludeType,
        parameterOptions:
            SymbolDisplayParameterOptions.IncludeType |
            SymbolDisplayParameterOptions.IncludeName |
            SymbolDisplayParameterOptions.IncludeParamsRefOut |
            SymbolDisplayParameterOptions.IncludeDefaultValue,
        miscellaneousOptions:
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    public static SymbolFacts Create(ISymbol symbol, string? project = null)
    {
        return new SymbolFacts(
            DisplayName: symbol.ToDisplayString(DisplayFormat),
            FullyQualifiedName: GetFullyQualifiedName(symbol),
            Signature: GetSignature(symbol),
            DocumentationCommentId: symbol.GetDocumentationCommentId(),
            Namespace: GetNamespace(symbol),
            ContainingType: GetContainingType(symbol),
            Project: project,
            Assembly: symbol.ContainingAssembly?.Name,
            Accessibility: GetAccessibility(symbol),
            IsSource: symbol.Locations.Any(location => location.IsInSource),
            IsMetadata: symbol.Locations.Any(location => location.IsInMetadata),
            IsStatic: symbol.IsStatic,
            IsAbstract: symbol.IsAbstract,
            IsVirtual: symbol.IsVirtual,
            IsOverride: symbol.IsOverride,
            IsAsync: IsAsync(symbol),
            IsExtensionMethod: IsExtensionMethod(symbol),
            IsConstructor: symbol is IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor },
            IsOperator: symbol is IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator or MethodKind.Conversion },
            IsIndexer: symbol is IPropertySymbol { IsIndexer: true },
            Arity: GetArity(symbol),
            TypeParameters: GetTypeParameters(symbol),
            TypeArguments: GetTypeArguments(symbol),
            ConstructedFrom: GetConstructedFrom(symbol),
            Parameters: GetParameters(symbol),
            ReturnType: GetReturnType(symbol),
            PropertyType: symbol is IPropertySymbol property ? CreateType(property.Type) : null,
            EventType: symbol is IEventSymbol eventSymbol ? CreateType(eventSymbol.Type) : null,
            FieldType: symbol is IFieldSymbol field ? CreateType(field.Type) : null,
            Attributes: GetAttributes(symbol));
    }

    public static SymbolTypeFacts? CreateType(ITypeSymbol? type)
    {
        if (type is null)
        {
            return null;
        }

        return new SymbolTypeFacts(
            Name: type.ToDisplayString(DisplayFormat),
            FullyQualifiedName: type.ToDisplayString(QualifiedNameFormat),
            NullableAnnotation: GetNullableAnnotation(type.NullableAnnotation),
            IsReferenceType: type.IsReferenceType,
            IsValueType: type.IsValueType,
            IsSource: type.Locations.Any(location => location.IsInSource),
            IsMetadata: type.Locations.Any(location => location.IsInMetadata),
            Assembly: type.ContainingAssembly?.Name);
    }

    public static SymbolLocationFacts? CreateLocation(ISymbol symbol)
    {
        SymbolSourceLocation? location = SymbolNavigationFacts.GetSourceLocations(symbol).FirstOrDefault();
        if (location is null)
        {
            return null;
        }

        return new SymbolLocationFacts(location.Path, location.Line, location.Column, location.EndLine, location.EndColumn);
    }

    private static string? GetFullyQualifiedName(ISymbol symbol)
    {
        string name = symbol.ToDisplayString(QualifiedNameFormat);
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static bool IsAsync(ISymbol symbol)
    {
        return symbol is IMethodSymbol method && method.IsAsync;
    }

    private static bool IsExtensionMethod(ISymbol symbol)
    {
        return symbol is IMethodSymbol method && (method.IsExtensionMethod || method.ReducedFrom is not null);
    }

    private static string? GetSignature(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol or IPropertySymbol or IEventSymbol or IFieldSymbol or INamedTypeSymbol =>
                symbol.ToDisplayString(SignatureFormat),
            _ => null
        };
    }

    private static string? GetNamespace(ISymbol symbol)
    {
        INamespaceSymbol? namespaceSymbol = symbol.ContainingNamespace;
        return namespaceSymbol is null || namespaceSymbol.IsGlobalNamespace
            ? null
            : namespaceSymbol.ToDisplayString(DisplayFormat);
    }

    private static string? GetContainingType(ISymbol symbol)
    {
        return symbol.ContainingType?.ToDisplayString(DisplayFormat);
    }

    private static string? GetAccessibility(ISymbol symbol)
    {
        return symbol.DeclaredAccessibility == Accessibility.NotApplicable
            ? null
            : symbol.DeclaredAccessibility.ToString();
    }

    private static int? GetArity(ISymbol symbol)
    {
        return symbol switch
        {
            INamedTypeSymbol namedType when namedType.Arity > 0 => namedType.Arity,
            IMethodSymbol method when method.Arity > 0 => method.Arity,
            _ => null
        };
    }

    private static IReadOnlyList<string>? GetTypeParameters(ISymbol symbol)
    {
        ImmutableArray<ITypeParameterSymbol> parameters = symbol switch
        {
            INamedTypeSymbol namedType => namedType.TypeParameters,
            IMethodSymbol method => method.TypeParameters,
            _ => []
        };

        return parameters.Length == 0
            ? null
            : [.. parameters.Select(parameter => parameter.Name)];
    }

    private static IReadOnlyList<SymbolTypeFacts>? GetTypeArguments(ISymbol symbol)
    {
        ImmutableArray<ITypeSymbol> arguments = symbol switch
        {
            INamedTypeSymbol namedType => namedType.TypeArguments,
            IMethodSymbol method => method.TypeArguments,
            _ => []
        };

        return arguments.Length == 0
            ? null
            : [.. arguments.Select(CreateType).OfType<SymbolTypeFacts>()];
    }

    private static string? GetConstructedFrom(ISymbol symbol)
    {
        return symbol switch
        {
            INamedTypeSymbol namedType
                when namedType.IsGenericType &&
                    !SymbolEqualityComparer.Default.Equals(namedType, namedType.ConstructedFrom) =>
                namedType.ConstructedFrom.ToDisplayString(DisplayFormat),
            IMethodSymbol method
                when method.IsGenericMethod &&
                    !SymbolEqualityComparer.Default.Equals(method, method.ConstructedFrom) =>
                method.ConstructedFrom.ToDisplayString(DisplayFormat),
            _ => null
        };
    }

    private static IReadOnlyList<SymbolParameterFacts>? GetParameters(ISymbol symbol)
    {
        ImmutableArray<IParameterSymbol> parameters = symbol switch
        {
            IMethodSymbol method => method.Parameters,
            IPropertySymbol { IsIndexer: true } property => property.Parameters,
            _ => []
        };

        return parameters.Length == 0
            ? null
            : [.. parameters.Select(parameter => new SymbolParameterFacts(
                Name: parameter.Name,
                Type: CreateType(parameter.Type),
                Ordinal: parameter.Ordinal,
                RefKind: parameter.RefKind == RefKind.None ? null : parameter.RefKind.ToString(),
                IsOptional: parameter.IsOptional,
                IsParams: parameter.IsParams,
                HasExplicitDefaultValue: parameter.HasExplicitDefaultValue,
                ExplicitDefaultValue: parameter.HasExplicitDefaultValue
                    ? parameter.ExplicitDefaultValue?.ToString()
                    : null))];
    }

    private static SymbolTypeFacts? GetReturnType(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol method when method.MethodKind is not MethodKind.Constructor and not MethodKind.StaticConstructor =>
                CreateType(method.ReturnType),
            _ => null
        };
    }

    private static IReadOnlyList<SymbolAttributeFacts>? GetAttributes(ISymbol symbol)
    {
        ImmutableArray<AttributeData> attributes = symbol.GetAttributes();
        return attributes.Length == 0
            ? null
            : [.. attributes.Select(CreateAttributeFacts)];
    }

    private static SymbolAttributeFacts CreateAttributeFacts(AttributeData attribute)
    {
        return new SymbolAttributeFacts(
            Type: CreateType(attribute.AttributeClass),
            Constructor: attribute.AttributeConstructor is null
                ? null
                : new SymbolAttributeConstructorFacts(
                    DisplayName: attribute.AttributeConstructor.ToDisplayString(DisplayFormat),
                    FullyQualifiedName: attribute.AttributeConstructor.ToDisplayString(QualifiedNameFormat),
                    Location: CreateLocation(attribute.AttributeConstructor)));
    }

    private static string? GetNullableAnnotation(NullableAnnotation annotation)
    {
        return annotation == NullableAnnotation.None ? null : annotation.ToString();
    }
}

internal sealed record SymbolFacts(
    string DisplayName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? FullyQualifiedName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Signature,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? DocumentationCommentId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Namespace,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ContainingType,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Project,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Assembly,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Accessibility,
    bool IsSource,
    bool IsMetadata,
    bool IsStatic,
    bool IsAbstract,
    bool IsVirtual,
    bool IsOverride,
    bool IsAsync,
    bool IsExtensionMethod,
    bool IsConstructor,
    bool IsOperator,
    bool IsIndexer,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? Arity,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? TypeParameters,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<SymbolTypeFacts>? TypeArguments,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ConstructedFrom,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<SymbolParameterFacts>? Parameters,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    SymbolTypeFacts? ReturnType,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    SymbolTypeFacts? PropertyType,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    SymbolTypeFacts? EventType,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    SymbolTypeFacts? FieldType,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<SymbolAttributeFacts>? Attributes);

internal sealed record SymbolTypeFacts(
    string Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? FullyQualifiedName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? NullableAnnotation,
    bool IsReferenceType,
    bool IsValueType,
    bool IsSource,
    bool IsMetadata,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Assembly);

internal sealed record SymbolParameterFacts(
    string Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    SymbolTypeFacts? Type,
    int Ordinal,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? RefKind,
    bool IsOptional,
    bool IsParams,
    bool HasExplicitDefaultValue,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ExplicitDefaultValue);

internal sealed record SymbolAttributeFacts(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    SymbolTypeFacts? Type,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    SymbolAttributeConstructorFacts? Constructor);

internal sealed record SymbolAttributeConstructorFacts(
    string DisplayName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? FullyQualifiedName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    SymbolLocationFacts? Location);

internal sealed record SymbolLocationFacts(string Path, int Line, int Column, int EndLine, int EndColumn);

using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Navlyn.GeneratedCode;

namespace Navlyn.PublicApi;

internal sealed class PublicApiSymbolExtractor
{
    public IReadOnlyList<PublicApiSymbol> Extract(
        string sourceText,
        string path,
        string? targetFramework,
        bool excludeGenerated)
    {
        if (excludeGenerated && GeneratedCodeFacts.IsGeneratedPath(path))
        {
            return [];
        }

        SyntaxTree tree = CSharpSyntaxTree.ParseText(sourceText, path: path);
        CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
        List<PublicApiSymbol> symbols = [];

        VisitMembers(root.Members, namespaceName: null, containingTypes: [], parentApiVisible: true, symbols);

        return [.. symbols
            .Select(symbol => symbol with { TargetFramework = targetFramework })
            .OrderBy(symbol => symbol.DocumentationCommentId, StringComparer.Ordinal)
            .ThenBy(symbol => symbol.Path, StringComparer.Ordinal)
            .ThenBy(symbol => symbol.Line)
            .ThenBy(symbol => symbol.Column)];
    }

    private static void VisitMembers(
        SyntaxList<MemberDeclarationSyntax> members,
        string? namespaceName,
        IReadOnlyList<string> containingTypes,
        bool parentApiVisible,
        List<PublicApiSymbol> symbols)
    {
        foreach (MemberDeclarationSyntax member in members)
        {
            switch (member)
            {
                case BaseNamespaceDeclarationSyntax namespaceDeclaration:
                    string childNamespace = CombineNamespace(namespaceName, namespaceDeclaration.Name.ToString());
                    VisitMembers(namespaceDeclaration.Members, childNamespace, containingTypes, parentApiVisible, symbols);
                    break;
                case TypeDeclarationSyntax typeDeclaration:
                    VisitType(typeDeclaration, namespaceName, containingTypes, parentApiVisible, symbols);
                    break;
                case EnumDeclarationSyntax enumDeclaration:
                    VisitEnum(enumDeclaration, namespaceName, containingTypes, parentApiVisible, symbols);
                    break;
                case DelegateDeclarationSyntax delegateDeclaration:
                    AddDelegate(delegateDeclaration, namespaceName, containingTypes, parentApiVisible, symbols);
                    break;
            }
        }
    }

    private static void VisitType(
        TypeDeclarationSyntax type,
        string? namespaceName,
        IReadOnlyList<string> containingTypes,
        bool parentApiVisible,
        List<PublicApiSymbol> symbols)
    {
        string accessibility = GetAccessibility(type.Modifiers, defaultAccessibility: containingTypes.Count == 0 ? "Internal" : "Private");
        bool apiVisible = parentApiVisible && IsApiAccessibility(accessibility);
        string typeName = TypeDisplayName(type.Identifier.ValueText, type.TypeParameterList);
        IReadOnlyList<string> nextContainingTypes = [.. containingTypes, typeName];

        if (apiVisible)
        {
            symbols.Add(CreateTypeSymbol(type, namespaceName, containingTypes, accessibility));
            foreach (PublicApiSymbol symbol in ExtractMembers(type, namespaceName, containingTypes, accessibility))
            {
                symbols.Add(symbol);
            }
        }

        VisitMembers(type.Members, namespaceName, nextContainingTypes, apiVisible, symbols);
    }

    private static void VisitEnum(
        EnumDeclarationSyntax enumDeclaration,
        string? namespaceName,
        IReadOnlyList<string> containingTypes,
        bool parentApiVisible,
        List<PublicApiSymbol> symbols)
    {
        string accessibility = GetAccessibility(enumDeclaration.Modifiers, defaultAccessibility: containingTypes.Count == 0 ? "Internal" : "Private");
        bool apiVisible = parentApiVisible && IsApiAccessibility(accessibility);
        if (!apiVisible)
        {
            return;
        }

        PublicApiSymbol enumSymbol = CreateTypeLikeSymbol(
            enumDeclaration,
            name: enumDeclaration.Identifier.ValueText,
            kind: "NamedType",
            namespaceName,
            containingTypes,
            accessibility,
            signature: $"enum {FullName(namespaceName, containingTypes, enumDeclaration.Identifier.ValueText)}",
            typeParameters: [],
            genericConstraints: []);
        symbols.Add(enumSymbol);

        foreach (EnumMemberDeclarationSyntax member in enumDeclaration.Members)
        {
            string container = FullName(namespaceName, containingTypes, enumDeclaration.Identifier.ValueText);
            string signature = member.EqualsValue is null
                ? $"{container}.{member.Identifier.ValueText}"
                : $"{container}.{member.Identifier.ValueText} = {Normalize(member.EqualsValue.Value.ToString())}";
            symbols.Add(CreateSymbol(
                member,
                name: member.Identifier.ValueText,
                kind: "EnumMember",
                container,
                namespaceName,
                accessibility: "Public",
                signature,
                modifiers: [],
                typeParameters: [],
                parameters: [],
                returnType: null,
                propertyType: null,
                fieldType: null,
                eventType: null,
                genericConstraints: [],
                nullableAnnotations: [],
                defaultValues: [],
                attributes: Attributes(member.AttributeLists)));
        }
    }

    private static void AddDelegate(
        DelegateDeclarationSyntax declaration,
        string? namespaceName,
        IReadOnlyList<string> containingTypes,
        bool parentApiVisible,
        List<PublicApiSymbol> symbols)
    {
        string accessibility = GetAccessibility(declaration.Modifiers, defaultAccessibility: containingTypes.Count == 0 ? "Internal" : "Private");
        if (!parentApiVisible || !IsApiAccessibility(accessibility))
        {
            return;
        }

        IReadOnlyList<PublicApiParameter> parameters = Parameters(declaration.ParameterList);
        string signature = $"{Normalize(declaration.ReturnType.ToString())} {FullName(namespaceName, containingTypes, TypeDisplayName(declaration.Identifier.ValueText, declaration.TypeParameterList))}({string.Join(", ", parameters.Select(parameter => parameter.Type))})";
        symbols.Add(CreateSymbol(
            declaration,
            declaration.Identifier.ValueText,
            "NamedType",
            Container(namespaceName, containingTypes),
            namespaceName,
            accessibility,
            signature,
            Modifiers(declaration.Modifiers),
            TypeParameters(declaration.TypeParameterList),
            parameters,
            Normalize(declaration.ReturnType.ToString()),
            propertyType: null,
            fieldType: null,
            eventType: null,
            GenericConstraints(declaration.ConstraintClauses),
            NullableAnnotations(signature),
            DefaultValues(parameters),
            Attributes(declaration.AttributeLists)));
    }

    private static PublicApiSymbol CreateTypeSymbol(
        TypeDeclarationSyntax type,
        string? namespaceName,
        IReadOnlyList<string> containingTypes,
        string accessibility)
    {
        string name = TypeDisplayName(type.Identifier.ValueText, type.TypeParameterList);
        IReadOnlyList<string> constraints = GenericConstraints(type.ConstraintClauses);
        string signature = $"{string.Join(" ", Modifiers(type.Modifiers))} {type.Keyword.ValueText} {FullName(namespaceName, containingTypes, name)} {string.Join(" ", constraints)}".Trim();
        return CreateTypeLikeSymbol(
            type,
            name,
            "NamedType",
            namespaceName,
            containingTypes,
            accessibility,
            Normalize(signature),
            TypeParameters(type.TypeParameterList),
            constraints);
    }

    private static PublicApiSymbol CreateTypeLikeSymbol(
        MemberDeclarationSyntax declaration,
        string name,
        string kind,
        string? namespaceName,
        IReadOnlyList<string> containingTypes,
        string accessibility,
        string signature,
        IReadOnlyList<string> typeParameters,
        IReadOnlyList<string> genericConstraints)
    {
        return CreateSymbol(
            declaration,
            name,
            kind,
            Container(namespaceName, containingTypes),
            namespaceName,
            accessibility,
            signature,
            Modifiers(GetModifiers(declaration)),
            typeParameters,
            parameters: [],
            returnType: null,
            propertyType: null,
            fieldType: null,
            eventType: null,
            genericConstraints,
            NullableAnnotations(signature),
            defaultValues: [],
            Attributes(GetAttributeLists(declaration)));
    }

    private static PublicApiSymbol CreateSymbol(
        CSharpSyntaxNode node,
        string name,
        string kind,
        string? container,
        string? namespaceName,
        string accessibility,
        string signature,
        IReadOnlyList<string> modifiers,
        IReadOnlyList<string> typeParameters,
        IReadOnlyList<PublicApiParameter> parameters,
        string? returnType,
        string? propertyType,
        string? fieldType,
        string? eventType,
        IReadOnlyList<string> genericConstraints,
        IReadOnlyList<string> nullableAnnotations,
        IReadOnlyList<string> defaultValues,
        IReadOnlyList<string> attributes)
    {
        FileLinePositionSpan span = node.SyntaxTree.GetLineSpan(node.Span);
        string documentationId = CreateDocumentationId(kind, container, name, parameters, signature);
        return new PublicApiSymbol(
            SymbolId: $"api:v1:{Hash(documentationId)}",
            Name: name,
            Kind: kind,
            Container: container,
            Namespace: namespaceName,
            Accessibility: accessibility,
            Signature: Normalize(signature),
            DocumentationCommentId: documentationId,
            Path: span.IsValid ? span.Path.Replace('\\', '/') : null,
            Line: span.IsValid ? span.StartLinePosition.Line + 1 : null,
            Column: span.IsValid ? span.StartLinePosition.Character + 1 : null,
            TargetFramework: null,
            Modifiers: modifiers,
            TypeParameters: typeParameters,
            Parameters: parameters,
            ReturnType: returnType,
            PropertyType: propertyType,
            FieldType: fieldType,
            EventType: eventType,
            GenericConstraints: genericConstraints,
            NullableAnnotations: nullableAnnotations,
            DefaultValues: defaultValues,
            Attributes: attributes);
    }

    private static string CreateDocumentationId(
        string kind,
        string? container,
        string name,
        IReadOnlyList<PublicApiParameter> parameters,
        string signature)
    {
        string prefix = kind switch
        {
            "NamedType" => "T",
            "Property" => "P",
            "Field" => "F",
            "Event" => "E",
            _ => "M"
        };
        string qualifiedName = string.IsNullOrWhiteSpace(container) ? name : $"{container}.{name}";
        string parameterList = parameters.Count == 0 ? string.Empty : $"({string.Join(",", parameters.Select(parameter => parameter.Type))})";
        return $"{prefix}:{qualifiedName}{parameterList}";
    }

    public static IReadOnlyList<PublicApiSymbol> ExtractMembers(
        TypeDeclarationSyntax type,
        string? namespaceName,
        IReadOnlyList<string> containingTypes,
        string typeAccessibility)
    {
        List<PublicApiSymbol> symbols = [];
        string typeName = TypeDisplayName(type.Identifier.ValueText, type.TypeParameterList);
        string container = FullName(namespaceName, containingTypes, typeName);
        bool isInterface = type is InterfaceDeclarationSyntax;

        foreach (MemberDeclarationSyntax member in type.Members)
        {
            switch (member)
            {
                case MethodDeclarationSyntax method:
                    AddMethod(method, container, namespaceName, isInterface, symbols);
                    break;
                case ConstructorDeclarationSyntax constructor:
                    AddConstructor(constructor, container, namespaceName, type.Identifier.ValueText, symbols);
                    break;
                case PropertyDeclarationSyntax property:
                    AddProperty(property, container, namespaceName, isInterface, symbols);
                    break;
                case IndexerDeclarationSyntax indexer:
                    AddIndexer(indexer, container, namespaceName, isInterface, symbols);
                    break;
                case EventDeclarationSyntax eventDeclaration:
                    AddEvent(eventDeclaration, container, namespaceName, isInterface, symbols);
                    break;
                case EventFieldDeclarationSyntax eventField:
                    AddEventField(eventField, container, namespaceName, isInterface, symbols);
                    break;
                case FieldDeclarationSyntax field:
                    AddField(field, container, namespaceName, symbols);
                    break;
            }
        }

        _ = typeAccessibility;
        return symbols;
    }

    private static void VisitMembers(
        TypeDeclarationSyntax type,
        string? namespaceName,
        IReadOnlyList<string> containingTypes,
        List<PublicApiSymbol> symbols)
    {
        foreach (PublicApiSymbol symbol in ExtractMembers(type, namespaceName, containingTypes, GetAccessibility(type.Modifiers, "Private")))
        {
            symbols.Add(symbol);
        }
    }

    private static void AddMethod(MethodDeclarationSyntax method, string container, string? namespaceName, bool isInterface, List<PublicApiSymbol> symbols)
    {
        string accessibility = isInterface ? "Public" : GetAccessibility(method.Modifiers, "Private");
        if (!IsApiAccessibility(accessibility) || method.ExplicitInterfaceSpecifier is not null)
        {
            return;
        }

        IReadOnlyList<PublicApiParameter> parameters = Parameters(method.ParameterList);
        IReadOnlyList<string> constraints = GenericConstraints(method.ConstraintClauses);
        string name = TypeDisplayName(method.Identifier.ValueText, method.TypeParameterList);
        string signature = $"{Normalize(method.ReturnType.ToString())} {container}.{name}({string.Join(", ", parameters.Select(parameter => parameter.Type))}) {string.Join(" ", constraints)}";
        symbols.Add(CreateSymbol(
            method,
            method.Identifier.ValueText,
            "Method",
            container,
            namespaceName,
            accessibility,
            signature,
            Modifiers(method.Modifiers),
            TypeParameters(method.TypeParameterList),
            parameters,
            Normalize(method.ReturnType.ToString()),
            propertyType: null,
            fieldType: null,
            eventType: null,
            constraints,
            NullableAnnotations(signature),
            DefaultValues(parameters),
            Attributes(method.AttributeLists)));
    }

    private static void AddConstructor(ConstructorDeclarationSyntax constructor, string container, string? namespaceName, string typeName, List<PublicApiSymbol> symbols)
    {
        string accessibility = GetAccessibility(constructor.Modifiers, "Private");
        if (!IsApiAccessibility(accessibility))
        {
            return;
        }

        IReadOnlyList<PublicApiParameter> parameters = Parameters(constructor.ParameterList);
        string signature = $"{container}.{typeName}({string.Join(", ", parameters.Select(parameter => parameter.Type))})";
        symbols.Add(CreateSymbol(
            constructor,
            typeName,
            "Constructor",
            container,
            namespaceName,
            accessibility,
            signature,
            Modifiers(constructor.Modifiers),
            typeParameters: [],
            parameters,
            returnType: null,
            propertyType: null,
            fieldType: null,
            eventType: null,
            genericConstraints: [],
            NullableAnnotations(signature),
            DefaultValues(parameters),
            Attributes(constructor.AttributeLists)));
    }

    private static void AddProperty(PropertyDeclarationSyntax property, string container, string? namespaceName, bool isInterface, List<PublicApiSymbol> symbols)
    {
        string accessibility = isInterface ? "Public" : GetAccessibility(property.Modifiers, "Private");
        if (!IsApiAccessibility(accessibility) || property.ExplicitInterfaceSpecifier is not null)
        {
            return;
        }

        string propertyType = Normalize(property.Type.ToString());
        string signature = $"{propertyType} {container}.{property.Identifier.ValueText}";
        symbols.Add(CreateSymbol(
            property,
            property.Identifier.ValueText,
            "Property",
            container,
            namespaceName,
            accessibility,
            signature,
            Modifiers(property.Modifiers),
            typeParameters: [],
            parameters: [],
            returnType: null,
            propertyType,
            fieldType: null,
            eventType: null,
            genericConstraints: [],
            NullableAnnotations(signature),
            defaultValues: [],
            Attributes(property.AttributeLists)));
    }

    private static void AddIndexer(IndexerDeclarationSyntax indexer, string container, string? namespaceName, bool isInterface, List<PublicApiSymbol> symbols)
    {
        string accessibility = isInterface ? "Public" : GetAccessibility(indexer.Modifiers, "Private");
        if (!IsApiAccessibility(accessibility))
        {
            return;
        }

        IReadOnlyList<PublicApiParameter> parameters = Parameters(indexer.ParameterList);
        string propertyType = Normalize(indexer.Type.ToString());
        string signature = $"{propertyType} {container}.this[{string.Join(", ", parameters.Select(parameter => parameter.Type))}]";
        symbols.Add(CreateSymbol(
            indexer,
            "this[]",
            "Indexer",
            container,
            namespaceName,
            accessibility,
            signature,
            Modifiers(indexer.Modifiers),
            typeParameters: [],
            parameters,
            returnType: null,
            propertyType,
            fieldType: null,
            eventType: null,
            genericConstraints: [],
            NullableAnnotations(signature),
            DefaultValues(parameters),
            Attributes(indexer.AttributeLists)));
    }

    private static void AddEvent(EventDeclarationSyntax eventDeclaration, string container, string? namespaceName, bool isInterface, List<PublicApiSymbol> symbols)
    {
        string accessibility = isInterface ? "Public" : GetAccessibility(eventDeclaration.Modifiers, "Private");
        if (!IsApiAccessibility(accessibility))
        {
            return;
        }

        string eventType = Normalize(eventDeclaration.Type.ToString());
        string signature = $"{eventType} {container}.{eventDeclaration.Identifier.ValueText}";
        symbols.Add(CreateSymbol(
            eventDeclaration,
            eventDeclaration.Identifier.ValueText,
            "Event",
            container,
            namespaceName,
            accessibility,
            signature,
            Modifiers(eventDeclaration.Modifiers),
            typeParameters: [],
            parameters: [],
            returnType: null,
            propertyType: null,
            fieldType: null,
            eventType,
            genericConstraints: [],
            NullableAnnotations(signature),
            defaultValues: [],
            Attributes(eventDeclaration.AttributeLists)));
    }

    private static void AddEventField(EventFieldDeclarationSyntax eventField, string container, string? namespaceName, bool isInterface, List<PublicApiSymbol> symbols)
    {
        string accessibility = isInterface ? "Public" : GetAccessibility(eventField.Modifiers, "Private");
        if (!IsApiAccessibility(accessibility))
        {
            return;
        }

        string eventType = Normalize(eventField.Declaration.Type.ToString());
        foreach (VariableDeclaratorSyntax variable in eventField.Declaration.Variables)
        {
            string signature = $"{eventType} {container}.{variable.Identifier.ValueText}";
            symbols.Add(CreateSymbol(
                variable,
                variable.Identifier.ValueText,
                "Event",
                container,
                namespaceName,
                accessibility,
                signature,
                Modifiers(eventField.Modifiers),
                typeParameters: [],
                parameters: [],
                returnType: null,
                propertyType: null,
                fieldType: null,
                eventType,
                genericConstraints: [],
                NullableAnnotations(signature),
                defaultValues: [],
                Attributes(eventField.AttributeLists)));
        }
    }

    private static void AddField(FieldDeclarationSyntax field, string container, string? namespaceName, List<PublicApiSymbol> symbols)
    {
        string accessibility = GetAccessibility(field.Modifiers, "Private");
        if (!IsApiAccessibility(accessibility))
        {
            return;
        }

        string fieldType = Normalize(field.Declaration.Type.ToString());
        foreach (VariableDeclaratorSyntax variable in field.Declaration.Variables)
        {
            string signature = $"{fieldType} {container}.{variable.Identifier.ValueText}";
            symbols.Add(CreateSymbol(
                variable,
                variable.Identifier.ValueText,
                "Field",
                container,
                namespaceName,
                accessibility,
                signature,
                Modifiers(field.Modifiers),
                typeParameters: [],
                parameters: [],
                returnType: null,
                propertyType: null,
                fieldType,
                eventType: null,
                genericConstraints: [],
                NullableAnnotations(signature),
                defaultValues: [],
                Attributes(field.AttributeLists)));
        }
    }

    private static string GetAccessibility(SyntaxTokenList modifiers, string defaultAccessibility)
    {
        bool isPublic = modifiers.Any(SyntaxKind.PublicKeyword);
        bool isProtected = modifiers.Any(SyntaxKind.ProtectedKeyword);
        bool isInternal = modifiers.Any(SyntaxKind.InternalKeyword);
        bool isPrivate = modifiers.Any(SyntaxKind.PrivateKeyword);

        if (isPublic)
        {
            return "Public";
        }

        if (isProtected && isInternal)
        {
            return "ProtectedOrInternal";
        }

        if (isProtected && isPrivate)
        {
            return "PrivateProtected";
        }

        if (isProtected)
        {
            return "Protected";
        }

        if (isInternal)
        {
            return "Internal";
        }

        if (isPrivate)
        {
            return "Private";
        }

        return defaultAccessibility;
    }

    private static bool IsApiAccessibility(string accessibility)
    {
        return accessibility is "Public" or "Protected" or "ProtectedOrInternal";
    }

    private static IReadOnlyList<string> Modifiers(SyntaxTokenList modifiers)
    {
        return [.. modifiers
            .Select(token => token.ValueText)
            .Where(text => text is not "public" and not "protected" and not "internal" and not "private")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(text => text, StringComparer.Ordinal)];
    }

    private static SyntaxTokenList GetModifiers(MemberDeclarationSyntax declaration)
    {
        return declaration switch
        {
            TypeDeclarationSyntax type => type.Modifiers,
            EnumDeclarationSyntax enumDeclaration => enumDeclaration.Modifiers,
            DelegateDeclarationSyntax delegateDeclaration => delegateDeclaration.Modifiers,
            _ => default
        };
    }

    private static SyntaxList<AttributeListSyntax> GetAttributeLists(MemberDeclarationSyntax declaration)
    {
        return declaration switch
        {
            TypeDeclarationSyntax type => type.AttributeLists,
            EnumDeclarationSyntax enumDeclaration => enumDeclaration.AttributeLists,
            DelegateDeclarationSyntax delegateDeclaration => delegateDeclaration.AttributeLists,
            _ => default
        };
    }

    private static IReadOnlyList<string> TypeParameters(TypeParameterListSyntax? list)
    {
        return list is null
            ? []
            : [.. list.Parameters.Select(parameter => parameter.Identifier.ValueText)];
    }

    private static IReadOnlyList<string> GenericConstraints(SyntaxList<TypeParameterConstraintClauseSyntax> clauses)
    {
        return [.. clauses
            .Select(clause => Normalize(clause.ToString()))
            .OrderBy(value => value, StringComparer.Ordinal)];
    }

    private static IReadOnlyList<PublicApiParameter> Parameters(BaseParameterListSyntax list)
    {
        return [.. list.Parameters.Select((parameter, index) => new PublicApiParameter(
            Name: parameter.Identifier.ValueText,
            Type: Normalize(parameter.Type?.ToString() ?? string.Empty),
            Ordinal: index,
            RefKind: parameter.Modifiers.Select(modifier => modifier.ValueText).FirstOrDefault(value => value is "ref" or "out" or "in"),
            IsOptional: parameter.Default is not null,
            DefaultValue: parameter.Default is null ? null : Normalize(parameter.Default.Value.ToString())))];
    }

    private static IReadOnlyList<string> Attributes(SyntaxList<AttributeListSyntax> lists)
    {
        return [.. lists
            .SelectMany(list => list.Attributes)
            .Select(attribute => Normalize(attribute.ToString()))
            .OrderBy(attribute => attribute, StringComparer.Ordinal)];
    }

    private static IReadOnlyList<string> NullableAnnotations(string signature)
    {
        return signature.Contains('?', StringComparison.Ordinal) ? ["annotated"] : [];
    }

    private static IReadOnlyList<string> DefaultValues(IReadOnlyList<PublicApiParameter> parameters)
    {
        return [.. parameters
            .Where(parameter => parameter.DefaultValue is not null)
            .Select(parameter => $"{parameter.Name}={parameter.DefaultValue}")];
    }

    private static string TypeDisplayName(string name, TypeParameterListSyntax? typeParameterList)
    {
        return typeParameterList is null ? name : $"{name}{Normalize(typeParameterList.ToString())}";
    }

    private static string FullName(string? namespaceName, IReadOnlyList<string> containingTypes, string name)
    {
        return string.Join('.', new[] { namespaceName }.Concat(containingTypes).Append(name).Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string? Container(string? namespaceName, IReadOnlyList<string> containingTypes)
    {
        string value = string.Join('.', new[] { namespaceName }.Concat(containingTypes).Where(part => !string.IsNullOrWhiteSpace(part)));
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string CombineNamespace(string? parent, string child)
    {
        return string.IsNullOrWhiteSpace(parent) ? child : $"{parent}.{child}";
    }

    private static string Normalize(string value)
    {
        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string Hash(string value)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes[..16]).ToLowerInvariant();
    }
}

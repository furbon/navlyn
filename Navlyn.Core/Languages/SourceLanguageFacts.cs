using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;
using CSharp = Microsoft.CodeAnalysis.CSharp;
using CSharpSyntax = Microsoft.CodeAnalysis.CSharp.Syntax;
using VisualBasic = Microsoft.CodeAnalysis.VisualBasic;
using VisualBasicSyntax = Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Navlyn.Languages;

internal static class SourceLanguageFacts
{
    public const string ProjectExtensionDisplay = ".csproj or .vbproj";
    public const string SourceExtensionDisplay = ".cs or .vb";
    public const string WorkspaceExtensionDisplay = "navlyn.workspace.json, .code-workspace, .slnx, .sln, .csproj, or .vbproj";
    public const string SolutionProjectExtensionDisplay = ".slnx, .sln, .csproj, or .vbproj";

    public static bool IsSupportedProjectFile(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".vbproj", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSupportedSourceFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string extension = Path.GetExtension(path);
        return extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".vb", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSupportedProjectLanguage(Project project)
    {
        return project.Language is LanguageNames.CSharp or LanguageNames.VisualBasic;
    }

    public static bool IsSupportedDocument(Document document)
    {
        return document.SupportsSyntaxTree &&
            IsSupportedProjectLanguage(document.Project) &&
            IsSupportedSourceFile(document.FilePath);
    }

    public static IReadOnlyList<IInvocationOperation> EnumerateInvocations(
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

    public static IReadOnlyList<INamedTypeSymbol> EnumerateNamedTypes(
        SyntaxNode root,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return [.. root.DescendantNodes()
            .Select(node =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return semanticModel.GetDeclaredSymbol(node, cancellationToken);
            })
            .OfType<INamedTypeSymbol>()
            .Where(symbol => !symbol.IsImplicitlyDeclared)
            .Where(symbol => symbol.Locations.Any(location => location.IsInSource))
            .GroupBy(SymbolIdentity)
            .Select(group => group.First())
            .OrderBy(symbol => symbol.Locations.FirstOrDefault()?.SourceTree?.FilePath, StringComparer.Ordinal)
            .ThenBy(symbol => symbol.Locations.FirstOrDefault()?.SourceSpan.Start ?? int.MaxValue)];
    }

    public static IReadOnlyList<IMethodSymbol> EnumerateMethods(
        SyntaxNode root,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return [.. root.DescendantNodes()
            .Select(node =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return semanticModel.GetDeclaredSymbol(node, cancellationToken);
            })
            .OfType<IMethodSymbol>()
            .Where(symbol => !symbol.IsImplicitlyDeclared)
            .Where(symbol => symbol.Locations.Any(location => location.IsInSource))
            .GroupBy(SymbolIdentity)
            .Select(group => group.First())
            .OrderBy(symbol => symbol.Locations.FirstOrDefault()?.SourceTree?.FilePath, StringComparer.Ordinal)
            .ThenBy(symbol => symbol.Locations.FirstOrDefault()?.SourceSpan.Start ?? int.MaxValue)];
    }

    public static TOperation? FindOperation<TOperation>(IOperation operation)
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

    public static string ShortAttributeName(AttributeData attribute)
    {
        return ShortAttributeName(attribute.AttributeClass?.Name ?? string.Empty);
    }

    public static string ShortAttributeName(string name)
    {
        string shortName = name.EndsWith("Attribute", StringComparison.Ordinal) ? name[..^"Attribute".Length] : name;
        int dot = shortName.LastIndexOf('.');
        return dot >= 0 ? shortName[(dot + 1)..] : shortName;
    }

    public static bool HasAttribute(ISymbol symbol, string expectedShortName)
    {
        return symbol.GetAttributes()
            .Select(ShortAttributeName)
            .Any(name => name == expectedShortName || name.EndsWith($".{expectedShortName}", StringComparison.Ordinal));
    }

    public static IReadOnlyList<AttributeData> GetAttributes(ISymbol symbol, string expectedShortName)
    {
        return [.. symbol.GetAttributes()
            .Where(attribute =>
            {
                string name = ShortAttributeName(attribute);
                return name == expectedShortName || name.EndsWith($".{expectedShortName}", StringComparison.Ordinal);
            })];
    }

    public static string NormalizeAttributeReason(AttributeData attribute)
    {
        return $"{ShortAttributeName(attribute).ToLowerInvariant()}-attribute";
    }

    public static string? GetFirstStringArgument(AttributeData attribute)
    {
        return attribute.ConstructorArguments
            .Select(TypedConstantToString)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    public static string? GetNamedStringArgument(AttributeData attribute, string name)
    {
        return attribute.NamedArguments
            .Where(argument => argument.Key == name)
            .Select(argument => TypedConstantToString(argument.Value))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    public static string? GetStringArgument(IInvocationOperation invocation, int index)
    {
        return GetOrderedArguments(invocation)
            .Select(argument => argument.Value.ConstantValue.HasValue ? argument.Value.ConstantValue.Value as string : null)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ElementAtOrDefault(index);
    }

    public static IReadOnlyList<string> GetStringArguments(IArgumentOperation? argument)
    {
        if (argument is null)
        {
            return [];
        }

        List<string> values = [];
        CollectStringConstants(argument.Value, values);
        return [.. values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)];
    }

    public static IReadOnlyList<IArgumentOperation> GetOrderedArguments(IInvocationOperation invocation)
    {
        return [.. invocation.Arguments
            .OrderBy(argument => argument.Syntax.SpanStart)
            .ThenBy(argument => argument.Syntax.Span.End)];
    }

    private static string SymbolIdentity(ISymbol symbol)
    {
        Location? location = symbol.Locations.FirstOrDefault(location => location.IsInSource);
        return symbol.GetDocumentationCommentId() ??
            $"{symbol.Kind}:{symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)}:{location?.SourceTree?.FilePath}:{location?.SourceSpan.Start}";
    }

    private static string? TypedConstantToString(TypedConstant constant)
    {
        return constant.Value as string;
    }

    private static void CollectStringConstants(IOperation operation, List<string> values)
    {
        if (operation.ConstantValue.HasValue && operation.ConstantValue.Value is string value)
        {
            values.Add(value);
        }

        foreach (IOperation child in operation.ChildOperations)
        {
            CollectStringConstants(child, values);
        }
    }

    public static bool IsIdentifierToken(SyntaxToken token)
    {
        return token.RawKind == (int)CSharp.SyntaxKind.IdentifierToken ||
            token.RawKind == (int)VisualBasic.SyntaxKind.IdentifierToken;
    }

    public static string GetSyntaxKindName(SyntaxNode node)
    {
        return node.Language switch
        {
            LanguageNames.CSharp => ((CSharp.SyntaxKind)node.RawKind).ToString(),
            LanguageNames.VisualBasic => ((VisualBasic.SyntaxKind)node.RawKind).ToString(),
            _ => node.RawKind.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    public static string GetSourceLanguageLabel(Project project)
    {
        return project.Language switch
        {
            LanguageNames.CSharp => "C#",
            LanguageNames.VisualBasic => "Visual Basic",
            _ => project.Language
        };
    }

    public static bool IsDeclarationNode(SyntaxNode node)
    {
        return IsCSharpDeclarationNode(node) || IsVisualBasicDeclarationNode(node);
    }

    public static bool IsOutlineNode(SyntaxNode node)
    {
        return IsCSharpOutlineNode(node) || IsVisualBasicOutlineNode(node);
    }

    public static string? GetSyntaxName(SyntaxNode node)
    {
        return GetCSharpSyntaxName(node) ?? GetVisualBasicSyntaxName(node);
    }

    public static bool IsFieldVariable(SyntaxNode node)
    {
        return node switch
        {
            CSharpSyntax.VariableDeclaratorSyntax { Parent.Parent: CSharpSyntax.BaseFieldDeclarationSyntax } => true,
            VisualBasicSyntax.ModifiedIdentifierSyntax { Parent.Parent: VisualBasicSyntax.FieldDeclarationSyntax } => true,
            _ => false
        };
    }

    public static SyntaxNode? FindDeclarationNode(SyntaxNode root, ISymbol symbol, Location location)
    {
        SyntaxNode node = root.FindNode(location.SourceSpan, getInnermostNodeForTie: true);
        IEnumerable<SyntaxNode> candidates = node.AncestorsAndSelf();
        return root.Language switch
        {
            LanguageNames.VisualBasic => FindVisualBasicDeclarationNode(candidates, symbol),
            _ => FindCSharpDeclarationNode(candidates, symbol)
        };
    }

    public static IEnumerable<(string TextKind, TextSpan Span)> GetBodySpans(SyntaxNode declaration)
    {
        SyntaxNode? body = declaration switch
        {
            CSharpSyntax.BaseMethodDeclarationSyntax method => (SyntaxNode?)method.Body ?? method.ExpressionBody,
            CSharpSyntax.LocalFunctionStatementSyntax local => (SyntaxNode?)local.Body ?? local.ExpressionBody,
            CSharpSyntax.PropertyDeclarationSyntax property => (SyntaxNode?)property.AccessorList ?? property.ExpressionBody,
            CSharpSyntax.IndexerDeclarationSyntax indexer => (SyntaxNode?)indexer.AccessorList ?? indexer.ExpressionBody,
            CSharpSyntax.AccessorDeclarationSyntax accessor => (SyntaxNode?)accessor.Body ?? accessor.ExpressionBody,
            CSharpSyntax.TypeDeclarationSyntax type => type.Members.Count == 0 ? null : type,
            VisualBasicSyntax.MethodBlockSyntax method => method,
            VisualBasicSyntax.ConstructorBlockSyntax constructor => constructor,
            VisualBasicSyntax.OperatorBlockSyntax operatorBlock => operatorBlock,
            VisualBasicSyntax.PropertyBlockSyntax property => property,
            VisualBasicSyntax.EventBlockSyntax eventBlock => eventBlock,
            VisualBasicSyntax.TypeBlockSyntax type => type.Members.Count == 0 ? null : type,
            VisualBasicSyntax.EnumBlockSyntax enumBlock => enumBlock,
            _ => declaration
        };

        if (body is not null)
        {
            yield return ("body", body.Span);
        }
    }

    public static IEnumerable<(string TextKind, TextSpan Span)> GetMemberSpans(SyntaxNode declaration)
    {
        if (declaration is CSharpSyntax.TypeDeclarationSyntax csharpType)
        {
            foreach (CSharpSyntax.MemberDeclarationSyntax member in csharpType.Members)
            {
                yield return ("member", member.Span);
            }

            yield break;
        }

        if (declaration is CSharpSyntax.CompilationUnitSyntax csharpUnit)
        {
            foreach (CSharpSyntax.MemberDeclarationSyntax member in csharpUnit.Members)
            {
                yield return ("member", member.Span);
            }

            yield break;
        }

        if (declaration is VisualBasicSyntax.TypeBlockSyntax visualBasicType)
        {
            foreach (VisualBasicSyntax.StatementSyntax member in visualBasicType.Members)
            {
                yield return ("member", member.Span);
            }
        }
    }

    public static IEnumerable<(string TextKind, TextSpan Span)> GetXmlDocSpans(SyntaxNode declaration)
    {
        foreach (SyntaxTrivia trivia in declaration.GetLeadingTrivia())
        {
            if (trivia.GetStructure()?.GetType().Name == "DocumentationCommentTriviaSyntax")
            {
                yield return ("xml-doc", trivia.FullSpan);
            }
        }
    }

    public static IEnumerable<(string TextKind, TextSpan Span)> GetAttributeSpans(SyntaxNode declaration)
    {
        foreach (SyntaxNode node in declaration.ChildNodes())
        {
            if (node.GetType().Name == "AttributeListSyntax")
            {
                yield return ("attributes", node.Span);
            }
        }
    }

    public static IReadOnlyList<string> GetModifierTexts(SyntaxNode node)
    {
        object? modifiers = node.GetType().GetProperty("Modifiers")?.GetValue(node);
        return modifiers is SyntaxTokenList tokenList
            ? [.. tokenList
                .Select(token => token.ValueText)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)]
            : [];
    }

    public static bool IsPartialDeclaration(SyntaxNode node)
    {
        return GetModifierTexts(node).Any(modifier => modifier.Equals("partial", StringComparison.OrdinalIgnoreCase));
    }

    public static string? GetScopeFrameKind(SyntaxNode node)
    {
        return node switch
        {
            CSharpSyntax.FileScopedNamespaceDeclarationSyntax => "Namespace",
            CSharpSyntax.NamespaceDeclarationSyntax => "Namespace",
            CSharpSyntax.TypeDeclarationSyntax => "Type",
            CSharpSyntax.DelegateDeclarationSyntax => "Delegate",
            CSharpSyntax.EnumDeclarationSyntax => "Enum",
            CSharpSyntax.BaseMethodDeclarationSyntax => "Member",
            CSharpSyntax.PropertyDeclarationSyntax => "Member",
            CSharpSyntax.IndexerDeclarationSyntax => "Member",
            CSharpSyntax.EventDeclarationSyntax => "Member",
            CSharpSyntax.EventFieldDeclarationSyntax => "Member",
            CSharpSyntax.FieldDeclarationSyntax => "Field",
            CSharpSyntax.LocalFunctionStatementSyntax => "LocalFunction",
            CSharpSyntax.AnonymousFunctionExpressionSyntax => "Lambda",
            CSharpSyntax.GlobalStatementSyntax => "TopLevelStatement",
            VisualBasicSyntax.NamespaceBlockSyntax => "Namespace",
            VisualBasicSyntax.NamespaceStatementSyntax => "Namespace",
            VisualBasicSyntax.TypeBlockSyntax => "Type",
            VisualBasicSyntax.TypeStatementSyntax => "Type",
            VisualBasicSyntax.DelegateStatementSyntax => "Delegate",
            VisualBasicSyntax.EnumBlockSyntax => "Enum",
            VisualBasicSyntax.EnumStatementSyntax => "Enum",
            VisualBasicSyntax.MethodBlockSyntax => "Member",
            VisualBasicSyntax.MethodStatementSyntax => "Member",
            VisualBasicSyntax.ConstructorBlockSyntax => "Member",
            VisualBasicSyntax.SubNewStatementSyntax => "Member",
            VisualBasicSyntax.OperatorBlockSyntax => "Member",
            VisualBasicSyntax.OperatorStatementSyntax => "Member",
            VisualBasicSyntax.PropertyBlockSyntax => "Member",
            VisualBasicSyntax.PropertyStatementSyntax => "Member",
            VisualBasicSyntax.EventBlockSyntax => "Member",
            VisualBasicSyntax.EventStatementSyntax => "Member",
            VisualBasicSyntax.FieldDeclarationSyntax => "Field",
            VisualBasicSyntax.LambdaExpressionSyntax => "Lambda",
            _ => null
        };
    }

    public static SyntaxNode? FindContainingExpression(SyntaxToken token, int position)
    {
        return token.Parent?
            .AncestorsAndSelf()
            .Where(node => IsExpressionNode(node) && node.Span.Contains(position))
            .OrderBy(node => node.Span.Length)
            .FirstOrDefault();
    }

    public static bool IsExpressionNode(SyntaxNode node)
    {
        return node is CSharpSyntax.ExpressionSyntax or VisualBasicSyntax.ExpressionSyntax;
    }

    public static SyntaxNode? FindAttributeNode(SyntaxToken token, int position)
    {
        return token.Parent?
            .AncestorsAndSelf()
            .FirstOrDefault(node => node.Span.Contains(position) && node.GetType().Name == "AttributeSyntax");
    }

    public static SyntaxNode? FindReturnNode(SyntaxToken token, int position)
    {
        return token.Parent?
            .AncestorsAndSelf()
            .FirstOrDefault(node =>
                node.Span.Contains(position) &&
                node is CSharpSyntax.ReturnStatementSyntax or CSharpSyntax.ArrowExpressionClauseSyntax or VisualBasicSyntax.ReturnStatementSyntax);
    }

    public static SyntaxNode? GetReturnExpression(SyntaxNode returnNode)
    {
        return returnNode switch
        {
            CSharpSyntax.ReturnStatementSyntax returnStatement => returnStatement.Expression,
            CSharpSyntax.ArrowExpressionClauseSyntax arrow => arrow.Expression,
            VisualBasicSyntax.ReturnStatementSyntax returnStatement => returnStatement.Expression,
            _ => null
        };
    }

    public static SyntaxNode? FindLambdaNode(SyntaxToken token, int position)
    {
        return token.Parent?
            .AncestorsAndSelf()
            .FirstOrDefault(node =>
                node.Span.Contains(position) &&
                node is CSharpSyntax.AnonymousFunctionExpressionSyntax or VisualBasicSyntax.LambdaExpressionSyntax);
    }

    private static bool IsCSharpDeclarationNode(SyntaxNode node)
    {
        return node is CSharpSyntax.BaseTypeDeclarationSyntax
            or CSharpSyntax.BaseNamespaceDeclarationSyntax
            or CSharpSyntax.DelegateDeclarationSyntax
            or CSharpSyntax.EnumMemberDeclarationSyntax
            or CSharpSyntax.BaseMethodDeclarationSyntax
            or CSharpSyntax.LocalFunctionStatementSyntax
            or CSharpSyntax.PropertyDeclarationSyntax
            or CSharpSyntax.IndexerDeclarationSyntax
            or CSharpSyntax.EventDeclarationSyntax
            or CSharpSyntax.UsingDirectiveSyntax
            or CSharpSyntax.VariableDeclaratorSyntax
            or CSharpSyntax.ForEachStatementSyntax
            or CSharpSyntax.ParameterSyntax
            or CSharpSyntax.TypeParameterSyntax
            or CSharpSyntax.SingleVariableDesignationSyntax;
    }

    private static bool IsVisualBasicDeclarationNode(SyntaxNode node)
    {
        return node is VisualBasicSyntax.NamespaceStatementSyntax
            or VisualBasicSyntax.TypeStatementSyntax
            or VisualBasicSyntax.EnumStatementSyntax
            or VisualBasicSyntax.EnumMemberDeclarationSyntax
            or VisualBasicSyntax.DelegateStatementSyntax
            or VisualBasicSyntax.MethodStatementSyntax
            or VisualBasicSyntax.SubNewStatementSyntax
            or VisualBasicSyntax.OperatorStatementSyntax
            or VisualBasicSyntax.PropertyStatementSyntax
            or VisualBasicSyntax.EventStatementSyntax
            or VisualBasicSyntax.ModifiedIdentifierSyntax
            or VisualBasicSyntax.ParameterSyntax
            or VisualBasicSyntax.TypeParameterSyntax;
    }

    private static bool IsCSharpOutlineNode(SyntaxNode node)
    {
        return node switch
        {
            CSharpSyntax.BaseTypeDeclarationSyntax
                or CSharpSyntax.BaseNamespaceDeclarationSyntax
                or CSharpSyntax.DelegateDeclarationSyntax
                or CSharpSyntax.EnumMemberDeclarationSyntax
                or CSharpSyntax.BaseMethodDeclarationSyntax
                or CSharpSyntax.LocalFunctionStatementSyntax
                or CSharpSyntax.PropertyDeclarationSyntax
                or CSharpSyntax.IndexerDeclarationSyntax
                or CSharpSyntax.EventDeclarationSyntax => true,
            CSharpSyntax.VariableDeclaratorSyntax => IsFieldVariable(node),
            _ => false
        };
    }

    private static bool IsVisualBasicOutlineNode(SyntaxNode node)
    {
        return node switch
        {
            VisualBasicSyntax.NamespaceStatementSyntax
                or VisualBasicSyntax.TypeStatementSyntax
                or VisualBasicSyntax.EnumStatementSyntax
                or VisualBasicSyntax.EnumMemberDeclarationSyntax
                or VisualBasicSyntax.DelegateStatementSyntax
                or VisualBasicSyntax.MethodStatementSyntax
                or VisualBasicSyntax.SubNewStatementSyntax
                or VisualBasicSyntax.OperatorStatementSyntax
                or VisualBasicSyntax.PropertyStatementSyntax
                or VisualBasicSyntax.EventStatementSyntax => true,
            VisualBasicSyntax.ModifiedIdentifierSyntax => IsFieldVariable(node),
            _ => false
        };
    }

    private static string? GetCSharpSyntaxName(SyntaxNode node)
    {
        return node switch
        {
            CSharpSyntax.BaseTypeDeclarationSyntax declaration => declaration.Identifier.ValueText,
            CSharpSyntax.BaseNamespaceDeclarationSyntax declaration => declaration.Name.ToString(),
            CSharpSyntax.DelegateDeclarationSyntax declaration => declaration.Identifier.ValueText,
            CSharpSyntax.EnumMemberDeclarationSyntax declaration => declaration.Identifier.ValueText,
            CSharpSyntax.ConstructorDeclarationSyntax declaration => declaration.Identifier.ValueText,
            CSharpSyntax.DestructorDeclarationSyntax declaration => declaration.Identifier.ValueText,
            CSharpSyntax.MethodDeclarationSyntax declaration => declaration.Identifier.ValueText,
            CSharpSyntax.OperatorDeclarationSyntax declaration => declaration.OperatorToken.ValueText,
            CSharpSyntax.ConversionOperatorDeclarationSyntax declaration => declaration.Type.ToString(),
            CSharpSyntax.LocalFunctionStatementSyntax declaration => declaration.Identifier.ValueText,
            CSharpSyntax.PropertyDeclarationSyntax declaration => declaration.Identifier.ValueText,
            CSharpSyntax.IndexerDeclarationSyntax => "this",
            CSharpSyntax.EventDeclarationSyntax declaration => declaration.Identifier.ValueText,
            CSharpSyntax.UsingDirectiveSyntax declaration => declaration.Alias?.Name.Identifier.ValueText ?? declaration.Name?.ToString(),
            CSharpSyntax.VariableDeclaratorSyntax declaration => declaration.Identifier.ValueText,
            CSharpSyntax.ForEachStatementSyntax declaration => declaration.Identifier.ValueText,
            CSharpSyntax.ParameterSyntax declaration => declaration.Identifier.ValueText,
            CSharpSyntax.TypeParameterSyntax declaration => declaration.Identifier.ValueText,
            CSharpSyntax.SingleVariableDesignationSyntax declaration => declaration.Identifier.ValueText,
            _ => null
        };
    }

    private static string? GetVisualBasicSyntaxName(SyntaxNode node)
    {
        return node switch
        {
            VisualBasicSyntax.NamespaceStatementSyntax declaration => declaration.Name.ToString(),
            VisualBasicSyntax.TypeStatementSyntax declaration => declaration.Identifier.ValueText,
            VisualBasicSyntax.EnumStatementSyntax declaration => declaration.Identifier.ValueText,
            VisualBasicSyntax.EnumMemberDeclarationSyntax declaration => declaration.Identifier.ValueText,
            VisualBasicSyntax.DelegateStatementSyntax declaration => declaration.Identifier.ValueText,
            VisualBasicSyntax.MethodStatementSyntax declaration => declaration.Identifier.ValueText,
            VisualBasicSyntax.SubNewStatementSyntax => "New",
            VisualBasicSyntax.OperatorStatementSyntax declaration => declaration.OperatorToken.ValueText,
            VisualBasicSyntax.PropertyStatementSyntax declaration => declaration.Identifier.ValueText,
            VisualBasicSyntax.EventStatementSyntax declaration => declaration.Identifier.ValueText,
            VisualBasicSyntax.ModifiedIdentifierSyntax declaration => declaration.Identifier.ValueText,
            VisualBasicSyntax.ParameterSyntax declaration => declaration.Identifier.Identifier.ValueText,
            VisualBasicSyntax.TypeParameterSyntax declaration => declaration.Identifier.ValueText,
            _ => null
        };
    }

    private static SyntaxNode? FindCSharpDeclarationNode(IEnumerable<SyntaxNode> candidates, ISymbol symbol)
    {
        return symbol switch
        {
            INamedTypeSymbol { TypeKind: TypeKind.Delegate } => candidates.OfType<CSharpSyntax.DelegateDeclarationSyntax>().FirstOrDefault(),
            INamedTypeSymbol { TypeKind: TypeKind.Enum } => candidates.OfType<CSharpSyntax.EnumDeclarationSyntax>().FirstOrDefault(),
            INamedTypeSymbol => candidates.OfType<CSharpSyntax.TypeDeclarationSyntax>().FirstOrDefault(),
            IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor } => candidates.OfType<CSharpSyntax.ConstructorDeclarationSyntax>().FirstOrDefault(),
            IMethodSymbol { MethodKind: MethodKind.LocalFunction } => candidates.OfType<CSharpSyntax.LocalFunctionStatementSyntax>().FirstOrDefault(),
            IMethodSymbol => candidates.OfType<CSharpSyntax.MethodDeclarationSyntax>().FirstOrDefault<SyntaxNode>() ?? candidates.OfType<CSharpSyntax.OperatorDeclarationSyntax>().FirstOrDefault<SyntaxNode>(),
            IPropertySymbol { IsIndexer: true } => candidates.OfType<CSharpSyntax.IndexerDeclarationSyntax>().FirstOrDefault(),
            IPropertySymbol => candidates.OfType<CSharpSyntax.PropertyDeclarationSyntax>().FirstOrDefault(),
            IEventSymbol => candidates.OfType<CSharpSyntax.EventDeclarationSyntax>().FirstOrDefault<SyntaxNode>() ?? candidates.OfType<CSharpSyntax.EventFieldDeclarationSyntax>().FirstOrDefault<SyntaxNode>(),
            IFieldSymbol => candidates.OfType<CSharpSyntax.FieldDeclarationSyntax>().FirstOrDefault<SyntaxNode>() ?? candidates.OfType<CSharpSyntax.VariableDeclaratorSyntax>().FirstOrDefault<SyntaxNode>(),
            IParameterSymbol => candidates.OfType<CSharpSyntax.ParameterSyntax>().FirstOrDefault(),
            ILocalSymbol => candidates.OfType<CSharpSyntax.VariableDeclaratorSyntax>().FirstOrDefault(),
            _ => candidates.FirstOrDefault(candidate => candidate is CSharpSyntax.MemberDeclarationSyntax)
        };
    }

    private static SyntaxNode? FindVisualBasicDeclarationNode(IEnumerable<SyntaxNode> candidates, ISymbol symbol)
    {
        return symbol switch
        {
            INamedTypeSymbol { TypeKind: TypeKind.Delegate } => candidates.OfType<VisualBasicSyntax.DelegateStatementSyntax>().FirstOrDefault(),
            INamedTypeSymbol { TypeKind: TypeKind.Enum } => candidates.OfType<VisualBasicSyntax.EnumBlockSyntax>().FirstOrDefault<SyntaxNode>() ?? candidates.OfType<VisualBasicSyntax.EnumStatementSyntax>().FirstOrDefault<SyntaxNode>(),
            INamedTypeSymbol => candidates.OfType<VisualBasicSyntax.TypeBlockSyntax>().FirstOrDefault<SyntaxNode>() ?? candidates.OfType<VisualBasicSyntax.TypeStatementSyntax>().FirstOrDefault<SyntaxNode>(),
            IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor } => candidates.OfType<VisualBasicSyntax.ConstructorBlockSyntax>().FirstOrDefault<SyntaxNode>() ?? candidates.OfType<VisualBasicSyntax.SubNewStatementSyntax>().FirstOrDefault<SyntaxNode>(),
            IMethodSymbol => candidates.OfType<VisualBasicSyntax.MethodBlockSyntax>().FirstOrDefault<SyntaxNode>() ?? candidates.OfType<VisualBasicSyntax.OperatorBlockSyntax>().FirstOrDefault<SyntaxNode>() ?? candidates.OfType<VisualBasicSyntax.MethodStatementSyntax>().FirstOrDefault<SyntaxNode>() ?? candidates.OfType<VisualBasicSyntax.OperatorStatementSyntax>().FirstOrDefault<SyntaxNode>(),
            IPropertySymbol => candidates.OfType<VisualBasicSyntax.PropertyBlockSyntax>().FirstOrDefault<SyntaxNode>() ?? candidates.OfType<VisualBasicSyntax.PropertyStatementSyntax>().FirstOrDefault<SyntaxNode>(),
            IEventSymbol => candidates.OfType<VisualBasicSyntax.EventBlockSyntax>().FirstOrDefault<SyntaxNode>() ?? candidates.OfType<VisualBasicSyntax.EventStatementSyntax>().FirstOrDefault<SyntaxNode>(),
            IFieldSymbol => candidates.OfType<VisualBasicSyntax.FieldDeclarationSyntax>().FirstOrDefault<SyntaxNode>() ?? candidates.OfType<VisualBasicSyntax.ModifiedIdentifierSyntax>().FirstOrDefault<SyntaxNode>(),
            IParameterSymbol => candidates.OfType<VisualBasicSyntax.ParameterSyntax>().FirstOrDefault(),
            ILocalSymbol => candidates.OfType<VisualBasicSyntax.ModifiedIdentifierSyntax>().FirstOrDefault(),
            _ => candidates.FirstOrDefault(candidate => candidate is VisualBasicSyntax.StatementSyntax)
        };
    }
}

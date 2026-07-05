using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;
using Navlyn.Diagnostics;
using Navlyn.Languages;
using Navlyn.Paths;

namespace Navlyn.Symbols;

internal sealed class SymbolInfoResolver
{
    public async Task<SymbolInfoResolutionResult> ResolveAsync(
        Solution solution,
        FileInfo file,
        int line,
        int column,
        Project? project,
        bool excludeGenerated,
        CancellationToken cancellationToken)
    {
        SourceDocumentResolutionResult documentResult =
            await new SourceDocumentResolver().ResolveAsync(solution, file, project, excludeGenerated, cancellationToken);

        if (documentResult.Error is not null)
        {
            return SymbolInfoResolutionResult.Failed(documentResult.Error);
        }

        SourceDocumentResolution sourceDocument = documentResult.Resolution!;
        if (!TryGetPosition(sourceDocument.Text, line, column, out int position, out string? positionError))
        {
            return SymbolInfoResolutionResult.Failed(
                DiagnosticIds.InvalidSourcePosition,
                positionError!,
                ExitCodes.UsageError);
        }

        SyntaxNode? root = await sourceDocument.Document.GetSyntaxRootAsync(cancellationToken);
        SemanticModel? semanticModel = await sourceDocument.Document.GetSemanticModelAsync(cancellationToken);
        if (root is null || semanticModel is null)
        {
            return SymbolInfoResolutionResult.Failed(
                DiagnosticIds.SymbolNotFoundAtPosition,
                $"No supported source symbol found at {sourceDocument.DisplayPath}:{line}:{column}.",
                ExitCodes.UsageError);
        }

        SyntaxToken token = root.FindToken(position);
        if (!token.Span.Contains(position))
        {
            return SymbolInfoResolutionResult.Failed(
                DiagnosticIds.SymbolNotFoundAtPosition,
                $"No supported source symbol found at {sourceDocument.DisplayPath}:{line}:{column}.",
                ExitCodes.UsageError);
        }

        ISymbol? symbol = SymbolNavigationFacts.ResolveSymbol(semanticModel, token, position, cancellationToken);
        if (symbol is null)
        {
            return SymbolInfoResolutionResult.Failed(
                DiagnosticIds.SymbolNotFoundAtPosition,
                $"No supported source symbol found at {sourceDocument.DisplayPath}:{line}:{column}.",
                ExitCodes.UsageError);
        }

        string projectName = sourceDocument.Document.Project.Name;
        SyntaxNode? expression = SourceLanguageFacts.FindContainingExpression(token, position);
        ISymbol? containingSymbol = semanticModel.GetEnclosingSymbol(position, cancellationToken);
        if (containingSymbol is not null)
        {
            containingSymbol = SymbolNavigationFacts.NormalizeSourceNavigationSymbol(containingSymbol);
        }

        return SymbolInfoResolutionResult.Succeeded(new SymbolInfoResolution(
            File: sourceDocument.DisplayPath,
            Line: line,
            Column: column,
            Symbol: CreateSymbol(symbol, root.SyntaxTree, position, projectName, excludeGenerated),
            Expression: expression is null ? null : CreateExpressionInfo(semanticModel, expression, cancellationToken),
            ContainingSymbol: containingSymbol is null
                ? null
                : CreateSymbol(containingSymbol, root.SyntaxTree, position, projectName, excludeGenerated),
            Invocation: CreateInvocationInfo(semanticModel, token, position, cancellationToken),
            Attribute: CreateAttributeInfo(semanticModel, token, position, projectName, excludeGenerated, cancellationToken),
            Return: CreateReturnInfo(semanticModel, token, position, cancellationToken),
            Lambda: CreateLambdaInfo(semanticModel, token, position, cancellationToken)));
    }

    private static SymbolInfoSymbol CreateSymbol(
        ISymbol symbol,
        SyntaxTree syntaxTree,
        int position,
        string projectName,
        bool excludeGenerated)
    {
        Location? location = SymbolNavigationFacts.GetBestSourceLocation(symbol, syntaxTree, position, excludeGenerated);
        SymbolSourceLocation? sourceLocation = location is null
            ? null
            : SymbolNavigationFacts.CreateSourceLocation(location, excludeGenerated);

        return new SymbolInfoSymbol(
            Name: symbol.Name,
            Kind: symbol.Kind.ToString(),
            Container: SymbolNavigationFacts.GetContainer(symbol),
            Facts: SymbolFactsBuilder.Create(symbol, projectName),
            Path: sourceLocation?.Path,
            Line: sourceLocation?.Line,
            Column: sourceLocation?.Column,
            EndLine: sourceLocation?.EndLine,
            EndColumn: sourceLocation?.EndColumn);
    }

    private static SymbolExpressionInfo CreateExpressionInfo(
        SemanticModel semanticModel,
        SyntaxNode expression,
        CancellationToken cancellationToken)
    {
        TypeInfo typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
        TextLocationInfo location = CreateLocation(expression.GetLocation());
        return new SymbolExpressionInfo(
            Kind: SourceLanguageFacts.GetSyntaxKindName(expression),
            Path: location.Path,
            Line: location.Line,
            Column: location.Column,
            EndLine: location.EndLine,
            EndColumn: location.EndColumn,
            Type: SymbolFactsBuilder.CreateType(typeInfo.Type),
            ConvertedType: SymbolFactsBuilder.CreateType(typeInfo.ConvertedType));
    }

    private static SymbolInvocationInfo? CreateInvocationInfo(
        SemanticModel semanticModel,
        SyntaxToken token,
        int position,
        CancellationToken cancellationToken)
    {
        foreach (SyntaxNode node in token.Parent?.AncestorsAndSelf() ?? [])
        {
            if (!node.Span.Contains(position))
            {
                continue;
            }

            IOperation? operation = semanticModel.GetOperation(node, cancellationToken);
            if (operation is IInvocationOperation invocation)
            {
                return new SymbolInvocationInfo(
                    Kind: "Invocation",
                    Target: SymbolFactsBuilder.Create(invocation.TargetMethod),
                    ConstructedType: null,
                    ExtensionReceiver: invocation.TargetMethod.ReducedFrom is null || invocation.Instance is null
                        ? null
                        : CreateOperationValueInfo(invocation.Instance),
                    Arguments: CreateArguments(invocation.Arguments));
            }

            if (operation is IObjectCreationOperation creation)
            {
                return new SymbolInvocationInfo(
                    Kind: "ObjectCreation",
                    Target: creation.Constructor is null ? null : SymbolFactsBuilder.Create(creation.Constructor),
                    ConstructedType: SymbolFactsBuilder.CreateType(creation.Type),
                    ExtensionReceiver: null,
                    Arguments: CreateArguments(creation.Arguments));
            }
        }

        return null;
    }

    private static SymbolAttributeInfo? CreateAttributeInfo(
        SemanticModel semanticModel,
        SyntaxToken token,
        int position,
        string projectName,
        bool excludeGenerated,
        CancellationToken cancellationToken)
    {
        SyntaxNode? attributeSyntax = SourceLanguageFacts.FindAttributeNode(token, position);

        if (attributeSyntax is null)
        {
            return null;
        }

        SymbolInfo symbolInfo = semanticModel.GetSymbolInfo(attributeSyntax, cancellationToken);
        IMethodSymbol? constructor = symbolInfo.Symbol as IMethodSymbol;
        INamedTypeSymbol? attributeType = constructor?.ContainingType;
        TextLocationInfo location = CreateLocation(attributeSyntax.GetLocation());

        return new SymbolAttributeInfo(
            Path: location.Path,
            Line: location.Line,
            Column: location.Column,
            EndLine: location.EndLine,
            EndColumn: location.EndColumn,
            Type: attributeType is null ? null : SymbolFactsBuilder.Create(attributeType, projectName),
            Constructor: constructor is null
                ? null
                : CreateSymbol(constructor, attributeSyntax.SyntaxTree, position, projectName, excludeGenerated));
    }

    private static SymbolReturnInfo? CreateReturnInfo(
        SemanticModel semanticModel,
        SyntaxToken token,
        int position,
        CancellationToken cancellationToken)
    {
        SyntaxNode? returnNode = SourceLanguageFacts.FindReturnNode(token, position);

        if (returnNode is null)
        {
            return null;
        }

        SyntaxNode? expression = SourceLanguageFacts.GetReturnExpression(returnNode);

        ISymbol? containingSymbol = semanticModel.GetEnclosingSymbol(position, cancellationToken);
        ITypeSymbol? declaredReturnType = containingSymbol switch
        {
            IMethodSymbol method => method.ReturnType,
            IPropertySymbol property => property.Type,
            _ => null
        };

        TypeInfo typeInfo = expression is null
            ? default
            : semanticModel.GetTypeInfo(expression, cancellationToken);

        return new SymbolReturnInfo(
            DeclaredReturnType: SymbolFactsBuilder.CreateType(declaredReturnType),
            ExpressionType: SymbolFactsBuilder.CreateType(typeInfo.Type),
            ConvertedType: SymbolFactsBuilder.CreateType(typeInfo.ConvertedType));
    }

    private static SymbolLambdaInfo? CreateLambdaInfo(
        SemanticModel semanticModel,
        SyntaxToken token,
        int position,
        CancellationToken cancellationToken)
    {
        SyntaxNode? lambdaSyntax = SourceLanguageFacts.FindLambdaNode(token, position);

        if (lambdaSyntax is null)
        {
            return null;
        }

        TypeInfo typeInfo = semanticModel.GetTypeInfo(lambdaSyntax, cancellationToken);
        IOperation? operation = semanticModel.GetOperation(lambdaSyntax, cancellationToken);
        IMethodSymbol? lambdaSymbol = operation is IAnonymousFunctionOperation lambdaOperation
            ? lambdaOperation.Symbol
            : null;

        return new SymbolLambdaInfo(
            TargetType: SymbolFactsBuilder.CreateType(typeInfo.ConvertedType),
            ReturnType: SymbolFactsBuilder.CreateType(lambdaSymbol?.ReturnType),
            Parameters: lambdaSymbol?.Parameters.Select(CreateParameterInfo).ToArray());
    }

    private static IReadOnlyList<SymbolArgumentInfo> CreateArguments(ImmutableArray<IArgumentOperation> arguments)
    {
        return [.. arguments
            .OrderBy(argument => argument.Syntax.SpanStart)
            .Select(CreateArgument)];
    }

    private static SymbolArgumentInfo CreateArgument(IArgumentOperation argument)
    {
        TextLocationInfo location = CreateLocation(argument.Syntax.GetLocation());
        return new SymbolArgumentInfo(
            Path: location.Path,
            Line: location.Line,
            Column: location.Column,
            EndLine: location.EndLine,
            EndColumn: location.EndColumn,
            ArgumentKind: argument.ArgumentKind.ToString(),
            Parameter: argument.Parameter is null ? null : CreateParameterInfo(argument.Parameter),
            ValueType: SymbolFactsBuilder.CreateType(argument.Value.Type),
            IsImplicit: argument.IsImplicit);
    }

    private static SymbolParameterBindingInfo CreateParameterInfo(IParameterSymbol parameter)
    {
        return new SymbolParameterBindingInfo(
            Name: parameter.Name,
            Ordinal: parameter.Ordinal,
            Type: SymbolFactsBuilder.CreateType(parameter.Type),
            RefKind: parameter.RefKind == RefKind.None ? null : parameter.RefKind.ToString(),
            IsOptional: parameter.IsOptional,
            IsParams: parameter.IsParams,
            HasExplicitDefaultValue: parameter.HasExplicitDefaultValue,
            ExplicitDefaultValue: parameter.HasExplicitDefaultValue
                ? parameter.ExplicitDefaultValue?.ToString()
                : null);
    }

    private static SymbolOperationValueInfo CreateOperationValueInfo(IOperation operation)
    {
        TextLocationInfo location = CreateLocation(operation.Syntax.GetLocation());
        return new SymbolOperationValueInfo(
            Path: location.Path,
            Line: location.Line,
            Column: location.Column,
            EndLine: location.EndLine,
            EndColumn: location.EndColumn,
            Type: SymbolFactsBuilder.CreateType(operation.Type));
    }

    private static TextLocationInfo CreateLocation(Location location)
    {
        FileLinePositionSpan lineSpan = location.GetLineSpan();
        return new TextLocationInfo(
            Path: PathDisplay.FromCurrentDirectory(lineSpan.Path),
            Line: lineSpan.StartLinePosition.Line + 1,
            Column: lineSpan.StartLinePosition.Character + 1,
            EndLine: lineSpan.EndLinePosition.Line + 1,
            EndColumn: lineSpan.EndLinePosition.Character + 1);
    }

    private static bool TryGetPosition(
        SourceText text,
        int line,
        int column,
        out int position,
        out string? error)
    {
        position = 0;
        error = null;

        if (line < 1)
        {
            error = $"Line must be 1 or greater. Actual value: {line}.";
            return false;
        }

        if (column < 1)
        {
            error = $"Column must be 1 or greater. Actual value: {column}.";
            return false;
        }

        if (line > text.Lines.Count)
        {
            error = $"Line {line} is outside the source file. The file has {text.Lines.Count} lines.";
            return false;
        }

        TextLine textLine = text.Lines[line - 1];
        int lineLength = textLine.End - textLine.Start;
        int maxColumn = lineLength + 1;
        if (column > maxColumn)
        {
            error = $"Column {column} is outside line {line}. The maximum column is {maxColumn}.";
            return false;
        }

        position = textLine.Start + column - 1;
        return true;
    }
}

internal sealed record SymbolInfoResolutionResult(SymbolInfoResolution? Resolution, SymbolInfoResolutionError? Error)
{
    public static SymbolInfoResolutionResult Succeeded(SymbolInfoResolution resolution)
    {
        return new SymbolInfoResolutionResult(resolution, Error: null);
    }

    public static SymbolInfoResolutionResult Failed(int diagnosticId, string message, int exitCode)
    {
        return new SymbolInfoResolutionResult(
            Resolution: null,
            Error: new SymbolInfoResolutionError(diagnosticId, message, exitCode));
    }

    public static SymbolInfoResolutionResult Failed(SymbolNavigationError error)
    {
        return Failed(error.DiagnosticId, error.Message, error.ExitCode);
    }
}

internal sealed record SymbolInfoResolution(
    string File,
    int Line,
    int Column,
    SymbolInfoSymbol Symbol,
    SymbolExpressionInfo? Expression,
    SymbolInfoSymbol? ContainingSymbol,
    SymbolInvocationInfo? Invocation,
    SymbolAttributeInfo? Attribute,
    SymbolReturnInfo? Return,
    SymbolLambdaInfo? Lambda);

internal sealed record SymbolInfoSymbol(
    string Name,
    string Kind,
    string? Container,
    SymbolFacts Facts,
    string? Path,
    int? Line,
    int? Column,
    int? EndLine,
    int? EndColumn);

internal sealed record SymbolExpressionInfo(
    string Kind,
    string Path,
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    SymbolTypeFacts? Type,
    SymbolTypeFacts? ConvertedType);

internal sealed record SymbolInvocationInfo(
    string Kind,
    SymbolFacts? Target,
    SymbolTypeFacts? ConstructedType,
    SymbolOperationValueInfo? ExtensionReceiver,
    IReadOnlyList<SymbolArgumentInfo> Arguments);

internal sealed record SymbolArgumentInfo(
    string Path,
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    string ArgumentKind,
    SymbolParameterBindingInfo? Parameter,
    SymbolTypeFacts? ValueType,
    bool IsImplicit);

internal sealed record SymbolParameterBindingInfo(
    string Name,
    int Ordinal,
    SymbolTypeFacts? Type,
    string? RefKind,
    bool IsOptional,
    bool IsParams,
    bool HasExplicitDefaultValue,
    string? ExplicitDefaultValue);

internal sealed record SymbolOperationValueInfo(
    string Path,
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    SymbolTypeFacts? Type);

internal sealed record SymbolAttributeInfo(
    string Path,
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    SymbolFacts? Type,
    SymbolInfoSymbol? Constructor);

internal sealed record SymbolReturnInfo(
    SymbolTypeFacts? DeclaredReturnType,
    SymbolTypeFacts? ExpressionType,
    SymbolTypeFacts? ConvertedType);

internal sealed record SymbolLambdaInfo(
    SymbolTypeFacts? TargetType,
    SymbolTypeFacts? ReturnType,
    IReadOnlyList<SymbolParameterBindingInfo>? Parameters);

internal sealed record TextLocationInfo(string Path, int Line, int Column, int EndLine, int EndColumn);

internal sealed record SymbolInfoResolutionError(int DiagnosticId, string Message, int ExitCode);

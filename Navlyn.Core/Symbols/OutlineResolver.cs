using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Navlyn.Symbols;

internal sealed class OutlineResolver
{
    public async Task<OutlineResolutionResult> ResolveAsync(
        Solution solution,
        FileInfo file,
        Project? project,
        bool excludeGenerated,
        CancellationToken cancellationToken)
    {
        SourceDocumentResolutionResult documentResult =
            await new SourceDocumentResolver().ResolveAsync(solution, file, project, excludeGenerated, cancellationToken);

        if (documentResult.Error is not null)
        {
            return OutlineResolutionResult.Failed(documentResult.Error);
        }

        SourceDocumentResolution sourceDocument = documentResult.Resolution!;
        SyntaxNode? root = await sourceDocument.Document.GetSyntaxRootAsync(cancellationToken);
        SemanticModel? semanticModel = await sourceDocument.Document.GetSemanticModelAsync(cancellationToken);
        if (root is null || semanticModel is null)
        {
            return OutlineResolutionResult.Succeeded(new OutlineResolution(
                File: sourceDocument.DisplayPath,
                Entries: []));
        }

        IReadOnlyList<OutlineEntry> entries = [.. root
            .DescendantNodes()
            .Where(IsOutlineNode)
            .Select(node => CreateEntry(sourceDocument, semanticModel, node, cancellationToken))
            .OfType<OutlineEntry>()
            .OrderBy(entry => entry.Line)
            .ThenBy(entry => entry.Column)
            .ThenBy(entry => entry.EndLine)
            .ThenBy(entry => entry.EndColumn)
            .ThenBy(entry => entry.Name, StringComparer.Ordinal)
            .ThenBy(entry => entry.Kind, StringComparer.Ordinal)];

        return OutlineResolutionResult.Succeeded(new OutlineResolution(
            File: sourceDocument.DisplayPath,
            Entries: entries));
    }

    private static OutlineEntry? CreateEntry(
        SourceDocumentResolution sourceDocument,
        SemanticModel semanticModel,
        SyntaxNode node,
        CancellationToken cancellationToken)
    {
        ISymbol? symbol = semanticModel.GetDeclaredSymbol(node, cancellationToken);
        if (symbol is null)
        {
            return null;
        }

        TextSpan span = node.Span;
        FileLinePositionSpan lineSpan = node.SyntaxTree.GetLineSpan(span, cancellationToken);
        if (!lineSpan.IsValid)
        {
            return null;
        }

        return new OutlineEntry(
            Name: symbol.Name,
            Kind: symbol.Kind.ToString(),
            Container: SymbolNavigationFacts.GetContainer(symbol),
            Facts: SymbolFactsBuilder.Create(symbol, sourceDocument.Document.Project.Name),
            Path: sourceDocument.DisplayPath,
            Line: lineSpan.StartLinePosition.Line + 1,
            Column: lineSpan.StartLinePosition.Character + 1,
            EndLine: lineSpan.EndLinePosition.Line + 1,
            EndColumn: lineSpan.EndLinePosition.Character + 1);
    }

    private static bool IsOutlineNode(SyntaxNode node)
    {
        return node switch
        {
            BaseTypeDeclarationSyntax
                or BaseNamespaceDeclarationSyntax
                or DelegateDeclarationSyntax
                or EnumMemberDeclarationSyntax
                or BaseMethodDeclarationSyntax
                or LocalFunctionStatementSyntax
                or PropertyDeclarationSyntax
                or IndexerDeclarationSyntax
                or EventDeclarationSyntax => true,
            VariableDeclaratorSyntax => IsFieldVariable(node),
            _ => false
        };
    }

    private static bool IsFieldVariable(SyntaxNode node)
    {
        return node.Parent?.Parent is BaseFieldDeclarationSyntax;
    }
}

internal sealed record OutlineResolutionResult(OutlineResolution? Resolution, OutlineResolutionError? Error)
{
    public static OutlineResolutionResult Succeeded(OutlineResolution resolution)
    {
        return new OutlineResolutionResult(resolution, Error: null);
    }

    public static OutlineResolutionResult Failed(int diagnosticId, string message, int exitCode)
    {
        return new OutlineResolutionResult(
            Resolution: null,
            Error: new OutlineResolutionError(diagnosticId, message, exitCode));
    }

    public static OutlineResolutionResult Failed(SymbolNavigationError error)
    {
        return Failed(error.DiagnosticId, error.Message, error.ExitCode);
    }
}

internal sealed record OutlineResolution(string File, IReadOnlyList<OutlineEntry> Entries);

internal sealed record OutlineEntry(
    string Name,
    string Kind,
    string? Container,
    SymbolFacts Facts,
    string Path,
    int Line,
    int Column,
    int EndLine,
    int EndColumn);

internal sealed record OutlineResolutionError(int DiagnosticId, string Message, int ExitCode);

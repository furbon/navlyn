using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Navlyn.Diagnostics;
using Navlyn.Languages;

namespace Navlyn.Symbols;

internal sealed class SymbolsInResolver
{
    public async Task<SymbolsInResolutionResult> ResolveAsync(
        Solution solution,
        FileInfo file,
        int line,
        int? startColumn,
        int? endColumn,
        Project? project,
        bool excludeGenerated,
        CancellationToken cancellationToken)
    {
        SourceDocumentResolutionResult documentResult =
            await new SourceDocumentResolver().ResolveAsync(solution, file, project, excludeGenerated, cancellationToken);

        if (documentResult.Error is not null)
        {
            return SymbolsInResolutionResult.Failed(documentResult.Error);
        }

        SourceDocumentResolution sourceDocument = documentResult.Resolution!;
        if (!TryGetSourceSpan(
            sourceDocument.Text,
            line,
            startColumn,
            endColumn,
            out TextSpan sourceSpan,
            out int effectiveStartColumn,
            out int effectiveEndColumn,
            out string? spanError))
        {
            return SymbolsInResolutionResult.Failed(
                DiagnosticIds.InvalidSourcePosition,
                spanError!,
                ExitCodes.UsageError);
        }

        SyntaxNode? root = await sourceDocument.Document.GetSyntaxRootAsync(cancellationToken);
        SemanticModel? semanticModel = await sourceDocument.Document.GetSemanticModelAsync(cancellationToken);
        if (root is null || semanticModel is null)
        {
            return SymbolsInResolutionResult.Succeeded(new SymbolsInResolution(
                File: sourceDocument.DisplayPath,
                Line: line,
                StartColumn: effectiveStartColumn,
                EndColumn: effectiveEndColumn,
                Symbols: []));
        }

        IReadOnlyList<SymbolsInSymbol> symbols = FindSymbols(
            root,
            semanticModel,
            sourceDocument.Text,
            sourceSpan,
            sourceDocument.Document.Project.Name,
            cancellationToken);

        return SymbolsInResolutionResult.Succeeded(new SymbolsInResolution(
            File: sourceDocument.DisplayPath,
            Line: line,
            StartColumn: effectiveStartColumn,
            EndColumn: effectiveEndColumn,
            Symbols: symbols));
    }

    private static IReadOnlyList<SymbolsInSymbol> FindSymbols(
        SyntaxNode root,
        SemanticModel semanticModel,
        SourceText text,
        TextSpan sourceSpan,
        string projectName,
        CancellationToken cancellationToken)
    {
        List<SymbolsInSymbol> symbols = [];

        foreach (SyntaxToken token in root.DescendantTokens(sourceSpan))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!SourceLanguageFacts.IsIdentifierToken(token) || !Overlaps(token.Span, sourceSpan))
            {
                continue;
            }

            int position = token.SpanStart;
            ISymbol? symbol = SymbolNavigationFacts.ResolveSymbol(
                semanticModel,
                token,
                position,
                cancellationToken);

            if (symbol is null)
            {
                continue;
            }

            LinePosition linePosition = text.Lines.GetLinePosition(position);
            LinePosition endLinePosition = text.Lines.GetLinePosition(token.Span.End);
            symbols.Add(new SymbolsInSymbol(
                Name: symbol.Name,
                Kind: symbol.Kind.ToString(),
                Container: SymbolNavigationFacts.GetContainer(symbol),
                Facts: SymbolFactsBuilder.Create(symbol, projectName),
                Line: linePosition.Line + 1,
                Column: linePosition.Character + 1,
                EndLine: endLinePosition.Line + 1,
                EndColumn: endLinePosition.Character + 1));
        }

        return [.. symbols
            .Distinct()
            .OrderBy(symbol => symbol.Line)
            .ThenBy(symbol => symbol.Column)
            .ThenBy(symbol => symbol.Name, StringComparer.Ordinal)
            .ThenBy(symbol => symbol.Kind, StringComparer.Ordinal)
            .ThenBy(symbol => symbol.Container, StringComparer.Ordinal)];
    }

    private static bool Overlaps(TextSpan left, TextSpan right)
    {
        return left.Start < right.End && right.Start < left.End;
    }

    private static bool TryGetSourceSpan(
        SourceText text,
        int line,
        int? startColumn,
        int? endColumn,
        out TextSpan sourceSpan,
        out int effectiveStartColumn,
        out int effectiveEndColumn,
        out string? error)
    {
        sourceSpan = default;
        effectiveStartColumn = startColumn ?? 1;
        effectiveEndColumn = endColumn ?? 1;
        error = null;

        if (line < 1)
        {
            error = $"Line must be 1 or greater. Actual value: {line}.";
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
        effectiveStartColumn = startColumn ?? 1;
        effectiveEndColumn = endColumn ?? maxColumn;

        if (effectiveStartColumn < 1)
        {
            error = $"Start column must be 1 or greater. Actual value: {effectiveStartColumn}.";
            return false;
        }

        if (effectiveEndColumn < 1)
        {
            error = $"End column must be 1 or greater. Actual value: {effectiveEndColumn}.";
            return false;
        }

        if (effectiveStartColumn > maxColumn)
        {
            error = $"Start column {effectiveStartColumn} is outside line {line}. The maximum column is {maxColumn}.";
            return false;
        }

        if (effectiveEndColumn > maxColumn)
        {
            error = $"End column {effectiveEndColumn} is outside line {line}. The maximum column is {maxColumn}.";
            return false;
        }

        if (effectiveStartColumn >= effectiveEndColumn)
        {
            if (startColumn is null && endColumn is null && lineLength == 0)
            {
                sourceSpan = TextSpan.FromBounds(textLine.Start, textLine.Start);
                return true;
            }

            error = $"Start column must be less than end column. Actual range: {effectiveStartColumn}..{effectiveEndColumn}.";
            return false;
        }

        int startPosition = textLine.Start + effectiveStartColumn - 1;
        int endPosition = textLine.Start + effectiveEndColumn - 1;
        sourceSpan = TextSpan.FromBounds(startPosition, endPosition);
        return true;
    }
}

internal sealed record SymbolsInResolutionResult(SymbolsInResolution? Resolution, SymbolsInResolutionError? Error)
{
    public static SymbolsInResolutionResult Succeeded(SymbolsInResolution resolution)
    {
        return new SymbolsInResolutionResult(resolution, Error: null);
    }

    public static SymbolsInResolutionResult Failed(int diagnosticId, string message, int exitCode)
    {
        return new SymbolsInResolutionResult(
            Resolution: null,
            Error: new SymbolsInResolutionError(diagnosticId, message, exitCode));
    }

    public static SymbolsInResolutionResult Failed(SymbolNavigationError error)
    {
        return Failed(error.DiagnosticId, error.Message, error.ExitCode);
    }
}

internal sealed record SymbolsInResolution(
    string File,
    int Line,
    int StartColumn,
    int EndColumn,
    IReadOnlyList<SymbolsInSymbol> Symbols);

internal sealed record SymbolsInSymbol(
    string Name,
    string Kind,
    string? Container,
    SymbolFacts Facts,
    int Line,
    int Column,
    int EndLine,
    int EndColumn);

internal sealed record SymbolsInResolutionError(int DiagnosticId, string Message, int ExitCode);

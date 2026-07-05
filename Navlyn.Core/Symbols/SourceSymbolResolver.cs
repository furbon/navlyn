using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Navlyn.Diagnostics;

namespace Navlyn.Symbols;

internal sealed class SourceSymbolResolver
{
    public async Task<SourceSymbolResolutionResult> ResolveAsync(
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
            return SourceSymbolResolutionResult.Failed(
                documentResult.Error.DiagnosticId,
                documentResult.Error.Message,
                documentResult.Error.ExitCode);
        }

        SourceDocumentResolution sourceDocument = documentResult.Resolution!;
        SourceText text = sourceDocument.Text;
        if (!TryGetPosition(text, line, column, out int position, out string? positionError))
        {
            return SourceSymbolResolutionResult.Failed(
                DiagnosticIds.InvalidSourcePosition,
                positionError!,
                ExitCodes.UsageError);
        }

        SyntaxNode? root = await sourceDocument.Document.GetSyntaxRootAsync(cancellationToken);
        SemanticModel? semanticModel = await sourceDocument.Document.GetSemanticModelAsync(cancellationToken);
        if (root is null || semanticModel is null)
        {
            return SymbolNotFound(sourceDocument.DisplayPath, line, column);
        }

        SyntaxToken token = root.FindToken(position);
        if (!token.Span.Contains(position))
        {
            return SymbolNotFound(sourceDocument.DisplayPath, line, column);
        }

        ISymbol? symbol = SymbolNavigationFacts.ResolveSymbol(semanticModel, token, position, cancellationToken);
        if (symbol is null)
        {
            return SymbolNotFound(sourceDocument.DisplayPath, line, column);
        }

        return SourceSymbolResolutionResult.Succeeded(new SourceSymbolResolution(
            File: sourceDocument.DisplayPath,
            Line: line,
            Column: column,
            Position: position,
            DocumentId: sourceDocument.Document.Id,
            ProjectId: sourceDocument.Document.Project.Id,
            SyntaxTree: root.SyntaxTree,
            ProjectName: sourceDocument.Document.Project.Name,
            Symbol: symbol));
    }

    private static SourceSymbolResolutionResult SymbolNotFound(string displayPath, int line, int column)
    {
        return SourceSymbolResolutionResult.Failed(
            DiagnosticIds.SymbolNotFoundAtPosition,
            $"No supported source symbol found at {displayPath}:{line}:{column}.",
            ExitCodes.UsageError);
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

internal sealed record SourceSymbolResolutionResult(SourceSymbolResolution? Resolution, SymbolNavigationError? Error)
{
    public static SourceSymbolResolutionResult Succeeded(SourceSymbolResolution resolution)
    {
        return new SourceSymbolResolutionResult(resolution, Error: null);
    }

    public static SourceSymbolResolutionResult Failed(int diagnosticId, string message, int exitCode)
    {
        return new SourceSymbolResolutionResult(
            Resolution: null,
            Error: new SymbolNavigationError(diagnosticId, message, exitCode));
    }
}

internal sealed record SourceSymbolResolution(
    string File,
    int Line,
    int Column,
    int Position,
    DocumentId DocumentId,
    ProjectId ProjectId,
    SyntaxTree SyntaxTree,
    string ProjectName,
    ISymbol Symbol);

internal sealed record SymbolNavigationError(int DiagnosticId, string Message, int ExitCode);

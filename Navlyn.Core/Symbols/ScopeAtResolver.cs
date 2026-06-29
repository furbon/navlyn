using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Navlyn.Diagnostics;
using Navlyn.Paths;
using Navlyn.Workspaces;

namespace Navlyn.Symbols;

internal sealed class ScopeAtResolver
{
    public async Task<ScopeAtResolutionResult> ResolveAsync(
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
            return ScopeAtResolutionResult.Failed(documentResult.Error);
        }

        SourceDocumentResolution sourceDocument = documentResult.Resolution!;
        if (!TryGetPosition(sourceDocument.Text, line, column, out int position, out string? positionError))
        {
            return ScopeAtResolutionResult.Failed(
                DiagnosticIds.InvalidSourcePosition,
                positionError!,
                ExitCodes.UsageError);
        }

        SyntaxNode? root = await sourceDocument.Document.GetSyntaxRootAsync(cancellationToken);
        SemanticModel? semanticModel = await sourceDocument.Document.GetSemanticModelAsync(cancellationToken);
        if (root is null)
        {
            return ScopeAtResolutionResult.Failed(
                DiagnosticIds.SymbolNotFoundAtPosition,
                $"No C# syntax found at {sourceDocument.DisplayPath}:{line}:{column}.",
                ExitCodes.UsageError);
        }

        SyntaxToken token = root.FindToken(position, findInsideTrivia: true);
        SyntaxNode? anchor = token.Parent;
        if (anchor is null)
        {
            return ScopeAtResolutionResult.Failed(
                DiagnosticIds.SymbolNotFoundAtPosition,
                $"No C# syntax found at {sourceDocument.DisplayPath}:{line}:{column}.",
                ExitCodes.UsageError);
        }

        IReadOnlyList<ScopeFrame> scopes = [.. anchor
            .AncestorsAndSelf()
            .Where(node => node.FullSpan.Contains(position))
            .Select(node => CreateFrame(node, semanticModel, sourceDocument.Document.Project.Name, cancellationToken))
            .OfType<ScopeFrame>()
            .Reverse()
            .DistinctBy(frame => $"{frame.Kind}:{frame.Path}:{frame.Line}:{frame.Column}:{frame.EndLine}:{frame.EndColumn}")];

        ISymbol? containingSymbol = semanticModel?.GetEnclosingSymbol(position, cancellationToken);
        if (containingSymbol is not null)
        {
            containingSymbol = SymbolNavigationFacts.NormalizeSourceNavigationSymbol(containingSymbol);
        }

        Project selectedProject = sourceDocument.Document.Project;
        ScopeProjectContext context = new(
            Name: selectedProject.Name,
            Path: selectedProject.FilePath is null ? null : PathDisplay.FromCurrentDirectory(selectedProject.FilePath),
            TargetFramework: ProjectContextFacts.GetTargetFramework(selectedProject),
            LanguageVersion: ProjectContextFacts.GetLanguageVersion(selectedProject),
            PreprocessorSymbols: ProjectContextFacts.GetPreprocessorSymbols(selectedProject));

        return ScopeAtResolutionResult.Succeeded(new ScopeAtResolution(
            File: sourceDocument.DisplayPath,
            Line: line,
            Column: column,
            ProjectContext: context,
            Scopes: scopes,
            ContainingSymbol: containingSymbol is null
                ? null
                : CreateSymbol(containingSymbol, selectedProject.Name)));
    }

    private static ScopeFrame? CreateFrame(
        SyntaxNode node,
        SemanticModel? semanticModel,
        string projectName,
        CancellationToken cancellationToken)
    {
        string kind = node switch
        {
            FileScopedNamespaceDeclarationSyntax => "Namespace",
            NamespaceDeclarationSyntax => "Namespace",
            TypeDeclarationSyntax => "Type",
            DelegateDeclarationSyntax => "Delegate",
            EnumDeclarationSyntax => "Enum",
            BaseMethodDeclarationSyntax => "Member",
            PropertyDeclarationSyntax => "Member",
            IndexerDeclarationSyntax => "Member",
            EventDeclarationSyntax => "Member",
            EventFieldDeclarationSyntax => "Member",
            FieldDeclarationSyntax => "Field",
            LocalFunctionStatementSyntax => "LocalFunction",
            AnonymousFunctionExpressionSyntax => "Lambda",
            GlobalStatementSyntax => "TopLevelStatement",
            _ => string.Empty
        };

        if (kind.Length == 0)
        {
            return null;
        }

        Location location = node.GetLocation();
        FileLinePositionSpan lineSpan = location.GetLineSpan();
        if (!lineSpan.IsValid)
        {
            return null;
        }

        ISymbol? symbol = semanticModel?.GetDeclaredSymbol(node, cancellationToken);
        if (symbol is not null)
        {
            symbol = SymbolNavigationFacts.NormalizeSourceNavigationSymbol(symbol);
        }

        return new ScopeFrame(
            Kind: kind,
            SyntaxKind: node.Kind().ToString(),
            Path: PathDisplay.FromCurrentDirectory(lineSpan.Path),
            Line: lineSpan.StartLinePosition.Line + 1,
            Column: lineSpan.StartLinePosition.Character + 1,
            EndLine: lineSpan.EndLinePosition.Line + 1,
            EndColumn: lineSpan.EndLinePosition.Character + 1,
            Symbol: symbol is null ? null : CreateSymbol(symbol, projectName));
    }

    private static ScopeSymbol CreateSymbol(ISymbol symbol, string projectName)
    {
        SymbolSourceLocation? location = SymbolNavigationFacts.GetSourceLocations(symbol).FirstOrDefault();
        return new ScopeSymbol(
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

internal sealed record ScopeAtResolutionResult(ScopeAtResolution? Resolution, SymbolNavigationError? Error)
{
    public static ScopeAtResolutionResult Succeeded(ScopeAtResolution resolution)
    {
        return new ScopeAtResolutionResult(resolution, Error: null);
    }

    public static ScopeAtResolutionResult Failed(int diagnosticId, string message, int exitCode)
    {
        return new ScopeAtResolutionResult(
            Resolution: null,
            Error: new SymbolNavigationError(diagnosticId, message, exitCode));
    }

    public static ScopeAtResolutionResult Failed(SymbolNavigationError error)
    {
        return Failed(error.DiagnosticId, error.Message, error.ExitCode);
    }
}

internal sealed record ScopeAtResolution(
    string File,
    int Line,
    int Column,
    ScopeProjectContext ProjectContext,
    IReadOnlyList<ScopeFrame> Scopes,
    ScopeSymbol? ContainingSymbol);

internal sealed record ScopeProjectContext(
    string Name,
    string? Path,
    string? TargetFramework,
    string? LanguageVersion,
    IReadOnlyList<string> PreprocessorSymbols);

internal sealed record ScopeFrame(
    string Kind,
    string SyntaxKind,
    string Path,
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    ScopeSymbol? Symbol);

internal sealed record ScopeSymbol(
    string Name,
    string Kind,
    string? Container,
    SymbolFacts Facts,
    string? Path,
    int? Line,
    int? Column,
    int? EndLine,
    int? EndColumn);

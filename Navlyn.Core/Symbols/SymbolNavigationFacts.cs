using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Navlyn.GeneratedCode;
using Navlyn.Paths;

namespace Navlyn.Symbols;

internal static class SymbolNavigationFacts
{
    public static string? GetContainer(ISymbol symbol)
    {
        return symbol.ContainingSymbol is null or INamespaceSymbol { IsGlobalNamespace: true }
            ? null
            : symbol.ContainingSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
    }

    public static Location? GetBestSourceLocation(ISymbol symbol, SyntaxTree syntaxTree, int position)
    {
        return GetBestSourceLocation(symbol, syntaxTree, position, excludeGenerated: false);
    }

    public static Location? GetBestSourceLocation(
        ISymbol symbol,
        SyntaxTree syntaxTree,
        int position,
        bool excludeGenerated)
    {
        IReadOnlyList<Location> sourceLocations = GetValidSourceLocations(symbol);
        if (excludeGenerated)
        {
            sourceLocations = [.. sourceLocations.Where(location =>
                !GeneratedCodeFacts.IsGeneratedPath(location.GetLineSpan().Path))];
        }

        return sourceLocations.FirstOrDefault(location =>
                location.SourceTree == syntaxTree && location.SourceSpan.Contains(position))
            ?? sourceLocations.FirstOrDefault();
    }

    public static IReadOnlyList<SymbolSourceLocation> GetSourceLocations(ISymbol symbol)
    {
        return GetSourceLocations(symbol, excludeGenerated: false);
    }

    public static IReadOnlyList<SymbolSourceLocation> GetSourceLocations(ISymbol symbol, bool excludeGenerated)
    {
        return [.. GetValidSourceLocations(symbol)
            .Select(location => CreateSourceLocation(location, excludeGenerated))
            .OfType<SymbolSourceLocation>()
            .Distinct()
            .OrderBy(location => location.Path, StringComparer.Ordinal)
            .ThenBy(location => location.Line)
            .ThenBy(location => location.Column)
            .ThenBy(location => location.EndLine)
            .ThenBy(location => location.EndColumn)];
    }

    public static SymbolSourceLocation? CreateSourceLocation(Location location, bool excludeGenerated = false)
    {
        if (!location.IsInSource)
        {
            return null;
        }

        FileLinePositionSpan lineSpan = location.GetLineSpan();
        if (!lineSpan.IsValid)
        {
            return null;
        }

        if (excludeGenerated && GeneratedCodeFacts.IsGeneratedPath(lineSpan.Path))
        {
            return null;
        }

        return new SymbolSourceLocation(
            Path: PathDisplay.FromCurrentDirectory(lineSpan.Path),
            Line: lineSpan.StartLinePosition.Line + 1,
            Column: lineSpan.StartLinePosition.Character + 1,
            EndLine: lineSpan.EndLinePosition.Line + 1,
            EndColumn: lineSpan.EndLinePosition.Character + 1);
    }

    public static ISymbol? ResolveSymbol(
        SemanticModel semanticModel,
        SyntaxToken token,
        int position,
        CancellationToken cancellationToken)
    {
        foreach (SyntaxNode node in token.Parent?.AncestorsAndSelf() ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!node.Span.Contains(position))
            {
                continue;
            }

            ISymbol? declaredSymbol = semanticModel.GetDeclaredSymbol(node, cancellationToken);
            if (declaredSymbol is not null)
            {
                return NormalizeSourceNavigationSymbol(declaredSymbol);
            }

            if (node is NameSyntax nameSyntax)
            {
                IAliasSymbol? aliasSymbol = semanticModel.GetAliasInfo(nameSyntax, cancellationToken);
                if (aliasSymbol is not null)
                {
                    return aliasSymbol;
                }
            }

            SymbolInfo symbolInfo = semanticModel.GetSymbolInfo(node, cancellationToken);
            if (symbolInfo.Symbol is not null)
            {
                return NormalizeSourceNavigationSymbol(symbolInfo.Symbol);
            }

            ISymbol? candidateSymbol = symbolInfo.CandidateSymbols
                .OrderBy(symbol => symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), StringComparer.Ordinal)
                .FirstOrDefault();
            if (candidateSymbol is not null)
            {
                return NormalizeSourceNavigationSymbol(candidateSymbol);
            }
        }

        return null;
    }

    public static ISymbol NormalizeSourceNavigationSymbol(ISymbol symbol)
    {
        return symbol is IMethodSymbol
        {
            MethodKind:
                MethodKind.PropertyGet or
                MethodKind.PropertySet or
                MethodKind.EventAdd or
                MethodKind.EventRemove,
            AssociatedSymbol: not null
        } method
            ? method.AssociatedSymbol
            : symbol;
    }

    private static IReadOnlyList<Location> GetValidSourceLocations(ISymbol symbol)
    {
        return [.. symbol.Locations
            .Where(location => location.IsInSource && location.GetLineSpan().IsValid)
            .OrderBy(location => PathDisplay.FromCurrentDirectory(location.GetLineSpan().Path), StringComparer.Ordinal)
            .ThenBy(location => location.GetLineSpan().StartLinePosition.Line)
            .ThenBy(location => location.GetLineSpan().StartLinePosition.Character)];
    }
}

internal sealed record SymbolSourceLocation(string Path, int Line, int Column, int EndLine, int EndColumn);

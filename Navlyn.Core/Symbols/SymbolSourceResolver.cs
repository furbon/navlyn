using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Navlyn.Diagnostics;
using Navlyn.GeneratedCode;
using Navlyn.Languages;
using Navlyn.Paths;

namespace Navlyn.Symbols;

internal sealed class SymbolSourceResolver
{
    public async Task<SymbolSourceResolutionResult> ResolveAsync(
        Solution solution,
        FileInfo file,
        int line,
        int column,
        Project? project,
        bool excludeGenerated,
        SymbolSourceOptions options,
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
            return SymbolSourceResolutionResult.Failed(result.Error);
        }

        SourceSymbolResolution source = result.Resolution!;
        SymbolSourceSymbol symbol = CreateSymbol(source.Symbol, source.ProjectName);
        IReadOnlyList<Location> locations = [.. source.Symbol.Locations
            .Where(location => location.IsInSource && location.GetLineSpan().IsValid)
            .Where(location => !excludeGenerated || !GeneratedCodeFacts.IsGeneratedPath(location.GetLineSpan().Path))
            .OrderBy(location => PathDisplay.FromCurrentDirectory(location.GetLineSpan().Path), StringComparer.Ordinal)
            .ThenBy(location => location.GetLineSpan().StartLinePosition.Line)
            .ThenBy(location => location.GetLineSpan().StartLinePosition.Character)];

        if (locations.Count == 0)
        {
            IReadOnlyList<string> warnings = source.Symbol.Locations.Any(location => location.IsInMetadata)
                ? ["metadata-only-symbol"]
                : ["source-location-not-found"];
            return SymbolSourceResolutionResult.Succeeded(new SymbolSourceResolution(
                source.File,
                source.Line,
                source.Column,
                symbol,
                options.View,
                new SymbolSourceLimits(options.MaxLines, options.BudgetTokens),
                Slices: [],
                Truncated: false,
                Warnings: warnings));
        }

        IReadOnlyList<SymbolSourceSlice> slices = [.. locations
            .SelectMany(location => CreateSlices(source.Symbol, location, options, cancellationToken))
            .OrderBy(slice => slice.Path, StringComparer.Ordinal)
            .ThenBy(slice => slice.StartLine)
            .ThenBy(slice => slice.StartColumn)
            .ThenBy(slice => slice.TextKind, StringComparer.Ordinal)];

        return SymbolSourceResolutionResult.Succeeded(new SymbolSourceResolution(
            source.File,
            source.Line,
            source.Column,
            symbol,
            options.View,
            new SymbolSourceLimits(options.MaxLines, options.BudgetTokens),
            slices,
            slices.Any(slice => slice.Truncated),
            Warnings: []));
    }

    private static IEnumerable<SymbolSourceSlice> CreateSlices(
        ISymbol symbol,
        Location location,
        SymbolSourceOptions options,
        CancellationToken cancellationToken)
    {
        SyntaxTree? syntaxTree = location.SourceTree;
        if (syntaxTree is null)
        {
            yield break;
        }

        SyntaxNode root = syntaxTree.GetRoot(cancellationToken);
        SourceText text = syntaxTree.GetText(cancellationToken);
        SyntaxNode? declaration = SourceLanguageFacts.FindDeclarationNode(root, symbol, location);
        if (declaration is null)
        {
            yield break;
        }

        foreach ((string textKind, TextSpan span) in GetViewSpans(declaration, options.View, symbol))
        {
            SymbolSourceSlice? slice = CreateSlice(text, syntaxTree, span, textKind, options);
            if (slice is not null)
            {
                yield return slice;
            }
        }

        if (options.View == "signature")
        {
            string? signature = SymbolFactsBuilder.Create(symbol).Signature;
            if (!string.IsNullOrWhiteSpace(signature))
            {
                FileLinePositionSpan lineSpan = location.GetLineSpan();
                yield return new SymbolSourceSlice(
                    TextKind: "signature",
                    Path: PathDisplay.FromCurrentDirectory(lineSpan.Path),
                    StartLine: lineSpan.StartLinePosition.Line + 1,
                    StartColumn: lineSpan.StartLinePosition.Character + 1,
                    EndLine: lineSpan.EndLinePosition.Line + 1,
                    EndColumn: lineSpan.EndLinePosition.Character + 1,
                    Lines: [signature],
                    Truncated: false);
            }
        }
    }

    private static IEnumerable<(string TextKind, TextSpan Span)> GetViewSpans(
        SyntaxNode declaration,
        string view,
        ISymbol symbol)
    {
        return view switch
        {
            "signature" => [],
            "body" => SourceLanguageFacts.GetBodySpans(declaration),
            "members" => SourceLanguageFacts.GetMemberSpans(declaration),
            "xml-doc" => SourceLanguageFacts.GetXmlDocSpans(declaration),
            "attributes" => SourceLanguageFacts.GetAttributeSpans(declaration),
            _ => [("declaration", declaration.Span)]
        };
    }

    private static SymbolSourceSlice? CreateSlice(
        SourceText text,
        SyntaxTree syntaxTree,
        TextSpan span,
        string textKind,
        SymbolSourceOptions options)
    {
        FileLinePositionSpan lineSpan = syntaxTree.GetLineSpan(span);
        if (!lineSpan.IsValid)
        {
            return null;
        }

        int startLine = lineSpan.StartLinePosition.Line + 1;
        int endLine = Math.Max(startLine, lineSpan.EndLinePosition.Line + 1);
        int lineCount = endLine - startLine + 1;
        int effectiveLineCount = Math.Min(lineCount, options.MaxLines);
        string[] lines = [.. text.Lines
            .Skip(startLine - 1)
            .Take(effectiveLineCount)
            .Select(line => line.ToString())];

        int charLimit = options.BudgetTokens * 4;
        bool budgetTruncated = false;
        int used = 0;
        List<string> boundedLines = [];
        foreach (string line in lines)
        {
            if (used + line.Length > charLimit)
            {
                int remaining = Math.Max(0, charLimit - used);
                if (remaining > 0)
                {
                    boundedLines.Add(line[..Math.Min(line.Length, remaining)]);
                }

                budgetTruncated = true;
                break;
            }

            boundedLines.Add(line);
            used += line.Length;
        }

        return new SymbolSourceSlice(
            TextKind: textKind,
            Path: PathDisplay.FromCurrentDirectory(lineSpan.Path),
            StartLine: startLine,
            StartColumn: lineSpan.StartLinePosition.Character + 1,
            EndLine: lineSpan.EndLinePosition.Line + 1,
            EndColumn: lineSpan.EndLinePosition.Character + 1,
            Lines: boundedLines,
            Truncated: lineCount > options.MaxLines || budgetTruncated);
    }

    private static SymbolSourceSymbol CreateSymbol(ISymbol symbol, string projectName)
    {
        SymbolSourceLocation? location = SymbolNavigationFacts.GetSourceLocations(symbol).FirstOrDefault();
        return new SymbolSourceSymbol(
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
}

internal sealed record SymbolSourceOptions(string View, int MaxLines, int BudgetTokens);

internal sealed record SymbolSourceResolutionResult(SymbolSourceResolution? Resolution, SymbolNavigationError? Error)
{
    public static SymbolSourceResolutionResult Succeeded(SymbolSourceResolution resolution)
    {
        return new SymbolSourceResolutionResult(resolution, Error: null);
    }

    public static SymbolSourceResolutionResult Failed(SymbolNavigationError error)
    {
        return new SymbolSourceResolutionResult(Resolution: null, error);
    }
}

internal sealed record SymbolSourceResolution(
    string File,
    int Line,
    int Column,
    SymbolSourceSymbol Symbol,
    string View,
    SymbolSourceLimits Limits,
    IReadOnlyList<SymbolSourceSlice> Slices,
    bool Truncated,
    IReadOnlyList<string> Warnings);

internal sealed record SymbolSourceLimits(int MaxLines, int BudgetTokens);

internal sealed record SymbolSourceSymbol(
    string Name,
    string Kind,
    string? Container,
    SymbolFacts Facts,
    string? Path,
    int? Line,
    int? Column,
    int? EndLine,
    int? EndColumn);

internal sealed record SymbolSourceSlice(
    string TextKind,
    string Path,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn,
    IReadOnlyList<string> Lines,
    bool Truncated);

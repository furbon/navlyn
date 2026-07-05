using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Navlyn.GeneratedCode;
using Navlyn.Languages;
using Navlyn.Paths;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.Diffs;

internal sealed class ChangedSymbolResolver
{
    private const int ChangedLineDisplayLimit = 25;

    public async Task<ChangedSymbolsResolution> ResolveAsync(
        LoadedWorkspace workspace,
        DiffSet diff,
        IReadOnlyList<Project> projects,
        bool excludeGenerated,
        int limit,
        CancellationToken cancellationToken)
    {
        List<ChangedSymbolBuilder> symbolBuilders = [];
        List<DiffUnresolvedChange> unresolvedChanges = [];

        foreach (DiffFile file in diff.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!SourceLanguageFacts.IsSupportedSourceFile(file.Path))
            {
                continue;
            }

            if (excludeGenerated && GeneratedCodeFacts.IsGeneratedPath(file.Path))
            {
                continue;
            }

            if (file.Status == "deleted" || file.Hunks.All(hunk => hunk.NewLineCount == 0))
            {
                unresolvedChanges.Add(new DiffUnresolvedChange(
                    file.Path,
                    file.Status,
                    file.Hunks,
                    ["deleted-or-not-in-current-workspace"]));
                continue;
            }

            IReadOnlyList<Document> documents = FindDocuments(projects, file.Path);
            if (documents.Count == 0)
            {
                unresolvedChanges.Add(new DiffUnresolvedChange(
                    file.Path,
                    file.Status,
                    file.Hunks,
                    ["source-file-not-in-current-workspace"]));
                continue;
            }

            bool resolvedAny = false;
            foreach (Document document in documents)
            {
                SourceText text = await document.GetTextAsync(cancellationToken);
                SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken);
                SemanticModel? semanticModel = await document.GetSemanticModelAsync(cancellationToken);
                if (root is null || semanticModel is null)
                {
                    continue;
                }

                foreach (DiffHunk hunk in file.Hunks)
                {
                    foreach (int line in hunk.NewLines())
                    {
                        if (line < 1 || line > text.Lines.Count)
                        {
                            continue;
                        }

                        ISymbol? symbol = ResolveChangedSymbol(root, semanticModel, text.Lines[line - 1], cancellationToken);
                        if (symbol is null)
                        {
                            continue;
                        }

                        SymbolSourceLocation? location = SymbolNavigationFacts
                            .GetSourceLocations(symbol, excludeGenerated)
                            .FirstOrDefault(sourceLocation => PathsEqual(sourceLocation.Path, file.Path));
                        if (location is null)
                        {
                            continue;
                        }

                        resolvedAny = true;
                        DiffChangedLine changedLine = new(file.Path, line);
                        string changeKind = GetChangeKind(symbol);
                        ChangedSymbolBuilder builder = GetOrAddBuilder(
                            symbolBuilders,
                            symbol,
                            document.Project.Name,
                            location);
                        builder.ChangeKinds.Add(changeKind);
                        builder.ChangedLines.Add(changedLine);
                    }
                }
            }

            if (!resolvedAny)
            {
                unresolvedChanges.Add(new DiffUnresolvedChange(
                    file.Path,
                    file.Status,
                    file.Hunks,
                    ["no-source-symbol-overlaps-diff-hunk"]));
            }
        }

        IReadOnlyList<DiffChangedSymbol> allSymbols = [.. symbolBuilders
            .Select(builder => builder.Build())
            .OrderBy(symbol => symbol.Path, StringComparer.Ordinal)
            .ThenBy(symbol => symbol.Line)
            .ThenBy(symbol => symbol.Column)
            .ThenBy(symbol => symbol.Kind, StringComparer.Ordinal)
            .ThenBy(symbol => symbol.Name, StringComparer.Ordinal)
            .ThenBy(symbol => symbol.Container, StringComparer.Ordinal)
            .ThenBy(symbol => symbol.Facts.Project, StringComparer.Ordinal)];

        return new ChangedSymbolsResolution(
            new ChangedSymbolsSection(
                TotalSymbols: allSymbols.Count,
                Limit: limit,
                Truncated: allSymbols.Count > limit,
                Symbols: [.. allSymbols.Take(limit)]),
            [.. unresolvedChanges
                .OrderBy(change => change.Path, StringComparer.Ordinal)
                .ThenBy(change => change.Status, StringComparer.Ordinal)]);
    }

    private static IReadOnlyList<Document> FindDocuments(IReadOnlyList<Project> projects, string path)
    {
        return [.. projects
            .OrderBy(project => project.FilePath, StringComparer.Ordinal)
            .ThenBy(project => project.Name, StringComparer.Ordinal)
            .SelectMany(project => project.Documents
                .Where(document => document.FilePath is not null)
                .OrderBy(document => document.FilePath, StringComparer.Ordinal)
                .ThenBy(document => document.Name, StringComparer.Ordinal))
            .Where(document => PathsEqual(PathDisplay.FromCurrentDirectory(document.FilePath!), path))];
    }

    private static ISymbol? ResolveChangedSymbol(
        SyntaxNode root,
        SemanticModel semanticModel,
        TextLine line,
        CancellationToken cancellationToken)
    {
        SyntaxNode? declaration = root
            .DescendantNodes()
            .Where(node => SourceLanguageFacts.IsDeclarationNode(node) && ContainsLine(node.FullSpan, line))
            .OrderBy(node => node.FullSpan.Length)
            .FirstOrDefault();

        return declaration is null
            ? null
            : GetDeclaredSymbol(semanticModel, declaration, cancellationToken);
    }

    private static bool ContainsLine(TextSpan span, TextLine line)
    {
        int start = line.Start;
        int end = Math.Max(line.End, line.Start);
        return span.Start <= start && span.End >= end;
    }

    private static ISymbol? GetDeclaredSymbol(
        SemanticModel semanticModel,
        SyntaxNode node,
        CancellationToken cancellationToken)
    {
        ISymbol? symbol = semanticModel.GetDeclaredSymbol(node, cancellationToken);

        return symbol is null ? null : SymbolNavigationFacts.NormalizeSourceNavigationSymbol(symbol);
    }

    private static ChangedSymbolBuilder GetOrAddBuilder(
        List<ChangedSymbolBuilder> builders,
        ISymbol symbol,
        string projectName,
        SymbolSourceLocation location)
    {
        ChangedSymbolBuilder? builder = builders.FirstOrDefault(existing =>
            existing.Path == location.Path &&
            existing.Line == location.Line &&
            existing.Column == location.Column &&
            existing.Kind == symbol.Kind.ToString() &&
            existing.Name == symbol.Name &&
            existing.ProjectName == projectName);
        if (builder is not null)
        {
            return builder;
        }

        builder = new ChangedSymbolBuilder(symbol, projectName, location);
        builders.Add(builder);
        return builder;
    }

    private static string GetChangeKind(ISymbol symbol)
    {
        return symbol is INamedTypeSymbol ? "type-body" : "body";
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            NormalizePath(left),
            NormalizePath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }

    private sealed class ChangedSymbolBuilder
    {
        private readonly ISymbol symbol;
        private readonly SymbolSourceLocation location;

        public ChangedSymbolBuilder(ISymbol symbol, string projectName, SymbolSourceLocation location)
        {
            this.symbol = symbol;
            ProjectName = projectName;
            this.location = location;
        }

        public string Name => symbol.Name;

        public string Kind => symbol.Kind.ToString();

        public string ProjectName { get; }

        public string Path => location.Path;

        public int Line => location.Line;

        public int Column => location.Column;

        public HashSet<string> ChangeKinds { get; } = new(StringComparer.Ordinal);

        public HashSet<DiffChangedLine> ChangedLines { get; } = [];

        public DiffChangedSymbol Build()
        {
            IReadOnlyList<DiffChangedLine> changedLines = [.. ChangedLines
                .OrderBy(line => line.Path, StringComparer.Ordinal)
                .ThenBy(line => line.Line)];

            return new DiffChangedSymbol(
                Name: symbol.Name,
                Kind: symbol.Kind.ToString(),
                Container: SymbolNavigationFacts.GetContainer(symbol),
                Facts: SymbolFactsBuilder.Create(symbol, ProjectName),
                Path: location.Path,
                Line: location.Line,
                Column: location.Column,
                EndLine: location.EndLine,
                EndColumn: location.EndColumn,
                ChangeKinds: [.. ChangeKinds.OrderBy(kind => kind, StringComparer.Ordinal)],
                TotalChangedLines: changedLines.Count,
                ChangedLineLimit: ChangedLineDisplayLimit,
                ChangedLinesTruncated: changedLines.Count > ChangedLineDisplayLimit,
                ChangedLines: [.. changedLines.Take(ChangedLineDisplayLimit)],
                ReasonCodes: ["diff-hunk-overlaps-symbol-span"]);
        }
    }
}

internal sealed record ChangedSymbolsResolution(
    ChangedSymbolsSection ChangedSymbols,
    IReadOnlyList<DiffUnresolvedChange> UnresolvedChanges);

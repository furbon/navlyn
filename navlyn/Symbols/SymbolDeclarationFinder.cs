using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Navlyn.GeneratedCode;
using Navlyn.Paths;

namespace Navlyn.Symbols;

internal sealed class SymbolDeclarationFinder
{
    public async Task<IReadOnlyList<SymbolDeclaration>> FindAsync(
        Solution solution,
        SymbolNameMatcher matcher,
        bool excludeGenerated,
        CancellationToken cancellationToken)
    {
        return await FindAsync(GetProjects(solution), matcher, excludeGenerated, cancellationToken);
    }

    public async Task<IReadOnlyList<SymbolDeclaration>> FindAsync(
        IReadOnlyList<Project> projects,
        SymbolNameMatcher matcher,
        bool excludeGenerated,
        CancellationToken cancellationToken)
    {
        return await FindAsync(projects, matcher.IsMatch, excludeGenerated, cancellationToken);
    }

    public async Task<IReadOnlyList<SymbolDeclaration>> FindAllAsync(
        IReadOnlyList<Project> projects,
        bool excludeGenerated,
        CancellationToken cancellationToken)
    {
        return await FindAsync(projects, _ => true, excludeGenerated, cancellationToken);
    }

    private static async Task<IReadOnlyList<SymbolDeclaration>> FindAsync(
        IReadOnlyList<Project> projects,
        Func<string, bool> isNameMatch,
        bool excludeGenerated,
        CancellationToken cancellationToken)
    {
        List<SymbolDeclaration> declarations = [];

        foreach (Document document in GetDocuments(projects, excludeGenerated))
        {
            cancellationToken.ThrowIfCancellationRequested();
            SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken);
            SemanticModel? semanticModel = await document.GetSemanticModelAsync(cancellationToken);

            if (root is null || semanticModel is null)
            {
                continue;
            }

            foreach (SyntaxNode node in root.DescendantNodes().Where(IsDeclarationNode))
            {
                AddDeclaration(document.Project, semanticModel, node, isNameMatch, declarations, cancellationToken);
            }
        }

        return [.. declarations
            .OrderBy(declaration => declaration.Path, StringComparer.Ordinal)
            .ThenBy(declaration => declaration.Line)
            .ThenBy(declaration => declaration.Column)
            .ThenBy(declaration => declaration.Name, StringComparer.Ordinal)];
    }

    private static IReadOnlyList<Project> GetProjects(Solution solution)
    {
        return [.. solution.Projects
            .OrderBy(project => project.FilePath, StringComparer.Ordinal)
            .ThenBy(project => project.Name, StringComparer.Ordinal)];
    }

    private static IEnumerable<Document> GetDocuments(IReadOnlyList<Project> projects, bool excludeGenerated)
    {
        return projects
            .SelectMany(project => project.Documents
                .Where(document => document.SupportsSyntaxTree)
                .Where(document => !excludeGenerated || !GeneratedCodeFacts.IsGeneratedPath(document.FilePath))
                .OrderBy(document => document.FilePath, StringComparer.Ordinal)
                .ThenBy(document => document.Name, StringComparer.Ordinal));
    }

    private static void AddDeclaration(
        Project project,
        SemanticModel semanticModel,
        SyntaxNode node,
        Func<string, bool> isNameMatch,
        List<SymbolDeclaration> declarations,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ISymbol? symbol = semanticModel.GetDeclaredSymbol(node, cancellationToken);
        if (symbol is null || !isNameMatch(symbol.Name))
        {
            return;
        }

        Location? location = symbol.Locations.FirstOrDefault(location =>
            location.IsInSource &&
            location.SourceTree == node.SyntaxTree &&
            node.Span.Contains(location.SourceSpan.Start));

        location ??= symbol.Locations.FirstOrDefault(location => location.IsInSource);
        if (location is null)
        {
            return;
        }

        FileLinePositionSpan lineSpan = location.GetLineSpan();
        if (!lineSpan.IsValid)
        {
            return;
        }

        declarations.Add(new SymbolDeclaration(
            Name: symbol.Name,
            Kind: symbol.Kind.ToString(),
            Container: SymbolNavigationFacts.GetContainer(symbol),
            Facts: SymbolFactsBuilder.Create(symbol, project.Name),
            Path: PathDisplay.FromCurrentDirectory(lineSpan.Path),
            Line: lineSpan.StartLinePosition.Line + 1,
            Column: lineSpan.StartLinePosition.Character + 1,
            EndLine: lineSpan.EndLinePosition.Line + 1,
            EndColumn: lineSpan.EndLinePosition.Character + 1));
    }

    private static bool IsDeclarationNode(SyntaxNode node)
    {
        return node is BaseTypeDeclarationSyntax
            or BaseNamespaceDeclarationSyntax
            or DelegateDeclarationSyntax
            or EnumMemberDeclarationSyntax
            or BaseMethodDeclarationSyntax
            or LocalFunctionStatementSyntax
            or PropertyDeclarationSyntax
            or IndexerDeclarationSyntax
            or EventDeclarationSyntax
            or UsingDirectiveSyntax
            or VariableDeclaratorSyntax
            or ForEachStatementSyntax
            or ParameterSyntax
            or TypeParameterSyntax
            or SingleVariableDesignationSyntax;
    }
}

internal sealed record SymbolDeclaration(
    string Name,
    string Kind,
    string? Container,
    SymbolFacts Facts,
    string Path,
    int Line,
    int Column,
    int EndLine,
    int EndColumn);

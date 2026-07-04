using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Navlyn.GeneratedCode;
using Navlyn.Paths;
using Navlyn.Workspaces;

namespace Navlyn.Symbols;

internal sealed class DeclarationIndex
{
    private readonly Solution solution;
    private readonly IReadOnlyList<DeclarationIndexEntry> entries;
    private readonly Dictionary<DocumentId, IReadOnlyList<DeclarationIndexEntry>> entriesByDocumentId;
    private readonly Dictionary<string, CandidateRecord> candidateRecords = new(StringComparer.Ordinal);
    private readonly Dictionary<DeclarationKey, SymbolDeclaration> enrichedDeclarations = [];
    private readonly SemaphoreSlim enrichmentGate = new(1, 1);

    private DeclarationIndex(
        Solution solution,
        IReadOnlyList<DeclarationIndexEntry> entries,
        string solutionFingerprint)
    {
        this.solution = solution;
        this.entries = entries;
        entriesByDocumentId = entries
            .GroupBy(entry => entry.DocumentId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<DeclarationIndexEntry>)[.. group]);
        SolutionFingerprint = solutionFingerprint;
    }

    public string SolutionFingerprint { get; }

    public int EntryCount => entries.Count;

    public IReadOnlyList<DeclarationIndexEntry> Entries => entries;

    public int SemanticEnrichmentCount { get; private set; }

    public int CandidateRecordCount => candidateRecords.Count;

    public static async Task<DeclarationIndex> CreateAsync(Solution solution, CancellationToken cancellationToken)
    {
        List<DeclarationIndexEntry> entries = [];
        foreach (Project project in solution.Projects
            .OrderBy(project => project.FilePath, StringComparer.Ordinal)
            .ThenBy(project => project.Name, StringComparer.Ordinal))
        {
            foreach (Document document in project.Documents
                .Where(document => document.SupportsSyntaxTree)
                .Where(document => document.FilePath is not null)
                .OrderBy(document => document.FilePath, StringComparer.Ordinal)
                .ThenBy(document => document.Name, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken);
                if (root is null)
                {
                    continue;
                }

                string fullPath = Path.GetFullPath(document.FilePath!);
                string displayPath = PathDisplay.FromCurrentDirectory(fullPath);
                foreach (SyntaxNode node in root.DescendantNodes().Where(IsDeclarationNode))
                {
                    string? name = GetSyntaxName(node);
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    FileLinePositionSpan lineSpan = node.SyntaxTree.GetLineSpan(node.Span, cancellationToken);
                    if (!lineSpan.IsValid)
                    {
                        continue;
                    }

                    entries.Add(new DeclarationIndexEntry(
                        Key: new DeclarationKey(document.Id, node.Span),
                        DocumentId: document.Id,
                        ProjectId: project.Id,
                        ProjectName: project.Name,
                        Name: name,
                        SyntaxKind: node.GetType().Name,
                        FullPath: fullPath,
                        DisplayPath: displayPath,
                        IsGenerated: GeneratedCodeFacts.IsGeneratedPath(fullPath),
                        Line: lineSpan.StartLinePosition.Line + 1,
                        Column: lineSpan.StartLinePosition.Character + 1));
                }
            }
        }

        return new DeclarationIndex(
            solution,
            [.. entries
                .OrderBy(entry => entry.DisplayPath, StringComparer.Ordinal)
                .ThenBy(entry => entry.Line)
                .ThenBy(entry => entry.Column)
                .ThenBy(entry => entry.Name, StringComparer.Ordinal)],
            CreateSolutionFingerprint(solution));
    }

    public IReadOnlyList<DeclarationIndexEntry> SearchSyntax(
        IReadOnlyList<Project> projects,
        FuzzyQueryOptions options,
        Func<string, bool> isNameMatch)
    {
        HashSet<ProjectId> projectIds = [.. projects.Select(project => project.Id)];
        return [.. entries
            .Where(entry => projectIds.Contains(entry.ProjectId))
            .Where(entry => !options.ExcludeGenerated || !entry.IsGenerated)
            .Where(entry => isNameMatch(entry.Name))
            .OrderBy(entry => entry.DisplayPath, StringComparer.Ordinal)
            .ThenBy(entry => entry.Line)
            .ThenBy(entry => entry.Column)
            .ThenBy(entry => entry.Name, StringComparer.Ordinal)];
    }

    public async Task<IReadOnlyList<SymbolDeclaration>> EnrichAsync(
        IReadOnlyList<DeclarationIndexEntry> syntaxEntries,
        CancellationToken cancellationToken)
    {
        if (syntaxEntries.Count == 0)
        {
            return [];
        }

        List<SymbolDeclaration> declarations = [];
        await enrichmentGate.WaitAsync(cancellationToken);
        try
        {
            foreach (IGrouping<DocumentId, DeclarationIndexEntry> group in syntaxEntries.GroupBy(entry => entry.DocumentId))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Document? document = solution.GetDocument(group.Key);
                if (document is null)
                {
                    continue;
                }

                SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken);
                SemanticModel? semanticModel = await document.GetSemanticModelAsync(cancellationToken);
                if (root is null || semanticModel is null)
                {
                    continue;
                }

                foreach (DeclarationIndexEntry entry in group)
                {
                    if (enrichedDeclarations.TryGetValue(entry.Key, out SymbolDeclaration? cached))
                    {
                        declarations.Add(cached);
                        continue;
                    }

                    SyntaxNode node = root.FindNode(entry.Key.Span, getInnermostNodeForTie: true);
                    SymbolDeclaration? declaration = CreateDeclaration(document.Project, semanticModel, node, cancellationToken);
                    if (declaration is null)
                    {
                        continue;
                    }

                    enrichedDeclarations[entry.Key] = declaration;
                    SemanticEnrichmentCount++;
                    declarations.Add(declaration);
                }
            }
        }
        finally
        {
            enrichmentGate.Release();
        }

        return [.. declarations
            .OrderBy(declaration => declaration.Path, StringComparer.Ordinal)
            .ThenBy(declaration => declaration.Line)
            .ThenBy(declaration => declaration.Column)
            .ThenBy(declaration => declaration.Name, StringComparer.Ordinal)];
    }

    public async Task<IReadOnlyList<SymbolDeclaration>> EnrichAllAsync(
        IReadOnlyList<Project> projects,
        bool excludeGenerated,
        CancellationToken cancellationToken)
    {
        HashSet<ProjectId> projectIds = [.. projects.Select(project => project.Id)];
        IReadOnlyList<DeclarationIndexEntry> projectEntries = [.. entries
            .Where(entry => projectIds.Contains(entry.ProjectId))
            .Where(entry => !excludeGenerated || !entry.IsGenerated)];
        return await EnrichAsync(projectEntries, cancellationToken);
    }

    public bool TryGetCandidateRecord(
        string candidateId,
        IReadOnlyList<Project> projects,
        bool excludeGenerated,
        out CandidateRecord record)
    {
        record = null!;
        HashSet<ProjectId> projectIds = [.. projects.Select(project => project.Id)];
        if (!candidateRecords.TryGetValue(candidateId, out CandidateRecord? candidateRecord) ||
            candidateRecord.SolutionFingerprint != SolutionFingerprint ||
            !projectIds.Contains(candidateRecord.ProjectId) ||
            (excludeGenerated && candidateRecord.IsGenerated))
        {
            return false;
        }

        record = candidateRecord;
        return true;
    }

    public void RecordCandidate(FuzzySymbolCandidate candidate, Project? project)
    {
        if (string.IsNullOrWhiteSpace(candidate.CandidateId) || project is null)
        {
            return;
        }

        candidateRecords[candidate.CandidateId] = new CandidateRecord(
            CandidateId: candidate.CandidateId,
            SolutionFingerprint: SolutionFingerprint,
            ProjectId: project.Id,
            ProjectName: project.Name,
            TargetFramework: ProjectContextFacts.GetTargetFramework(project),
            Path: candidate.Path,
            Line: candidate.Line,
            Column: candidate.Column,
            EndLine: candidate.EndLine,
            EndColumn: candidate.EndColumn,
            IsGenerated: GeneratedCodeFacts.IsGeneratedPath(candidate.Path),
            Candidate: candidate);
    }

    public CandidateRecord? RecordCandidate(FuzzySymbolCandidate candidate, IReadOnlyList<Project> projects)
    {
        Project? project = FindCandidateProject(projects, candidate);
        RecordCandidate(candidate, project);
        return candidate.CandidateId is null
            ? null
            : candidateRecords.GetValueOrDefault(candidate.CandidateId);
    }

    public Project? FindCandidateProject(IReadOnlyList<Project> projects, FuzzySymbolCandidate candidate)
    {
        string? projectName = candidate.Selector?.Project ?? candidate.Facts.Project;
        string? targetFramework = candidate.Selector?.TargetFramework;
        return projects.FirstOrDefault(project =>
            project.Name == projectName &&
            (targetFramework is null || ProjectContextFacts.GetTargetFramework(project) == targetFramework)) ??
            projects.FirstOrDefault(project => project.Name == projectName);
    }

    private static SymbolDeclaration? CreateDeclaration(
        Project project,
        SemanticModel semanticModel,
        SyntaxNode node,
        CancellationToken cancellationToken)
    {
        ISymbol? symbol = semanticModel.GetDeclaredSymbol(node, cancellationToken);
        if (symbol is null)
        {
            return null;
        }

        Location? location = symbol.Locations.FirstOrDefault(location =>
            location.IsInSource &&
            location.SourceTree == node.SyntaxTree &&
            node.Span.Contains(location.SourceSpan.Start));

        location ??= symbol.Locations.FirstOrDefault(location => location.IsInSource);
        if (location is null)
        {
            return null;
        }

        FileLinePositionSpan lineSpan = location.GetLineSpan();
        if (!lineSpan.IsValid)
        {
            return null;
        }

        return new SymbolDeclaration(
            Name: symbol.Name,
            Kind: symbol.Kind.ToString(),
            Container: SymbolNavigationFacts.GetContainer(symbol),
            Facts: SymbolFactsBuilder.Create(symbol, project.Name),
            Path: PathDisplay.FromCurrentDirectory(lineSpan.Path),
            Line: lineSpan.StartLinePosition.Line + 1,
            Column: lineSpan.StartLinePosition.Character + 1,
            EndLine: lineSpan.EndLinePosition.Line + 1,
            EndColumn: lineSpan.EndLinePosition.Character + 1);
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

    private static string? GetSyntaxName(SyntaxNode node)
    {
        return node switch
        {
            BaseTypeDeclarationSyntax declaration => declaration.Identifier.ValueText,
            BaseNamespaceDeclarationSyntax declaration => declaration.Name.ToString(),
            DelegateDeclarationSyntax declaration => declaration.Identifier.ValueText,
            EnumMemberDeclarationSyntax declaration => declaration.Identifier.ValueText,
            ConstructorDeclarationSyntax declaration => declaration.Identifier.ValueText,
            DestructorDeclarationSyntax declaration => declaration.Identifier.ValueText,
            MethodDeclarationSyntax declaration => declaration.Identifier.ValueText,
            OperatorDeclarationSyntax declaration => declaration.OperatorToken.ValueText,
            ConversionOperatorDeclarationSyntax declaration => declaration.Type.ToString(),
            LocalFunctionStatementSyntax declaration => declaration.Identifier.ValueText,
            PropertyDeclarationSyntax declaration => declaration.Identifier.ValueText,
            IndexerDeclarationSyntax => "this",
            EventDeclarationSyntax declaration => declaration.Identifier.ValueText,
            UsingDirectiveSyntax declaration => declaration.Alias?.Name.Identifier.ValueText ?? declaration.Name?.ToString(),
            VariableDeclaratorSyntax declaration => declaration.Identifier.ValueText,
            ForEachStatementSyntax declaration => declaration.Identifier.ValueText,
            ParameterSyntax declaration => declaration.Identifier.ValueText,
            TypeParameterSyntax declaration => declaration.Identifier.ValueText,
            SingleVariableDesignationSyntax declaration => declaration.Identifier.ValueText,
            _ => null
        };
    }

    private static string CreateSolutionFingerprint(Solution solution)
    {
        string canonical = string.Join(
            "\n",
            solution.Projects
                .OrderBy(project => project.FilePath, StringComparer.Ordinal)
                .ThenBy(project => project.Name, StringComparer.Ordinal)
                .Select(project => string.Join(
                    "|",
                    project.Name,
                    project.FilePath ?? "",
                    ProjectContextFacts.GetTargetFramework(project) ?? "",
                    project.AssemblyName ?? "")));
        byte[] hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash).ToLowerInvariant()[..16];
    }
}

internal static class DeclarationIndexProvider
{
    private static readonly ConditionalWeakTable<Solution, DeclarationIndexCache> Indexes = new();

    public static Task<DeclarationIndex> GetOrCreateAsync(Solution solution, CancellationToken cancellationToken)
    {
        return Indexes.GetValue(solution, static _ => new DeclarationIndexCache()).GetOrCreateAsync(solution, cancellationToken);
    }

    private sealed class DeclarationIndexCache
    {
        private readonly SemaphoreSlim gate = new(1, 1);
        private DeclarationIndex? index;

        public async Task<DeclarationIndex> GetOrCreateAsync(Solution solution, CancellationToken cancellationToken)
        {
            if (index is not null)
            {
                return index;
            }

            await gate.WaitAsync(cancellationToken);
            try
            {
                index ??= await DeclarationIndex.CreateAsync(solution, cancellationToken);
                return index;
            }
            finally
            {
                gate.Release();
            }
        }
    }
}

internal sealed record DeclarationIndexEntry(
    DeclarationKey Key,
    DocumentId DocumentId,
    ProjectId ProjectId,
    string ProjectName,
    string Name,
    string SyntaxKind,
    string FullPath,
    string DisplayPath,
    bool IsGenerated,
    int Line,
    int Column);

internal readonly record struct DeclarationKey(DocumentId DocumentId, TextSpan Span);

internal sealed record CandidateRecord(
    string CandidateId,
    string SolutionFingerprint,
    ProjectId ProjectId,
    string ProjectName,
    string? TargetFramework,
    string Path,
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    bool IsGenerated,
    FuzzySymbolCandidate Candidate);

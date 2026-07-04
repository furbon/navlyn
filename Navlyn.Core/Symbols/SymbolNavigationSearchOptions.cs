using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Navlyn.GeneratedCode;
using Navlyn.Paths;

namespace Navlyn.Symbols;

internal static class SymbolNavigationSearchScopes
{
    public const string File = "file";
    public const string Project = "project";
    public const string DependentProjects = "dependent-projects";
    public const string Solution = "solution";
    public const string WorkspaceSet = "workspace-set";

    public static readonly IReadOnlyList<string> Values =
    [
        File,
        Project,
        DependentProjects,
        Solution,
        WorkspaceSet
    ];
}

internal sealed record SymbolNavigationSearchOptions(string Scope, int MaxDocuments)
{
    public const string DefaultScope = SymbolNavigationSearchScopes.DependentProjects;
    public const int DefaultMaxDocuments = 200;

    public static SymbolNavigationSearchOptions Default { get; } = new(DefaultScope, DefaultMaxDocuments);

    public static SymbolNavigationSearchOptions Create(string? scope, int? maxDocuments)
    {
        return new SymbolNavigationSearchOptions(
            string.IsNullOrWhiteSpace(scope) ? DefaultScope : scope.Trim(),
            maxDocuments ?? DefaultMaxDocuments);
    }
}

internal sealed record SymbolNavigationSearchMetadata(
    string Scope,
    string CostClass,
    bool Partial,
    int CandidateProjectCount,
    int SearchedProjectCount,
    int CandidateDocumentCount,
    int PrefilteredDocumentCount,
    int SearchedDocumentCount,
    bool LexicalPrefilterApplied,
    int MaxDocuments,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? TruncationReason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? NextScope,
    IReadOnlyList<string> RerunHints)
{
    public static SymbolNavigationSearchMetadata Local(string scope = SymbolNavigationSearchScopes.File)
    {
        return new SymbolNavigationSearchMetadata(
            Scope: scope,
            CostClass: "local",
            Partial: false,
            CandidateProjectCount: 1,
            SearchedProjectCount: 1,
            CandidateDocumentCount: 1,
            PrefilteredDocumentCount: 1,
            SearchedDocumentCount: 1,
            LexicalPrefilterApplied: false,
            MaxDocuments: 1,
            TruncationReason: null,
            NextScope: null,
            RerunHints: []);
    }
}

internal sealed record SymbolNavigationSearchPlan(
    SymbolNavigationSearchMetadata Metadata,
    ImmutableHashSet<Document> Documents,
    IReadOnlyList<Project> Projects);

internal static class SymbolNavigationSearchPlanner
{
    public static async Task<SymbolNavigationSearchPlan> CreateAsync(
        Solution solution,
        SourceSymbolResolution source,
        SymbolNavigationSearchOptions options,
        bool excludeGenerated,
        CancellationToken cancellationToken)
    {
        Document? sourceDocument = solution.GetDocument(source.DocumentId);
        Project? sourceProject = solution.GetProject(source.ProjectId) ?? sourceDocument?.Project;
        IReadOnlyList<Project> candidateProjects = GetCandidateProjects(solution, sourceProject, options.Scope);
        IReadOnlyList<Document> candidateDocuments = options.Scope == SymbolNavigationSearchScopes.File && sourceDocument is not null
            ? [sourceDocument]
            : OrderDocuments(
                candidateProjects
                    .SelectMany(project => project.Documents)
                    .Where(document => document.FilePath is not null)
                    .Where(document => !excludeGenerated || !GeneratedCodeFacts.IsGeneratedPath(document.FilePath!)),
                source.DocumentId);

        IReadOnlyList<string> lexicalTerms = GetLexicalTerms(source.Symbol);
        IReadOnlyList<Document> prefilteredDocuments = lexicalTerms.Count == 0
            ? candidateDocuments
            : await ApplyLexicalPrefilterAsync(candidateDocuments, lexicalTerms, source.DocumentId, cancellationToken);

        IReadOnlyList<Document> searchedDocuments = [.. prefilteredDocuments.Take(options.MaxDocuments)];
        bool partial = prefilteredDocuments.Count > searchedDocuments.Count;
        string? nextScope = GetNextScope(options.Scope);
        IReadOnlyList<string> rerunHints = CreateRerunHints(options, partial, nextScope);

        SymbolNavigationSearchMetadata metadata = new(
            Scope: options.Scope,
            CostClass: GetCostClass(options.Scope),
            Partial: partial,
            CandidateProjectCount: candidateProjects.Count,
            SearchedProjectCount: searchedDocuments.Select(document => document.Project.Id).Distinct().Count(),
            CandidateDocumentCount: candidateDocuments.Count,
            PrefilteredDocumentCount: prefilteredDocuments.Count,
            SearchedDocumentCount: searchedDocuments.Count,
            LexicalPrefilterApplied: lexicalTerms.Count > 0,
            MaxDocuments: options.MaxDocuments,
            TruncationReason: partial ? "document-budget" : null,
            NextScope: nextScope,
            RerunHints: rerunHints);

        return new SymbolNavigationSearchPlan(
            metadata,
            searchedDocuments.ToImmutableHashSet(),
            [.. searchedDocuments
                .Select(document => document.Project)
                .DistinctBy(project => project.Id)
                .OrderBy(project => project.Name, StringComparer.Ordinal)
                .ThenBy(project => project.FilePath, StringComparer.Ordinal)]);
    }

    private static IReadOnlyList<Project> GetCandidateProjects(
        Solution solution,
        Project? sourceProject,
        string scope)
    {
        if (sourceProject is null)
        {
            return OrderProjects(solution.Projects);
        }

        if (scope == SymbolNavigationSearchScopes.File)
        {
            return [sourceProject];
        }

        if (scope == SymbolNavigationSearchScopes.Project)
        {
            return [sourceProject];
        }

        if (scope == SymbolNavigationSearchScopes.DependentProjects)
        {
            ProjectDependencyGraph graph = solution.GetProjectDependencyGraph();
            HashSet<ProjectId> ids = [sourceProject.Id];
            foreach (ProjectId id in graph.GetProjectsThatTransitivelyDependOnThisProject(sourceProject.Id))
            {
                ids.Add(id);
            }

            return OrderProjects(ids
                .Select(solution.GetProject)
                .OfType<Project>());
        }

        return OrderProjects(solution.Projects);
    }

    private static IReadOnlyList<Project> OrderProjects(IEnumerable<Project> projects)
    {
        return [.. projects
            .DistinctBy(project => project.Id)
            .OrderBy(project => project.Name, StringComparer.Ordinal)
            .ThenBy(project => project.FilePath, StringComparer.Ordinal)];
    }

    private static IReadOnlyList<Document> OrderDocuments(IEnumerable<Document> documents, DocumentId sourceDocumentId)
    {
        return [.. documents
            .DistinctBy(document => document.Id)
            .OrderBy(document => document.Id == sourceDocumentId ? 0 : 1)
            .ThenBy(document => GetDocumentPath(document), StringComparer.Ordinal)
            .ThenBy(document => document.Project.Name, StringComparer.Ordinal)];
    }

    private static async Task<IReadOnlyList<Document>> ApplyLexicalPrefilterAsync(
        IReadOnlyList<Document> documents,
        IReadOnlyList<string> lexicalTerms,
        DocumentId sourceDocumentId,
        CancellationToken cancellationToken)
    {
        List<Document> matches = [];
        foreach (Document document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string text = (await document.GetTextAsync(cancellationToken)).ToString();
            if (lexicalTerms.Any(term => text.Contains(term, StringComparison.Ordinal)))
            {
                matches.Add(document);
            }
        }

        return matches.Count == 0
            ? documents.Where(document => document.Id == sourceDocumentId).ToArray()
            : OrderDocuments(matches, sourceDocumentId);
    }

    private static IReadOnlyList<string> GetLexicalTerms(ISymbol symbol)
    {
        string? name = symbol switch
        {
            IMethodSymbol { MethodKind: MethodKind.Constructor } method => method.ContainingType?.Name,
            IMethodSymbol { MethodKind: MethodKind.Destructor } method => method.ContainingType?.Name,
            _ => symbol.Name
        };

        if (string.IsNullOrWhiteSpace(name) ||
            name.StartsWith(".", StringComparison.Ordinal) ||
            name.Contains(' '))
        {
            return [];
        }

        return [name];
    }

    private static string GetCostClass(string scope)
    {
        return scope is SymbolNavigationSearchScopes.File or SymbolNavigationSearchScopes.Project
            ? "moderate"
            : "expensive";
    }

    private static string? GetNextScope(string scope)
    {
        return scope switch
        {
            SymbolNavigationSearchScopes.File => SymbolNavigationSearchScopes.Project,
            SymbolNavigationSearchScopes.Project => SymbolNavigationSearchScopes.DependentProjects,
            SymbolNavigationSearchScopes.DependentProjects => SymbolNavigationSearchScopes.Solution,
            SymbolNavigationSearchScopes.WorkspaceSet => SymbolNavigationSearchScopes.Solution,
            _ => null
        };
    }

    private static IReadOnlyList<string> CreateRerunHints(
        SymbolNavigationSearchOptions options,
        bool partial,
        string? nextScope)
    {
        List<string> hints = [];
        if (partial)
        {
            hints.Add($"Increase --max-documents above {options.MaxDocuments} to search the remaining lexically matching documents.");
        }

        if (nextScope is not null && nextScope != options.Scope)
        {
            hints.Add($"Use --scope {nextScope} to expand the search scope.");
        }

        if (options.Scope != SymbolNavigationSearchScopes.Solution)
        {
            hints.Add("Use --scope solution when a full solution search is required.");
        }

        return hints;
    }

    private static string GetDocumentPath(Document document)
    {
        return document.FilePath is null
            ? document.Name
            : PathDisplay.FromCurrentDirectory(document.FilePath);
    }
}

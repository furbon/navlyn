using Microsoft.CodeAnalysis;
using Navlyn.Languages;
using Navlyn.GeneratedCode;
using Navlyn.Paths;
using Navlyn.RepoGraph;
using Navlyn.Symbols;

namespace Navlyn.Testing;

internal sealed class TestDiscoveryResolver
{
    public async Task<TestDiscoveryResult> DiscoverAsync(
        IReadOnlyList<Project> projects,
        IReadOnlyList<Project>? explicitTestProjects,
        bool excludeGenerated,
        bool includeSnippets,
        int snippetLines,
        CancellationToken cancellationToken)
    {
        string repositoryRoot = PathDisplay.FindRepositoryRoot(projects.Select(project => project.FilePath).FirstOrDefault(path => path is not null) ?? Directory.GetCurrentDirectory()) ??
            Directory.GetCurrentDirectory();
        ProjectFileReader reader = new();
        IReadOnlyList<Project> candidateProjects = explicitTestProjects ?? [.. projects.Where(project => IsTestProject(project, reader, repositoryRoot))];
        List<TestProjectInfo> testProjects = [.. candidateProjects
            .Select(project => CreateProjectInfo(project, reader, repositoryRoot))
            .OrderBy(project => project.Path, StringComparer.Ordinal)
            .ThenBy(project => project.Name, StringComparer.Ordinal)];
        Dictionary<string, TestProjectInfo> infoByName = testProjects.ToDictionary(project => project.Name, StringComparer.Ordinal);
        List<TestImpactCandidate> candidates = [];

        foreach (Project project in candidateProjects.OrderBy(project => project.FilePath, StringComparer.Ordinal).ThenBy(project => project.Name, StringComparer.Ordinal))
        {
            foreach (Document document in project.Documents
                .Where(document => document.FilePath is not null)
                .Where(SourceLanguageFacts.IsSupportedDocument)
                .Where(document => !excludeGenerated || !GeneratedCodeFacts.IsGeneratedPath(document.FilePath))
                .OrderBy(document => document.FilePath, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken);
                SemanticModel? semanticModel = await document.GetSemanticModelAsync(cancellationToken);
                if (root is null || semanticModel is null)
                {
                    continue;
                }

                foreach (IMethodSymbol method in EnumerateSourceMethods(root, semanticModel, cancellationToken))
                {
                    string? framework = GetTestFramework(method);
                    if (framework is null)
                    {
                        continue;
                    }

                    SymbolSourceLocation? location = SymbolNavigationFacts.GetSourceLocations(method, excludeGenerated).FirstOrDefault();
                    if (location is null)
                    {
                        continue;
                    }

                    candidates.Add(new TestImpactCandidate(
                        Kind: "testMethod",
                        Framework: framework,
                        Name: method.Name,
                        Container: SymbolNavigationFacts.GetContainer(method),
                        Facts: SymbolFactsBuilder.Create(method, project.Name),
                        Path: location.Path,
                        Line: location.Line,
                        Column: location.Column,
                        EndLine: location.EndLine,
                        EndColumn: location.EndColumn,
                        Project: infoByName[project.Name],
                        Confidence: "low",
                        Score: 10,
                        ReasonCodes: ["test-method-attribute"],
                        Evidence: [new TestEvidence("attribute", location.Path, location.Line, location.Column, location.EndLine, location.EndColumn, ["test-method-attribute"])],
                        Snippet: includeSnippets ? FuzzySnippetReader.TryRead(location.Path, location.Line, snippetLines) : null));
                }
            }
        }

        return new TestDiscoveryResult(testProjects, [.. candidates
            .OrderBy(candidate => candidate.Project.Path, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Path, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Line)
            .ThenBy(candidate => candidate.Column)
            .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)]);
    }

    private static bool IsTestProject(Project project, ProjectFileReader reader, string repositoryRoot)
    {
        TestProjectInfo info = CreateProjectInfo(project, reader, repositoryRoot);
        return info.ReasonCodes.Count > 0;
    }

    private static TestProjectInfo CreateProjectInfo(Project project, ProjectFileReader reader, string repositoryRoot)
    {
        string? displayPath = project.FilePath is null ? null : PathDisplay.FromCurrentDirectory(project.FilePath);
        ProjectFileFacts facts = reader.Read(displayPath, repositoryRoot);
        RepoGraphProjectClassification classification = ProjectClassifier.Classify(project.Name, displayPath, project.AssemblyName, facts);
        IReadOnlyList<string> reasons = classification.Kind == "test"
            ? classification.ReasonCodes
            : [];
        return new TestProjectInfo(
            Name: project.Name,
            Path: displayPath,
            TargetFramework: Navlyn.Workspaces.ProjectContextFacts.GetTargetFramework(project),
            ReasonCodes: reasons);
    }

    private static IReadOnlyList<IMethodSymbol> EnumerateSourceMethods(
        SyntaxNode root,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return [.. root.DescendantNodes()
            .Select(node => semanticModel.GetDeclaredSymbol(node, cancellationToken))
            .OfType<IMethodSymbol>()
            .Where(method => method.MethodKind == MethodKind.Ordinary)
            .Where(method => !method.IsImplicitlyDeclared)
            .Where(method => method.Locations.Any(location => location.IsInSource))
            .GroupBy(method => method.GetDocumentationCommentId() ?? $"{method.ContainingType?.ToDisplayString()}.{method.Name}:{method.Locations.FirstOrDefault()?.SourceSpan.Start}")
            .Select(group => group.First())
            .OrderBy(method => method.Locations.FirstOrDefault()?.SourceTree?.FilePath, StringComparer.Ordinal)
            .ThenBy(method => method.Locations.FirstOrDefault()?.SourceSpan.Start ?? int.MaxValue)];
    }

    private static string? GetTestFramework(IMethodSymbol method)
    {
        foreach (AttributeData attribute in method.GetAttributes())
        {
            string name = attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ??
                attribute.AttributeClass?.Name ??
                string.Empty;
            string shortName = name.EndsWith("Attribute", StringComparison.Ordinal) ? name[..^"Attribute".Length] : name;
            if (shortName.EndsWith(".Fact", StringComparison.Ordinal) || shortName is "Fact" or "Theory")
            {
                return "xunit";
            }

            if (shortName.EndsWith(".Theory", StringComparison.Ordinal))
            {
                return "xunit";
            }

            if (shortName.EndsWith(".Test", StringComparison.Ordinal) || shortName is "Test" or "TestCase")
            {
                return "nunit";
            }

            if (shortName.EndsWith(".TestCase", StringComparison.Ordinal))
            {
                return "nunit";
            }

            if (shortName.EndsWith(".TestMethod", StringComparison.Ordinal) || shortName is "TestMethod" or "DataTestMethod")
            {
                return "mstest";
            }

            if (shortName.EndsWith(".DataTestMethod", StringComparison.Ordinal))
            {
                return "mstest";
            }
        }

        return null;
    }
}


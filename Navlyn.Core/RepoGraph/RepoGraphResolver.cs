using Microsoft.CodeAnalysis;
using Navlyn.Paths;
using Navlyn.Workspaces;

namespace Navlyn.RepoGraph;

internal sealed class RepoGraphResolver
{
    public RepoGraphResult Resolve(
        LoadedWorkspace workspace,
        IReadOnlyList<Project> projects,
        RepoGraphOptions options)
    {
        string repositoryRoot = PathDisplay.FindRepositoryRoot(workspace.FullPath) ??
            Path.GetDirectoryName(workspace.FullPath) ??
            Directory.GetCurrentDirectory();

        ProjectFileReader reader = new();
        Dictionary<string, ProjectFileFacts> factsByProjectPath = new(StringComparer.Ordinal);
        List<ProjectWithFacts> projectFacts = [];

        foreach (Project project in projects.OrderBy(project => project.FilePath, StringComparer.Ordinal).ThenBy(project => project.Name, StringComparer.Ordinal))
        {
            string? displayPath = project.FilePath is null ? null : PathDisplay.FromCurrentDirectory(project.FilePath);
            if (displayPath is not null && !factsByProjectPath.TryGetValue(displayPath, out _))
            {
                factsByProjectPath[displayPath] = reader.Read(displayPath, repositoryRoot);
            }

            ProjectFileFacts fileFacts = displayPath is null
                ? reader.Read(null, repositoryRoot)
                : factsByProjectPath[displayPath];
            string? targetFramework = ProjectContextFacts.GetTargetFramework(project);
            IReadOnlyList<string> targetFrameworks = fileFacts.TargetFrameworks.Count == 0 && targetFramework is not null
                ? [targetFramework]
                : fileFacts.TargetFrameworks;
            string id = CreateProjectId(displayPath, project.Name, targetFramework);
            RepoGraphProjectClassification? classification = options.IncludeClassification
                ? ProjectClassifier.Classify(project.Name, displayPath, project.AssemblyName, fileFacts)
                : null;

            RepoGraphProject graphProject = new(
                Id: id,
                Name: project.Name,
                Path: displayPath,
                Language: project.Language,
                AssemblyName: project.AssemblyName,
                TargetFramework: targetFramework,
                TargetFrameworks: targetFrameworks,
                OutputType: fileFacts.OutputType,
                Sdk: fileFacts.Sdk,
                Nullable: fileFacts.Nullable,
                ImplicitUsings: fileFacts.ImplicitUsings,
                LanguageVersion: ProjectContextFacts.GetLanguageVersion(project),
                PreprocessorSymbols: options.IncludePreprocessorSymbols
                    ? NullIfEmpty(ProjectContextFacts.GetPreprocessorSymbols(project))
                    : null,
                Classification: classification);

            projectFacts.Add(new ProjectWithFacts(graphProject, fileFacts, project.FilePath));
        }

        IReadOnlyList<ProjectWithFacts> orderedProjects = [.. projectFacts
            .OrderBy(project => project.Project.Path, StringComparer.Ordinal)
            .ThenBy(project => project.Project.TargetFramework, StringComparer.Ordinal)
            .ThenBy(project => project.Project.Name, StringComparer.Ordinal)];

        RepoGraphEdgesSection edges = CreateEdges(workspace.Solution, orderedProjects, options.IncludePackages);
        RepoGraphRelationshipsSection relationships = CreateRelationships(orderedProjects, edges.ProjectReferences, options.RelationshipLimit);
        RepoGraphRepositorySection? repository = options.IncludeMsbuildFiles
            ? CreateRepositorySection(repositoryRoot, reader, orderedProjects)
            : null;

        IReadOnlyList<string> warnings = [.. orderedProjects
            .SelectMany(project => project.FileFacts.Warnings.Select(warning => $"{project.Project.Id}:{warning}"))
            .OrderBy(warning => warning, StringComparer.Ordinal)];

        return new RepoGraphResult(
            Workspace: workspace.DisplayPath,
            Kind: workspace.Kind,
            Command: "repo-graph",
            Projects: new RepoGraphProjectsSection(orderedProjects.Count, [.. orderedProjects.Select(project => project.Project)]),
            Edges: edges,
            Relationships: relationships,
            Repository: repository,
            Limits: new RepoGraphLimits(options.RelationshipLimit),
            Truncated: relationships.Truncated,
            Warnings: warnings,
            NextActions: []);
    }

    private static RepoGraphEdgesSection CreateEdges(
        Solution solution,
        IReadOnlyList<ProjectWithFacts> projects,
        bool includePackages)
    {
        Dictionary<string, ProjectWithFacts> byPath = projects
            .Where(project => project.Project.Path is not null)
            .GroupBy(project => project.Project.Path!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        Dictionary<string, ProjectWithFacts> byName = projects
            .GroupBy(project => project.Project.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        Dictionary<string, string> roslynProjectIds = solution.Projects.ToDictionary(project => project.Id.Id.ToString(), project => project.Name, StringComparer.Ordinal);

        List<RepoGraphProjectReferenceEdge> projectReferences = [];
        foreach (Project roslynProject in solution.Projects)
        {
            ProjectWithFacts? from = projects.FirstOrDefault(project => project.Project.Name == roslynProject.Name);
            if (from is null)
            {
                continue;
            }

            foreach (ProjectReference reference in roslynProject.ProjectReferences)
            {
                Project? referencedProject = solution.GetProject(reference.ProjectId);
                string? toId = referencedProject is null || !byName.TryGetValue(referencedProject.Name, out ProjectWithFacts? toProject)
                    ? null
                    : toProject.Project.Id;
                projectReferences.Add(new RepoGraphProjectReferenceEdge(
                    FromProjectId: from.Project.Id,
                    ToProjectId: toId,
                    Include: referencedProject?.FilePath is null ? reference.ProjectId.Id.ToString() : PathDisplay.FromCurrentDirectory(referencedProject.FilePath),
                    Path: referencedProject?.FilePath is null ? null : PathDisplay.FromCurrentDirectory(referencedProject.FilePath),
                    ReasonCodes: ["project-reference"]));
            }
        }

        foreach (ProjectWithFacts project in projects)
        {
            foreach (ProjectFileProjectReference reference in project.FileFacts.ProjectReferences)
            {
                string? toId = reference.Path is not null && byPath.TryGetValue(reference.Path, out ProjectWithFacts? toProject)
                    ? toProject.Project.Id
                    : null;

                bool exists = projectReferences.Any(edge =>
                    edge.FromProjectId == project.Project.Id &&
                    edge.ToProjectId == toId &&
                    edge.Path == reference.Path);
                if (!exists)
                {
                    projectReferences.Add(new RepoGraphProjectReferenceEdge(
                        FromProjectId: project.Project.Id,
                        ToProjectId: toId,
                        Include: reference.Include,
                        Path: reference.Path,
                        ReasonCodes: ["project-reference"]));
                }
            }
        }

        IReadOnlyList<RepoGraphPackageReferenceEdge> packageReferences = includePackages
            ? [.. projects
                .SelectMany(project => project.FileFacts.PackageReferences.Select(package => new RepoGraphPackageReferenceEdge(
                    ProjectId: project.Project.Id,
                    Name: package.Name,
                    Version: package.Version,
                    IsCentralVersion: package.IsCentralVersion,
                    PrivateAssets: package.PrivateAssets,
                    IncludeAssets: package.IncludeAssets,
                    ExcludeAssets: package.ExcludeAssets)))
                .OrderBy(edge => edge.ProjectId, StringComparer.Ordinal)
                .ThenBy(edge => edge.Name, StringComparer.Ordinal)
                .ThenBy(edge => edge.Version, StringComparer.Ordinal)]
            : [];

        _ = roslynProjectIds;
        return new RepoGraphEdgesSection(
            ProjectReferences: [.. projectReferences
                .OrderBy(edge => edge.FromProjectId, StringComparer.Ordinal)
                .ThenBy(edge => edge.ToProjectId, StringComparer.Ordinal)
                .ThenBy(edge => edge.Include, StringComparer.Ordinal)],
            PackageReferences: packageReferences);
    }

    private static RepoGraphRelationshipsSection CreateRelationships(
        IReadOnlyList<ProjectWithFacts> projects,
        IReadOnlyList<RepoGraphProjectReferenceEdge> projectReferences,
        int limit)
    {
        Dictionary<string, ProjectWithFacts> byId = projects.ToDictionary(project => project.Project.Id, StringComparer.Ordinal);
        List<RepoGraphRelationship> relationships = [];

        foreach (RepoGraphProjectReferenceEdge edge in projectReferences)
        {
            if (edge.ToProjectId is null ||
                !byId.TryGetValue(edge.FromProjectId, out ProjectWithFacts? from) ||
                !byId.TryGetValue(edge.ToProjectId, out ProjectWithFacts? to))
            {
                continue;
            }

            if (from.Project.Classification?.Kind == "test" &&
                to.Project.Classification?.Kind is not "test" and not "benchmark" and not "unknown")
            {
                relationships.Add(new RepoGraphRelationship(
                    Kind: "tests",
                    FromProjectId: from.Project.Id,
                    ToProjectId: to.Project.Id,
                    Confidence: "high",
                    ReasonCodes: [.. new[]
                    {
                        "test-project-classification",
                        "project-reference-to-production"
                    }
                    .Concat(from.Project.Classification.ReasonCodes.Where(reason => reason.EndsWith("-package", StringComparison.Ordinal)))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(reason => reason, StringComparer.Ordinal)]));
            }
        }

        IReadOnlyList<RepoGraphRelationship> ordered = [.. relationships
            .OrderBy(relationship => relationship.Kind, StringComparer.Ordinal)
            .ThenBy(relationship => relationship.FromProjectId, StringComparer.Ordinal)
            .ThenBy(relationship => relationship.ToProjectId, StringComparer.Ordinal)];

        return new RepoGraphRelationshipsSection(
            TotalItems: ordered.Count,
            Limit: limit,
            Truncated: ordered.Count > limit,
            Items: [.. ordered.Take(limit)]);
    }

    private static RepoGraphRepositorySection CreateRepositorySection(
        string repositoryRoot,
        ProjectFileReader reader,
        IReadOnlyList<ProjectWithFacts> projects)
    {
        string? globalJson = File.Exists(Path.Combine(repositoryRoot, "global.json"))
            ? PathDisplay.FromCurrentDirectory(Path.Combine(repositoryRoot, "global.json"))
            : null;

        return new RepoGraphRepositorySection(
            Root: PathDisplay.FromCurrentDirectory(repositoryRoot),
            GlobalJson: globalJson,
            CentralPackageManagement: reader.GetCentralPackageManagement(repositoryRoot),
            MsbuildFiles: reader.DiscoverMsbuildFiles(repositoryRoot, projects));
    }

    private static string CreateProjectId(string? path, string name, string? targetFramework)
    {
        string identityPath = path ?? name;
        return $"project:{identityPath}:{targetFramework ?? string.Empty}";
    }

    private static IReadOnlyList<string>? NullIfEmpty(IReadOnlyList<string> values)
    {
        return values.Count == 0 ? null : values;
    }
}


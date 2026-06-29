using System.Text.Json.Serialization;

namespace Navlyn.RepoGraph;

internal sealed record RepoGraphOptions(
    bool IncludePackages,
    bool IncludeMsbuildFiles,
    bool IncludePreprocessorSymbols,
    bool IncludeClassification,
    int RelationshipLimit);

internal sealed record RepoGraphResult(
    string Workspace,
    string Kind,
    string Command,
    RepoGraphProjectsSection Projects,
    RepoGraphEdgesSection Edges,
    RepoGraphRelationshipsSection Relationships,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    RepoGraphRepositorySection? Repository,
    RepoGraphLimits Limits,
    bool Truncated,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<RepoGraphNextAction> NextActions);

internal sealed record RepoGraphProjectsSection(int TotalProjects, IReadOnlyList<RepoGraphProject> Items);

internal sealed record RepoGraphProject(
    string Id,
    string Name,
    string? Path,
    string Language,
    string? AssemblyName,
    string? TargetFramework,
    IReadOnlyList<string> TargetFrameworks,
    string? OutputType,
    string? Sdk,
    string? Nullable,
    string? ImplicitUsings,
    string? LanguageVersion,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? PreprocessorSymbols,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    RepoGraphProjectClassification? Classification);

internal sealed record RepoGraphProjectClassification(
    string Kind,
    string Confidence,
    IReadOnlyList<string> ReasonCodes);

internal sealed record RepoGraphEdgesSection(
    IReadOnlyList<RepoGraphProjectReferenceEdge> ProjectReferences,
    IReadOnlyList<RepoGraphPackageReferenceEdge> PackageReferences);

internal sealed record RepoGraphProjectReferenceEdge(
    string FromProjectId,
    string? ToProjectId,
    string Include,
    string? Path,
    IReadOnlyList<string> ReasonCodes);

internal sealed record RepoGraphPackageReferenceEdge(
    string ProjectId,
    string Name,
    string? Version,
    bool IsCentralVersion,
    string? PrivateAssets,
    string? IncludeAssets,
    string? ExcludeAssets);

internal sealed record RepoGraphRelationshipsSection(
    int TotalItems,
    int Limit,
    bool Truncated,
    IReadOnlyList<RepoGraphRelationship> Items);

internal sealed record RepoGraphRelationship(
    string Kind,
    string FromProjectId,
    string ToProjectId,
    string Confidence,
    IReadOnlyList<string> ReasonCodes);

internal sealed record RepoGraphRepositorySection(
    string Root,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? GlobalJson,
    RepoGraphCentralPackageManagement CentralPackageManagement,
    IReadOnlyList<RepoGraphMsbuildFile> MsbuildFiles);

internal sealed record RepoGraphCentralPackageManagement(bool Enabled, IReadOnlyList<string> Files);

internal sealed record RepoGraphMsbuildFile(
    string Kind,
    string Path,
    IReadOnlyList<string> AppliesToProjectIds);

internal sealed record RepoGraphLimits(int RelationshipLimit);

internal sealed record RepoGraphNextAction(string Command, string Workspace, string Reason);

internal sealed record ProjectFileFacts(
    string? Sdk,
    string? OutputType,
    string? Nullable,
    string? ImplicitUsings,
    string? TargetFramework,
    IReadOnlyList<string> TargetFrameworks,
    bool PackAsTool,
    IReadOnlyList<ProjectFilePackageReference> PackageReferences,
    IReadOnlyList<ProjectFileProjectReference> ProjectReferences,
    IReadOnlyList<string> Warnings);

internal sealed record ProjectFilePackageReference(
    string Name,
    string? Version,
    bool IsCentralVersion,
    string? PrivateAssets,
    string? IncludeAssets,
    string? ExcludeAssets);

internal sealed record ProjectFileProjectReference(string Include, string? Path);


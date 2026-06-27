using Microsoft.CodeAnalysis;
using Navlyn.Diagnostics;
using Navlyn.Paths;
using Navlyn.Workspaces;

namespace Navlyn.PublicApi;

internal sealed class PublicApiDiffResolver
{
    public async Task<PublicApiDiffExecutionResult> ResolveAsync(
        LoadedWorkspace workspace,
        IReadOnlyList<Project> projects,
        IReadOnlyList<PublicApiProjectFilter>? projectFilters,
        PublicApiDiffOptions options,
        CancellationToken cancellationToken)
    {
        string? repositoryRoot = PathDisplay.FindRepositoryRoot(workspace.FullPath);
        if (repositoryRoot is null)
        {
            return PublicApiDiffExecutionResult.Failed(
                DiagnosticIds.GitRepositoryNotFound,
                "Git repository root was not found for public API diff.",
                ExitCodes.UsageError);
        }

        GitSourceSnapshotReader reader = new();
        GitSourceReadResult baseFiles = await reader.ReadRefAsync(repositoryRoot, options.BaseRef, options.ExcludeGenerated, cancellationToken);
        if (baseFiles.Error is not null)
        {
            return PublicApiDiffExecutionResult.Failed(baseFiles.Error.DiagnosticId, baseFiles.Error.Message, baseFiles.Error.ExitCode);
        }

        IReadOnlyList<GitSourceFile> headFiles;
        string headLabel;
        string mode;
        if (string.IsNullOrWhiteSpace(options.HeadRef))
        {
            headFiles = reader.ReadWorkingTree(projects, options.ExcludeGenerated);
            headLabel = "workingTree";
            mode = "gitSourceSnapshot";
        }
        else
        {
            GitSourceReadResult headResult = await reader.ReadRefAsync(repositoryRoot, options.HeadRef, options.ExcludeGenerated, cancellationToken);
            if (headResult.Error is not null)
            {
                return PublicApiDiffExecutionResult.Failed(headResult.Error.DiagnosticId, headResult.Error.Message, headResult.Error.ExitCode);
            }

            headFiles = headResult.Files;
            headLabel = options.HeadRef;
            mode = "gitSourceSnapshot";
        }

        IReadOnlyList<string> projectPathPrefixes = GetProjectPathPrefixes(projects);
        PublicApiSnapshot baseSnapshot = CreateSnapshot("base", options.BaseRef, FilterToProjectScope(baseFiles.Files, projectPathPrefixes), options.SymbolLimit, options.ExcludeGenerated);
        PublicApiSnapshot headSnapshot = CreateSnapshot("head", headLabel, FilterToProjectScope(headFiles, projectPathPrefixes), options.SymbolLimit, options.ExcludeGenerated);
        PublicApiChangesSection changes = new PublicApiComparer().Compare(
            baseSnapshot,
            headSnapshot,
            options.IncludeAdditions,
            options.IncludeAttributes,
            options.ChangeLimit);
        PublicApiDiffSummary summary = CreateSummary(changes);

        List<string> warnings =
        [
            "Public API diff uses source-level snapshots; it is not a NuGet package or IL diff."
        ];
        warnings.AddRange(baseSnapshot.Warnings);
        warnings.AddRange(headSnapshot.Warnings);
        if (baseSnapshot.Truncated || headSnapshot.Truncated)
        {
            warnings.Add("Public API symbol snapshot was truncated; diff results may be incomplete.");
        }

        PublicApiDiffResult result = new(
            Workspace: workspace.DisplayPath,
            Kind: workspace.Kind,
            Command: "public-api-diff",
            Comparison: new PublicApiComparison(options.BaseRef, headLabel, mode, workspace.DisplayPath),
            Projects: projectFilters,
            Limits: new PublicApiDiffLimits(options.SymbolLimit, options.ChangeLimit),
            Summary: summary,
            Changes: changes,
            Truncated: baseSnapshot.Truncated || headSnapshot.Truncated || changes.Truncated,
            Warnings: [.. warnings.Distinct(StringComparer.Ordinal).OrderBy(warning => warning, StringComparer.Ordinal)],
            NextActions: []);

        return PublicApiDiffExecutionResult.Succeeded(result);
    }

    private static PublicApiSnapshot CreateSnapshot(
        string side,
        string gitRef,
        IReadOnlyList<GitSourceFile> files,
        int symbolLimit,
        bool excludeGenerated)
    {
        PublicApiSymbolExtractor extractor = new();
        List<PublicApiSymbol> symbols = [];
        List<string> warnings = [];

        foreach (GitSourceFile file in files)
        {
            try
            {
                symbols.AddRange(extractor.Extract(file.Text, file.Path, targetFramework: null, excludeGenerated));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                warnings.Add($"{side}:{file.Path}:source-parse-failed");
            }
        }

        IReadOnlyList<PublicApiSymbol> ordered = [.. symbols
            .OrderBy(symbol => symbol.DocumentationCommentId, StringComparer.Ordinal)
            .ThenBy(symbol => symbol.Path, StringComparer.Ordinal)
            .ThenBy(symbol => symbol.Line)
            .ThenBy(symbol => symbol.Column)];

        return new PublicApiSnapshot(
            Side: side,
            Ref: gitRef,
            Symbols: [.. ordered.Take(symbolLimit)],
            Truncated: ordered.Count > symbolLimit,
            Warnings: warnings);
    }

    private static PublicApiDiffSummary CreateSummary(PublicApiChangesSection changes)
    {
        return new PublicApiDiffSummary(
            TotalChanges: changes.TotalChanges,
            BreakingSourceChanges: changes.Items.Count(change => change.SourceCompatibility.Risk == "breaking"),
            BreakingBinaryChanges: changes.Items.Count(change => change.BinaryCompatibility.Risk == "breaking"),
            Additions: changes.Items.Count(change => change.Kind == "addition"),
            Removals: changes.Items.Count(change => change.Kind == "removal"),
            SignatureChanges: changes.Items.Count(change => change.Code == "public-signature-changed"));
    }

    private static IReadOnlyList<string> GetProjectPathPrefixes(IReadOnlyList<Project> projects)
    {
        return [.. projects
            .Select(project => project.FilePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => PathDisplay.FromCurrentDirectory(Path.GetDirectoryName(path!) ?? path!))
            .Select(path => path.TrimEnd('/') + "/")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)];
    }

    private static IReadOnlyList<GitSourceFile> FilterToProjectScope(
        IReadOnlyList<GitSourceFile> files,
        IReadOnlyList<string> projectPathPrefixes)
    {
        if (projectPathPrefixes.Count == 0)
        {
            return files;
        }

        return [.. files.Where(file => projectPathPrefixes.Any(prefix => file.Path.StartsWith(prefix, StringComparison.Ordinal)))];
    }
}

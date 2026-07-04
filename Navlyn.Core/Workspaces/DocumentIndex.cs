using Microsoft.CodeAnalysis;
using Navlyn.Paths;

namespace Navlyn.Workspaces;

internal sealed class DocumentIndex
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private readonly Dictionary<string, IReadOnlyList<DocumentIndexEntry>> byFullPath;
    private readonly Dictionary<string, IReadOnlyList<DocumentIndexEntry>> byRepositoryRelativePath;
    private readonly Dictionary<string, IReadOnlyList<DocumentIndexEntry>> byFileName;

    private DocumentIndex(
        IReadOnlyList<DocumentIndexEntry> entries,
        Dictionary<string, IReadOnlyList<DocumentIndexEntry>> byFullPath,
        Dictionary<string, IReadOnlyList<DocumentIndexEntry>> byRepositoryRelativePath,
        Dictionary<string, IReadOnlyList<DocumentIndexEntry>> byFileName,
        long estimatedMemoryBytes)
    {
        Entries = entries;
        this.byFullPath = byFullPath;
        this.byRepositoryRelativePath = byRepositoryRelativePath;
        this.byFileName = byFileName;
        EstimatedMemoryBytes = estimatedMemoryBytes;
    }

    public IReadOnlyList<DocumentIndexEntry> Entries { get; }

    public int DocumentCount => Entries.Count;

    public int UniquePathCount => byFullPath.Count;

    public int FileNameKeyCount => byFileName.Count;

    public long EstimatedMemoryBytes { get; }

    public static DocumentIndex Create(Solution solution)
    {
        string? repositoryRoot = GetRepositoryRoot(solution);
        IReadOnlyList<DocumentIndexEntry> entries = [.. solution.Projects
            .OrderBy(project => project.FilePath, StringComparer.Ordinal)
            .ThenBy(project => project.Name, StringComparer.Ordinal)
            .SelectMany(project => project.Documents
                .Where(document => document.FilePath is not null)
                .OrderBy(document => document.FilePath, StringComparer.Ordinal)
                .ThenBy(document => document.Name, StringComparer.Ordinal)
                .Select(document => CreateEntry(document, repositoryRoot)))];

        return new DocumentIndex(
            entries,
            CreateLookup(entries, entry => entry.FullPath, PathComparer),
            CreateLookup(entries.Where(entry => entry.RepositoryRelativePath is not null), entry => entry.RepositoryRelativePath!, PathComparer),
            CreateLookup(entries, entry => entry.FileName, StringComparer.OrdinalIgnoreCase),
            EstimateMemoryBytes(entries));
    }

    public DocumentIndexLookupResult Find(
        IReadOnlyList<string> sourcePaths,
        Project? project)
    {
        foreach (string sourcePath in sourcePaths)
        {
            string fullPath = Path.GetFullPath(sourcePath);
            if (TrySelect(byFullPath, fullPath, project, out DocumentIndexEntry? fullPathEntry))
            {
                return DocumentIndexLookupResult.Found(fullPathEntry!, fullPath);
            }

            string normalizedPath = NormalizeRelativeKey(sourcePath);
            if (TrySelect(byRepositoryRelativePath, normalizedPath, project, out DocumentIndexEntry? relativeEntry))
            {
                return DocumentIndexLookupResult.Found(relativeEntry!, sourcePath);
            }
        }

        return DocumentIndexLookupResult.NotFound(sourcePaths.Count == 0 ? null : sourcePaths[0]);
    }

    public bool Contains(IReadOnlyList<string> sourcePaths)
    {
        return Find(sourcePaths, project: null).Entry is not null;
    }

    public IReadOnlyList<DocumentIndexEntry> FindByFileName(string fileName)
    {
        return byFileName.TryGetValue(fileName, out IReadOnlyList<DocumentIndexEntry>? entries)
            ? entries
            : [];
    }

    private static DocumentIndexEntry CreateEntry(Document document, string? repositoryRoot)
    {
        string fullPath = Path.GetFullPath(document.FilePath!);
        string? repositoryRelativePath = repositoryRoot is null || IsOutsideRoot(fullPath, repositoryRoot)
            ? null
            : Path.GetRelativePath(repositoryRoot, fullPath).Replace('\\', '/');

        return new DocumentIndexEntry(
            Document: document,
            DocumentId: document.Id.Id.ToString(),
            ProjectId: document.Project.Id.Id.ToString(),
            ProjectName: document.Project.Name,
            ProjectFilePath: document.Project.FilePath,
            FullPath: fullPath,
            RepositoryRelativePath: repositoryRelativePath,
            DisplayPath: PathDisplay.FromCurrentDirectory(fullPath),
            FileName: Path.GetFileName(fullPath));
    }

    private static Dictionary<string, IReadOnlyList<DocumentIndexEntry>> CreateLookup(
        IEnumerable<DocumentIndexEntry> entries,
        Func<DocumentIndexEntry, string> getKey,
        StringComparer comparer)
    {
        return entries
            .GroupBy(getKey, comparer)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<DocumentIndexEntry>)[.. group
                    .OrderBy(entry => entry.FullPath, StringComparer.Ordinal)
                    .ThenBy(entry => entry.ProjectName, StringComparer.Ordinal)
                    .ThenBy(entry => entry.DocumentId, StringComparer.Ordinal)],
                comparer);
    }

    private static bool TrySelect(
        IReadOnlyDictionary<string, IReadOnlyList<DocumentIndexEntry>> lookup,
        string key,
        Project? project,
        out DocumentIndexEntry? entry)
    {
        entry = null;
        if (!lookup.TryGetValue(key, out IReadOnlyList<DocumentIndexEntry>? entries))
        {
            return false;
        }

        entry = project is null
            ? entries.FirstOrDefault()
            : entries.FirstOrDefault(candidate => candidate.Document.Project.Id == project.Id);
        return entry is not null;
    }

    private static string NormalizeRelativeKey(string path)
    {
        return path.Replace('\\', '/').TrimStart('/', '.');
    }

    private static string? GetRepositoryRoot(Solution solution)
    {
        string? anchor = solution.FilePath ?? solution.Projects
            .Select(project => project.FilePath)
            .FirstOrDefault(path => path is not null);
        return anchor is null ? null : PathDisplay.FindRepositoryRoot(anchor);
    }

    private static long EstimateMemoryBytes(IReadOnlyList<DocumentIndexEntry> entries)
    {
        return entries.Sum(entry =>
            128L +
            entry.FullPath.Length * sizeof(char) +
            entry.DisplayPath.Length * sizeof(char) +
            entry.FileName.Length * sizeof(char) +
            (entry.RepositoryRelativePath?.Length ?? 0) * sizeof(char) +
            entry.ProjectName.Length * sizeof(char) +
            (entry.ProjectFilePath?.Length ?? 0) * sizeof(char));
    }

    private static bool IsOutsideRoot(string path, string root)
    {
        string relativePath = Path.GetRelativePath(root, path);
        return relativePath == ".." ||
            relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal) ||
            Path.IsPathRooted(relativePath);
    }
}

internal sealed record DocumentIndexEntry(
    Document Document,
    string DocumentId,
    string ProjectId,
    string ProjectName,
    string? ProjectFilePath,
    string FullPath,
    string? RepositoryRelativePath,
    string DisplayPath,
    string FileName);

internal sealed record DocumentIndexLookupResult(DocumentIndexEntry? Entry, string? MatchedSourcePath)
{
    public static DocumentIndexLookupResult Found(DocumentIndexEntry entry, string matchedSourcePath)
    {
        return new DocumentIndexLookupResult(entry, matchedSourcePath);
    }

    public static DocumentIndexLookupResult NotFound(string? sourcePath)
    {
        return new DocumentIndexLookupResult(Entry: null, sourcePath);
    }
}

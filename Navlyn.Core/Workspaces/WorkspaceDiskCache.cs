using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Navlyn.Paths;
using Navlyn.Symbols;

namespace Navlyn.Workspaces;

internal static class WorkspaceDiskCache
{
    public const int SchemaVersion = 1;
    public const string DefaultCacheMode = "auto";

    private const string ManifestFileName = "workspace-index.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public static async Task<WorkspaceStatusResult> CreateStatusAsync(
        string command,
        LoadedWorkspace workspace,
        WorkspaceSnapshot? snapshot,
        FileInfo workspaceInput,
        WorkspaceDiskCacheRequest request,
        CancellationToken cancellationToken)
    {
        WorkspaceDiskCacheSettings settings = ResolveSettings(workspaceInput, workspace, request);
        WorkspaceDiskCacheStatus cacheStatus = await ResolveCacheStatusAsync(workspace, settings, request, cancellationToken);
        DocumentIndex documentIndex = workspace.DocumentIndex ?? DocumentIndexProvider.GetOrCreate(workspace.Solution);
        WorkspaceSnapshot effectiveSnapshot = snapshot ?? WorkspaceSnapshot.Create(workspace);

        return new WorkspaceStatusResult(
            Command: command,
            Workspace: new WorkspaceStatusWorkspace(
                Path: workspace.DisplayPath,
                FullPath: workspace.FullPath,
                Kind: workspace.Kind,
                ProjectCount: workspace.ProjectCount,
                DocumentCount: documentIndex.DocumentCount,
                Projects: [.. workspace.Projects.Select(project => new WorkspaceStatusProject(
                    project.Name,
                    project.Path,
                    project.Language,
                    project.AssemblyName,
                    project.TargetFramework))]),
            Snapshot: new WorkspaceStatusSnapshot(
                Fingerprint: effectiveSnapshot.Fingerprint,
                SnapshotId: effectiveSnapshot.SnapshotId,
                FreshnessStatus: effectiveSnapshot.FreshnessStatus,
                DocumentIndexDocumentCount: documentIndex.DocumentCount,
                DocumentIndexEstimatedBytes: documentIndex.EstimatedMemoryBytes),
            Cache: cacheStatus,
            Versions: WorkspaceStatusVersions.Create());
    }

    public static bool TryParseCacheMode(string value)
    {
        return value is "auto" or "on" or "off";
    }

    internal static WorkspaceDiskCacheSettings ResolveSettings(
        FileInfo workspaceInput,
        LoadedWorkspace workspace,
        WorkspaceDiskCacheRequest request)
    {
        string input = workspaceInput.ToString();
        string resolvedInput = string.Equals(input.Trim(), "auto", StringComparison.Ordinal)
            ? workspace.FullPath
            : Path.GetFullPath(input);
        NavlynWorkspaceCacheHints? hints = TryReadCacheHints(resolvedInput, out string? hintDirectory);
        string baseDirectory = hintDirectory ??
            PathDisplay.FindRepositoryRoot(workspace.FullPath) ??
            Path.GetDirectoryName(workspace.FullPath) ??
            Directory.GetCurrentDirectory();
        bool enabled = request.CacheMode switch
        {
            "on" => true,
            "off" => false,
            _ => hints?.Enabled == true
        };

        string directory = request.DirectoryOverride is not null
            ? Path.GetFullPath(request.DirectoryOverride)
            : ResolveCacheDirectory(baseDirectory, hints?.Directory);
        return new WorkspaceDiskCacheSettings(
            Enabled: enabled,
            Mode: request.CacheMode,
            Directory: directory,
            ManifestPath: Path.Combine(directory, ManifestFileName),
            Source: hints is null ? "default" : "navlyn.workspace.json",
            ConfigEnabled: hints?.Enabled);
    }

    private static async Task<WorkspaceDiskCacheStatus> ResolveCacheStatusAsync(
        LoadedWorkspace workspace,
        WorkspaceDiskCacheSettings settings,
        WorkspaceDiskCacheRequest request,
        CancellationToken cancellationToken)
    {
        if (!settings.Enabled)
        {
            return WorkspaceDiskCacheStatus.Disabled(settings);
        }

        bool cleared = false;
        if (request.Clear)
        {
            Clear(settings);
            cleared = true;
        }

        WorkspaceDiskCacheManifest currentManifest = await CreateManifestAsync(
            workspace,
            includeDeclarationIndex: request.Write,
            cancellationToken);

        if (request.Write)
        {
            Directory.CreateDirectory(settings.Directory);
            await using FileStream stream = File.Create(settings.ManifestPath);
            await JsonSerializer.SerializeAsync(stream, currentManifest, JsonOptions, cancellationToken);
            return WorkspaceDiskCacheStatus.Fresh(settings, currentManifest, cleared, writeRequested: true);
        }

        if (!File.Exists(settings.ManifestPath))
        {
            return WorkspaceDiskCacheStatus.Missing(settings, currentManifest.WorkspaceFingerprint, cleared);
        }

        WorkspaceDiskCacheManifest? cachedManifest;
        try
        {
            await using FileStream stream = File.OpenRead(settings.ManifestPath);
            cachedManifest = await JsonSerializer.DeserializeAsync<WorkspaceDiskCacheManifest>(stream, JsonOptions, cancellationToken);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return WorkspaceDiskCacheStatus.Invalid(settings, currentManifest.WorkspaceFingerprint, $"Failed to read cache manifest: {ex.Message}", cleared);
        }

        if (cachedManifest is null)
        {
            return WorkspaceDiskCacheStatus.Invalid(settings, currentManifest.WorkspaceFingerprint, "Cache manifest was empty.", cleared);
        }

        List<string> staleReasons = [];
        if (cachedManifest.SchemaVersion != SchemaVersion)
        {
            staleReasons.Add($"schemaVersion changed from {cachedManifest.SchemaVersion} to {SchemaVersion}");
        }

        if (!string.Equals(cachedManifest.WorkspacePath, currentManifest.WorkspacePath, PathComparison()))
        {
            staleReasons.Add("workspacePath changed");
        }

        if (!string.Equals(cachedManifest.WorkspaceFingerprint, currentManifest.WorkspaceFingerprint, StringComparison.Ordinal))
        {
            staleReasons.Add("tracked workspace files or version fingerprints changed");
        }

        return staleReasons.Count == 0
            ? WorkspaceDiskCacheStatus.Fresh(settings, cachedManifest, cleared, writeRequested: false)
            : WorkspaceDiskCacheStatus.Stale(settings, currentManifest.WorkspaceFingerprint, cachedManifest, staleReasons, cleared);
    }

    private static async Task<WorkspaceDiskCacheManifest> CreateManifestAsync(
        LoadedWorkspace workspace,
        bool includeDeclarationIndex,
        CancellationToken cancellationToken)
    {
        DocumentIndex documentIndex = workspace.DocumentIndex ?? DocumentIndexProvider.GetOrCreate(workspace.Solution);
        IReadOnlyList<WorkspaceDiskCacheTrackedFile> trackedFiles = await CreateTrackedFilesAsync(workspace, documentIndex, cancellationToken);
        WorkspaceStatusVersions versions = WorkspaceStatusVersions.Create();
        string workspaceFingerprint = CreateWorkspaceFingerprint(workspace, trackedFiles, versions);

        WorkspaceDiskCacheDeclarationIndex? declarationIndex = null;
        if (includeDeclarationIndex)
        {
            DeclarationIndex declarations = await DeclarationIndex.CreateAsync(workspace.Solution, cancellationToken);
            declarationIndex = new WorkspaceDiskCacheDeclarationIndex(
                Status: "written",
                EntryCount: declarations.EntryCount,
                SolutionFingerprint: declarations.SolutionFingerprint,
                Entries: [.. declarations.Entries.Select(entry => new WorkspaceDiskCacheDeclarationEntry(
                    entry.Name,
                    entry.SyntaxKind,
                    PathDisplay.FromCurrentDirectory(entry.FullPath),
                    entry.ProjectName,
                    entry.Line,
                    entry.Column))]);
        }

        return new WorkspaceDiskCacheManifest(
            SchemaVersion: SchemaVersion,
            CreatedUtc: DateTimeOffset.UtcNow,
            WorkspacePath: workspace.FullPath,
            WorkspaceKind: workspace.Kind,
            WorkspaceFingerprint: workspaceFingerprint,
            Versions: versions,
            Projects: [.. workspace.Projects.Select(project => new WorkspaceDiskCacheProjectFact(
                project.Name,
                project.Path,
                project.Language,
                project.AssemblyName,
                project.TargetFramework,
                project.PreprocessorSymbols))],
            DocumentIndex: new WorkspaceDiskCacheDocumentIndex(
                DocumentCount: documentIndex.DocumentCount,
                UniquePathCount: documentIndex.UniquePathCount,
                EstimatedMemoryBytes: documentIndex.EstimatedMemoryBytes,
                Documents: [.. documentIndex.Entries.Select(entry => new WorkspaceDiskCacheDocumentFact(
                    entry.DisplayPath,
                    entry.RepositoryRelativePath,
                    entry.ProjectName,
                    entry.DocumentId,
                    entry.ProjectId))]),
            DeclarationIndex: declarationIndex ?? new WorkspaceDiskCacheDeclarationIndex(
                Status: "not-written",
                EntryCount: null,
                SolutionFingerprint: null,
                Entries: []),
            CandidateRecords: new WorkspaceDiskCacheCandidateRecords(
                Stored: false,
                Reason: "Candidate ids are session-local and are not persisted until a stable cross-process candidate contract is available."),
            TrackedFiles: trackedFiles);
    }

    private static async Task<IReadOnlyList<WorkspaceDiskCacheTrackedFile>> CreateTrackedFilesAsync(
        LoadedWorkspace workspace,
        DocumentIndex documentIndex,
        CancellationToken cancellationToken)
    {
        List<string> paths = [];
        AddPath(paths, workspace.FullPath);
        AddPath(paths, workspace.Solution.FilePath);
        foreach (Project project in workspace.Solution.Projects)
        {
            AddPath(paths, project.FilePath);
        }

        foreach (DocumentIndexEntry entry in documentIndex.Entries)
        {
            AddPath(paths, entry.FullPath);
        }

        string? globalJson = FindGlobalJson(workspace.FullPath);
        AddPath(paths, globalJson);

        List<WorkspaceDiskCacheTrackedFile> trackedFiles = [];
        foreach (string path in paths.Distinct(PathComparer).OrderBy(path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(path))
            {
                continue;
            }

            FileInfo info = new(path);
            trackedFiles.Add(new WorkspaceDiskCacheTrackedFile(
                Path: PathDisplay.FromCurrentDirectory(path),
                FullPath: path,
                Length: info.Length,
                LastWriteTimeUtc: info.LastWriteTimeUtc,
                Sha256: await HashFileAsync(path, cancellationToken)));
        }

        return trackedFiles;
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string CreateWorkspaceFingerprint(
        LoadedWorkspace workspace,
        IReadOnlyList<WorkspaceDiskCacheTrackedFile> trackedFiles,
        WorkspaceStatusVersions versions)
    {
        string canonical = string.Join(
            "\n",
            [
                workspace.FullPath,
                workspace.Kind,
                versions.NavlynVersion,
                versions.RoslynVersion,
                versions.DotNetRuntimeVersion,
                .. trackedFiles.Select(file => string.Join(
                    "|",
                    [
                        file.FullPath,
                        file.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        file.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        file.Sha256
                    ]))
            ]);

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void Clear(WorkspaceDiskCacheSettings settings)
    {
        if (File.Exists(settings.ManifestPath))
        {
            File.Delete(settings.ManifestPath);
        }
    }

    private static NavlynWorkspaceCacheHints? TryReadCacheHints(string workspaceInput, out string? baseDirectory)
    {
        baseDirectory = null;
        if (!Path.GetFileName(workspaceInput).Equals("navlyn.workspace.json", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(workspaceInput))
        {
            return null;
        }

        try
        {
            using FileStream stream = File.OpenRead(workspaceInput);
            using JsonDocument document = JsonDocument.Parse(
                stream,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });

            if (!document.RootElement.TryGetProperty("cacheHints", out JsonElement cacheHints) ||
                cacheHints.ValueKind != JsonValueKind.Object)
            {
                return new NavlynWorkspaceCacheHints(Enabled: null, Directory: null);
            }

            bool? enabled = null;
            if (cacheHints.TryGetProperty("enabled", out JsonElement enabledElement) &&
                (enabledElement.ValueKind == JsonValueKind.True || enabledElement.ValueKind == JsonValueKind.False))
            {
                enabled = enabledElement.GetBoolean();
            }

            string? directory = null;
            if (cacheHints.TryGetProperty("directory", out JsonElement directoryElement) &&
                directoryElement.ValueKind == JsonValueKind.String)
            {
                directory = directoryElement.GetString();
            }

            baseDirectory = Path.GetDirectoryName(Path.GetFullPath(workspaceInput));
            return new NavlynWorkspaceCacheHints(enabled, directory);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ResolveCacheDirectory(string baseDirectory, string? configuredDirectory)
    {
        if (!string.IsNullOrWhiteSpace(configuredDirectory))
        {
            return Path.IsPathRooted(configuredDirectory)
                ? Path.GetFullPath(configuredDirectory)
                : Path.GetFullPath(Path.Combine(baseDirectory, configuredDirectory));
        }

        return Path.GetFullPath(Path.Combine(baseDirectory, ".navlyn", "cache"));
    }

    private static void AddPath(List<string> paths, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        paths.Add(Path.GetFullPath(path));
    }

    private static string? FindGlobalJson(string anchor)
    {
        DirectoryInfo? directory = new(Path.GetDirectoryName(Path.GetFullPath(anchor)) ?? Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "global.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static StringComparison PathComparison()
    {
        return OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }
}

internal sealed record WorkspaceDiskCacheRequest(
    string CacheMode,
    bool Write,
    bool Clear,
    string? DirectoryOverride)
{
    public static WorkspaceDiskCacheRequest Default { get; } = new(WorkspaceDiskCache.DefaultCacheMode, Write: false, Clear: false, DirectoryOverride: null);
}

internal sealed record WorkspaceDiskCacheSettings(
    bool Enabled,
    string Mode,
    string Directory,
    string ManifestPath,
    string Source,
    bool? ConfigEnabled);

internal sealed record WorkspaceStatusResult(
    string Command,
    WorkspaceStatusWorkspace Workspace,
    WorkspaceStatusSnapshot Snapshot,
    WorkspaceDiskCacheStatus Cache,
    WorkspaceStatusVersions Versions);

internal sealed record WorkspaceStatusWorkspace(
    string Path,
    string FullPath,
    string Kind,
    int ProjectCount,
    int DocumentCount,
    IReadOnlyList<WorkspaceStatusProject> Projects);

internal sealed record WorkspaceStatusProject(
    string Name,
    string? Path,
    string Language,
    string? AssemblyName,
    string? TargetFramework);

internal sealed record WorkspaceStatusSnapshot(
    string Fingerprint,
    string SnapshotId,
    string FreshnessStatus,
    int DocumentIndexDocumentCount,
    long DocumentIndexEstimatedBytes);

internal sealed record WorkspaceStatusVersions(
    string NavlynVersion,
    string RoslynVersion,
    string DotNetRuntimeVersion)
{
    public static WorkspaceStatusVersions Create()
    {
        return new WorkspaceStatusVersions(
            NavlynVersion: GetVersion(typeof(WorkspaceLoader).Assembly),
            RoslynVersion: GetVersion(typeof(Workspace).Assembly),
            DotNetRuntimeVersion: Environment.Version.ToString());
    }

    private static string GetVersion(Assembly assembly)
    {
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
            assembly.GetName().Version?.ToString() ??
            "unknown";
    }
}

internal sealed record WorkspaceDiskCacheStatus(
    bool Enabled,
    string Mode,
    string Directory,
    string ManifestPath,
    string Status,
    string Source,
    bool? ConfigEnabled,
    string? CurrentFingerprint,
    string? CachedFingerprint,
    string? CreatedUtc,
    bool WriteRequested,
    bool ClearRequested,
    IReadOnlyList<string> StaleReasons,
    bool CandidateRecordsStored,
    string CandidateRecordsReason,
    int? CachedProjectCount,
    int? CachedDocumentCount,
    int? CachedDeclarationCount)
{
    public static WorkspaceDiskCacheStatus Disabled(WorkspaceDiskCacheSettings settings)
    {
        return Create(settings, "disabled", null, null, null, writeRequested: false, clearRequested: false, [], null);
    }

    public static WorkspaceDiskCacheStatus Missing(
        WorkspaceDiskCacheSettings settings,
        string currentFingerprint,
        bool clearRequested)
    {
        return Create(settings, "missing", currentFingerprint, null, null, writeRequested: false, clearRequested, [], null);
    }

    public static WorkspaceDiskCacheStatus Invalid(
        WorkspaceDiskCacheSettings settings,
        string currentFingerprint,
        string reason,
        bool clearRequested)
    {
        return Create(settings, "invalid", currentFingerprint, null, null, writeRequested: false, clearRequested, [reason], null);
    }

    public static WorkspaceDiskCacheStatus Fresh(
        WorkspaceDiskCacheSettings settings,
        WorkspaceDiskCacheManifest manifest,
        bool clearRequested,
        bool writeRequested)
    {
        return Create(
            settings,
            "fresh",
            manifest.WorkspaceFingerprint,
            manifest.WorkspaceFingerprint,
            manifest,
            writeRequested,
            clearRequested,
            [],
            manifest);
    }

    public static WorkspaceDiskCacheStatus Stale(
        WorkspaceDiskCacheSettings settings,
        string currentFingerprint,
        WorkspaceDiskCacheManifest cachedManifest,
        IReadOnlyList<string> staleReasons,
        bool clearRequested)
    {
        return Create(
            settings,
            "stale",
            currentFingerprint,
            cachedManifest.WorkspaceFingerprint,
            cachedManifest,
            writeRequested: false,
            clearRequested,
            staleReasons,
            cachedManifest);
    }

    private static WorkspaceDiskCacheStatus Create(
        WorkspaceDiskCacheSettings settings,
        string status,
        string? currentFingerprint,
        string? cachedFingerprint,
        WorkspaceDiskCacheManifest? createdManifest,
        bool writeRequested,
        bool clearRequested,
        IReadOnlyList<string> staleReasons,
        WorkspaceDiskCacheManifest? cachedManifest)
    {
        return new WorkspaceDiskCacheStatus(
            Enabled: settings.Enabled,
            Mode: settings.Mode,
            Directory: settings.Directory,
            ManifestPath: settings.ManifestPath,
            Status: status,
            Source: settings.Source,
            ConfigEnabled: settings.ConfigEnabled,
            CurrentFingerprint: currentFingerprint,
            CachedFingerprint: cachedFingerprint,
            CreatedUtc: createdManifest?.CreatedUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            WriteRequested: writeRequested,
            ClearRequested: clearRequested,
            StaleReasons: staleReasons,
            CandidateRecordsStored: cachedManifest?.CandidateRecords.Stored ?? false,
            CandidateRecordsReason: cachedManifest?.CandidateRecords.Reason ??
                "Candidate ids are session-local and are not persisted until a stable cross-process candidate contract is available.",
            CachedProjectCount: cachedManifest?.Projects.Count,
            CachedDocumentCount: cachedManifest?.DocumentIndex.DocumentCount,
            CachedDeclarationCount: cachedManifest?.DeclarationIndex.EntryCount);
    }
}

internal sealed record WorkspaceDiskCacheManifest(
    int SchemaVersion,
    DateTimeOffset CreatedUtc,
    string WorkspacePath,
    string WorkspaceKind,
    string WorkspaceFingerprint,
    WorkspaceStatusVersions Versions,
    IReadOnlyList<WorkspaceDiskCacheProjectFact> Projects,
    WorkspaceDiskCacheDocumentIndex DocumentIndex,
    WorkspaceDiskCacheDeclarationIndex DeclarationIndex,
    WorkspaceDiskCacheCandidateRecords CandidateRecords,
    IReadOnlyList<WorkspaceDiskCacheTrackedFile> TrackedFiles);

internal sealed record WorkspaceDiskCacheProjectFact(
    string Name,
    string? Path,
    string Language,
    string? AssemblyName,
    string? TargetFramework,
    IReadOnlyList<string> PreprocessorSymbols);

internal sealed record WorkspaceDiskCacheDocumentIndex(
    int DocumentCount,
    int UniquePathCount,
    long EstimatedMemoryBytes,
    IReadOnlyList<WorkspaceDiskCacheDocumentFact> Documents);

internal sealed record WorkspaceDiskCacheDocumentFact(
    string Path,
    string? RepositoryRelativePath,
    string ProjectName,
    string DocumentId,
    string ProjectId);

internal sealed record WorkspaceDiskCacheDeclarationIndex(
    string Status,
    int? EntryCount,
    string? SolutionFingerprint,
    IReadOnlyList<WorkspaceDiskCacheDeclarationEntry> Entries);

internal sealed record WorkspaceDiskCacheDeclarationEntry(
    string Name,
    string SyntaxKind,
    string Path,
    string ProjectName,
    int Line,
    int Column);

internal sealed record WorkspaceDiskCacheCandidateRecords(bool Stored, string Reason);

internal sealed record WorkspaceDiskCacheTrackedFile(
    string Path,
    string FullPath,
    long Length,
    DateTime LastWriteTimeUtc,
    string Sha256);

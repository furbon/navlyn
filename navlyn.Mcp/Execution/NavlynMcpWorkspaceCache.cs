using Microsoft.CodeAnalysis;
using Navlyn.Mcp.Configuration;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.Mcp.Execution;

internal sealed class NavlynMcpWorkspaceCache(NavlynMcpServerOptions options) : IDisposable
{
    private readonly SemaphoreSlim loadLock = new(1, 1);
    private readonly WorkspaceSnapshotManager workspaceManager = new();
    private CachedWorkspace? cachedWorkspace;

    public async Task<NavlynMcpWorkspaceCacheResult> GetAsync(CancellationToken cancellationToken)
    {
        if (cachedWorkspace is not null)
        {
            return NavlynMcpWorkspaceCacheResult.Succeeded(cachedWorkspace, cacheHit: true);
        }

        await loadLock.WaitAsync(cancellationToken);
        try
        {
            if (cachedWorkspace is not null)
            {
                return NavlynMcpWorkspaceCacheResult.Succeeded(cachedWorkspace, cacheHit: true);
            }

            WorkspaceSnapshotManagerResult loadResult = await workspaceManager.GetAsync(
                new FileInfo(options.Workspace),
                new WorkspaceLoadOptions(options.WorkspaceRootPolicy),
                cancellationToken);
            if (loadResult.Error is not null)
            {
                return NavlynMcpWorkspaceCacheResult.Failed(loadResult.Error, loadResult.Diagnostics);
            }

            cachedWorkspace = new CachedWorkspace(loadResult.Snapshot!);
            return NavlynMcpWorkspaceCacheResult.Succeeded(cachedWorkspace, loadResult.CacheHit);
        }
        finally
        {
            loadLock.Release();
        }
    }

    public async Task<NavlynMcpWorkspaceCacheResult> RefreshAsync(CancellationToken cancellationToken)
    {
        await loadLock.WaitAsync(cancellationToken);
        try
        {
            cachedWorkspace = null;

            WorkspaceSnapshotManagerResult loadResult = await workspaceManager.RefreshAsync(
                new FileInfo(options.Workspace),
                new WorkspaceLoadOptions(options.WorkspaceRootPolicy),
                cancellationToken);
            if (loadResult.Error is not null)
            {
                return NavlynMcpWorkspaceCacheResult.Failed(loadResult.Error, loadResult.Diagnostics);
            }

            cachedWorkspace = new CachedWorkspace(loadResult.Snapshot!);
            return NavlynMcpWorkspaceCacheResult.Succeeded(cachedWorkspace, loadResult.CacheHit);
        }
        finally
        {
            loadLock.Release();
        }
    }

    public void Dispose()
    {
        workspaceManager.Dispose();
        loadLock.Dispose();
    }

    internal sealed class CachedWorkspace(WorkspaceSnapshot snapshot)
    {
        private readonly object candidateGate = new();
        private readonly Dictionary<string, NavlynMcpCandidateTarget> candidateTargets = new(StringComparer.Ordinal);

        public LoadedWorkspace Workspace => snapshot.Workspace;

        public string Fingerprint => snapshot.Fingerprint;

        public string SnapshotId => snapshot.SnapshotId;

        public string FreshnessStatus => snapshot.FreshnessStatus;

        public DocumentIndex DocumentIndex => snapshot.DocumentIndex;

        public void RecordCandidateTarget(OutlineEntry entry)
        {
            lock (candidateGate)
            {
                candidateTargets[entry.CandidateId] = new NavlynMcpCandidateTarget(
                    entry.CandidateId,
                    entry.CandidatePath,
                    entry.CandidateLine,
                    entry.CandidateColumn,
                    entry.Facts.Project);
            }
        }

        public void RecordCandidateTarget(NavlynMcpCandidateTarget target)
        {
            lock (candidateGate)
            {
                candidateTargets[target.CandidateId] = target;
            }
        }

        public bool TryGetCandidateTarget(string candidateId, out NavlynMcpCandidateTarget target)
        {
            lock (candidateGate)
            {
                return candidateTargets.TryGetValue(candidateId, out target!);
            }
        }

        public Project? FindProject(string? projectName)
        {
            return string.IsNullOrWhiteSpace(projectName)
                ? null
                : Workspace.Solution.Projects
                    .OrderBy(project => project.FilePath, StringComparer.Ordinal)
                    .ThenBy(project => project.Name, StringComparer.Ordinal)
                    .FirstOrDefault(project => string.Equals(project.Name, projectName, StringComparison.Ordinal));
        }
    }
}

internal sealed record NavlynMcpWorkspaceCacheResult(
    NavlynMcpWorkspaceCache.CachedWorkspace? CachedWorkspace,
    bool CacheHit,
    WorkspaceLoadError? Error,
    IReadOnlyList<WorkspaceLoadDiagnostic> Diagnostics)
{
    public static NavlynMcpWorkspaceCacheResult Succeeded(
        NavlynMcpWorkspaceCache.CachedWorkspace cachedWorkspace,
        bool cacheHit)
    {
        return new NavlynMcpWorkspaceCacheResult(cachedWorkspace, cacheHit, Error: null, Diagnostics: []);
    }

    public static NavlynMcpWorkspaceCacheResult Failed(
        WorkspaceLoadError error,
        IReadOnlyList<WorkspaceLoadDiagnostic> diagnostics)
    {
        return new NavlynMcpWorkspaceCacheResult(CachedWorkspace: null, CacheHit: false, error, diagnostics);
    }
}

internal sealed record NavlynMcpCandidateTarget(
    string CandidateId,
    string Path,
    int Line,
    int Column,
    string? ProjectName);

using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Navlyn.Mcp.Configuration;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.Mcp.Execution;

internal sealed class NavlynMcpWorkspaceCache(NavlynMcpServerOptions options) : IDisposable
{
    private readonly SemaphoreSlim loadLock = new(1, 1);
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

            WorkspaceLoadResult loadResult = await new WorkspaceLoader().LoadAsync(new FileInfo(options.Workspace), cancellationToken);
            if (loadResult.Error is not null)
            {
                return NavlynMcpWorkspaceCacheResult.Failed(loadResult.Error, loadResult.Diagnostics);
            }

            cachedWorkspace = new CachedWorkspace(loadResult.Workspace!, CreateFingerprint(loadResult.Workspace!));
            return NavlynMcpWorkspaceCacheResult.Succeeded(cachedWorkspace, cacheHit: false);
        }
        finally
        {
            loadLock.Release();
        }
    }

    public void Dispose()
    {
        cachedWorkspace?.Workspace.Dispose();
        loadLock.Dispose();
    }

    private static string CreateFingerprint(LoadedWorkspace workspace)
    {
        string canonical = string.Join(
            "\n",
            [
                workspace.FullPath,
                workspace.Kind,
                .. workspace.Projects.Select(project => string.Join(
                    "|",
                    [
                        project.Name,
                        project.Path ?? "",
                        project.TargetFramework ?? "",
                        project.AssemblyName ?? ""
                    ]))
            ]);

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash).ToLowerInvariant()[..16];
    }

    internal sealed class CachedWorkspace(LoadedWorkspace workspace, string fingerprint)
    {
        private readonly object candidateGate = new();
        private readonly Dictionary<string, NavlynMcpCandidateTarget> candidateTargets = new(StringComparer.Ordinal);

        public LoadedWorkspace Workspace { get; } = workspace;

        public string Fingerprint { get; } = fingerprint;

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

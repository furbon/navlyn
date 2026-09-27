using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Navlyn.Mcp.Configuration;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.Mcp.Execution;

internal sealed class NavlynMcpWorkspaceCache(NavlynMcpServerOptions options) : IDisposable
{
    private const int MaximumLoadAttempts = 3;
    private readonly Action? beforePublication;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly WorkspaceLoader loader = new();
    private CachedWorkspace? active;
    private long generation;
    private bool disposed;

    internal NavlynMcpWorkspaceCache(NavlynMcpServerOptions options, Action beforePublication)
        : this(options)
    {
        this.beforePublication = beforePublication;
    }

    public async Task<NavlynMcpWorkspaceCacheResult> GetAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            WorkspaceInputSpec spec = active?.InputSpec ?? WorkspaceInputSpec.Create(options, null);
            if (active is not null)
            {
                WorkspaceInputState current = Capture(spec, cancellationToken);
                if (!IsInspectable(current))
                {
                    RetireActive();
                    throw new WorkspaceInputStaleException();
                }

                if (current.IsComplete && current.Digest == active.InputState.Digest)
                {
                    return NavlynMcpWorkspaceCacheResult.Succeeded(Lease(active, true), true);
                }

                RetireActive();
            }

            return await LoadStableAsync(spec, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<NavlynMcpWorkspaceCacheResult> RefreshAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            WorkspaceInputSpec spec = active?.InputSpec ?? WorkspaceInputSpec.Create(options, null);
            RetireActive();
            return await LoadStableAsync(spec, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task InvalidateAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            RetireActive();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<bool> ValidateAsync(WorkspaceLease lease, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (active != lease.CachedWorkspace || generation != lease.Generation)
            {
                return false;
            }

            WorkspaceInputState current = Capture(lease.CachedWorkspace.InputSpec, cancellationToken);
            if (!IsInspectable(current))
            {
                RetireActive();
                throw new WorkspaceInputStaleException();
            }

            if (!current.IsComplete || current.Digest != lease.CachedWorkspace.InputState.Digest)
            {
                RetireActive();
                return false;
            }

            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose()
    {
        gate.Wait();
        try
        {
            if (!disposed)
            {
                disposed = true;
                RetireActive();
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<NavlynMcpWorkspaceCacheResult> LoadStableAsync(WorkspaceInputSpec spec, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < MaximumLoadAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WorkspaceInputState before = Capture(spec, cancellationToken);
            if (!IsInspectable(before))
            {
                throw new WorkspaceInputStaleException();
            }

            WorkspaceLoadResult loaded = await loader.LoadAsync(
                new FileInfo(options.Workspace),
                new WorkspaceLoadOptions(options.WorkspaceRootPolicy),
                cancellationToken);
            if (loaded.Error is not null)
            {
                return NavlynMcpWorkspaceCacheResult.Failed(loaded.Error, loaded.Diagnostics);
            }

            LoadedWorkspace workspace = loaded.Workspace!;
            bool published = false;
            try
            {
                WorkspaceInputState after = Capture(spec, cancellationToken);
                WorkspaceInputSpec nextSpec = WorkspaceInputSpec.Create(options, workspace);
                WorkspaceInputState nextState = Capture(nextSpec, cancellationToken);
                if (!IsInspectable(after) || !IsInspectable(nextState))
                {
                    throw new WorkspaceInputStaleException();
                }

                if (before.Digest == after.Digest && after.Digest == nextState.Digest && nextState.IsComplete)
                {
                    beforePublication?.Invoke();
                    cancellationToken.ThrowIfCancellationRequested();
                    WorkspaceSnapshot baseSnapshot = WorkspaceSnapshot.Create(workspace);
                    WorkspaceSnapshot snapshot = baseSnapshot with
                    {
                        SnapshotId = CreateSnapshotId(baseSnapshot.Fingerprint, nextState.Digest)
                    };
                    WorkspaceInputState publicationState = Capture(nextSpec, cancellationToken);
                    if (!IsInspectable(publicationState))
                    {
                        throw new WorkspaceInputStaleException();
                    }

                    if (!publicationState.IsComplete || publicationState.Digest != nextState.Digest)
                    {
                        spec = nextSpec;
                        continue;
                    }

                    CachedWorkspace cached = new(snapshot, nextState, nextSpec, generation);
                    active = cached;
                    published = true;
                    return NavlynMcpWorkspaceCacheResult.Succeeded(Lease(cached, false), false);
                }

                spec = nextSpec;
            }
            finally
            {
                if (!published)
                {
                    workspace.Dispose();
                }
            }
        }

        throw new WorkspaceInputStaleException();
    }

    private WorkspaceLease Lease(CachedWorkspace workspace, bool cacheHit)
    {
        workspace.LeaseCount++;
        return new WorkspaceLease(this, workspace, workspace.Generation, cacheHit);
    }

    private async ValueTask ReleaseAsync(CachedWorkspace workspace)
    {
        await gate.WaitAsync();
        try
        {
            workspace.LeaseCount--;
            if (workspace.Retired && workspace.LeaseCount == 0)
            {
                workspace.Workspace.Dispose();
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private void RetireActive()
    {
        generation++;
        CachedWorkspace? previous = active;
        active = null;
        if (previous is null)
        {
            return;
        }

        previous.Retired = true;
        if (previous.LeaseCount == 0)
        {
            previous.Workspace.Dispose();
        }
    }

    private static WorkspaceInputState Capture(WorkspaceInputSpec spec, CancellationToken cancellationToken)
    {
        return WorkspaceInputState.Capture(
            spec.Root, spec.SelectedInputs, spec.LoadedInputs, spec.AdditionalRoots, cancellationToken,
            spec.ProjectDirectories);
    }

    private static bool IsInspectable(WorkspaceInputState state)
    {
        int fileErrors = state.Files.Count(file => file.Error is not null);
        return state.Errors.Count == fileErrors && state.Files.All(file => file.Error is null or "missing");
    }

    private static string CreateSnapshotId(string fingerprint, string digest)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint + "\0" + digest));
        return Convert.ToHexString(hash).ToLowerInvariant()[..16];
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    internal sealed class WorkspaceLease(
        NavlynMcpWorkspaceCache owner,
        CachedWorkspace cachedWorkspace,
        long generation,
        bool cacheHit) : IAsyncDisposable
    {
        private int released;

        public CachedWorkspace CachedWorkspace { get; } = cachedWorkspace;
        public long Generation { get; } = generation;
        public bool CacheHit { get; } = cacheHit;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
            {
                await owner.ReleaseAsync(CachedWorkspace);
            }
        }
    }

    internal sealed class CachedWorkspace(
        WorkspaceSnapshot snapshot,
        WorkspaceInputState inputState,
        WorkspaceInputSpec inputSpec,
        long generation)
    {
        private readonly object candidateGate = new();
        private readonly Dictionary<string, NavlynMcpCandidateTarget> candidateTargets = new(StringComparer.Ordinal);

        public LoadedWorkspace Workspace => snapshot.Workspace;
        public string Fingerprint => snapshot.Fingerprint;
        public string SnapshotId => snapshot.SnapshotId;
        public string FreshnessStatus => snapshot.FreshnessStatus;
        public DocumentIndex DocumentIndex => snapshot.DocumentIndex;
        public WorkspaceInputState InputState { get; } = inputState;
        public WorkspaceInputSpec InputSpec { get; } = inputSpec;
        public long Generation { get; } = generation;
        public int LeaseCount { get; set; }
        public bool Retired { get; set; }

        public void RecordCandidateTarget(OutlineEntry entry)
        {
            lock (candidateGate)
            {
                candidateTargets[entry.CandidateId] = new NavlynMcpCandidateTarget(
                    entry.CandidateId, entry.CandidatePath, entry.CandidateLine, entry.CandidateColumn, entry.Facts.Project);
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

    internal sealed record WorkspaceInputSpec(
        string Root,
        IReadOnlyList<string> SelectedInputs,
        IReadOnlyList<string> LoadedInputs,
        IReadOnlyList<string> AdditionalRoots,
        IReadOnlyList<string> ProjectDirectories)
    {
        public static WorkspaceInputSpec Create(NavlynMcpServerOptions options, LoadedWorkspace? workspace)
        {
            bool isAuto = string.Equals(options.Workspace, "auto", StringComparison.Ordinal);
            string root = isAuto ? options.WorkingDirectory : Path.GetDirectoryName(Path.GetFullPath(options.Workspace))!;
            List<string> selected = [];
            if (!isAuto)
            {
                selected.Add(options.Workspace);
            }

            if (workspace is null)
            {
                return new WorkspaceInputSpec(root, selected, [], [], []);
            }

            selected.Add(workspace.FullPath);
            List<string> loaded = [];
            List<string> roots = [];
            List<string> projectDirectories = [];
            if (isAuto) roots.Add(root);
            Add(loaded, workspace.Solution.FilePath);
            foreach (Project project in workspace.Solution.Projects)
            {
                Add(loaded, project.FilePath);
                if (project.FilePath is not null)
                {
                    string projectDirectory = Path.GetDirectoryName(project.FilePath)!;
                    roots.Add(projectDirectory);
                    projectDirectories.Add(projectDirectory);
                }

                foreach (Document document in project.Documents)
                {
                    Add(loaded, document.FilePath);
                    if (document.FilePath is not null)
                    {
                        roots.Add(Path.GetDirectoryName(document.FilePath)!);
                    }
                }

                foreach (TextDocument document in project.AdditionalDocuments.Concat(project.AnalyzerConfigDocuments))
                {
                    Add(loaded, document.FilePath);
                }

                foreach (MetadataReference reference in project.MetadataReferences)
                {
                    if (reference is PortableExecutableReference portableReference)
                    {
                        Add(loaded, portableReference.FilePath);
                    }
                }

                foreach (AnalyzerReference reference in project.AnalyzerReferences)
                {
                    Add(loaded, reference.FullPath);
                }
            }

            return new WorkspaceInputSpec(root, selected, loaded, roots, projectDirectories);
        }

        private static void Add(List<string> paths, string? path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                paths.Add(path);
            }
        }
    }
}

internal sealed record NavlynMcpWorkspaceCacheResult(
    NavlynMcpWorkspaceCache.WorkspaceLease? Lease,
    bool CacheHit,
    WorkspaceLoadError? Error,
    IReadOnlyList<WorkspaceLoadDiagnostic> Diagnostics)
{
    public static NavlynMcpWorkspaceCacheResult Succeeded(NavlynMcpWorkspaceCache.WorkspaceLease lease, bool cacheHit)
        => new(lease, cacheHit, Error: null, Diagnostics: []);

    public static NavlynMcpWorkspaceCacheResult Failed(WorkspaceLoadError error, IReadOnlyList<WorkspaceLoadDiagnostic> diagnostics)
        => new(Lease: null, CacheHit: false, error, diagnostics);
}

internal sealed class WorkspaceInputStaleException : Exception
{
    public WorkspaceInputStaleException()
        : base("Workspace inputs changed or could not be inspected. Wait for edits to finish, then retry the call.")
    {
    }
}

internal sealed record NavlynMcpCandidateTarget(string CandidateId, string Path, int Line, int Column, string? ProjectName);

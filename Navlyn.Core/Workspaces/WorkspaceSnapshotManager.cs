using System.Security.Cryptography;
using System.Text;

namespace Navlyn.Workspaces;

internal sealed class WorkspaceSnapshotManager : IDisposable
{
    private readonly int capacity;
    private readonly Func<FileInfo, WorkspaceLoadOptions, CancellationToken, Task<WorkspaceLoadResult>> loadAsync;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<WorkspaceSnapshotKey, LinkedListNode<WorkspaceSnapshotCacheEntry>> entries = [];
    private readonly LinkedList<WorkspaceSnapshotCacheEntry> lru = [];

    public WorkspaceSnapshotManager(int capacity = 4)
        : this((workspace, options, cancellationToken) => new WorkspaceLoader().LoadAsync(workspace, options, cancellationToken), capacity)
    {
    }

    internal WorkspaceSnapshotManager(
        Func<FileInfo, WorkspaceLoadOptions, CancellationToken, Task<WorkspaceLoadResult>> loadAsync,
        int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be 1 or greater.");
        }

        this.loadAsync = loadAsync;
        this.capacity = capacity;
    }

    public async Task<WorkspaceSnapshotManagerResult> GetAsync(
        FileInfo workspace,
        WorkspaceLoadOptions options,
        CancellationToken cancellationToken)
    {
        WorkspaceSnapshotKey key = WorkspaceSnapshotKey.Create(workspace, options);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (entries.TryGetValue(key, out LinkedListNode<WorkspaceSnapshotCacheEntry>? node))
            {
                lru.Remove(node);
                lru.AddFirst(node);
                return WorkspaceSnapshotManagerResult.Succeeded(node.Value.Snapshot, cacheHit: true);
            }
        }
        finally
        {
            gate.Release();
        }

        WorkspaceLoadResult loadResult = await loadAsync(workspace, options, cancellationToken);
        if (loadResult.Error is not null)
        {
            return WorkspaceSnapshotManagerResult.Failed(loadResult.Error, loadResult.Diagnostics);
        }

        WorkspaceSnapshot snapshot = WorkspaceSnapshot.Create(loadResult.Workspace!);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (entries.TryGetValue(key, out LinkedListNode<WorkspaceSnapshotCacheEntry>? existingNode))
            {
                snapshot.Workspace.Dispose();
                lru.Remove(existingNode);
                lru.AddFirst(existingNode);
                return WorkspaceSnapshotManagerResult.Succeeded(existingNode.Value.Snapshot, cacheHit: true);
            }

            AddEntry(key, snapshot);
            return WorkspaceSnapshotManagerResult.Succeeded(snapshot, cacheHit: false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<WorkspaceSnapshotManagerResult> RefreshAsync(
        FileInfo workspace,
        WorkspaceLoadOptions options,
        CancellationToken cancellationToken)
    {
        WorkspaceSnapshotKey key = WorkspaceSnapshotKey.Create(workspace, options);
        await gate.WaitAsync(cancellationToken);
        try
        {
            RemoveEntry(key);
        }
        finally
        {
            gate.Release();
        }

        return await GetAsync(workspace, options, cancellationToken);
    }

    public void Dispose()
    {
        foreach (WorkspaceSnapshotCacheEntry entry in lru)
        {
            entry.Snapshot.Workspace.Dispose();
        }

        entries.Clear();
        lru.Clear();
        gate.Dispose();
    }

    private void AddEntry(WorkspaceSnapshotKey key, WorkspaceSnapshot snapshot)
    {
        LinkedListNode<WorkspaceSnapshotCacheEntry> node = new(new WorkspaceSnapshotCacheEntry(key, snapshot));
        lru.AddFirst(node);
        entries[key] = node;

        while (entries.Count > capacity)
        {
            LinkedListNode<WorkspaceSnapshotCacheEntry> last = lru.Last!;
            lru.RemoveLast();
            entries.Remove(last.Value.Key);
            last.Value.Snapshot.Workspace.Dispose();
        }
    }

    private void RemoveEntry(WorkspaceSnapshotKey key)
    {
        if (!entries.Remove(key, out LinkedListNode<WorkspaceSnapshotCacheEntry>? node))
        {
            return;
        }

        lru.Remove(node);
        node.Value.Snapshot.Workspace.Dispose();
    }
}

internal sealed record WorkspaceSnapshot(
    LoadedWorkspace Workspace,
    string Fingerprint,
    string SnapshotId,
    string FreshnessStatus,
    DocumentIndex DocumentIndex)
{
    public static WorkspaceSnapshot Create(LoadedWorkspace workspace)
    {
        string fingerprint = CreateFingerprint(workspace);
        return new WorkspaceSnapshot(
            workspace,
            fingerprint,
            SnapshotId: fingerprint,
            FreshnessStatus: "fresh",
            DocumentIndex: workspace.DocumentIndex ?? DocumentIndexProvider.GetOrCreate(workspace.Solution));
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
}

internal sealed record WorkspaceSnapshotKey(string WorkspacePath, WorkspaceRootPolicy? RootPolicyOverride)
{
    public static WorkspaceSnapshotKey Create(FileInfo workspace, WorkspaceLoadOptions options)
    {
        string input = workspace.ToString();
        string path = string.Equals(input.Trim(), "auto", StringComparison.Ordinal)
            ? input.Trim()
            : Path.GetFullPath(input);
        return new WorkspaceSnapshotKey(path, options.RootPolicyOverride);
    }
}

internal sealed record WorkspaceSnapshotManagerResult(
    WorkspaceSnapshot? Snapshot,
    bool CacheHit,
    WorkspaceLoadError? Error,
    IReadOnlyList<WorkspaceLoadDiagnostic> Diagnostics)
{
    public static WorkspaceSnapshotManagerResult Succeeded(WorkspaceSnapshot snapshot, bool cacheHit)
    {
        return new WorkspaceSnapshotManagerResult(snapshot, cacheHit, Error: null, Diagnostics: []);
    }

    public static WorkspaceSnapshotManagerResult Failed(
        WorkspaceLoadError error,
        IReadOnlyList<WorkspaceLoadDiagnostic> diagnostics)
    {
        return new WorkspaceSnapshotManagerResult(Snapshot: null, CacheHit: false, error, diagnostics);
    }
}

internal sealed record WorkspaceSnapshotCacheEntry(WorkspaceSnapshotKey Key, WorkspaceSnapshot Snapshot);

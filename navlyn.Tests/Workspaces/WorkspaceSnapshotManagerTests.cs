using Microsoft.CodeAnalysis.MSBuild;
using Navlyn.Workspaces;

namespace Navlyn.Tests.Workspaces;

public sealed class WorkspaceSnapshotManagerTests
{
    [Fact]
    public async Task GetAsync_ReusesSnapshotForSameWorkspaceKey()
    {
        Dictionary<string, int> loads = new(StringComparer.OrdinalIgnoreCase);
        using WorkspaceSnapshotManager manager = new(CreateFakeLoader(loads), capacity: 2);
        FileInfo workspace = new(Path.Combine(Path.GetTempPath(), "navlyn-manager-a.slnx"));

        WorkspaceSnapshotManagerResult first = await manager.GetAsync(workspace, WorkspaceLoadOptions.Default, CancellationToken.None);
        WorkspaceSnapshotManagerResult second = await manager.GetAsync(workspace, WorkspaceLoadOptions.Default, CancellationToken.None);

        Assert.Null(first.Error);
        Assert.Null(second.Error);
        Assert.False(first.CacheHit);
        Assert.True(second.CacheHit);
        Assert.Same(first.Snapshot, second.Snapshot);
        Assert.Equal(1, loads[Path.GetFullPath(workspace.ToString())]);
    }

    [Fact]
    public async Task GetAsync_EvictsLeastRecentlyUsedSnapshotWhenCapacityIsExceeded()
    {
        Dictionary<string, int> loads = new(StringComparer.OrdinalIgnoreCase);
        using WorkspaceSnapshotManager manager = new(CreateFakeLoader(loads), capacity: 1);
        FileInfo firstWorkspace = new(Path.Combine(Path.GetTempPath(), "navlyn-manager-a.slnx"));
        FileInfo secondWorkspace = new(Path.Combine(Path.GetTempPath(), "navlyn-manager-b.slnx"));

        WorkspaceSnapshotManagerResult first = await manager.GetAsync(firstWorkspace, WorkspaceLoadOptions.Default, CancellationToken.None);
        WorkspaceSnapshotManagerResult second = await manager.GetAsync(secondWorkspace, WorkspaceLoadOptions.Default, CancellationToken.None);
        WorkspaceSnapshotManagerResult firstAgain = await manager.GetAsync(firstWorkspace, WorkspaceLoadOptions.Default, CancellationToken.None);

        Assert.False(first.CacheHit);
        Assert.False(second.CacheHit);
        Assert.False(firstAgain.CacheHit);
        Assert.Equal(2, loads[Path.GetFullPath(firstWorkspace.ToString())]);
        Assert.Equal(1, loads[Path.GetFullPath(secondWorkspace.ToString())]);
    }

    private static Func<FileInfo, WorkspaceLoadOptions, CancellationToken, Task<WorkspaceLoadResult>> CreateFakeLoader(
        Dictionary<string, int> loads)
    {
        return (workspace, _, _) =>
        {
            string fullPath = Path.GetFullPath(workspace.ToString());
            loads[fullPath] = loads.TryGetValue(fullPath, out int count) ? count + 1 : 1;
            MSBuildWorkspace msbuildWorkspace = MSBuildWorkspace.Create();
            LoadedWorkspace loadedWorkspace = new(
                FullPath: fullPath,
                DisplayPath: Path.GetFileName(fullPath),
                Kind: "solution",
                Workspace: msbuildWorkspace,
                Solution: msbuildWorkspace.CurrentSolution,
                Projects: [],
                DocumentIndex: DocumentIndexProvider.GetOrCreate(msbuildWorkspace.CurrentSolution));

            return Task.FromResult(WorkspaceLoadResult.Succeeded(loadedWorkspace, []));
        };
    }
}

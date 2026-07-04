using Microsoft.CodeAnalysis.MSBuild;
using Navlyn.Workspaces;

namespace Navlyn.Tests.Workspaces;

public sealed class WorkspaceDiskCacheTests
{
    [Fact]
    public async Task CreateStatusAsync_WritesManifestAndRejectsStaleTrackedFile()
    {
        using TemporaryDirectory temp = TemporaryDirectory.Create();
        string workspacePath = Path.Combine(temp.Path, "sample.slnx");
        string cacheDirectory = Path.Combine(temp.Path, ".navlyn", "cache");
        await File.WriteAllTextAsync(workspacePath, "first", CancellationToken.None);

        using MSBuildWorkspace msbuildWorkspace = MSBuildWorkspace.Create();
        using LoadedWorkspace workspace = new(
            FullPath: workspacePath,
            DisplayPath: "sample.slnx",
            Kind: "solution",
            Workspace: msbuildWorkspace,
            Solution: msbuildWorkspace.CurrentSolution,
            Projects: [],
            DocumentIndex: DocumentIndexProvider.GetOrCreate(msbuildWorkspace.CurrentSolution));

        WorkspaceStatusResult writeResult = await WorkspaceDiskCache.CreateStatusAsync(
            "workspace-refresh",
            workspace,
            snapshot: null,
            new FileInfo(workspacePath),
            new WorkspaceDiskCacheRequest("on", Write: true, Clear: false, DirectoryOverride: cacheDirectory),
            CancellationToken.None);

        Assert.Equal("fresh", writeResult.Cache.Status);
        Assert.True(File.Exists(writeResult.Cache.ManifestPath));
        Assert.Equal(0, writeResult.Cache.CachedDeclarationCount);

        await File.WriteAllTextAsync(workspacePath, "second", CancellationToken.None);

        WorkspaceStatusResult staleResult = await WorkspaceDiskCache.CreateStatusAsync(
            "workspace-status",
            workspace,
            snapshot: null,
            new FileInfo(workspacePath),
            new WorkspaceDiskCacheRequest("on", Write: false, Clear: false, DirectoryOverride: cacheDirectory),
            CancellationToken.None);

        Assert.Equal("stale", staleResult.Cache.Status);
        Assert.NotEqual(staleResult.Cache.CurrentFingerprint, staleResult.Cache.CachedFingerprint);
        Assert.Contains(staleResult.Cache.StaleReasons, reason => reason.Contains("tracked workspace files", StringComparison.Ordinal));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "navlyn-cache-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}

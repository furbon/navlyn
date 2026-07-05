namespace Navlyn.Workspaces;

internal sealed record WorkspaceLoadOptions(
    WorkspaceRootPolicy? RootPolicyOverride = null,
    WorkspaceTimingCollector? Timing = null)
{
    public static WorkspaceLoadOptions Default { get; } = new();
}

internal sealed class WorkspaceTimingCollector
{
    private readonly List<WorkspaceTimingStage> _stages = [];

    public IDisposable Measure(string name)
    {
        return new TimingScope(this, name);
    }

    public IReadOnlyList<WorkspaceTimingStage> Stages => _stages;

    private void Add(string name, TimeSpan elapsed)
    {
        _stages.Add(new WorkspaceTimingStage(name, (long)Math.Round(elapsed.TotalMilliseconds)));
    }

    private sealed class TimingScope(WorkspaceTimingCollector collector, string name) : IDisposable
    {
        private readonly System.Diagnostics.Stopwatch _stopwatch = System.Diagnostics.Stopwatch.StartNew();
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _stopwatch.Stop();
            collector.Add(name, _stopwatch.Elapsed);
            _disposed = true;
        }
    }
}

internal sealed record WorkspaceTimingStage(string Name, long ElapsedMs);

internal enum WorkspaceRootPolicy
{
    RepoRelative,
    AllowListed,
    All
}

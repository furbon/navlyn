using Navlyn.Workspaces;

namespace Navlyn.Cli;

internal sealed class CliInvocationContext : IDisposable
{
    private static readonly AsyncLocal<CliInvocationContext?> Slot = new();
    private readonly CliInvocationContext? previous;

    private CliInvocationContext(IReadOnlyList<string> arguments, string? workingDirectory, LoadedWorkspace? preloadedWorkspace)
    {
        previous = Slot.Value;
        PreloadedWorkspace = preloadedWorkspace;
        Arguments = arguments.ToArray();
        WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory();
        Slot.Value = this;
    }

    public static CliInvocationContext? Current => Slot.Value;
    public IReadOnlyList<string> Arguments { get; }
    public string WorkingDirectory { get; }
    public LoadedWorkspace? PreloadedWorkspace { get; }
    public string? StandardInput { get; set; }

    public static CliInvocationContext Begin(IReadOnlyList<string> arguments, string? workingDirectory = null, LoadedWorkspace? preloadedWorkspace = null)
        => new(arguments, workingDirectory, preloadedWorkspace);

    public void Dispose() => Slot.Value = previous;
}

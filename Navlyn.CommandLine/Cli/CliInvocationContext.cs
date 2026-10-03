namespace Navlyn.Cli;

internal sealed class CliInvocationContext : IDisposable
{
    private static readonly AsyncLocal<CliInvocationContext?> Slot = new();
    private readonly CliInvocationContext? previous;

    private CliInvocationContext(IReadOnlyList<string> arguments, string? workingDirectory)
    {
        previous = Slot.Value;
        Arguments = arguments.ToArray();
        WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory();
        Slot.Value = this;
    }

    public static CliInvocationContext? Current => Slot.Value;
    public IReadOnlyList<string> Arguments { get; }
    public string WorkingDirectory { get; }
    public string? StandardInput { get; set; }

    public static CliInvocationContext Begin(IReadOnlyList<string> arguments, string? workingDirectory = null)
        => new(arguments, workingDirectory);

    public void Dispose() => Slot.Value = previous;
}

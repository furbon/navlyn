using Microsoft.CodeAnalysis;

namespace Navlyn.Workspaces;

internal sealed class WorkspaceSelectionScope : IDisposable
{
    private static readonly AsyncLocal<WorkspaceSelectionScope?> Slot = new();
    private readonly WorkspaceSelectionScope? previous;
    private WorkspaceSelectionScope(string? targetFramework)
    {
        previous = Slot.Value;
        TargetFramework = targetFramework?.Trim() ?? previous?.TargetFramework;
        Slot.Value = this;
    }
    public string? TargetFramework { get; }
    public static string? CurrentTargetFramework => Slot.Value?.TargetFramework;
    public static WorkspaceSelectionScope Begin(string? targetFramework) => new(targetFramework);
    public static bool Includes(Project project) => CurrentTargetFramework is null ||
        string.Equals(ProjectContextFacts.GetTargetFramework(project), CurrentTargetFramework, StringComparison.OrdinalIgnoreCase);
    public void Dispose() => Slot.Value = previous;
}

using Microsoft.CodeAnalysis;

namespace Navlyn.Workspaces;

internal sealed class WorkspaceSelectionScope : IDisposable
{
    private static readonly AsyncLocal<WorkspaceSelectionScope?> Slot = new();
    private readonly WorkspaceSelectionScope? previous;
    private WorkspaceSelectionScope(string? targetFramework, string? typeKind)
    {
        previous = Slot.Value;
        TargetFramework = targetFramework?.Trim() ?? previous?.TargetFramework;
        TypeKind = typeKind ?? previous?.TypeKind;
        Slot.Value = this;
    }
    public string? TargetFramework { get; }
    public static string? CurrentTargetFramework => Slot.Value?.TargetFramework;
    public string? TypeKind { get; }
    public static string? CurrentTypeKind => Slot.Value?.TypeKind;
    public static bool IsValidTypeKind(string? value) => value is null or "class" or "interface" or "struct" or "enum" or "delegate" or "record" or "record-class" or "record-struct";
    public static WorkspaceSelectionScope Begin(string? targetFramework, string? typeKind = null) => new(targetFramework, typeKind);
    public static bool IncludesType(Navlyn.Symbols.SymbolFacts facts) => CurrentTypeKind switch
    {
        null => true,
        "record" => facts.IsRecord == true,
        "record-class" => facts.IsRecord == true && facts.TypeKind == "class",
        "record-struct" => facts.IsRecord == true && facts.TypeKind == "struct",
        string kind => facts.TypeKind == kind
    };
    public static bool Includes(Project project) => CurrentTargetFramework is null ||
        string.Equals(ProjectContextFacts.GetTargetFramework(project), CurrentTargetFramework, StringComparison.OrdinalIgnoreCase);
    public void Dispose() => Slot.Value = previous;
}

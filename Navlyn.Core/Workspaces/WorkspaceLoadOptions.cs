namespace Navlyn.Workspaces;

internal sealed record WorkspaceLoadOptions(WorkspaceRootPolicy? RootPolicyOverride = null)
{
    public static WorkspaceLoadOptions Default { get; } = new();
}

internal enum WorkspaceRootPolicy
{
    RepoRelative,
    AllowListed,
    All
}

using Navlyn.Mcp.Configuration;

namespace Navlyn.Mcp.Tools;

internal static class NavlynMcpToolProfilePolicy
{
    private static readonly string[] ReaderTools =
    [
        NavlynMcpTools.WorkspaceSummaryTool,
        NavlynMcpTools.WorkspaceStatusTool,
        NavlynMcpTools.WorkspaceRefreshTool,
        NavlynMcpTools.ResolveTargetTool,
        NavlynMcpTools.FindSymbolTool,
        NavlynMcpTools.FileOutlineTool,
        NavlynMcpTools.InspectFileTool,
        NavlynMcpTools.SymbolSourceTool,
        NavlynMcpTools.SymbolEdgesTool,
        NavlynMcpTools.AboutSymbolTool,
        NavlynMcpTools.RelatedFilesTool,
        NavlynMcpTools.ExactNavigationTool
    ];

    private static readonly string[] ReviewTools =
    [
        NavlynMcpTools.WorkspaceSummaryTool,
        NavlynMcpTools.WorkspaceStatusTool,
        NavlynMcpTools.WorkspaceRefreshTool,
        NavlynMcpTools.ResolveTargetTool,
        NavlynMcpTools.FindSymbolTool,
        NavlynMcpTools.FileOutlineTool,
        NavlynMcpTools.InspectFileTool,
        NavlynMcpTools.SymbolSourceTool,
        NavlynMcpTools.SymbolEdgesTool,
        NavlynMcpTools.AboutSymbolTool,
        NavlynMcpTools.RelatedFilesTool,
        NavlynMcpTools.ImpactTool,
        NavlynMcpTools.EntrypointsTool,
        NavlynMcpTools.ExactNavigationTool,
        NavlynMcpTools.TestsForDiffTool,
        NavlynMcpTools.PublicApiDiffTool,
        NavlynMcpTools.ReviewDiffTool,
        NavlynMcpTools.ContextPackTool
    ];

    private static readonly string[] EditTools =
    [
        NavlynMcpTools.WorkspaceSummaryTool,
        NavlynMcpTools.WorkspaceStatusTool,
        NavlynMcpTools.WorkspaceRefreshTool,
        NavlynMcpTools.ResolveTargetTool,
        NavlynMcpTools.FindSymbolTool,
        NavlynMcpTools.FileOutlineTool,
        NavlynMcpTools.InspectFileTool,
        NavlynMcpTools.SymbolSourceTool,
        NavlynMcpTools.SymbolEdgesTool,
        NavlynMcpTools.AboutSymbolTool,
        NavlynMcpTools.RelatedFilesTool,
        NavlynMcpTools.ImpactTool,
        NavlynMcpTools.EntrypointsTool,
        NavlynMcpTools.ExactNavigationTool,
        NavlynMcpTools.TestsForSymbolTool,
        NavlynMcpTools.DiImpactTool,
        NavlynMcpTools.ContextPackTool
    ];

    private static readonly string[] FullTools =
    [
        NavlynMcpTools.WorkspaceSummaryTool,
        NavlynMcpTools.WorkspaceStatusTool,
        NavlynMcpTools.WorkspaceRefreshTool,
        NavlynMcpTools.ResolveTargetTool,
        NavlynMcpTools.FindSymbolTool,
        NavlynMcpTools.FileOutlineTool,
        NavlynMcpTools.InspectFileTool,
        NavlynMcpTools.SymbolSourceTool,
        NavlynMcpTools.SymbolEdgesTool,
        NavlynMcpTools.AboutSymbolTool,
        NavlynMcpTools.RelatedFilesTool,
        NavlynMcpTools.ImpactTool,
        NavlynMcpTools.EntrypointsTool,
        NavlynMcpTools.ExactNavigationTool,
        NavlynMcpTools.TestsForSymbolTool,
        NavlynMcpTools.TestsForDiffTool,
        NavlynMcpTools.DiImpactTool,
        NavlynMcpTools.PublicApiDiffTool,
        NavlynMcpTools.ReviewDiffTool,
        NavlynMcpTools.ContextPackTool,
        NavlynMcpTools.BatchTool
    ];

    public static IReadOnlyList<string> GetToolNames(NavlynMcpToolProfile profile)
    {
        return profile switch
        {
            NavlynMcpToolProfile.Reader => ReaderTools,
            NavlynMcpToolProfile.Review => ReviewTools,
            NavlynMcpToolProfile.Edit => EditTools,
            NavlynMcpToolProfile.Full => FullTools,
            _ => ReaderTools
        };
    }

    public static bool Allows(NavlynMcpToolProfile profile, string toolName)
    {
        return GetToolNames(profile).Contains(toolName, StringComparer.Ordinal);
    }
}

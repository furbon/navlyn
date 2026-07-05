using Navlyn.Mcp.Configuration;

namespace Navlyn.Mcp.Tools;

internal static class NavlynMcpToolProfilePolicy
{
    private static readonly string[] ReaderTools =
    [
        NavlynMcpTools.WorkspaceSummaryTool,
        NavlynMcpTools.WorkspaceStatusTool,
        NavlynMcpTools.WorkspaceRefreshTool,
        NavlynMcpTools.DoctorTool,
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
        NavlynMcpTools.DoctorTool,
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
        NavlynMcpTools.PostEditGuardTool,
        NavlynMcpTools.WrongSymbolGuardTool,
        NavlynMcpTools.ContextPackTool
    ];

    private static readonly string[] EditTools =
    [
        NavlynMcpTools.WorkspaceSummaryTool,
        NavlynMcpTools.WorkspaceStatusTool,
        NavlynMcpTools.WorkspaceRefreshTool,
        NavlynMcpTools.DoctorTool,
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
        NavlynMcpTools.EditPreflightTool,
        NavlynMcpTools.PostEditGuardTool,
        NavlynMcpTools.WrongSymbolGuardTool,
        NavlynMcpTools.ChangeIntentPackTool,
        NavlynMcpTools.AgentHandoffPackTool,
        NavlynMcpTools.ConfidenceLedgerTool,
        NavlynMcpTools.ContextPackTool
    ];

    private static readonly string[] FullTools =
    [
        NavlynMcpTools.WorkspaceSummaryTool,
        NavlynMcpTools.WorkspaceStatusTool,
        NavlynMcpTools.WorkspaceRefreshTool,
        NavlynMcpTools.DoctorTool,
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
        NavlynMcpTools.EditPreflightTool,
        NavlynMcpTools.PostEditGuardTool,
        NavlynMcpTools.WrongSymbolGuardTool,
        NavlynMcpTools.ChangeIntentPackTool,
        NavlynMcpTools.AgentHandoffPackTool,
        NavlynMcpTools.ConfidenceLedgerTool,
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

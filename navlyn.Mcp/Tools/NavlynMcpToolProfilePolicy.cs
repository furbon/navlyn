using Navlyn.Mcp.Configuration;

namespace Navlyn.Mcp.Tools;

internal static class NavlynMcpToolProfilePolicy
{
    private static readonly string[] UnifiedTools =
    [
        NavlynMcpTools.TargetTool,
        NavlynMcpTools.ReadTool,
        NavlynMcpTools.FileOutlineTool,
        NavlynMcpTools.NavigateTool,
        NavlynMcpTools.PrepareEditTool,
        NavlynMcpTools.VerifyEditTool,
        NavlynMcpTools.ReviewTool,
        NavlynMcpTools.WorkspaceSummaryTool,
        NavlynMcpTools.WorkspaceStatusTool,
        NavlynMcpTools.WorkspaceRefreshTool,
        NavlynMcpTools.DoctorTool,
        NavlynMcpTools.ImpactTool,
        NavlynMcpTools.ContextPackTool,
        NavlynMcpTools.EntrypointsTool,
        NavlynMcpTools.TestsForSymbolTool,
        NavlynMcpTools.TestsForDiffTool,
        NavlynMcpTools.DiagnosticsTool,
        NavlynMcpTools.DiTool,
        NavlynMcpTools.PublicApiDiffTool,
        NavlynMcpTools.RoutesTool,
        NavlynMcpTools.OptionsTool,
        NavlynMcpTools.MessagesTool,
        NavlynMcpTools.EfTool,
        NavlynMcpTools.PackagesTool,
        NavlynMcpTools.BatchTool
    ];

    public static IReadOnlyList<string> GetToolNames(NavlynMcpToolProfile profile)
    {
        _ = profile;
        return UnifiedTools;
    }

    public static bool Allows(NavlynMcpToolProfile profile, string toolName)
    {
        return GetToolNames(profile).Contains(toolName, StringComparer.Ordinal);
    }
}

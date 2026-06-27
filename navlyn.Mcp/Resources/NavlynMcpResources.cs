using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Navlyn.Mcp.Tools;

namespace Navlyn.Mcp.Resources;

[McpServerResourceType]
internal static class NavlynMcpResources
{
    private const string WorkspaceSummaryResource = "navlyn://workspace/summary";
    private const string SymbolResource = "navlyn://symbol/{candidateId}";
    private const string SymbolSourceResource = "navlyn://symbol/{candidateId}/source{?view}";
    private const string FileResource = "navlyn://file/{+path}";

    [McpServerResource(UriTemplate = WorkspaceSummaryResource, Name = "navlyn_workspace_summary", Title = "Navlyn Workspace Summary")]
    public static Task<string> WorkspaceSummary(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        return RunJsonAsync(
            services,
            "navlyn_resource_workspace_summary",
            NavlynToolCommandBuilder.WorkspaceSummary(
                project: null,
                projects: null,
                includePackages: null,
                includeMsbuildFiles: null,
                includePreprocessorSymbols: null,
                classification: null,
                relationshipLimit: null,
                profile: "compact"),
            cancellationToken);
    }

    [McpServerResource(UriTemplate = SymbolResource, Name = "navlyn_symbol", Title = "Navlyn Symbol Facts")]
    public static Task<string> Symbol(
        IServiceProvider services,
        string candidateId,
        CancellationToken cancellationToken = default)
    {
        return RunJsonAsync(
            services,
            "navlyn_resource_symbol",
            NavlynToolCommandBuilder.FuzzySymbolCommand(
                "about",
                query: null,
                candidateId,
                assumeKind: null,
                assumeKinds: null,
                match: null,
                caseSensitive: null,
                project: null,
                projects: null,
                excludeGenerated: null,
                memberLimit: null,
                referenceLimit: null,
                relationLimit: null,
                include: null,
                limit: null,
                depth: null,
                includeSnippets: null,
                snippetLines: null,
                candidatePolicy: null,
                minConfidence: null,
                explainSelection: null),
            cancellationToken);
    }

    [McpServerResource(UriTemplate = SymbolSourceResource, Name = "navlyn_symbol_source", Title = "Navlyn Symbol Source")]
    public static Task<string> SymbolSource(
        IServiceProvider services,
        string candidateId,
        string? view = null,
        CancellationToken cancellationToken = default)
    {
        string effectiveView = string.IsNullOrWhiteSpace(view) ? "declaration" : view.Trim();
        CommandBuildResult command = IsKnownSourceView(effectiveView)
            ? CommandBuildResult.Valid("symbol-source", ["--candidate-id", candidateId.Trim(), "--view", effectiveView])
            : CommandBuildResult.Invalid("view must be one of: signature, declaration, body, members, xml-doc, attributes.");

        return RunJsonAsync(
            services,
            "navlyn_resource_symbol_source",
            command,
            cancellationToken);
    }

    [McpServerResource(UriTemplate = FileResource, Name = "navlyn_file", Title = "Navlyn File Facts")]
    public static Task<string> FileFacts(
        IServiceProvider services,
        string path,
        CancellationToken cancellationToken = default)
    {
        NavlynMcpToolService service = services.GetRequiredService<NavlynMcpToolService>();
        return Task.FromResult(NavlynToolResultFormatter.ToJson(service.CreateInvalidArgumentResult(
            "navlyn_resource_file",
            $"navlyn://file/{path}",
            "navlyn://file resources are advertised for discovery, but raw file reads are intentionally unsupported. Use navlyn_context_pack, navlyn_exact_navigation, or navlyn://symbol/{candidateId}/source?view=declaration for bounded source facts.")));
    }

    private static async Task<string> RunJsonAsync(
        IServiceProvider services,
        string resourceName,
        CommandBuildResult command,
        CancellationToken cancellationToken)
    {
        NavlynMcpToolService service = services.GetRequiredService<NavlynMcpToolService>();
        NavlynToolResult result = await service.RunAsync(resourceName, command, cancellationToken);
        return NavlynToolResultFormatter.ToJson(result);
    }

    private static bool IsKnownSourceView(string view)
    {
        return view is "signature" or "declaration" or "body" or "members" or "xml-doc" or "attributes";
    }
}

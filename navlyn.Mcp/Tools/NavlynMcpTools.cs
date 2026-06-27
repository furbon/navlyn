using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Navlyn.Mcp.Tools;

[McpServerToolType]
internal static class NavlynMcpTools
{
    public const string WorkspaceSummaryTool = "navlyn_workspace_summary";
    public const string FindSymbolTool = "navlyn_find_symbol";
    public const string AboutSymbolTool = "navlyn_about_symbol";
    public const string RelatedFilesTool = "navlyn_related_files";
    public const string ImpactTool = "navlyn_impact";
    public const string EntrypointsTool = "navlyn_entrypoints";
    public const string ReviewDiffTool = "navlyn_review_diff";
    public const string ContextPackTool = "navlyn_context_pack";
    public const string BatchTool = "navlyn_batch";

    private const string WorkspaceSummaryDescription =
        "Use first when starting in a C#/.NET repository and you need projects, target frameworks, references, packages, test relationships, or MSBuild file facts. Do not use for specific symbol lookup, comments, strings, docs, or non-C# files. Returns repo-graph JSON with CLI truncation fields and next actions; use profile compact for small first scans. Follow with navlyn_find_symbol, navlyn_context_pack, or navlyn_review_diff.";

    private const string FindSymbolDescription =
        "Use when you have an approximate C# symbol name and need deterministic candidates or candidate ids. Do not use for comments, strings, markdown, generated artifacts, or non-C# content. Ambiguous results are returned as candidates; do not merge them. Follow with navlyn_about_symbol, navlyn_related_files, or navlyn_impact using candidateId.";

    private const string AboutSymbolDescription =
        "Use when you need selected-symbol facts such as definition, member outline, reference summary, and relations. Prefer candidateId from navlyn_find_symbol when possible. Do not use for diff review; use navlyn_review_diff or navlyn_context_pack diff mode. Ambiguous queries return CLI candidate information without synthesized combined facts.";

    private const string RelatedFilesDescription =
        "Use when you need a file-first map of files related to a selected C# symbol. Do not use for change-risk analysis; use navlyn_impact. Results are bounded by CLI limits and preserve truncation fields. Follow with navlyn_about_symbol or navlyn_impact.";

    private const string ImpactDescription =
        "Use before editing a C# symbol or when asked for static impact/risk. Returns references, callers, calls, implementations, affected files, and optional entrypoint chains. Do not claim runtime, reflection, DI, or config certainty from this static bounded analysis. Follow with navlyn_context_pack before non-trivial edits.";

    private const string EntrypointsDescription =
        "Use to understand how a symbol can be reached from static callers or to inspect framework-discovered entrypoints. Symbol mode calls entrypoints; framework mode calls framework-entrypoints. Do not use for full impact; use navlyn_impact. Results are heuristic and bounded.";

    private const string ReviewDiffDescription =
        "Use for code review or PR/diff investigation. Returns changed symbols, impact facts, diagnostics, related tests, findings, and next actions. Do not use when there is no Git diff or when prose review comments are requested; Navlyn returns facts only. Use profile evidence for review facts or compact when output budgets are tight. Follow with navlyn_context_pack diff mode for bounded reading material.";

    private const string ContextPackDescription =
        "Use when an agent needs a bounded reading queue before reviewing, modifying, or understanding C# code. Supports query, candidateId, or diff mode. Do not use just to list candidates; use navlyn_find_symbol. Respects CLI budgets, output profiles, and truncation fields; lower budgetTokens, limits, or profile if output is too large.";

    private const string BatchDescription =
        "Use when you need multiple existing Navlyn batch-supported facts from the fixed workspace in one MCP call. Accepts the CLI batch defaults/requests shape only, including request-level profile for workflow commands. Prefer this for public-api-diff, tests-for-diff, framework-entrypoints, and DI facts when avoiding repeated workspace loads.";

    [McpServerTool(Name = WorkspaceSummaryTool, Title = "Navlyn Workspace Summary", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description(WorkspaceSummaryDescription)]
    public static Task<CallToolResult> WorkspaceSummary(
        IServiceProvider services,
        [Description("Single project filter by name or repository-relative .csproj path. Mutually exclusive with projects.")] string? project = null,
        [Description("Project filters by name or repository-relative .csproj path. Mutually exclusive with project.")] string[]? projects = null,
        [Description("Whether to include package references. Omit to use CLI default.")] bool? includePackages = null,
        [Description("Whether to include repository MSBuild files. Omit to use CLI default.")] bool? includeMsbuildFiles = null,
        [Description("Whether to include preprocessor symbols. Omit to use CLI default.")] bool? includePreprocessorSymbols = null,
        [Description("Whether to include project classification facts. Omit to use CLI default.")] bool? classification = null,
        [Description("Maximum inferred relationships. Must be 1 or greater.")] int? relationshipLimit = null,
        [Description("Output profile: compact, evidence, or full. Omit to use CLI default full.")] string? profile = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            services,
            WorkspaceSummaryTool,
            NavlynToolCommandBuilder.WorkspaceSummary(project, projects, includePackages, includeMsbuildFiles, includePreprocessorSymbols, classification, relationshipLimit, profile),
            cancellationToken);
    }

    [McpServerTool(Name = FindSymbolTool, Title = "Navlyn Find Symbol", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description(FindSymbolDescription)]
    public static Task<CallToolResult> FindSymbol(
        IServiceProvider services,
        [Description("Approximate symbol name query. Required.")] string query,
        [Description("Single Roslyn SymbolKind hint. Mutually exclusive with assumeKinds.")] string? assumeKind = null,
        [Description("Roslyn SymbolKind hints. Mutually exclusive with assumeKind.")] string[]? assumeKinds = null,
        [Description("Match mode: smart, exact, contains, or regex.")] string? match = null,
        [Description("Use case-sensitive symbol matching.")] bool? caseSensitive = null,
        [Description("Single project filter. Mutually exclusive with projects.")] string? project = null,
        [Description("Project filters. Mutually exclusive with project.")] string[]? projects = null,
        [Description("Exclude generated code candidates.")] bool? excludeGenerated = null,
        [Description("Candidate limit. Must be 1 or greater.")] int? limit = null,
        [Description("Candidate policy: fail, select, or group.")] string? candidatePolicy = null,
        [Description("Minimum confidence: high, medium, or low.")] string? minConfidence = null,
        [Description("Include selection explanation in the CLI result.")] bool? explainSelection = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            services,
            FindSymbolTool,
            NavlynToolCommandBuilder.FindSymbol(query, assumeKind, assumeKinds, match, caseSensitive, project, projects, excludeGenerated, limit, candidatePolicy, minConfidence, explainSelection),
            cancellationToken);
    }

    [McpServerTool(Name = AboutSymbolTool, Title = "Navlyn About Symbol", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description(AboutSymbolDescription)]
    public static Task<CallToolResult> AboutSymbol(
        IServiceProvider services,
        string? query = null,
        string? candidateId = null,
        string? assumeKind = null,
        string[]? assumeKinds = null,
        string? match = null,
        bool? caseSensitive = null,
        string? project = null,
        string[]? projects = null,
        bool? excludeGenerated = null,
        int? memberLimit = null,
        int? referenceLimit = null,
        int? relationLimit = null,
        bool? includeSnippets = null,
        int? snippetLines = null,
        string? candidatePolicy = null,
        string? minConfidence = null,
        bool? explainSelection = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            services,
            AboutSymbolTool,
            NavlynToolCommandBuilder.FuzzySymbolCommand("about", query, candidateId, assumeKind, assumeKinds, match, caseSensitive, project, projects, excludeGenerated, memberLimit, referenceLimit, relationLimit, include: null, limit: null, depth: null, includeSnippets, snippetLines, candidatePolicy, minConfidence, explainSelection),
            cancellationToken);
    }

    [McpServerTool(Name = RelatedFilesTool, Title = "Navlyn Related Files", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description(RelatedFilesDescription)]
    public static Task<CallToolResult> RelatedFiles(
        IServiceProvider services,
        string? query = null,
        string? candidateId = null,
        string? assumeKind = null,
        string[]? assumeKinds = null,
        string? match = null,
        bool? caseSensitive = null,
        string? project = null,
        string[]? projects = null,
        bool? excludeGenerated = null,
        string? include = null,
        int? limit = null,
        int? depth = null,
        bool? includeSnippets = null,
        int? snippetLines = null,
        string? candidatePolicy = null,
        string? minConfidence = null,
        bool? explainSelection = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            services,
            RelatedFilesTool,
            NavlynToolCommandBuilder.FuzzySymbolCommand("related", query, candidateId, assumeKind, assumeKinds, match, caseSensitive, project, projects, excludeGenerated, memberLimit: null, referenceLimit: null, relationLimit: null, include, limit, depth, includeSnippets, snippetLines, candidatePolicy, minConfidence, explainSelection),
            cancellationToken);
    }

    [McpServerTool(Name = ImpactTool, Title = "Navlyn Impact", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description(ImpactDescription)]
    public static Task<CallToolResult> Impact(
        IServiceProvider services,
        string? query = null,
        string? candidateId = null,
        string? assumeKind = null,
        string[]? assumeKinds = null,
        string? match = null,
        bool? caseSensitive = null,
        string? project = null,
        string[]? projects = null,
        bool? excludeGenerated = null,
        string? include = null,
        int? limit = null,
        int? depth = null,
        bool? includeSnippets = null,
        int? snippetLines = null,
        string? candidatePolicy = null,
        string? minConfidence = null,
        bool? explainSelection = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            services,
            ImpactTool,
            NavlynToolCommandBuilder.FuzzySymbolCommand("impact", query, candidateId, assumeKind, assumeKinds, match, caseSensitive, project, projects, excludeGenerated, memberLimit: null, referenceLimit: null, relationLimit: null, include, limit, depth, includeSnippets, snippetLines, candidatePolicy, minConfidence, explainSelection),
            cancellationToken);
    }

    [McpServerTool(Name = EntrypointsTool, Title = "Navlyn Entrypoints", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description(EntrypointsDescription)]
    public static Task<CallToolResult> Entrypoints(
        IServiceProvider services,
        string? mode = null,
        string? query = null,
        string? candidateId = null,
        string? assumeKind = null,
        string[]? assumeKinds = null,
        string? match = null,
        bool? caseSensitive = null,
        string? project = null,
        string[]? projects = null,
        bool? excludeGenerated = null,
        string? framework = null,
        int? limit = null,
        int? depth = null,
        bool? includeSnippets = null,
        int? snippetLines = null,
        string? candidatePolicy = null,
        string? minConfidence = null,
        bool? explainSelection = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            services,
            EntrypointsTool,
            NavlynToolCommandBuilder.Entrypoints(mode, query, candidateId, assumeKind, assumeKinds, match, caseSensitive, project, projects, excludeGenerated, framework, limit, depth, includeSnippets, snippetLines, candidatePolicy, minConfidence, explainSelection),
            cancellationToken);
    }

    [McpServerTool(Name = ReviewDiffTool, Title = "Navlyn Review Diff", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description(ReviewDiffDescription)]
    public static Task<CallToolResult> ReviewDiff(
        IServiceProvider services,
        string? @base = null,
        string? head = null,
        bool? staged = null,
        bool? includeUnstaged = null,
        string? project = null,
        string[]? projects = null,
        bool? excludeGenerated = null,
        int? symbolLimit = null,
        int? impactLimit = null,
        int? diagnosticLimit = null,
        int? relatedTestLimit = null,
        int? depth = null,
        bool? includeSnippets = null,
        int? snippetLines = null,
        string? profile = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            services,
            ReviewDiffTool,
            NavlynToolCommandBuilder.ReviewDiff(@base, head, staged, includeUnstaged, project, projects, excludeGenerated, symbolLimit, impactLimit, diagnosticLimit, relatedTestLimit, depth, includeSnippets, snippetLines, profile),
            cancellationToken);
    }

    [McpServerTool(Name = ContextPackTool, Title = "Navlyn Context Pack", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description(ContextPackDescription)]
    public static Task<CallToolResult> ContextPack(
        IServiceProvider services,
        string? query = null,
        string? candidateId = null,
        bool? diff = null,
        string? @base = null,
        string? head = null,
        bool? staged = null,
        bool? includeUnstaged = null,
        string? goal = null,
        int? budgetTokens = null,
        int? itemLimit = null,
        string? snippetPolicy = null,
        int? snippetLines = null,
        int? candidateLimit = null,
        int? memberLimit = null,
        int? referenceLimit = null,
        int? relationLimit = null,
        int? fileLimit = null,
        int? diagnosticLimit = null,
        int? symbolLimit = null,
        int? impactLimit = null,
        int? relatedTestLimit = null,
        int? depth = null,
        string? candidatePolicy = null,
        string? minConfidence = null,
        bool? explainSelection = null,
        string? assumeKind = null,
        string[]? assumeKinds = null,
        string? match = null,
        bool? caseSensitive = null,
        string? project = null,
        string[]? projects = null,
        bool? excludeGenerated = null,
        string? profile = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            services,
            ContextPackTool,
            NavlynToolCommandBuilder.ContextPack(query, candidateId, diff, @base, head, staged, includeUnstaged, goal, budgetTokens, itemLimit, snippetPolicy, snippetLines, candidateLimit, memberLimit, referenceLimit, relationLimit, fileLimit, diagnosticLimit, symbolLimit, impactLimit, relatedTestLimit, depth, candidatePolicy, minConfidence, explainSelection, assumeKind, assumeKinds, match, caseSensitive, project, projects, excludeGenerated, profile),
            cancellationToken);
    }

    [McpServerTool(Name = BatchTool, Title = "Navlyn Batch", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description(BatchDescription)]
    public static Task<CallToolResult> Batch(
        IServiceProvider services,
        JsonElement? defaults = null,
        JsonElement? requests = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            services,
            BatchTool,
            NavlynToolCommandBuilder.Batch(defaults, requests),
            cancellationToken);
    }

    private static async Task<CallToolResult> RunAsync(
        IServiceProvider services,
        string toolName,
        CommandBuildResult command,
        CancellationToken cancellationToken)
    {
        NavlynMcpToolService service = services.GetRequiredService<NavlynMcpToolService>();
        NavlynToolResult result = await service.RunAsync(toolName, command, cancellationToken);
        return NavlynToolResultFormatter.ToCallToolResult(result);
    }
}

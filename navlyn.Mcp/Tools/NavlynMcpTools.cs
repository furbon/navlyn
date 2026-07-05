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
    public const string WorkspaceStatusTool = "navlyn_workspace_status";
    public const string WorkspaceRefreshTool = "navlyn_workspace_refresh";
    public const string DoctorTool = "navlyn_doctor";
    public const string FindSymbolTool = "navlyn_find_symbol";
    public const string ResolveTargetTool = "navlyn_resolve_target";
    public const string FileOutlineTool = "navlyn_file_outline";
    public const string SymbolSourceTool = "navlyn_symbol_source";
    public const string SymbolEdgesTool = "navlyn_symbol_edges";
    public const string InspectFileTool = "navlyn_inspect_file";
    public const string AboutSymbolTool = "navlyn_about_symbol";
    public const string RelatedFilesTool = "navlyn_related_files";
    public const string ImpactTool = "navlyn_impact";
    public const string EntrypointsTool = "navlyn_entrypoints";
    public const string ExactNavigationTool = "navlyn_exact_navigation";
    public const string TestsForSymbolTool = "navlyn_tests_for_symbol";
    public const string TestsForDiffTool = "navlyn_tests_for_diff";
    public const string DiImpactTool = "navlyn_di_impact";
    public const string PublicApiDiffTool = "navlyn_public_api_diff";
    public const string ReviewDiffTool = "navlyn_review_diff";
    public const string ContextPackTool = "navlyn_context_pack";
    public const string EditPreflightTool = "navlyn_edit_preflight";
    public const string PostEditGuardTool = "navlyn_post_edit_guard";
    public const string WrongSymbolGuardTool = "navlyn_wrong_symbol_guard";
    public const string ChangeIntentPackTool = "navlyn_change_intent_pack";
    public const string AgentHandoffPackTool = "navlyn_agent_handoff_pack";
    public const string ConfidenceLedgerTool = "navlyn_confidence_ledger";
    public const string BatchTool = "navlyn_batch";

    private const string WorkspaceSummaryDescription =
        "Use only when project structure, target frameworks, package references, test relationships, or MSBuild file facts would change the answer. Do not run as a default first step for single-file review, specific symbol lookup, comments, strings, docs, or non-C# files. Returns repo-graph JSON; use profile compact when a small workspace map is enough.";

    private const string WorkspaceStatusDescription =
        "Use to inspect the current workspace snapshot, freshness metadata, direct cache status, and optional on-disk cache manifest state. This is a lifecycle/status tool, not a repository overview; use navlyn_workspace_summary for project graph facts.";

    private const string WorkspaceRefreshDescription =
        "Use only when the workspace snapshot or on-disk cache should be explicitly refreshed. It forces a fresh workspace load in the server process and can clear or write the lightweight .navlyn/cache manifest when requested.";

    private const string DoctorDescription =
        "Use at setup time or after workspace failures to verify the configured Navlyn workspace, .NET SDK availability, supported target frameworks, load diagnostics, and the first safe commands to try. It returns CLI doctor JSON and performs read-only checks only.";

    private const string FindSymbolDescription =
        "Use when you have an approximate C# symbol name and need deterministic candidates or candidate ids. Do not use for comments, strings, markdown, generated artifacts, or non-C# content. Ambiguous results are returned as candidates; do not merge them. Follow with navlyn_about_symbol, navlyn_related_files, or navlyn_impact using candidateId.";

    private const string ResolveTargetDescription =
        "Use as the standard first symbol entry when an agent has an approximate C# name, a candidateId, or an exact source position and needs one small target envelope with recommended next actions. Prefer navlyn_find_symbol when the user explicitly wants a candidate list. Do not use for comments, strings, docs, non-C# files, or arbitrary command execution.";

    private const string FileOutlineDescription =
        "Use for a semantic outline of one known C# source file when a file map is useful before deeper symbol inspection. Returns outline entries with reusable candidateId values. Do not use for tests, impact analysis, repository overview, comments, strings, docs, non-C# files, or arbitrary command execution.";

    private const string SymbolSourceDescription =
        "Use when one selected C# symbol needs bounded source text by candidateId or exact file/line/column. Prefer this over navlyn_context_pack for a single declaration, body, members, XML doc, or attributes view. Do not use for broad file reading, impact analysis, tests, or diff review.";

    private const string SymbolEdgesDescription =
        "Use when one selected C# symbol needs direct relationship edges: references, callers, calls, or implementations. Prefer candidateId from navlyn_file_outline, navlyn_find_symbol, or navlyn_resolve_target. References and callers are scoped expensive searches; set scope/maxDocuments for broad questions and prefer calls for cheap local outgoing edges. Use filters and limits for noisy symbols. Do not use for definition lookup, source text, test discovery, or static risk analysis.";

    private const string InspectFileDescription =
        "Use for a compact semantic inspection of one known C# source file. It returns the same bounded outline facts as navlyn_file_outline and does not include tests, impact, diagnostics, context packs, or raw file text.";

    private const string AboutSymbolDescription =
        "Use when one selected C# symbol needs a compact summary. Use profile light for first-pass definition/member facts; use full only when reference summary and shallow relations are needed. Prefer candidateId from navlyn_find_symbol or navlyn_resolve_target. Do not use for diff review or as a repository overview; ambiguous queries return candidate information without synthesized combined facts.";

    private const string RelatedFilesDescription =
        "Use when you need a file-first map of files related to a selected C# symbol. Do not use for change-risk analysis; use navlyn_impact. Results are bounded by CLI limits and preserve truncation fields. Follow with navlyn_about_symbol or navlyn_impact.";

    private const string ImpactDescription =
        "Use before editing a selected C# symbol or when static source impact/risk is explicitly needed. Use profile light for declarations plus cheap local calls; use full or explicit include values for bounded references, callers, implementations, hierarchy, and affected files. Set scope/maxDocuments for broad questions. Do not claim runtime, reflection, DI, or config certainty from this static analysis. Escalate to navlyn_context_pack only when the agent needs a reading queue.";

    private const string EntrypointsDescription =
        "Use to understand how a symbol can be reached from static callers or to inspect framework-discovered entrypoints. Symbol mode calls entrypoints; framework mode calls framework-entrypoints. Do not use for full impact; use navlyn_impact. Results are heuristic and bounded.";

    private const string ExactNavigationDescription =
        "Use after navlyn_find_symbol or when you already have an exact C# source position and need precise lower-level Roslyn navigation. Supports allowlist operations: definition, references, callers, calls, implementations, type_hierarchy, and symbol_info. References and callers are scoped expensive searches; calls is local to the containing member. Prefer navlyn_symbol_edges for references/callers/calls/implementations and navlyn_symbol_source for bounded source text. Do not use for broad repository search, diff review, or arbitrary CLI execution.";

    private const string TestsForSymbolDescription =
        "Use only when planning or reviewing an edit and related test candidates are needed for a selected C# symbol. Prefer candidateId from navlyn_find_symbol or an exact file/line/column. Do not use for first-pass comprehension, and do not treat this as a test runner; Navlyn reports static facts only.";

    private const string TestsForDiffDescription =
        "Use only for PR or working-tree investigation when related tests for changed C# symbols are explicitly useful. Do not use for first-pass code reading, as a test runner, or when there is no diff. Use profile compact or evidence when output budgets are tight.";

    private const string DiImpactDescription =
        "Use before changing a DI service or implementation type when you need source-level Microsoft.Extensions.DependencyInjection registrations, consumers, constructor dependencies, and risk facts. Do not treat this as runtime container proof; reflection, configuration, and custom containers can be incomplete.";

    private const string PublicApiDiffDescription =
        "Use for release or review checks when you need source-level public/protected API changes between Git refs. Requires base. Do not use for runtime binary compatibility proof; this reports Navlyn's source-level public API facts.";

    private const string ReviewDiffDescription =
        "Use only for Git diff, PR, staged, or working-tree change investigation. Returns changed symbols, impact facts, diagnostics, related tests, findings, and next actions. Do not use for single-file review, general code review with no diff, or prose review comments; Navlyn returns facts only. Escalate to navlyn_context_pack diff mode only when bounded reading material is needed.";

    private const string ContextPackDescription =
        "Use as an escalation tool when normal file reads or smaller Navlyn facts are not enough and the agent needs a bounded reading queue before review, modification, or explanation. Supports query, candidateId, or diff mode, plus changeKind ranking hints. Do not use just to list candidates or as a default first step; use navlyn_find_symbol, navlyn_resolve_target, or exact navigation first.";

    private const string EditPreflightDescription =
        "Use immediately before editing one intended C# target. It anchors fuzzy intent, returns bounded source/context/test evidence, risk, known unknowns, and the post-edit guard command. Do not use for broad repository review or after the edit.";

    private const string PostEditGuardDescription =
        "Use after an edit to compare a saved preflight anchor or candidateId with the current diff. It returns wrong-target risk, changed symbols, score reasons, and a policy pass/fail result.";

    private const string WrongSymbolGuardDescription =
        "Use when no full preflight file exists and the agent needs to compare intended C# symbol intent with changed symbols. It is a focused wrong-symbol risk check for CI or agent policy.";

    private const string AgentPackDescription =
        "Use when an edit handoff needs a compact intent, evidence, confidence, or reading-queue record derived from the same semantic preflight evidence.";

    private const string BatchDescription =
        "Use only after deciding that several batch-supported Navlyn facts are needed from the same fixed workspace. It is an optimization and orchestration tool, not a default discovery step. Accepts the CLI batch defaults/requests shape only, including request-level profile for workflow commands.";

    [McpServerTool(Name = WorkspaceSummaryTool, Title = "Navlyn Workspace Summary", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
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

    [McpServerTool(Name = WorkspaceStatusTool, Title = "Navlyn Workspace Status", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
    [Description(WorkspaceStatusDescription)]
    public static Task<CallToolResult> WorkspaceStatus(
        IServiceProvider services,
        [Description("On-disk cache mode: auto, on, or off. Auto honors navlyn.workspace.json cacheHints.")] string? cache = null,
        [Description("Optional on-disk cache directory override.")] string? cacheDirectory = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            services,
            WorkspaceStatusTool,
            NavlynToolCommandBuilder.WorkspaceStatus(cache, cacheDirectory),
            cancellationToken);
    }

    [McpServerTool(Name = WorkspaceRefreshTool, Title = "Navlyn Workspace Refresh", ReadOnly = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
    [Description(WorkspaceRefreshDescription)]
    public static Task<CallToolResult> WorkspaceRefresh(
        IServiceProvider services,
        [Description("On-disk cache mode: auto, on, or off. Auto honors navlyn.workspace.json cacheHints.")] string? cache = null,
        [Description("Optional on-disk cache directory override.")] string? cacheDirectory = null,
        [Description("Remove the current on-disk workspace cache manifest before reporting or writing cache state.")] bool? clearCache = null,
        [Description("Write a fresh lightweight on-disk workspace cache manifest.")] bool? writeCache = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            services,
            WorkspaceRefreshTool,
            NavlynToolCommandBuilder.WorkspaceRefresh(cache, cacheDirectory, clearCache, writeCache),
            cancellationToken);
    }

    [McpServerTool(Name = DoctorTool, Title = "Navlyn Doctor", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
    [Description(DoctorDescription)]
    public static Task<CallToolResult> Doctor(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            services,
            DoctorTool,
            NavlynToolCommandBuilder.Doctor(),
            cancellationToken);
    }

    [McpServerTool(Name = FindSymbolTool, Title = "Navlyn Find Symbol", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
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

    [McpServerTool(Name = ResolveTargetTool, Title = "Navlyn Resolve Target", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
    [Description(ResolveTargetDescription)]
    public static Task<CallToolResult> ResolveTarget(
        IServiceProvider services,
        [Description("Approximate symbol name query. Mutually exclusive with candidateId and file/line/column.")] string? query = null,
        [Description("Candidate id returned by a previous fuzzy command. Mutually exclusive with query and file/line/column.")] string? candidateId = null,
        [Description("C# source file target. Must be provided with line and column when query and candidateId are omitted.")] string? file = null,
        [Description("1-based source line. Must be provided with file and column when source position mode is used.")] int? line = null,
        [Description("1-based source column. Must be provided with file and line when source position mode is used.")] int? column = null,
        [Description("Single Roslyn SymbolKind hint for query mode. Mutually exclusive with assumeKinds.")] string? assumeKind = null,
        [Description("Roslyn SymbolKind hints for query mode. Mutually exclusive with assumeKind.")] string[]? assumeKinds = null,
        [Description("Query match mode: smart, exact, contains, or regex.")] string? match = null,
        [Description("Use case-sensitive query matching.")] bool? caseSensitive = null,
        [Description("Single project filter. Mutually exclusive with projects.")] string? project = null,
        [Description("Project filters. Mutually exclusive with project.")] string[]? projects = null,
        [Description("Exclude generated code candidates or source-position targets.")] bool? excludeGenerated = null,
        [Description("Candidate display limit for query mode. Must be 1 or greater.")] int? limit = null,
        [Description("Candidate policy for query mode: fail or select.")] string? candidatePolicy = null,
        [Description("Minimum confidence for query mode: high, medium, or low.")] string? minConfidence = null,
        [Description("Include selection explanation in query or candidateId mode.")] bool? explainSelection = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            services,
            ResolveTargetTool,
            NavlynToolCommandBuilder.ResolveTarget(query, candidateId, file, line, column, assumeKind, assumeKinds, match, caseSensitive, project, projects, excludeGenerated, limit, candidatePolicy, minConfidence, explainSelection),
            cancellationToken);
    }

    [McpServerTool(Name = FileOutlineTool, Title = "Navlyn File Outline", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
    [Description(FileOutlineDescription)]
    public static Task<CallToolResult> FileOutline(
        IServiceProvider services,
        [Description("C# source file to outline. Required.")] string file,
        [Description("Input project context by project name or repository-relative .csproj path.")] string? project = null,
        [Description("Exclude generated source files.")] bool? excludeGenerated = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            services,
            FileOutlineTool,
            NavlynToolCommandBuilder.FileOutline(file, project, excludeGenerated),
            cancellationToken);
    }

    [McpServerTool(Name = SymbolSourceTool, Title = "Navlyn Symbol Source", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
    [Description(SymbolSourceDescription)]
    public static Task<CallToolResult> SymbolSource(
        IServiceProvider services,
        [Description("Candidate id returned by navlyn_file_outline, navlyn_find_symbol, or navlyn_resolve_target. Mutually exclusive with file/line/column.")] string? candidateId = null,
        [Description("C# source file target. Must be provided with line and column when candidateId is omitted.")] string? file = null,
        [Description("1-based source line. Must be provided with file and column when candidateId is omitted.")] int? line = null,
        [Description("1-based source column. Must be provided with file and line when candidateId is omitted.")] int? column = null,
        [Description("Input project context by project name or repository-relative .csproj path.")] string? project = null,
        [Description("Exclude generated source files.")] bool? excludeGenerated = null,
        [Description("Source view: signature, declaration, body, members, xml-doc, or attributes.")] string? view = null,
        [Description("Maximum source lines per slice. Must be 1 or greater.")] int? maxLines = null,
        [Description("Approximate token budget per slice. Must be 1 or greater.")] int? budgetTokens = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            services,
            SymbolSourceTool,
            NavlynToolCommandBuilder.SymbolSource(candidateId, file, line, column, project, excludeGenerated, view, maxLines, budgetTokens),
            cancellationToken);
    }

    [McpServerTool(Name = SymbolEdgesTool, Title = "Navlyn Symbol Edges", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
    [Description(SymbolEdgesDescription)]
    public static Task<CallToolResult> SymbolEdges(
        IServiceProvider services,
        [Description("Relationship operation: references, callers, calls, or implementations.")] string operation,
        [Description("Candidate id returned by navlyn_file_outline, navlyn_find_symbol, or navlyn_resolve_target. Mutually exclusive with file/line/column.")] string? candidateId = null,
        [Description("C# source file target. Must be provided with line and column when candidateId is omitted.")] string? file = null,
        [Description("1-based source line. Must be provided with file and column when candidateId is omitted.")] int? line = null,
        [Description("1-based source column. Must be provided with file and line when candidateId is omitted.")] int? column = null,
        [Description("Input project context by project name or repository-relative .csproj path.")] string? project = null,
        [Description("Exclude generated source files and generated result locations where the CLI operation supports it.")] bool? excludeGenerated = null,
        [Description("Single result project filter. Mutually exclusive with resultProjects.")] string? resultProject = null,
        [Description("Result project filters. Mutually exclusive with resultProject.")] string[]? resultProjects = null,
        [Description("Single result path fragment filter. Mutually exclusive with resultPaths.")] string? resultPath = null,
        [Description("Result path fragment filters. Mutually exclusive with resultPath.")] string[]? resultPaths = null,
        [Description("Single result symbol kind filter. Mutually exclusive with resultKinds.")] string? resultKind = null,
        [Description("Result symbol kind filters. Mutually exclusive with resultKind.")] string[]? resultKinds = null,
        [Description("Single reference usage kind filter for operation references. Mutually exclusive with usageKinds.")] string? usageKind = null,
        [Description("Reference usage kind filters for operation references. Mutually exclusive with usageKind.")] string[]? usageKinds = null,
        [Description("Grouped reference summaries for operation references. Values: file, project, containing-symbol, usage-kind, test-vs-production.")] string[]? groupBy = null,
        [Description("Result limit. Must be 1 or greater.")] int? limit = null,
        [Description("Search scope for references/callers: file, project, dependent-projects, workspace-set, or solution.")] string? scope = null,
        [Description("Maximum lexically matching documents for references/callers. Must be 1 or greater.")] int? maxDocuments = null,
        [Description("Include metadata-only symbol facts where supported by calls.")] bool? includeMetadata = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            services,
            SymbolEdgesTool,
            NavlynToolCommandBuilder.SymbolEdges(operation, candidateId, file, line, column, project, excludeGenerated, resultProject, resultProjects, resultPath, resultPaths, resultKind, resultKinds, usageKind, usageKinds, groupBy, limit, scope, maxDocuments, includeMetadata),
            cancellationToken);
    }

    [McpServerTool(Name = InspectFileTool, Title = "Navlyn Inspect File", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
    [Description(InspectFileDescription)]
    public static Task<CallToolResult> InspectFile(
        IServiceProvider services,
        [Description("C# source file to inspect. Required.")] string file,
        [Description("Input project context by project name or repository-relative .csproj path.")] string? project = null,
        [Description("Exclude generated source files.")] bool? excludeGenerated = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            services,
            InspectFileTool,
            NavlynToolCommandBuilder.InspectFile(file, project, excludeGenerated),
            cancellationToken);
    }

    [McpServerTool(Name = AboutSymbolTool, Title = "Navlyn About Symbol", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
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
        [Description("Search scope for heavy reference/relation facts: file, project, dependent-projects, workspace-set, or solution.")] string? scope = null,
        [Description("Maximum lexically matching documents for heavy reference/relation facts. Must be 1 or greater.")] int? maxDocuments = null,
        [Description("Workflow profile: light omits heavy references/relations; full keeps compatibility behavior.")] string? profile = null,
        string? candidatePolicy = null,
        string? minConfidence = null,
        bool? explainSelection = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            services,
            AboutSymbolTool,
            NavlynToolCommandBuilder.FuzzySymbolCommand("about", query, candidateId, assumeKind, assumeKinds, match, caseSensitive, project, projects, excludeGenerated, memberLimit, referenceLimit, relationLimit, include: null, limit: null, depth: null, includeSnippets, snippetLines, scope, maxDocuments, profile, candidatePolicy, minConfidence, explainSelection),
            cancellationToken);
    }

    [McpServerTool(Name = RelatedFilesTool, Title = "Navlyn Related Files", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
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
            NavlynToolCommandBuilder.FuzzySymbolCommand("related", query, candidateId, assumeKind, assumeKinds, match, caseSensitive, project, projects, excludeGenerated, memberLimit: null, referenceLimit: null, relationLimit: null, include, limit, depth, includeSnippets, snippetLines, scope: null, maxDocuments: null, profile: null, candidatePolicy, minConfidence, explainSelection),
            cancellationToken);
    }

    [McpServerTool(Name = ImpactTool, Title = "Navlyn Impact", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
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
        [Description("Search scope for heavy references/callers: file, project, dependent-projects, workspace-set, or solution.")] string? scope = null,
        [Description("Maximum lexically matching documents for heavy references/callers. Must be 1 or greater.")] int? maxDocuments = null,
        [Description("Workflow profile: light defaults to declarations and local calls; full keeps compatibility behavior.")] string? profile = null,
        string? candidatePolicy = null,
        string? minConfidence = null,
        bool? explainSelection = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            services,
            ImpactTool,
            NavlynToolCommandBuilder.FuzzySymbolCommand("impact", query, candidateId, assumeKind, assumeKinds, match, caseSensitive, project, projects, excludeGenerated, memberLimit: null, referenceLimit: null, relationLimit: null, include, limit, depth, includeSnippets, snippetLines, scope, maxDocuments, profile, candidatePolicy, minConfidence, explainSelection),
            cancellationToken);
    }

    [McpServerTool(Name = EntrypointsTool, Title = "Navlyn Entrypoints", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
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

    [McpServerTool(Name = ExactNavigationTool, Title = "Navlyn Exact Navigation", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
    [Description(ExactNavigationDescription)]
    public static Task<CallToolResult> ExactNavigation(
        IServiceProvider services,
        [Description("Operation: definition, references, callers, calls, implementations, type_hierarchy, or symbol_info.")] string operation,
        [Description("Candidate id returned by navlyn_find_symbol or another fuzzy command. Mutually exclusive with file/line/column.")] string? candidateId = null,
        [Description("C# source file target. Must be provided with line and column when candidateId is omitted.")] string? file = null,
        [Description("1-based source line. Must be provided with file and column when candidateId is omitted.")] int? line = null,
        [Description("1-based source column. Must be provided with file and line when candidateId is omitted.")] int? column = null,
        [Description("Input project context by project name or repository-relative .csproj path.")] string? project = null,
        [Description("Exclude generated source files and generated result locations where the CLI operation supports it.")] bool? excludeGenerated = null,
        [Description("Single result project filter. Supported for references, callers, calls, and implementations. Mutually exclusive with resultProjects.")] string? resultProject = null,
        [Description("Result project filters. Supported for references, callers, calls, and implementations. Mutually exclusive with resultProject.")] string[]? resultProjects = null,
        [Description("Single result path fragment filter. Supported for references, callers, calls, and implementations. Mutually exclusive with resultPaths.")] string? resultPath = null,
        [Description("Result path fragment filters. Supported for references, callers, calls, and implementations. Mutually exclusive with resultPath.")] string[]? resultPaths = null,
        [Description("Single result symbol kind filter. Supported for references, callers, calls, and implementations. Mutually exclusive with resultKinds.")] string? resultKind = null,
        [Description("Result symbol kind filters. Supported for references, callers, calls, and implementations. Mutually exclusive with resultKind.")] string[]? resultKinds = null,
        [Description("Single reference usage kind filter for operation references. Mutually exclusive with usageKinds. Values include read, write, invoke, construct, inherit, implement, override, attribute, nameof, typeof.")] string? usageKind = null,
        [Description("Reference usage kind filters for operation references. Mutually exclusive with usageKind.")] string[]? usageKinds = null,
        [Description("Grouped reference summaries for operation references. Values: file, project, containing-symbol, usage-kind, test-vs-production.")] string[]? groupBy = null,
        [Description("Result limit. Supported for references, callers, calls, and implementations. Must be 1 or greater.")] int? limit = null,
        [Description("Search scope for references/callers: file, project, dependent-projects, workspace-set, or solution.")] string? scope = null,
        [Description("Maximum lexically matching documents for references/callers. Must be 1 or greater.")] int? maxDocuments = null,
        [Description("Include metadata-only symbol facts where supported by definition and calls.")] bool? includeMetadata = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            services,
            ExactNavigationTool,
            NavlynToolCommandBuilder.ExactNavigation(operation, candidateId, file, line, column, project, excludeGenerated, resultProject, resultProjects, resultPath, resultPaths, resultKind, resultKinds, usageKind, usageKinds, groupBy, limit, scope, maxDocuments, includeMetadata),
            cancellationToken);
    }

    [McpServerTool(Name = TestsForSymbolTool, Title = "Navlyn Tests For Symbol", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
    [Description(TestsForSymbolDescription)]
    public static Task<CallToolResult> TestsForSymbol(
        IServiceProvider services,
        string? query = null,
        string? candidateId = null,
        string? file = null,
        int? line = null,
        int? column = null,
        string? assumeKind = null,
        string[]? assumeKinds = null,
        string? match = null,
        bool? caseSensitive = null,
        string? project = null,
        string[]? projects = null,
        string? testProject = null,
        string[]? testProjects = null,
        bool? excludeGenerated = null,
        int? candidateLimit = null,
        int? testLimit = null,
        int? referenceLimit = null,
        bool? includeSnippets = null,
        int? snippetLines = null,
        string? candidatePolicy = null,
        string? minConfidence = null,
        bool? explainSelection = null,
        string? profile = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            services,
            TestsForSymbolTool,
            NavlynToolCommandBuilder.TestsForSymbol(query, candidateId, file, line, column, assumeKind, assumeKinds, match, caseSensitive, project, projects, testProject, testProjects, excludeGenerated, candidateLimit, testLimit, referenceLimit, includeSnippets, snippetLines, candidatePolicy, minConfidence, explainSelection, profile),
            cancellationToken);
    }

    [McpServerTool(Name = TestsForDiffTool, Title = "Navlyn Tests For Diff", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
    [Description(TestsForDiffDescription)]
    public static Task<CallToolResult> TestsForDiff(
        IServiceProvider services,
        string? @base = null,
        string? head = null,
        bool? staged = null,
        bool? includeUnstaged = null,
        string? project = null,
        string[]? projects = null,
        string? testProject = null,
        string[]? testProjects = null,
        bool? excludeGenerated = null,
        int? symbolLimit = null,
        int? testLimit = null,
        int? referenceLimit = null,
        bool? includeSnippets = null,
        int? snippetLines = null,
        string? profile = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            services,
            TestsForDiffTool,
            NavlynToolCommandBuilder.TestsForDiff(@base, head, staged, includeUnstaged, project, projects, testProject, testProjects, excludeGenerated, symbolLimit, testLimit, referenceLimit, includeSnippets, snippetLines, profile),
            cancellationToken);
    }

    [McpServerTool(Name = DiImpactTool, Title = "Navlyn DI Impact", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
    [Description(DiImpactDescription)]
    public static Task<CallToolResult> DiImpact(
        IServiceProvider services,
        string? query = null,
        string? candidateId = null,
        string? file = null,
        int? line = null,
        int? column = null,
        string? assumeKind = null,
        string[]? assumeKinds = null,
        string? match = null,
        bool? caseSensitive = null,
        string? project = null,
        string[]? projects = null,
        bool? excludeGenerated = null,
        int? candidateLimit = null,
        int? registrationLimit = null,
        int? consumerLimit = null,
        int? dependencyLimit = null,
        int? riskLimit = null,
        int? depth = null,
        bool? includeSnippets = null,
        int? snippetLines = null,
        string? candidatePolicy = null,
        string? minConfidence = null,
        bool? explainSelection = null,
        string? profile = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            services,
            DiImpactTool,
            NavlynToolCommandBuilder.DiImpact(query, candidateId, file, line, column, assumeKind, assumeKinds, match, caseSensitive, project, projects, excludeGenerated, candidateLimit, registrationLimit, consumerLimit, dependencyLimit, riskLimit, depth, includeSnippets, snippetLines, candidatePolicy, minConfidence, explainSelection, profile),
            cancellationToken);
    }

    [McpServerTool(Name = PublicApiDiffTool, Title = "Navlyn Public API Diff", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
    [Description(PublicApiDiffDescription)]
    public static Task<CallToolResult> PublicApiDiff(
        IServiceProvider services,
        string? @base = null,
        string? head = null,
        string? project = null,
        string[]? projects = null,
        bool? excludeGenerated = null,
        bool? includeAdditions = null,
        bool? includeAttributes = null,
        int? symbolLimit = null,
        int? changeLimit = null,
        string? profile = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            services,
            PublicApiDiffTool,
            NavlynToolCommandBuilder.PublicApiDiff(@base, head, project, projects, excludeGenerated, includeAdditions, includeAttributes, symbolLimit, changeLimit, profile),
            cancellationToken);
    }

    [McpServerTool(Name = ReviewDiffTool, Title = "Navlyn Review Diff", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
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

    [McpServerTool(Name = ContextPackTool, Title = "Navlyn Context Pack", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
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
        [Description("Optional edit ranking hint: behavior, signature, rename, constructor, nullability, async, public-api, di-registration, or endpoint.")] string? changeKind = null,
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
            NavlynToolCommandBuilder.ContextPack(query, candidateId, diff, @base, head, staged, includeUnstaged, goal, changeKind, budgetTokens, itemLimit, snippetPolicy, snippetLines, candidateLimit, memberLimit, referenceLimit, relationLimit, fileLimit, diagnosticLimit, symbolLimit, impactLimit, relatedTestLimit, depth, candidatePolicy, minConfidence, explainSelection, assumeKind, assumeKinds, match, caseSensitive, project, projects, excludeGenerated, profile),
            cancellationToken);
    }

    [McpServerTool(Name = EditPreflightTool, Title = "Navlyn Edit Preflight", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
    [Description(EditPreflightDescription)]
    public static Task<CallToolResult> EditPreflight(
        IServiceProvider services,
        string? query = null,
        string? candidateId = null,
        string? file = null,
        int? line = null,
        int? column = null,
        string? assumeKind = null,
        string[]? assumeKinds = null,
        string? match = null,
        bool? caseSensitive = null,
        string? project = null,
        string[]? projects = null,
        bool? excludeGenerated = null,
        string? goal = null,
        string? changeKind = null,
        int? budgetTokens = null,
        int? itemLimit = null,
        int? referenceLimit = null,
        int? testLimit = null,
        int? candidateLimit = null,
        string? candidatePolicy = null,
        string? minConfidence = null,
        bool? explainSelection = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            services,
            EditPreflightTool,
            NavlynToolCommandBuilder.AgentTargetPack("edit-preflight", query, candidateId, file, line, column, assumeKind, assumeKinds, match, caseSensitive, project, projects, excludeGenerated, goal, changeKind, budgetTokens, itemLimit, referenceLimit, testLimit, candidateLimit, candidatePolicy, minConfidence, explainSelection),
            cancellationToken);
    }

    [McpServerTool(Name = PostEditGuardTool, Title = "Navlyn Post Edit Guard", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
    [Description(PostEditGuardDescription)]
    public static Task<CallToolResult> PostEditGuard(
        IServiceProvider services,
        string? candidateId = null,
        string? preflight = null,
        string? @base = null,
        string? head = null,
        bool? staged = null,
        bool? includeUnstaged = null,
        string? project = null,
        string[]? projects = null,
        bool? excludeGenerated = null,
        int? symbolLimit = null,
        string? failOnRisk = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            services,
            PostEditGuardTool,
            NavlynToolCommandBuilder.PostEditGuard(candidateId, preflight, @base, head, staged, includeUnstaged, project, projects, excludeGenerated, symbolLimit, failOnRisk),
            cancellationToken);
    }

    [McpServerTool(Name = WrongSymbolGuardTool, Title = "Navlyn Wrong Symbol Guard", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
    [Description(WrongSymbolGuardDescription)]
    public static Task<CallToolResult> WrongSymbolGuard(
        IServiceProvider services,
        string? query = null,
        string? candidateId = null,
        string? file = null,
        int? line = null,
        int? column = null,
        string? assumeKind = null,
        string[]? assumeKinds = null,
        string? match = null,
        bool? caseSensitive = null,
        string? project = null,
        string[]? projects = null,
        bool? excludeGenerated = null,
        string? @base = null,
        string? head = null,
        bool? staged = null,
        bool? includeUnstaged = null,
        int? symbolLimit = null,
        string? failOnRisk = null,
        int? candidateLimit = null,
        string? candidatePolicy = null,
        string? minConfidence = null,
        bool? explainSelection = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            services,
            WrongSymbolGuardTool,
            NavlynToolCommandBuilder.WrongSymbolGuard(query, candidateId, file, line, column, assumeKind, assumeKinds, match, caseSensitive, project, projects, excludeGenerated, @base, head, staged, includeUnstaged, symbolLimit, failOnRisk, candidateLimit, candidatePolicy, minConfidence, explainSelection),
            cancellationToken);
    }

    [McpServerTool(Name = ChangeIntentPackTool, Title = "Navlyn Change Intent Pack", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
    [Description(AgentPackDescription)]
    public static Task<CallToolResult> ChangeIntentPack(IServiceProvider services, string? query = null, string? candidateId = null, string? file = null, int? line = null, int? column = null, string? assumeKind = null, string[]? assumeKinds = null, string? match = null, bool? caseSensitive = null, string? project = null, string[]? projects = null, bool? excludeGenerated = null, string? goal = null, string? changeKind = null, int? candidateLimit = null, string? candidatePolicy = null, string? minConfidence = null, bool? explainSelection = null, CancellationToken cancellationToken = default)
    {
        return RunAsync(services, ChangeIntentPackTool, NavlynToolCommandBuilder.AgentTargetPack("change-intent-pack", query, candidateId, file, line, column, assumeKind, assumeKinds, match, caseSensitive, project, projects, excludeGenerated, goal, changeKind, budgetTokens: null, itemLimit: null, referenceLimit: null, testLimit: null, candidateLimit, candidatePolicy, minConfidence, explainSelection), cancellationToken);
    }

    [McpServerTool(Name = AgentHandoffPackTool, Title = "Navlyn Agent Handoff Pack", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
    [Description(AgentPackDescription)]
    public static Task<CallToolResult> AgentHandoffPack(IServiceProvider services, string? query = null, string? candidateId = null, string? file = null, int? line = null, int? column = null, string? assumeKind = null, string[]? assumeKinds = null, string? match = null, bool? caseSensitive = null, string? project = null, string[]? projects = null, bool? excludeGenerated = null, string? goal = null, string? changeKind = null, int? candidateLimit = null, string? candidatePolicy = null, string? minConfidence = null, bool? explainSelection = null, CancellationToken cancellationToken = default)
    {
        return RunAsync(services, AgentHandoffPackTool, NavlynToolCommandBuilder.AgentTargetPack("agent-handoff-pack", query, candidateId, file, line, column, assumeKind, assumeKinds, match, caseSensitive, project, projects, excludeGenerated, goal, changeKind, budgetTokens: null, itemLimit: null, referenceLimit: null, testLimit: null, candidateLimit, candidatePolicy, minConfidence, explainSelection), cancellationToken);
    }

    [McpServerTool(Name = ConfidenceLedgerTool, Title = "Navlyn Confidence Ledger", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
    [Description(AgentPackDescription)]
    public static Task<CallToolResult> ConfidenceLedger(IServiceProvider services, string? query = null, string? candidateId = null, string? file = null, int? line = null, int? column = null, string? assumeKind = null, string[]? assumeKinds = null, string? match = null, bool? caseSensitive = null, string? project = null, string[]? projects = null, bool? excludeGenerated = null, string? goal = null, string? changeKind = null, int? candidateLimit = null, string? candidatePolicy = null, string? minConfidence = null, bool? explainSelection = null, CancellationToken cancellationToken = default)
    {
        return RunAsync(services, ConfidenceLedgerTool, NavlynToolCommandBuilder.AgentTargetPack("confidence-ledger", query, candidateId, file, line, column, assumeKind, assumeKinds, match, caseSensitive, project, projects, excludeGenerated, goal, changeKind, budgetTokens: null, itemLimit: null, referenceLimit: null, testLimit: null, candidateLimit, candidatePolicy, minConfidence, explainSelection), cancellationToken);
    }

    [McpServerTool(Name = BatchTool, Title = "Navlyn Batch", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
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

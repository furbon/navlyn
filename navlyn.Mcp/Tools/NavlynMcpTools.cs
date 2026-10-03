using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Navlyn.Mcp.Tools;

[McpServerToolType]
internal static class NavlynMcpTools
{
    public const string TargetTool = "navlyn_target";
    public const string ReadTool = "navlyn_read";
    public const string PrepareEditTool = "navlyn_prepare_edit";
    public const string VerifyEditTool = "navlyn_verify_edit";
    public const string ReviewTool = "navlyn_review";
    public const string WorkspaceSummaryTool = "navlyn_workspace_summary";
    public const string WorkspaceStatusTool = "navlyn_workspace_status";
    public const string WorkspaceRefreshTool = "navlyn_workspace_refresh";
    public const string DoctorTool = "navlyn_doctor";
    public const string FileOutlineTool = "navlyn_file_outline";
    public const string NavigateTool = "navlyn_navigate";
    public const string ImpactTool = "navlyn_impact";
    public const string EntrypointsTool = "navlyn_entrypoints";
    public const string TestsForSymbolTool = "navlyn_tests_for_symbol";
    public const string TestsForDiffTool = "navlyn_tests_for_diff";
    public const string DiagnosticsTool = "navlyn_diagnostics";
    public const string DiTool = "navlyn_di";
    public const string RoutesTool = "navlyn_routes";
    public const string OptionsTool = "navlyn_options";
    public const string MessagesTool = "navlyn_messages";
    public const string EfTool = "navlyn_ef";
    public const string PackagesTool = "navlyn_packages";
    public const string PublicApiDiffTool = "navlyn_public_api_diff";
    public const string ContextPackTool = "navlyn_context_pack";
    public const string BatchTool = "navlyn_batch";

    private const string TargetDescription =
        "Canonical first tool when approximate C# or Visual Basic symbol identity could change the answer. Use mode select normally; use mode list only for explicit broader candidate discovery. Select needs a query, candidateId, or exact source position; list needs a query. Do not use for comments, strings, docs, or text search. Stop at a selected target, candidate list, or unresolved ambiguity; results are static symbol evidence, not runtime behavior.";

    private const string ReadDescription =
        "Use after a target is known when its bounded C# or Visual Basic declaration or source is needed. Requires candidateId or an exact file/line/column. Do not use for broad file reading, repository search, diff review, or generated/non-Roslyn text. Returns static source, not runtime behavior.";

    private const string PrepareEditDescription =
        "Use immediately before editing one intended C# or Visual Basic target when bounded preparation evidence is needed. Provide candidateId, query, or exact source position. It resolves the target and gathers bounded source, context, and test evidence; do not use for edits or broad exploration. Confidence and known unknowns are static evidence, not a correctness guarantee.";

    private const string VerifyEditDescription =
        "Use as a post-edit diff-to-intent guard when checking whether the actual diff matches one intended C# or Visual Basic target. Provide exactly one saved preflight path, candidateId, query, or file/line/column. Do not use before editing or as a test runner; it does not edit files. A mismatch is evidence to inspect, not proof the edit is wrong; static guard facts do not prove runtime correctness.";

    private const string ReviewDescription =
        "Use first when the question concerns an actual Git diff, PR, staged changes, or working-tree changes. The Git diff is the required input; optional refs or project filters narrow it. Do not use for single-symbol reading or when no diff is in scope. Returns bounded static source-level changed-symbol, impact, diagnostic, and test facts, not runtime proof or an approval.";

    private const string WorkspaceSummaryDescription =
        "Use when comparing or reasoning across project structure, target frameworks, package references, test relationships, or MSBuild facts; it is not a default preamble. Project filters are optional. Do not use merely to read a declared value from one named project file, or for single-file review, a specific symbol, comments, strings, docs, or non-Roslyn files. Returns a static workspace snapshot, not build or runtime behavior; omitted profile is compact.";

    private const string WorkspaceStatusDescription =
        "Use first when workspace snapshot freshness, direct cache status, or the optional cache manifest is the question; no symbol anchor is needed. This lifecycle/status tool is not a default preamble or repository overview; use navlyn_workspace_summary for project graph facts. Reported state is static cache metadata, not proof that source builds or runs.";

    private const string WorkspaceRefreshDescription =
        "Use first only when a stale or missing workspace snapshot must be explicitly refreshed; no symbol anchor is needed. Do not use as a default preamble or to edit source. It reloads static workspace facts and may clear or write the lightweight cache manifest when requested; refresh is not a build and does not prove runtime behavior.";

    private const string DoctorDescription =
        "Use first during setup or after workspace-load failures to check the configured workspace, .NET SDK, target frameworks, load diagnostics, and safe next steps. Requires no symbol anchor. Do not use as a repository overview or routine preamble. Performs read-only environment checks; it does not build the project or establish runtime behavior.";

    private const string FileOutlineDescription =
        "Use when semantic structure in one known C# or Visual Basic file is needed before deeper symbol inspection. Requires a source-file path. Do not use for ordinary reading, tests, impact, repository overview, comments, strings, docs, non-Roslyn files, or command execution. Outline entries provide reusable candidateIds; they are static facts, not runtime behavior.";

    private const string ImpactDescription =
        "Use first when edit risk or impact around a selected C# or Visual Basic symbol is the question. Requires exactly one query or candidateId; profile light is the bounded default. Do not use for broad source reading or runtime investigation. Declarations and relationships are static and may be partial, not proof of reflection, DI, or configuration behavior. Escalate to navlyn_context_pack only when smaller facts are insufficient.";

    private const string EntrypointsDescription =
        "Use first to ask how a selected symbol is reached by callers, or to inspect framework-discovered entrypoints. Symbol mode accepts query or candidateId; framework mode uses framework discovery inputs. Do not use for full impact; use navlyn_impact. Results are bounded static/heuristic evidence, not proof of runtime reachability.";

    private const string NavigateDescription =
        "Use first for one precise definition, references, callers, calls, implementations, type hierarchy, or symbol-info fact about a known C# or Visual Basic target. Requires candidateId or exact source position plus an operation. Do not use for broad repository search or diff review. References and callers are expensive and may be partial; results are static relationships, not runtime call proof.";

    private const string TestsForSymbolDescription =
        "Use when planning or reviewing an edit and test candidates for one C# or Visual Basic symbol could change the decision. Requires candidateId, query, or exact source position. Do not use for first-pass comprehension or as a test runner; it returns candidates only and never executes tests. Matches are static and may be incomplete.";

    private const string TestsForDiffDescription =
        "Use first during PR or working-tree review when test candidates for changed C# or Visual Basic symbols are needed. Requires an actual diff or Git refs. Do not use for first-pass reading or as a test runner; it returns candidates only and never executes tests. Matches are bounded static evidence, not proof tests execute or pass.";

    private const string DiagnosticsDescription =
        "Use first when investigating compiler diagnostics already present in the loaded workspace. Requires mode workspace, symbol, or pack; add a project, diagnostic ID, candidateId, or exact source position as appropriate. Do not use for runtime exceptions, logs, edits, or fix suggestions. This is existing static diagnostic evidence; Navlyn does not run a build.";

    private const string DiDescription =
        "Use first for Microsoft.Extensions.DependencyInjection registration, dependency, risk, or consumer source patterns. Requires mode graph, registrations, or impact; target inputs apply to the latter modes. Do not use for runtime container inspection. Results are bounded static source facts, not runtime proof of registrations or resolution.";

    private const string RoutesDescription =
        "Use first for source-defined ASP.NET Core endpoints/auth patterns or the impact of one route pattern. Requires mode map or impact; impact requires a route pattern. Do not use for runtime route tables or effective authorization. Results are bounded static source evidence and do not read secrets or configuration values.";

    private const string OptionsDescription =
        "Use first for source-defined options registrations, bindings, consumers, validation, or impact of one options/configuration query. Requires mode graph or impact; impact requires a query. Do not use to inspect runtime configuration. Results are bounded static source evidence and do not read effective values or secrets.";

    private const string MessagesDescription =
        "Use first to find MediatR handler declarations or send/publish call sites for one message target. Requires mode handlers or flow plus candidateId, query, or exact source position. Do not use to trace runtime delivery or broker behavior. Results are bounded static source facts, not proof of dispatch or execution.";

    private const string EfDescription =
        "Use first for EF Core entity, DbContext, query-site source facts, or impact around one entity. Requires mode model or impact; impact needs an entity target. Do not use to inspect the runtime model, database schema/state, or execute queries. Results are bounded static source patterns, not runtime proof.";

    private const string PackagesDescription =
        "Use first for package-reference/namespace usage or source-level impact of one named package. Requires mode usage or impact and a package name. Do not use for dependency installation or security/license decisions. Results are bounded source facts, not proof of compatibility, runtime loading, vulnerabilities, or license compliance.";

    private const string PublicApiDiffDescription =
        "Use first for release or review questions about public/protected API changes between Git refs. Requires a base ref; head is optional. Do not use for runtime binary compatibility checks. Results are bounded source-level API facts and do not prove binary or behavioral compatibility.";

    private const string ContextPackDescription =
        "Use only as an escalation when ordinary reads and smaller Navlyn facts are insufficient and a bounded reading queue is needed for review, modification, or explanation. Requires query, candidateId, or diff input; goal and changeKind guide ranking. Do not use to list candidates or as a default first step; start with target, read, review, or precise navigation. The pack is static context, not a complete analysis.";

    private const string BatchDescription =
        "Advanced optimization for two or more already-selected, batch-supported facts from the same workspace. Prefer focused MCP tools for a single fact. Do not use batch for initial discovery or as a checklist.";

    [McpServerTool(Name = WorkspaceSummaryTool, Title = "Navlyn Workspace Summary", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
    [Description(WorkspaceSummaryDescription)]
    public static Task<CallToolResult> WorkspaceSummary(
        IServiceProvider services,
        [Description("Single project filter by name or repository-relative .csproj/.vbproj path. Mutually exclusive with projects.")] string? project = null,
        [Description("Project filters by name or repository-relative .csproj/.vbproj path. Mutually exclusive with project.")] string[]? projects = null,
        [Description("Whether to include package references. Omit to use CLI default.")] bool? includePackages = null,
        [Description("Whether to include repository MSBuild files. Omit to use CLI default.")] bool? includeMsbuildFiles = null,
        [Description("Whether to include preprocessor symbols. Omit to use CLI default.")] bool? includePreprocessorSymbols = null,
        [Description("Whether to include project classification facts. Omit to use CLI default.")] bool? classification = null,
        [Description("Maximum inferred relationships. Must be 1 or greater.")] int? relationshipLimit = null,
        [Description("Output profile: compact, evidence, or full. Omitted defaults to compact for this MCP tool.")] string? profile = null,
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

    [McpServerTool(Name = TargetTool, Title = "Navlyn Target", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
    [Description(TargetDescription)]
    public static Task<CallToolResult> Target(
        IServiceProvider services,
        [Description("Target mode: select (default) resolves one target; list returns candidates without selecting one and requires query.")] string? mode = "select",
        [Description("Approximate symbol name query. Required for list mode; mutually exclusive with candidateId and file/line/column in select mode.")] string? query = null,
        [Description("Candidate id from a previous Navlyn result. Select mode only; mutually exclusive with query and file/line/column.")] string? candidateId = null,
        [Description("C# or Visual Basic source file target. Select mode only; provide with line and column when query and candidateId are omitted.")] string? file = null,
        [Description("1-based source line. Select mode only; provide with file and column for source-position mode.")] int? line = null,
        [Description("1-based source column. Select mode only; provide with file and line for source-position mode.")] int? column = null,
        [Description("Single case-insensitive symbol kind hint, e.g. class/interface/struct/record/enum/delegate (NamedType), method, property, field, or event. Query mode only; mutually exclusive with assumeKinds.")] string? assumeKind = null,
        [Description("Case-insensitive symbol kind hints; natural type names map to NamedType. Query mode only; mutually exclusive with assumeKind.")] string[]? assumeKinds = null,
        [Description("Query match mode: smart, exact, contains, or regex.")] string? match = null,
        [Description("Use case-sensitive query matching.")] bool? caseSensitive = null,
        [Description("Single project filter. Mutually exclusive with projects.")] string? project = null,
        [Description("Project filters. Mutually exclusive with project.")] string[]? projects = null,
        [Description("Exclude generated code candidates.")] bool? excludeGenerated = null,
        [Description("Candidate limit. Must be 1 or greater.")] int? limit = null,
        [Description("Candidate policy: fail or select in select mode; in list mode, omit it or use group to return candidates without selecting one.")] string? candidatePolicy = null,
        [Description("Minimum confidence: high, medium, or low.")] string? minConfidence = null,
        [Description("Include selection explanation in the CLI result.")] bool? explainSelection = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            services,
            TargetTool,
            NavlynToolCommandBuilder.Target(mode, query, candidateId, file, line, column, assumeKind, assumeKinds, match, caseSensitive, project, projects, excludeGenerated, limit, candidatePolicy, minConfidence, explainSelection),
            cancellationToken);
    }

    [McpServerTool(Name = ReadTool, Title = "Navlyn Read", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
    [Description(ReadDescription)]
    public static Task<CallToolResult> Read(
        IServiceProvider services,
        [Description("Candidate id from navlyn_target or another Navlyn tool. Mutually exclusive with file/line/column.")] string? candidateId = null,
        [Description("C# or Visual Basic source file. Must be provided with line and column when candidateId is omitted.")] string? file = null,
        [Description("1-based source line.")] int? line = null,
        [Description("1-based source column.")] int? column = null,
        [Description("Optional project context for candidate/source-position resolution.")] string? project = null,
        [Description("Exclude generated source locations.")] bool? excludeGenerated = null,
        [Description("Source view: signature, declaration, body, members, xml-doc, or attributes.")] string? view = null,
        [Description("Maximum source lines per slice. Must be 1 or greater.")] int? maxLines = null,
        [Description("Approximate token budget per slice. Must be 1 or greater.")] int? budgetTokens = null,
        [Description("External member source: none (default), metadata, or decompiled.")] string? externalSource = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            services,
            ReadTool,
            NavlynToolCommandBuilder.Read(candidateId, file, line, column, project, excludeGenerated, view, maxLines, budgetTokens, externalSource),
            cancellationToken);
    }

    [McpServerTool(Name = PrepareEditTool, Title = "Navlyn Prepare Edit", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
    [Description(PrepareEditDescription)]
    public static Task<CallToolResult> PrepareEdit(
        IServiceProvider services,
        [Description("Approximate symbol name for the intended edit target. Mutually exclusive with candidateId and file/line/column.")] string? query = null,
        [Description("Candidate id from navlyn_target or another Navlyn tool. Mutually exclusive with query and file/line/column.")] string? candidateId = null,
        [Description("C# or Visual Basic source file for exact source-position target mode. Must be provided with line and column.")] string? file = null,
        [Description("1-based source line for exact source-position target mode. Must be provided with file and column.")] int? line = null,
        [Description("1-based source column for exact source-position target mode. Must be provided with file and line.")] int? column = null,
        [Description("Single Roslyn SymbolKind hint for query mode, such as NamedType or Method. Mutually exclusive with assumeKinds.")] string? assumeKind = null,
        [Description("Roslyn SymbolKind hints for query mode. Mutually exclusive with assumeKind.")] string[]? assumeKinds = null,
        [Description("Query match mode: smart, exact, contains, or regex. Omit for smart matching.")] string? match = null,
        [Description("Whether query matching is case-sensitive. Omit for Navlyn default.")] bool? caseSensitive = null,
        [Description("Single input project context by project name or repository-relative .csproj/.vbproj path. Mutually exclusive with projects.")] string? project = null,
        [Description("Input project contexts by name or repository-relative .csproj/.vbproj path. Mutually exclusive with project.")] string[]? projects = null,
        [Description("Reject generated source targets and exclude generated evidence where supported.")] bool? excludeGenerated = null,
        [Description("Edit planning goal, such as modify, review, or understand. Use modify before changing one target.")] string? goal = null,
        [Description("Edit ranking hint, such as behavior, signature, rename, constructor, nullability, async, public-api, di-registration, or endpoint.")] string? changeKind = null,
        [Description("Approximate token budget for bounded context evidence. Omit for command default.")] int? budgetTokens = null,
        [Description("Maximum number of context items to include. Must be 1 or greater when provided.")] int? itemLimit = null,
        [Description("Maximum reference evidence items to include. Must be 1 or greater when provided.")] int? referenceLimit = null,
        [Description("Maximum related test candidates to include. Must be 1 or greater when provided.")] int? testLimit = null,
        [Description("Maximum fuzzy target candidates to consider before selecting or reporting ambiguity. Must be 1 or greater when provided.")] int? candidateLimit = null,
        [Description("Candidate selection policy for ambiguous query mode: fail or select. Omit for Navlyn default.")] string? candidatePolicy = null,
        [Description("Minimum confidence required for automatic target selection. Omit for Navlyn default.")] string? minConfidence = null,
        [Description("For query or candidate target modes, include rank inputs, reason codes, and ambiguity reasons. Omit for exact source-position mode.")] bool? explainSelection = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            services,
            PrepareEditTool,
            NavlynToolCommandBuilder.PrepareEdit(query, candidateId, file, line, column, assumeKind, assumeKinds, match, caseSensitive, project, projects, excludeGenerated, goal, changeKind, budgetTokens, itemLimit, referenceLimit, testLimit, candidateLimit, candidatePolicy, minConfidence, explainSelection),
            cancellationToken);
    }

    [McpServerTool(Name = VerifyEditTool, Title = "Navlyn Verify Edit", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
    [Description(VerifyEditDescription)]
    public static Task<CallToolResult> VerifyEdit(
        IServiceProvider services,
        [Description("Approximate intended symbol query. Mutually exclusive with preflight, candidateId, and source-position fields; supports fuzzy narrowing fields.")] string? query = null,
        [Description("Candidate id for the intended pre-edit target. Anchor mode; mutually exclusive with preflight, query, and source-position fields. Fuzzy-selection options are not supported.")] string? candidateId = null,
        [Description("Path to a saved prepare-edit JSON file containing the intended anchor. Anchor mode; mutually exclusive with candidateId, query, and source-position fields. Fuzzy-selection options are not supported.")] string? preflight = null,
        [Description("Exact C# or Visual Basic source file for source-position mode. Provide with line and column; mutually exclusive with preflight, candidateId, and query.")] string? file = null,
        [Description("1-based source line. Required with file and column for source-position mode.")] int? line = null,
        [Description("1-based source column. Required with file and line for source-position mode.")] int? column = null,
        [Description("Symbol kind narrowing for query mode only; mutually exclusive with anchor modes and source-position mode.")] string? assumeKind = null,
        [Description("Symbol kind narrowing values for query mode only; mutually exclusive with anchor modes and source-position mode.")] string[]? assumeKinds = null,
        [Description("Query match mode: smart, exact, contains, or regex. Query mode only.")] string? match = null,
        [Description("Require case-sensitive matching. Query mode only.")] bool? caseSensitive = null,
        [Description("Base Git ref for the diff comparison. Omit to use working-tree mode.")] string? @base = null,
        [Description("Head Git ref for the diff comparison. Omit to use working-tree mode.")] string? head = null,
        [Description("Compare staged changes only. Mutually interacts with includeUnstaged according to CLI diff rules.")] bool? staged = null,
        [Description("Include unstaged working-tree changes. Omit for command default.")] bool? includeUnstaged = null,
        [Description("Single project filter by name or repository-relative .csproj/.vbproj path. Mutually exclusive with projects.")] string? project = null,
        [Description("Project filters by name or repository-relative .csproj/.vbproj path. Mutually exclusive with project.")] string[]? projects = null,
        [Description("Exclude generated source files from changed-symbol evidence where supported.")] bool? excludeGenerated = null,
        [Description("Maximum changed symbols to inspect. Must be 1 or greater when provided.")] int? symbolLimit = null,
        [Description("Fail policy threshold: low, medium, or high. The tool returns deterministic JSON even when policy fails.")] string? failOnRisk = null,
        [Description("Maximum query candidates to consider. Query mode only; must be 1 or greater.")] int? candidateLimit = null,
        [Description("Candidate policy for query mode: fail or select. Anchor and source-position modes do not support this option.")] string? candidatePolicy = null,
        [Description("Minimum query candidate confidence: high, medium, or low. Query mode only.")] string? minConfidence = null,
        [Description("Include why a query candidate was selected. Query mode only.")] bool? explainSelection = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            services,
            VerifyEditTool,
            NavlynToolCommandBuilder.VerifyEdit(query, candidateId, preflight, file, line, column, assumeKind, assumeKinds, match, caseSensitive, @base, head, staged, includeUnstaged, project, projects, excludeGenerated, symbolLimit, failOnRisk, candidateLimit, candidatePolicy, minConfidence, explainSelection),
            cancellationToken);
    }

    [McpServerTool(Name = ReviewTool, Title = "Navlyn Review", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
    [Description(ReviewDescription)]
    public static Task<CallToolResult> Review(
        IServiceProvider services,
        [Description("Base Git ref for review. Omit to review working-tree changes.")] string? @base = null,
        [Description("Head Git ref for review. Omit to review working-tree changes.")] string? head = null,
        [Description("Review staged changes only. Mutually interacts with includeUnstaged according to CLI diff rules.")] bool? staged = null,
        [Description("Include unstaged working-tree changes. Omit for command default.")] bool? includeUnstaged = null,
        [Description("Single project filter by name or repository-relative .csproj/.vbproj path. Mutually exclusive with projects.")] string? project = null,
        [Description("Project filters by name or repository-relative .csproj/.vbproj path. Mutually exclusive with project.")] string[]? projects = null,
        [Description("Exclude generated source files from review facts where supported.")] bool? excludeGenerated = null,
        [Description("Maximum changed symbols to report. Must be 1 or greater when provided.")] int? symbolLimit = null,
        [Description("Maximum impact facts to report. Must be 1 or greater when provided.")] int? impactLimit = null,
        [Description("Maximum diagnostics to report. Must be 1 or greater when provided.")] int? diagnosticLimit = null,
        [Description("Maximum related test candidates to report. Must be 1 or greater when provided.")] int? relatedTestLimit = null,
        [Description("Static impact depth for review facts. Must be 1 or greater when provided.")] int? depth = null,
        [Description("Include bounded source snippets in review facts. Omit to keep evidence compact.")] bool? includeSnippets = null,
        [Description("Number of context lines per snippet when snippets are included. Must be 0 or greater when provided.")] int? snippetLines = null,
        [Description("Output profile: compact, evidence, or full. Omitted defaults to evidence for this MCP tool.")] string? profile = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            services,
            ReviewTool,
            NavlynToolCommandBuilder.Review(@base, head, staged, includeUnstaged, project, projects, excludeGenerated, symbolLimit, impactLimit, diagnosticLimit, relatedTestLimit, depth, includeSnippets, snippetLines, profile),
            cancellationToken);
    }

    [McpServerTool(Name = FileOutlineTool, Title = "Navlyn File Outline", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
    [Description(FileOutlineDescription)]
    public static Task<CallToolResult> FileOutline(
        IServiceProvider services,
        [Description("C# or Visual Basic source file to outline. Required.")] string file,
        [Description("Input project context by project name or repository-relative .csproj/.vbproj path.")] string? project = null,
        [Description("Exclude generated source files.")] bool? excludeGenerated = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            services,
            FileOutlineTool,
            NavlynToolCommandBuilder.FileOutline(file, project, excludeGenerated),
            cancellationToken);
    }

    [McpServerTool(Name = ImpactTool, Title = "Navlyn Impact", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
    [Description(ImpactDescription)]
    public static Task<CallToolResult> Impact(
        IServiceProvider services,
        [Description("Approximate symbol name query; mutually exclusive with candidateId.")] string? query = null,
        [Description("Previously selected symbol candidate id; mutually exclusive with query.")] string? candidateId = null,
        [Description("Assumed symbol kind for query matching. Mutually exclusive with assumeKinds; not used for candidateId.")] string? assumeKind = null,
        [Description("Assumed symbol kinds for query matching. Mutually exclusive with assumeKind; not used for candidateId.")] string[]? assumeKinds = null,
        [Description("Query matching mode: smart, exact, contains, or regex. Not used for candidateId.")] string? match = null,
        [Description("Use case-sensitive query matching. Not used for candidateId.")] bool? caseSensitive = null,
        [Description("Single project filter. Mutually exclusive with projects.")] string? project = null,
        [Description("Multiple project filters. Mutually exclusive with project.")] string[]? projects = null,
        [Description("Exclude generated source from static impact facts.")] bool? excludeGenerated = null,
        [Description("Comma-separated impact include modes: references, callers, calls, implementations, or hierarchy. Omit to use the selected profile's defaults.")] string? include = null,
        [Description("Maximum impact facts to return; must be 1 or greater when provided.")] int? limit = null,
        [Description("Maximum graph traversal depth; must be 0 or greater when provided.")] int? depth = null,
        [Description("Include bounded source snippets in impact evidence.")] bool? includeSnippets = null,
        [Description("Maximum source context lines per snippet; a nonnegative line count.")] int? snippetLines = null,
        [Description("Search scope for heavy references/callers: file, project, dependent-projects, workspace-set, or solution.")] string? scope = null,
        [Description("Maximum lexically matching documents for heavy references/callers. Must be 1 or greater.")] int? maxDocuments = null,
        [Description("Workflow profile: light or full. Omitted defaults to light, which favors declarations and local calls.")] string? profile = null,
        [Description("Candidate policy for query selection: fail or select. Not used for candidateId.")] string? candidatePolicy = null,
        [Description("Minimum query candidate confidence: high, medium, or low. Not used for candidateId.")] string? minConfidence = null,
        [Description("Include query candidate-selection rationale. Not used for candidateId.")] bool? explainSelection = null,
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
        [Description("Entrypoint mode: symbol or framework. Omit to infer symbol when query/candidateId is supplied, otherwise framework.")] string? mode = null,
        [Description("Approximate symbol query for symbol mode; mutually exclusive with candidateId and not valid in framework mode.")] string? query = null,
        [Description("Previously selected symbol candidate id for symbol mode; mutually exclusive with query and not valid in framework mode.")] string? candidateId = null,
        [Description("Assumed symbol kind for query matching. Mutually exclusive with assumeKinds; symbol mode only.")] string? assumeKind = null,
        [Description("Assumed symbol kinds for query matching. Mutually exclusive with assumeKind; symbol mode only.")] string[]? assumeKinds = null,
        [Description("Query matching mode: smart, exact, contains, or regex. Symbol mode only.")] string? match = null,
        [Description("Use case-sensitive query matching. Symbol mode only.")] bool? caseSensitive = null,
        [Description("Single project filter. Mutually exclusive with projects.")] string? project = null,
        [Description("Multiple project filters. Mutually exclusive with project.")] string[]? projects = null,
        [Description("Exclude generated source from entrypoint facts.")] bool? excludeGenerated = null,
        [Description("Framework filter(s): aspnetcore, test, or worker. Framework mode scans these families; in symbol mode a supplied framework enables framework-aware annotations.")] string? framework = null,
        [Description("Maximum entrypoint facts or chains to return; must be 1 or greater when provided.")] int? limit = null,
        [Description("Maximum static caller-chain depth; must be 0 or greater when provided.")] int? depth = null,
        [Description("Include bounded source snippets in entrypoint evidence.")] bool? includeSnippets = null,
        [Description("Maximum source context lines per snippet; a nonnegative line count.")] int? snippetLines = null,
        [Description("Candidate policy for symbol-query selection: fail or select. Not used in framework mode or with candidateId.")] string? candidatePolicy = null,
        [Description("Minimum query candidate confidence: high, medium, or low. Symbol mode only.")] string? minConfidence = null,
        [Description("Include query candidate-selection rationale. Symbol mode only.")] bool? explainSelection = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            services,
            EntrypointsTool,
            NavlynToolCommandBuilder.Entrypoints(mode, query, candidateId, assumeKind, assumeKinds, match, caseSensitive, project, projects, excludeGenerated, framework, limit, depth, includeSnippets, snippetLines, candidatePolicy, minConfidence, explainSelection),
            cancellationToken);
    }

    [McpServerTool(Name = NavigateTool, Title = "Navlyn Navigate", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
    [Description(NavigateDescription)]
    public static Task<CallToolResult> Navigate(
        IServiceProvider services,
        [Description("Operation: definition, references, callers, calls, implementations, type_hierarchy, or symbol_info.")] string operation,
        [Description("Candidate id returned by navlyn_target or navlyn_file_outline. Mutually exclusive with file/line/column.")] string? candidateId = null,
        [Description("C# or Visual Basic source file target. Must be provided with line and column when candidateId is omitted.")] string? file = null,
        [Description("1-based source line. Must be provided with file and column when candidateId is omitted.")] int? line = null,
        [Description("1-based source column. Must be provided with file and line when candidateId is omitted.")] int? column = null,
        [Description("Input project context by project name or repository-relative .csproj/.vbproj path.")] string? project = null,
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
            NavigateTool,
            NavlynToolCommandBuilder.Navigate(operation, candidateId, file, line, column, project, excludeGenerated, resultProject, resultProjects, resultPath, resultPaths, resultKind, resultKinds, usageKind, usageKinds, groupBy, limit, scope, maxDocuments, includeMetadata),
            cancellationToken);
    }

    [McpServerTool(Name = TestsForSymbolTool, Title = "Navlyn Tests For Symbol", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
    [Description(TestsForSymbolDescription)]
    public static Task<CallToolResult> TestsForSymbol(
        IServiceProvider services,
        [Description("Approximate symbol-name query; provide exactly one of query, candidateId, or source position.")] string? query = null,
        [Description("Previously selected symbol candidate id; mutually exclusive with query and source position.")] string? candidateId = null,
        [Description("C# or Visual Basic source file for exact target mode; requires line and column.")] string? file = null,
        [Description("1-based source line; requires file and column.")] int? line = null,
        [Description("1-based source column; requires file and line.")] int? column = null,
        [Description("Assumed symbol kind for query matching. Mutually exclusive with assumeKinds; query mode only.")] string? assumeKind = null,
        [Description("Assumed symbol kinds for query matching. Mutually exclusive with assumeKind; query mode only.")] string[]? assumeKinds = null,
        [Description("Query matching mode: smart, exact, contains, or regex; query mode only.")] string? match = null,
        [Description("Use case-sensitive query matching; query mode only.")] bool? caseSensitive = null,
        [Description("Single source project filter. Mutually exclusive with projects; source-position mode accepts at most one project.")] string? project = null,
        [Description("Source project filters. Mutually exclusive with project; source-position mode accepts at most one project.")] string[]? projects = null,
        [Description("Single test project filter. Mutually exclusive with testProjects.")] string? testProject = null,
        [Description("Test project filters. Mutually exclusive with testProject.")] string[]? testProjects = null,
        [Description("Exclude generated source from symbol and test facts.")] bool? excludeGenerated = null,
        [Description("Maximum fuzzy candidates to consider; must be 1 or greater when provided. Not used for source-position mode.")] int? candidateLimit = null,
        [Description("Maximum related test facts; must be 1 or greater when provided.")] int? testLimit = null,
        [Description("Maximum references scanned; must be 1 or greater when provided.")] int? referenceLimit = null,
        [Description("Include bounded source snippets for related test facts.")] bool? includeSnippets = null,
        [Description("Maximum source context lines per snippet; must be 0 or greater.")] int? snippetLines = null,
        [Description("Candidate policy for query or candidate selection: fail or select. Not supported for source-position mode.")] string? candidatePolicy = null,
        [Description("Minimum candidate confidence: high, medium, or low. Not supported for source-position mode.")] string? minConfidence = null,
        [Description("Include candidate-selection rationale where supported; not supported for source-position mode.")] bool? explainSelection = null,
        [Description("Output profile: compact, evidence, or full. Omitted defaults to compact for this MCP tool.")] string? profile = null,
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
        [Description("Base Git ref for diff analysis. Omit to use the CLI's working-tree diff behavior.")] string? @base = null,
        [Description("Head Git ref for diff analysis; requires a compatible base ref when supplied.")] string? head = null,
        [Description("Limit diff analysis to staged changes according to CLI diff rules.")] bool? staged = null,
        [Description("Include unstaged changes according to CLI diff rules.")] bool? includeUnstaged = null,
        [Description("Source project filter. Mutually exclusive with projects.")] string? project = null,
        [Description("Source project filters. Mutually exclusive with project.")] string[]? projects = null,
        [Description("Single test project filter. Mutually exclusive with testProjects.")] string? testProject = null,
        [Description("Test project filters. Mutually exclusive with testProject.")] string[]? testProjects = null,
        [Description("Exclude generated source from changed-symbol and test facts.")] bool? excludeGenerated = null,
        [Description("Maximum changed symbols to inspect; must be 1 or greater when provided.")] int? symbolLimit = null,
        [Description("Maximum related test facts; must be 1 or greater when provided.")] int? testLimit = null,
        [Description("Maximum references scanned; must be 1 or greater when provided.")] int? referenceLimit = null,
        [Description("Include bounded source snippets for related test facts.")] bool? includeSnippets = null,
        [Description("Maximum source context lines per snippet; must be 0 or greater.")] int? snippetLines = null,
        [Description("Output profile: compact, evidence, or full. Omitted defaults to compact for this MCP tool.")] string? profile = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            services,
            TestsForDiffTool,
            NavlynToolCommandBuilder.TestsForDiff(@base, head, staged, includeUnstaged, project, projects, testProject, testProjects, excludeGenerated, symbolLimit, testLimit, referenceLimit, includeSnippets, snippetLines, profile),
            cancellationToken);
    }

    [McpServerTool(Name = DiagnosticsTool, Title = "Navlyn Diagnostics", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
    [Description(DiagnosticsDescription)]
    public static Task<CallToolResult> Diagnostics(
        IServiceProvider services,
        [Description("Required diagnostics operation: workspace, symbol, or pack.")] string mode,
        [Description("Single project filter. Mutually exclusive with projects in workspace mode; optional context in symbol and pack modes.")] string? project = null,
        [Description("Multiple project filters for workspace mode only. Mutually exclusive with project.")] string[]? projects = null,
        [Description("Exclude generated source diagnostics where supported.")] bool? excludeGenerated = null,
        [Description("One severity filter: Hidden, Info, Warning, or Error. Mutually exclusive with severities.")] string? severity = null,
        [Description("Severity filters: Hidden, Info, Warning, or Error. Mutually exclusive with severity.")] string[]? severities = null,
        [Description("Maximum diagnostics to return. Must be 1 or greater; omitted uses the CLI default.")] int? limit = null,
        [Description("One exact diagnostic id filter in workspace/symbol mode, or the required diagnostic-pack input in pack mode. Mutually exclusive with diagnosticIds.")] string? diagnosticId = null,
        [Description("Multiple exact diagnostic id filters in workspace/symbol mode only. Mutually exclusive with diagnosticId.")] string[]? diagnosticIds = null,
        [Description("Selected symbol candidate id for symbol mode. Mutually exclusive with file/line/column; not supported in workspace or pack mode.")] string? candidateId = null,
        [Description("C# or Visual Basic source file for symbol source-position mode or pack input mode. Must be provided with line and column.")] string? file = null,
        [Description("1-based source line for source-position mode. Must be provided with file and column.")] int? line = null,
        [Description("1-based source column for source-position mode. Must be provided with file and line.")] int? column = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            services,
            DiagnosticsTool,
            NavlynToolCommandBuilder.Diagnostics(mode, project, projects, excludeGenerated, severity, severities, limit, diagnosticId, diagnosticIds, candidateId, file, line, column),
            cancellationToken);
    }

    [McpServerTool(Name = DiTool, Title = "Navlyn Dependency Injection", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
    [Description(DiDescription)]
    public static Task<CallToolResult> Di(
        IServiceProvider services,
        [Description("Required dependency-injection operation: graph, registrations, or impact.")] string mode,
        [Description("Approximate type query for registrations or impact mode; mutually exclusive with candidateId and source position.")] string? query = null,
        [Description("Selected type candidate id for registrations or impact mode; mutually exclusive with query and source position. Query-only assumeKind, assumeKinds, match, and caseSensitive are unsupported with candidateId.")] string? candidateId = null,
        [Description("C# or Visual Basic source file for registrations/impact source-position mode. Must be provided with line and column; only one project is allowed.")] string? file = null,
        [Description("1-based source line for source-position mode. Must be provided with file and column.")] int? line = null,
        [Description("1-based source column for source-position mode. Must be provided with file and line.")] int? column = null,
        [Description("Single Roslyn SymbolKind query hint, such as NamedType. Query mode only; mutually exclusive with assumeKinds.")] string? assumeKind = null,
        [Description("Roslyn SymbolKind query hints. Query mode only; mutually exclusive with assumeKind.")] string[]? assumeKinds = null,
        [Description("Query match mode: smart, exact, contains, or regex. Query mode only.")] string? match = null,
        [Description("Whether query matching is case-sensitive. Query mode only.")] bool? caseSensitive = null,
        [Description("Fuzzy candidate policy: fail or select. Applies to query/candidate selection, not graph or source-position mode.")] string? candidatePolicy = null,
        [Description("Minimum fuzzy-selection confidence: high, medium, or low.")] string? minConfidence = null,
        [Description("Include fuzzy selection reasoning in the result.")] bool? explainSelection = null,
        [Description("Maximum fuzzy candidates. Must be 1 or greater; query/candidate modes only.")] int? candidateLimit = null,
        [Description("Single project filter. Mutually exclusive with projects; source-position mode accepts at most one project.")] string? project = null,
        [Description("Multiple project filters for graph or fuzzy query/candidate mode. Mutually exclusive with project; source-position mode accepts at most one project.")] string[]? projects = null,
        [Description("Exclude generated source registrations where supported.")] bool? excludeGenerated = null,
        [Description("Maximum registrations. Must be 1 or greater.")] int? registrationLimit = null,
        [Description("Maximum constructor dependency edges. Must be 1 or greater.")] int? dependencyLimit = null,
        [Description("Maximum risk facts. Must be 1 or greater; graph and impact modes only.")] int? riskLimit = null,
        [Description("Maximum consumers. Must be 1 or greater; impact mode only.")] int? consumerLimit = null,
        [Description("Constructor dependency traversal depth. Must be 0 or greater; impact mode only.")] int? depth = null,
        [Description("Include options registrations in graph mode. Explicit false disables them; graph mode only.")] bool? includeOptions = null,
        [Description("Include hosted-service registrations in graph mode. Explicit false disables them; graph mode only.")] bool? includeHostedServices = null,
        [Description("Include conservative DI risk facts in graph mode. Explicit false disables them; graph mode only.")] bool? includeRisks = null,
        [Description("Include bounded source snippets where supported.")] bool? includeSnippets = null,
        [Description("Maximum source snippet lines. Must be 0 or greater.")] int? snippetLines = null,
        [Description("Output profile: compact, evidence, or full. Omitted defaults to compact for this MCP tool.")] string? profile = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            services,
            DiTool,
            NavlynToolCommandBuilder.Di(mode, query, candidateId, file, line, column, assumeKind, assumeKinds, match, caseSensitive, candidatePolicy, minConfidence, explainSelection, candidateLimit, project, projects, excludeGenerated, registrationLimit, dependencyLimit, riskLimit, consumerLimit, depth, includeOptions, includeHostedServices, includeRisks, includeSnippets, snippetLines, profile),
            cancellationToken);
    }

    [McpServerTool(Name = RoutesTool, Title = "Navlyn Routes", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
    [Description(RoutesDescription)]
    public static Task<CallToolResult> Routes(
        IServiceProvider services,
        [Description("Required route operation: map or impact.")] string mode,
        [Description("One route pattern for impact mode only; required and nonblank in impact mode. Mutually exclusive with routes.")] string? route = null,
        [Description("Route pattern fragments for map mode only; repeated as CLI route filters. Mutually exclusive with route.")] string[]? routes = null,
        [Description("Map-only endpoint kind filters: any, controller-action, or minimal-api. Can be repeated.")] string[]? endpointKinds = null,
        [Description("Map-only source auth filter: any, required, anonymous, or unknown. Omit to preserve the CLI default.")] string? auth = null,
        [Description("Single project filter. Mutually exclusive with projects.")] string? project = null,
        [Description("Project filters. Mutually exclusive with project.")] string[]? projects = null,
        [Description("Exclude generated source where supported.")] bool? excludeGenerated = null,
        [Description("Maximum route facts; must be 1 or greater. Omit to preserve the CLI default.")] int? routeLimit = null,
        [Description("Maximum evidence items per fact; must be 1 or greater. Omit to preserve the CLI default.")] int? evidenceLimit = null,
        [Description("Include bounded source snippets where supported.")] bool? includeSnippets = null,
        [Description("Maximum source snippet lines; must be 0 or greater.")] int? snippetLines = null,
        [Description("Output profile: compact, evidence, or full. Omitted defaults to compact for this MCP tool.")] string? profile = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            services,
            RoutesTool,
            NavlynToolCommandBuilder.Routes(mode, route, routes, endpointKinds, auth, project, projects, excludeGenerated, routeLimit, evidenceLimit, includeSnippets, snippetLines, profile),
            cancellationToken);
    }

    [McpServerTool(Name = OptionsTool, Title = "Navlyn Options and Configuration", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
    [Description(OptionsDescription)]
    public static Task<CallToolResult> Options(
        IServiceProvider services,
        [Description("Required options operation: graph or impact.")] string mode,
        [Description("Optional option type or configuration key query for graph; required and nonblank for impact.")] string? query = null,
        [Description("Single project filter. Mutually exclusive with projects.")] string? project = null,
        [Description("Project filters. Mutually exclusive with project.")] string[]? projects = null,
        [Description("Exclude generated source.")] bool? excludeGenerated = null,
        [Description("Maximum option type facts; must be 1 or greater. Omit to preserve the CLI default.")] int? optionLimit = null,
        [Description("Maximum option consumer facts; must be 1 or greater. Omit to preserve the CLI default.")] int? consumerLimit = null,
        [Description("Maximum binding or validation facts; must be 1 or greater. Omit to preserve the CLI default.")] int? bindingLimit = null,
        [Description("Maximum evidence items per fact; must be 1 or greater. Omit to preserve the CLI default.")] int? evidenceLimit = null,
        [Description("Include bounded source snippets.")] bool? includeSnippets = null,
        [Description("Maximum source snippet lines; must be 0 or greater.")] int? snippetLines = null,
        [Description("Output profile: compact, evidence, or full. Omitted defaults to compact for this MCP tool.")] string? profile = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            services,
            OptionsTool,
            NavlynToolCommandBuilder.Options(mode, query, project, projects, excludeGenerated, optionLimit, consumerLimit, bindingLimit, evidenceLimit, includeSnippets, snippetLines, profile),
            cancellationToken);
    }

    [McpServerTool(Name = MessagesTool, Title = "Navlyn Messages", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
    [Description(MessagesDescription)]
    public static Task<CallToolResult> Messages(
        IServiceProvider services,
        [Description("Required MediatR operation: handlers or flow.")] string mode,
        [Description("Approximate message type query. Provide exactly one of query, candidateId, or file with line and column.")] string? query = null,
        [Description("Selected message symbol candidate id. Mutually exclusive with query and source position; query-only fuzzy narrowing fields are not allowed.")] string? candidateId = null,
        [Description("C# or Visual Basic source file for exact source-position targeting; requires line and column.")] string? file = null,
        [Description("1-based source line; requires file and column.")] int? line = null,
        [Description("1-based source column; requires file and line.")] int? column = null,
        [Description("Query-only assumed symbol kind. Mutually exclusive with assumeKinds; not allowed with candidateId or source position.")] string? assumeKind = null,
        [Description("Query-only assumed symbol kinds. Mutually exclusive with assumeKind; not allowed with candidateId or source position.")] string[]? assumeKinds = null,
        [Description("Query-only matching mode: smart, exact, contains, or regex; not allowed with candidateId or source position.")] string? match = null,
        [Description("Query-only case-sensitive matching; not allowed with candidateId or source position.")] bool? caseSensitive = null,
        [Description("Candidate selection policy: fail or select. Candidate-selection option supported with query or candidateId, not source position.")] string? candidatePolicy = null,
        [Description("Minimum candidate confidence: high, medium, or low. Candidate-selection option supported with query or candidateId, not source position.")] string? minConfidence = null,
        [Description("Include candidate-selection explanation where supported; not allowed with source position.")] bool? explainSelection = null,
        [Description("Single project filter. Mutually exclusive with projects; source position accepts at most one project.")] string? project = null,
        [Description("Project filters. Mutually exclusive with project; source position accepts at most one project.")] string[]? projects = null,
        [Description("Exclude generated source.")] bool? excludeGenerated = null,
        [Description("Maximum fuzzy candidates; must be 1 or greater. Maps to the CLI candidate limit.")] int? candidateLimit = null,
        [Description("Maximum handler facts; must be 1 or greater. Omit to preserve the CLI default.")] int? handlerLimit = null,
        [Description("Maximum send/publish call-site facts; flow mode only, must be 1 or greater. Handlers mode rejects it.")] int? callSiteLimit = null,
        [Description("Maximum evidence items per fact; must be 1 or greater. Omit to preserve the CLI default.")] int? evidenceLimit = null,
        [Description("Include bounded source snippets.")] bool? includeSnippets = null,
        [Description("Maximum source snippet lines; must be 0 or greater.")] int? snippetLines = null,
        [Description("Output profile: compact, evidence, or full. Omitted defaults to compact for this MCP tool.")] string? profile = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            services,
            MessagesTool,
            NavlynToolCommandBuilder.Messages(mode, query, candidateId, file, line, column, assumeKind, assumeKinds, match, caseSensitive, candidatePolicy, minConfidence, explainSelection, project, projects, excludeGenerated, candidateLimit, handlerLimit, callSiteLimit, evidenceLimit, includeSnippets, snippetLines, profile),
            cancellationToken);
    }

    [McpServerTool(Name = EfTool, Title = "Navlyn EF Core", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
    [Description(EfDescription)]
    public static Task<CallToolResult> Ef(
        IServiceProvider services,
        [Description("Required EF operation: model or impact.")] string mode,
        [Description("Model-mode entity type fragment filter. Model mode rejects selected-target inputs.")] string? entity = null,
        [Description("Model-mode DbContext type fragment filter. Can be combined with entity; model mode rejects selected-target inputs.")] string? dbcontext = null,
        [Description("Approximate entity type query for impact mode. Provide exactly one of query, candidateId, or file with line and column.")] string? query = null,
        [Description("Selected entity symbol candidate id for impact mode. Mutually exclusive with query and source position; query-only fuzzy fields are not allowed.")] string? candidateId = null,
        [Description("C# or Visual Basic source file for exact impact targeting; requires line and column.")] string? file = null,
        [Description("1-based source line; requires file and column.")] int? line = null,
        [Description("1-based source column; requires file and line.")] int? column = null,
        [Description("Query-only assumed symbol kind. Mutually exclusive with assumeKinds; not allowed with candidateId or source position.")] string? assumeKind = null,
        [Description("Query-only assumed symbol kinds. Mutually exclusive with assumeKind; not allowed with candidateId or source position.")] string[]? assumeKinds = null,
        [Description("Query-only matching mode: smart, exact, contains, or regex; not allowed with candidateId or source position.")] string? match = null,
        [Description("Query-only case-sensitive matching; not allowed with candidateId or source position.")] bool? caseSensitive = null,
        [Description("Candidate selection policy: fail or select. Supported with query or candidateId, not source position.")] string? candidatePolicy = null,
        [Description("Minimum candidate confidence: high, medium, or low. Supported with query or candidateId, not source position.")] string? minConfidence = null,
        [Description("Include candidate-selection explanation where supported; not allowed with source position.")] bool? explainSelection = null,
        [Description("Single project filter. Mutually exclusive with projects; source position accepts at most one project.")] string? project = null,
        [Description("Project filters. Mutually exclusive with project; source position accepts at most one project.")] string[]? projects = null,
        [Description("Exclude generated source.")] bool? excludeGenerated = null,
        [Description("Maximum fuzzy candidates for impact mode; must be 1 or greater. Maps to CLI candidate-limit.")] int? candidateLimit = null,
        [Description("Maximum EF entity/model facts; must be 1 or greater. Omit to preserve the CLI default.")] int? entityLimit = null,
        [Description("Maximum EF query-site facts; must be 1 or greater. Omit to preserve the CLI default.")] int? querySiteLimit = null,
        [Description("Maximum evidence items per fact; must be 1 or greater. Omit to preserve the CLI default.")] int? evidenceLimit = null,
        [Description("Include bounded source snippets.")] bool? includeSnippets = null,
        [Description("Maximum source snippet lines; must be 0 or greater.")] int? snippetLines = null,
        [Description("Output profile: compact, evidence, or full. Omitted defaults to compact for this MCP tool.")] string? profile = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            services,
            EfTool,
            NavlynToolCommandBuilder.Ef(mode, entity, dbcontext, query, candidateId, file, line, column, assumeKind, assumeKinds, match, caseSensitive, candidatePolicy, minConfidence, explainSelection, project, projects, excludeGenerated, candidateLimit, entityLimit, querySiteLimit, evidenceLimit, includeSnippets, snippetLines, profile),
            cancellationToken);
    }

    [McpServerTool(Name = PackagesTool, Title = "Navlyn Packages", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
    [Description(PackagesDescription)]
    public static Task<CallToolResult> Packages(
        IServiceProvider services,
        [Description("Required package operation: usage or impact.")] string mode,
        [Description("Required nonblank package id to inspect.")] string package,
        [Description("Namespace hints for source usage attribution. Each item maps to a repeated --namespace option.")] string[]? namespaces = null,
        [Description("Single project filter. Mutually exclusive with projects.")] string? project = null,
        [Description("Project filters. Mutually exclusive with project.")] string[]? projects = null,
        [Description("Whether to include test projects. Omit to preserve the CLI default; true or false is forwarded explicitly when supplied.")] bool? includeTests = null,
        [Description("Exclude generated source.")] bool? excludeGenerated = null,
        [Description("Maximum source package-usage facts; must be 1 or greater. Omit to preserve the CLI default.")] int? usageLimit = null,
        [Description("Maximum package references; must be 1 or greater. Omit to preserve the CLI default.")] int? referenceLimit = null,
        [Description("Output profile: compact, evidence, or full. Omitted defaults to compact for this MCP tool.")] string? profile = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            services,
            PackagesTool,
            NavlynToolCommandBuilder.Packages(mode, package, namespaces, project, projects, includeTests, excludeGenerated, usageLimit, referenceLimit, profile),
            cancellationToken);
    }

    [McpServerTool(Name = PublicApiDiffTool, Title = "Navlyn Public API Diff", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
    [Description(PublicApiDiffDescription)]
    public static Task<CallToolResult> PublicApiDiff(
        IServiceProvider services,
        [Description("Required base Git ref for public API comparison.")] string? @base = null,
        [Description("Head Git ref for public API comparison; omit to use the CLI default.")] string? head = null,
        [Description("Single project filter. Mutually exclusive with projects.")] string? project = null,
        [Description("Project filters. Mutually exclusive with project.")] string[]? projects = null,
        [Description("Exclude generated source from public API facts.")] bool? excludeGenerated = null,
        [Description("Include public API additions; explicit true or false overrides the CLI default.")] bool? includeAdditions = null,
        [Description("Include public attribute changes; explicit true or false overrides the CLI default.")] bool? includeAttributes = null,
        [Description("Maximum API symbols to inspect; must be 1 or greater when provided.")] int? symbolLimit = null,
        [Description("Maximum API changes to return; must be 1 or greater when provided.")] int? changeLimit = null,
        [Description("Output profile: compact, evidence, or full. Omitted defaults to evidence for this MCP tool.")] string? profile = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            services,
            PublicApiDiffTool,
            NavlynToolCommandBuilder.PublicApiDiff(@base, head, project, projects, excludeGenerated, includeAdditions, includeAttributes, symbolLimit, changeLimit, profile),
            cancellationToken);
    }

    [McpServerTool(Name = ContextPackTool, Title = "Navlyn Context Pack", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
    [Description(ContextPackDescription)]
    public static Task<CallToolResult> ContextPack(
        IServiceProvider services,
        [Description("Approximate symbol query for one-symbol context mode; mutually exclusive with candidateId and diff.")] string? query = null,
        [Description("Previously selected symbol candidate id for one-symbol context mode; mutually exclusive with query and diff.")] string? candidateId = null,
        [Description("Set true to build context from a Git diff. Mutually exclusive with query/candidateId; diff-specific options require true.")] bool? diff = null,
        [Description("Base Git ref for diff mode; only valid when diff is true.")] string? @base = null,
        [Description("Head Git ref for diff mode; only valid when diff is true.")] string? head = null,
        [Description("Inspect staged changes in diff mode; only valid when diff is true.")] bool? staged = null,
        [Description("Include unstaged changes in diff mode; only valid when diff is true.")] bool? includeUnstaged = null,
        [Description("Context-pack goal: review, modify, or understand. Affects ranking, not execution.")] string? goal = null,
        [Description("Optional edit ranking hint: behavior, signature, rename, constructor, nullability, async, public-api, di-registration, or endpoint.")] string? changeKind = null,
        [Description("Maximum context-pack output budget in tokens; must be 1 or greater.")] int? budgetTokens = null,
        [Description("Maximum context evidence items; must be 1 or greater.")] int? itemLimit = null,
        [Description("Snippet policy: none, signature, line, or block.")] string? snippetPolicy = null,
        [Description("Maximum source context lines per snippet; must be 0 or greater.")] int? snippetLines = null,
        [Description("Maximum fuzzy candidates to consider; must be 1 or greater when provided.")] int? candidateLimit = null,
        [Description("Maximum member facts; must be 1 or greater when provided.")] int? memberLimit = null,
        [Description("Maximum reference facts; must be 1 or greater when provided.")] int? referenceLimit = null,
        [Description("Maximum relationship facts; must be 1 or greater when provided.")] int? relationLimit = null,
        [Description("Maximum related files; must be 1 or greater when provided.")] int? fileLimit = null,
        [Description("Maximum diagnostic facts; must be 1 or greater when provided.")] int? diagnosticLimit = null,
        [Description("Maximum symbol facts; must be 1 or greater when provided.")] int? symbolLimit = null,
        [Description("Maximum impact facts; must be 1 or greater when provided.")] int? impactLimit = null,
        [Description("Maximum related test facts; must be 1 or greater when provided.")] int? relatedTestLimit = null,
        [Description("Maximum impact traversal depth; must be 0 or greater.")] int? depth = null,
        [Description("Candidate policy for supported fuzzy selection: fail or select. Not used with a selected candidate or source position.")] string? candidatePolicy = null,
        [Description("Minimum fuzzy candidate confidence: high, medium, or low. Query mode only.")] string? minConfidence = null,
        [Description("Include fuzzy candidate-selection rationale where applicable.")] bool? explainSelection = null,
        [Description("Assumed symbol kind for fuzzy query matching. Mutually exclusive with assumeKinds.")] string? assumeKind = null,
        [Description("Assumed symbol kinds for fuzzy query matching. Mutually exclusive with assumeKind.")] string[]? assumeKinds = null,
        [Description("Fuzzy query matching mode: smart, exact, contains, or regex.")] string? match = null,
        [Description("Use case-sensitive fuzzy query matching.")] bool? caseSensitive = null,
        [Description("Single project filter. Mutually exclusive with projects.")] string? project = null,
        [Description("Multiple project filters. Mutually exclusive with project.")] string[]? projects = null,
        [Description("Exclude generated source from context evidence.")] bool? excludeGenerated = null,
        [Description("Output profile: compact, evidence, or full. Omitted defaults to compact for this MCP tool.")] string? profile = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            services,
            ContextPackTool,
            NavlynToolCommandBuilder.ContextPack(query, candidateId, diff, @base, head, staged, includeUnstaged, goal, changeKind, budgetTokens, itemLimit, snippetPolicy, snippetLines, candidateLimit, memberLimit, referenceLimit, relationLimit, fileLimit, diagnosticLimit, symbolLimit, impactLimit, relatedTestLimit, depth, candidatePolicy, minConfidence, explainSelection, assumeKind, assumeKinds, match, caseSensitive, project, projects, excludeGenerated, profile),
            cancellationToken);
    }

    [McpServerTool(Name = BatchTool, Title = "Navlyn Batch", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(NavlynToolResult))]
    [Description(BatchDescription)]
    public static Task<CallToolResult> Batch(
        IServiceProvider services,
        [Description("Optional batch-wide defaults object in the existing Navlyn CLI batch format.")] JsonElement? defaults = null,
        [Description("Two or more already-selected batch-supported fact requests in the existing Navlyn CLI batch format.")] JsonElement? requests = null,
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
        if (result.Ok && command.ResultCommand is { } resultCommand)
        {
            result = result.WithResultCommand(resultCommand);
        }

        return NavlynToolResultFormatter.ToCallToolResult(result);
    }
}

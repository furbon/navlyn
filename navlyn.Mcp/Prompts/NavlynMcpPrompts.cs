using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Navlyn.Mcp.Prompts;

[McpServerPromptType]
internal static class NavlynMcpPrompts
{
    [McpServerPrompt(Name = "navlyn_understand_symbol", Title = "Understand A C# Symbol")]
    [Description("Guide an MCP client through a facts-only Navlyn symbol understanding flow.")]
    public static string UnderstandSymbol(
        [Description("Candidate id from navlyn_resolve_target or navlyn_find_symbol when available.")] string? candidateId = null,
        [Description("Approximate symbol query when candidateId is not available.")] string? query = null)
    {
        string target = FormatTarget(candidateId, query);
        return $"""
Use Navlyn as a facts-only C# semantic investigation server for {target}.

Recommended flow:
1. If candidateId is missing, call navlyn_resolve_target with the query and inspect confidence, selectedTarget, candidates, and warnings.
2. Call navlyn_about_symbol with candidateId when possible.
3. Call navlyn_symbol_source for bounded source text or navlyn_symbol_edges for references, callers, calls, or implementations when precise facts are needed.
4. Escalate to navlyn_context_pack with goal understand and a compact profile only when normal file reads or smaller symbol facts are not enough.

Do not infer runtime behavior from static facts alone. Check confidence, warnings, truncation, and CLI diagnostics before relying on a result.
""";
    }

    [McpServerPrompt(Name = "navlyn_prepare_edit", Title = "Prepare A C# Edit")]
    [Description("Guide an MCP client through pre-edit semantic investigation using Navlyn facts.")]
    public static string PrepareEdit(
        [Description("Candidate id from navlyn_resolve_target or navlyn_find_symbol when available.")] string? candidateId = null,
        [Description("Approximate symbol query when candidateId is not available.")] string? query = null,
        [Description("Optional expected change kind such as signature, behavior, nullability, async, or public-api.")] string? changeKind = null)
    {
        string target = FormatTarget(candidateId, query);
        string change = string.IsNullOrWhiteSpace(changeKind) ? "the planned change" : $"a {changeKind.Trim()} change";
        return $"""
Prepare {change} for {target} with Navlyn facts before editing.

Recommended flow:
1. Resolve the target with navlyn_resolve_target if candidateId is missing.
2. Call navlyn_symbol_edges with operation references and a sensible limit.
3. Call navlyn_impact with candidateId and depth 2 for bounded static impact.
4. Call navlyn_tests_for_symbol only when related test candidates are part of the edit plan or explicitly requested.
5. Escalate to navlyn_context_pack with goal modify, changeKind when known, profile compact, and a budget that fits the client only when a bounded reading queue is needed.

Keep Navlyn in facts-provider mode. Use the returned facts to decide what files to read and edit with normal editor/file tools.
""";
    }

    [McpServerPrompt(Name = "navlyn_review_diff", Title = "Review A Git Diff With Facts")]
    [Description("Guide an MCP client through a Navlyn diff review facts flow.")]
    public static string ReviewDiff(
        [Description("Optional base Git ref.")] string? @base = null,
        [Description("Optional head Git ref. Requires base.")] string? head = null,
        [Description("Use the staged diff instead of refs/default working tree mode.")] bool? staged = null)
    {
        string diffMode = staged == true
            ? "the staged diff"
            : string.IsNullOrWhiteSpace(@base)
                ? "the current Git diff"
                : $"the diff from {@base}{(string.IsNullOrWhiteSpace(head) ? "" : $" to {head}")}";
        return $"""
Use Navlyn to collect deterministic review facts for {diffMode}.

Recommended flow:
1. Call navlyn_review_diff with profile evidence, plus base/head/staged arguments when applicable.
2. Inspect changed symbols, impact facts, diagnostics, related tests, warnings, truncation, and next actions.
3. Call navlyn_tests_for_diff only if test impact needs a smaller focused result.
4. Call navlyn_context_pack with diff true and goal review only when bounded reading material is needed.

Navlyn does not generate review comments or approve changes. Treat it as source-level evidence for the client or reviewer.
""";
    }

    [McpServerPrompt(Name = "navlyn_fix_diagnostic", Title = "Fix A C# Diagnostic")]
    [Description("Guide an MCP client through a diagnostic-focused Navlyn investigation.")]
    public static string FixDiagnostic(
        [Description("Repository-relative diagnostic file path when known.")] string? file = null,
        [Description("1-based diagnostic line when known.")] int? line = null,
        [Description("1-based diagnostic column when known.")] int? column = null,
        [Description("Diagnostic id such as CS8602 when known.")] string? diagnosticId = null)
    {
        string target = FormatDiagnosticTarget(file, line, column, diagnosticId);
        return $"""
Investigate {target} with Navlyn facts before editing.

Recommended flow:
1. If an exact file/line/column is known, call navlyn_exact_navigation with operation symbol_info or definition, or call navlyn_symbol_source when bounded source text is the needed fact.
2. Use navlyn_context_pack around the affected symbol or diff only when bounded reading material is needed.
3. Use navlyn_batch for batch-supported workspace, diagnostics, review, tests, DI, or application-domain facts only after deciding several facts are needed from one workspace.
4. For direct CLI-only facts such as symbol-diagnostics, diagnostic-pack, scope-at, or signature, ask the client to run the matching Navlyn CLI command outside MCP when that surface is available.
5. After editing, run the repository's normal build/test validation outside Navlyn.

Navlyn reports compiler and source-level facts. It does not apply fixes or prove runtime behavior.
""";
    }

    private static string FormatTarget(string? candidateId, string? query)
    {
        if (!string.IsNullOrWhiteSpace(candidateId))
        {
            return $"candidateId {candidateId.Trim()}";
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            return $"query {query.Trim()}";
        }

        return "the target symbol";
    }

    private static string FormatDiagnosticTarget(string? file, int? line, int? column, string? diagnosticId)
    {
        string location = string.IsNullOrWhiteSpace(file)
            ? "the diagnostic"
            : $"{file.Trim()}:{line?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"}:{column?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"}";
        return string.IsNullOrWhiteSpace(diagnosticId)
            ? location
            : $"{diagnosticId.Trim()} at {location}";
    }
}

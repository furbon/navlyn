using Microsoft.CodeAnalysis;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.Diagnostics;

internal sealed class DiagnosticPackResolver
{
    public async Task<DiagnosticPackResolution> ResolveAsync(
        LoadedWorkspace workspace,
        IReadOnlyList<Project> projects,
        DiagnosticPackInput input,
        bool excludeGenerated,
        IReadOnlyList<string> severities,
        int limit,
        int budgetTokens,
        CancellationToken cancellationToken)
    {
        WorkspaceDiagnosticsResolution diagnostics = await new WorkspaceDiagnosticsResolver().ResolveAsync(
            projects,
            excludeGenerated,
            cancellationToken);

        HashSet<string> severitySet = [.. severities];
        IReadOnlyList<WorkspaceDiagnosticResult> matching = [.. diagnostics.Diagnostics
            .Where(diagnostic => severitySet.Count == 0 || severitySet.Contains(diagnostic.Severity))
            .Where(diagnostic => MatchesInput(diagnostic, input))
            .OrderBy(diagnostic => diagnostic.Path, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Line)
            .ThenBy(diagnostic => diagnostic.Column)
            .ThenBy(diagnostic => diagnostic.Project.Path, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Project.Name, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Id, StringComparer.Ordinal)];

        WorkspaceDiagnosticResult? representative = matching.FirstOrDefault(diagnostic =>
            diagnostic.Path is not null && diagnostic.Line is not null && diagnostic.Column is not null);

        DiagnosticPackContext? context = representative is null
            ? null
            : await CreateContextAsync(workspace, representative, excludeGenerated, budgetTokens, cancellationToken);

        IReadOnlyList<DiagnosticPackDiagnostic> items = [.. matching
            .Take(limit)
            .Select(diagnostic => new DiagnosticPackDiagnostic(
                diagnostic.Project,
                diagnostic.Severity,
                diagnostic.Id,
                diagnostic.Message,
                diagnostic.Path,
                diagnostic.Line,
                diagnostic.Column,
                diagnostic.EndLine,
                diagnostic.EndColumn,
                GetReasonCodes(diagnostic, input)))];

        IReadOnlyList<string> warnings = matching.Count == 0
            ? ["no-diagnostics-matched"]
            : [];

        return new DiagnosticPackResolution(
            Workspace: workspace.DisplayPath,
            Kind: workspace.Kind,
            Input: input,
            ExcludeGenerated: excludeGenerated,
            Filters: new DiagnosticPackFilters(severities, limit, budgetTokens),
            TotalDiagnostics: matching.Count,
            Truncated: matching.Count > limit,
            Diagnostics: items,
            Context: context,
            Warnings: warnings,
            NextActions: CreateNextActions(workspace.DisplayPath, representative));
    }

    private static bool MatchesInput(WorkspaceDiagnosticResult diagnostic, DiagnosticPackInput input)
    {
        if (input.Mode == "id")
        {
            return string.Equals(diagnostic.Id, input.Id, StringComparison.Ordinal);
        }

        if (diagnostic.Path is null || diagnostic.Line is null || input.File is null || input.Line is null)
        {
            return false;
        }

        if (!string.Equals(diagnostic.Path, input.File, StringComparison.Ordinal))
        {
            return false;
        }

        int diagnosticEndLine = diagnostic.EndLine ?? diagnostic.Line.Value;
        return diagnostic.Line.Value <= input.Line.Value && diagnosticEndLine >= input.Line.Value;
    }

    private static async Task<DiagnosticPackContext?> CreateContextAsync(
        LoadedWorkspace workspace,
        WorkspaceDiagnosticResult diagnostic,
        bool excludeGenerated,
        int budgetTokens,
        CancellationToken cancellationToken)
    {
        if (diagnostic.Path is null || diagnostic.Line is null || diagnostic.Column is null)
        {
            return null;
        }

        FileInfo file = new(diagnostic.Path);
        ScopeAtResolution? scope = null;
        SignatureResolution? signature = null;
        SymbolSourceResolution? source = null;
        List<string> warnings = [];

        ScopeAtResolutionResult scopeResult = await new ScopeAtResolver().ResolveAsync(
            workspace.Solution,
            file,
            diagnostic.Line.Value,
            diagnostic.Column.Value,
            project: null,
            excludeGenerated,
            cancellationToken);
        if (scopeResult.Error is null)
        {
            scope = scopeResult.Resolution;
        }
        else
        {
            warnings.Add($"scope-at-failed:{DiagnosticIds.Prefix}{scopeResult.Error.DiagnosticId}");
        }

        SignatureResolutionResult signatureResult = await new SignatureResolver().ResolveAsync(
            workspace.Solution,
            file,
            diagnostic.Line.Value,
            diagnostic.Column.Value,
            project: null,
            excludeGenerated,
            cancellationToken);
        if (signatureResult.Error is null)
        {
            signature = signatureResult.Resolution;
        }
        else
        {
            warnings.Add($"signature-failed:{DiagnosticIds.Prefix}{signatureResult.Error.DiagnosticId}");
        }

        SymbolSourceResolutionResult sourceResult = await new SymbolSourceResolver().ResolveAsync(
            workspace.Solution,
            file,
            diagnostic.Line.Value,
            diagnostic.Column.Value,
            project: null,
            excludeGenerated,
            new SymbolSourceOptions("declaration", MaxLines: 40, budgetTokens),
            cancellationToken);
        if (sourceResult.Error is null)
        {
            source = sourceResult.Resolution;
        }
        else
        {
            warnings.Add($"symbol-source-failed:{DiagnosticIds.Prefix}{sourceResult.Error.DiagnosticId}");
        }

        return new DiagnosticPackContext(scope, signature, source, warnings);
    }

    private static IReadOnlyList<string> GetReasonCodes(WorkspaceDiagnosticResult diagnostic, DiagnosticPackInput input)
    {
        return input.Mode == "id"
            ? ["diagnostic-id-match"]
            : ["diagnostic-location-match"];
    }

    private static IReadOnlyList<DiagnosticPackNextAction> CreateNextActions(
        string workspace,
        WorkspaceDiagnosticResult? diagnostic)
    {
        if (diagnostic?.Path is null || diagnostic.Line is null || diagnostic.Column is null)
        {
            return [];
        }

        return
        [
            new DiagnosticPackNextAction("scope-at", workspace, diagnostic.Path, diagnostic.Line.Value, diagnostic.Column.Value, "Inspect enclosing scopes for this diagnostic."),
            new DiagnosticPackNextAction("symbol-source", workspace, diagnostic.Path, diagnostic.Line.Value, diagnostic.Column.Value, "Read the declaration around this diagnostic."),
            new DiagnosticPackNextAction("symbol-diagnostics", workspace, diagnostic.Path, diagnostic.Line.Value, diagnostic.Column.Value, "Inspect diagnostics scoped to the containing symbol.")
        ];
    }
}

internal sealed record DiagnosticPackInput(
    string Mode,
    string? Id,
    string? File,
    int? Line,
    int? Column);

internal sealed record DiagnosticPackResolution(
    string Workspace,
    string Kind,
    DiagnosticPackInput Input,
    bool ExcludeGenerated,
    DiagnosticPackFilters Filters,
    int TotalDiagnostics,
    bool Truncated,
    IReadOnlyList<DiagnosticPackDiagnostic> Diagnostics,
    DiagnosticPackContext? Context,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<DiagnosticPackNextAction> NextActions);

internal sealed record DiagnosticPackFilters(
    IReadOnlyList<string> Severities,
    int Limit,
    int BudgetTokens);

internal sealed record DiagnosticPackDiagnostic(
    WorkspaceDiagnosticProject Project,
    string Severity,
    string Id,
    string Message,
    string? Path,
    int? Line,
    int? Column,
    int? EndLine,
    int? EndColumn,
    IReadOnlyList<string> ReasonCodes);

internal sealed record DiagnosticPackContext(
    ScopeAtResolution? Scope,
    SignatureResolution? Signature,
    SymbolSourceResolution? Source,
    IReadOnlyList<string> Warnings);

internal sealed record DiagnosticPackNextAction(
    string Command,
    string Workspace,
    string File,
    int Line,
    int Column,
    string Reason);

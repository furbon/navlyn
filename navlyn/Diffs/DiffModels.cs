using System.Text.Json.Serialization;
using Navlyn.Diagnostics;
using Navlyn.Symbols;

namespace Navlyn.Diffs;

internal sealed record DiffRequest(
    string Mode,
    string? Base,
    string? Head,
    bool Staged,
    bool IncludeUnstaged);

internal sealed record DiffReadResult(DiffSet? Diff, DiffWorkflowError? Error)
{
    public static DiffReadResult Succeeded(DiffSet diff)
    {
        return new DiffReadResult(diff, Error: null);
    }

    public static DiffReadResult Failed(int diagnosticId, string message, int exitCode)
    {
        return new DiffReadResult(null, new DiffWorkflowError(diagnosticId, message, exitCode));
    }
}

internal sealed record DiffWorkflowError(int DiagnosticId, string Message, int ExitCode);

internal sealed record DiffWorkflowExecutionResult<T>(T? Result, DiffWorkflowError? Error)
{
    public static DiffWorkflowExecutionResult<T> Succeeded(T result)
    {
        return new DiffWorkflowExecutionResult<T>(result, Error: null);
    }

    public static DiffWorkflowExecutionResult<T> Failed(DiffWorkflowError error)
    {
        return new DiffWorkflowExecutionResult<T>(Result: default, error);
    }
}

internal sealed record DiffSet(
    string Mode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Base,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Head,
    bool Staged,
    bool IncludeUnstaged,
    int TotalFiles,
    IReadOnlyList<DiffFile> Files);

internal sealed record DiffFile(
    string Path,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? OldPath,
    string Status,
    IReadOnlyList<DiffHunk> Hunks);

internal sealed record DiffHunk(int OldStart, int OldLineCount, int NewStart, int NewLineCount)
{
    public IEnumerable<int> NewLines()
    {
        for (int line = NewStart; line < NewStart + NewLineCount; line++)
        {
            yield return line;
        }
    }
}

internal sealed record DiffWorkflowLimits(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? SymbolLimit,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? ImpactLimit,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? DiagnosticLimit,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? RelatedTestLimit,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? Depth);

internal sealed record DiffProjectFilter(
    string Filter,
    string Name,
    string? Path,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? TargetFramework);

internal sealed record ChangedSymbolsResult(
    string Workspace,
    string Kind,
    string Command,
    DiffSet Diff,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<DiffProjectFilter>? Projects,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    bool ExcludeGenerated,
    DiffWorkflowLimits Limits,
    bool Truncated,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<DiffNextAction> NextActions,
    ChangedSymbolsSection ChangedSymbols,
    IReadOnlyList<DiffUnresolvedChange> UnresolvedChanges);

internal sealed record ImpactDiffResult(
    string Workspace,
    string Kind,
    string Command,
    DiffSet Diff,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<DiffProjectFilter>? Projects,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    bool ExcludeGenerated,
    DiffWorkflowLimits Limits,
    bool Truncated,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<DiffNextAction> NextActions,
    ChangedSymbolsSection ChangedSymbols,
    IReadOnlyList<DiffUnresolvedChange> UnresolvedChanges,
    DiffImpactSection Impact);

internal sealed record DiagnosticsDiffResult(
    string Workspace,
    string Kind,
    string Command,
    DiffSet Diff,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<DiffProjectFilter>? Projects,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? Severities,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? Ids,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    bool ExcludeGenerated,
    DiffWorkflowLimits Limits,
    bool Truncated,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<DiffNextAction> NextActions,
    ChangedSymbolsSection ChangedSymbols,
    IReadOnlyList<DiffUnresolvedChange> UnresolvedChanges,
    DiagnosticsScopeSection DiagnosticsScope,
    DiffDiagnosticsSection Diagnostics);

internal sealed record ReviewDiffResult(
    string Workspace,
    string Kind,
    string Command,
    DiffSet Diff,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<DiffProjectFilter>? Projects,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    bool ExcludeGenerated,
    DiffWorkflowLimits Limits,
    bool Truncated,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<DiffNextAction> NextActions,
    ChangedSymbolsSection ChangedSymbols,
    IReadOnlyList<DiffUnresolvedChange> UnresolvedChanges,
    PublicContractChangesSection PublicContractChanges,
    DiffImpactSection Impact,
    RelatedTestsSection RelatedTests,
    DiagnosticsScopeSection DiagnosticsScope,
    DiffDiagnosticsSection Diagnostics,
    IReadOnlyList<ReviewFinding> Findings);

internal sealed record ChangedSymbolsSection(
    int TotalSymbols,
    int Limit,
    bool Truncated,
    IReadOnlyList<DiffChangedSymbol> Symbols);

internal sealed record DiffChangedSymbol(
    string Name,
    string Kind,
    string? Container,
    SymbolFacts Facts,
    string Path,
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    IReadOnlyList<string> ChangeKinds,
    int TotalChangedLines,
    int ChangedLineLimit,
    bool ChangedLinesTruncated,
    IReadOnlyList<DiffChangedLine> ChangedLines,
    IReadOnlyList<string> ReasonCodes);

internal sealed record DiffChangedLine(string Path, int Line);

internal sealed record DiffUnresolvedChange(
    string Path,
    string Status,
    IReadOnlyList<DiffHunk> Hunks,
    IReadOnlyList<string> ReasonCodes);

internal sealed record DiffImpactSection(
    int TotalItems,
    int Limit,
    bool Truncated,
    IReadOnlyList<DiffImpactItem> Items);

internal sealed record DiffImpactItem(
    DiffChangedSymbol ChangedSymbol,
    LimitedList<DiffSourceLocation> References,
    LimitedList<DiffCallGroup> Callers,
    LimitedList<DiffCallGroup> Calls,
    LimitedList<DiffSymbolLocation> Implementations,
    LimitedList<DiffEntrypointChain> EntrypointChains,
    IReadOnlyList<DiffAffectedFile> AffectedFiles,
    IReadOnlyList<DiffRiskReason> RiskReasons);

internal sealed record LimitedList<T>(
    int TotalItems,
    int Limit,
    bool Truncated,
    IReadOnlyList<T> Items);

internal sealed record DiffSourceLocation(
    string Path,
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DiffSymbolLocation? ContainingSymbol,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DiffSnippet? Snippet);

internal sealed record DiffSymbolLocation(
    string Name,
    string Kind,
    string? Container,
    SymbolFacts Facts,
    string? Path,
    int? Line,
    int? Column,
    int? EndLine,
    int? EndColumn,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DiffSnippet? Snippet = null);

internal sealed record DiffCallGroup(DiffSymbolLocation Symbol, IReadOnlyList<DiffSourceLocation> Locations);

internal sealed record DiffEntrypointChain(IReadOnlyList<DiffSymbolLocation> Symbols, string EndReason);

internal sealed record DiffAffectedFile(
    string Path,
    string ImpactLevel,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<DiffSourceLocation> Locations);

internal sealed record DiffRiskReason(
    string Code,
    string Severity,
    string Confidence,
    IReadOnlyList<DiffSourceLocation> Evidence);

internal sealed record DiagnosticsScopeSection(IReadOnlyList<DiagnosticsScopePath> Paths);

internal sealed record DiagnosticsScopePath(string Path, IReadOnlyList<string> ReasonCodes);

internal sealed record DiffDiagnosticsSection(
    int TotalDiagnostics,
    int Limit,
    bool Truncated,
    IReadOnlyList<DiffDiagnosticItem> Items);

internal sealed record DiffDiagnosticItem(
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

internal sealed record PublicContractChangesSection(
    int TotalChanges,
    int Limit,
    bool Truncated,
    IReadOnlyList<PublicContractChange> Changes);

internal sealed record PublicContractChange(
    string Code,
    string Confidence,
    DiffChangedSymbol Symbol,
    IReadOnlyList<string> ReasonCodes);

internal sealed record RelatedTestsSection(
    int TotalCandidates,
    int Limit,
    bool Truncated,
    IReadOnlyList<RelatedTestCandidate> Candidates);

internal sealed record RelatedTestCandidate(
    string Path,
    string Confidence,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<DiffSourceLocation> Evidence);

internal sealed record ReviewFinding(
    string Kind,
    string Code,
    string Severity,
    string Confidence,
    string Claim,
    IReadOnlyList<DiffSourceLocation> Evidence,
    IReadOnlyList<DiffSourceLocation> SourceLocations,
    IReadOnlyList<string> SymbolIds,
    IReadOnlyList<string> ReasonCodes);

internal sealed record DiffNextAction(
    string Command,
    string Workspace,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Query,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? File,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? Line,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? Column,
    string Reason);

internal sealed record DiffSnippet(int StartLine, int EndLine, IReadOnlyList<string> Lines);

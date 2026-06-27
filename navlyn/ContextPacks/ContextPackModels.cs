using System.Text.Json.Serialization;
using Navlyn.Diagnostics;
using Navlyn.Diffs;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.ContextPacks;

internal sealed record ContextPackOptions(
    string Goal,
    int BudgetTokens,
    int ItemLimit,
    string SnippetPolicy,
    int SnippetLines,
    int CandidateLimit,
    int MemberLimit,
    int ReferenceLimit,
    int RelationLimit,
    int FileLimit,
    int QueryDiagnosticLimit,
    int SymbolLimit,
    int ImpactLimit,
    int DiffDiagnosticLimit,
    int RelatedTestLimit,
    int Depth);

internal sealed record ContextPackResult(
    string Workspace,
    string Kind,
    string Command,
    string Mode,
    string Goal,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    object? Projects,
    bool ExcludeGenerated,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ContextPackQuery? Query,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ContextPackSelection? Selection,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DiffSet? Diff,
    ContextPackBudget Budget,
    ContextPackLimits Limits,
    ContextPack Pack,
    bool Truncated,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<ContextPackNextAction> NextActions);

internal sealed record ContextPackQuery(
    string Text,
    string Match,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? CaseSensitive,
    FuzzyAssumptions Assumptions);

internal sealed record ContextPackSelection(
    string Confidence,
    int CandidateCount,
    int TotalCandidates,
    IReadOnlyList<FuzzySymbolCandidate> Candidates,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FuzzySymbolCandidate? SelectedCandidate,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<FuzzySymbolCandidate>? Alternatives,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FuzzySelectionInput? SelectionInput = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FuzzySelectionExplanation? SelectionExplanation = null);

internal sealed record ContextPackBudget(
    int RequestedTokens,
    string Estimator,
    int CharLimit,
    int EstimatedTokensUsed,
    int CharsUsed,
    bool Truncated);

internal sealed record ContextPackLimits(
    int ItemLimit,
    int CandidateLimit,
    int MemberLimit,
    int ReferenceLimit,
    int RelationLimit,
    int FileLimit,
    int DiagnosticLimit,
    int SymbolLimit,
    int ImpactLimit,
    int RelatedTestLimit,
    int Depth);

internal sealed record ContextPack(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ContextPackRoot? Root,
    object Sections,
    IReadOnlyList<ContextPackItem> Items,
    IReadOnlyList<ContextPackOmitted> Omitted);

internal sealed record ContextPackRoot(
    string Kind,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    object? Symbol,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? TotalChangedSymbols,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? TotalChangedFiles);

internal sealed record QueryContextSections(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FuzzySourceLocation? Definition,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ContextMemberOutlineSection? MemberOutline,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ContextReferenceSection? References,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ContextRelatedFilesSection? RelatedFiles,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FuzzyRelationSummary? CallRelations,
    ContextPackDiagnosticsSection Diagnostics);

internal sealed record DiffContextSections(
    ChangedSymbolsSection ChangedSymbols,
    IReadOnlyList<DiffUnresolvedChange> UnresolvedChanges,
    PublicContractChangesSection PublicContractChanges,
    DiffImpactSection Impact,
    RelatedTestsSection RelatedTests,
    DiagnosticsScopeSection DiagnosticsScope,
    DiffDiagnosticsSection Diagnostics,
    IReadOnlyList<ReviewFinding> Findings);

internal sealed record EmptyContextSections;

internal sealed record ContextRelatedFilesSection(
    int TotalFiles,
    int Limit,
    bool Truncated,
    IReadOnlyList<FuzzyRelatedFile> Files);

internal sealed record ContextMemberOutlineSection(
    int TotalMembers,
    int Limit,
    bool Truncated,
    IReadOnlyList<FuzzyMemberEntry> Members);

internal sealed record ContextReferenceSection(
    int TotalMatches,
    int Limit,
    bool Truncated,
    IReadOnlyList<FuzzySourceLocation> References,
    IReadOnlyList<FuzzyReferenceFileSummary> Files);

internal sealed record ContextPackDiagnosticsSection(
    int TotalDiagnostics,
    int Limit,
    bool Truncated,
    IReadOnlyList<ContextPackDiagnosticItem> Items);

internal sealed record ContextPackDiagnosticItem(
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

internal sealed record ContextPackItem(
    string Id,
    string Kind,
    int Priority,
    IReadOnlyList<string> ReasonCodes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    object? Symbol,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ContextPackSourceLocation? SourceLocation,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ContextPackItemContent? Content,
    int EstimatedTokens);

internal sealed record ContextPackSourceLocation(
    string Path,
    int Line,
    int Column,
    int EndLine,
    int EndColumn);

internal sealed record ContextPackItemContent(
    string TextKind,
    int? StartLine,
    int? EndLine,
    IReadOnlyList<string> Lines,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    bool Truncated = false);

internal sealed record ContextPackOmitted(
    string Reason,
    string Kind,
    int TotalItems,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? FirstOmittedItemId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ContextPackNextAction? NextAction);

internal sealed record ContextPackNextAction(
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

internal sealed record ContextPackMaterial(
    ContextPackItem Item,
    int CharCount);

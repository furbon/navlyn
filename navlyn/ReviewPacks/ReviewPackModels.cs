using System.Text.Json.Serialization;
using Navlyn.Diffs;

namespace Navlyn.ReviewPacks;

internal static class ReviewPackNames
{
    public static readonly string[] Ordered = ["async", "disposal", "nullability", "security", "architecture"];

    public static bool IsKnown(string value)
    {
        return Ordered.Contains(value, StringComparer.Ordinal);
    }
}

internal sealed record ReviewPackOptions(
    IReadOnlyList<string> Packs,
    string Scope,
    DiffRequest? DiffRequest,
    IReadOnlyList<string> ProjectFilters,
    bool ExcludeGenerated,
    int FindingLimit,
    int EvidenceLimit,
    int SymbolLimit,
    int FileLimit,
    bool IncludeSnippets,
    int SnippetLines,
    string? ArchitectureConfig);

internal sealed record ReviewPackExecutionResult(ReviewPackResult? Result, ReviewPackError? Error)
{
    public static ReviewPackExecutionResult Succeeded(ReviewPackResult result)
    {
        return new ReviewPackExecutionResult(result, Error: null);
    }

    public static ReviewPackExecutionResult Failed(int diagnosticId, string message, int exitCode)
    {
        return new ReviewPackExecutionResult(Result: null, new ReviewPackError(diagnosticId, message, exitCode));
    }
}

internal sealed record ReviewPackError(int DiagnosticId, string Message, int ExitCode);

internal sealed record ReviewPackProjectFilter(
    string Filter,
    string Name,
    string? Path,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? TargetFramework);

internal sealed record ReviewPackResult(
    string Workspace,
    string Kind,
    string Command,
    ReviewPackScope Scope,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<ReviewPackProjectFilter>? Projects,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    bool ExcludeGenerated,
    ReviewPackPacksSection Packs,
    ReviewPackSummary Summary,
    IReadOnlyList<ReviewPackFinding> Findings,
    IReadOnlyList<ReviewPackPackResult> PackResults,
    ReviewPackLimits Limits,
    bool Truncated,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<ReviewPackNextAction> NextActions);

internal sealed record ReviewPackScope(
    string Mode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DiffSet? Diff,
    int TotalFiles,
    IReadOnlyList<string> Files);

internal sealed record ReviewPackPacksSection(
    IReadOnlyList<string> Requested,
    IReadOnlyList<string> Executed,
    IReadOnlyList<ReviewPackSkippedPack> Skipped);

internal sealed record ReviewPackSkippedPack(string Pack, string Status, string Reason);

internal sealed record ReviewPackSummary(
    int TotalFindings,
    IReadOnlyDictionary<string, int> ByPack,
    IReadOnlyDictionary<string, int> BySeverity,
    IReadOnlyDictionary<string, int> ByConfidence);

internal sealed record ReviewPackLimits(
    int FindingLimit,
    int EvidenceLimit,
    int SymbolLimit,
    int FileLimit);

internal sealed record ReviewPackPackResult(
    string Pack,
    string Status,
    int TotalFindings,
    int Limit,
    bool Truncated,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<ReviewPackRuleSummary> Rules);

internal sealed record ReviewPackRuleSummary(
    string RuleId,
    int TotalFindings,
    string Severity);

internal sealed record ReviewPackFinding(
    string Pack,
    string RuleId,
    string Kind,
    string Severity,
    string Confidence,
    string Claim,
    IReadOnlyList<ReviewPackEvidence> Evidence,
    IReadOnlyList<ReviewPackEvidence> SourceLocations,
    IReadOnlyList<string> SymbolIds,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<ReviewPackNextAction> NextActions);

internal sealed record ReviewPackEvidence(
    string Path,
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ContainingSymbol,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ReviewPackSnippet? Snippet);

internal sealed record ReviewPackSnippet(int StartLine, int EndLine, IReadOnlyList<string> Lines);

internal sealed record ReviewPackNextAction(
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

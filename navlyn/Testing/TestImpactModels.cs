using System.Text.Json.Serialization;
using Navlyn.Symbols;

namespace Navlyn.Testing;

internal sealed record TestImpactOptions(
    int TestLimit,
    int ReferenceLimit,
    bool IncludeSnippets,
    int SnippetLines,
    bool ExcludeGenerated);

internal sealed record TestsForSymbolResult(
    string Workspace,
    string Kind,
    string Command,
    TestSelectionInput SelectionInput,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FuzzySelectionSection? Selection,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    TestSubject? Subject,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<TestProjectFilter>? Projects,
    IReadOnlyList<TestProjectInfo> TestProjects,
    TestImpactLimits Limits,
    TestCandidatesSection Tests,
    bool Truncated,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<TestNextAction> NextActions);

internal sealed record TestsForDiffResult(
    string Workspace,
    string Kind,
    string Command,
    object Diff,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<TestProjectFilter>? Projects,
    IReadOnlyList<TestProjectInfo> TestProjects,
    TestImpactDiffLimits Limits,
    object ChangedSymbols,
    IReadOnlyList<object> UnresolvedChanges,
    TestCandidatesSection Tests,
    bool Truncated,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<TestNextAction> NextActions);

internal sealed record TestSelectionInput(
    string Mode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Query,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CandidateId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? File,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? Line,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? Column);

internal sealed record FuzzySelectionSection(
    string Confidence,
    int CandidateCount,
    int TotalCandidates,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FuzzySymbolCandidate? SelectedCandidate,
    IReadOnlyList<FuzzySymbolCandidate> Candidates,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FuzzySelectionExplanation? SelectionExplanation);

internal sealed record TestSubject(
    string Name,
    string Kind,
    string? Container,
    SymbolFacts Facts,
    string? Path,
    int? Line,
    int? Column,
    int? EndLine,
    int? EndColumn);

internal sealed record TestProjectFilter(
    string Filter,
    string Name,
    string? Path,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? TargetFramework);

internal sealed record TestProjectInfo(
    string Name,
    string? Path,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? TargetFramework,
    IReadOnlyList<string> ReasonCodes);

internal sealed record TestImpactLimits(
    int CandidateLimit,
    int TestLimit,
    int ReferenceLimit);

internal sealed record TestImpactDiffLimits(
    int SymbolLimit,
    int TestLimit,
    int ReferenceLimit);

internal sealed record TestCandidatesSection(
    int TotalCandidates,
    int Limit,
    bool Truncated,
    IReadOnlyList<TestImpactCandidate> Candidates);

internal sealed record TestImpactCandidate(
    string Kind,
    string Framework,
    string Name,
    string? Container,
    SymbolFacts Facts,
    string Path,
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    TestProjectInfo Project,
    string Confidence,
    int Score,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<TestEvidence> Evidence,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FuzzySnippet? Snippet);

internal sealed record TestEvidence(
    string Kind,
    string Path,
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    IReadOnlyList<string> ReasonCodes);

internal sealed record TestNextAction(string Command, string Workspace, string Reason);

internal sealed record TestDiscoveryResult(
    IReadOnlyList<TestProjectInfo> TestProjects,
    IReadOnlyList<TestImpactCandidate> Candidates);


using System.Text.Json.Serialization;
using Navlyn.Symbols;

namespace Navlyn.Entrypoints;

internal sealed record FrameworkEntrypointOptions(
    IReadOnlyList<string> Frameworks,
    int Limit,
    int EvidenceLimit,
    bool IncludeSnippets,
    int SnippetLines,
    bool ExcludeGenerated);

internal sealed record FrameworkEntrypointsResult(
    string Workspace,
    string Kind,
    string Command,
    IReadOnlyList<string> Frameworks,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<FrameworkEntrypointProjectFilter>? Projects,
    FrameworkEntrypointLimits Limits,
    FrameworkEntrypointsSection Entrypoints,
    bool Truncated,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<FrameworkEntrypointNextAction> NextActions);

internal sealed record FrameworkEntrypointLimits(int Limit, int EvidenceLimit);

internal sealed record FrameworkEntrypointsSection(
    int TotalEntrypoints,
    int Limit,
    bool Truncated,
    IReadOnlyList<FrameworkEntrypointItem> Items);

internal sealed record FrameworkEntrypointItem(
    string EntrypointKind,
    string Framework,
    string Name,
    string? Container,
    SymbolFacts Facts,
    string Path,
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    FrameworkEntrypointProject Project,
    string Confidence,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<FrameworkEntrypointEvidence> Evidence,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FuzzySnippet? Snippet);

internal sealed record FrameworkEntrypointProject(
    string Name,
    string? Path,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? TargetFramework);

internal sealed record FrameworkEntrypointProjectFilter(
    string Filter,
    string Name,
    string? Path,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? TargetFramework);

internal sealed record FrameworkEntrypointEvidence(
    string Kind,
    string Path,
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    IReadOnlyList<string> ReasonCodes);

internal sealed record FrameworkEntrypointNextAction(string Command, string Workspace, string Reason);

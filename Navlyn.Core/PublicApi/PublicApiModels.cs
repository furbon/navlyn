using System.Text.Json.Serialization;

namespace Navlyn.PublicApi;

internal sealed record PublicApiDiffOptions(
    string BaseRef,
    string? HeadRef,
    bool ExcludeGenerated,
    bool IncludeAdditions,
    bool IncludeAttributes,
    int SymbolLimit,
    int ChangeLimit);

internal sealed record PublicApiDiffExecutionResult(PublicApiDiffResult? Result, PublicApiDiffError? Error)
{
    public static PublicApiDiffExecutionResult Succeeded(PublicApiDiffResult result)
    {
        return new PublicApiDiffExecutionResult(result, Error: null);
    }

    public static PublicApiDiffExecutionResult Failed(int diagnosticId, string message, int exitCode)
    {
        return new PublicApiDiffExecutionResult(Result: null, new PublicApiDiffError(diagnosticId, message, exitCode));
    }
}

internal sealed record PublicApiDiffError(int DiagnosticId, string Message, int ExitCode);

internal sealed record PublicApiDiffResult(
    string Workspace,
    string Kind,
    string Command,
    PublicApiComparison Comparison,
    IReadOnlyList<PublicApiProjectFilter>? Projects,
    PublicApiDiffLimits Limits,
    PublicApiDiffSummary Summary,
    PublicApiChangesSection Changes,
    bool Truncated,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<PublicApiNextAction> NextActions);

internal sealed record PublicApiComparison(
    string Base,
    string Head,
    string Mode,
    string WorkspacePath);

internal sealed record PublicApiProjectFilter(
    string Filter,
    string Name,
    string? Path,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? TargetFramework);

internal sealed record PublicApiDiffLimits(int SymbolLimit, int ChangeLimit);

internal sealed record PublicApiDiffSummary(
    int TotalChanges,
    int BreakingSourceChanges,
    int BreakingBinaryChanges,
    int Additions,
    int Removals,
    int SignatureChanges);

internal sealed record PublicApiChangesSection(
    int TotalChanges,
    int Limit,
    bool Truncated,
    IReadOnlyList<PublicApiChange> Items);

internal sealed record PublicApiChange(
    string Code,
    string Kind,
    PublicApiCompatibility SourceCompatibility,
    PublicApiCompatibility BinaryCompatibility,
    PublicApiSymbol Symbol,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    PublicApiSymbol? Before,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    PublicApiSymbol? After,
    IReadOnlyList<PublicApiEvidence> Evidence,
    IReadOnlyList<string> ReasonCodes);

internal sealed record PublicApiCompatibility(
    string Risk,
    string Confidence,
    IReadOnlyList<string> ReasonCodes);

internal sealed record PublicApiSymbol(
    string SymbolId,
    string Name,
    string Kind,
    string? Container,
    string? Namespace,
    string Accessibility,
    string Signature,
    string DocumentationCommentId,
    string? Path,
    int? Line,
    int? Column,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? TargetFramework,
    IReadOnlyList<string> Modifiers,
    IReadOnlyList<string> TypeParameters,
    IReadOnlyList<PublicApiParameter> Parameters,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ReturnType,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? PropertyType,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? FieldType,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? EventType,
    IReadOnlyList<string> GenericConstraints,
    IReadOnlyList<string> NullableAnnotations,
    IReadOnlyList<string> DefaultValues,
    IReadOnlyList<string> Attributes);

internal sealed record PublicApiParameter(
    string Name,
    string Type,
    int Ordinal,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? RefKind,
    bool IsOptional,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? DefaultValue);

internal sealed record PublicApiEvidence(
    string Side,
    string? Path,
    int? Line,
    int? Column,
    IReadOnlyList<string> ReasonCodes);

internal sealed record PublicApiNextAction(string Command, string Workspace, string Reason);

internal sealed record PublicApiSnapshot(
    string Side,
    string Ref,
    IReadOnlyList<PublicApiSymbol> Symbols,
    bool Truncated,
    IReadOnlyList<string> Warnings);


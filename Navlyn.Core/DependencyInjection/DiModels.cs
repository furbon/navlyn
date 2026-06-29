using System.Text.Json.Serialization;
using Navlyn.Symbols;

namespace Navlyn.DependencyInjection;

internal sealed record DiGraphOptions(
    int RegistrationLimit,
    int DependencyLimit,
    int RiskLimit,
    bool IncludeOptions,
    bool IncludeHostedServices,
    bool IncludeRisks,
    bool IncludeSnippets,
    int SnippetLines,
    bool ExcludeGenerated);

internal sealed record DiImpactOptions(
    int RegistrationLimit,
    int ConsumerLimit,
    int DependencyLimit,
    int RiskLimit,
    int Depth,
    bool IncludeSnippets,
    int SnippetLines,
    bool ExcludeGenerated);

internal sealed record DiGraphResult(
    string Workspace,
    string Kind,
    string Command,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<DiProjectFilter>? Projects,
    DiGraphLimits Limits,
    DiRegistrationsSection Registrations,
    DiDependenciesSection Dependencies,
    DiRisksSection Risks,
    bool Truncated,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<DiNextAction> NextActions);

internal sealed record WhereRegisteredResult(
    string Workspace,
    string Kind,
    string Command,
    DiSelectionInput SelectionInput,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DiSelectionSection? Selection,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DiTypeInfo? Subject,
    DiWhereRegisteredLimits Limits,
    DiRegistrationsSection Registrations,
    DiDependenciesSection ConstructorDependencies,
    bool Truncated,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<DiNextAction> NextActions);

internal sealed record DiImpactResult(
    string Workspace,
    string Kind,
    string Command,
    DiSelectionInput SelectionInput,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DiSelectionSection? Selection,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DiTypeInfo? Subject,
    DiImpactLimits Limits,
    DiRegistrationsSection Registrations,
    DiDependenciesSection ConstructorDependencies,
    DiConsumersSection Consumers,
    DiRisksSection Risks,
    bool Truncated,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<DiNextAction> NextActions);

internal sealed record DiGraphLimits(int RegistrationLimit, int DependencyLimit, int RiskLimit);

internal sealed record DiWhereRegisteredLimits(int CandidateLimit, int RegistrationLimit, int DependencyLimit);

internal sealed record DiImpactLimits(int CandidateLimit, int RegistrationLimit, int ConsumerLimit, int DependencyLimit, int RiskLimit, int Depth);

internal sealed record DiRegistrationsSection(
    int TotalRegistrations,
    int Limit,
    bool Truncated,
    IReadOnlyList<DiRegistrationItem> Items);

internal sealed record DiDependenciesSection(
    int TotalEdges,
    int Limit,
    bool Truncated,
    IReadOnlyList<DiDependencyEdge> Items);

internal sealed record DiConsumersSection(
    int TotalConsumers,
    int Limit,
    bool Truncated,
    IReadOnlyList<DiConsumerItem> Items);

internal sealed record DiRisksSection(
    int TotalRisks,
    int Limit,
    bool Truncated,
    IReadOnlyList<DiRiskFact> Items);

internal sealed record DiRegistrationItem(
    string RegistrationKind,
    string Lifetime,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DiTypeInfo? ServiceType,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DiTypeInfo? ImplementationType,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DiFactoryInfo? Factory,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DiInstanceInfo? Instance,
    string Path,
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    DiProjectInfo Project,
    string Confidence,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<DiEvidence> Evidence,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FuzzySnippet? Snippet);

internal sealed record DiDependencyEdge(
    DiTypeInfo ImplementationType,
    DiTypeInfo DependencyType,
    string ParameterName,
    int ParameterOrdinal,
    bool IsOptional,
    bool IsEnumerable,
    bool IsFactoryLike,
    string Confidence,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<DiEvidence> Evidence);

internal sealed record DiConsumerItem(
    DiTypeInfo ConsumerType,
    DiTypeInfo DependencyType,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<DiEvidence> Evidence);

internal sealed record DiRiskFact(
    string RiskKind,
    string Severity,
    string Confidence,
    string Claim,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DiTypeInfo? ServiceType,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DiTypeInfo? ImplementationType,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DiTypeInfo? DependencyType,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<DiEvidence> Evidence);

internal sealed record DiTypeInfo(
    string Name,
    string Kind,
    string? Container,
    SymbolFacts Facts,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Path,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? Line,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? Column,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? EndLine,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? EndColumn);

internal sealed record DiFactoryInfo(string Kind, IReadOnlyList<DiEvidence> Evidence);

internal sealed record DiInstanceInfo(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DiTypeInfo? Type,
    IReadOnlyList<DiEvidence> Evidence);

internal sealed record DiProjectInfo(
    string Name,
    string? Path,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? TargetFramework);

internal sealed record DiProjectFilter(
    string Filter,
    string Name,
    string? Path,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? TargetFramework);

internal sealed record DiEvidence(
    string Kind,
    string Path,
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    IReadOnlyList<string> ReasonCodes);

internal sealed record DiSelectionInput(
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

internal sealed record DiSelectionSection(
    string Confidence,
    int CandidateCount,
    int TotalCandidates,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FuzzySymbolCandidate? SelectedCandidate,
    IReadOnlyList<FuzzySymbolCandidate> Candidates,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FuzzySelectionExplanation? SelectionExplanation);

internal sealed record DiNextAction(string Command, string Workspace, string Reason);

internal sealed record DiGraphResolution(
    IReadOnlyList<DiRegistrationItem> Registrations,
    IReadOnlyList<DiDependencyEdge> Dependencies,
    IReadOnlyList<DiRiskFact> Risks,
    IReadOnlyList<string> Warnings);

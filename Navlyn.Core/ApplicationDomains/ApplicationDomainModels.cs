using System.Text.Json.Serialization;
using Navlyn.Symbols;

namespace Navlyn.ApplicationDomains;

internal sealed record ApplicationDomainProject(
    string Name,
    string? Path,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? TargetFramework);

internal sealed record ApplicationDomainProjectFilter(
    string Filter,
    string Name,
    string? Path,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? TargetFramework);

internal sealed record ApplicationDomainEvidence(
    string Kind,
    string Path,
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    IReadOnlyList<string> ReasonCodes);

internal sealed record ApplicationDomainSymbol(
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

internal sealed record ApplicationDomainSelectionInput(
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

internal sealed record ApplicationDomainSelectionSection(
    string Confidence,
    int CandidateCount,
    int TotalCandidates,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FuzzySymbolCandidate? SelectedCandidate,
    IReadOnlyList<FuzzySymbolCandidate> Candidates,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FuzzySelectionExplanation? SelectionExplanation);

internal sealed record ApplicationDomainSection<T>(
    int TotalItems,
    int Limit,
    bool Truncated,
    IReadOnlyList<T> Items);

internal sealed record RouteMapOptions(
    int RouteLimit,
    int EvidenceLimit,
    IReadOnlyList<string> RouteFilters,
    IReadOnlyList<string> EndpointKinds,
    string AuthFilter,
    bool IncludeSnippets,
    int SnippetLines,
    bool ExcludeGenerated);

internal sealed record RouteMapResult(
    string Workspace,
    string Kind,
    string Command,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<ApplicationDomainProjectFilter>? Projects,
    RouteMapFilters Filters,
    RouteMapLimits Limits,
    ApplicationDomainSection<RouteEndpointFact> Routes,
    bool Truncated,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<ApplicationDomainNextAction> NextActions);

internal sealed record RouteImpactResult(
    string Workspace,
    string Kind,
    string Command,
    string Route,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<ApplicationDomainProjectFilter>? Projects,
    RouteMapLimits Limits,
    ApplicationDomainSection<RouteEndpointFact> Routes,
    bool Truncated,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<ApplicationDomainNextAction> NextActions);

internal sealed record RouteMapFilters(
    IReadOnlyList<string> Routes,
    IReadOnlyList<string> EndpointKinds,
    string Auth);

internal sealed record RouteMapLimits(int RouteLimit, int EvidenceLimit);

internal sealed record RouteEndpointFact(
    string EndpointKind,
    IReadOnlyList<string> HttpMethods,
    string? RoutePattern,
    string? NormalizedRoutePattern,
    ApplicationDomainSymbol Handler,
    RouteAuthFact Auth,
    ApplicationDomainProject Project,
    string Path,
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    string Confidence,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<ApplicationDomainEvidence> Evidence,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FuzzySnippet? Snippet);

internal sealed record RouteAuthFact(
    string Kind,
    IReadOnlyList<string> Policies,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Schemes,
    IReadOnlyList<ApplicationDomainEvidence> Evidence);

internal sealed record OptionsGraphOptions(
    string? Query,
    int OptionLimit,
    int ConsumerLimit,
    int BindingLimit,
    int EvidenceLimit,
    bool IncludeSnippets,
    int SnippetLines,
    bool ExcludeGenerated);

internal sealed record OptionsGraphResult(
    string Workspace,
    string Kind,
    string Command,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<ApplicationDomainProjectFilter>? Projects,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Query,
    OptionsGraphLimits Limits,
    ApplicationDomainSection<OptionTypeFact> Options,
    ApplicationDomainSection<OptionBindingFact> Bindings,
    ApplicationDomainSection<OptionConsumerFact> Consumers,
    ApplicationDomainSection<OptionValidationFact> Validations,
    bool Truncated,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<ApplicationDomainNextAction> NextActions);

internal sealed record ConfigImpactResult(
    string Workspace,
    string Kind,
    string Command,
    string Query,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<ApplicationDomainProjectFilter>? Projects,
    OptionsGraphLimits Limits,
    ApplicationDomainSection<OptionTypeFact> Options,
    ApplicationDomainSection<OptionBindingFact> Bindings,
    ApplicationDomainSection<OptionConsumerFact> Consumers,
    ApplicationDomainSection<OptionValidationFact> Validations,
    bool Truncated,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<ApplicationDomainNextAction> NextActions);

internal sealed record OptionsGraphLimits(int OptionLimit, int ConsumerLimit, int BindingLimit, int EvidenceLimit);

internal sealed record OptionTypeFact(
    ApplicationDomainSymbol Type,
    ApplicationDomainProject Project,
    string Confidence,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<ApplicationDomainEvidence> Evidence);

internal sealed record OptionBindingFact(
    ApplicationDomainSymbol OptionType,
    string? ConfigurationKey,
    string BindingKind,
    ApplicationDomainProject Project,
    string Path,
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    string Confidence,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<ApplicationDomainEvidence> Evidence);

internal sealed record OptionConsumerFact(
    ApplicationDomainSymbol ConsumerType,
    ApplicationDomainSymbol OptionType,
    string ConsumerKind,
    ApplicationDomainProject Project,
    string Path,
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    string Confidence,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<ApplicationDomainEvidence> Evidence);

internal sealed record OptionValidationFact(
    ApplicationDomainSymbol OptionType,
    string ValidationKind,
    ApplicationDomainProject Project,
    string Path,
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    string Confidence,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<ApplicationDomainEvidence> Evidence);

internal sealed record MessageFlowOptions(
    int HandlerLimit,
    int CallSiteLimit,
    int EvidenceLimit,
    bool IncludeSnippets,
    int SnippetLines,
    bool ExcludeGenerated);

internal sealed record MessageFlowResult(
    string Workspace,
    string Kind,
    string Command,
    ApplicationDomainSelectionInput SelectionInput,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ApplicationDomainSelectionSection? Selection,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ApplicationDomainSymbol? Subject,
    MessageFlowLimits Limits,
    ApplicationDomainSection<MessageHandlerFact> Handlers,
    ApplicationDomainSection<MessageCallSiteFact> CallSites,
    bool Truncated,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<ApplicationDomainNextAction> NextActions);

internal sealed record MessageFlowLimits(int CandidateLimit, int HandlerLimit, int CallSiteLimit, int EvidenceLimit);

internal sealed record MessageHandlerFact(
    string MessageKind,
    ApplicationDomainSymbol MessageType,
    ApplicationDomainSymbol HandlerType,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ApplicationDomainSymbol? HandleMethod,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ApplicationDomainSymbol? ResponseType,
    ApplicationDomainProject Project,
    string Confidence,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<ApplicationDomainEvidence> Evidence);

internal sealed record MessageCallSiteFact(
    string CallKind,
    ApplicationDomainSymbol MessageType,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ApplicationDomainSymbol? ContainingSymbol,
    ApplicationDomainProject Project,
    string Path,
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    string Confidence,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<ApplicationDomainEvidence> Evidence);

internal sealed record EfModelOptions(
    string? EntityQuery,
    string? DbContextQuery,
    int EntityLimit,
    int QuerySiteLimit,
    int EvidenceLimit,
    bool IncludeSnippets,
    int SnippetLines,
    bool ExcludeGenerated);

internal sealed record EfModelResult(
    string Workspace,
    string Kind,
    string Command,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<ApplicationDomainProjectFilter>? Projects,
    EfModelFilters Filters,
    EfModelLimits Limits,
    ApplicationDomainSection<EfDbContextFact> DbContexts,
    ApplicationDomainSection<EfEntityFact> Entities,
    ApplicationDomainSection<EfDbSetFact> DbSets,
    ApplicationDomainSection<EfConfigurationFact> Configurations,
    ApplicationDomainSection<EfQuerySiteFact> QuerySites,
    bool Truncated,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<ApplicationDomainNextAction> NextActions);

internal sealed record EntityImpactResult(
    string Workspace,
    string Kind,
    string Command,
    ApplicationDomainSelectionInput SelectionInput,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ApplicationDomainSelectionSection? Selection,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ApplicationDomainSymbol? Subject,
    EfModelLimits Limits,
    ApplicationDomainSection<EfDbSetFact> DbSets,
    ApplicationDomainSection<EfConfigurationFact> Configurations,
    ApplicationDomainSection<EfQuerySiteFact> QuerySites,
    bool Truncated,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<ApplicationDomainNextAction> NextActions);

internal sealed record EfModelFilters(string? Entity, string? DbContext);

internal sealed record EfModelLimits(int CandidateLimit, int EntityLimit, int QuerySiteLimit, int EvidenceLimit);

internal sealed record EfDbContextFact(ApplicationDomainSymbol Type, ApplicationDomainProject Project, string Confidence, IReadOnlyList<string> ReasonCodes, IReadOnlyList<ApplicationDomainEvidence> Evidence);

internal sealed record EfEntityFact(ApplicationDomainSymbol Type, ApplicationDomainProject Project, string Confidence, IReadOnlyList<string> ReasonCodes, IReadOnlyList<ApplicationDomainEvidence> Evidence);

internal sealed record EfDbSetFact(
    ApplicationDomainSymbol DbContext,
    ApplicationDomainSymbol EntityType,
    string PropertyName,
    ApplicationDomainProject Project,
    string Path,
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    string Confidence,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<ApplicationDomainEvidence> Evidence);

internal sealed record EfConfigurationFact(
    ApplicationDomainSymbol EntityType,
    ApplicationDomainSymbol ConfigurationType,
    ApplicationDomainProject Project,
    string Confidence,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<ApplicationDomainEvidence> Evidence);

internal sealed record EfQuerySiteFact(
    ApplicationDomainSymbol EntityType,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ApplicationDomainSymbol? ContainingSymbol,
    string QueryKind,
    ApplicationDomainProject Project,
    string Path,
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    string Confidence,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<ApplicationDomainEvidence> Evidence);

internal sealed record PackageUsageOptions(
    string Package,
    IReadOnlyList<string> NamespaceHints,
    int UsageLimit,
    int ReferenceLimit,
    bool IncludeTests,
    bool ExcludeGenerated);

internal sealed record PackageUsageResult(
    string Workspace,
    string Kind,
    string Command,
    string Package,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<ApplicationDomainProjectFilter>? Projects,
    PackageUsageLimits Limits,
    ApplicationDomainSection<PackageReferenceFact> PackageReferences,
    ApplicationDomainSection<PackageSourceUsageFact> Usages,
    bool Truncated,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<ApplicationDomainNextAction> NextActions);

internal sealed record PackageImpactResult(
    string Workspace,
    string Kind,
    string Command,
    string Package,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<ApplicationDomainProjectFilter>? Projects,
    PackageUsageLimits Limits,
    ApplicationDomainSection<PackageReferenceFact> PackageReferences,
    ApplicationDomainSection<PackageSourceUsageFact> Usages,
    bool Truncated,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<ApplicationDomainNextAction> NextActions);

internal sealed record PackageUsageLimits(int UsageLimit, int ReferenceLimit);

internal sealed record PackageReferenceFact(
    string Name,
    string? Version,
    bool IsCentralVersion,
    ApplicationDomainProject Project,
    string Confidence,
    IReadOnlyList<string> ReasonCodes);

internal sealed record PackageSourceUsageFact(
    string UsageKind,
    string NamespaceOrAssembly,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ApplicationDomainSymbol? Symbol,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ApplicationDomainSymbol? ContainingSymbol,
    ApplicationDomainProject Project,
    string Path,
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    string Confidence,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<ApplicationDomainEvidence> Evidence);

internal sealed record ApplicationDomainNextAction(string Command, string Workspace, string Reason);

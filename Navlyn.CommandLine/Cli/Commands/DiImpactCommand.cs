using System.CommandLine;
using Navlyn.Cli.OutputProfiles;
using Navlyn.DependencyInjection;
using Navlyn.Diagnostics;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class DiImpactCommand
{
    private const int DefaultCandidateLimit = 20;
    private const int DefaultRegistrationLimit = 50;
    private const int DefaultConsumerLimit = 50;
    private const int DefaultDependencyLimit = 100;
    private const int DefaultRiskLimit = 50;
    private const int DefaultDepth = 2;

    public static Command Create()
    {
        Option<string?> queryOption = new("--query") { Description = "Type query." };
        Option<string?> candidateIdOption = FuzzyCommandSupport.CreateCandidateIdOption();
        Option<FileInfo?> fileOption = new("--file") { Description = "Source file for source-position mode." };
        Option<int?> lineOption = new("--line") { Description = "1-based source line for source-position mode." };
        Option<int?> columnOption = new("--column") { Description = "1-based source column for source-position mode." };
        Option<string[]> assumeKindOption = FuzzyCommandSupport.CreateAssumeKindOption();
        Option<string> matchOption = FuzzyCommandSupport.CreateMatchOption();
        Option<bool> caseSensitiveOption = SharedOptions.CreateCaseSensitiveOption();
        Option<string[]> projectOption = SharedOptions.CreateProjectFiltersOption();
        Option<bool> excludeGeneratedOption = SharedOptions.CreateExcludeGeneratedOption();
        Option<int?> candidateLimitOption = new("--candidate-limit") { Description = $"Maximum fuzzy candidates. Defaults to {DefaultCandidateLimit}." };
        Option<int?> registrationLimitOption = new("--registration-limit") { Description = $"Maximum registrations. Defaults to {DefaultRegistrationLimit}." };
        Option<int?> consumerLimitOption = new("--consumer-limit") { Description = $"Maximum consumers. Defaults to {DefaultConsumerLimit}." };
        Option<int?> dependencyLimitOption = new("--dependency-limit") { Description = $"Maximum dependency edges. Defaults to {DefaultDependencyLimit}." };
        Option<int?> riskLimitOption = new("--risk-limit") { Description = $"Maximum risk facts. Defaults to {DefaultRiskLimit}." };
        Option<int?> depthOption = new("--depth") { Description = $"Constructor dependency traversal depth. Defaults to {DefaultDepth}." };
        Option<bool> includeSnippetsOption = FuzzyCommandSupport.CreateIncludeSnippetsOption();
        Option<int?> snippetLinesOption = FuzzyCommandSupport.CreateSnippetLinesOption();
        Option<string> candidatePolicyOption = FuzzyCommandSupport.CreateCandidatePolicyOption("fail");
        Option<string> minConfidenceOption = FuzzyCommandSupport.CreateMinConfidenceOption("medium");
        Option<bool> explainSelectionOption = FuzzyCommandSupport.CreateExplainSelectionOption();
        Option<string> profileOption = OutputProfile.CreateOption();

        return WorkspaceCommand.Create(
            "di-impact",
            "Estimate source-level DI impact for a selected type.",
            [queryOption, candidateIdOption, fileOption, lineOption, columnOption, assumeKindOption, matchOption, caseSensitiveOption, projectOption, excludeGeneratedOption, candidateLimitOption, registrationLimitOption, consumerLimitOption, dependencyLimitOption, riskLimitOption, depthOption, includeSnippetsOption, snippetLinesOption, candidatePolicyOption, minConfidenceOption, explainSelectionOption, profileOption],
            (workspace, parseResult, cancellationToken) => ExecuteAsync(
                workspace,
                parseResult.GetValue(queryOption),
                parseResult.GetValue(candidateIdOption),
                parseResult.GetValue(fileOption),
                parseResult.GetValue(lineOption),
                parseResult.GetValue(columnOption),
                parseResult.GetValue(assumeKindOption) ?? [],
                parseResult.GetValue(matchOption)!,
                parseResult.GetValue(caseSensitiveOption),
                parseResult.GetValue(projectOption) ?? [],
                parseResult.GetValue(excludeGeneratedOption),
                parseResult.GetValue(candidateLimitOption),
                parseResult.GetValue(registrationLimitOption),
                parseResult.GetValue(consumerLimitOption),
                parseResult.GetValue(dependencyLimitOption),
                parseResult.GetValue(riskLimitOption),
                parseResult.GetValue(depthOption),
                parseResult.GetValue(includeSnippetsOption),
                parseResult.GetValue(snippetLinesOption),
                parseResult.GetValue(candidatePolicyOption)!,
                parseResult.GetValue(minConfidenceOption)!,
                parseResult.GetValue(explainSelectionOption),
                parseResult.GetValue(profileOption)!,
                cancellationToken));
    }

    private static async Task<int> ExecuteAsync(
        LoadedWorkspace workspace,
        string? query,
        string? candidateId,
        FileInfo? file,
        int? line,
        int? column,
        IReadOnlyList<string> assumeKinds,
        string match,
        bool caseSensitive,
        IReadOnlyList<string> projectFilters,
        bool excludeGenerated,
        int? candidateLimit,
        int? registrationLimit,
        int? consumerLimit,
        int? dependencyLimit,
        int? riskLimit,
        int? depth,
        bool includeSnippets,
        int? snippetLines,
        string candidatePolicy,
        string minConfidence,
        bool explainSelection,
        string profile,
        CancellationToken cancellationToken)
    {
        int effectiveCandidateLimit = candidateLimit ?? DefaultCandidateLimit;
        int effectiveRegistrationLimit = registrationLimit ?? DefaultRegistrationLimit;
        int effectiveConsumerLimit = consumerLimit ?? DefaultConsumerLimit;
        int effectiveDependencyLimit = dependencyLimit ?? DefaultDependencyLimit;
        int effectiveRiskLimit = riskLimit ?? DefaultRiskLimit;
        int effectiveDepth = depth ?? DefaultDepth;
        int effectiveSnippetLines = snippetLines ?? FuzzyDiscoveryResolver.DefaultSnippetLines;
        if (!ValidateLimits(effectiveCandidateLimit, effectiveRegistrationLimit, effectiveConsumerLimit, effectiveDependencyLimit, effectiveRiskLimit, effectiveDepth, effectiveSnippetLines, out int exitCode))
        {
            return exitCode;
        }

        if (!DiGraphCommand.ResolveProjects(workspace, projectFilters, out IReadOnlyList<Microsoft.CodeAnalysis.Project> projects, out _, out exitCode))
        {
            return exitCode;
        }

        DiSubjectResolution subject = await DiCommandSupport.ResolveSubjectAsync(
            workspace,
            "di-impact",
            query,
            candidateId,
            file,
            line,
            column,
            assumeKinds,
            match,
            caseSensitive,
            projectFilters,
            excludeGenerated,
            effectiveCandidateLimit,
            candidatePolicy,
            minConfidence,
            explainSelection,
            cancellationToken);
        if (!subject.Success)
        {
            if (subject.DiagnosticId is not null && subject.DiagnosticMessage is not null)
            {
                DiagnosticReporter.WriteError(subject.DiagnosticId.Value, subject.DiagnosticMessage);
            }

            return subject.ExitCode;
        }

        DiGraphResolution graph = await new DiRegistrationResolver().ResolveAsync(
            workspace,
            projects,
            new DiGraphOptions(
                effectiveRegistrationLimit,
                effectiveDependencyLimit,
                effectiveRiskLimit,
                IncludeOptions: true,
                IncludeHostedServices: true,
                IncludeRisks: true,
                includeSnippets,
                effectiveSnippetLines,
                excludeGenerated),
            cancellationToken);

        IReadOnlyList<DiRegistrationItem> registrations = subject.Subject is null
            ? []
            : [.. graph.Registrations.Where(item =>
                DiRegistrationResolver.Matches(item.ServiceType, subject.Subject) ||
                DiRegistrationResolver.Matches(item.ImplementationType, subject.Subject) ||
                DiRegistrationResolver.Matches(item.Instance?.Type, subject.Subject))];
        IReadOnlyList<DiDependencyEdge> dependencies = subject.Subject is null
            ? []
            : [.. graph.Dependencies.Where(edge => DiRegistrationResolver.Matches(edge.ImplementationType, subject.Subject))];
        IReadOnlyList<DiConsumerItem> consumers = subject.Subject is null
            ? []
            : [.. graph.Dependencies
                .Where(edge => DiRegistrationResolver.Matches(edge.DependencyType, subject.Subject))
                .Select(edge => new DiConsumerItem(edge.ImplementationType, edge.DependencyType, ["constructor-depends-on-selected-type"], edge.Evidence))];
        IReadOnlyList<DiRiskFact> risks = subject.Subject is null
            ? []
            : [.. graph.Risks.Where(risk =>
                DiRegistrationResolver.Matches(risk.ServiceType, subject.Subject) ||
                DiRegistrationResolver.Matches(risk.ImplementationType, subject.Subject) ||
                DiRegistrationResolver.Matches(risk.DependencyType, subject.Subject))];

        List<string> warnings = [.. graph.Warnings];
        if (subject.Subject is null)
        {
            warnings.Add("no-selected-symbol");
        }

        DiRegistrationsSection registrationSection = DiRegistrationResolver.CreateRegistrationsSection(registrations, effectiveRegistrationLimit);
        DiDependenciesSection dependencySection = DiRegistrationResolver.CreateDependenciesSection(dependencies, effectiveDependencyLimit);
        DiConsumersSection consumerSection = CreateConsumersSection(consumers, effectiveConsumerLimit);
        DiRisksSection riskSection = DiRegistrationResolver.CreateRisksSection(risks, effectiveRiskLimit);
        DiImpactResult result = new(
            Workspace: workspace.DisplayPath,
            Kind: workspace.Kind,
            Command: "di-impact",
            SelectionInput: subject.SelectionInput!,
            Selection: subject.Selection,
            Subject: subject.Subject,
            Limits: new DiImpactLimits(effectiveCandidateLimit, effectiveRegistrationLimit, effectiveConsumerLimit, effectiveDependencyLimit, effectiveRiskLimit, effectiveDepth),
            Registrations: registrationSection,
            ConstructorDependencies: dependencySection,
            Consumers: consumerSection,
            Risks: riskSection,
            Truncated: registrationSection.Truncated || dependencySection.Truncated || consumerSection.Truncated || riskSection.Truncated,
            Warnings: [.. warnings.Distinct(StringComparer.Ordinal).OrderBy(warning => warning, StringComparer.Ordinal)],
            NextActions: []);
        ConsoleJsonWriter.Write(OutputProfile.Format(workspace, "di-impact", profile, result, new
        {
            inputMode = !string.IsNullOrWhiteSpace(query) ? "query" : !string.IsNullOrWhiteSpace(candidateId) ? "candidateId" : "sourcePosition",
            projectFilters,
            excludeGenerated,
            candidateLimit = effectiveCandidateLimit,
            registrationLimit = effectiveRegistrationLimit,
            consumerLimit = effectiveConsumerLimit,
            dependencyLimit = effectiveDependencyLimit,
            riskLimit = effectiveRiskLimit,
            depth = effectiveDepth,
            includeSnippets,
            snippetLines = effectiveSnippetLines
        }));
        return ExitCodes.Success;
    }

    private static DiConsumersSection CreateConsumersSection(IReadOnlyList<DiConsumerItem> consumers, int limit)
    {
        IReadOnlyList<DiConsumerItem> ordered = [.. consumers
            .GroupBy(consumer => DiRegistrationResolver.Identity(consumer.ConsumerType), StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(consumer => DiRegistrationResolver.Identity(consumer.ConsumerType), StringComparer.Ordinal)];
        return new DiConsumersSection(ordered.Count, limit, ordered.Count > limit, [.. ordered.Take(limit)]);
    }

    private static bool ValidateLimits(int candidateLimit, int registrationLimit, int consumerLimit, int dependencyLimit, int riskLimit, int depth, int snippetLines, out int exitCode)
    {
        return FuzzyCommandSupport.TryCreatePositiveOption("--candidate-limit", candidateLimit, out exitCode) &&
            FuzzyCommandSupport.TryCreatePositiveOption("--registration-limit", registrationLimit, out exitCode) &&
            FuzzyCommandSupport.TryCreatePositiveOption("--consumer-limit", consumerLimit, out exitCode) &&
            FuzzyCommandSupport.TryCreatePositiveOption("--dependency-limit", dependencyLimit, out exitCode) &&
            FuzzyCommandSupport.TryCreatePositiveOption("--risk-limit", riskLimit, out exitCode) &&
            FuzzyCommandSupport.TryCreateNonNegativeOption("--depth", depth, out exitCode) &&
            FuzzyCommandSupport.TryCreateNonNegativeOption("--snippet-lines", snippetLines, out exitCode);
    }
}

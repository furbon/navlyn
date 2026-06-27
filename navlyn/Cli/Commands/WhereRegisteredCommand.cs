using System.CommandLine;
using Navlyn.Cli.OutputProfiles;
using Navlyn.DependencyInjection;
using Navlyn.Diagnostics;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class WhereRegisteredCommand
{
    private const int DefaultCandidateLimit = 20;
    private const int DefaultRegistrationLimit = 50;
    private const int DefaultDependencyLimit = 100;

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
        Option<int?> dependencyLimitOption = new("--dependency-limit") { Description = $"Maximum dependency edges. Defaults to {DefaultDependencyLimit}." };
        Option<bool> includeSnippetsOption = FuzzyCommandSupport.CreateIncludeSnippetsOption();
        Option<int?> snippetLinesOption = FuzzyCommandSupport.CreateSnippetLinesOption();
        Option<string> candidatePolicyOption = FuzzyCommandSupport.CreateCandidatePolicyOption("fail");
        Option<string> minConfidenceOption = FuzzyCommandSupport.CreateMinConfidenceOption("medium");
        Option<bool> explainSelectionOption = FuzzyCommandSupport.CreateExplainSelectionOption();
        Option<string> profileOption = OutputProfile.CreateOption();

        return WorkspaceCommand.Create(
            "where-registered",
            "Find source-level DI registrations for a selected type.",
            [queryOption, candidateIdOption, fileOption, lineOption, columnOption, assumeKindOption, matchOption, caseSensitiveOption, projectOption, excludeGeneratedOption, candidateLimitOption, registrationLimitOption, dependencyLimitOption, includeSnippetsOption, snippetLinesOption, candidatePolicyOption, minConfidenceOption, explainSelectionOption, profileOption],
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
                parseResult.GetValue(dependencyLimitOption),
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
        int? dependencyLimit,
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
        int effectiveDependencyLimit = dependencyLimit ?? DefaultDependencyLimit;
        int effectiveSnippetLines = snippetLines ?? FuzzyDiscoveryResolver.DefaultSnippetLines;
        if (!ValidateLimits(effectiveCandidateLimit, effectiveRegistrationLimit, effectiveDependencyLimit, effectiveSnippetLines, out int exitCode))
        {
            return exitCode;
        }

        if (!DiGraphCommand.ResolveProjects(workspace, projectFilters, out IReadOnlyList<Microsoft.CodeAnalysis.Project> projects, out _, out exitCode))
        {
            return exitCode;
        }

        DiSubjectResolution subject = await DiCommandSupport.ResolveSubjectAsync(
            workspace,
            "where-registered",
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
            new DiGraphOptions(effectiveRegistrationLimit, effectiveDependencyLimit, RiskLimit: 1, IncludeOptions: true, IncludeHostedServices: true, IncludeRisks: false, includeSnippets, effectiveSnippetLines, excludeGenerated),
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
        List<string> warnings = [.. graph.Warnings];
        if (subject.Subject is null)
        {
            warnings.Add("no-selected-symbol");
        }
        else if (registrations.Count == 0)
        {
            warnings.Add("selected-type-not-registered");
        }

        DiRegistrationsSection registrationSection = DiRegistrationResolver.CreateRegistrationsSection(registrations, effectiveRegistrationLimit);
        DiDependenciesSection dependencySection = DiRegistrationResolver.CreateDependenciesSection(dependencies, effectiveDependencyLimit);
        WhereRegisteredResult result = new(
            Workspace: workspace.DisplayPath,
            Kind: workspace.Kind,
            Command: "where-registered",
            SelectionInput: subject.SelectionInput!,
            Selection: subject.Selection,
            Subject: subject.Subject,
            Limits: new DiWhereRegisteredLimits(effectiveCandidateLimit, effectiveRegistrationLimit, effectiveDependencyLimit),
            Registrations: registrationSection,
            ConstructorDependencies: dependencySection,
            Truncated: registrationSection.Truncated || dependencySection.Truncated,
            Warnings: [.. warnings.Distinct(StringComparer.Ordinal).OrderBy(warning => warning, StringComparer.Ordinal)],
            NextActions: []);
        ConsoleJsonWriter.Write(OutputProfile.Format(workspace, "where-registered", profile, result, new
        {
            inputMode = !string.IsNullOrWhiteSpace(query) ? "query" : !string.IsNullOrWhiteSpace(candidateId) ? "candidateId" : "sourcePosition",
            projectFilters,
            excludeGenerated,
            candidateLimit = effectiveCandidateLimit,
            registrationLimit = effectiveRegistrationLimit,
            dependencyLimit = effectiveDependencyLimit,
            includeSnippets,
            snippetLines = effectiveSnippetLines
        }));
        return ExitCodes.Success;
    }

    internal static bool ValidateLimits(int candidateLimit, int registrationLimit, int dependencyLimit, int snippetLines, out int exitCode)
    {
        return FuzzyCommandSupport.TryCreatePositiveOption("--candidate-limit", candidateLimit, out exitCode) &&
            FuzzyCommandSupport.TryCreatePositiveOption("--registration-limit", registrationLimit, out exitCode) &&
            FuzzyCommandSupport.TryCreatePositiveOption("--dependency-limit", dependencyLimit, out exitCode) &&
            FuzzyCommandSupport.TryCreateNonNegativeOption("--snippet-lines", snippetLines, out exitCode);
    }
}

using System.CommandLine;
using Navlyn.Cli.OutputProfiles;
using Navlyn.DependencyInjection;
using Navlyn.Diagnostics;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class DiGraphCommand
{
    private const int DefaultRegistrationLimit = 200;
    private const int DefaultDependencyLimit = 300;
    private const int DefaultRiskLimit = 100;

    public static Command Create()
    {
        Option<string[]> projectOption = SharedOptions.CreateProjectFiltersOption();
        Option<int?> registrationLimitOption = new("--registration-limit") { Description = $"Maximum registrations. Defaults to {DefaultRegistrationLimit}." };
        Option<int?> dependencyLimitOption = new("--dependency-limit") { Description = $"Maximum constructor dependency edges. Defaults to {DefaultDependencyLimit}." };
        Option<int?> riskLimitOption = new("--risk-limit") { Description = $"Maximum DI risk facts. Defaults to {DefaultRiskLimit}." };
        Option<bool> includeOptionsOption = CreateDefaultTrueOption("--include-options", "Include options registrations.");
        Option<bool> includeHostedServicesOption = CreateDefaultTrueOption("--include-hosted-services", "Include hosted service registrations.");
        Option<bool> includeRisksOption = CreateDefaultTrueOption("--include-risks", "Include conservative DI risk facts.");
        Option<bool> excludeGeneratedOption = SharedOptions.CreateExcludeGeneratedOption();
        Option<bool> includeSnippetsOption = FuzzyCommandSupport.CreateIncludeSnippetsOption();
        Option<int?> snippetLinesOption = FuzzyCommandSupport.CreateSnippetLinesOption();
        Option<string> profileOption = OutputProfile.CreateOption();

        return WorkspaceCommand.Create(
            "di-graph",
            "Report source-level Microsoft.Extensions.DependencyInjection registration facts.",
            [projectOption, registrationLimitOption, dependencyLimitOption, riskLimitOption, includeOptionsOption, includeHostedServicesOption, includeRisksOption, excludeGeneratedOption, includeSnippetsOption, snippetLinesOption, profileOption],
            (workspace, parseResult, cancellationToken) => ExecuteAsync(
                workspace,
                parseResult.GetValue(projectOption) ?? [],
                parseResult.GetValue(registrationLimitOption),
                parseResult.GetValue(dependencyLimitOption),
                parseResult.GetValue(riskLimitOption),
                parseResult.GetValue(includeOptionsOption),
                parseResult.GetValue(includeHostedServicesOption),
                parseResult.GetValue(includeRisksOption),
                parseResult.GetValue(excludeGeneratedOption),
                parseResult.GetValue(includeSnippetsOption),
                parseResult.GetValue(snippetLinesOption),
                parseResult.GetValue(profileOption)!,
                cancellationToken));
    }

    private static async Task<int> ExecuteAsync(
        LoadedWorkspace workspace,
        IReadOnlyList<string> projectFilters,
        int? registrationLimit,
        int? dependencyLimit,
        int? riskLimit,
        bool includeOptions,
        bool includeHostedServices,
        bool includeRisks,
        bool excludeGenerated,
        bool includeSnippets,
        int? snippetLines,
        string profile,
        CancellationToken cancellationToken)
    {
        int effectiveRegistrationLimit = registrationLimit ?? DefaultRegistrationLimit;
        int effectiveDependencyLimit = dependencyLimit ?? DefaultDependencyLimit;
        int effectiveRiskLimit = riskLimit ?? DefaultRiskLimit;
        int effectiveSnippetLines = snippetLines ?? FuzzyDiscoveryResolver.DefaultSnippetLines;
        if (!ValidateLimits(effectiveRegistrationLimit, effectiveDependencyLimit, effectiveRiskLimit, effectiveSnippetLines, out int exitCode))
        {
            return exitCode;
        }

        if (!ResolveProjects(workspace, projectFilters, out IReadOnlyList<Microsoft.CodeAnalysis.Project> projects, out IReadOnlyList<DiProjectFilter>? projectOutputs, out exitCode))
        {
            return exitCode;
        }

        DiGraphResolution resolution = await new DiRegistrationResolver().ResolveAsync(
            workspace,
            projects,
            new DiGraphOptions(
                effectiveRegistrationLimit,
                effectiveDependencyLimit,
                effectiveRiskLimit,
                includeOptions,
                includeHostedServices,
                includeRisks,
                includeSnippets,
                effectiveSnippetLines,
                excludeGenerated),
            cancellationToken);

        DiRegistrationsSection registrations = DiRegistrationResolver.CreateRegistrationsSection(resolution.Registrations, effectiveRegistrationLimit);
        DiDependenciesSection dependencies = DiRegistrationResolver.CreateDependenciesSection(resolution.Dependencies, effectiveDependencyLimit);
        DiRisksSection risks = DiRegistrationResolver.CreateRisksSection(resolution.Risks, effectiveRiskLimit);
        DiGraphResult result = new(
            Workspace: workspace.DisplayPath,
            Kind: workspace.Kind,
            Command: "di-graph",
            Projects: projectOutputs,
            Limits: new DiGraphLimits(effectiveRegistrationLimit, effectiveDependencyLimit, effectiveRiskLimit),
            Registrations: registrations,
            Dependencies: dependencies,
            Risks: risks,
            Truncated: registrations.Truncated || dependencies.Truncated || risks.Truncated,
            Warnings: resolution.Warnings,
            NextActions: []);
        ConsoleJsonWriter.Write(OutputProfile.Format(workspace, "di-graph", profile, result, new
        {
            projectFilters,
            registrationLimit = effectiveRegistrationLimit,
            dependencyLimit = effectiveDependencyLimit,
            riskLimit = effectiveRiskLimit,
            includeOptions,
            includeHostedServices,
            includeRisks,
            excludeGenerated,
            includeSnippets,
            snippetLines = effectiveSnippetLines
        }));
        return ExitCodes.Success;
    }

    internal static bool ResolveProjects(
        LoadedWorkspace workspace,
        IReadOnlyList<string> projectFilters,
        out IReadOnlyList<Microsoft.CodeAnalysis.Project> projects,
        out IReadOnlyList<DiProjectFilter>? projectOutputs,
        out int exitCode)
    {
        ProjectFilterResolutionResult projectResult = new ProjectFilterResolver().ResolveMany(workspace.Solution, projectFilters);
        if (projectResult.Error is not null)
        {
            DiagnosticReporter.WriteError(projectResult.Error.DiagnosticId, projectResult.Error.Message);
            projects = [];
            projectOutputs = null;
            exitCode = projectResult.Error.ExitCode;
            return false;
        }

        projects = projectResult.Projects;
        projectOutputs = projectResult.AppliedFilters.Count == 0
            ? null
            : projectResult.AppliedFilters.Select(filter => new DiProjectFilter(filter.Filter, filter.Name, filter.Path, filter.TargetFramework)).ToArray();
        exitCode = ExitCodes.Success;
        return true;
    }

    internal static bool ValidateLimits(int registrationLimit, int dependencyLimit, int riskLimit, int snippetLines, out int exitCode)
    {
        return FuzzyCommandSupport.TryCreatePositiveOption("--registration-limit", registrationLimit, out exitCode) &&
            FuzzyCommandSupport.TryCreatePositiveOption("--dependency-limit", dependencyLimit, out exitCode) &&
            FuzzyCommandSupport.TryCreatePositiveOption("--risk-limit", riskLimit, out exitCode) &&
            FuzzyCommandSupport.TryCreateNonNegativeOption("--snippet-lines", snippetLines, out exitCode);
    }

    private static Option<bool> CreateDefaultTrueOption(string name, string description)
    {
        return new Option<bool>(name)
        {
            Description = description,
            DefaultValueFactory = _ => true
        };
    }
}

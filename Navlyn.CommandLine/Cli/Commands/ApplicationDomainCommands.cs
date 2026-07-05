using System.CommandLine;
using Navlyn.ApplicationDomains;
using Navlyn.Cli.OutputProfiles;
using Navlyn.Diagnostics;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class RouteMapCommand
{
    public static Command Create()
    {
        Option<string[]> projectOption = SharedOptions.CreateProjectFiltersOption();
        Option<string[]> routeOption = new("--route") { Description = "Route pattern fragment filter. Can be specified more than once." };
        routeOption.AllowMultipleArgumentsPerToken = true;
        Option<string[]> endpointKindOption = new("--endpoint-kind") { Description = "Endpoint kind filter: controller-action, minimal-api, or any. Can be specified more than once." };
        endpointKindOption.AllowMultipleArgumentsPerToken = true;
        Option<string> authOption = new("--auth") { Description = "Auth filter: any, required, anonymous, or unknown.", DefaultValueFactory = _ => "any" };
        authOption.AcceptOnlyFromAmong("any", "required", "anonymous", "unknown");
        Option<int?> routeLimitOption = new("--route-limit") { Description = $"Maximum routes. Defaults to {ApplicationDomainCommandSupport.DefaultRouteLimit}." };
        Option<int?> evidenceLimitOption = new("--evidence-limit") { Description = $"Maximum evidence items per fact. Defaults to {ApplicationDomainCommandSupport.DefaultEvidenceLimit}." };
        Option<bool> excludeGeneratedOption = SharedOptions.CreateExcludeGeneratedOption();
        Option<bool> includeSnippetsOption = FuzzyCommandSupport.CreateIncludeSnippetsOption();
        Option<int?> snippetLinesOption = FuzzyCommandSupport.CreateSnippetLinesOption();
        Option<string> profileOption = OutputProfile.CreateOption();

        return WorkspaceCommand.Create(
            "route-map",
            "Report source-level ASP.NET Core route and auth facts.",
            [projectOption, routeOption, endpointKindOption, authOption, routeLimitOption, evidenceLimitOption, excludeGeneratedOption, includeSnippetsOption, snippetLinesOption, profileOption],
            async (workspace, parseResult, cancellationToken) =>
            {
                int routeLimit = parseResult.GetValue(routeLimitOption) ?? ApplicationDomainCommandSupport.DefaultRouteLimit;
                int evidenceLimit = parseResult.GetValue(evidenceLimitOption) ?? ApplicationDomainCommandSupport.DefaultEvidenceLimit;
                int snippetLines = parseResult.GetValue(snippetLinesOption) ?? FuzzyDiscoveryResolver.DefaultSnippetLines;
                if (!ApplicationDomainCommandSupport.ValidatePositiveLimits(out int exitCode, ("--route-limit", routeLimit), ("--evidence-limit", evidenceLimit)) ||
                    !ApplicationDomainCommandSupport.ValidateNonNegativeLimits(out exitCode, ("--snippet-lines", snippetLines)))
                {
                    return exitCode;
                }

                if (!ApplicationDomainCommandSupport.ResolveProjects(workspace, parseResult.GetValue(projectOption) ?? [], out var projects, out var projectOutputs, out exitCode))
                {
                    return exitCode;
                }

                IReadOnlyList<string> endpointKinds = ApplicationDomainCommandSupport.SplitValues(parseResult.GetValue(endpointKindOption) ?? []);
                if (!ValidateEndpointKinds(endpointKinds))
                {
                    return ExitCodes.UsageError;
                }

                RouteMapResult result = await new ApplicationDomainResolver().ResolveRouteMapAsync(
                    workspace,
                    projects,
                    projectOutputs,
                    new RouteMapOptions(
                        routeLimit,
                        evidenceLimit,
                        ApplicationDomainCommandSupport.SplitValues(parseResult.GetValue(routeOption) ?? []),
                        endpointKinds,
                        parseResult.GetValue(authOption)!,
                        parseResult.GetValue(includeSnippetsOption),
                        snippetLines,
                        parseResult.GetValue(excludeGeneratedOption)),
                    cancellationToken);

                ConsoleJsonWriter.Write(OutputProfile.Format(workspace, "route-map", parseResult.GetValue(profileOption)!, result, new
                {
                    projectFilters = parseResult.GetValue(projectOption) ?? [],
                    routes = parseResult.GetValue(routeOption) ?? [],
                    endpointKinds,
                    auth = parseResult.GetValue(authOption)!,
                    routeLimit,
                    evidenceLimit,
                    excludeGenerated = parseResult.GetValue(excludeGeneratedOption),
                    includeSnippets = parseResult.GetValue(includeSnippetsOption),
                    snippetLines
                }));
                return ExitCodes.Success;
            });
    }

    internal static bool ValidateEndpointKinds(IReadOnlyList<string> kinds)
    {
        string[] allowed = ["any", "controller-action", "minimal-api"];
        string? invalid = kinds.FirstOrDefault(kind => !allowed.Contains(kind, StringComparer.Ordinal));
        if (invalid is not null)
        {
            DiagnosticReporter.WriteError(DiagnosticIds.ParseError, $"Invalid --endpoint-kind value '{invalid}'. Allowed values: {string.Join(", ", allowed)}.");
            return false;
        }

        return true;
    }
}

internal static class RouteImpactCommand
{
    public static Command Create()
    {
        Option<string> routeOption = new("--route") { Description = "Route pattern fragment to inspect." };
        Option<string[]> projectOption = SharedOptions.CreateProjectFiltersOption();
        Option<int?> routeLimitOption = new("--route-limit") { Description = $"Maximum matched routes. Defaults to {ApplicationDomainCommandSupport.DefaultRouteLimit}." };
        Option<int?> evidenceLimitOption = new("--evidence-limit") { Description = $"Maximum evidence items per fact. Defaults to {ApplicationDomainCommandSupport.DefaultEvidenceLimit}." };
        Option<bool> excludeGeneratedOption = SharedOptions.CreateExcludeGeneratedOption();
        Option<bool> includeSnippetsOption = FuzzyCommandSupport.CreateIncludeSnippetsOption();
        Option<int?> snippetLinesOption = FuzzyCommandSupport.CreateSnippetLinesOption();
        Option<string> profileOption = OutputProfile.CreateOption();

        return WorkspaceCommand.Create(
            "route-impact",
            "Report source-level facts for routes matching a pattern fragment.",
            [routeOption, projectOption, routeLimitOption, evidenceLimitOption, excludeGeneratedOption, includeSnippetsOption, snippetLinesOption, profileOption],
            async (workspace, parseResult, cancellationToken) =>
            {
                string route = parseResult.GetValue(routeOption)!;
                if (string.IsNullOrWhiteSpace(route))
                {
                    DiagnosticReporter.WriteError(DiagnosticIds.ParseError, "--route is required.");
                    return ExitCodes.UsageError;
                }

                int routeLimit = parseResult.GetValue(routeLimitOption) ?? ApplicationDomainCommandSupport.DefaultRouteLimit;
                int evidenceLimit = parseResult.GetValue(evidenceLimitOption) ?? ApplicationDomainCommandSupport.DefaultEvidenceLimit;
                int snippetLines = parseResult.GetValue(snippetLinesOption) ?? FuzzyDiscoveryResolver.DefaultSnippetLines;
                if (!ApplicationDomainCommandSupport.ValidatePositiveLimits(out int exitCode, ("--route-limit", routeLimit), ("--evidence-limit", evidenceLimit)) ||
                    !ApplicationDomainCommandSupport.ValidateNonNegativeLimits(out exitCode, ("--snippet-lines", snippetLines)))
                {
                    return exitCode;
                }

                if (!ApplicationDomainCommandSupport.ResolveProjects(workspace, parseResult.GetValue(projectOption) ?? [], out var projects, out var projectOutputs, out exitCode))
                {
                    return exitCode;
                }

                RouteImpactResult result = await new ApplicationDomainResolver().ResolveRouteImpactAsync(
                    workspace,
                    projects,
                    projectOutputs,
                    route.Trim(),
                    new RouteMapOptions(routeLimit, evidenceLimit, [], [], "any", parseResult.GetValue(includeSnippetsOption), snippetLines, parseResult.GetValue(excludeGeneratedOption)),
                    cancellationToken);

                ConsoleJsonWriter.Write(OutputProfile.Format(workspace, "route-impact", parseResult.GetValue(profileOption)!, result, new
                {
                    route = route.Trim(),
                    projectFilters = parseResult.GetValue(projectOption) ?? [],
                    routeLimit,
                    evidenceLimit,
                    excludeGenerated = parseResult.GetValue(excludeGeneratedOption),
                    includeSnippets = parseResult.GetValue(includeSnippetsOption),
                    snippetLines
                }));
                return ExitCodes.Success;
            });
    }
}

internal static class OptionsGraphCommand
{
    public static Command Create()
    {
        Option<string?> queryOption = new("--query") { Description = "Optional option type or configuration key fragment." };
        Option<string[]> projectOption = SharedOptions.CreateProjectFiltersOption();
        Option<int?> optionLimitOption = new("--option-limit") { Description = $"Maximum option type facts. Defaults to {ApplicationDomainCommandSupport.DefaultOptionLimit}." };
        Option<int?> consumerLimitOption = new("--consumer-limit") { Description = $"Maximum option consumer facts. Defaults to {ApplicationDomainCommandSupport.DefaultConsumerLimit}." };
        Option<int?> bindingLimitOption = new("--binding-limit") { Description = $"Maximum binding or validation facts. Defaults to {ApplicationDomainCommandSupport.DefaultBindingLimit}." };
        Option<int?> evidenceLimitOption = new("--evidence-limit") { Description = $"Maximum evidence items per fact. Defaults to {ApplicationDomainCommandSupport.DefaultEvidenceLimit}." };
        Option<bool> excludeGeneratedOption = SharedOptions.CreateExcludeGeneratedOption();
        Option<bool> includeSnippetsOption = FuzzyCommandSupport.CreateIncludeSnippetsOption();
        Option<int?> snippetLinesOption = FuzzyCommandSupport.CreateSnippetLinesOption();
        Option<string> profileOption = OutputProfile.CreateOption();

        return WorkspaceCommand.Create(
            "options-graph",
            "Report source-level options and configuration binding facts.",
            [queryOption, projectOption, optionLimitOption, consumerLimitOption, bindingLimitOption, evidenceLimitOption, excludeGeneratedOption, includeSnippetsOption, snippetLinesOption, profileOption],
            (workspace, parseResult, cancellationToken) => ExecuteAsync(
                "options-graph",
                parseResult.GetValue(queryOption),
                requireQuery: false,
                workspace,
                parseResult.GetValue(projectOption) ?? [],
                parseResult.GetValue(optionLimitOption),
                parseResult.GetValue(consumerLimitOption),
                parseResult.GetValue(bindingLimitOption),
                parseResult.GetValue(evidenceLimitOption),
                parseResult.GetValue(excludeGeneratedOption),
                parseResult.GetValue(includeSnippetsOption),
                parseResult.GetValue(snippetLinesOption),
                parseResult.GetValue(profileOption)!,
                cancellationToken));
    }

    internal static async Task<int> ExecuteAsync(
        string commandName,
        string? query,
        bool requireQuery,
        LoadedWorkspace workspace,
        IReadOnlyList<string> projectFilters,
        int? optionLimit,
        int? consumerLimit,
        int? bindingLimit,
        int? evidenceLimit,
        bool excludeGenerated,
        bool includeSnippets,
        int? snippetLines,
        string profile,
        CancellationToken cancellationToken)
    {
        if (requireQuery && string.IsNullOrWhiteSpace(query))
        {
            DiagnosticReporter.WriteError(DiagnosticIds.ParseError, "--query is required.");
            return ExitCodes.UsageError;
        }

        int effectiveOptionLimit = optionLimit ?? ApplicationDomainCommandSupport.DefaultOptionLimit;
        int effectiveConsumerLimit = consumerLimit ?? ApplicationDomainCommandSupport.DefaultConsumerLimit;
        int effectiveBindingLimit = bindingLimit ?? ApplicationDomainCommandSupport.DefaultBindingLimit;
        int effectiveEvidenceLimit = evidenceLimit ?? ApplicationDomainCommandSupport.DefaultEvidenceLimit;
        int effectiveSnippetLines = snippetLines ?? FuzzyDiscoveryResolver.DefaultSnippetLines;
        if (!ApplicationDomainCommandSupport.ValidatePositiveLimits(out int exitCode, ("--option-limit", effectiveOptionLimit), ("--consumer-limit", effectiveConsumerLimit), ("--binding-limit", effectiveBindingLimit), ("--evidence-limit", effectiveEvidenceLimit)) ||
            !ApplicationDomainCommandSupport.ValidateNonNegativeLimits(out exitCode, ("--snippet-lines", effectiveSnippetLines)))
        {
            return exitCode;
        }

        if (!ApplicationDomainCommandSupport.ResolveProjects(workspace, projectFilters, out var projects, out var projectOutputs, out exitCode))
        {
            return exitCode;
        }

        OptionsGraphOptions options = new(query, effectiveOptionLimit, effectiveConsumerLimit, effectiveBindingLimit, effectiveEvidenceLimit, includeSnippets, effectiveSnippetLines, excludeGenerated);
        object result = commandName == "config-impact"
            ? await new ApplicationDomainResolver().ResolveConfigImpactAsync(workspace, projects, projectOutputs, query!.Trim(), options, cancellationToken)
            : await new ApplicationDomainResolver().ResolveOptionsGraphAsync(workspace, projects, projectOutputs, options, cancellationToken);

        ConsoleJsonWriter.Write(OutputProfile.Format(workspace, commandName, profile, result, new
        {
            query,
            projectFilters,
            optionLimit = effectiveOptionLimit,
            consumerLimit = effectiveConsumerLimit,
            bindingLimit = effectiveBindingLimit,
            evidenceLimit = effectiveEvidenceLimit,
            excludeGenerated,
            includeSnippets,
            snippetLines = effectiveSnippetLines
        }));
        return ExitCodes.Success;
    }
}

internal static class ConfigImpactCommand
{
    public static Command Create()
    {
        Option<string?> queryOption = new("--query") { Description = "Option type or configuration key fragment." };
        Option<string[]> projectOption = SharedOptions.CreateProjectFiltersOption();
        Option<int?> optionLimitOption = new("--option-limit") { Description = $"Maximum option type facts. Defaults to {ApplicationDomainCommandSupport.DefaultOptionLimit}." };
        Option<int?> consumerLimitOption = new("--consumer-limit") { Description = $"Maximum option consumer facts. Defaults to {ApplicationDomainCommandSupport.DefaultConsumerLimit}." };
        Option<int?> bindingLimitOption = new("--binding-limit") { Description = $"Maximum binding or validation facts. Defaults to {ApplicationDomainCommandSupport.DefaultBindingLimit}." };
        Option<int?> evidenceLimitOption = new("--evidence-limit") { Description = $"Maximum evidence items per fact. Defaults to {ApplicationDomainCommandSupport.DefaultEvidenceLimit}." };
        Option<bool> excludeGeneratedOption = SharedOptions.CreateExcludeGeneratedOption();
        Option<bool> includeSnippetsOption = FuzzyCommandSupport.CreateIncludeSnippetsOption();
        Option<int?> snippetLinesOption = FuzzyCommandSupport.CreateSnippetLinesOption();
        Option<string> profileOption = OutputProfile.CreateOption();

        return WorkspaceCommand.Create(
            "config-impact",
            "Report source-level impact facts for an option type or configuration key.",
            [queryOption, projectOption, optionLimitOption, consumerLimitOption, bindingLimitOption, evidenceLimitOption, excludeGeneratedOption, includeSnippetsOption, snippetLinesOption, profileOption],
            (workspace, parseResult, cancellationToken) => OptionsGraphCommand.ExecuteAsync(
                "config-impact",
                parseResult.GetValue(queryOption),
                requireQuery: true,
                workspace,
                parseResult.GetValue(projectOption) ?? [],
                parseResult.GetValue(optionLimitOption),
                parseResult.GetValue(consumerLimitOption),
                parseResult.GetValue(bindingLimitOption),
                parseResult.GetValue(evidenceLimitOption),
                parseResult.GetValue(excludeGeneratedOption),
                parseResult.GetValue(includeSnippetsOption),
                parseResult.GetValue(snippetLinesOption),
                parseResult.GetValue(profileOption)!,
                cancellationToken));
    }
}

internal static class WhereHandledCommand
{
    public static Command Create()
    {
        return MessageCommandSupport.Create("where-handled", "Find source-level MediatR handlers for a request or notification.", includeCallSites: false);
    }
}

internal static class MessageFlowCommand
{
    public static Command Create()
    {
        return MessageCommandSupport.Create("message-flow", "Report bounded source-level MediatR message handlers and send/publish call sites.", includeCallSites: true);
    }
}

internal static class MessageCommandSupport
{
    public static Command Create(string commandName, string description, bool includeCallSites)
    {
        Option<string?> queryOption = new("--query") { Description = "Message type query." };
        Option<string?> candidateIdOption = FuzzyCommandSupport.CreateCandidateIdOption();
        Option<FileInfo?> fileOption = new("--file") { Description = "Path to a C# or Visual Basic source file in the workspace." };
        Option<int?> lineOption = new("--line") { Description = "1-based source line." };
        Option<int?> columnOption = new("--column") { Description = "1-based source column." };
        Option<string[]> assumeKindOption = FuzzyCommandSupport.CreateAssumeKindOption();
        Option<string> matchOption = FuzzyCommandSupport.CreateMatchOption();
        Option<bool> caseSensitiveOption = new("--case-sensitive") { Description = "Use case-sensitive fuzzy matching." };
        Option<string[]> projectOption = SharedOptions.CreateProjectFiltersOption();
        Option<int?> candidateLimitOption = new("--candidate-limit") { Description = $"Maximum fuzzy candidates. Defaults to {ApplicationDomainCommandSupport.DefaultCandidateLimit}." };
        Option<int?> handlerLimitOption = new("--handler-limit") { Description = $"Maximum handler facts. Defaults to {ApplicationDomainCommandSupport.DefaultHandlerLimit}." };
        Option<int?> callSiteLimitOption = new("--call-site-limit") { Description = $"Maximum send/publish call sites. Defaults to {ApplicationDomainCommandSupport.DefaultCallSiteLimit}." };
        Option<int?> evidenceLimitOption = new("--evidence-limit") { Description = $"Maximum evidence items per fact. Defaults to {ApplicationDomainCommandSupport.DefaultEvidenceLimit}." };
        Option<string> candidatePolicyOption = FuzzyCommandSupport.CreateCandidatePolicyOption("fail");
        Option<string> minConfidenceOption = FuzzyCommandSupport.CreateMinConfidenceOption("medium");
        Option<bool> explainSelectionOption = FuzzyCommandSupport.CreateExplainSelectionOption();
        Option<bool> excludeGeneratedOption = SharedOptions.CreateExcludeGeneratedOption();
        Option<bool> includeSnippetsOption = FuzzyCommandSupport.CreateIncludeSnippetsOption();
        Option<int?> snippetLinesOption = FuzzyCommandSupport.CreateSnippetLinesOption();
        Option<string> profileOption = OutputProfile.CreateOption();

        return WorkspaceCommand.Create(
            commandName,
            description,
            [queryOption, candidateIdOption, fileOption, lineOption, columnOption, assumeKindOption, matchOption, caseSensitiveOption, projectOption, candidateLimitOption, handlerLimitOption, callSiteLimitOption, evidenceLimitOption, candidatePolicyOption, minConfidenceOption, explainSelectionOption, excludeGeneratedOption, includeSnippetsOption, snippetLinesOption, profileOption],
            async (workspace, parseResult, cancellationToken) =>
            {
                int candidateLimit = parseResult.GetValue(candidateLimitOption) ?? ApplicationDomainCommandSupport.DefaultCandidateLimit;
                int handlerLimit = parseResult.GetValue(handlerLimitOption) ?? ApplicationDomainCommandSupport.DefaultHandlerLimit;
                int callSiteLimit = includeCallSites ? parseResult.GetValue(callSiteLimitOption) ?? ApplicationDomainCommandSupport.DefaultCallSiteLimit : 0;
                int evidenceLimit = parseResult.GetValue(evidenceLimitOption) ?? ApplicationDomainCommandSupport.DefaultEvidenceLimit;
                int snippetLines = parseResult.GetValue(snippetLinesOption) ?? FuzzyDiscoveryResolver.DefaultSnippetLines;
                if (!ApplicationDomainCommandSupport.ValidatePositiveLimits(out int exitCode, ("--candidate-limit", candidateLimit), ("--handler-limit", handlerLimit), ("--evidence-limit", evidenceLimit)) ||
                    includeCallSites && !ApplicationDomainCommandSupport.ValidatePositiveLimits(out exitCode, ("--call-site-limit", callSiteLimit)) ||
                    !ApplicationDomainCommandSupport.ValidateNonNegativeLimits(out exitCode, ("--snippet-lines", snippetLines)))
                {
                    return exitCode;
                }

                ApplicationDomainSubjectResolution subject = await ApplicationDomainCommandSupport.ResolveSubjectAsync(
                    workspace,
                    commandName,
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
                    candidateLimit,
                    parseResult.GetValue(candidatePolicyOption)!,
                    parseResult.GetValue(minConfidenceOption)!,
                    parseResult.GetValue(explainSelectionOption),
                    cancellationToken);
                if (!subject.Success)
                {
                    if (subject.DiagnosticId is not null && subject.DiagnosticMessage is not null)
                    {
                        DiagnosticReporter.WriteError(subject.DiagnosticId.Value, subject.DiagnosticMessage);
                    }

                    return subject.ExitCode;
                }

                if (!ApplicationDomainCommandSupport.ResolveProjects(workspace, parseResult.GetValue(projectOption) ?? [], out var projects, out _, out exitCode))
                {
                    return exitCode;
                }

                MessageFlowResult result = await new ApplicationDomainResolver().ResolveMessageFlowAsync(
                    workspace,
                    projects,
                    subject.SelectionInput!,
                    subject.Selection,
                    subject.Subject,
                    new MessageFlowOptions(handlerLimit, callSiteLimit, evidenceLimit, parseResult.GetValue(includeSnippetsOption), snippetLines, parseResult.GetValue(excludeGeneratedOption)),
                    candidateLimit,
                    commandName,
                    cancellationToken);

                ConsoleJsonWriter.Write(OutputProfile.Format(workspace, commandName, parseResult.GetValue(profileOption)!, result, new
                {
                    projectFilters = parseResult.GetValue(projectOption) ?? [],
                    candidateLimit,
                    handlerLimit,
                    callSiteLimit,
                    evidenceLimit,
                    excludeGenerated = parseResult.GetValue(excludeGeneratedOption),
                    includeSnippets = parseResult.GetValue(includeSnippetsOption),
                    snippetLines
                }));
                return ExitCodes.Success;
            });
    }
}

internal static class EfModelCommand
{
    public static Command Create()
    {
        Option<string?> entityOption = new("--entity") { Description = "Entity type fragment filter." };
        Option<string?> dbContextOption = new("--dbcontext") { Description = "DbContext type fragment filter." };
        Option<string[]> projectOption = SharedOptions.CreateProjectFiltersOption();
        Option<int?> entityLimitOption = new("--entity-limit") { Description = $"Maximum entity/model facts. Defaults to {ApplicationDomainCommandSupport.DefaultEntityLimit}." };
        Option<int?> querySiteLimitOption = new("--query-site-limit") { Description = $"Maximum EF query site facts. Defaults to {ApplicationDomainCommandSupport.DefaultQuerySiteLimit}." };
        Option<int?> evidenceLimitOption = new("--evidence-limit") { Description = $"Maximum evidence items per fact. Defaults to {ApplicationDomainCommandSupport.DefaultEvidenceLimit}." };
        Option<bool> excludeGeneratedOption = SharedOptions.CreateExcludeGeneratedOption();
        Option<bool> includeSnippetsOption = FuzzyCommandSupport.CreateIncludeSnippetsOption();
        Option<int?> snippetLinesOption = FuzzyCommandSupport.CreateSnippetLinesOption();
        Option<string> profileOption = OutputProfile.CreateOption();

        return WorkspaceCommand.Create(
            "ef-model",
            "Report source-level EF Core model facts.",
            [entityOption, dbContextOption, projectOption, entityLimitOption, querySiteLimitOption, evidenceLimitOption, excludeGeneratedOption, includeSnippetsOption, snippetLinesOption, profileOption],
            async (workspace, parseResult, cancellationToken) =>
            {
                int entityLimit = parseResult.GetValue(entityLimitOption) ?? ApplicationDomainCommandSupport.DefaultEntityLimit;
                int querySiteLimit = parseResult.GetValue(querySiteLimitOption) ?? ApplicationDomainCommandSupport.DefaultQuerySiteLimit;
                int evidenceLimit = parseResult.GetValue(evidenceLimitOption) ?? ApplicationDomainCommandSupport.DefaultEvidenceLimit;
                int snippetLines = parseResult.GetValue(snippetLinesOption) ?? FuzzyDiscoveryResolver.DefaultSnippetLines;
                if (!ApplicationDomainCommandSupport.ValidatePositiveLimits(out int exitCode, ("--entity-limit", entityLimit), ("--query-site-limit", querySiteLimit), ("--evidence-limit", evidenceLimit)) ||
                    !ApplicationDomainCommandSupport.ValidateNonNegativeLimits(out exitCode, ("--snippet-lines", snippetLines)))
                {
                    return exitCode;
                }

                if (!ApplicationDomainCommandSupport.ResolveProjects(workspace, parseResult.GetValue(projectOption) ?? [], out var projects, out var projectOutputs, out exitCode))
                {
                    return exitCode;
                }

                EfModelResult result = await new ApplicationDomainResolver().ResolveEfModelAsync(
                    workspace,
                    projects,
                    projectOutputs,
                    new EfModelOptions(parseResult.GetValue(entityOption), parseResult.GetValue(dbContextOption), entityLimit, querySiteLimit, evidenceLimit, parseResult.GetValue(includeSnippetsOption), snippetLines, parseResult.GetValue(excludeGeneratedOption)),
                    cancellationToken);

                ConsoleJsonWriter.Write(OutputProfile.Format(workspace, "ef-model", parseResult.GetValue(profileOption)!, result, new
                {
                    entity = parseResult.GetValue(entityOption),
                    dbcontext = parseResult.GetValue(dbContextOption),
                    projectFilters = parseResult.GetValue(projectOption) ?? [],
                    entityLimit,
                    querySiteLimit,
                    evidenceLimit,
                    excludeGenerated = parseResult.GetValue(excludeGeneratedOption),
                    includeSnippets = parseResult.GetValue(includeSnippetsOption),
                    snippetLines
                }));
                return ExitCodes.Success;
            });
    }
}

internal static class EntityImpactCommand
{
    public static Command Create()
    {
        return EntityImpactCommandSupport.Create();
    }
}

internal static class EntityImpactCommandSupport
{
    public static Command Create()
    {
        Option<string?> queryOption = new("--query") { Description = "Entity type query." };
        Option<string?> candidateIdOption = FuzzyCommandSupport.CreateCandidateIdOption();
        Option<FileInfo?> fileOption = new("--file") { Description = "Path to a C# or Visual Basic source file in the workspace." };
        Option<int?> lineOption = new("--line") { Description = "1-based source line." };
        Option<int?> columnOption = new("--column") { Description = "1-based source column." };
        Option<string[]> assumeKindOption = FuzzyCommandSupport.CreateAssumeKindOption();
        Option<string> matchOption = FuzzyCommandSupport.CreateMatchOption();
        Option<bool> caseSensitiveOption = new("--case-sensitive") { Description = "Use case-sensitive fuzzy matching." };
        Option<string[]> projectOption = SharedOptions.CreateProjectFiltersOption();
        Option<int?> candidateLimitOption = new("--candidate-limit") { Description = $"Maximum fuzzy candidates. Defaults to {ApplicationDomainCommandSupport.DefaultCandidateLimit}." };
        Option<int?> entityLimitOption = new("--entity-limit") { Description = $"Maximum entity/model facts. Defaults to {ApplicationDomainCommandSupport.DefaultEntityLimit}." };
        Option<int?> querySiteLimitOption = new("--query-site-limit") { Description = $"Maximum EF query site facts. Defaults to {ApplicationDomainCommandSupport.DefaultQuerySiteLimit}." };
        Option<int?> evidenceLimitOption = new("--evidence-limit") { Description = $"Maximum evidence items per fact. Defaults to {ApplicationDomainCommandSupport.DefaultEvidenceLimit}." };
        Option<string> candidatePolicyOption = FuzzyCommandSupport.CreateCandidatePolicyOption("fail");
        Option<string> minConfidenceOption = FuzzyCommandSupport.CreateMinConfidenceOption("medium");
        Option<bool> explainSelectionOption = FuzzyCommandSupport.CreateExplainSelectionOption();
        Option<bool> excludeGeneratedOption = SharedOptions.CreateExcludeGeneratedOption();
        Option<bool> includeSnippetsOption = FuzzyCommandSupport.CreateIncludeSnippetsOption();
        Option<int?> snippetLinesOption = FuzzyCommandSupport.CreateSnippetLinesOption();
        Option<string> profileOption = OutputProfile.CreateOption();

        return WorkspaceCommand.Create(
            "entity-impact",
            "Report source-level EF impact facts for an entity type.",
            [queryOption, candidateIdOption, fileOption, lineOption, columnOption, assumeKindOption, matchOption, caseSensitiveOption, projectOption, candidateLimitOption, entityLimitOption, querySiteLimitOption, evidenceLimitOption, candidatePolicyOption, minConfidenceOption, explainSelectionOption, excludeGeneratedOption, includeSnippetsOption, snippetLinesOption, profileOption],
            async (workspace, parseResult, cancellationToken) =>
            {
                int candidateLimit = parseResult.GetValue(candidateLimitOption) ?? ApplicationDomainCommandSupport.DefaultCandidateLimit;
                int entityLimit = parseResult.GetValue(entityLimitOption) ?? ApplicationDomainCommandSupport.DefaultEntityLimit;
                int querySiteLimit = parseResult.GetValue(querySiteLimitOption) ?? ApplicationDomainCommandSupport.DefaultQuerySiteLimit;
                int evidenceLimit = parseResult.GetValue(evidenceLimitOption) ?? ApplicationDomainCommandSupport.DefaultEvidenceLimit;
                int snippetLines = parseResult.GetValue(snippetLinesOption) ?? FuzzyDiscoveryResolver.DefaultSnippetLines;
                if (!ApplicationDomainCommandSupport.ValidatePositiveLimits(out int exitCode, ("--candidate-limit", candidateLimit), ("--entity-limit", entityLimit), ("--query-site-limit", querySiteLimit), ("--evidence-limit", evidenceLimit)) ||
                    !ApplicationDomainCommandSupport.ValidateNonNegativeLimits(out exitCode, ("--snippet-lines", snippetLines)))
                {
                    return exitCode;
                }

                ApplicationDomainSubjectResolution subject = await ApplicationDomainCommandSupport.ResolveSubjectAsync(
                    workspace,
                    "entity-impact",
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
                    candidateLimit,
                    parseResult.GetValue(candidatePolicyOption)!,
                    parseResult.GetValue(minConfidenceOption)!,
                    parseResult.GetValue(explainSelectionOption),
                    cancellationToken);
                if (!subject.Success)
                {
                    if (subject.DiagnosticId is not null && subject.DiagnosticMessage is not null)
                    {
                        DiagnosticReporter.WriteError(subject.DiagnosticId.Value, subject.DiagnosticMessage);
                    }

                    return subject.ExitCode;
                }

                if (!ApplicationDomainCommandSupport.ResolveProjects(workspace, parseResult.GetValue(projectOption) ?? [], out var projects, out _, out exitCode))
                {
                    return exitCode;
                }

                EntityImpactResult result = await new ApplicationDomainResolver().ResolveEntityImpactAsync(
                    workspace,
                    projects,
                    subject.SelectionInput!,
                    subject.Selection,
                    subject.Subject,
                    new EfModelOptions(null, null, entityLimit, querySiteLimit, evidenceLimit, parseResult.GetValue(includeSnippetsOption), snippetLines, parseResult.GetValue(excludeGeneratedOption)),
                    candidateLimit,
                    cancellationToken);

                ConsoleJsonWriter.Write(OutputProfile.Format(workspace, "entity-impact", parseResult.GetValue(profileOption)!, result, new
                {
                    projectFilters = parseResult.GetValue(projectOption) ?? [],
                    candidateLimit,
                    entityLimit,
                    querySiteLimit,
                    evidenceLimit,
                    excludeGenerated = parseResult.GetValue(excludeGeneratedOption),
                    includeSnippets = parseResult.GetValue(includeSnippetsOption),
                    snippetLines
                }));
                return ExitCodes.Success;
            });
    }
}

internal static class PackageUsageCommand
{
    public static Command Create()
    {
        return PackageCommandSupport.Create("package-usage", "Report source-level package reference and usage facts.", impact: false);
    }
}

internal static class PackageImpactCommand
{
    public static Command Create()
    {
        return PackageCommandSupport.Create("package-impact", "Report source-level package impact facts.", impact: true);
    }
}

internal static class PackageCommandSupport
{
    public static Command Create(string commandName, string description, bool impact)
    {
        Option<string?> packageOption = new("--package") { Description = "Package id to inspect." };
        Option<string[]> namespaceOption = new("--namespace") { Description = "Namespace hint for source usage attribution. Can be specified more than once." };
        namespaceOption.AllowMultipleArgumentsPerToken = true;
        Option<string[]> projectOption = SharedOptions.CreateProjectFiltersOption();
        Option<int?> usageLimitOption = new("--usage-limit") { Description = $"Maximum source usage facts. Defaults to {ApplicationDomainCommandSupport.DefaultUsageLimit}." };
        Option<int?> referenceLimitOption = new("--reference-limit") { Description = $"Maximum package references. Defaults to {ApplicationDomainCommandSupport.DefaultReferenceLimit}." };
        Option<bool> includeTestsOption = new("--include-tests") { Description = "Include test project usage facts.", DefaultValueFactory = _ => true };
        Option<bool> excludeGeneratedOption = SharedOptions.CreateExcludeGeneratedOption();
        Option<string> profileOption = OutputProfile.CreateOption();

        return WorkspaceCommand.Create(
            commandName,
            description,
            [packageOption, namespaceOption, projectOption, usageLimitOption, referenceLimitOption, includeTestsOption, excludeGeneratedOption, profileOption],
            async (workspace, parseResult, cancellationToken) =>
            {
                string? package = parseResult.GetValue(packageOption);
                if (string.IsNullOrWhiteSpace(package))
                {
                    DiagnosticReporter.WriteError(DiagnosticIds.ParseError, "--package is required.");
                    return ExitCodes.UsageError;
                }

                int usageLimit = parseResult.GetValue(usageLimitOption) ?? ApplicationDomainCommandSupport.DefaultUsageLimit;
                int referenceLimit = parseResult.GetValue(referenceLimitOption) ?? ApplicationDomainCommandSupport.DefaultReferenceLimit;
                if (!ApplicationDomainCommandSupport.ValidatePositiveLimits(out int exitCode, ("--usage-limit", usageLimit), ("--reference-limit", referenceLimit)))
                {
                    return exitCode;
                }

                if (!ApplicationDomainCommandSupport.ResolveProjects(workspace, parseResult.GetValue(projectOption) ?? [], out var projects, out var projectOutputs, out exitCode))
                {
                    return exitCode;
                }

                PackageUsageOptions options = new(
                    package.Trim(),
                    ApplicationDomainCommandSupport.SplitValues(parseResult.GetValue(namespaceOption) ?? []),
                    usageLimit,
                    referenceLimit,
                    parseResult.GetValue(includeTestsOption),
                    parseResult.GetValue(excludeGeneratedOption));

                object result = impact
                    ? await new ApplicationDomainResolver().ResolvePackageImpactAsync(workspace, projects, projectOutputs, options, cancellationToken)
                    : await new ApplicationDomainResolver().ResolvePackageUsageAsync(workspace, projects, projectOutputs, options, cancellationToken);

                ConsoleJsonWriter.Write(OutputProfile.Format(workspace, commandName, parseResult.GetValue(profileOption)!, result, new
                {
                    package = package.Trim(),
                    namespaces = options.NamespaceHints,
                    projectFilters = parseResult.GetValue(projectOption) ?? [],
                    usageLimit,
                    referenceLimit,
                    includeTests = parseResult.GetValue(includeTestsOption),
                    excludeGenerated = parseResult.GetValue(excludeGeneratedOption)
                }));
                return ExitCodes.Success;
            });
    }
}

using Microsoft.CodeAnalysis;
using Navlyn.ApplicationDomains;
using Navlyn.Cli.OutputProfiles;
using Navlyn.Diagnostics;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static partial class BatchCommand
{
    private static async Task<BatchRequestResult> ExecuteRouteMapAsync(
        LoadedWorkspace loadedWorkspace,
        BatchDefaults defaults,
        BatchRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetProjectFilters(request.Payload, defaults, out IReadOnlyList<string> projectFilters, out BatchError? error) ||
            !TryGetStringArray(request.Payload, "routes", out IReadOnlyList<string> routes, out error, allowStringValue: true) ||
            !TryGetStringArray(request.Payload, "endpointKinds", out IReadOnlyList<string> endpointKinds, out error, allowStringValue: true) ||
            !TryGetOptionalString(request.Payload, "auth", out string? auth, out error) ||
            !TryGetEffectiveExcludeGenerated(request.Payload, defaults, out bool excludeGenerated, out error) ||
            !TryGetOptionalInt(request.Payload, "routeLimit", out int? routeLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "evidenceLimit", out int? evidenceLimit, out error) ||
            !TryGetFuzzySnippetOptions(request.Payload, out bool includeSnippets, out int snippetLines, out error) ||
            !TryGetProfile(request.Payload, out string profile, out error))
        {
            return request.Failed(error!);
        }

        int effectiveRouteLimit = routeLimit ?? ApplicationDomainCommandSupport.DefaultRouteLimit;
        int effectiveEvidenceLimit = evidenceLimit ?? ApplicationDomainCommandSupport.DefaultEvidenceLimit;
        BatchError? limitError =
            GetPositiveBatchLimitError("routeLimit", effectiveRouteLimit) ??
            GetPositiveBatchLimitError("evidenceLimit", effectiveEvidenceLimit);
        if (limitError is not null)
        {
            return request.Failed(limitError);
        }

        IReadOnlyList<string> normalizedEndpointKinds = ApplicationDomainCommandSupport.SplitValues(endpointKinds);
        if (!RouteMapCommand.ValidateEndpointKinds(normalizedEndpointKinds))
        {
            return request.Failed(DiagnosticIds.ParseError, "Invalid endpointKinds value.");
        }

        string authFilter = string.IsNullOrWhiteSpace(auth) ? "any" : auth.Trim();
        if (authFilter is not ("any" or "required" or "anonymous" or "unknown"))
        {
            return request.Failed(DiagnosticIds.ParseError, "auth must be any, required, anonymous, or unknown.");
        }

        if (!TryResolveApplicationDomainProjects(loadedWorkspace, projectFilters, out IReadOnlyList<Project> projects, out IReadOnlyList<ApplicationDomainProjectFilter>? projectOutputs, out error))
        {
            return request.Failed(error!);
        }

        RouteMapResult result = await new ApplicationDomainResolver().ResolveRouteMapAsync(
            loadedWorkspace,
            projects,
            projectOutputs,
            new RouteMapOptions(effectiveRouteLimit, effectiveEvidenceLimit, ApplicationDomainCommandSupport.SplitValues(routes), normalizedEndpointKinds, authFilter, includeSnippets, snippetLines, excludeGenerated),
            cancellationToken);

        return request.Success(OutputProfile.Format(loadedWorkspace, "route-map", profile, result, new
        {
            projectFilters,
            routes,
            endpointKinds = normalizedEndpointKinds,
            auth = authFilter,
            routeLimit = effectiveRouteLimit,
            evidenceLimit = effectiveEvidenceLimit,
            excludeGenerated,
            includeSnippets,
            snippetLines
        }));
    }

    private static async Task<BatchRequestResult> ExecuteRouteImpactAsync(
        LoadedWorkspace loadedWorkspace,
        BatchDefaults defaults,
        BatchRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetOptionalString(request.Payload, "route", out string? route, out BatchError? error) ||
            !TryGetProjectFilters(request.Payload, defaults, out IReadOnlyList<string> projectFilters, out error) ||
            !TryGetEffectiveExcludeGenerated(request.Payload, defaults, out bool excludeGenerated, out error) ||
            !TryGetOptionalInt(request.Payload, "routeLimit", out int? routeLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "evidenceLimit", out int? evidenceLimit, out error) ||
            !TryGetFuzzySnippetOptions(request.Payload, out bool includeSnippets, out int snippetLines, out error) ||
            !TryGetProfile(request.Payload, out string profile, out error))
        {
            return request.Failed(error!);
        }

        if (string.IsNullOrWhiteSpace(route))
        {
            return request.Failed(DiagnosticIds.ParseError, "route is required.");
        }

        int effectiveRouteLimit = routeLimit ?? ApplicationDomainCommandSupport.DefaultRouteLimit;
        int effectiveEvidenceLimit = evidenceLimit ?? ApplicationDomainCommandSupport.DefaultEvidenceLimit;
        BatchError? limitError =
            GetPositiveBatchLimitError("routeLimit", effectiveRouteLimit) ??
            GetPositiveBatchLimitError("evidenceLimit", effectiveEvidenceLimit);
        if (limitError is not null)
        {
            return request.Failed(limitError);
        }

        if (!TryResolveApplicationDomainProjects(loadedWorkspace, projectFilters, out IReadOnlyList<Project> projects, out IReadOnlyList<ApplicationDomainProjectFilter>? projectOutputs, out error))
        {
            return request.Failed(error!);
        }

        RouteImpactResult result = await new ApplicationDomainResolver().ResolveRouteImpactAsync(
            loadedWorkspace,
            projects,
            projectOutputs,
            route.Trim(),
            new RouteMapOptions(effectiveRouteLimit, effectiveEvidenceLimit, [], [], "any", includeSnippets, snippetLines, excludeGenerated),
            cancellationToken);

        return request.Success(OutputProfile.Format(loadedWorkspace, "route-impact", profile, result, new
        {
            route = route.Trim(),
            projectFilters,
            routeLimit = effectiveRouteLimit,
            evidenceLimit = effectiveEvidenceLimit,
            excludeGenerated,
            includeSnippets,
            snippetLines
        }));
    }

    private static async Task<BatchRequestResult> ExecuteOptionsGraphAsync(
        LoadedWorkspace loadedWorkspace,
        BatchDefaults defaults,
        BatchRequest request,
        CancellationToken cancellationToken)
    {
        return await ExecuteOptionsDomainAsync(loadedWorkspace, defaults, request, requireQuery: false, cancellationToken);
    }

    private static async Task<BatchRequestResult> ExecuteConfigImpactAsync(
        LoadedWorkspace loadedWorkspace,
        BatchDefaults defaults,
        BatchRequest request,
        CancellationToken cancellationToken)
    {
        return await ExecuteOptionsDomainAsync(loadedWorkspace, defaults, request, requireQuery: true, cancellationToken);
    }

    private static async Task<BatchRequestResult> ExecuteOptionsDomainAsync(
        LoadedWorkspace loadedWorkspace,
        BatchDefaults defaults,
        BatchRequest request,
        bool requireQuery,
        CancellationToken cancellationToken)
    {
        if (!TryGetOptionalString(request.Payload, "query", out string? query, out BatchError? error) ||
            !TryGetProjectFilters(request.Payload, defaults, out IReadOnlyList<string> projectFilters, out error) ||
            !TryGetEffectiveExcludeGenerated(request.Payload, defaults, out bool excludeGenerated, out error) ||
            !TryGetOptionalInt(request.Payload, "optionLimit", out int? optionLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "consumerLimit", out int? consumerLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "bindingLimit", out int? bindingLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "evidenceLimit", out int? evidenceLimit, out error) ||
            !TryGetFuzzySnippetOptions(request.Payload, out bool includeSnippets, out int snippetLines, out error) ||
            !TryGetProfile(request.Payload, out string profile, out error))
        {
            return request.Failed(error!);
        }

        if (requireQuery && string.IsNullOrWhiteSpace(query))
        {
            return request.Failed(DiagnosticIds.ParseError, "query is required.");
        }

        int effectiveOptionLimit = optionLimit ?? ApplicationDomainCommandSupport.DefaultOptionLimit;
        int effectiveConsumerLimit = consumerLimit ?? ApplicationDomainCommandSupport.DefaultConsumerLimit;
        int effectiveBindingLimit = bindingLimit ?? ApplicationDomainCommandSupport.DefaultBindingLimit;
        int effectiveEvidenceLimit = evidenceLimit ?? ApplicationDomainCommandSupport.DefaultEvidenceLimit;
        BatchError? limitError =
            GetPositiveBatchLimitError("optionLimit", effectiveOptionLimit) ??
            GetPositiveBatchLimitError("consumerLimit", effectiveConsumerLimit) ??
            GetPositiveBatchLimitError("bindingLimit", effectiveBindingLimit) ??
            GetPositiveBatchLimitError("evidenceLimit", effectiveEvidenceLimit);
        if (limitError is not null)
        {
            return request.Failed(limitError);
        }

        if (!TryResolveApplicationDomainProjects(loadedWorkspace, projectFilters, out IReadOnlyList<Project> projects, out IReadOnlyList<ApplicationDomainProjectFilter>? projectOutputs, out error))
        {
            return request.Failed(error!);
        }

        OptionsGraphOptions options = new(query, effectiveOptionLimit, effectiveConsumerLimit, effectiveBindingLimit, effectiveEvidenceLimit, includeSnippets, snippetLines, excludeGenerated);
        object result = request.Command == "config-impact"
            ? await new ApplicationDomainResolver().ResolveConfigImpactAsync(loadedWorkspace, projects, projectOutputs, query!.Trim(), options, cancellationToken)
            : await new ApplicationDomainResolver().ResolveOptionsGraphAsync(loadedWorkspace, projects, projectOutputs, options, cancellationToken);

        return request.Success(OutputProfile.Format(loadedWorkspace, request.Command, profile, result, new
        {
            query,
            projectFilters,
            optionLimit = effectiveOptionLimit,
            consumerLimit = effectiveConsumerLimit,
            bindingLimit = effectiveBindingLimit,
            evidenceLimit = effectiveEvidenceLimit,
            excludeGenerated,
            includeSnippets,
            snippetLines
        }));
    }

    private static async Task<BatchRequestResult> ExecuteMessageDomainAsync(
        LoadedWorkspace loadedWorkspace,
        BatchDefaults defaults,
        BatchRequest request,
        bool includeCallSites,
        CancellationToken cancellationToken)
    {
        if (!TryGetSymbolSelectionInput(request.Payload, request.Command, out _, out BatchError? error) ||
            !TryGetProjectFilters(request.Payload, defaults, out IReadOnlyList<string> projectFilters, out error) ||
            !TryGetEffectiveExcludeGenerated(request.Payload, defaults, out bool excludeGenerated, out error) ||
            !TryGetOptionalInt(request.Payload, "candidateLimit", out int? candidateLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "handlerLimit", out int? handlerLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "callSiteLimit", out int? callSiteLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "evidenceLimit", out int? evidenceLimit, out error) ||
            !TryGetFuzzySnippetOptions(request.Payload, out bool includeSnippets, out int snippetLines, out error) ||
            !TryGetProfile(request.Payload, out string profile, out error))
        {
            return request.Failed(error!);
        }

        int effectiveCandidateLimit = candidateLimit ?? ApplicationDomainCommandSupport.DefaultCandidateLimit;
        int effectiveHandlerLimit = handlerLimit ?? ApplicationDomainCommandSupport.DefaultHandlerLimit;
        int effectiveCallSiteLimit = includeCallSites ? callSiteLimit ?? ApplicationDomainCommandSupport.DefaultCallSiteLimit : 0;
        int effectiveEvidenceLimit = evidenceLimit ?? ApplicationDomainCommandSupport.DefaultEvidenceLimit;
        BatchError? limitError =
            GetPositiveBatchLimitError("candidateLimit", effectiveCandidateLimit) ??
            GetPositiveBatchLimitError("handlerLimit", effectiveHandlerLimit) ??
            GetPositiveBatchLimitError("evidenceLimit", effectiveEvidenceLimit) ??
            (includeCallSites ? GetPositiveBatchLimitError("callSiteLimit", effectiveCallSiteLimit) : null);
        if (limitError is not null)
        {
            return request.Failed(limitError);
        }

        if (!TryResolveApplicationDomainProjects(loadedWorkspace, projectFilters, out IReadOnlyList<Project> projects, out _, out error))
        {
            return request.Failed(error!);
        }

        ApplicationDomainSubjectResolution subject = await ResolveApplicationDomainSubjectForBatchAsync(loadedWorkspace, defaults, request, excludeGenerated, effectiveCandidateLimit, cancellationToken);
        if (!subject.Success)
        {
            return request.Failed(subject.DiagnosticId ?? DiagnosticIds.ParseError, subject.DiagnosticMessage ?? $"Invalid {request.Command} request.");
        }

        MessageFlowResult result = await new ApplicationDomainResolver().ResolveMessageFlowAsync(
            loadedWorkspace,
            projects,
            subject.SelectionInput!,
            subject.Selection,
            subject.Subject,
            new MessageFlowOptions(effectiveHandlerLimit, effectiveCallSiteLimit, effectiveEvidenceLimit, includeSnippets, snippetLines, excludeGenerated),
            effectiveCandidateLimit,
            request.Command,
            cancellationToken);

        return request.Success(OutputProfile.Format(loadedWorkspace, request.Command, profile, result, new
        {
            projectFilters,
            candidateLimit = effectiveCandidateLimit,
            handlerLimit = effectiveHandlerLimit,
            callSiteLimit = effectiveCallSiteLimit,
            evidenceLimit = effectiveEvidenceLimit,
            excludeGenerated,
            includeSnippets,
            snippetLines
        }));
    }

    private static async Task<BatchRequestResult> ExecuteEfModelAsync(
        LoadedWorkspace loadedWorkspace,
        BatchDefaults defaults,
        BatchRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetOptionalString(request.Payload, "entity", out string? entity, out BatchError? error) ||
            !TryGetOptionalString(request.Payload, "dbcontext", out string? dbcontext, out error) ||
            !TryGetProjectFilters(request.Payload, defaults, out IReadOnlyList<string> projectFilters, out error) ||
            !TryGetEffectiveExcludeGenerated(request.Payload, defaults, out bool excludeGenerated, out error) ||
            !TryGetOptionalInt(request.Payload, "entityLimit", out int? entityLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "querySiteLimit", out int? querySiteLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "evidenceLimit", out int? evidenceLimit, out error) ||
            !TryGetFuzzySnippetOptions(request.Payload, out bool includeSnippets, out int snippetLines, out error) ||
            !TryGetProfile(request.Payload, out string profile, out error))
        {
            return request.Failed(error!);
        }

        int effectiveEntityLimit = entityLimit ?? ApplicationDomainCommandSupport.DefaultEntityLimit;
        int effectiveQuerySiteLimit = querySiteLimit ?? ApplicationDomainCommandSupport.DefaultQuerySiteLimit;
        int effectiveEvidenceLimit = evidenceLimit ?? ApplicationDomainCommandSupport.DefaultEvidenceLimit;
        BatchError? limitError =
            GetPositiveBatchLimitError("entityLimit", effectiveEntityLimit) ??
            GetPositiveBatchLimitError("querySiteLimit", effectiveQuerySiteLimit) ??
            GetPositiveBatchLimitError("evidenceLimit", effectiveEvidenceLimit);
        if (limitError is not null)
        {
            return request.Failed(limitError);
        }

        if (!TryResolveApplicationDomainProjects(loadedWorkspace, projectFilters, out IReadOnlyList<Project> projects, out IReadOnlyList<ApplicationDomainProjectFilter>? projectOutputs, out error))
        {
            return request.Failed(error!);
        }

        EfModelResult result = await new ApplicationDomainResolver().ResolveEfModelAsync(
            loadedWorkspace,
            projects,
            projectOutputs,
            new EfModelOptions(entity, dbcontext, effectiveEntityLimit, effectiveQuerySiteLimit, effectiveEvidenceLimit, includeSnippets, snippetLines, excludeGenerated),
            cancellationToken);

        return request.Success(OutputProfile.Format(loadedWorkspace, "ef-model", profile, result, new
        {
            entity,
            dbcontext,
            projectFilters,
            entityLimit = effectiveEntityLimit,
            querySiteLimit = effectiveQuerySiteLimit,
            evidenceLimit = effectiveEvidenceLimit,
            excludeGenerated,
            includeSnippets,
            snippetLines
        }));
    }

    private static async Task<BatchRequestResult> ExecuteEntityImpactAsync(
        LoadedWorkspace loadedWorkspace,
        BatchDefaults defaults,
        BatchRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetSymbolSelectionInput(request.Payload, request.Command, out _, out BatchError? error) ||
            !TryGetProjectFilters(request.Payload, defaults, out IReadOnlyList<string> projectFilters, out error) ||
            !TryGetEffectiveExcludeGenerated(request.Payload, defaults, out bool excludeGenerated, out error) ||
            !TryGetOptionalInt(request.Payload, "candidateLimit", out int? candidateLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "entityLimit", out int? entityLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "querySiteLimit", out int? querySiteLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "evidenceLimit", out int? evidenceLimit, out error) ||
            !TryGetFuzzySnippetOptions(request.Payload, out bool includeSnippets, out int snippetLines, out error) ||
            !TryGetProfile(request.Payload, out string profile, out error))
        {
            return request.Failed(error!);
        }

        int effectiveCandidateLimit = candidateLimit ?? ApplicationDomainCommandSupport.DefaultCandidateLimit;
        int effectiveEntityLimit = entityLimit ?? ApplicationDomainCommandSupport.DefaultEntityLimit;
        int effectiveQuerySiteLimit = querySiteLimit ?? ApplicationDomainCommandSupport.DefaultQuerySiteLimit;
        int effectiveEvidenceLimit = evidenceLimit ?? ApplicationDomainCommandSupport.DefaultEvidenceLimit;
        BatchError? limitError =
            GetPositiveBatchLimitError("candidateLimit", effectiveCandidateLimit) ??
            GetPositiveBatchLimitError("entityLimit", effectiveEntityLimit) ??
            GetPositiveBatchLimitError("querySiteLimit", effectiveQuerySiteLimit) ??
            GetPositiveBatchLimitError("evidenceLimit", effectiveEvidenceLimit);
        if (limitError is not null)
        {
            return request.Failed(limitError);
        }

        if (!TryResolveApplicationDomainProjects(loadedWorkspace, projectFilters, out IReadOnlyList<Project> projects, out _, out error))
        {
            return request.Failed(error!);
        }

        ApplicationDomainSubjectResolution subject = await ResolveApplicationDomainSubjectForBatchAsync(loadedWorkspace, defaults, request, excludeGenerated, effectiveCandidateLimit, cancellationToken);
        if (!subject.Success)
        {
            return request.Failed(subject.DiagnosticId ?? DiagnosticIds.ParseError, subject.DiagnosticMessage ?? "Invalid entity-impact request.");
        }

        EntityImpactResult result = await new ApplicationDomainResolver().ResolveEntityImpactAsync(
            loadedWorkspace,
            projects,
            subject.SelectionInput!,
            subject.Selection,
            subject.Subject,
            new EfModelOptions(null, null, effectiveEntityLimit, effectiveQuerySiteLimit, effectiveEvidenceLimit, includeSnippets, snippetLines, excludeGenerated),
            effectiveCandidateLimit,
            cancellationToken);

        return request.Success(OutputProfile.Format(loadedWorkspace, "entity-impact", profile, result, new
        {
            projectFilters,
            candidateLimit = effectiveCandidateLimit,
            entityLimit = effectiveEntityLimit,
            querySiteLimit = effectiveQuerySiteLimit,
            evidenceLimit = effectiveEvidenceLimit,
            excludeGenerated,
            includeSnippets,
            snippetLines
        }));
    }

    private static async Task<BatchRequestResult> ExecutePackageDomainAsync(
        LoadedWorkspace loadedWorkspace,
        BatchDefaults defaults,
        BatchRequest request,
        bool impact,
        CancellationToken cancellationToken)
    {
        if (!TryGetOptionalString(request.Payload, "package", out string? package, out BatchError? error) ||
            !TryGetStringArray(request.Payload, "namespaces", out IReadOnlyList<string> namespaces, out error, allowStringValue: true) ||
            !TryGetProjectFilters(request.Payload, defaults, out IReadOnlyList<string> projectFilters, out error) ||
            !TryGetEffectiveExcludeGenerated(request.Payload, defaults, out bool excludeGenerated, out error) ||
            !TryGetDefaultTrueBool(request.Payload, "includeTests", out bool includeTests, out error) ||
            !TryGetOptionalInt(request.Payload, "usageLimit", out int? usageLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "referenceLimit", out int? referenceLimit, out error) ||
            !TryGetProfile(request.Payload, out string profile, out error))
        {
            return request.Failed(error!);
        }

        if (string.IsNullOrWhiteSpace(package))
        {
            return request.Failed(DiagnosticIds.ParseError, "package is required.");
        }

        int effectiveUsageLimit = usageLimit ?? ApplicationDomainCommandSupport.DefaultUsageLimit;
        int effectiveReferenceLimit = referenceLimit ?? ApplicationDomainCommandSupport.DefaultReferenceLimit;
        BatchError? limitError =
            GetPositiveBatchLimitError("usageLimit", effectiveUsageLimit) ??
            GetPositiveBatchLimitError("referenceLimit", effectiveReferenceLimit);
        if (limitError is not null)
        {
            return request.Failed(limitError);
        }

        if (!TryResolveApplicationDomainProjects(loadedWorkspace, projectFilters, out IReadOnlyList<Project> projects, out IReadOnlyList<ApplicationDomainProjectFilter>? projectOutputs, out error))
        {
            return request.Failed(error!);
        }

        PackageUsageOptions options = new(package.Trim(), ApplicationDomainCommandSupport.SplitValues(namespaces), effectiveUsageLimit, effectiveReferenceLimit, includeTests, excludeGenerated);
        object result = impact
            ? await new ApplicationDomainResolver().ResolvePackageImpactAsync(loadedWorkspace, projects, projectOutputs, options, cancellationToken)
            : await new ApplicationDomainResolver().ResolvePackageUsageAsync(loadedWorkspace, projects, projectOutputs, options, cancellationToken);

        return request.Success(OutputProfile.Format(loadedWorkspace, request.Command, profile, result, new
        {
            package = package.Trim(),
            namespaces = options.NamespaceHints,
            projectFilters,
            usageLimit = effectiveUsageLimit,
            referenceLimit = effectiveReferenceLimit,
            includeTests,
            excludeGenerated
        }));
    }

    private static async Task<ApplicationDomainSubjectResolution> ResolveApplicationDomainSubjectForBatchAsync(
        LoadedWorkspace loadedWorkspace,
        BatchDefaults defaults,
        BatchRequest request,
        bool excludeGenerated,
        int candidateLimit,
        CancellationToken cancellationToken)
    {
        if (!TryGetSymbolSelectionInput(request.Payload, request.Command, out SymbolSelectionInput input, out BatchError? inputError))
        {
            return ApplicationDomainSubjectResolution.Failed(DiagnosticNumber(inputError), inputError?.Message, ExitCodes.UsageError);
        }

        if (input.IsSourcePosition)
        {
            SourceSymbolResolutionResult sourceResult = await new SourceSymbolResolver().ResolveAsync(
                loadedWorkspace.Solution,
                input.File!,
                input.Line!.Value,
                input.Column!.Value,
                project: null,
                excludeGenerated,
                cancellationToken);
            if (sourceResult.Error is not null)
            {
                return ApplicationDomainSubjectResolution.Failed(sourceResult.Error.DiagnosticId, sourceResult.Error.Message, sourceResult.Error.ExitCode);
            }

            SourceSymbolResolution resolved = sourceResult.Resolution!;
            ISymbol symbol = resolved.Symbol is INamedTypeSymbol ? resolved.Symbol : resolved.Symbol.ContainingType ?? resolved.Symbol;
            if (symbol is not INamedTypeSymbol)
            {
                return ApplicationDomainSubjectResolution.Failed(DiagnosticIds.SymbolNotFoundAtPosition, "Selected symbol is not a type or a member contained by a type.", ExitCodes.UsageError);
            }

            return ApplicationDomainSubjectResolution.Succeeded(
                new ApplicationDomainSelectionInput("sourcePosition", Query: null, CandidateId: null, resolved.File, resolved.Line, resolved.Column),
                selection: null,
                subject: ApplicationDomainResolver.CreateSymbol(symbol, resolved.ProjectName, excludeGenerated));
        }

        if (!TryGetFuzzyQuery(
            loadedWorkspace,
            defaults,
            request,
            readCandidateLimit: false,
            out FuzzyQueryOptions fuzzyOptions,
            out IReadOnlyList<Project> projects,
            out _,
            out BatchError? error))
        {
            return ApplicationDomainSubjectResolution.Failed(DiagnosticNumber(error), error?.Message, ExitCodes.UsageError);
        }

        fuzzyOptions = fuzzyOptions with { Limit = candidateLimit };
        FuzzyCandidateResolution resolution = await new FuzzyDiscoveryResolver().ResolveCandidatesForSelectionAsync(projects, fuzzyOptions, cancellationToken);
        if (resolution.Error is not null)
        {
            return ApplicationDomainSubjectResolution.Failed(resolution.Error.DiagnosticId, resolution.Error.Message, resolution.Error.ExitCode);
        }

        FuzzySymbolCandidate? selected = resolution.SelectedCandidate;
        ApplicationDomainSymbol? subject = selected is null
            ? null
            : new ApplicationDomainSymbol(selected.Name, selected.Kind, selected.Container, selected.Facts, selected.Path, selected.Line, selected.Column, selected.EndLine, selected.EndColumn);

        return ApplicationDomainSubjectResolution.Succeeded(
            new ApplicationDomainSelectionInput(input.CandidateId is null ? "query" : "candidateId", input.Query?.Trim(), input.CandidateId?.Trim(), File: null, Line: null, Column: null),
            new ApplicationDomainSelectionSection(
                resolution.Confidence,
                Math.Min(resolution.Candidates.Count, candidateLimit),
                resolution.TotalCandidates,
                selected,
                [.. resolution.Candidates.Take(candidateLimit)],
                resolution.SelectionExplanation),
            subject);
    }

    private static bool TryResolveApplicationDomainProjects(
        LoadedWorkspace loadedWorkspace,
        IReadOnlyList<string> projectFilters,
        out IReadOnlyList<Project> projects,
        out IReadOnlyList<ApplicationDomainProjectFilter>? projectOutputs,
        out BatchError? error)
    {
        ProjectFilterResolutionResult projectResult = new ProjectFilterResolver().ResolveMany(loadedWorkspace.Solution, projectFilters);
        if (projectResult.Error is not null)
        {
            projects = [];
            projectOutputs = null;
            error = BatchError.FromDiagnostic(projectResult.Error.DiagnosticId, projectResult.Error.Message);
            return false;
        }

        projects = projectResult.Projects;
        projectOutputs = projectResult.AppliedFilters.Count == 0
            ? null
            : projectResult.AppliedFilters.Select(filter => new ApplicationDomainProjectFilter(filter.Filter, filter.Name, filter.Path, filter.TargetFramework)).ToArray();
        error = null;
        return true;
    }
}

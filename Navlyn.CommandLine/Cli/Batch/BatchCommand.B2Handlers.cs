using System.Text.Json;
using Microsoft.CodeAnalysis;
using Navlyn.Cli.OutputProfiles;
using Navlyn.DependencyInjection;
using Navlyn.Diagnostics;
using Navlyn.Diffs;
using Navlyn.Entrypoints;
using Navlyn.Paths;
using Navlyn.PublicApi;
using Navlyn.RepoGraph;
using Navlyn.Symbols;
using Navlyn.Testing;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static partial class BatchCommand
{
    private static readonly string[] DefaultFrameworks = ["aspnetcore", "test", "worker"];
    private static readonly string[] AllowedFrameworks = ["aspnetcore", "test", "worker", "azure-functions", "grpc", "mediatr", "messaging"];

    private static BatchRequestResult ExecuteRepoGraph(
        LoadedWorkspace loadedWorkspace,
        BatchDefaults defaults,
        BatchRequest request)
    {
        if (!TryGetProjectFilters(request.Payload, defaults, out IReadOnlyList<string> projectFilters, out BatchError? error) ||
            !TryGetDefaultTrueBool(request.Payload, "includePackages", out bool includePackages, out error) ||
            !TryGetDefaultTrueBool(request.Payload, "includeMsbuildFiles", out bool includeMsbuildFiles, out error) ||
            !TryGetDefaultTrueBool(request.Payload, "includePreprocessorSymbols", out bool includePreprocessorSymbols, out error) ||
            !TryGetDefaultTrueBool(request.Payload, "classification", out bool classification, out error) ||
            !TryGetOptionalInt(request.Payload, "relationshipLimit", out int? relationshipLimit, out error) ||
            !TryGetProfile(request.Payload, out string profile, out error))
        {
            return request.Failed(error!);
        }

        int effectiveRelationshipLimit = relationshipLimit ?? 200;
        BatchError? limitError = GetPositiveBatchLimitError("relationshipLimit", effectiveRelationshipLimit);
        if (limitError is not null)
        {
            return request.Failed(limitError);
        }

        ProjectFilterResolutionResult projectResult = new ProjectFilterResolver().ResolveMany(loadedWorkspace.Solution, projectFilters);
        if (projectResult.Error is not null)
        {
            return request.Failed(projectResult.Error);
        }

        RepoGraphResult result = new RepoGraphResolver().Resolve(
            loadedWorkspace,
            projectResult.Projects,
            new RepoGraphOptions(
                includePackages,
                includeMsbuildFiles,
                includePreprocessorSymbols,
                classification,
                effectiveRelationshipLimit));

        return request.Success(OutputProfile.Format(loadedWorkspace, "repo-graph", profile, result, new
        {
            projectFilters,
            includePackages,
            includeMsbuildFiles,
            includePreprocessorSymbols,
            classification,
            relationshipLimit = effectiveRelationshipLimit
        }));
    }

    private static async Task<BatchRequestResult> ExecutePublicApiDiffAsync(
        LoadedWorkspace loadedWorkspace,
        BatchDefaults defaults,
        BatchRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetOptionalString(request.Payload, "base", out string? baseRef, out BatchError? error) ||
            !TryGetOptionalString(request.Payload, "head", out string? headRef, out error) ||
            !TryGetProjectFilters(request.Payload, defaults, out IReadOnlyList<string> projectFilters, out error) ||
            !TryGetEffectiveExcludeGenerated(request.Payload, defaults, out bool excludeGenerated, out error) ||
            !TryGetDefaultTrueBool(request.Payload, "includeAdditions", out bool includeAdditions, out error) ||
            !TryGetDefaultTrueBool(request.Payload, "includeAttributes", out bool includeAttributes, out error) ||
            !TryGetOptionalInt(request.Payload, "symbolLimit", out int? symbolLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "changeLimit", out int? changeLimit, out error) ||
            !TryGetProfile(request.Payload, out string profile, out error))
        {
            return request.Failed(error!);
        }

        if (string.IsNullOrWhiteSpace(baseRef))
        {
            return request.Failed(DiagnosticIds.InvalidDiffOptions, "base is required.");
        }

        if (request.Payload.TryGetProperty("staged", out _) ||
            request.Payload.TryGetProperty("includeUnstaged", out _))
        {
            return request.Failed(DiagnosticIds.InvalidDiffOptions, "public-api-diff batch requests support base/head refs only.");
        }

        int effectiveSymbolLimit = symbolLimit ?? 5000;
        int effectiveChangeLimit = changeLimit ?? 200;
        BatchError? limitError =
            GetPositiveBatchLimitError("symbolLimit", effectiveSymbolLimit) ??
            GetPositiveBatchLimitError("changeLimit", effectiveChangeLimit);
        if (limitError is not null)
        {
            return request.Failed(limitError);
        }

        ProjectFilterResolutionResult projectResult = new ProjectFilterResolver().ResolveMany(loadedWorkspace.Solution, projectFilters);
        if (projectResult.Error is not null)
        {
            return request.Failed(projectResult.Error);
        }

        IReadOnlyList<PublicApiProjectFilter>? projectOutputs = projectResult.AppliedFilters.Count == 0
            ? null
            : projectResult.AppliedFilters.Select(filter => new PublicApiProjectFilter(
                Filter: filter.Filter,
                Name: filter.Name,
                Path: filter.Path,
                TargetFramework: filter.TargetFramework)).ToArray();

        PublicApiDiffExecutionResult result = await new PublicApiDiffResolver().ResolveAsync(
            loadedWorkspace,
            projectResult.Projects,
            projectOutputs,
            new PublicApiDiffOptions(
                BaseRef: baseRef.Trim(),
                HeadRef: string.IsNullOrWhiteSpace(headRef) ? null : headRef.Trim(),
                ExcludeGenerated: excludeGenerated,
                IncludeAdditions: includeAdditions,
                IncludeAttributes: includeAttributes,
                SymbolLimit: effectiveSymbolLimit,
                ChangeLimit: effectiveChangeLimit),
            cancellationToken);

        return result.Error is not null
            ? request.Failed(result.Error.DiagnosticId, result.Error.Message)
            : request.Success(OutputProfile.Format(loadedWorkspace, "public-api-diff", profile, result.Result!, new
            {
                baseRef,
                headRef,
                projectFilters,
                excludeGenerated,
                includeAdditions,
                includeAttributes,
                symbolLimit = effectiveSymbolLimit,
                changeLimit = effectiveChangeLimit
            }));
    }

    private static async Task<BatchRequestResult> ExecuteTestsForSymbolAsync(
        LoadedWorkspace loadedWorkspace,
        BatchDefaults defaults,
        BatchRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetSymbolSelectionInput(request.Payload, "tests-for-symbol", out SymbolSelectionInput input, out BatchError? error) ||
            !TryGetProjectFilters(request.Payload, defaults, out IReadOnlyList<string> projectFilters, out error) ||
            !TryGetTestProjectFilters(request.Payload, out IReadOnlyList<string> testProjectFilters, out error) ||
            !TryGetEffectiveExcludeGenerated(request.Payload, defaults, out bool excludeGenerated, out error) ||
            !TryGetOptionalInt(request.Payload, "candidateLimit", out int? candidateLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "testLimit", out int? testLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "referenceLimit", out int? referenceLimit, out error) ||
            !TryGetFuzzySnippetOptions(request.Payload, out bool includeSnippets, out int snippetLines, out error) ||
            !TryGetProfile(request.Payload, out string profile, out error))
        {
            return request.Failed(error!);
        }

        int effectiveCandidateLimit = candidateLimit ?? 20;
        int effectiveTestLimit = testLimit ?? 50;
        int effectiveReferenceLimit = referenceLimit ?? 200;
        BatchError? limitError =
            GetPositiveBatchLimitError("candidateLimit", effectiveCandidateLimit) ??
            GetPositiveBatchLimitError("testLimit", effectiveTestLimit) ??
            GetPositiveBatchLimitError("referenceLimit", effectiveReferenceLimit);
        if (limitError is not null)
        {
            return request.Failed(limitError);
        }

        if (!TryResolveTestProjectContext(
            loadedWorkspace,
            projectFilters,
            testProjectFilters,
            out IReadOnlyList<Project> projects,
            out IReadOnlyList<Project>? testProjects,
            out IReadOnlyList<TestProjectFilter>? projectOutputs,
            out error))
        {
            return request.Failed(error!);
        }

        TestSubject? subject;
        FuzzySelectionSection? selection = null;
        TestSelectionInput selectionInput;
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
                return request.Failed(sourceResult.Error.DiagnosticId, sourceResult.Error.Message);
            }

            SourceSymbolResolution resolved = sourceResult.Resolution!;
            SymbolSourceLocation? location = SymbolNavigationFacts.GetSourceLocations(resolved.Symbol, excludeGenerated).FirstOrDefault();
            subject = new TestSubject(
                resolved.Symbol.Name,
                resolved.Symbol.Kind.ToString(),
                SymbolNavigationFacts.GetContainer(resolved.Symbol),
                SymbolFactsBuilder.Create(resolved.Symbol, resolved.ProjectName),
                location?.Path,
                location?.Line,
                location?.Column,
                location?.EndLine,
                location?.EndColumn);
            selectionInput = new TestSelectionInput("sourcePosition", Query: null, CandidateId: null, resolved.File, resolved.Line, resolved.Column);
        }
        else
        {
            if (!TryGetFuzzyQuery(
                loadedWorkspace,
                defaults,
                request,
                readCandidateLimit: false,
                out FuzzyQueryOptions fuzzyOptions,
                out projects,
                out _,
                out error))
            {
                return request.Failed(error!);
            }

            fuzzyOptions = fuzzyOptions with { Limit = effectiveCandidateLimit };
            FuzzyCandidateResolution resolution = await new FuzzyDiscoveryResolver().ResolveCandidatesForSelectionAsync(
                projects,
                fuzzyOptions,
                cancellationToken);
            if (resolution.Error is not null)
            {
                return request.Failed(resolution.Error.DiagnosticId, resolution.Error.Message);
            }

            FuzzySymbolCandidate? selected = resolution.SelectedCandidate;
            subject = selected is null
                ? null
                : new TestSubject(selected.Name, selected.Kind, selected.Container, selected.Facts, selected.Path, selected.Line, selected.Column, selected.EndLine, selected.EndColumn);
            selection = new FuzzySelectionSection(
                resolution.Confidence,
                Math.Min(resolution.Candidates.Count, effectiveCandidateLimit),
                resolution.TotalCandidates,
                selected,
                [.. resolution.Candidates.Take(effectiveCandidateLimit)],
                resolution.SelectionExplanation);
            selectionInput = new TestSelectionInput(input.CandidateId is null ? "query" : "candidateId", input.Query?.Trim(), input.CandidateId?.Trim(), File: null, Line: null, Column: null);
        }

        TestImpactResolution impact = subject is null
            ? new TestImpactResolution([], new TestCandidatesSection(0, effectiveTestLimit, Truncated: false, Candidates: []), ["no-selected-symbol"])
            : await new TestImpactResolver().ResolveForSymbolAsync(
                loadedWorkspace,
                projects,
                testProjects,
                subject,
                new TestImpactOptions(effectiveTestLimit, effectiveReferenceLimit, includeSnippets, snippetLines, excludeGenerated),
                cancellationToken);

        TestsForSymbolResult result = new(
            Workspace: loadedWorkspace.DisplayPath,
            Kind: loadedWorkspace.Kind,
            Command: "tests-for-symbol",
            SelectionInput: selectionInput,
            Selection: selection,
            Subject: subject,
            Projects: projectOutputs,
            TestProjects: impact.TestProjects,
            Limits: new TestImpactLimits(effectiveCandidateLimit, effectiveTestLimit, effectiveReferenceLimit),
            Tests: impact.Tests,
            Truncated: impact.Tests.Truncated,
            Warnings: impact.Warnings,
            NextActions: []);
        return request.Success(OutputProfile.Format(loadedWorkspace, "tests-for-symbol", profile, result, new
        {
            projectFilters,
            testProjectFilters,
            excludeGenerated,
            candidateLimit = effectiveCandidateLimit,
            testLimit = effectiveTestLimit,
            referenceLimit = effectiveReferenceLimit,
            includeSnippets,
            snippetLines
        }));
    }

    private static async Task<BatchRequestResult> ExecuteTestsForDiffAsync(
        LoadedWorkspace loadedWorkspace,
        BatchDefaults defaults,
        BatchRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetDiffRequest(request.Payload, out DiffRequest diffRequest, out BatchError? error) ||
            !TryGetProjectFilters(request.Payload, defaults, out IReadOnlyList<string> projectFilters, out error) ||
            !TryGetTestProjectFilters(request.Payload, out IReadOnlyList<string> testProjectFilters, out error) ||
            !TryGetEffectiveExcludeGenerated(request.Payload, defaults, out bool excludeGenerated, out error) ||
            !TryGetOptionalInt(request.Payload, "symbolLimit", out int? symbolLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "testLimit", out int? testLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "referenceLimit", out int? referenceLimit, out error) ||
            !TryGetFuzzySnippetOptions(request.Payload, out bool includeSnippets, out int snippetLines, out error) ||
            !TryGetProfile(request.Payload, out string profile, out error))
        {
            return request.Failed(error!);
        }

        int effectiveSymbolLimit = symbolLimit ?? 50;
        int effectiveTestLimit = testLimit ?? 100;
        int effectiveReferenceLimit = referenceLimit ?? 200;
        BatchError? limitError =
            GetPositiveBatchLimitError("symbolLimit", effectiveSymbolLimit) ??
            GetPositiveBatchLimitError("testLimit", effectiveTestLimit) ??
            GetPositiveBatchLimitError("referenceLimit", effectiveReferenceLimit);
        if (limitError is not null)
        {
            return request.Failed(limitError);
        }

        if (!TryResolveTestProjectContext(
            loadedWorkspace,
            projectFilters,
            testProjectFilters,
            out IReadOnlyList<Project> projects,
            out IReadOnlyList<Project>? testProjects,
            out IReadOnlyList<TestProjectFilter>? projectOutputs,
            out error))
        {
            return request.Failed(error!);
        }

        string? repositoryRoot = PathDisplay.FindRepositoryRoot(loadedWorkspace.FullPath);
        if (repositoryRoot is null)
        {
            return request.Failed(DiagnosticIds.GitRepositoryNotFound, "Git repository root was not found for tests-for-diff.");
        }

        DiffReadResult diffResult = await new GitDiffProvider().ReadAsync(repositoryRoot, diffRequest, cancellationToken);
        if (diffResult.Error is not null)
        {
            return request.Failed(diffResult.Error.DiagnosticId, diffResult.Error.Message);
        }

        ChangedSymbolsResolution changedSymbols = await new ChangedSymbolResolver().ResolveAsync(
            loadedWorkspace,
            diffResult.Diff!,
            projects,
            excludeGenerated,
            effectiveSymbolLimit,
            cancellationToken);

        TestImpactResolution impact = await new TestImpactResolver().ResolveForChangedSymbolsAsync(
            loadedWorkspace,
            projects,
            testProjects,
            changedSymbols.ChangedSymbols.Symbols,
            new TestImpactOptions(effectiveTestLimit, effectiveReferenceLimit, includeSnippets, snippetLines, excludeGenerated),
            cancellationToken);

        TestsForDiffResult result = new(
            Workspace: loadedWorkspace.DisplayPath,
            Kind: loadedWorkspace.Kind,
            Command: "tests-for-diff",
            Diff: diffResult.Diff!,
            Projects: projectOutputs,
            TestProjects: impact.TestProjects,
            Limits: new TestImpactDiffLimits(effectiveSymbolLimit, effectiveTestLimit, effectiveReferenceLimit),
            ChangedSymbols: changedSymbols.ChangedSymbols,
            UnresolvedChanges: [.. changedSymbols.UnresolvedChanges],
            Tests: impact.Tests,
            Truncated: changedSymbols.ChangedSymbols.Truncated || impact.Tests.Truncated,
            Warnings: impact.Warnings,
            NextActions: []);
        return request.Success(OutputProfile.Format(loadedWorkspace, "tests-for-diff", profile, result, new
        {
            projectFilters,
            testProjectFilters,
            excludeGenerated,
            symbolLimit = effectiveSymbolLimit,
            testLimit = effectiveTestLimit,
            referenceLimit = effectiveReferenceLimit,
            includeSnippets,
            snippetLines
        }));
    }

    private static async Task<BatchRequestResult> ExecuteFrameworkEntrypointsAsync(
        LoadedWorkspace loadedWorkspace,
        BatchDefaults defaults,
        BatchRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetProjectFilters(request.Payload, defaults, out IReadOnlyList<string> projectFilters, out BatchError? error) ||
            !TryGetStringArray(request.Payload, "frameworks", out IReadOnlyList<string> frameworkFilters, out error, allowStringValue: true) ||
            !TryGetStringArray(request.Payload, "entrypointKinds", out IReadOnlyList<string> entrypointKindFilters, out error, allowStringValue: true) ||
            !TryGetEffectiveExcludeGenerated(request.Payload, defaults, out bool excludeGenerated, out error) ||
            !TryGetOptionalInt(request.Payload, "limit", out int? limit, out error) ||
            !TryGetOptionalInt(request.Payload, "evidenceLimit", out int? evidenceLimit, out error) ||
            !TryGetFuzzySnippetOptions(request.Payload, out bool includeSnippets, out int snippetLines, out error) ||
            !TryGetProfile(request.Payload, out string profile, out error))
        {
            return request.Failed(error!);
        }

        if (frameworkFilters.Count == 0 &&
            !TryGetStringArray(request.Payload, "framework", out frameworkFilters, out error, allowStringValue: true) ||
            entrypointKindFilters.Count == 0 &&
            !TryGetStringArray(request.Payload, "entrypointKind", out entrypointKindFilters, out error, allowStringValue: true))
        {
            return request.Failed(error!);
        }

        int effectiveLimit = limit ?? 100;
        int effectiveEvidenceLimit = evidenceLimit ?? 5;
        BatchError? limitError =
            GetPositiveBatchLimitError("limit", effectiveLimit) ??
            GetPositiveBatchLimitError("evidenceLimit", effectiveEvidenceLimit);
        if (limitError is not null)
        {
            return request.Failed(limitError);
        }

        if (!TryNormalizeFrameworks(frameworkFilters, out IReadOnlyList<string> frameworks, out error))
        {
            return request.Failed(error!);
        }

        ProjectFilterResolutionResult projectResult = new ProjectFilterResolver().ResolveMany(loadedWorkspace.Solution, projectFilters);
        if (projectResult.Error is not null)
        {
            return request.Failed(projectResult.Error);
        }

        FrameworkEntrypointsResult result = await new FrameworkEntrypointDiscoveryResolver().DiscoverAsync(
            loadedWorkspace,
            projectResult.Projects,
            projectResult.AppliedFilters.Count == 0
                ? null
                : projectResult.AppliedFilters.Select(filter => new FrameworkEntrypointProjectFilter(filter.Filter, filter.Name, filter.Path, filter.TargetFramework)).ToArray(),
            new FrameworkEntrypointOptions(frameworks, effectiveLimit, effectiveEvidenceLimit, includeSnippets, snippetLines, excludeGenerated),
            cancellationToken);

        IReadOnlyList<string> normalizedKinds = [.. entrypointKindFilters
            .SelectMany(SplitValues)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)];
        if (normalizedKinds.Count > 0)
        {
            HashSet<string> kinds = new(normalizedKinds, StringComparer.Ordinal);
            IReadOnlyList<FrameworkEntrypointItem> filtered = [.. result.Entrypoints.Items.Where(item => kinds.Contains(item.EntrypointKind))];
            result = result with
            {
                Entrypoints = new FrameworkEntrypointsSection(
                    TotalEntrypoints: filtered.Count,
                    Limit: result.Entrypoints.Limit,
                    Truncated: false,
                    Items: filtered),
                Truncated = false,
                Warnings = filtered.Count == 0 ? ["no-framework-entrypoints-found"] : []
            };
        }

        return request.Success(OutputProfile.Format(loadedWorkspace, "framework-entrypoints", profile, result, new
        {
            projectFilters,
            frameworks,
            entrypointKindFilters,
            excludeGenerated,
            limit = effectiveLimit,
            evidenceLimit = effectiveEvidenceLimit,
            includeSnippets,
            snippetLines
        }));
    }

    private static async Task<BatchRequestResult> ExecuteDiGraphAsync(
        LoadedWorkspace loadedWorkspace,
        BatchDefaults defaults,
        BatchRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetProjectFilters(request.Payload, defaults, out IReadOnlyList<string> projectFilters, out BatchError? error) ||
            !TryGetOptionalInt(request.Payload, "registrationLimit", out int? registrationLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "dependencyLimit", out int? dependencyLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "riskLimit", out int? riskLimit, out error) ||
            !TryGetDefaultTrueBool(request.Payload, "includeOptions", out bool includeOptions, out error) ||
            !TryGetDefaultTrueBool(request.Payload, "includeHostedServices", out bool includeHostedServices, out error) ||
            !TryGetDefaultTrueBool(request.Payload, "includeRisks", out bool includeRisks, out error) ||
            !TryGetEffectiveExcludeGenerated(request.Payload, defaults, out bool excludeGenerated, out error) ||
            !TryGetFuzzySnippetOptions(request.Payload, out bool includeSnippets, out int snippetLines, out error) ||
            !TryGetProfile(request.Payload, out string profile, out error))
        {
            return request.Failed(error!);
        }

        int effectiveRegistrationLimit = registrationLimit ?? 200;
        int effectiveDependencyLimit = dependencyLimit ?? 300;
        int effectiveRiskLimit = riskLimit ?? 100;
        BatchError? limitError =
            GetPositiveBatchLimitError("registrationLimit", effectiveRegistrationLimit) ??
            GetPositiveBatchLimitError("dependencyLimit", effectiveDependencyLimit) ??
            GetPositiveBatchLimitError("riskLimit", effectiveRiskLimit);
        if (limitError is not null)
        {
            return request.Failed(limitError);
        }

        if (!TryResolveDiProjects(loadedWorkspace, projectFilters, out IReadOnlyList<Project> projects, out IReadOnlyList<DiProjectFilter>? projectOutputs, out error))
        {
            return request.Failed(error!);
        }

        DiGraphResolution resolution = await new DiRegistrationResolver().ResolveAsync(
            loadedWorkspace,
            projects,
            new DiGraphOptions(
                effectiveRegistrationLimit,
                effectiveDependencyLimit,
                effectiveRiskLimit,
                includeOptions,
                includeHostedServices,
                includeRisks,
                includeSnippets,
                snippetLines,
                excludeGenerated),
            cancellationToken);

        DiRegistrationsSection registrations = DiRegistrationResolver.CreateRegistrationsSection(resolution.Registrations, effectiveRegistrationLimit);
        DiDependenciesSection dependencies = DiRegistrationResolver.CreateDependenciesSection(resolution.Dependencies, effectiveDependencyLimit);
        DiRisksSection risks = DiRegistrationResolver.CreateRisksSection(resolution.Risks, effectiveRiskLimit);
        DiGraphResult result = new(
            Workspace: loadedWorkspace.DisplayPath,
            Kind: loadedWorkspace.Kind,
            Command: "di-graph",
            Projects: projectOutputs,
            Limits: new DiGraphLimits(effectiveRegistrationLimit, effectiveDependencyLimit, effectiveRiskLimit),
            Registrations: registrations,
            Dependencies: dependencies,
            Risks: risks,
            Truncated: registrations.Truncated || dependencies.Truncated || risks.Truncated,
            Warnings: resolution.Warnings,
            NextActions: []);
        return request.Success(OutputProfile.Format(loadedWorkspace, "di-graph", profile, result, new
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
            snippetLines
        }));
    }

    private static async Task<BatchRequestResult> ExecuteWhereRegisteredAsync(
        LoadedWorkspace loadedWorkspace,
        BatchDefaults defaults,
        BatchRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetSymbolSelectionInput(request.Payload, "where-registered", out _, out BatchError? error) ||
            !TryGetProjectFilters(request.Payload, defaults, out IReadOnlyList<string> projectFilters, out error) ||
            !TryGetEffectiveExcludeGenerated(request.Payload, defaults, out bool excludeGenerated, out error) ||
            !TryGetOptionalInt(request.Payload, "candidateLimit", out int? candidateLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "registrationLimit", out int? registrationLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "dependencyLimit", out int? dependencyLimit, out error) ||
            !TryGetFuzzySnippetOptions(request.Payload, out bool includeSnippets, out int snippetLines, out error) ||
            !TryGetProfile(request.Payload, out string profile, out error))
        {
            return request.Failed(error!);
        }

        int effectiveCandidateLimit = candidateLimit ?? 20;
        int effectiveRegistrationLimit = registrationLimit ?? 50;
        int effectiveDependencyLimit = dependencyLimit ?? 100;
        BatchError? limitError =
            GetPositiveBatchLimitError("candidateLimit", effectiveCandidateLimit) ??
            GetPositiveBatchLimitError("registrationLimit", effectiveRegistrationLimit) ??
            GetPositiveBatchLimitError("dependencyLimit", effectiveDependencyLimit);
        if (limitError is not null)
        {
            return request.Failed(limitError);
        }

        if (!TryResolveDiProjects(loadedWorkspace, projectFilters, out IReadOnlyList<Project> projects, out _, out error))
        {
            return request.Failed(error!);
        }

        DiSubjectResolution subject = await ResolveDiSubjectForBatchAsync(
            loadedWorkspace,
            defaults,
            request,
            excludeGenerated,
            effectiveCandidateLimit,
            cancellationToken);
        if (!subject.Success)
        {
            return request.Failed(subject.DiagnosticId ?? DiagnosticIds.ParseError, subject.DiagnosticMessage ?? "Invalid where-registered request.");
        }

        DiGraphResolution graph = await new DiRegistrationResolver().ResolveAsync(
            loadedWorkspace,
            projects,
            new DiGraphOptions(effectiveRegistrationLimit, effectiveDependencyLimit, RiskLimit: 1, IncludeOptions: true, IncludeHostedServices: true, IncludeRisks: false, includeSnippets, snippetLines, excludeGenerated),
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
            Workspace: loadedWorkspace.DisplayPath,
            Kind: loadedWorkspace.Kind,
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
        return request.Success(OutputProfile.Format(loadedWorkspace, "where-registered", profile, result, new
        {
            projectFilters,
            excludeGenerated,
            candidateLimit = effectiveCandidateLimit,
            registrationLimit = effectiveRegistrationLimit,
            dependencyLimit = effectiveDependencyLimit,
            includeSnippets,
            snippetLines
        }));
    }

    private static async Task<BatchRequestResult> ExecuteDiImpactAsync(
        LoadedWorkspace loadedWorkspace,
        BatchDefaults defaults,
        BatchRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetSymbolSelectionInput(request.Payload, "di-impact", out _, out BatchError? error) ||
            !TryGetProjectFilters(request.Payload, defaults, out IReadOnlyList<string> projectFilters, out error) ||
            !TryGetEffectiveExcludeGenerated(request.Payload, defaults, out bool excludeGenerated, out error) ||
            !TryGetOptionalInt(request.Payload, "candidateLimit", out int? candidateLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "registrationLimit", out int? registrationLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "consumerLimit", out int? consumerLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "dependencyLimit", out int? dependencyLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "riskLimit", out int? riskLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "depth", out int? depth, out error) ||
            !TryGetFuzzySnippetOptions(request.Payload, out bool includeSnippets, out int snippetLines, out error) ||
            !TryGetProfile(request.Payload, out string profile, out error))
        {
            return request.Failed(error!);
        }

        int effectiveCandidateLimit = candidateLimit ?? 20;
        int effectiveRegistrationLimit = registrationLimit ?? 50;
        int effectiveConsumerLimit = consumerLimit ?? 50;
        int effectiveDependencyLimit = dependencyLimit ?? 100;
        int effectiveRiskLimit = riskLimit ?? 50;
        int effectiveDepth = depth ?? 2;
        BatchError? limitError =
            GetPositiveBatchLimitError("candidateLimit", effectiveCandidateLimit) ??
            GetPositiveBatchLimitError("registrationLimit", effectiveRegistrationLimit) ??
            GetPositiveBatchLimitError("consumerLimit", effectiveConsumerLimit) ??
            GetPositiveBatchLimitError("dependencyLimit", effectiveDependencyLimit) ??
            GetPositiveBatchLimitError("riskLimit", effectiveRiskLimit) ??
            GetNonNegativeBatchLimitError("depth", effectiveDepth);
        if (limitError is not null)
        {
            return request.Failed(limitError);
        }

        if (!TryResolveDiProjects(loadedWorkspace, projectFilters, out IReadOnlyList<Project> projects, out _, out error))
        {
            return request.Failed(error!);
        }

        DiSubjectResolution subject = await ResolveDiSubjectForBatchAsync(
            loadedWorkspace,
            defaults,
            request,
            excludeGenerated,
            effectiveCandidateLimit,
            cancellationToken);
        if (!subject.Success)
        {
            return request.Failed(subject.DiagnosticId ?? DiagnosticIds.ParseError, subject.DiagnosticMessage ?? "Invalid di-impact request.");
        }

        DiGraphResolution graph = await new DiRegistrationResolver().ResolveAsync(
            loadedWorkspace,
            projects,
            new DiGraphOptions(
                effectiveRegistrationLimit,
                effectiveDependencyLimit,
                effectiveRiskLimit,
                IncludeOptions: true,
                IncludeHostedServices: true,
                IncludeRisks: true,
                includeSnippets,
                snippetLines,
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
            Workspace: loadedWorkspace.DisplayPath,
            Kind: loadedWorkspace.Kind,
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
        return request.Success(OutputProfile.Format(loadedWorkspace, "di-impact", profile, result, new
        {
            projectFilters,
            excludeGenerated,
            candidateLimit = effectiveCandidateLimit,
            registrationLimit = effectiveRegistrationLimit,
            consumerLimit = effectiveConsumerLimit,
            dependencyLimit = effectiveDependencyLimit,
            riskLimit = effectiveRiskLimit,
            depth = effectiveDepth,
            includeSnippets,
            snippetLines
        }));
    }


    private static bool TryGetDefaultTrueBool(
        JsonElement payload,
        string propertyName,
        out bool value,
        out BatchError? error)
    {
        if (!TryGetOptionalBool(payload, propertyName, out bool? optionalValue, out error))
        {
            value = true;
            return false;
        }

        value = optionalValue ?? true;
        return true;
    }

    private static bool TryGetSymbolSelectionInput(
        JsonElement payload,
        string commandName,
        out SymbolSelectionInput input,
        out BatchError? error)
    {
        input = default!;
        if (!TryGetOptionalString(payload, "query", out string? query, out error) ||
            !TryGetOptionalString(payload, "candidateId", out string? candidateId, out error) ||
            !TryGetOptionalFile(payload, "file", out FileInfo? file, out error) ||
            !TryGetOptionalInt(payload, "line", out int? line, out error) ||
            !TryGetOptionalInt(payload, "column", out int? column, out error))
        {
            return false;
        }

        bool hasQuery = !string.IsNullOrWhiteSpace(query);
        bool hasCandidate = !string.IsNullOrWhiteSpace(candidateId);
        bool hasPosition = file is not null || line is not null || column is not null;
        if ((hasQuery ? 1 : 0) + (hasCandidate ? 1 : 0) + (hasPosition ? 1 : 0) != 1 ||
            hasPosition && (file is null || line is null || column is null))
        {
            error = BatchError.FromDiagnostic(
                DiagnosticIds.ParseError,
                $"Specify exactly one {commandName} input mode: query, candidateId, or file with line and column.");
            return false;
        }

        input = new SymbolSelectionInput(
            string.IsNullOrWhiteSpace(query) ? null : query,
            string.IsNullOrWhiteSpace(candidateId) ? null : candidateId,
            file,
            line,
            column);
        error = null;
        return true;
    }

    private static bool TryGetTestProjectFilters(
        JsonElement payload,
        out IReadOnlyList<string> testProjectFilters,
        out BatchError? error)
    {
        return TryGetResultStringFilters(payload, "testProject", "testProjects", out testProjectFilters, out error);
    }

    private static bool TryResolveTestProjectContext(
        LoadedWorkspace loadedWorkspace,
        IReadOnlyList<string> projectFilters,
        IReadOnlyList<string> testProjectFilters,
        out IReadOnlyList<Project> projects,
        out IReadOnlyList<Project>? testProjects,
        out IReadOnlyList<TestProjectFilter>? projectOutputs,
        out BatchError? error)
    {
        projects = [];
        testProjects = null;
        projectOutputs = null;

        ProjectFilterResolutionResult projectResult = new ProjectFilterResolver().ResolveMany(loadedWorkspace.Solution, projectFilters);
        if (projectResult.Error is not null)
        {
            error = BatchError.FromDiagnostic(projectResult.Error.DiagnosticId, projectResult.Error.Message);
            return false;
        }

        ProjectFilterResolutionResult testProjectResult = new ProjectFilterResolver().ResolveMany(loadedWorkspace.Solution, testProjectFilters);
        if (testProjectResult.Error is not null)
        {
            error = BatchError.FromDiagnostic(testProjectResult.Error.DiagnosticId, testProjectResult.Error.Message);
            return false;
        }

        projects = projectResult.Projects;
        testProjects = testProjectFilters.Count == 0 ? null : testProjectResult.Projects;
        projectOutputs = projectResult.AppliedFilters.Count == 0
            ? null
            : projectResult.AppliedFilters.Select(filter => new TestProjectFilter(filter.Filter, filter.Name, filter.Path, filter.TargetFramework)).ToArray();
        error = null;
        return true;
    }

    private static bool TryResolveDiProjects(
        LoadedWorkspace loadedWorkspace,
        IReadOnlyList<string> projectFilters,
        out IReadOnlyList<Project> projects,
        out IReadOnlyList<DiProjectFilter>? projectOutputs,
        out BatchError? error)
    {
        projects = [];
        projectOutputs = null;

        ProjectFilterResolutionResult projectResult = new ProjectFilterResolver().ResolveMany(loadedWorkspace.Solution, projectFilters);
        if (projectResult.Error is not null)
        {
            error = BatchError.FromDiagnostic(projectResult.Error.DiagnosticId, projectResult.Error.Message);
            return false;
        }

        projects = projectResult.Projects;
        projectOutputs = projectResult.AppliedFilters.Count == 0
            ? null
            : projectResult.AppliedFilters.Select(filter => new DiProjectFilter(filter.Filter, filter.Name, filter.Path, filter.TargetFramework)).ToArray();
        error = null;
        return true;
    }

    private static async Task<DiSubjectResolution> ResolveDiSubjectForBatchAsync(
        LoadedWorkspace loadedWorkspace,
        BatchDefaults defaults,
        BatchRequest request,
        bool excludeGenerated,
        int candidateLimit,
        CancellationToken cancellationToken)
    {
        if (!TryGetSymbolSelectionInput(request.Payload, request.Command, out SymbolSelectionInput input, out BatchError? inputError))
        {
            return DiSubjectResolution.Failed(DiagnosticNumber(inputError), inputError?.Message, ExitCodes.UsageError);
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
                return DiSubjectResolution.Failed(sourceResult.Error.DiagnosticId, sourceResult.Error.Message, sourceResult.Error.ExitCode);
            }

            SourceSymbolResolution resolved = sourceResult.Resolution!;
            ISymbol symbol = resolved.Symbol is INamedTypeSymbol
                ? resolved.Symbol
                : resolved.Symbol.ContainingType ?? resolved.Symbol;
            if (symbol is not INamedTypeSymbol)
            {
                return DiSubjectResolution.Failed(DiagnosticIds.SymbolNotFoundAtPosition, "Selected symbol is not a type or a member contained by a type.", ExitCodes.UsageError);
            }

            return DiSubjectResolution.Succeeded(
                new DiSelectionInput("sourcePosition", Query: null, CandidateId: null, resolved.File, resolved.Line, resolved.Column),
                selection: null,
                subject: CreateDiSubject(symbol, resolved.ProjectName, excludeGenerated));
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
            return DiSubjectResolution.Failed(DiagnosticNumber(error), error?.Message, ExitCodes.UsageError);
        }

        fuzzyOptions = fuzzyOptions with { Limit = candidateLimit };
        FuzzyCandidateResolution resolution = await new FuzzyDiscoveryResolver().ResolveCandidatesForSelectionAsync(
            projects,
            fuzzyOptions,
            cancellationToken);
        if (resolution.Error is not null)
        {
            return DiSubjectResolution.Failed(resolution.Error.DiagnosticId, resolution.Error.Message, resolution.Error.ExitCode);
        }

        FuzzySymbolCandidate? selected = resolution.SelectedCandidate;
        DiTypeInfo? subject = selected is null
            ? null
            : new DiTypeInfo(
                selected.Name,
                selected.Kind,
                selected.Container,
                selected.Facts,
                selected.Path,
                selected.Line,
                selected.Column,
                selected.EndLine,
                selected.EndColumn);

        return DiSubjectResolution.Succeeded(
            new DiSelectionInput(input.CandidateId is null ? "query" : "candidateId", input.Query?.Trim(), input.CandidateId?.Trim(), File: null, Line: null, Column: null),
            new DiSelectionSection(
                resolution.Confidence,
                Math.Min(resolution.Candidates.Count, candidateLimit),
                resolution.TotalCandidates,
                selected,
                [.. resolution.Candidates.Take(candidateLimit)],
                resolution.SelectionExplanation),
            subject);
    }

    private static DiTypeInfo CreateDiSubject(ISymbol symbol, string? projectName, bool excludeGenerated)
    {
        SymbolSourceLocation? location = SymbolNavigationFacts.GetSourceLocations(symbol, excludeGenerated).FirstOrDefault();
        return new DiTypeInfo(
            symbol.Name,
            symbol.Kind.ToString(),
            SymbolNavigationFacts.GetContainer(symbol),
            SymbolFactsBuilder.Create(symbol, projectName),
            location?.Path,
            location?.Line,
            location?.Column,
            location?.EndLine,
            location?.EndColumn);
    }

    private static DiConsumersSection CreateConsumersSection(IReadOnlyList<DiConsumerItem> consumers, int limit)
    {
        IReadOnlyList<DiConsumerItem> ordered = [.. consumers
            .GroupBy(consumer => DiRegistrationResolver.Identity(consumer.ConsumerType), StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(consumer => DiRegistrationResolver.Identity(consumer.ConsumerType), StringComparer.Ordinal)];
        return new DiConsumersSection(ordered.Count, limit, ordered.Count > limit, [.. ordered.Take(limit)]);
    }

    private static bool TryNormalizeFrameworks(
        IReadOnlyList<string> filters,
        out IReadOnlyList<string> frameworks,
        out BatchError? error)
    {
        IReadOnlyList<string> values = filters.Count == 0
            ? DefaultFrameworks
            : [.. filters.SelectMany(SplitValues).Where(value => value.Length > 0)];

        HashSet<string> allowed = new(AllowedFrameworks, StringComparer.Ordinal);
        string? invalid = values.FirstOrDefault(value => !allowed.Contains(value));
        if (invalid is not null)
        {
            frameworks = [];
            error = BatchError.FromDiagnostic(
                DiagnosticIds.ParseError,
                $"Invalid framework value '{invalid}'. Allowed values: {string.Join(", ", AllowedFrameworks)}.");
            return false;
        }

        frameworks = [.. values.Distinct(StringComparer.Ordinal).OrderBy(FrameworkOrder).ThenBy(value => value, StringComparer.Ordinal)];
        error = null;
        return true;
    }

    private static IEnumerable<string> SplitValues(string value)
    {
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static int FrameworkOrder(string framework)
    {
        int index = Array.IndexOf(AllowedFrameworks, framework);
        return index < 0 ? 100 : index;
    }

    private static int? DiagnosticNumber(BatchError? error)
    {
        if (error is null ||
            !error.Code.StartsWith(DiagnosticIds.Prefix, StringComparison.Ordinal) ||
            !int.TryParse(error.Code[DiagnosticIds.Prefix.Length..], out int diagnosticId))
        {
            return null;
        }

        return diagnosticId;
    }

}

using System.CommandLine;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Navlyn.Cli.OutputProfiles;
using Navlyn.ContextPacks;
using Navlyn.Diagnostics;
using Navlyn.Diffs;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static partial class BatchCommand
{
    public static Command Create()
    {
        Option<FileInfo?> inputOption = new("--input")
        {
            Description = "Path to a batch JSON file. When omitted, batch JSON is read from stdin."
        };

        return WorkspaceCommand.Create(
            "batch",
            "Run multiple machine-readable requests through one workspace load.",
            [inputOption],
            (workspace, parseResult, cancellationToken) => ExecuteAsync(
                workspace,
                parseResult.GetValue(inputOption),
                cancellationToken));
    }

    private static async Task<int> ExecuteAsync(
        LoadedWorkspace loadedWorkspace,
        FileInfo? input,
        CancellationToken cancellationToken)
    {
        string inputJson;
        try
        {
            inputJson = input is null
                ? await Console.In.ReadToEndAsync(cancellationToken)
                : await File.ReadAllTextAsync(input.FullName, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            DiagnosticReporter.WriteError(
                DiagnosticIds.InvalidBatchInput,
                $"Failed to read batch input: {ex.Message}");
            return ExitCodes.UsageError;
        }

        JsonDocument parsedDocument;
        try
        {
            parsedDocument = JsonDocument.Parse(inputJson);
        }
        catch (JsonException ex)
        {
            DiagnosticReporter.WriteError(
                DiagnosticIds.InvalidBatchInput,
                $"Invalid batch JSON: {ex.Message}");
            return ExitCodes.UsageError;
        }

        using (parsedDocument)
        {
            if (!TryReadBatchInput(parsedDocument.RootElement, out BatchInput batchInput, out string? error))
            {
                DiagnosticReporter.WriteError(DiagnosticIds.InvalidBatchInput, error!);
                return ExitCodes.UsageError;
            }

            List<BatchRequestResult> results = [];
            foreach (BatchRequest request in batchInput.Requests)
            {
                results.Add(await ExecuteRequestAsync(loadedWorkspace, batchInput.Defaults, request, cancellationToken));
            }

            ConsoleJsonWriter.Write(new BatchResult(
                Workspace: loadedWorkspace.DisplayPath,
                Kind: loadedWorkspace.Kind,
                TotalRequests: results.Count,
                SucceededRequests: results.Count(result => result.Ok),
                FailedRequests: results.Count(result => !result.Ok),
                Results: results));
        }

        return ExitCodes.Success;
    }

    private static async Task<BatchRequestResult> ExecuteReviewDiffAsync(
        LoadedWorkspace loadedWorkspace,
        BatchDefaults defaults,
        BatchRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetDiffRequest(request.Payload, out DiffRequest diffRequest, out BatchError? error) ||
            !TryGetDiffProjectContext(loadedWorkspace, defaults, request.Payload, out IReadOnlyList<Project> projects, out IReadOnlyList<DiffProjectFilter>? projectOutputs, out error) ||
            !TryGetEffectiveExcludeGenerated(request.Payload, defaults, out bool excludeGenerated, out error) ||
            !TryGetOptionalInt(request.Payload, "symbolLimit", out int? symbolLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "impactLimit", out int? impactLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "diagnosticLimit", out int? diagnosticLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "relatedTestLimit", out int? relatedTestLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "depth", out int? depth, out error) ||
            !TryGetOptionalBool(request.Payload, "includeSnippets", out bool? includeSnippets, out error) ||
            !TryGetOptionalInt(request.Payload, "snippetLines", out int? snippetLines, out error) ||
            !TryGetProfile(request.Payload, out string profile, out error))
        {
            return request.Failed(error!);
        }

        int effectiveSymbolLimit = symbolLimit ?? 50;
        int effectiveImpactLimit = impactLimit ?? 100;
        int effectiveDiagnosticLimit = diagnosticLimit ?? 100;
        int effectiveRelatedTestLimit = relatedTestLimit ?? 50;
        int effectiveDepth = depth ?? 2;
        int effectiveSnippetLines = snippetLines ?? 1;
        BatchError? limitError =
            GetPositiveBatchLimitError("symbolLimit", effectiveSymbolLimit) ??
            GetPositiveBatchLimitError("impactLimit", effectiveImpactLimit) ??
            GetPositiveBatchLimitError("diagnosticLimit", effectiveDiagnosticLimit) ??
            GetPositiveBatchLimitError("relatedTestLimit", effectiveRelatedTestLimit) ??
            GetNonNegativeBatchLimitError("depth", effectiveDepth) ??
            GetNonNegativeBatchLimitError("snippetLines", effectiveSnippetLines);
        if (limitError is not null)
        {
            return request.Failed(limitError);
        }

        DiffWorkflowExecutionResult<ReviewDiffResult> result =
            await new DiffWorkflowResolver().ResolveReviewAsync(
                loadedWorkspace,
                diffRequest,
                projects,
                projectOutputs,
                excludeGenerated,
                effectiveSymbolLimit,
                effectiveImpactLimit,
                effectiveDiagnosticLimit,
                effectiveRelatedTestLimit,
                effectiveDepth,
                includeSnippets ?? false,
                effectiveSnippetLines,
                cancellationToken);

        return result.Error is not null
            ? request.Failed(result.Error.DiagnosticId, result.Error.Message)
            : request.Success(OutputProfile.Format(loadedWorkspace, "review-diff", profile, result.Result!, new
            {
                excludeGenerated,
                symbolLimit = effectiveSymbolLimit,
                impactLimit = effectiveImpactLimit,
                diagnosticLimit = effectiveDiagnosticLimit,
                relatedTestLimit = effectiveRelatedTestLimit,
                depth = effectiveDepth,
                includeSnippets = includeSnippets ?? false,
                snippetLines = effectiveSnippetLines
            }));
    }

    private static async Task<BatchRequestResult> ExecuteContextPackAsync(
        LoadedWorkspace loadedWorkspace,
        BatchDefaults defaults,
        BatchRequest request,
        CancellationToken cancellationToken)
    {
        bool hasQuery = request.Payload.TryGetProperty("query", out JsonElement queryElement) &&
            queryElement.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(queryElement.GetString());
        bool hasCandidateId = request.Payload.TryGetProperty("candidateId", out JsonElement candidateIdElement) &&
            candidateIdElement.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(candidateIdElement.GetString());
        if (!TryGetOptionalBool(request.Payload, "diff", out bool? diffValue, out BatchError? error) ||
            !TryGetOptionalString(request.Payload, "goal", out string? goal, out error) ||
            !TryGetOptionalString(request.Payload, "changeKind", out string? changeKind, out error) ||
            !TryGetOptionalString(request.Payload, "snippetPolicy", out string? snippetPolicy, out error) ||
            !TryGetOptionalInt(request.Payload, "budgetTokens", out int? budgetTokens, out error) ||
            !TryGetOptionalInt(request.Payload, "itemLimit", out int? itemLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "snippetLines", out int? snippetLines, out error) ||
            !TryGetOptionalInt(request.Payload, "candidateLimit", out int? candidateLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "memberLimit", out int? memberLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "referenceLimit", out int? referenceLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "relationLimit", out int? relationLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "fileLimit", out int? fileLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "diagnosticLimit", out int? diagnosticLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "symbolLimit", out int? symbolLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "impactLimit", out int? impactLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "relatedTestLimit", out int? relatedTestLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "depth", out int? depth, out error) ||
            !TryGetEffectiveExcludeGenerated(request.Payload, defaults, out bool excludeGenerated, out error) ||
            !TryGetProfile(request.Payload, out string profile, out error))
        {
            return request.Failed(error!);
        }

        bool diff = diffValue ?? false;
        if ((hasQuery || hasCandidateId) == diff || (hasQuery && hasCandidateId))
        {
            return request.Failed(DiagnosticIds.InvalidBatchInput, "Specify exactly one context-pack input mode: query, candidateId, or diff.");
        }

        if (!diff &&
            (request.Payload.TryGetProperty("base", out _) ||
                request.Payload.TryGetProperty("head", out _) ||
                request.Payload.TryGetProperty("staged", out _) ||
                request.Payload.TryGetProperty("includeUnstaged", out _)))
        {
            return request.Failed(DiagnosticIds.InvalidDiffOptions, "Diff options require diff: true.");
        }

        if (goal is not null && goal is not ("review" or "modify" or "understand"))
        {
            return request.Failed(DiagnosticIds.ParseError, "goal must be review, modify, or understand.");
        }

        if (changeKind is not null && changeKind is not ("behavior" or "signature" or "rename" or "constructor" or "nullability" or "async" or "public-api" or "di-registration" or "endpoint"))
        {
            return request.Failed(DiagnosticIds.ParseError, "changeKind must be behavior, signature, rename, constructor, nullability, async, public-api, di-registration, or endpoint.");
        }

        if (snippetPolicy is not null && snippetPolicy is not ("none" or "signature" or "line" or "block"))
        {
            return request.Failed(DiagnosticIds.ParseError, "snippetPolicy must be none, signature, line, or block.");
        }

        ContextPackOptions options = new(
            Goal: goal ?? (diff ? "review" : "understand"),
            BudgetTokens: budgetTokens ?? 8000,
            ItemLimit: itemLimit ?? 80,
            SnippetPolicy: snippetPolicy ?? "line",
            SnippetLines: snippetLines ?? 1,
            CandidateLimit: candidateLimit ?? 20,
            MemberLimit: memberLimit ?? 50,
            ReferenceLimit: referenceLimit ?? 100,
            RelationLimit: relationLimit ?? 25,
            FileLimit: fileLimit ?? 50,
            QueryDiagnosticLimit: diagnosticLimit ?? 50,
            SymbolLimit: symbolLimit ?? 50,
            ImpactLimit: impactLimit ?? 100,
            DiffDiagnosticLimit: diagnosticLimit ?? 100,
            RelatedTestLimit: relatedTestLimit ?? 50,
            Depth: depth ?? 2,
            ChangeKind: changeKind);

        BatchError? limitError =
            GetPositiveBatchLimitError("budgetTokens", options.BudgetTokens) ??
            GetPositiveBatchLimitError("itemLimit", options.ItemLimit) ??
            GetNonNegativeBatchLimitError("snippetLines", options.SnippetLines) ??
            GetPositiveBatchLimitError("candidateLimit", options.CandidateLimit) ??
            GetPositiveBatchLimitError("memberLimit", options.MemberLimit) ??
            GetPositiveBatchLimitError("referenceLimit", options.ReferenceLimit) ??
            GetPositiveBatchLimitError("relationLimit", options.RelationLimit) ??
            GetPositiveBatchLimitError("fileLimit", options.FileLimit) ??
            GetPositiveBatchLimitError("diagnosticLimit", diff ? options.DiffDiagnosticLimit : options.QueryDiagnosticLimit) ??
            GetPositiveBatchLimitError("symbolLimit", options.SymbolLimit) ??
            GetPositiveBatchLimitError("impactLimit", options.ImpactLimit) ??
            GetPositiveBatchLimitError("relatedTestLimit", options.RelatedTestLimit) ??
            GetNonNegativeBatchLimitError("depth", options.Depth);
        if (limitError is not null)
        {
            return request.Failed(limitError);
        }

        ContextPackResolver resolver = new();
        if (!diff)
        {
            if (!TryGetFuzzyQuery(
                loadedWorkspace,
                defaults,
                request,
                readCandidateLimit: false,
                out FuzzyQueryOptions queryOptions,
                out IReadOnlyList<Project> projects,
                out IReadOnlyList<FuzzyProjectFilter>? projectOutputs,
                out error))
            {
                return request.Failed(error!);
            }

            queryOptions = queryOptions with { Limit = options.CandidateLimit, ExcludeGenerated = excludeGenerated };
            FuzzyDiscoveryResolver fuzzyResolver = new();
            BatchRequestResult? failedResult = await ValidateFuzzySelectionAsync(request, fuzzyResolver, projects, queryOptions, cancellationToken);
            if (failedResult is not null)
            {
                return failedResult;
            }

            ContextPackResult result = await resolver.ResolveQueryAsync(
                loadedWorkspace,
                queryOptions,
                projects,
                projectOutputs,
                excludeGenerated,
                options,
                cancellationToken);
            return request.Success(OutputProfile.Format(loadedWorkspace, "context-pack", profile, result, new
            {
                mode = hasQuery ? "query" : "candidateId",
                goal = options.Goal,
                changeKind = options.ChangeKind,
                excludeGenerated,
                options
            }));
        }

        if (!TryGetDiffRequest(request.Payload, out DiffRequest diffRequest, out error) ||
            !TryGetDiffProjectContext(loadedWorkspace, defaults, request.Payload, out IReadOnlyList<Project> diffProjects, out IReadOnlyList<DiffProjectFilter>? diffProjectOutputs, out error))
        {
            return request.Failed(error!);
        }

        DiffWorkflowExecutionResult<ContextPackResult> diffResult = await resolver.ResolveDiffAsync(
            loadedWorkspace,
            diffRequest,
            diffProjects,
            diffProjectOutputs,
            excludeGenerated,
            options,
            cancellationToken);

        return diffResult.Error is not null
            ? request.Failed(diffResult.Error.DiagnosticId, diffResult.Error.Message)
            : request.Success(OutputProfile.Format(loadedWorkspace, "context-pack", profile, diffResult.Result!, new
            {
                mode = "diff",
                changeKind = options.ChangeKind,
                excludeGenerated,
                options
            }));
    }

    private static object CreateOverviewResult(LoadedWorkspace loadedWorkspace)
    {
        return new OverviewResult(
            Workspace: loadedWorkspace.DisplayPath,
            Kind: loadedWorkspace.Kind,
            Projects: loadedWorkspace.Projects.Select(project => new OverviewProjectResult(
                Name: project.Name,
                Path: project.Path,
                Language: project.Language,
                AssemblyName: project.AssemblyName,
                TargetFramework: project.TargetFramework,
                LanguageVersion: project.LanguageVersion,
                PreprocessorSymbols: project.PreprocessorSymbols.Count == 0
                    ? null
                    : project.PreprocessorSymbols)).ToArray());
    }

    private static async Task<BatchRequestResult> ExecuteDiagnosticsAsync(
        LoadedWorkspace loadedWorkspace,
        BatchDefaults defaults,
        BatchRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetProjectFilters(request.Payload, defaults, out IReadOnlyList<string> projectFilters, out BatchError? error))
        {
            return request.Failed(error!);
        }

        if (!TryGetEffectiveExcludeGenerated(request.Payload, defaults, out bool excludeGenerated, out error))
        {
            return request.Failed(error!);
        }

        if (!TryGetDiagnosticsFilters(
            request.Payload,
            out IReadOnlyList<string> severities,
            out IReadOnlyList<string> ids,
            out int? limit,
            out error))
        {
            return request.Failed(error!);
        }

        ProjectFilterResolutionResult projectResult =
            new ProjectFilterResolver().ResolveMany(loadedWorkspace.Solution, projectFilters);
        if (projectResult.Error is not null)
        {
            return request.Failed(projectResult.Error);
        }

        WorkspaceDiagnosticsResolution resolution =
            await new WorkspaceDiagnosticsResolver().ResolveAsync(projectResult.Projects, excludeGenerated, cancellationToken);

        IReadOnlyList<WorkspaceDiagnosticResult> filteredDiagnostics = [.. resolution.Diagnostics
            .Where(diagnostic =>
                (severities.Count == 0 || severities.Contains(diagnostic.Severity)) &&
                (ids.Count == 0 || ids.Contains(diagnostic.Id)))];
        IReadOnlyList<WorkspaceDiagnosticResult> limitedDiagnostics = limit is null
            ? filteredDiagnostics
            : [.. filteredDiagnostics.Take(limit.Value)];

        return request.Success(new DiagnosticsResult(
            Workspace: loadedWorkspace.DisplayPath,
            Kind: loadedWorkspace.Kind,
            Projects: projectResult.AppliedFilters.Count == 0
                ? null
                : projectResult.AppliedFilters.Select(ProjectFilterOutput.FromAppliedFilter).ToArray(),
            Severities: severities.Count == 0 ? null : severities,
            Ids: ids.Count == 0 ? null : ids,
            ExcludeGenerated: excludeGenerated,
            Limit: limit,
            TotalDiagnostics: filteredDiagnostics.Count,
            Diagnostics: limitedDiagnostics.Select(diagnostic => new DiagnosticResult(
                Project: new DiagnosticProjectResult(
                    Name: diagnostic.Project.Name,
                    Path: diagnostic.Project.Path,
                    TargetFramework: diagnostic.Project.TargetFramework),
                Severity: diagnostic.Severity,
                Id: diagnostic.Id,
                Message: diagnostic.Message,
                Path: diagnostic.Path,
                Line: diagnostic.Line,
                Column: diagnostic.Column,
                EndLine: diagnostic.EndLine,
                EndColumn: diagnostic.EndColumn)).ToArray()));
    }

    private static async Task<BatchRequestResult> ExecuteSymbolsAsync(
        LoadedWorkspace loadedWorkspace,
        BatchDefaults defaults,
        BatchRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetRequiredString(request.Payload, "query", out string query, out BatchError? error))
        {
            return request.Failed(error!);
        }

        if (!TryGetOptionalString(request.Payload, "match", out string? matchValue, out error))
        {
            return request.Failed(error!);
        }

        string match = matchValue ?? "contains";
        if (match is not ("contains" or "exact" or "regex"))
        {
            return request.Failed(
                DiagnosticIds.ParseError,
                "Symbol name match mode must be contains, exact, or regex.");
        }

        if (!TryGetOptionalInt(request.Payload, "limit", out int? limit, out error))
        {
            return request.Failed(error!);
        }

        if (limit <= 0)
        {
            return request.Failed(DiagnosticIds.InvalidLimit, "--limit must be 1 or greater.");
        }

        if (!TryGetStringArray(request.Payload, "kinds", out IReadOnlyList<string> kinds, out error, allowStringValue: true))
        {
            return request.Failed(error!);
        }

        if (!TryGetStringArray(request.Payload, "namespaces", out IReadOnlyList<string> namespaceFilters, out error, allowStringValue: true) ||
            !TryGetOptionalString(request.Payload, "namespaceMatch", out string? namespaceMatchValue, out error) ||
            !TryGetStringArray(request.Payload, "containers", out IReadOnlyList<string> containerFilters, out error, allowStringValue: true) ||
            !TryGetOptionalString(request.Payload, "containerMatch", out string? containerMatchValue, out error) ||
            !TryGetStringArray(request.Payload, "accessibilities", out IReadOnlyList<string> accessibilityFilters, out error, allowStringValue: true))
        {
            return request.Failed(error!);
        }

        string namespaceMatch = namespaceMatchValue ?? "contains";
        string containerMatch = containerMatchValue ?? "contains";
        if (namespaceMatch is not ("contains" or "exact" or "regex"))
        {
            return request.Failed(DiagnosticIds.ParseError, "Namespace match mode must be contains, exact, or regex.");
        }

        if (containerMatch is not ("contains" or "exact" or "regex"))
        {
            return request.Failed(DiagnosticIds.ParseError, "Container match mode must be contains, exact, or regex.");
        }

        if (!TryGetProjectFilters(request.Payload, defaults, out IReadOnlyList<string> projectFilters, out error))
        {
            return request.Failed(error!);
        }

        string? kindError = GetKindError(kinds);
        if (kindError is not null)
        {
            return request.Failed(DiagnosticIds.InvalidSymbolKind, kindError);
        }

        string? accessibilityError = GetAccessibilityError(accessibilityFilters);
        if (accessibilityError is not null)
        {
            return request.Failed(DiagnosticIds.InvalidSymbolKind, accessibilityError);
        }

        ProjectFilterResolutionResult projectResult =
            new ProjectFilterResolver().ResolveMany(loadedWorkspace.Solution, projectFilters);
        if (projectResult.Error is not null)
        {
            return request.Failed(projectResult.Error);
        }

        IReadOnlyList<string> normalizedKinds = NormalizeKinds(kinds);
        IReadOnlyList<string> normalizedNamespaces = NormalizeStrings(namespaceFilters);
        IReadOnlyList<string> normalizedContainers = NormalizeStrings(containerFilters);
        IReadOnlyList<string> normalizedAccessibilities = NormalizeStrings(accessibilityFilters);
        if (!TryGetOptionalBool(request.Payload, "caseSensitive", out bool? caseSensitiveValue, out error) ||
            !TryGetEffectiveExcludeGenerated(request.Payload, defaults, out bool excludeGenerated, out error))
        {
            return request.Failed(error!);
        }

        bool caseSensitive = caseSensitiveValue ?? false;
        SymbolSearchOptions options = new(
            Query: query,
            MatchMode: ParseMatchMode(match),
            CaseSensitive: caseSensitive);

        if (!SymbolNameMatcher.TryCreate(options, out SymbolNameMatcher matcher, out string? errorMessage))
        {
            return request.Failed(DiagnosticIds.InvalidRegex, errorMessage!);
        }

        if (!TryCreateMatchers(normalizedNamespaces, namespaceMatch, caseSensitive, out IReadOnlyList<SymbolNameMatcher> namespaceMatchers, out errorMessage) ||
            !TryCreateMatchers(normalizedContainers, containerMatch, caseSensitive, out IReadOnlyList<SymbolNameMatcher> containerMatchers, out errorMessage))
        {
            return request.Failed(DiagnosticIds.InvalidRegex, errorMessage!);
        }

        IReadOnlyList<SymbolDeclaration> declarations =
            await new SymbolDeclarationFinder().FindAsync(projectResult.Projects, matcher, excludeGenerated, cancellationToken);

        IReadOnlyList<SymbolDeclaration> filteredDeclarations = FilterDeclarations(
            declarations,
            normalizedKinds,
            namespaceMatchers,
            containerMatchers,
            normalizedAccessibilities);
        IReadOnlyList<SymbolDeclaration> limitedDeclarations = limit is null
            ? filteredDeclarations
            : [.. filteredDeclarations.Take(limit.Value)];

        return request.Success(new SymbolsResult(
            Query: query,
            Match: match,
            CaseSensitive: caseSensitive,
            Kinds: normalizedKinds,
            Namespaces: normalizedNamespaces.Count == 0 ? null : normalizedNamespaces,
            NamespaceMatch: normalizedNamespaces.Count == 0 ? null : namespaceMatch,
            Containers: normalizedContainers.Count == 0 ? null : normalizedContainers,
            ContainerMatch: normalizedContainers.Count == 0 ? null : containerMatch,
            Accessibilities: normalizedAccessibilities.Count == 0 ? null : normalizedAccessibilities,
            Projects: projectResult.AppliedFilters.Count == 0
                ? null
                : projectResult.AppliedFilters.Select(ProjectFilterOutput.FromAppliedFilter).ToArray(),
            ExcludeGenerated: excludeGenerated,
            Limit: limit,
            TotalMatches: filteredDeclarations.Count,
            Matches: limitedDeclarations.Select(declaration => new SymbolMatchResult(
                Name: declaration.Name,
                Kind: declaration.Kind,
                Container: declaration.Container,
                Facts: declaration.Facts,
                Path: declaration.Path,
                Line: declaration.Line,
                Column: declaration.Column,
                EndLine: declaration.EndLine,
                EndColumn: declaration.EndColumn)).ToArray()));
    }

    private static async Task<BatchRequestResult> ExecuteSymbolsInAsync(
        LoadedWorkspace loadedWorkspace,
        BatchDefaults defaults,
        BatchRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetRequiredFile(request.Payload, out FileInfo file, out BatchError? error) ||
            !TryGetRequiredInt(request.Payload, "line", out int line, out error))
        {
            return request.Failed(error!);
        }

        if (!TryResolveSingleProject(loadedWorkspace, request.Payload, defaults, out Project? project, out ProjectFilterOutput? projectFilter, out error))
        {
            return request.Failed(error!);
        }

        if (!TryGetEffectiveExcludeGenerated(request.Payload, defaults, out bool excludeGenerated, out error) ||
            !TryGetOptionalInt(request.Payload, "startColumn", out int? startColumn, out error) ||
            !TryGetOptionalInt(request.Payload, "endColumn", out int? endColumn, out error))
        {
            return request.Failed(error!);
        }

        SymbolsInResolutionResult result = await new SymbolsInResolver().ResolveAsync(
            loadedWorkspace.Solution,
            file,
            line,
            startColumn,
            endColumn,
            project,
            excludeGenerated,
            cancellationToken);

        if (result.Error is not null)
        {
            return request.Failed(result.Error);
        }

        SymbolsInResolution resolution = result.Resolution!;
        return request.Success(new SymbolsInResult(
            File: resolution.File,
            Line: resolution.Line,
            StartColumn: resolution.StartColumn,
            EndColumn: resolution.EndColumn,
            Project: projectFilter,
            ExcludeGenerated: excludeGenerated,
            Symbols: resolution.Symbols.Select(symbol => new SymbolsInSymbolResult(
                Name: symbol.Name,
                Kind: symbol.Kind,
                Container: symbol.Container,
                Facts: symbol.Facts,
                Line: symbol.Line,
                Column: symbol.Column,
                EndLine: symbol.EndLine,
                EndColumn: symbol.EndColumn)).ToArray()));
    }

    private static async Task<BatchRequestResult> ExecuteOutlineAsync(
        LoadedWorkspace loadedWorkspace,
        BatchDefaults defaults,
        BatchRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetRequiredFile(request.Payload, out FileInfo file, out BatchError? error))
        {
            return request.Failed(error!);
        }

        if (!TryResolveSingleProject(loadedWorkspace, request.Payload, defaults, out Project? project, out ProjectFilterOutput? projectFilter, out error))
        {
            return request.Failed(error!);
        }

        if (!TryGetEffectiveExcludeGenerated(request.Payload, defaults, out bool excludeGenerated, out error))
        {
            return request.Failed(error!);
        }

        OutlineResolutionResult result = await new OutlineResolver().ResolveAsync(
            loadedWorkspace.Solution,
            file,
            project,
            excludeGenerated,
            cancellationToken);

        if (result.Error is not null)
        {
            return request.Failed(result.Error);
        }

        OutlineResolution resolution = result.Resolution!;
        return request.Success(new OutlineResult(
            File: resolution.File,
            Project: projectFilter,
            ExcludeGenerated: excludeGenerated,
            Entries: resolution.Entries.Select(entry => new OutlineEntryResult(
                Name: entry.Name,
                Kind: entry.Kind,
                Container: entry.Container,
                Facts: entry.Facts,
                Path: entry.Path,
                Line: entry.Line,
                Column: entry.Column,
                EndLine: entry.EndLine,
                EndColumn: entry.EndColumn)).ToArray()));
    }

    private static async Task<BatchRequestResult> ExecuteSymbolAtAsync(
        LoadedWorkspace loadedWorkspace,
        BatchDefaults defaults,
        BatchRequest request,
        CancellationToken cancellationToken)
    {
        SourcePositionBatchResolution positionResult = await ResolveSourcePositionAsync(
            loadedWorkspace,
            defaults,
            request,
            cancellationToken);
        if (positionResult.Error is not null)
        {
            return request.Failed(positionResult.Error);
        }

        SourcePositionBatchOptions options = positionResult.Options!;

        SymbolAtResolutionResult result = await new SymbolAtResolver().ResolveAsync(
            loadedWorkspace.Solution,
            options.File,
            options.Line,
            options.Column,
            options.Project,
            options.ExcludeGenerated,
            cancellationToken);

        if (result.Error is not null)
        {
            return request.Failed(result.Error);
        }

        SymbolAtResolution resolution = result.Resolution!;
        return request.Success(new SymbolAtResult(
            File: resolution.File,
            Line: resolution.Line,
            Column: resolution.Column,
            Project: options.ProjectFilter,
            SelectionInput: options.SelectionInput,
            ExcludeGenerated: options.ExcludeGenerated,
            Symbol: new SymbolAtSymbolResult(
                Name: resolution.Symbol.Name,
                Kind: resolution.Symbol.Kind,
                Container: resolution.Symbol.Container,
                Facts: resolution.Symbol.Facts,
                Path: resolution.Symbol.Path,
                Line: resolution.Symbol.Line,
                Column: resolution.Symbol.Column,
                EndLine: resolution.Symbol.EndLine,
                EndColumn: resolution.Symbol.EndColumn)));
    }

    private static async Task<BatchRequestResult> ExecuteSymbolInfoAsync(
        LoadedWorkspace loadedWorkspace,
        BatchDefaults defaults,
        BatchRequest request,
        CancellationToken cancellationToken)
    {
        SourcePositionBatchResolution positionResult = await ResolveSourcePositionAsync(
            loadedWorkspace,
            defaults,
            request,
            cancellationToken);
        if (positionResult.Error is not null)
        {
            return request.Failed(positionResult.Error);
        }

        SourcePositionBatchOptions options = positionResult.Options!;

        SymbolInfoResolutionResult result = await new SymbolInfoResolver().ResolveAsync(
            loadedWorkspace.Solution,
            options.File,
            options.Line,
            options.Column,
            options.Project,
            options.ExcludeGenerated,
            cancellationToken);

        if (result.Error is not null)
        {
            return request.Failed(result.Error);
        }

        SymbolInfoResolution resolution = result.Resolution!;
        return request.Success(new SymbolInfoResult(
            File: resolution.File,
            Line: resolution.Line,
            Column: resolution.Column,
            Project: options.ProjectFilter,
            SelectionInput: options.SelectionInput,
            ExcludeGenerated: options.ExcludeGenerated,
            Symbol: resolution.Symbol,
            Expression: resolution.Expression,
            ContainingSymbol: resolution.ContainingSymbol,
            Invocation: resolution.Invocation,
            Attribute: resolution.Attribute,
            Return: resolution.Return,
            Lambda: resolution.Lambda));
    }

    private static async Task<BatchRequestResult> ExecuteDefinitionAsync(
        LoadedWorkspace loadedWorkspace,
        BatchDefaults defaults,
        BatchRequest request,
        CancellationToken cancellationToken)
    {
        SourcePositionBatchResolution positionResult = await ResolveSourcePositionAsync(
            loadedWorkspace,
            defaults,
            request,
            cancellationToken);
        if (positionResult.Error is not null)
        {
            return request.Failed(positionResult.Error);
        }

        SourcePositionBatchOptions options = positionResult.Options!;
        BatchError? error;

        if (!TryGetOptionalBool(request.Payload, "includeMetadata", out bool? includeMetadata, out error))
        {
            return request.Failed(error!);
        }

        DefinitionResolutionResult result = await new DefinitionResolver().ResolveAsync(
            loadedWorkspace.Solution,
            options.File,
            options.Line,
            options.Column,
            options.Project,
            options.ExcludeGenerated,
            includeMetadata ?? false,
            cancellationToken);

        if (result.Error is not null)
        {
            return request.Failed(result.Error);
        }

        DefinitionResolution resolution = result.Resolution!;
        return request.Success(new DefinitionResult(
            File: resolution.File,
            Line: resolution.Line,
            Column: resolution.Column,
            Project: options.ProjectFilter,
            SelectionInput: options.SelectionInput,
            ExcludeGenerated: options.ExcludeGenerated,
            IncludeMetadata: includeMetadata ?? false,
            Symbol: new SourceSymbolResult(
                Name: resolution.Symbol.Name,
                Kind: resolution.Symbol.Kind,
                Container: resolution.Symbol.Container,
                Facts: resolution.Symbol.Facts),
            Definitions: resolution.Definitions.Select(definition => new SourceLocationResult(
                Path: definition.Path,
                Line: definition.Line,
                Column: definition.Column,
                EndLine: definition.EndLine,
                EndColumn: definition.EndColumn)).ToArray()));
    }

    private static async Task<BatchRequestResult> ExecuteReferencesAsync(
        LoadedWorkspace loadedWorkspace,
        BatchDefaults defaults,
        BatchRequest request,
        CancellationToken cancellationToken)
    {
        SourcePositionBatchResolution positionResult = await ResolveSourcePositionAsync(
            loadedWorkspace,
            defaults,
            request,
            cancellationToken);
        if (positionResult.Error is not null)
        {
            return request.Failed(positionResult.Error);
        }

        SourcePositionBatchOptions options = positionResult.Options!;
        BatchError? error;

        if (!TryGetNavigationResultFilter(loadedWorkspace, request.Payload, out NavigationResultBatchOptions resultFilter, out error))
        {
            return request.Failed(error!);
        }

        if (!TryGetReferenceUsageOptions(
            request.Payload,
            out IReadOnlyList<string> usageKinds,
            out IReadOnlyList<string> groupBy,
            out error))
        {
            return request.Failed(error!);
        }

        ReferencesResolutionResult result = await new ReferencesResolver().ResolveAsync(
            loadedWorkspace.Solution,
            options.File,
            options.Line,
            options.Column,
            options.Project,
            options.ExcludeGenerated,
            cancellationToken);

        if (result.Error is not null)
        {
            return request.Failed(result.Error);
        }

        ReferencesResolution resolution = result.Resolution!;
        IReadOnlyList<SymbolReferenceLocation> filteredReferences = [.. resolution.References
            .Where(reference => NavigationResultOptions.MatchesSymbol(resultFilter.Filter, reference.Path, resolution.Symbol.Kind))
            .Where(reference => usageKinds.Count == 0 || usageKinds.Contains(reference.UsageKind, StringComparer.Ordinal))];
        IReadOnlyList<SymbolReferenceLocation> limitedReferences =
            NavigationResultOptions.ApplyLimit(filteredReferences, resultFilter.Filter.Limit);
        IReadOnlyList<ReferenceUsageGroup> groups = ReferenceUsageTaxonomy.CreateGroups(filteredReferences, groupBy);

        return request.Success(new ReferencesResult(
            File: resolution.File,
            Line: resolution.Line,
            Column: resolution.Column,
            Project: options.ProjectFilter,
            SelectionInput: options.SelectionInput,
            ResultProjects: resultFilter.ProjectFilters,
            ResultPaths: resultFilter.Filter.PathFilters.Count == 0 ? null : resultFilter.Filter.PathFilters,
            ResultKinds: resultFilter.Filter.KindFilters.Count == 0 ? null : resultFilter.Filter.KindFilters,
            UsageKinds: usageKinds.Count == 0 ? null : usageKinds,
            GroupBy: groupBy.Count == 0 ? null : groupBy,
            ExcludeGenerated: options.ExcludeGenerated,
            Limit: resultFilter.Filter.Limit,
            TotalMatches: filteredReferences.Count,
            UsageKindCounts: ReferenceUsageTaxonomy.CreateCounts(filteredReferences),
            Symbol: new SourceSymbolResult(
                Name: resolution.Symbol.Name,
                Kind: resolution.Symbol.Kind,
                Container: resolution.Symbol.Container,
                Facts: resolution.Symbol.Facts),
            References: limitedReferences.Select(reference => new ReferenceLocationResult(
                Path: reference.Path,
                Line: reference.Line,
                Column: reference.Column,
                EndLine: reference.EndLine,
                EndColumn: reference.EndColumn,
                UsageKind: reference.UsageKind,
                ContainingSymbol: reference.ContainingSymbol is null
                    ? null
                    : new SourceSymbolLocationResult(
                        Name: reference.ContainingSymbol.Name,
                        Kind: reference.ContainingSymbol.Kind,
                        Container: reference.ContainingSymbol.Container,
                        Facts: reference.ContainingSymbol.Facts,
                        Path: reference.ContainingSymbol.Path,
                        Line: reference.ContainingSymbol.Line,
                        Column: reference.ContainingSymbol.Column,
                        EndLine: reference.ContainingSymbol.EndLine,
                        EndColumn: reference.ContainingSymbol.EndColumn))).ToArray(),
            Groups: groups.Count == 0 ? null : groups));
    }

    private static async Task<BatchRequestResult> ExecuteImplementationsAsync(
        LoadedWorkspace loadedWorkspace,
        BatchDefaults defaults,
        BatchRequest request,
        CancellationToken cancellationToken)
    {
        SourcePositionBatchResolution positionResult = await ResolveSourcePositionAsync(
            loadedWorkspace,
            defaults,
            request,
            cancellationToken);
        if (positionResult.Error is not null)
        {
            return request.Failed(positionResult.Error);
        }

        SourcePositionBatchOptions options = positionResult.Options!;
        BatchError? error;

        if (!TryGetNavigationResultFilter(loadedWorkspace, request.Payload, out NavigationResultBatchOptions resultFilter, out error))
        {
            return request.Failed(error!);
        }

        ImplementationsResolutionResult result = await new ImplementationsResolver().ResolveAsync(
            loadedWorkspace.Solution,
            options.File,
            options.Line,
            options.Column,
            options.Project,
            options.ExcludeGenerated,
            cancellationToken);

        if (result.Error is not null)
        {
            return request.Failed(result.Error);
        }

        ImplementationsResolution resolution = result.Resolution!;
        IReadOnlyList<ImplementationLocation> filteredImplementations = [.. resolution.Implementations
            .Where(implementation => NavigationResultOptions.MatchesSymbol(resultFilter.Filter, implementation.Path, implementation.Kind))];
        IReadOnlyList<ImplementationLocation> limitedImplementations =
            NavigationResultOptions.ApplyLimit(filteredImplementations, resultFilter.Filter.Limit);

        return request.Success(new ImplementationsResult(
            File: resolution.File,
            Line: resolution.Line,
            Column: resolution.Column,
            Project: options.ProjectFilter,
            SelectionInput: options.SelectionInput,
            ResultProjects: resultFilter.ProjectFilters,
            ResultPaths: resultFilter.Filter.PathFilters.Count == 0 ? null : resultFilter.Filter.PathFilters,
            ResultKinds: resultFilter.Filter.KindFilters.Count == 0 ? null : resultFilter.Filter.KindFilters,
            ExcludeGenerated: options.ExcludeGenerated,
            Limit: resultFilter.Filter.Limit,
            TotalMatches: filteredImplementations.Count,
            Symbol: new SourceSymbolResult(
                Name: resolution.Symbol.Name,
                Kind: resolution.Symbol.Kind,
                Container: resolution.Symbol.Container,
                Facts: resolution.Symbol.Facts),
            Implementations: limitedImplementations.Select(implementation => new ImplementationLocationResult(
                Name: implementation.Name,
                Kind: implementation.Kind,
                Container: implementation.Container,
                Facts: implementation.Facts,
                Path: implementation.Path,
                Line: implementation.Line,
                Column: implementation.Column,
                EndLine: implementation.EndLine,
                EndColumn: implementation.EndColumn)).ToArray()));
    }

    private static async Task<BatchRequestResult> ExecuteCallersAsync(
        LoadedWorkspace loadedWorkspace,
        BatchDefaults defaults,
        BatchRequest request,
        CancellationToken cancellationToken)
    {
        SourcePositionBatchResolution positionResult = await ResolveSourcePositionAsync(
            loadedWorkspace,
            defaults,
            request,
            cancellationToken);
        if (positionResult.Error is not null)
        {
            return request.Failed(positionResult.Error);
        }

        SourcePositionBatchOptions options = positionResult.Options!;
        BatchError? error;

        if (!TryGetNavigationResultFilter(loadedWorkspace, request.Payload, out NavigationResultBatchOptions resultFilter, out error))
        {
            return request.Failed(error!);
        }

        CallersResolutionResult result = await new CallHierarchyResolver().ResolveCallersAsync(
            loadedWorkspace.Solution,
            options.File,
            options.Line,
            options.Column,
            options.Project,
            options.ExcludeGenerated,
            cancellationToken);

        if (result.Error is not null)
        {
            return request.Failed(result.Error);
        }

        CallersResolution resolution = result.Resolution!;
        IReadOnlyList<CallHierarchyGroup> filteredCallers = [.. resolution.Callers
            .Where(group => group.Symbol.Path is not null &&
                NavigationResultOptions.MatchesSymbol(resultFilter.Filter, group.Symbol.Path, group.Symbol.Kind))];
        IReadOnlyList<CallHierarchyGroup> limitedCallers =
            NavigationResultOptions.ApplyLimit(filteredCallers, resultFilter.Filter.Limit);

        return request.Success(new CallersResult(
            File: resolution.File,
            Line: resolution.Line,
            Column: resolution.Column,
            Project: options.ProjectFilter,
            SelectionInput: options.SelectionInput,
            ResultProjects: resultFilter.ProjectFilters,
            ResultPaths: resultFilter.Filter.PathFilters.Count == 0 ? null : resultFilter.Filter.PathFilters,
            ResultKinds: resultFilter.Filter.KindFilters.Count == 0 ? null : resultFilter.Filter.KindFilters,
            ExcludeGenerated: options.ExcludeGenerated,
            Limit: resultFilter.Filter.Limit,
            TotalGroups: filteredCallers.Count,
            Symbol: CallHierarchySymbolResult.FromSymbol(resolution.Symbol),
            Callers: limitedCallers.Select(CallHierarchyGroupResult.FromGroup).ToArray()));
    }

    private static async Task<BatchRequestResult> ExecuteTypeHierarchyAsync(
        LoadedWorkspace loadedWorkspace,
        BatchDefaults defaults,
        BatchRequest request,
        CancellationToken cancellationToken)
    {
        SourcePositionBatchResolution positionResult = await ResolveSourcePositionAsync(
            loadedWorkspace,
            defaults,
            request,
            cancellationToken);
        if (positionResult.Error is not null)
        {
            return request.Failed(positionResult.Error);
        }

        SourcePositionBatchOptions options = positionResult.Options!;

        TypeHierarchyResolutionResult result = await new TypeHierarchyResolver().ResolveAsync(
            loadedWorkspace.Solution,
            options.File,
            options.Line,
            options.Column,
            options.Project,
            options.ExcludeGenerated,
            cancellationToken);

        if (result.Error is not null)
        {
            return request.Failed(result.Error);
        }

        TypeHierarchyResolution resolution = result.Resolution!;
        return request.Success(new TypeHierarchyResult(
            File: resolution.File,
            Line: resolution.Line,
            Column: resolution.Column,
            Project: options.ProjectFilter,
            SelectionInput: options.SelectionInput,
            ExcludeGenerated: options.ExcludeGenerated,
            Symbol: resolution.Symbol,
            BaseTypes: resolution.BaseTypes,
            Interfaces: resolution.Interfaces,
            DerivedTypes: resolution.DerivedTypes,
            ImplementingTypes: resolution.ImplementingTypes,
            BaseMembers: resolution.BaseMembers,
            OverridingMembers: resolution.OverridingMembers,
            ImplementedMembers: resolution.ImplementedMembers));
    }

    private static async Task<BatchRequestResult> ExecuteCallsAsync(
        LoadedWorkspace loadedWorkspace,
        BatchDefaults defaults,
        BatchRequest request,
        CancellationToken cancellationToken)
    {
        SourcePositionBatchResolution positionResult = await ResolveSourcePositionAsync(
            loadedWorkspace,
            defaults,
            request,
            cancellationToken);
        if (positionResult.Error is not null)
        {
            return request.Failed(positionResult.Error);
        }

        SourcePositionBatchOptions options = positionResult.Options!;
        BatchError? error;

        if (!TryGetNavigationResultFilter(loadedWorkspace, request.Payload, out NavigationResultBatchOptions resultFilter, out error))
        {
            return request.Failed(error!);
        }

        if (!TryGetOptionalBool(request.Payload, "includeMetadata", out bool? includeMetadata, out error))
        {
            return request.Failed(error!);
        }

        CallsResolutionResult result = await new CallHierarchyResolver().ResolveCallsAsync(
            loadedWorkspace.Solution,
            options.File,
            options.Line,
            options.Column,
            options.Project,
            options.ExcludeGenerated,
            includeMetadata ?? false,
            cancellationToken);

        if (result.Error is not null)
        {
            return request.Failed(result.Error);
        }

        CallsResolution resolution = result.Resolution!;
        IReadOnlyList<CallHierarchyGroup> filteredCalls = [.. resolution.Calls
            .Where(group => NavigationResultOptions.MatchesSymbolOrMetadata(
                resultFilter.Filter,
                group.Symbol.Path,
                group.Symbol.Kind))];
        IReadOnlyList<CallHierarchyGroup> limitedCalls =
            NavigationResultOptions.ApplyLimit(filteredCalls, resultFilter.Filter.Limit);

        return request.Success(new CallsResult(
            File: resolution.File,
            Line: resolution.Line,
            Column: resolution.Column,
            Project: options.ProjectFilter,
            SelectionInput: options.SelectionInput,
            ResultProjects: resultFilter.ProjectFilters,
            ResultPaths: resultFilter.Filter.PathFilters.Count == 0 ? null : resultFilter.Filter.PathFilters,
            ResultKinds: resultFilter.Filter.KindFilters.Count == 0 ? null : resultFilter.Filter.KindFilters,
            ExcludeGenerated: options.ExcludeGenerated,
            IncludeMetadata: includeMetadata ?? false,
            Limit: resultFilter.Filter.Limit,
            TotalGroups: filteredCalls.Count,
            Caller: CallHierarchySymbolResult.FromSymbol(resolution.Caller),
            Calls: limitedCalls.Select(CallHierarchyGroupResult.FromGroup).ToArray()));
    }

    private static async Task<BatchRequestResult> ExecuteFuzzyFindAsync(
        LoadedWorkspace loadedWorkspace,
        BatchDefaults defaults,
        BatchRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetFuzzyQuery(
            loadedWorkspace,
            defaults,
            request,
            readCandidateLimit: true,
            out FuzzyQueryOptions options,
            out IReadOnlyList<Project> projects,
            out IReadOnlyList<FuzzyProjectFilter>? projectFilters,
            out BatchError? error))
        {
            return request.Failed(error!);
        }

        FuzzyDiscoveryResolver resolver = new();
        BatchRequestResult? failedResult = await ValidateFuzzySelectionAsync(request, resolver, projects, options, cancellationToken);
        if (failedResult is not null)
        {
            return failedResult;
        }

        return request.Success(await resolver.FindAsync(
            loadedWorkspace,
            options,
            projects,
            projectFilters,
            cancellationToken));
    }

    private static async Task<BatchRequestResult> ExecuteResolveTargetAsync(
        LoadedWorkspace loadedWorkspace,
        BatchDefaults defaults,
        BatchRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetSymbolSelectionInput(request.Payload, "resolve-target", out SymbolSelectionInput input, out BatchError? error) ||
            !TryGetEffectiveExcludeGenerated(request.Payload, defaults, out bool excludeGenerated, out error))
        {
            return request.Failed(error!);
        }

        ResolveTargetResolver resolver = new();
        if (input.IsSourcePosition)
        {
            if (!TryGetProjectFilters(request.Payload, defaults, out IReadOnlyList<string> projectFilters, out error))
            {
                return request.Failed(error!);
            }

            if (projectFilters.Count > 1)
            {
                return request.Failed(DiagnosticIds.ParseError, "Source-position mode accepts at most one project filter.");
            }

            ProjectFilterResolutionResult projectResult = new ProjectFilterResolver().ResolveSingle(
                loadedWorkspace.Solution,
                projectFilters.Count == 0 ? null : projectFilters[0]);
            if (projectResult.Error is not null)
            {
                return request.Failed(projectResult.Error);
            }

            Project? project = projectFilters.Count == 0 ? null : projectResult.Projects.Single();
            ResolveTargetResult result = await resolver.ResolveSourcePositionAsync(
                loadedWorkspace,
                input.File!,
                input.Line!.Value,
                input.Column!.Value,
                project,
                excludeGenerated,
                cancellationToken);

            return result.SelectedTarget is null
                ? request.Failed(DiagnosticIds.SymbolNotFoundAtPosition, result.Warnings.FirstOrDefault() ?? "No symbol was found at the source position.")
                : request.Success(result);
        }

        if (!TryGetFuzzyQuery(
            loadedWorkspace,
            defaults,
            request,
            readCandidateLimit: true,
            out FuzzyQueryOptions options,
            out IReadOnlyList<Project> projects,
            out IReadOnlyList<FuzzyProjectFilter>? projectFiltersOutput,
            out error))
        {
            return request.Failed(error!);
        }

        FuzzyDiscoveryResolver fuzzyResolver = new();
        BatchRequestResult? failedResult = await ValidateFuzzySelectionAsync(request, fuzzyResolver, projects, options, cancellationToken);
        if (failedResult is not null)
        {
            return failedResult;
        }

        return request.Success(await resolver.ResolveFuzzyAsync(
            loadedWorkspace,
            options,
            projects,
            projectFiltersOutput,
            cancellationToken));
    }

    private static async Task<BatchRequestResult> ExecuteFuzzyWhereUsedAsync(
        LoadedWorkspace loadedWorkspace,
        BatchDefaults defaults,
        BatchRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetFuzzyQuery(
            loadedWorkspace,
            defaults,
            request,
            readCandidateLimit: false,
            out FuzzyQueryOptions options,
            out IReadOnlyList<Project> projects,
            out IReadOnlyList<FuzzyProjectFilter>? projectFilters,
            out BatchError? error) ||
            !TryGetFuzzySnippetOptions(request.Payload, out bool includeSnippets, out int snippetLines, out error) ||
            !TryGetOptionalInt(request.Payload, "limit", out int? limit, out error) ||
            !TryGetReferenceUsageOptions(request.Payload, out IReadOnlyList<string> usageKinds, out IReadOnlyList<string> groupBy, out error))
        {
            return request.Failed(error!);
        }

        int referenceLimit = limit ?? FuzzyDiscoveryResolver.DefaultReferenceLimit;
        if (referenceLimit <= 0)
        {
            return request.Failed(DiagnosticIds.InvalidLimit, "limit must be 1 or greater.");
        }

        FuzzyDiscoveryResolver resolver = new();
        BatchRequestResult? failedResult = await ValidateFuzzySelectionAsync(request, resolver, projects, options, cancellationToken);
        if (failedResult is not null)
        {
            return failedResult;
        }

        return request.Success(await resolver.WhereUsedAsync(
            loadedWorkspace,
            options,
            new FuzzyLocationOptions(referenceLimit, FuzzyDiscoveryResolver.DefaultReferenceFileLimit, includeSnippets, snippetLines, options.ExcludeGenerated, usageKinds, groupBy),
            projects,
            projectFilters,
            cancellationToken));
    }

    private static async Task<BatchRequestResult> ExecuteFuzzyAboutAsync(
        LoadedWorkspace loadedWorkspace,
        BatchDefaults defaults,
        BatchRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetFuzzyQuery(
            loadedWorkspace,
            defaults,
            request,
            readCandidateLimit: false,
            out FuzzyQueryOptions options,
            out IReadOnlyList<Project> projects,
            out IReadOnlyList<FuzzyProjectFilter>? projectFilters,
            out BatchError? error) ||
            !TryGetFuzzySnippetOptions(request.Payload, out bool includeSnippets, out int snippetLines, out error) ||
            !TryGetOptionalInt(request.Payload, "memberLimit", out int? memberLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "referenceLimit", out int? referenceLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "relationLimit", out int? relationLimit, out error))
        {
            return request.Failed(error!);
        }

        int effectiveMemberLimit = memberLimit ?? FuzzyDiscoveryResolver.DefaultMemberLimit;
        int effectiveReferenceLimit = referenceLimit ?? FuzzyDiscoveryResolver.DefaultReferenceLimit;
        int effectiveRelationLimit = relationLimit ?? FuzzyDiscoveryResolver.DefaultRelationLimit;
        if (effectiveMemberLimit <= 0 || effectiveReferenceLimit <= 0 || effectiveRelationLimit <= 0)
        {
            return request.Failed(DiagnosticIds.InvalidLimit, "memberLimit, referenceLimit, and relationLimit must be 1 or greater.");
        }

        FuzzyDiscoveryResolver resolver = new();
        BatchRequestResult? failedResult = await ValidateFuzzySelectionAsync(request, resolver, projects, options, cancellationToken);
        if (failedResult is not null)
        {
            return failedResult;
        }

        return request.Success(await resolver.AboutAsync(
            loadedWorkspace,
            options,
            new FuzzyAboutOptions(effectiveMemberLimit, effectiveReferenceLimit, effectiveRelationLimit, includeSnippets, snippetLines, options.ExcludeGenerated),
            projects,
            projectFilters,
            cancellationToken));
    }

    private static async Task<BatchRequestResult> ExecuteFuzzyFilesAsync(
        LoadedWorkspace loadedWorkspace,
        BatchDefaults defaults,
        BatchRequest request,
        string intent,
        CancellationToken cancellationToken)
    {
        if (!TryGetFuzzyQuery(
            loadedWorkspace,
            defaults,
            request,
            readCandidateLimit: false,
            out FuzzyQueryOptions options,
            out IReadOnlyList<Project> projects,
            out IReadOnlyList<FuzzyProjectFilter>? projectFilters,
            out BatchError? error) ||
            !TryGetFuzzySnippetOptions(request.Payload, out bool includeSnippets, out int snippetLines, out error) ||
            !TryGetOptionalInt(request.Payload, "limit", out int? limit, out error) ||
            !TryGetOptionalInt(request.Payload, "depth", out int? depth, out error))
        {
            return request.Failed(error!);
        }

        int effectiveLimit = limit ?? FuzzyDiscoveryResolver.DefaultFileLimit;
        int effectiveDepth = depth ?? FuzzyDiscoveryResolver.DefaultDepth;
        if (effectiveLimit <= 0 || effectiveDepth < 0)
        {
            return request.Failed(DiagnosticIds.InvalidLimit, "limit must be 1 or greater and depth must be 0 or greater.");
        }

        if (!TryGetStringArray(request.Payload, "include", out IReadOnlyList<string> includeValues, out error, allowStringValue: true))
        {
            return request.Failed(error!);
        }

        IReadOnlyList<string> defaultIncludes = intent == "impact"
            ? ["references", "callers", "calls", "implementations", "hierarchy"]
            : ["declarations", "references", "callers", "calls", "implementations", "hierarchy"];
        IReadOnlyList<string> include = includeValues.Count == 0
            ? defaultIncludes
            : [.. includeValues
                .SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)];
        if (!FuzzyCommandSupport.ValidateInclude(include, out string? includeError))
        {
            return request.Failed(DiagnosticIds.ParseError, includeError!);
        }

        FuzzyDiscoveryResolver resolver = new();
        BatchRequestResult? failedResult = await ValidateFuzzySelectionAsync(request, resolver, projects, options, cancellationToken);
        if (failedResult is not null)
        {
            return failedResult;
        }

        return request.Success(await resolver.FilesAsync(
            loadedWorkspace,
            intent,
            options,
            new FuzzyFilesOptions(include, effectiveLimit, effectiveDepth, includeSnippets, snippetLines, options.ExcludeGenerated),
            projects,
            projectFilters,
            cancellationToken));
    }

    private static async Task<BatchRequestResult> ExecuteFuzzyEntrypointsAsync(
        LoadedWorkspace loadedWorkspace,
        BatchDefaults defaults,
        BatchRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetFuzzyQuery(
            loadedWorkspace,
            defaults,
            request,
            readCandidateLimit: false,
            out FuzzyQueryOptions options,
            out IReadOnlyList<Project> projects,
            out IReadOnlyList<FuzzyProjectFilter>? projectFilters,
            out BatchError? error) ||
            !TryGetFuzzySnippetOptions(request.Payload, out bool includeSnippets, out int snippetLines, out error) ||
            !TryGetOptionalInt(request.Payload, "limit", out int? limit, out error) ||
            !TryGetOptionalInt(request.Payload, "depth", out int? depth, out error))
        {
            return request.Failed(error!);
        }

        int effectiveLimit = limit ?? FuzzyDiscoveryResolver.DefaultEntrypointLimit;
        int effectiveDepth = depth ?? FuzzyDiscoveryResolver.DefaultDepth;
        if (effectiveLimit <= 0 || effectiveDepth < 0)
        {
            return request.Failed(DiagnosticIds.InvalidLimit, "limit must be 1 or greater and depth must be 0 or greater.");
        }

        FuzzyDiscoveryResolver resolver = new();
        BatchRequestResult? failedResult = await ValidateFuzzySelectionAsync(request, resolver, projects, options, cancellationToken);
        if (failedResult is not null)
        {
            return failedResult;
        }

        return request.Success(await resolver.EntrypointsAsync(
            loadedWorkspace,
            options,
            new FuzzyEntrypointsOptions(effectiveDepth, effectiveLimit, includeSnippets, snippetLines, options.ExcludeGenerated),
            projects,
            projectFilters,
            cancellationToken));
    }

    private static async Task<BatchRequestResult?> ValidateFuzzySelectionAsync(
        BatchRequest request,
        FuzzyDiscoveryResolver resolver,
        IReadOnlyList<Project> projects,
        FuzzyQueryOptions options,
        CancellationToken cancellationToken)
    {
        FuzzyCandidateResolution resolution = await resolver.ResolveCandidatesForSelectionAsync(
            projects,
            options,
            cancellationToken);

        if (resolution.Error is null)
        {
            return null;
        }

        return request.Failed(resolution.Error.DiagnosticId, resolution.Error.Message);
    }

    private static bool TryReadBatchInput(
        JsonElement root,
        out BatchInput input,
        out string? error)
    {
        input = default!;

        if (root.ValueKind != JsonValueKind.Object)
        {
            error = "Batch input must be a JSON object.";
            return false;
        }

        if (!TryReadDefaults(root, out BatchDefaults defaults, out error))
        {
            return false;
        }

        if (!root.TryGetProperty("requests", out JsonElement requestsElement) ||
            requestsElement.ValueKind != JsonValueKind.Array)
        {
            error = "Batch input must contain a requests array.";
            return false;
        }

        List<BatchRequest> requests = [];
        foreach (JsonElement requestElement in requestsElement.EnumerateArray())
        {
            if (requestElement.ValueKind != JsonValueKind.Object)
            {
                error = "Each batch request must be a JSON object.";
                return false;
            }

            if (!TryGetRequiredString(requestElement, "id", out string id, out BatchError? requestError))
            {
                error = requestError!.Message;
                return false;
            }

            if (string.IsNullOrWhiteSpace(id))
            {
                error = "Batch request id must not be empty.";
                return false;
            }

            if (!TryGetRequiredString(requestElement, "command", out string command, out requestError))
            {
                error = $"Batch request '{id}' must include a command string.";
                return false;
            }

            requests.Add(new BatchRequest(id, command, requestElement));
        }

        if (requests.Count == 0)
        {
            error = "Batch input must contain at least one request.";
            return false;
        }

        input = new BatchInput(defaults, requests);
        error = null;
        return true;
    }

    private static bool TryReadDefaults(JsonElement root, out BatchDefaults defaults, out string? error)
    {
        defaults = new BatchDefaults(Project: null, ExcludeGenerated: null);
        if (!root.TryGetProperty("defaults", out JsonElement defaultsElement))
        {
            error = null;
            return true;
        }

        if (defaultsElement.ValueKind != JsonValueKind.Object)
        {
            error = "defaults must be a JSON object.";
            return false;
        }

        if (!TryGetOptionalString(defaultsElement, "project", out string? project, out BatchError? batchError))
        {
            error = $"defaults.{batchError!.Message}";
            return false;
        }

        if (!TryGetOptionalBool(defaultsElement, "excludeGenerated", out bool? excludeGenerated, out batchError))
        {
            error = $"defaults.{batchError!.Message}";
            return false;
        }

        defaults = new BatchDefaults(Project: project, ExcludeGenerated: excludeGenerated);
        error = null;
        return true;
    }

    private static async Task<SourcePositionBatchResolution> ResolveSourcePositionAsync(
        LoadedWorkspace loadedWorkspace,
        BatchDefaults defaults,
        BatchRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetOptionalString(request.Payload, "candidateId", out string? candidateId, out BatchError? error) ||
            !TryGetOptionalFile(request.Payload, "file", out FileInfo? file, out error) ||
            !TryGetOptionalInt(request.Payload, "line", out int? line, out error) ||
            !TryGetOptionalInt(request.Payload, "column", out int? column, out error) ||
            !TryResolveSingleProject(loadedWorkspace, request.Payload, defaults, out Project? project, out ProjectFilterOutput? projectFilter, out error) ||
            !TryGetEffectiveExcludeGenerated(request.Payload, defaults, out bool excludeGenerated, out error))
        {
            return SourcePositionBatchResolution.Failed(error!);
        }

        bool hasCandidateId = !string.IsNullOrWhiteSpace(candidateId);
        bool hasAnySourcePosition = file is not null || line is not null || column is not null;
        bool hasCompleteSourcePosition = file is not null && line is not null && column is not null;
        if (hasCandidateId && hasAnySourcePosition || !hasCandidateId && !hasCompleteSourcePosition)
        {
            return SourcePositionBatchResolution.Failed(BatchError.FromDiagnostic(
                DiagnosticIds.ParseError,
                $"Specify exactly one {request.Command} input mode: candidateId or file with line and column."));
        }

        CandidateSelectionInput? selectionInput = null;
        if (hasCandidateId)
        {
            IReadOnlyList<Project> projects = project is null
                ? loadedWorkspace.Solution.Projects.ToArray()
                : [project];
            CandidateTargetResolutionResult targetResult = await new CandidateTargetResolver().ResolveAsync(
                loadedWorkspace.Solution,
                projects,
                candidateId!,
                excludeGenerated,
                cancellationToken);
            if (targetResult.Error is not null)
            {
                return SourcePositionBatchResolution.Failed(BatchError.FromDiagnostic(
                    targetResult.Error.DiagnosticId,
                    targetResult.Error.Message));
            }

            CandidateTargetResolution target = targetResult.Resolution!;
            file = target.File;
            line = target.Line;
            column = target.Column;
            project ??= target.Project;
            selectionInput = new CandidateSelectionInput("candidateId", target.CandidateId);
        }

        return SourcePositionBatchResolution.Succeeded(new SourcePositionBatchOptions(
            File: file!,
            Line: line!.Value,
            Column: column!.Value,
            Project: project,
            ProjectFilter: projectFilter,
            ExcludeGenerated: excludeGenerated,
            SelectionInput: selectionInput));
    }

    private static bool TryResolveSingleProject(
        LoadedWorkspace loadedWorkspace,
        JsonElement payload,
        BatchDefaults defaults,
        out Project? project,
        out ProjectFilterOutput? projectFilter,
        out BatchError? error)
    {
        project = null;
        projectFilter = null;
        error = null;

        if (!TryGetOptionalString(payload, "project", out string? requestProject, out error))
        {
            return false;
        }

        string? filter = requestProject ?? defaults.Project;
        ProjectFilterResolutionResult result =
            new ProjectFilterResolver().ResolveSingle(loadedWorkspace.Solution, filter);

        if (result.Error is not null)
        {
            error = BatchError.FromDiagnostic(result.Error.DiagnosticId, result.Error.Message);
            return false;
        }

        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        project = result.Projects.Single();
        projectFilter = ProjectFilterOutput.FromAppliedFilter(result.AppliedFilters.Single());
        return true;
    }

    private static bool TryGetProjectFilters(
        JsonElement payload,
        BatchDefaults defaults,
        out IReadOnlyList<string> projectFilters,
        out BatchError? error)
    {
        projectFilters = [];

        bool hasProject = payload.TryGetProperty("project", out JsonElement projectElement);
        bool hasProjects = payload.TryGetProperty("projects", out JsonElement projectsElement);
        if (hasProject && hasProjects)
        {
            error = BatchError.FromDiagnostic(
                DiagnosticIds.InvalidBatchInput,
                "Use either project or projects, not both.");
            return false;
        }

        if (hasProjects)
        {
            return TryReadStringArrayElement(projectsElement, "projects", out projectFilters, out error, allowStringValue: true);
        }

        string? project;
        if (hasProject)
        {
            if (!TryReadOptionalStringElement(projectElement, "project", out project, out error))
            {
                return false;
            }
        }
        else
        {
            project = defaults.Project;
        }

        projectFilters = string.IsNullOrWhiteSpace(project) ? [] : [project!];
        error = null;
        return true;
    }

    private static bool TryGetDiffProjectContext(
        LoadedWorkspace loadedWorkspace,
        BatchDefaults defaults,
        JsonElement payload,
        out IReadOnlyList<Project> projects,
        out IReadOnlyList<DiffProjectFilter>? projectOutputs,
        out BatchError? error)
    {
        projects = [];
        projectOutputs = null;

        if (!TryGetProjectFilters(payload, defaults, out IReadOnlyList<string> projectFilters, out error))
        {
            return false;
        }

        ProjectFilterResolutionResult result =
            new ProjectFilterResolver().ResolveMany(loadedWorkspace.Solution, projectFilters);
        if (result.Error is not null)
        {
            error = BatchError.FromDiagnostic(result.Error.DiagnosticId, result.Error.Message);
            return false;
        }

        projects = result.Projects;
        projectOutputs = result.AppliedFilters.Count == 0
            ? null
            : result.AppliedFilters.Select(filter => new DiffProjectFilter(
                Filter: filter.Filter,
                Name: filter.Name,
                Path: filter.Path,
                TargetFramework: filter.TargetFramework)).ToArray();
        return true;
    }

    private static bool TryGetDiffRequest(
        JsonElement payload,
        out DiffRequest request,
        out BatchError? error)
    {
        request = default!;
        if (!TryGetOptionalString(payload, "base", out string? baseRef, out error) ||
            !TryGetOptionalString(payload, "head", out string? headRef, out error) ||
            !TryGetOptionalBool(payload, "staged", out bool? stagedValue, out error) ||
            !TryGetOptionalBool(payload, "includeUnstaged", out bool? includeUnstagedValue, out error))
        {
            return false;
        }

        bool staged = stagedValue ?? false;
        bool includeUnstaged = includeUnstagedValue ?? true;
        if (!string.IsNullOrWhiteSpace(headRef) && string.IsNullOrWhiteSpace(baseRef))
        {
            error = BatchError.FromDiagnostic(DiagnosticIds.InvalidDiffOptions, "head requires base.");
            return false;
        }

        if (staged && (!string.IsNullOrWhiteSpace(baseRef) || !string.IsNullOrWhiteSpace(headRef)))
        {
            error = BatchError.FromDiagnostic(DiagnosticIds.InvalidDiffOptions, "staged cannot be combined with base or head.");
            return false;
        }

        string? normalizedBase = string.IsNullOrWhiteSpace(baseRef) ? null : baseRef.Trim();
        string? normalizedHead = string.IsNullOrWhiteSpace(headRef) ? null : headRef.Trim();
        string mode = normalizedBase is not null && normalizedHead is not null
            ? "range"
            : normalizedBase is not null
                ? "baseToWorkingTree"
                : staged
                    ? "staged"
                    : "workingTree";

        request = new DiffRequest(
            mode,
            normalizedBase,
            normalizedHead,
            staged,
            !staged && normalizedBase is null && normalizedHead is null && includeUnstaged);
        error = null;
        return true;
    }

    private static BatchError? GetPositiveBatchLimitError(string propertyName, int value)
    {
        return value > 0
            ? null
            : BatchError.FromDiagnostic(DiagnosticIds.InvalidLimit, $"{propertyName} must be 1 or greater.");
    }

    private static BatchError? GetNonNegativeBatchLimitError(string propertyName, int value)
    {
        return value >= 0
            ? null
            : BatchError.FromDiagnostic(DiagnosticIds.InvalidLimit, $"{propertyName} must be 0 or greater.");
    }

    private static bool TryGetFuzzyQuery(
        LoadedWorkspace loadedWorkspace,
        BatchDefaults defaults,
        BatchRequest request,
        bool readCandidateLimit,
        out FuzzyQueryOptions options,
        out IReadOnlyList<Project> projects,
        out IReadOnlyList<FuzzyProjectFilter>? projectFilters,
        out BatchError? error)
    {
        options = default!;
        projects = [];
        projectFilters = null;

        if (!TryGetOptionalString(request.Payload, "query", out string? queryValue, out error) ||
            !TryGetOptionalString(request.Payload, "candidateId", out string? candidateIdValue, out error) ||
            !TryGetOptionalString(request.Payload, "match", out string? matchValue, out error) ||
            !TryGetOptionalString(request.Payload, "candidatePolicy", out string? candidatePolicyValue, out error) ||
            !TryGetOptionalString(request.Payload, "minConfidence", out string? minConfidenceValue, out error) ||
            !TryGetOptionalBool(request.Payload, "explainSelection", out bool? explainSelectionValue, out error) ||
            !TryGetOptionalBool(request.Payload, "caseSensitive", out bool? caseSensitive, out error) ||
            !TryGetStringArray(request.Payload, "assumeKinds", out IReadOnlyList<string> assumeKinds, out error, allowStringValue: true))
        {
            return false;
        }

        if (assumeKinds.Count == 0 &&
            !TryGetStringArray(request.Payload, "assumeKind", out assumeKinds, out error, allowStringValue: true))
        {
            return false;
        }

        string match = matchValue ?? "smart";
        if (match is not ("smart" or "exact" or "contains" or "regex"))
        {
            error = BatchError.FromDiagnostic(DiagnosticIds.ParseError, "Fuzzy match mode must be smart, exact, contains, or regex.");
            return false;
        }

        bool hasQuery = !string.IsNullOrWhiteSpace(queryValue);
        bool hasCandidateId = !string.IsNullOrWhiteSpace(candidateIdValue);
        if (request.Command == "find" && hasCandidateId)
        {
            error = BatchError.FromDiagnostic(DiagnosticIds.ParseError, "find does not accept candidateId.");
            return false;
        }

        if (hasQuery == hasCandidateId)
        {
            error = BatchError.FromDiagnostic(DiagnosticIds.ParseError, "Specify exactly one fuzzy selection input: query or candidateId.");
            return false;
        }

        bool allowGroupPolicy = request.Command is "find" or "where-used";
        string defaultPolicy = request.Command == "resolve-target"
            ? "select"
            : allowGroupPolicy ? "group" : "fail";
        string candidatePolicy = candidatePolicyValue ?? defaultPolicy;
        if (candidatePolicy is not ("fail" or "select" or "group"))
        {
            error = BatchError.FromDiagnostic(DiagnosticIds.InvalidCandidatePolicy, "candidatePolicy must be fail, select, or group.");
            return false;
        }

        if (!allowGroupPolicy && candidatePolicy == "group")
        {
            error = BatchError.FromDiagnostic(DiagnosticIds.InvalidCandidatePolicy, "candidatePolicy group is not supported by this command.");
            return false;
        }

        string minConfidence = minConfidenceValue ?? (request.Command == "find" ? "low" : "medium");
        if (minConfidence is not ("high" or "medium" or "low"))
        {
            error = BatchError.FromDiagnostic(DiagnosticIds.ParseError, "minConfidence must be high, medium, or low.");
            return false;
        }

        string query = hasCandidateId ? candidateIdValue!.Trim() : queryValue!.Trim();
        string? candidateId = hasCandidateId ? query : null;
        if (hasCandidateId)
        {
            if (assumeKinds.Count > 0 || match != "smart" || caseSensitive == true)
            {
                error = BatchError.FromDiagnostic(DiagnosticIds.ParseError, "candidateId cannot be combined with assumeKind, match, or caseSensitive.");
                return false;
            }

            if (!FuzzyCandidateIdentity.TryParseCandidateId(candidateId!))
            {
                error = BatchError.FromDiagnostic(DiagnosticIds.InvalidCandidateId, $"Invalid candidate id: {candidateId}.");
                return false;
            }
        }

        if (!TryGetProjectFilters(request.Payload, defaults, out IReadOnlyList<string> projectFilterValues, out error) ||
            !TryGetEffectiveExcludeGenerated(request.Payload, defaults, out bool excludeGenerated, out error))
        {
            return false;
        }

        int? limit = null;
        if (readCandidateLimit &&
            (!TryGetOptionalInt(request.Payload, "limit", out limit, out error) || limit <= 0))
        {
            error = error ?? BatchError.FromDiagnostic(DiagnosticIds.InvalidLimit, "limit must be 1 or greater.");
            return false;
        }

        string? kindError = GetKindError(assumeKinds);
        if (kindError is not null)
        {
            error = BatchError.FromDiagnostic(DiagnosticIds.InvalidSymbolKind, kindError);
            return false;
        }

        ProjectFilterResolutionResult projectResult =
            new ProjectFilterResolver().ResolveMany(loadedWorkspace.Solution, projectFilterValues);
        if (projectResult.Error is not null)
        {
            error = BatchError.FromDiagnostic(projectResult.Error.DiagnosticId, projectResult.Error.Message);
            return false;
        }

        options = new FuzzyQueryOptions(
            Query: query,
            AssumeKinds: hasCandidateId ? [] : NormalizeStrings(assumeKinds),
            Match: match,
            CaseSensitive: hasCandidateId ? null : caseSensitive == true ? true : null,
            ExcludeGenerated: excludeGenerated,
            Limit: limit,
            CandidateId: candidateId,
            Selection: new FuzzySelectionOptions(candidatePolicy, minConfidence, explainSelectionValue ?? false));
        if (candidateId is null && match == "regex")
        {
            SymbolSearchOptions searchOptions = new(
                Query: options.Query,
                MatchMode: SymbolMatchMode.Regex,
                CaseSensitive: options.CaseSensitive ?? false);

            if (!SymbolNameMatcher.TryCreate(searchOptions, out _, out string? regexError))
            {
                error = BatchError.FromDiagnostic(DiagnosticIds.InvalidRegex, regexError!);
                return false;
            }
        }

        projects = projectResult.Projects;
        projectFilters = projectResult.AppliedFilters.Count == 0
            ? null
            : projectResult.AppliedFilters.Select(filter => new FuzzyProjectFilter(
                Filter: filter.Filter,
                Name: filter.Name,
                Path: filter.Path,
                TargetFramework: filter.TargetFramework)).ToArray();
        error = null;
        return true;
    }

    private static bool TryGetFuzzySnippetOptions(
        JsonElement payload,
        out bool includeSnippets,
        out int snippetLines,
        out BatchError? error)
    {
        includeSnippets = false;
        snippetLines = FuzzyDiscoveryResolver.DefaultSnippetLines;

        if (!TryGetOptionalBool(payload, "includeSnippets", out bool? includeSnippetsValue, out error) ||
            !TryGetOptionalInt(payload, "snippetLines", out int? snippetLinesValue, out error))
        {
            return false;
        }

        includeSnippets = includeSnippetsValue ?? false;
        snippetLines = snippetLinesValue ?? FuzzyDiscoveryResolver.DefaultSnippetLines;
        if (snippetLines < 0)
        {
            error = BatchError.FromDiagnostic(DiagnosticIds.InvalidLimit, "snippetLines must be 0 or greater.");
            return false;
        }

        return true;
    }

    private static bool TryGetNavigationResultFilter(
        LoadedWorkspace loadedWorkspace,
        JsonElement payload,
        out NavigationResultBatchOptions resultFilter,
        out BatchError? error)
    {
        resultFilter = default!;

        if (!TryGetResultStringFilters(payload, "resultProject", "resultProjects", out IReadOnlyList<string> resultProjects, out error) ||
            !TryGetResultStringFilters(payload, "resultPath", "resultPaths", out IReadOnlyList<string> resultPaths, out error) ||
            !TryGetResultStringFilters(payload, "resultKind", "resultKinds", out IReadOnlyList<string> resultKinds, out error) ||
            !TryGetOptionalInt(payload, "limit", out int? limit, out error))
        {
            return false;
        }

        if (limit <= 0)
        {
            error = BatchError.FromDiagnostic(DiagnosticIds.InvalidLimit, "--limit must be 1 or greater.");
            return false;
        }

        string? kindError = GetKindError(resultKinds);
        if (kindError is not null)
        {
            error = BatchError.FromDiagnostic(DiagnosticIds.InvalidSymbolKind, kindError);
            return false;
        }

        ProjectFilterResolutionResult projectResult =
            new ProjectFilterResolver().ResolveMany(loadedWorkspace.Solution, resultProjects);
        if (projectResult.Error is not null)
        {
            error = BatchError.FromDiagnostic(projectResult.Error.DiagnosticId, projectResult.Error.Message);
            return false;
        }

        IReadOnlyList<string> normalizedPaths = [.. resultPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim().Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)];

        IReadOnlyList<string> normalizedKinds = NormalizeKinds(resultKinds);
        NavigationResultFilter filter = new(
            Projects: projectResult.Projects,
            AppliedProjectFilters: projectResult.AppliedFilters,
            PathFilters: normalizedPaths,
            KindFilters: normalizedKinds,
            Limit: limit);

        resultFilter = new NavigationResultBatchOptions(
            Filter: filter,
            ProjectFilters: projectResult.AppliedFilters.Count == 0
                ? null
                : projectResult.AppliedFilters.Select(ProjectFilterOutput.FromAppliedFilter).ToArray());
        error = null;
        return true;
    }

    private static bool TryGetReferenceUsageOptions(
        JsonElement payload,
        out IReadOnlyList<string> usageKinds,
        out IReadOnlyList<string> groupBy,
        out BatchError? error)
    {
        usageKinds = [];
        groupBy = [];

        if (!TryGetResultStringFilters(payload, "usageKind", "usageKinds", out IReadOnlyList<string> usageKindValues, out error) ||
            !TryGetStringArray(payload, "groupBy", out IReadOnlyList<string> groupByValues, out error, allowStringValue: true))
        {
            return false;
        }

        if (!ReferenceUsageTaxonomy.TryNormalizeUsageKinds(usageKindValues, out usageKinds, out string? usageError))
        {
            error = BatchError.FromDiagnostic(DiagnosticIds.ParseError, usageError!);
            return false;
        }

        if (!ReferenceUsageTaxonomy.TryNormalizeGroupKinds(groupByValues, out groupBy, out string? groupError))
        {
            error = BatchError.FromDiagnostic(DiagnosticIds.ParseError, groupError!);
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryGetDiagnosticsFilters(
        JsonElement payload,
        out IReadOnlyList<string> severities,
        out IReadOnlyList<string> ids,
        out int? limit,
        out BatchError? error)
    {
        severities = [];
        ids = [];
        limit = null;

        if (!TryGetResultStringFilters(payload, "severity", "severities", out IReadOnlyList<string> rawSeverities, out error) ||
            !TryGetDiagnosticIdFilters(payload, out IReadOnlyList<string> rawIds, out error) ||
            !TryGetOptionalInt(payload, "limit", out limit, out error))
        {
            return false;
        }

        if (limit <= 0)
        {
            error = BatchError.FromDiagnostic(DiagnosticIds.InvalidLimit, "--limit must be 1 or greater.");
            return false;
        }

        List<string> normalizedSeverities = [];
        foreach (string severity in rawSeverities)
        {
            if (string.IsNullOrWhiteSpace(severity))
            {
                error = BatchError.FromDiagnostic(
                    DiagnosticIds.InvalidDiagnosticSeverity,
                    "Diagnostic severity must not be empty.");
                return false;
            }

            string trimmed = severity.Trim();
            if (!Enum.GetNames<DiagnosticSeverity>().Contains(trimmed, StringComparer.Ordinal))
            {
                error = BatchError.FromDiagnostic(
                    DiagnosticIds.InvalidDiagnosticSeverity,
                    $"Unknown diagnostic severity: {trimmed}.");
                return false;
            }

            normalizedSeverities.Add(trimmed);
        }

        severities = [.. normalizedSeverities
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)];
        ids = [.. rawIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)];
        error = null;
        return true;
    }

    private static bool TryGetDiagnosticIdFilters(
        JsonElement payload,
        out IReadOnlyList<string> ids,
        out BatchError? error)
    {
        ids = [];

        bool hasDiagnosticId = payload.TryGetProperty("diagnosticId", out JsonElement diagnosticIdElement);
        bool hasDiagnosticIds = payload.TryGetProperty("diagnosticIds", out JsonElement diagnosticIdsElement);
        bool hasIds = payload.TryGetProperty("ids", out JsonElement idsElement);
        int propertyCount = (hasDiagnosticId ? 1 : 0) + (hasDiagnosticIds ? 1 : 0) + (hasIds ? 1 : 0);

        if (propertyCount > 1)
        {
            error = BatchError.FromDiagnostic(
                DiagnosticIds.InvalidBatchInput,
                "Use only one of diagnosticId, diagnosticIds, or ids.");
            return false;
        }

        if (hasDiagnosticId)
        {
            return TryReadStringArrayElement(diagnosticIdElement, "diagnosticId", out ids, out error, allowStringValue: true);
        }

        if (hasDiagnosticIds)
        {
            return TryReadStringArrayElement(diagnosticIdsElement, "diagnosticIds", out ids, out error, allowStringValue: true);
        }

        if (hasIds)
        {
            return TryReadStringArrayElement(idsElement, "ids", out ids, out error, allowStringValue: true);
        }

        error = null;
        return true;
    }

    private static bool TryGetResultStringFilters(
        JsonElement payload,
        string singularProperty,
        string pluralProperty,
        out IReadOnlyList<string> values,
        out BatchError? error)
    {
        values = [];

        bool hasSingular = payload.TryGetProperty(singularProperty, out JsonElement singularElement);
        bool hasPlural = payload.TryGetProperty(pluralProperty, out JsonElement pluralElement);
        if (hasSingular && hasPlural)
        {
            error = BatchError.FromDiagnostic(
                DiagnosticIds.InvalidBatchInput,
                $"Use either {singularProperty} or {pluralProperty}, not both.");
            return false;
        }

        if (hasPlural)
        {
            return TryReadStringArrayElement(pluralElement, pluralProperty, out values, out error, allowStringValue: true);
        }

        if (hasSingular)
        {
            return TryReadStringArrayElement(singularElement, singularProperty, out values, out error, allowStringValue: true);
        }

        error = null;
        return true;
    }

    private static bool TryGetRequiredFile(
        JsonElement payload,
        out FileInfo file,
        out BatchError? error)
    {
        file = default!;
        if (!TryGetRequiredString(payload, "file", out string filePath, out error))
        {
            return false;
        }

        file = new FileInfo(filePath);
        return true;
    }

    private static bool TryGetRequiredString(
        JsonElement payload,
        string propertyName,
        out string value,
        out BatchError? error)
    {
        value = string.Empty;
        if (!payload.TryGetProperty(propertyName, out JsonElement element) ||
            element.ValueKind != JsonValueKind.String)
        {
            error = BatchError.FromDiagnostic(
                DiagnosticIds.InvalidBatchInput,
                $"Batch request must include a {propertyName} string.");
            return false;
        }

        value = element.GetString()!;
        error = null;
        return true;
    }

    private static bool TryGetOptionalFile(
        JsonElement payload,
        string propertyName,
        out FileInfo? file,
        out BatchError? error)
    {
        file = null;
        if (!payload.TryGetProperty(propertyName, out JsonElement element))
        {
            error = null;
            return true;
        }

        if (!TryReadOptionalStringElement(element, propertyName, out string? value, out error))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            error = BatchError.FromDiagnostic(
                DiagnosticIds.InvalidBatchInput,
                $"{propertyName} must not be empty.");
            return false;
        }

        file = new FileInfo(value);
        error = null;
        return true;
    }

    private static bool TryGetRequiredInt(
        JsonElement payload,
        string propertyName,
        out int value,
        out BatchError? error)
    {
        value = 0;
        if (!payload.TryGetProperty(propertyName, out JsonElement element) ||
            element.ValueKind != JsonValueKind.Number ||
            !element.TryGetInt32(out value))
        {
            error = BatchError.FromDiagnostic(
                DiagnosticIds.InvalidBatchInput,
                $"Batch request must include a numeric {propertyName} value.");
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryGetStringArray(
        JsonElement payload,
        string propertyName,
        out IReadOnlyList<string> values,
        out BatchError? error,
        bool allowStringValue)
    {
        values = [];
        error = null;
        if (!payload.TryGetProperty(propertyName, out JsonElement element))
        {
            return true;
        }

        return TryReadStringArrayElement(element, propertyName, out values, out error, allowStringValue);
    }

    private static bool TryReadStringArrayElement(
        JsonElement element,
        string propertyName,
        out IReadOnlyList<string> values,
        out BatchError? error,
        bool allowStringValue)
    {
        values = [];
        if (allowStringValue && element.ValueKind == JsonValueKind.String)
        {
            values = [element.GetString()!];
            error = null;
            return true;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            error = BatchError.FromDiagnostic(
                DiagnosticIds.InvalidBatchInput,
                $"{propertyName} must be an array of strings.");
            return false;
        }

        List<string> strings = [];
        foreach (JsonElement item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                error = BatchError.FromDiagnostic(
                    DiagnosticIds.InvalidBatchInput,
                    $"{propertyName} must contain only strings.");
                return false;
            }

            strings.Add(item.GetString()!);
        }

        values = strings;
        error = null;
        return true;
    }

    private static bool TryGetOptionalInt(
        JsonElement payload,
        string propertyName,
        out int? value,
        out BatchError? error)
    {
        value = null;
        if (!payload.TryGetProperty(propertyName, out JsonElement element))
        {
            error = null;
            return true;
        }

        if (element.ValueKind != JsonValueKind.Number ||
            !element.TryGetInt32(out int intValue))
        {
            error = BatchError.FromDiagnostic(
                DiagnosticIds.InvalidBatchInput,
                $"{propertyName} must be a number.");
            return false;
        }

        value = intValue;
        error = null;
        return true;
    }

    private static bool TryGetOptionalString(
        JsonElement payload,
        string propertyName,
        out string? value,
        out BatchError? error)
    {
        value = null;
        if (!payload.TryGetProperty(propertyName, out JsonElement element))
        {
            error = null;
            return true;
        }

        return TryReadOptionalStringElement(element, propertyName, out value, out error);
    }

    private static bool TryGetProfile(JsonElement payload, out string profile, out BatchError? error)
    {
        if (!TryGetOptionalString(payload, "profile", out string? value, out error))
        {
            profile = OutputProfile.Default;
            return false;
        }

        if (value is null)
        {
            profile = OutputProfile.Default;
            error = null;
            return true;
        }

        profile = value.Trim();
        if (!OutputProfile.IsValid(profile))
        {
            error = BatchError.FromDiagnostic(
                DiagnosticIds.ParseError,
                "profile must be compact, evidence, or full.");
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryReadOptionalStringElement(
        JsonElement element,
        string propertyName,
        out string? value,
        out BatchError? error)
    {
        value = null;
        if (element.ValueKind != JsonValueKind.String)
        {
            error = BatchError.FromDiagnostic(
                DiagnosticIds.InvalidBatchInput,
                $"{propertyName} must be a string.");
            return false;
        }

        value = element.GetString();
        error = null;
        return true;
    }

    private static bool TryGetOptionalBool(
        JsonElement payload,
        string propertyName,
        out bool? value,
        out BatchError? error)
    {
        value = null;
        if (!payload.TryGetProperty(propertyName, out JsonElement element))
        {
            error = null;
            return true;
        }

        if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            error = BatchError.FromDiagnostic(
                DiagnosticIds.InvalidBatchInput,
                $"{propertyName} must be a boolean.");
            return false;
        }

        value = element.GetBoolean();
        error = null;
        return true;
    }

    private static bool TryGetEffectiveExcludeGenerated(
        JsonElement payload,
        BatchDefaults defaults,
        out bool excludeGenerated,
        out BatchError? error)
    {
        if (!TryGetOptionalBool(payload, "excludeGenerated", out bool? requestExcludeGenerated, out error))
        {
            excludeGenerated = false;
            return false;
        }

        excludeGenerated = requestExcludeGenerated ?? defaults.ExcludeGenerated ?? false;
        error = null;
        return true;
    }

    private static IReadOnlyList<SymbolDeclaration> FilterDeclarations(
        IReadOnlyList<SymbolDeclaration> declarations,
        IReadOnlyList<string> kinds,
        IReadOnlyList<SymbolNameMatcher> namespaceMatchers,
        IReadOnlyList<SymbolNameMatcher> containerMatchers,
        IReadOnlyList<string> accessibilities)
    {
        HashSet<string> kindSet = [.. kinds];
        HashSet<string> accessibilitySet = [.. accessibilities];
        return [.. declarations.Where(declaration =>
            (kindSet.Count == 0 || kindSet.Contains(declaration.Kind)) &&
            MatchesAny(namespaceMatchers, declaration.Facts.Namespace) &&
            MatchesAny(containerMatchers, declaration.Container) &&
            (accessibilitySet.Count == 0 ||
                (declaration.Facts.Accessibility is not null && accessibilitySet.Contains(declaration.Facts.Accessibility))))];
    }

    private static IReadOnlyList<string> NormalizeKinds(IReadOnlyList<string> kinds)
    {
        return [.. kinds
            .Where(kind => !string.IsNullOrWhiteSpace(kind))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(kind => kind, StringComparer.Ordinal)];
    }

    private static IReadOnlyList<string> NormalizeStrings(IReadOnlyList<string> values)
    {
        return [.. values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)];
    }

    private static string? GetKindError(IReadOnlyList<string> kinds)
    {
        foreach (string kind in kinds)
        {
            if (string.IsNullOrWhiteSpace(kind))
            {
                return "Symbol kind must not be empty.";
            }

            if (!Enum.GetNames<SymbolKind>().Contains(kind, StringComparer.Ordinal))
            {
                return $"Unknown symbol kind: {kind}.";
            }
        }

        return null;
    }

    private static string? GetAccessibilityError(IReadOnlyList<string> accessibilities)
    {
        foreach (string accessibility in accessibilities)
        {
            if (string.IsNullOrWhiteSpace(accessibility))
            {
                return "Accessibility filter must not be empty.";
            }

            if (!Enum.GetNames<Accessibility>().Contains(accessibility, StringComparer.Ordinal))
            {
                return $"Unknown accessibility: {accessibility}.";
            }
        }

        return null;
    }

    private static SymbolMatchMode ParseMatchMode(string match)
    {
        return match switch
        {
            "contains" => SymbolMatchMode.Contains,
            "exact" => SymbolMatchMode.Exact,
            "regex" => SymbolMatchMode.Regex,
            _ => throw new InvalidOperationException($"Unexpected match mode: {match}")
        };
    }

    private static bool TryCreateMatchers(
        IReadOnlyList<string> values,
        string match,
        bool caseSensitive,
        out IReadOnlyList<SymbolNameMatcher> matchers,
        out string? errorMessage)
    {
        List<SymbolNameMatcher> createdMatchers = [];
        foreach (string value in values)
        {
            SymbolSearchOptions options = new(
                Query: value,
                MatchMode: ParseMatchMode(match),
                CaseSensitive: caseSensitive);

            if (!SymbolNameMatcher.TryCreate(options, out SymbolNameMatcher matcher, out errorMessage))
            {
                matchers = [];
                return false;
            }

            createdMatchers.Add(matcher);
        }

        matchers = createdMatchers;
        errorMessage = null;
        return true;
    }

    private static bool MatchesAny(IReadOnlyList<SymbolNameMatcher> matchers, string? value)
    {
        return matchers.Count == 0 ||
            (value is not null && matchers.Any(matcher => matcher.IsMatch(value)));
    }


}

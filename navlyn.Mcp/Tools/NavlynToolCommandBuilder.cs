using System.Text.Json;
using System.Text.Json.Nodes;
using Navlyn.Mcp.Execution;

namespace Navlyn.Mcp.Tools;

internal static class NavlynToolCommandBuilder
{
    private static readonly string[] MatchValues = ["smart", "exact", "contains", "regex"];
    private static readonly string[] CandidatePolicyValues = ["fail", "select", "group"];
    private static readonly string[] MinConfidenceValues = ["high", "medium", "low"];
    private static readonly string[] GoalValues = ["review", "modify", "understand"];
    private static readonly string[] ChangeKindValues = ["behavior", "signature", "rename", "constructor", "nullability", "async", "public-api", "di-registration", "endpoint"];
    private static readonly string[] RiskValues = ["low", "medium", "high"];
    private static readonly string[] SnippetPolicyValues = ["none", "signature", "line", "block"];
    private static readonly string[] EntrypointModeValues = ["symbol", "framework"];
    private static readonly string[] ProfileValues = ["compact", "evidence", "full"];
    private static readonly string[] WorkflowProfileValues = ["light", "full"];
    private static readonly string[] NavigationScopeValues = ["file", "project", "dependent-projects", "workspace-set", "solution"];
    private static readonly string[] CacheModeValues = ["auto", "on", "off"];
    private static readonly string[] ExactNavigationOperations = ["definition", "references", "callers", "calls", "implementations", "type_hierarchy", "symbol_info"];
    private static readonly string[] FilteredExactNavigationOperations = ["references", "callers", "calls", "implementations"];
    private static readonly string[] SymbolEdgeOperations = ["references", "callers", "calls", "implementations"];
    private static readonly string[] SourceViewValues = ["signature", "declaration", "body", "members", "xml-doc", "attributes"];
    private static readonly string[] ReferenceUsageKindValues = ["read", "write", "invoke", "construct", "inherit", "implement", "override", "attribute", "nameof", "typeof"];
    private static readonly string[] ReferenceGroupByValues = ["file", "project", "containing-symbol", "usage-kind", "test-vs-production"];

    public static CommandBuildResult WorkspaceSummary(
        string? project,
        string[]? projects,
        bool? includePackages,
        bool? includeMsbuildFiles,
        bool? includePreprocessorSymbols,
        bool? classification,
        int? relationshipLimit,
        string? profile)
    {
        List<string> args = [];
        if (!TryAddProjects(args, project, projects, out string? error))
        {
            return CommandBuildResult.Invalid(error);
        }

        AddOptionalBoolValue(args, "--include-packages", includePackages);
        AddOptionalBoolValue(args, "--include-msbuild-files", includeMsbuildFiles);
        AddOptionalBoolValue(args, "--include-preprocessor-symbols", includePreprocessorSymbols);
        AddOptionalBoolValue(args, "--classification", classification);
        if (!TryAddPositiveInt(args, "--relationship-limit", relationshipLimit, out error) ||
            !TryAddAllowedValue(args, "--profile", profile, ProfileValues, out error))
        {
            return CommandBuildResult.Invalid(error);
        }

        return CommandBuildResult.Valid("repo-graph", args);
    }

    public static CommandBuildResult WorkspaceStatus(string? cache, string? cacheDirectory)
    {
        List<string> args = [];
        if (!TryAddAllowedValue(args, "--cache", cache, CacheModeValues, out string? error))
        {
            return CommandBuildResult.Invalid(error);
        }

        AddOptionalValue(args, "--cache-directory", cacheDirectory);
        return CommandBuildResult.Valid("workspace-status", args);
    }

    public static CommandBuildResult WorkspaceRefresh(
        string? cache,
        string? cacheDirectory,
        bool? clearCache,
        bool? writeCache)
    {
        List<string> args = [];
        if (!TryAddAllowedValue(args, "--cache", cache, CacheModeValues, out string? error))
        {
            return CommandBuildResult.Invalid(error);
        }

        AddOptionalValue(args, "--cache-directory", cacheDirectory);
        AddOptionalFlag(args, "--clear-cache", clearCache);
        AddOptionalFlag(args, "--write-cache", writeCache);
        return CommandBuildResult.Valid("workspace-refresh", args);
    }

    public static CommandBuildResult Doctor()
    {
        return CommandBuildResult.Valid("doctor", []);
    }

    public static CommandBuildResult FindSymbol(
        string query,
        string? assumeKind,
        string[]? assumeKinds,
        string? match,
        bool? caseSensitive,
        string? project,
        string[]? projects,
        bool? excludeGenerated,
        int? limit,
        string? candidatePolicy,
        string? minConfidence,
        bool? explainSelection)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return CommandBuildResult.Invalid("query is required.");
        }

        List<string> args = ["--query", query];
        if (!TryAddFuzzyOptions(
            args,
            assumeKind,
            assumeKinds,
            match,
            caseSensitive,
            project,
            projects,
            excludeGenerated,
            limit,
            candidatePolicy,
            minConfidence,
            explainSelection,
            allowGroupPolicy: true,
            out string? error))
        {
            return CommandBuildResult.Invalid(error);
        }

        return CommandBuildResult.Valid("find", args);
    }

    public static CommandBuildResult ResolveTarget(
        string? query,
        string? candidateId,
        string? file,
        int? line,
        int? column,
        string? assumeKind,
        string[]? assumeKinds,
        string? match,
        bool? caseSensitive,
        string? project,
        string[]? projects,
        bool? excludeGenerated,
        int? limit,
        string? candidatePolicy,
        string? minConfidence,
        bool? explainSelection)
    {
        if (!TryAddSymbolOrPositionInput([], query, candidateId, file, line, column, out List<string> args, out string? error))
        {
            return CommandBuildResult.Invalid(error);
        }

        bool sourcePositionMode = !string.IsNullOrWhiteSpace(file) || line is not null || column is not null;
        if (sourcePositionMode)
        {
            if (!TryAddSourcePositionOptions(
                args,
                "resolve-target",
                assumeKind,
                assumeKinds,
                match,
                caseSensitive,
                project,
                projects,
                excludeGenerated,
                limit,
                candidatePolicy,
                minConfidence,
                explainSelection,
                out error))
            {
                return CommandBuildResult.Invalid(error);
            }

            return CommandBuildResult.Valid("resolve-target", args);
        }

        if (!TryAddFuzzyOptions(
            args,
            assumeKind,
            assumeKinds,
            match,
            caseSensitive,
            project,
            projects,
            excludeGenerated,
            limit,
            candidatePolicy,
            minConfidence,
            explainSelection,
            allowGroupPolicy: false,
            out error))
        {
            return CommandBuildResult.Invalid(error);
        }

        return CommandBuildResult.Valid("resolve-target", args);
    }

    public static CommandBuildResult FuzzySymbolCommand(
        string cliCommand,
        string? query,
        string? candidateId,
        string? assumeKind,
        string[]? assumeKinds,
        string? match,
        bool? caseSensitive,
        string? project,
        string[]? projects,
        bool? excludeGenerated,
        int? memberLimit,
        int? referenceLimit,
        int? relationLimit,
        string? include,
        int? limit,
        int? depth,
        bool? includeSnippets,
        int? snippetLines,
        string? scope,
        int? maxDocuments,
        string? profile,
        string? candidatePolicy,
        string? minConfidence,
        bool? explainSelection)
    {
        if (!TryAddSymbolInput([], query, candidateId, out List<string> args, out string? error))
        {
            return CommandBuildResult.Invalid(error);
        }

        if (!TryAddFuzzyOptions(
            args,
            assumeKind,
            assumeKinds,
            match,
            caseSensitive,
            project,
            projects,
            excludeGenerated,
            limit: null,
            candidatePolicy,
            minConfidence,
            explainSelection,
            allowGroupPolicy: false,
            out error))
        {
            return CommandBuildResult.Invalid(error);
        }

        if (!TryAddPositiveInt(args, "--member-limit", memberLimit, out error) ||
            !TryAddPositiveInt(args, "--reference-limit", referenceLimit, out error) ||
            !TryAddPositiveInt(args, "--relation-limit", relationLimit, out error) ||
            !TryAddPositiveInt(args, "--limit", limit, out error) ||
            !TryAddNonNegativeInt(args, "--depth", depth, out error) ||
            !TryAddNonNegativeInt(args, "--snippet-lines", snippetLines, out error) ||
            !TryAddAllowedValue(args, "--scope", scope, NavigationScopeValues, out error) ||
            !TryAddPositiveInt(args, "--max-documents", maxDocuments, out error) ||
            !TryAddAllowedValue(args, "--profile", profile, WorkflowProfileValues, out error))
        {
            return CommandBuildResult.Invalid(error);
        }

        AddOptionalValue(args, "--include", include);
        AddOptionalFlag(args, "--include-snippets", includeSnippets);
        return CommandBuildResult.Valid(cliCommand, args);
    }

    public static CommandBuildResult Entrypoints(
        string? mode,
        string? query,
        string? candidateId,
        string? assumeKind,
        string[]? assumeKinds,
        string? match,
        bool? caseSensitive,
        string? project,
        string[]? projects,
        bool? excludeGenerated,
        string? framework,
        int? limit,
        int? depth,
        bool? includeSnippets,
        int? snippetLines,
        string? candidatePolicy,
        string? minConfidence,
        bool? explainSelection)
    {
        string effectiveMode = string.IsNullOrWhiteSpace(mode)
            ? string.IsNullOrWhiteSpace(query) && string.IsNullOrWhiteSpace(candidateId) ? "framework" : "symbol"
            : mode.Trim();
        if (!EntrypointModeValues.Contains(effectiveMode, StringComparer.Ordinal))
        {
            return CommandBuildResult.Invalid("mode must be symbol or framework.");
        }

        if (effectiveMode == "framework")
        {
            if (!string.IsNullOrWhiteSpace(query) || !string.IsNullOrWhiteSpace(candidateId))
            {
                return CommandBuildResult.Invalid("query and candidateId are not valid in framework entrypoint mode.");
            }

            List<string> frameworkArgs = [];
            if (!TryAddProjects(frameworkArgs, project, projects, out string? error) ||
                !TryAddPositiveInt(frameworkArgs, "--limit", limit, out error) ||
                !TryAddNonNegativeInt(frameworkArgs, "--snippet-lines", snippetLines, out error))
            {
                return CommandBuildResult.Invalid(error);
            }

            AddOptionalRepeated(frameworkArgs, "--framework", SplitCsv(framework));
            AddOptionalFlag(frameworkArgs, "--exclude-generated", excludeGenerated);
            AddOptionalFlag(frameworkArgs, "--include-snippets", includeSnippets);
            return CommandBuildResult.Valid("framework-entrypoints", frameworkArgs);
        }

        CommandBuildResult symbol = FuzzySymbolCommand(
            "entrypoints",
            query,
            candidateId,
            assumeKind,
            assumeKinds,
            match,
            caseSensitive,
            project,
            projects,
            excludeGenerated,
            memberLimit: null,
            referenceLimit: null,
            relationLimit: null,
            include: null,
            limit,
            depth,
            includeSnippets,
            snippetLines,
            scope: null,
            maxDocuments: null,
            profile: null,
            candidatePolicy,
            minConfidence,
            explainSelection);
        if (!symbol.IsValid)
        {
            return symbol;
        }

        List<string> symbolArgs = [.. symbol.Arguments];
        if (!string.IsNullOrWhiteSpace(framework))
        {
            symbolArgs.Add("--framework-aware");
            AddOptionalRepeated(symbolArgs, "--framework", SplitCsv(framework));
        }

        return CommandBuildResult.Valid("entrypoints", symbolArgs);
    }

    public static CommandBuildResult ReviewDiff(
        string? baseRef,
        string? head,
        bool? staged,
        bool? includeUnstaged,
        string? project,
        string[]? projects,
        bool? excludeGenerated,
        int? symbolLimit,
        int? impactLimit,
        int? diagnosticLimit,
        int? relatedTestLimit,
        int? depth,
        bool? includeSnippets,
        int? snippetLines,
        string? profile)
    {
        List<string> args = [];
        if (!TryAddDiffOptions(args, baseRef, head, staged, includeUnstaged, out string? error) ||
            !TryAddProjects(args, project, projects, out error) ||
            !TryAddPositiveInt(args, "--symbol-limit", symbolLimit, out error) ||
            !TryAddPositiveInt(args, "--impact-limit", impactLimit, out error) ||
            !TryAddPositiveInt(args, "--diagnostic-limit", diagnosticLimit, out error) ||
            !TryAddPositiveInt(args, "--related-test-limit", relatedTestLimit, out error) ||
            !TryAddNonNegativeInt(args, "--depth", depth, out error) ||
            !TryAddNonNegativeInt(args, "--snippet-lines", snippetLines, out error) ||
            !TryAddAllowedValue(args, "--profile", profile, ProfileValues, out error))
        {
            return CommandBuildResult.Invalid(error);
        }

        AddOptionalFlag(args, "--exclude-generated", excludeGenerated);
        AddOptionalFlag(args, "--include-snippets", includeSnippets);
        return CommandBuildResult.Valid("review-diff", args);
    }

    public static CommandBuildResult ContextPack(
        string? query,
        string? candidateId,
        bool? diff,
        string? baseRef,
        string? head,
        bool? staged,
        bool? includeUnstaged,
        string? goal,
        string? changeKind,
        int? budgetTokens,
        int? itemLimit,
        string? snippetPolicy,
        int? snippetLines,
        int? candidateLimit,
        int? memberLimit,
        int? referenceLimit,
        int? relationLimit,
        int? fileLimit,
        int? diagnosticLimit,
        int? symbolLimit,
        int? impactLimit,
        int? relatedTestLimit,
        int? depth,
        string? candidatePolicy,
        string? minConfidence,
        bool? explainSelection,
        string? assumeKind,
        string[]? assumeKinds,
        string? match,
        bool? caseSensitive,
        string? project,
        string[]? projects,
        bool? excludeGenerated,
        string? profile)
    {
        bool effectiveDiff = diff ?? false;
        bool hasQuery = !string.IsNullOrWhiteSpace(query);
        bool hasCandidateId = !string.IsNullOrWhiteSpace(candidateId);
        if ((hasQuery || hasCandidateId) == effectiveDiff || (hasQuery && hasCandidateId))
        {
            return CommandBuildResult.Invalid("Specify exactly one context-pack input mode: query, candidateId, or diff.");
        }

        List<string> args = [];
        if (hasQuery)
        {
            args.Add("--query");
            args.Add(query!);
        }
        else if (hasCandidateId)
        {
            args.Add("--candidate-id");
            args.Add(candidateId!);
        }
        else
        {
            args.Add("--diff");
        }

        if (!effectiveDiff && HasAnyDiffOption(baseRef, head, staged, includeUnstaged))
        {
            return CommandBuildResult.Invalid("Diff options require diff: true.");
        }

        if (effectiveDiff && !TryAddDiffOptions(args, baseRef, head, staged, includeUnstaged, out string? error))
        {
            return CommandBuildResult.Invalid(error);
        }

        if (effectiveDiff)
        {
            if (!TryAddDiffContextOptions(
                args,
                assumeKind,
                assumeKinds,
                match,
                caseSensitive,
                project,
                projects,
                excludeGenerated,
                candidateLimit,
                candidatePolicy,
                minConfidence,
                explainSelection,
                out error))
            {
                return CommandBuildResult.Invalid(error);
            }
        }
        else
        {
            if (!TryAddFuzzyOptions(
                args,
                assumeKind,
                assumeKinds,
                match,
                caseSensitive,
                project,
                projects,
                excludeGenerated,
                limit: null,
                candidatePolicy,
                minConfidence,
                explainSelection,
                allowGroupPolicy: false,
                out error))
            {
                return CommandBuildResult.Invalid(error);
            }
        }

        if (!TryAddAllowedValue(args, "--goal", goal, GoalValues, out error) ||
            !TryAddAllowedValue(args, "--change-kind", changeKind, ChangeKindValues, out error) ||
            !TryAddAllowedValue(args, "--snippet-policy", snippetPolicy, SnippetPolicyValues, out error) ||
            !TryAddPositiveInt(args, "--budget-tokens", budgetTokens, out error) ||
            !TryAddPositiveInt(args, "--item-limit", itemLimit, out error) ||
            !TryAddNonNegativeInt(args, "--snippet-lines", snippetLines, out error) ||
            !TryAddPositiveInt(args, "--candidate-limit", candidateLimit, out error) ||
            !TryAddPositiveInt(args, "--member-limit", memberLimit, out error) ||
            !TryAddPositiveInt(args, "--reference-limit", referenceLimit, out error) ||
            !TryAddPositiveInt(args, "--relation-limit", relationLimit, out error) ||
            !TryAddPositiveInt(args, "--file-limit", fileLimit, out error) ||
            !TryAddPositiveInt(args, "--diagnostic-limit", diagnosticLimit, out error) ||
            !TryAddPositiveInt(args, "--symbol-limit", symbolLimit, out error) ||
            !TryAddPositiveInt(args, "--impact-limit", impactLimit, out error) ||
            !TryAddPositiveInt(args, "--related-test-limit", relatedTestLimit, out error) ||
            !TryAddNonNegativeInt(args, "--depth", depth, out error) ||
            !TryAddAllowedValue(args, "--profile", profile, ProfileValues, out error))
        {
            return CommandBuildResult.Invalid(error);
        }

        return CommandBuildResult.Valid("context-pack", args);
    }

    public static CommandBuildResult FileOutline(
        string file,
        string? project,
        bool? excludeGenerated)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            return CommandBuildResult.Invalid("file is required.");
        }

        List<string> args = ["--file", file.Trim()];
        AddOptionalValue(args, "--project", project);
        AddOptionalFlag(args, "--exclude-generated", excludeGenerated);
        return CommandBuildResult.Valid("outline", args);
    }

    public static CommandBuildResult SymbolSource(
        string? candidateId,
        string? file,
        int? line,
        int? column,
        string? project,
        bool? excludeGenerated,
        string? view,
        int? maxLines,
        int? budgetTokens)
    {
        List<string> args = [];
        if (!TryAddExactNavigationTarget(args, candidateId, file, line, column, out string? error) ||
            !TryAddAllowedValue(args, "--view", view, SourceViewValues, out error) ||
            !TryAddPositiveInt(args, "--max-lines", maxLines, out error) ||
            !TryAddPositiveInt(args, "--budget-tokens", budgetTokens, out error))
        {
            return CommandBuildResult.Invalid(error);
        }

        AddOptionalValue(args, "--project", project);
        AddOptionalFlag(args, "--exclude-generated", excludeGenerated);
        return CommandBuildResult.Valid("symbol-source", args);
    }

    public static CommandBuildResult SymbolEdges(
        string operation,
        string? candidateId,
        string? file,
        int? line,
        int? column,
        string? project,
        bool? excludeGenerated,
        string? resultProject,
        string[]? resultProjects,
        string? resultPath,
        string[]? resultPaths,
        string? resultKind,
        string[]? resultKinds,
        string? usageKind,
        string[]? usageKinds,
        string[]? groupBy,
        int? limit,
        string? scope,
        int? maxDocuments,
        bool? includeMetadata)
    {
        if (string.IsNullOrWhiteSpace(operation))
        {
            return CommandBuildResult.Invalid("operation is required.");
        }

        string normalizedOperation = operation.Trim();
        if (!SymbolEdgeOperations.Contains(normalizedOperation, StringComparer.Ordinal))
        {
            return CommandBuildResult.Invalid($"operation must be one of: {string.Join(", ", SymbolEdgeOperations)}.");
        }

        return ExactNavigation(
            normalizedOperation,
            candidateId,
            file,
            line,
            column,
            project,
            excludeGenerated,
            resultProject,
            resultProjects,
            resultPath,
            resultPaths,
            resultKind,
            resultKinds,
            usageKind,
            usageKinds,
            groupBy,
            limit,
            scope,
            maxDocuments,
            includeMetadata);
    }

    public static CommandBuildResult SymbolEdges(
        string operation,
        string? candidateId,
        string? file,
        int? line,
        int? column,
        string? project,
        bool? excludeGenerated,
        string? resultProject,
        string[]? resultProjects,
        string? resultPath,
        string[]? resultPaths,
        string? resultKind,
        string[]? resultKinds,
        string? usageKind,
        string[]? usageKinds,
        string[]? groupBy,
        int? limit,
        bool? includeMetadata)
    {
        return SymbolEdges(
            operation,
            candidateId,
            file,
            line,
            column,
            project,
            excludeGenerated,
            resultProject,
            resultProjects,
            resultPath,
            resultPaths,
            resultKind,
            resultKinds,
            usageKind,
            usageKinds,
            groupBy,
            limit,
            scope: null,
            maxDocuments: null,
            includeMetadata);
    }

    public static CommandBuildResult InspectFile(
        string file,
        string? project,
        bool? excludeGenerated)
    {
        return FileOutline(file, project, excludeGenerated);
    }

    public static CommandBuildResult ExactNavigation(
        string operation,
        string? candidateId,
        string? file,
        int? line,
        int? column,
        string? project,
        bool? excludeGenerated,
        string? resultProject,
        string[]? resultProjects,
        string? resultPath,
        string[]? resultPaths,
        string? resultKind,
        string[]? resultKinds,
        string? usageKind,
        string[]? usageKinds,
        string[]? groupBy,
        int? limit,
        string? scope,
        int? maxDocuments,
        bool? includeMetadata)
    {
        if (string.IsNullOrWhiteSpace(operation))
        {
            return CommandBuildResult.Invalid("operation is required.");
        }

        string normalizedOperation = operation.Trim();
        if (!ExactNavigationOperations.Contains(normalizedOperation, StringComparer.Ordinal))
        {
            return CommandBuildResult.Invalid($"operation must be one of: {string.Join(", ", ExactNavigationOperations)}.");
        }

        List<string> args = [];
        if (!TryAddExactNavigationTarget(args, candidateId, file, line, column, out string? error))
        {
            return CommandBuildResult.Invalid(error);
        }

        AddOptionalValue(args, "--project", project);
        AddOptionalFlag(args, "--exclude-generated", excludeGenerated);

        bool hasResultFilters = !string.IsNullOrWhiteSpace(resultProject) ||
            NormalizeValues(resultProjects).Count > 0 ||
            !string.IsNullOrWhiteSpace(resultPath) ||
            NormalizeValues(resultPaths).Count > 0 ||
            !string.IsNullOrWhiteSpace(resultKind) ||
            NormalizeValues(resultKinds).Count > 0 ||
            limit is not null;
        if (hasResultFilters && !FilteredExactNavigationOperations.Contains(normalizedOperation, StringComparer.Ordinal))
        {
            return CommandBuildResult.Invalid("result filters are supported only for references, callers, calls, and implementations.");
        }

        bool hasReferenceUsageOptions = !string.IsNullOrWhiteSpace(usageKind) ||
            NormalizeValues(usageKinds).Count > 0 ||
            NormalizeValues(groupBy).Count > 0;
        if (hasReferenceUsageOptions && normalizedOperation != "references")
        {
            return CommandBuildResult.Invalid("usageKind, usageKinds, and groupBy are supported only for references.");
        }

        if (!TryAddSingleOrMany(args, "--result-project", resultProject, resultProjects, "resultProject", "resultProjects", out error) ||
            !TryAddSingleOrMany(args, "--result-path", resultPath, resultPaths, "resultPath", "resultPaths", out error) ||
            !TryAddSingleOrMany(args, "--result-kind", resultKind, resultKinds, "resultKind", "resultKinds", out error) ||
            !TryAddSingleOrManyAllowed(args, "--usage-kind", usageKind, usageKinds, "usageKind", "usageKinds", ReferenceUsageKindValues, out error) ||
            !TryAddManyAllowed(args, "--group-by", groupBy, "groupBy", ReferenceGroupByValues, out error) ||
            !TryAddPositiveInt(args, "--limit", limit, out error) ||
            !TryAddAllowedValue(args, "--scope", normalizedOperation is "references" or "callers" ? scope : null, NavigationScopeValues, out error) ||
            !TryAddPositiveInt(args, "--max-documents", normalizedOperation is "references" or "callers" ? maxDocuments : null, out error))
        {
            return CommandBuildResult.Invalid(error);
        }

        if (includeMetadata == true && normalizedOperation is not ("definition" or "calls"))
        {
            return CommandBuildResult.Invalid("includeMetadata is supported only for definition and calls.");
        }

        AddOptionalFlag(args, "--include-metadata", includeMetadata);
        return CommandBuildResult.Valid(ToCliExactNavigationCommand(normalizedOperation), args);
    }

    public static CommandBuildResult ExactNavigation(
        string operation,
        string? candidateId,
        string? file,
        int? line,
        int? column,
        string? project,
        bool? excludeGenerated,
        string? resultProject,
        string[]? resultProjects,
        string? resultPath,
        string[]? resultPaths,
        string? resultKind,
        string[]? resultKinds,
        string? usageKind,
        string[]? usageKinds,
        string[]? groupBy,
        int? limit,
        bool? includeMetadata)
    {
        return ExactNavigation(
            operation,
            candidateId,
            file,
            line,
            column,
            project,
            excludeGenerated,
            resultProject,
            resultProjects,
            resultPath,
            resultPaths,
            resultKind,
            resultKinds,
            usageKind,
            usageKinds,
            groupBy,
            limit,
            scope: null,
            maxDocuments: null,
            includeMetadata);
    }

    public static CommandBuildResult TestsForSymbol(
        string? query,
        string? candidateId,
        string? file,
        int? line,
        int? column,
        string? assumeKind,
        string[]? assumeKinds,
        string? match,
        bool? caseSensitive,
        string? project,
        string[]? projects,
        string? testProject,
        string[]? testProjects,
        bool? excludeGenerated,
        int? candidateLimit,
        int? testLimit,
        int? referenceLimit,
        bool? includeSnippets,
        int? snippetLines,
        string? candidatePolicy,
        string? minConfidence,
        bool? explainSelection,
        string? profile)
    {
        if (!TryAddSymbolOrPositionInput([], query, candidateId, file, line, column, out List<string> args, out string? error))
        {
            return CommandBuildResult.Invalid(error);
        }

        bool sourcePositionMode = !string.IsNullOrWhiteSpace(file) || line is not null || column is not null;
        if (sourcePositionMode)
        {
            if (!TryAddSourcePositionOptions(
                args,
                "tests-for-symbol",
                assumeKind,
                assumeKinds,
                match,
                caseSensitive,
                project,
                projects,
                excludeGenerated,
                candidateLimit,
                candidatePolicy,
                minConfidence,
                explainSelection,
                out error))
            {
                return CommandBuildResult.Invalid(error);
            }
        }
        else if (!TryAddFuzzyOptions(args, assumeKind, assumeKinds, match, caseSensitive, project, projects, excludeGenerated, limit: null, candidatePolicy, minConfidence, explainSelection, allowGroupPolicy: false, out error) ||
            !TryAddPositiveInt(args, "--candidate-limit", candidateLimit, out error))
        {
            return CommandBuildResult.Invalid(error);
        }

        if (!TryAddSingleOrMany(args, "--test-project", testProject, testProjects, "testProject", "testProjects", out error) ||
            !TryAddPositiveInt(args, "--test-limit", testLimit, out error) ||
            !TryAddPositiveInt(args, "--reference-limit", referenceLimit, out error) ||
            !TryAddNonNegativeInt(args, "--snippet-lines", snippetLines, out error) ||
            !TryAddAllowedValue(args, "--profile", profile, ProfileValues, out error))
        {
            return CommandBuildResult.Invalid(error);
        }

        AddOptionalFlag(args, "--include-snippets", includeSnippets);
        return CommandBuildResult.Valid("tests-for-symbol", args);
    }

    public static CommandBuildResult TestsForDiff(
        string? baseRef,
        string? head,
        bool? staged,
        bool? includeUnstaged,
        string? project,
        string[]? projects,
        string? testProject,
        string[]? testProjects,
        bool? excludeGenerated,
        int? symbolLimit,
        int? testLimit,
        int? referenceLimit,
        bool? includeSnippets,
        int? snippetLines,
        string? profile)
    {
        List<string> args = [];
        if (!TryAddDiffOptions(args, baseRef, head, staged, includeUnstaged, out string? error) ||
            !TryAddProjects(args, project, projects, out error) ||
            !TryAddSingleOrMany(args, "--test-project", testProject, testProjects, "testProject", "testProjects", out error) ||
            !TryAddPositiveInt(args, "--symbol-limit", symbolLimit, out error) ||
            !TryAddPositiveInt(args, "--test-limit", testLimit, out error) ||
            !TryAddPositiveInt(args, "--reference-limit", referenceLimit, out error) ||
            !TryAddNonNegativeInt(args, "--snippet-lines", snippetLines, out error) ||
            !TryAddAllowedValue(args, "--profile", profile, ProfileValues, out error))
        {
            return CommandBuildResult.Invalid(error);
        }

        AddOptionalFlag(args, "--exclude-generated", excludeGenerated);
        AddOptionalFlag(args, "--include-snippets", includeSnippets);
        return CommandBuildResult.Valid("tests-for-diff", args);
    }

    public static CommandBuildResult DiImpact(
        string? query,
        string? candidateId,
        string? file,
        int? line,
        int? column,
        string? assumeKind,
        string[]? assumeKinds,
        string? match,
        bool? caseSensitive,
        string? project,
        string[]? projects,
        bool? excludeGenerated,
        int? candidateLimit,
        int? registrationLimit,
        int? consumerLimit,
        int? dependencyLimit,
        int? riskLimit,
        int? depth,
        bool? includeSnippets,
        int? snippetLines,
        string? candidatePolicy,
        string? minConfidence,
        bool? explainSelection,
        string? profile)
    {
        if (!TryAddSymbolOrPositionInput([], query, candidateId, file, line, column, out List<string> args, out string? error))
        {
            return CommandBuildResult.Invalid(error);
        }

        bool sourcePositionMode = !string.IsNullOrWhiteSpace(file) || line is not null || column is not null;
        if (sourcePositionMode)
        {
            if (!TryAddSourcePositionOptions(
                args,
                "di-impact",
                assumeKind,
                assumeKinds,
                match,
                caseSensitive,
                project,
                projects,
                excludeGenerated,
                candidateLimit,
                candidatePolicy,
                minConfidence,
                explainSelection,
                out error))
            {
                return CommandBuildResult.Invalid(error);
            }
        }
        else if (!TryAddFuzzyOptions(args, assumeKind, assumeKinds, match, caseSensitive, project, projects, excludeGenerated, limit: null, candidatePolicy, minConfidence, explainSelection, allowGroupPolicy: false, out error) ||
            !TryAddPositiveInt(args, "--candidate-limit", candidateLimit, out error))
        {
            return CommandBuildResult.Invalid(error);
        }

        if (!TryAddPositiveInt(args, "--registration-limit", registrationLimit, out error) ||
            !TryAddPositiveInt(args, "--consumer-limit", consumerLimit, out error) ||
            !TryAddPositiveInt(args, "--dependency-limit", dependencyLimit, out error) ||
            !TryAddPositiveInt(args, "--risk-limit", riskLimit, out error) ||
            !TryAddNonNegativeInt(args, "--depth", depth, out error) ||
            !TryAddNonNegativeInt(args, "--snippet-lines", snippetLines, out error) ||
            !TryAddAllowedValue(args, "--profile", profile, ProfileValues, out error))
        {
            return CommandBuildResult.Invalid(error);
        }

        AddOptionalFlag(args, "--include-snippets", includeSnippets);
        return CommandBuildResult.Valid("di-impact", args);
    }

    public static CommandBuildResult PublicApiDiff(
        string? baseRef,
        string? head,
        string? project,
        string[]? projects,
        bool? excludeGenerated,
        bool? includeAdditions,
        bool? includeAttributes,
        int? symbolLimit,
        int? changeLimit,
        string? profile)
    {
        if (string.IsNullOrWhiteSpace(baseRef))
        {
            return CommandBuildResult.Invalid("base is required.");
        }

        List<string> args = ["--base", baseRef.Trim()];
        AddOptionalValue(args, "--head", head);
        if (!TryAddProjects(args, project, projects, out string? error) ||
            !TryAddPositiveInt(args, "--symbol-limit", symbolLimit, out error) ||
            !TryAddPositiveInt(args, "--change-limit", changeLimit, out error) ||
            !TryAddAllowedValue(args, "--profile", profile, ProfileValues, out error))
        {
            return CommandBuildResult.Invalid(error);
        }

        AddOptionalFlag(args, "--exclude-generated", excludeGenerated);
        AddOptionalBoolValue(args, "--include-additions", includeAdditions);
        AddOptionalBoolValue(args, "--include-attributes", includeAttributes);
        return CommandBuildResult.Valid("public-api-diff", args);
    }

    public static CommandBuildResult Batch(JsonElement? defaults, JsonElement? requests)
    {
        if (requests is null || requests.Value.ValueKind != JsonValueKind.Array || requests.Value.GetArrayLength() == 0)
        {
            return CommandBuildResult.Invalid("requests is required and must be a non-empty array.");
        }

        JsonObject input = [];
        if (defaults is not null && defaults.Value.ValueKind != JsonValueKind.Null)
        {
            input["defaults"] = JsonNode.Parse(defaults.Value.GetRawText());
        }

        input["requests"] = JsonNode.Parse(requests.Value.GetRawText());
        return CommandBuildResult.Valid("batch", [], input.ToJsonString(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    public static CommandBuildResult AgentTargetPack(
        string command,
        string? query,
        string? candidateId,
        string? file,
        int? line,
        int? column,
        string? assumeKind,
        string[]? assumeKinds,
        string? match,
        bool? caseSensitive,
        string? project,
        string[]? projects,
        bool? excludeGenerated,
        string? goal,
        string? changeKind,
        int? budgetTokens,
        int? itemLimit,
        int? referenceLimit,
        int? testLimit,
        int? candidateLimit,
        string? candidatePolicy,
        string? minConfidence,
        bool? explainSelection)
    {
        if (!TryAddSymbolOrPositionInput([], query, candidateId, file, line, column, out List<string> args, out string? error))
        {
            return CommandBuildResult.Invalid(error);
        }

        bool sourcePositionMode = !string.IsNullOrWhiteSpace(file) || line is not null || column is not null;
        if (sourcePositionMode)
        {
            if (!TryAddSourcePositionOptions(
                args,
                command,
                assumeKind,
                assumeKinds,
                match,
                caseSensitive,
                project,
                projects,
                excludeGenerated,
                candidateLimit,
                candidatePolicy,
                minConfidence,
                explainSelection,
                out error))
            {
                return CommandBuildResult.Invalid(error);
            }
        }
        else if (!TryAddFuzzyOptions(
            args,
            assumeKind,
            assumeKinds,
            match,
            caseSensitive,
            project,
            projects,
            excludeGenerated,
            candidateLimit,
            candidatePolicy,
            minConfidence,
            explainSelection,
            allowGroupPolicy: false,
            out error))
        {
            return CommandBuildResult.Invalid(error);
        }

        if (!TryAddAllowedValue(args, "--goal", goal, GoalValues, out error) ||
            !TryAddAllowedValue(args, "--change-kind", changeKind, ChangeKindValues, out error) ||
            !TryAddPositiveInt(args, "--budget-tokens", budgetTokens, out error) ||
            !TryAddPositiveInt(args, "--item-limit", itemLimit, out error) ||
            !TryAddPositiveInt(args, "--reference-limit", referenceLimit, out error) ||
            !TryAddPositiveInt(args, "--test-limit", testLimit, out error))
        {
            return CommandBuildResult.Invalid(error);
        }

        return CommandBuildResult.Valid(command, args);
    }

    public static CommandBuildResult PostEditGuard(
        string? candidateId,
        string? preflight,
        string? baseRef,
        string? head,
        bool? staged,
        bool? includeUnstaged,
        string? project,
        string[]? projects,
        bool? excludeGenerated,
        int? symbolLimit,
        string? failOnRisk)
    {
        bool hasCandidate = !string.IsNullOrWhiteSpace(candidateId);
        bool hasPreflight = !string.IsNullOrWhiteSpace(preflight);
        if (hasCandidate == hasPreflight)
        {
            return CommandBuildResult.Invalid("Specify exactly one anchor: candidateId or preflight.");
        }

        List<string> args = [];
        AddOptionalValue(args, "--candidate-id", candidateId);
        AddOptionalValue(args, "--preflight", preflight);
        if (!TryAddDiffOptions(args, baseRef, head, staged, includeUnstaged, out string? error) ||
            !TryAddProjects(args, project, projects, out error) ||
            !TryAddPositiveInt(args, "--symbol-limit", symbolLimit, out error) ||
            !TryAddAllowedValue(args, "--fail-on-risk", failOnRisk, RiskValues, out error))
        {
            return CommandBuildResult.Invalid(error);
        }

        AddOptionalFlag(args, "--exclude-generated", excludeGenerated);
        return CommandBuildResult.Valid("post-edit-guard", args);
    }

    public static CommandBuildResult WrongSymbolGuard(
        string? query,
        string? candidateId,
        string? file,
        int? line,
        int? column,
        string? assumeKind,
        string[]? assumeKinds,
        string? match,
        bool? caseSensitive,
        string? project,
        string[]? projects,
        bool? excludeGenerated,
        string? baseRef,
        string? head,
        bool? staged,
        bool? includeUnstaged,
        int? symbolLimit,
        string? failOnRisk,
        int? candidateLimit,
        string? candidatePolicy,
        string? minConfidence,
        bool? explainSelection)
    {
        CommandBuildResult target = AgentTargetPack(
            "wrong-symbol-guard",
            query,
            candidateId,
            file,
            line,
            column,
            assumeKind,
            assumeKinds,
            match,
            caseSensitive,
            project,
            projects,
            excludeGenerated,
            goal: null,
            changeKind: null,
            budgetTokens: null,
            itemLimit: null,
            referenceLimit: null,
            testLimit: null,
            candidateLimit,
            candidatePolicy,
            minConfidence,
            explainSelection);
        if (!target.IsValid)
        {
            return target;
        }

        List<string> args = [.. target.Arguments];
        if (!TryAddDiffOptions(args, baseRef, head, staged, includeUnstaged, out string? error) ||
            !TryAddPositiveInt(args, "--symbol-limit", symbolLimit, out error) ||
            !TryAddAllowedValue(args, "--fail-on-risk", failOnRisk, RiskValues, out error))
        {
            return CommandBuildResult.Invalid(error);
        }

        return CommandBuildResult.Valid("wrong-symbol-guard", args);
    }

    private static bool TryAddSourcePositionOptions(
        List<string> args,
        string commandName,
        string? assumeKind,
        string[]? assumeKinds,
        string? match,
        bool? caseSensitive,
        string? project,
        string[]? projects,
        bool? excludeGenerated,
        int? candidateLimit,
        string? candidatePolicy,
        string? minConfidence,
        bool? explainSelection,
        out string? error)
    {
        if (HasFuzzySelectionOptions(assumeKind, assumeKinds, match, caseSensitive, candidateLimit, candidatePolicy, minConfidence, explainSelection))
        {
            error = $"Source-position {commandName} mode cannot be combined with fuzzy options.";
            return false;
        }

        if (!TryAddSourcePositionProject(args, project, projects, out error))
        {
            return false;
        }

        AddOptionalFlag(args, "--exclude-generated", excludeGenerated);
        return true;
    }

    private static bool TryAddDiffContextOptions(
        List<string> args,
        string? assumeKind,
        string[]? assumeKinds,
        string? match,
        bool? caseSensitive,
        string? project,
        string[]? projects,
        bool? excludeGenerated,
        int? candidateLimit,
        string? candidatePolicy,
        string? minConfidence,
        bool? explainSelection,
        out string? error)
    {
        if (HasFuzzySelectionOptions(assumeKind, assumeKinds, match, caseSensitive, candidateLimit, candidatePolicy, minConfidence, explainSelection))
        {
            error = "Diff context-pack mode cannot be combined with fuzzy selection options.";
            return false;
        }

        if (!TryAddProjects(args, project, projects, out error))
        {
            return false;
        }

        AddOptionalFlag(args, "--exclude-generated", excludeGenerated);
        return true;
    }

    private static bool HasFuzzySelectionOptions(
        string? assumeKind,
        string[]? assumeKinds,
        string? match,
        bool? caseSensitive,
        int? candidateLimit,
        string? candidatePolicy,
        string? minConfidence,
        bool? explainSelection)
    {
        return !string.IsNullOrWhiteSpace(assumeKind) ||
            NormalizeValues(assumeKinds).Count > 0 ||
            !string.IsNullOrWhiteSpace(match) ||
            caseSensitive is not null ||
            candidateLimit is not null ||
            !string.IsNullOrWhiteSpace(candidatePolicy) ||
            !string.IsNullOrWhiteSpace(minConfidence) ||
            explainSelection is not null;
    }

    private static bool TryAddFuzzyOptions(
        List<string> args,
        string? assumeKind,
        string[]? assumeKinds,
        string? match,
        bool? caseSensitive,
        string? project,
        string[]? projects,
        bool? excludeGenerated,
        int? limit,
        string? candidatePolicy,
        string? minConfidence,
        bool? explainSelection,
        bool allowGroupPolicy,
        out string? error)
    {
        if (!TryAddSingleOrMany(args, "--assume-kind", assumeKind, assumeKinds, "assumeKind", "assumeKinds", out error) ||
            !TryAddProjects(args, project, projects, out error) ||
            !TryAddPositiveInt(args, "--limit", limit, out error) ||
            !TryAddAllowedValue(args, "--match", match, MatchValues, out error) ||
            !TryAddAllowedValue(args, "--candidate-policy", candidatePolicy, allowGroupPolicy ? CandidatePolicyValues : CandidatePolicyValues.Where(value => value != "group").ToArray(), out error) ||
            !TryAddAllowedValue(args, "--min-confidence", minConfidence, MinConfidenceValues, out error))
        {
            return false;
        }

        AddOptionalFlag(args, "--case-sensitive", caseSensitive);
        AddOptionalFlag(args, "--exclude-generated", excludeGenerated);
        AddOptionalFlag(args, "--explain-selection", explainSelection);
        return true;
    }

    private static bool TryAddSymbolInput(
        List<string> args,
        string? query,
        string? candidateId,
        out List<string> updatedArgs,
        out string? error)
    {
        bool hasQuery = !string.IsNullOrWhiteSpace(query);
        bool hasCandidateId = !string.IsNullOrWhiteSpace(candidateId);
        if (hasQuery == hasCandidateId)
        {
            updatedArgs = args;
            error = "Specify exactly one of query or candidateId.";
            return false;
        }

        updatedArgs = args;
        updatedArgs.Add(hasQuery ? "--query" : "--candidate-id");
        updatedArgs.Add(hasQuery ? query! : candidateId!);
        error = null;
        return true;
    }

    private static bool TryAddExactNavigationTarget(
        List<string> args,
        string? candidateId,
        string? file,
        int? line,
        int? column,
        out string? error)
    {
        bool hasCandidateId = !string.IsNullOrWhiteSpace(candidateId);
        bool hasAnySourcePosition = !string.IsNullOrWhiteSpace(file) || line is not null || column is not null;
        bool hasCompleteSourcePosition = !string.IsNullOrWhiteSpace(file) && line is not null && column is not null;
        if (hasCandidateId && hasAnySourcePosition || !hasCandidateId && !hasCompleteSourcePosition)
        {
            error = "Specify exactly one target: candidateId or file with line and column.";
            return false;
        }

        if (hasCandidateId)
        {
            args.Add("--candidate-id");
            args.Add(candidateId!.Trim());
        }
        else
        {
            args.Add("--file");
            args.Add(file!.Trim());
            args.Add("--line");
            args.Add(line!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            args.Add("--column");
            args.Add(column!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        error = null;
        return true;
    }

    private static bool TryAddSymbolOrPositionInput(
        List<string> args,
        string? query,
        string? candidateId,
        string? file,
        int? line,
        int? column,
        out List<string> updatedArgs,
        out string? error)
    {
        bool hasQuery = !string.IsNullOrWhiteSpace(query);
        bool hasCandidateId = !string.IsNullOrWhiteSpace(candidateId);
        bool hasAnySourcePosition = !string.IsNullOrWhiteSpace(file) || line is not null || column is not null;
        bool hasCompleteSourcePosition = !string.IsNullOrWhiteSpace(file) && line is not null && column is not null;
        int modeCount = (hasQuery ? 1 : 0) + (hasCandidateId ? 1 : 0) + (hasAnySourcePosition ? 1 : 0);
        if (modeCount != 1 || hasAnySourcePosition && !hasCompleteSourcePosition)
        {
            updatedArgs = args;
            error = "Specify exactly one target: query, candidateId, or file with line and column.";
            return false;
        }

        updatedArgs = args;
        if (hasQuery)
        {
            updatedArgs.Add("--query");
            updatedArgs.Add(query!.Trim());
        }
        else if (hasCandidateId)
        {
            updatedArgs.Add("--candidate-id");
            updatedArgs.Add(candidateId!.Trim());
        }
        else
        {
            updatedArgs.Add("--file");
            updatedArgs.Add(file!.Trim());
            updatedArgs.Add("--line");
            updatedArgs.Add(line!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            updatedArgs.Add("--column");
            updatedArgs.Add(column!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        error = null;
        return true;
    }

    private static string ToCliExactNavigationCommand(string operation)
    {
        return operation switch
        {
            "type_hierarchy" => "type-hierarchy",
            "symbol_info" => "symbol-info",
            _ => operation
        };
    }

    private static bool TryAddDiffOptions(
        List<string> args,
        string? baseRef,
        string? head,
        bool? staged,
        bool? includeUnstaged,
        out string? error)
    {
        if (!string.IsNullOrWhiteSpace(head) && string.IsNullOrWhiteSpace(baseRef))
        {
            error = "head requires base.";
            return false;
        }

        if (staged == true && (!string.IsNullOrWhiteSpace(baseRef) || !string.IsNullOrWhiteSpace(head)))
        {
            error = "staged cannot be combined with base or head.";
            return false;
        }

        AddOptionalValue(args, "--base", baseRef);
        AddOptionalValue(args, "--head", head);
        AddOptionalFlag(args, "--staged", staged);
        AddOptionalBoolValue(args, "--include-unstaged", includeUnstaged);
        error = null;
        return true;
    }

    private static bool TryAddProjects(List<string> args, string? project, string[]? projects, out string? error)
    {
        return TryAddSingleOrMany(args, "--project", project, projects, "project", "projects", out error);
    }

    private static bool TryAddSourcePositionProject(List<string> args, string? project, string[]? projects, out string? error)
    {
        bool hasSingle = !string.IsNullOrWhiteSpace(project);
        IReadOnlyList<string> values = NormalizeValues(projects);
        if (hasSingle && values.Count > 0)
        {
            error = "project and projects are mutually exclusive.";
            return false;
        }

        if (values.Count > 1)
        {
            error = "Source-position mode accepts at most one project.";
            return false;
        }

        AddOptionalValue(args, "--project", hasSingle ? project : values.FirstOrDefault());
        error = null;
        return true;
    }

    private static bool TryAddSingleOrMany(
        List<string> args,
        string option,
        string? single,
        string[]? many,
        string singleName,
        string manyName,
        out string? error)
    {
        bool hasSingle = !string.IsNullOrWhiteSpace(single);
        IReadOnlyList<string> values = NormalizeValues(many);
        if (hasSingle && values.Count > 0)
        {
            error = $"{singleName} and {manyName} are mutually exclusive.";
            return false;
        }

        if (hasSingle)
        {
            args.Add(option);
            args.Add(single!.Trim());
        }
        else
        {
            AddOptionalRepeated(args, option, values);
        }

        error = null;
        return true;
    }

    private static bool TryAddSingleOrManyAllowed(
        List<string> args,
        string option,
        string? single,
        string[]? many,
        string singleName,
        string manyName,
        IReadOnlyList<string> allowed,
        out string? error)
    {
        bool hasSingle = !string.IsNullOrWhiteSpace(single);
        IReadOnlyList<string> values = NormalizeValues(many);
        if (hasSingle && values.Count > 0)
        {
            error = $"{singleName} and {manyName} are mutually exclusive.";
            return false;
        }

        IReadOnlyList<string> normalized = hasSingle ? SplitCsv(single) : SplitCsv(values);
        string? invalid = normalized.FirstOrDefault(value => !allowed.Contains(value, StringComparer.Ordinal));
        if (invalid is not null)
        {
            error = $"{singleName} must be one of: {string.Join(", ", allowed)}.";
            return false;
        }

        AddOptionalRepeated(args, option, normalized);
        error = null;
        return true;
    }

    private static bool TryAddManyAllowed(
        List<string> args,
        string option,
        string[]? values,
        string name,
        IReadOnlyList<string> allowed,
        out string? error)
    {
        IReadOnlyList<string> normalized = SplitCsv(NormalizeValues(values));
        string? invalid = normalized.FirstOrDefault(value => !allowed.Contains(value, StringComparer.Ordinal));
        if (invalid is not null)
        {
            error = $"{name} must be one of: {string.Join(", ", allowed)}.";
            return false;
        }

        AddOptionalRepeated(args, option, normalized);
        error = null;
        return true;
    }

    private static bool TryAddAllowedValue(
        List<string> args,
        string option,
        string? value,
        IReadOnlyList<string> allowed,
        out string? error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            error = null;
            return true;
        }

        string trimmed = value.Trim();
        if (!allowed.Contains(trimmed, StringComparer.Ordinal))
        {
            error = $"{option.TrimStart('-')} must be one of: {string.Join(", ", allowed)}.";
            return false;
        }

        args.Add(option);
        args.Add(trimmed);
        error = null;
        return true;
    }

    private static bool TryAddPositiveInt(List<string> args, string option, int? value, out string? error)
    {
        if (value is null)
        {
            error = null;
            return true;
        }

        if (value <= 0)
        {
            error = $"{option.TrimStart('-')} must be 1 or greater.";
            return false;
        }

        args.Add(option);
        args.Add(value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        error = null;
        return true;
    }

    private static bool TryAddNonNegativeInt(List<string> args, string option, int? value, out string? error)
    {
        if (value is null)
        {
            error = null;
            return true;
        }

        if (value < 0)
        {
            error = $"{option.TrimStart('-')} must be 0 or greater.";
            return false;
        }

        args.Add(option);
        args.Add(value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        error = null;
        return true;
    }

    private static void AddOptionalValue(List<string> args, string option, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            args.Add(option);
            args.Add(value.Trim());
        }
    }

    private static void AddOptionalBoolValue(List<string> args, string option, bool? value)
    {
        if (value is not null)
        {
            args.Add(option);
            args.Add(value.Value ? "true" : "false");
        }
    }

    private static void AddOptionalFlag(List<string> args, string option, bool? value)
    {
        if (value == true)
        {
            args.Add(option);
        }
    }

    private static void AddOptionalRepeated(List<string> args, string option, IReadOnlyList<string> values)
    {
        foreach (string value in values)
        {
            args.Add(option);
            args.Add(value);
        }
    }

    private static bool HasAnyDiffOption(string? baseRef, string? head, bool? staged, bool? includeUnstaged)
    {
        return !string.IsNullOrWhiteSpace(baseRef) ||
            !string.IsNullOrWhiteSpace(head) ||
            staged is not null ||
            includeUnstaged is not null;
    }

    private static IReadOnlyList<string> NormalizeValues(string[]? values)
    {
        return values is null
            ? []
            : [.. values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim())];
    }

    private static IReadOnlyList<string> SplitCsv(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? []
            : [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }

    private static IReadOnlyList<string> SplitCsv(IReadOnlyList<string> values)
    {
        return [.. values.SelectMany(SplitCsv)];
    }
}

internal sealed record CommandBuildResult(
    bool IsValid,
    string? Command,
    IReadOnlyList<string> Arguments,
    string? StandardInput,
    string? Error)
{
    public static CommandBuildResult Valid(string command, IReadOnlyList<string> arguments, string? standardInput = null)
    {
        return new CommandBuildResult(IsValid: true, command, arguments, standardInput, Error: null);
    }

    public static CommandBuildResult Invalid(string? error)
    {
        return new CommandBuildResult(IsValid: false, Command: null, Arguments: [], StandardInput: null, error ?? "Invalid tool arguments.");
    }
}

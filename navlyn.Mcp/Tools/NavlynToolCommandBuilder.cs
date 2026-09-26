using System.Text.Json;
using System.Text.Json.Nodes;
using Navlyn.Mcp.Execution;

namespace Navlyn.Mcp.Tools;

internal static class NavlynToolCommandBuilder
{
    private static readonly string[] MatchValues = ["smart", "exact", "contains", "regex"];
    private static readonly string[] CandidatePolicyValues = ["fail", "select", "group"];
    private static readonly string[] MessageCandidatePolicyValues = ["fail", "select"];
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
    private static readonly string[] DiagnosticSeverityValues = ["Hidden", "Info", "Warning", "Error"];
    private static readonly string[] EndpointKindValues = ["any", "controller-action", "minimal-api"];
    private static readonly string[] RouteAuthValues = ["any", "required", "anonymous", "unknown"];
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
            !TryAddProfile(args, profile, "compact", ProfileValues, out error))
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

    public static CommandBuildResult Diagnostics(
        string? mode,
        string? project,
        string[]? projects,
        bool? excludeGenerated,
        string? severity,
        string[]? severities,
        int? limit,
        string? diagnosticId,
        string[]? diagnosticIds,
        string? candidateId,
        string? file,
        int? line,
        int? column)
    {
        if (mode is not ("workspace" or "symbol" or "pack"))
        {
            return CommandBuildResult.Invalid("mode must be workspace, symbol, or pack.");
        }

        List<string> args = [];
        string? error;
        string command;

        switch (mode)
        {
            case "workspace":
                if (HasAnyTargetInput(candidateId, file, line, column))
                {
                    return CommandBuildResult.Invalid("workspace mode does not accept candidateId or source-position inputs.");
                }

                if (!TryAddProjects(args, project, projects, out error))
                {
                    return CommandBuildResult.Invalid(error);
                }

                command = "diagnostics";
                break;

            case "symbol":
                if (projects is not null)
                {
                    return CommandBuildResult.Invalid("projects is supported only in workspace mode; use project for symbol mode.");
                }

                if (!TryAddExactNavigationTarget(args, candidateId, file, line, column, out error))
                {
                    return CommandBuildResult.Invalid(error);
                }

                AddOptionalValue(args, "--project", project);
                command = "symbol-diagnostics";
                break;

            case "pack":
                if (projects is not null)
                {
                    return CommandBuildResult.Invalid("projects is supported only in workspace mode; use project for pack mode.");
                }

                if (!string.IsNullOrWhiteSpace(candidateId))
                {
                    return CommandBuildResult.Invalid("candidateId is not supported in pack mode.");
                }

                if (diagnosticIds is not null)
                {
                    return CommandBuildResult.Invalid("diagnosticIds is not supported in pack mode; provide diagnosticId as the pack input.");
                }

                bool hasDiagnosticId = !string.IsNullOrWhiteSpace(diagnosticId);
                bool hasAnyPosition = HasAnyPosition(file, line, column);
                bool hasCompletePosition = !string.IsNullOrWhiteSpace(file) && line is not null && column is not null;
                if (hasDiagnosticId == hasAnyPosition || hasAnyPosition && !hasCompletePosition)
                {
                    return CommandBuildResult.Invalid("pack mode requires exactly one input: diagnosticId or file with line and column.");
                }

                if (hasDiagnosticId)
                {
                    AddOptionalValue(args, "--id", diagnosticId);
                }
                else if (!TryAddExactNavigationTarget(args, candidateId: null, file, line, column, out error))
                {
                    return CommandBuildResult.Invalid(error);
                }

                AddOptionalValue(args, "--project", project);
                command = "diagnostic-pack";
                break;

            default:
                return CommandBuildResult.Invalid("mode must be workspace, symbol, or pack.");
        }

        AddOptionalFlag(args, "--exclude-generated", excludeGenerated);
        if (!TryAddDiagnosticSeverities(args, severity, severities, out error) ||
            !TryAddPositiveInt(args, "--limit", limit, out error))
        {
            return CommandBuildResult.Invalid(error);
        }

        if ((mode is "workspace" or "symbol") &&
            !TryAddSingleOrMany(args, "--id", diagnosticId, diagnosticIds, "diagnosticId", "diagnosticIds", out error))
        {
            return CommandBuildResult.Invalid(error);
        }

        return CommandBuildResult.Valid(command, args);
    }

    public static CommandBuildResult Target(
        string? mode,
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
        string effectiveMode = mode ?? "select";
        if (effectiveMode == "list")
        {
            if (candidatePolicy is not null && candidatePolicy != "group")
            {
                return CommandBuildResult.Invalid("candidatePolicy in list mode must be omitted or group.");
            }

            if (!string.IsNullOrWhiteSpace(candidateId) ||
                !string.IsNullOrWhiteSpace(file) ||
                line is not null ||
                column is not null)
            {
                return CommandBuildResult.Invalid("list mode accepts query and fuzzy filters only; candidateId and source-position inputs are not valid.");
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                return CommandBuildResult.Invalid("query is required in list mode.");
            }

            return FindSymbol(query, assumeKind, assumeKinds, match, caseSensitive, project, projects, excludeGenerated, limit, "group", minConfidence, explainSelection);
        }

        if (effectiveMode != "select")
        {
            return CommandBuildResult.Invalid("mode must be select or list.");
        }

        CommandBuildResult result = ResolveTarget(query, candidateId, file, line, column, assumeKind, assumeKinds, match, caseSensitive, project, projects, excludeGenerated, limit, candidatePolicy, minConfidence, explainSelection);
        return result.IsValid ? result with { Command = "target" } : result;
    }

    public static CommandBuildResult Read(
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
        CommandBuildResult result = SymbolSource(candidateId, file, line, column, project, excludeGenerated, view, maxLines, budgetTokens);
        return result.IsValid ? result with { Command = "read" } : result;
    }

    public static CommandBuildResult PrepareEdit(
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
        return AgentTargetPack("prepare-edit", query, candidateId, file, line, column, assumeKind, assumeKinds, match, caseSensitive, project, projects, excludeGenerated, goal, changeKind, budgetTokens, itemLimit, referenceLimit, testLimit, candidateLimit, candidatePolicy, minConfidence, explainSelection);
    }

    public static CommandBuildResult VerifyEdit(
        string? query,
        string? candidateId,
        string? preflight,
        string? file,
        int? line,
        int? column,
        string? assumeKind,
        string[]? assumeKinds,
        string? match,
        bool? caseSensitive,
        string? baseRef,
        string? head,
        bool? staged,
        bool? includeUnstaged,
        string? project,
        string[]? projects,
        bool? excludeGenerated,
        int? symbolLimit,
        string? failOnRisk,
        int? candidateLimit,
        string? candidatePolicy,
        string? minConfidence,
        bool? explainSelection)
    {
        bool hasPreflight = !string.IsNullOrWhiteSpace(preflight);
        bool hasCandidateId = !string.IsNullOrWhiteSpace(candidateId);
        bool hasQuery = !string.IsNullOrWhiteSpace(query);
        bool hasAnySourcePosition = !string.IsNullOrWhiteSpace(file) || line is not null || column is not null;
        int intentCount = (hasPreflight ? 1 : 0) + (hasCandidateId ? 1 : 0) + (hasQuery ? 1 : 0) + (hasAnySourcePosition ? 1 : 0);
        if (intentCount != 1)
        {
            return CommandBuildResult.Invalid("Specify exactly one verify intent: preflight, candidateId, query, or file with line and column.");
        }

        if (hasPreflight || hasCandidateId)
        {
            if (HasFuzzySelectionOptions(assumeKind, assumeKinds, match, caseSensitive, candidateLimit, candidatePolicy, minConfidence, explainSelection))
            {
                return CommandBuildResult.Invalid("Fuzzy selection options are supported only with query mode.");
            }

            CommandBuildResult anchorResult = PostEditGuard(candidateId, preflight, baseRef, head, staged, includeUnstaged, project, projects, excludeGenerated, symbolLimit, failOnRisk);
            return anchorResult.IsValid ? anchorResult with { Command = "verify-edit", ResultCommand = "verify-edit" } : anchorResult;
        }

        CommandBuildResult symbolResult = WrongSymbolGuard(
            query,
            null,
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
            baseRef,
            head,
            staged,
            includeUnstaged,
            symbolLimit,
            failOnRisk,
            candidateLimit,
            candidatePolicy,
            minConfidence,
            explainSelection);
        return symbolResult.IsValid ? symbolResult with { ResultCommand = "verify-edit" } : symbolResult;
    }

    public static CommandBuildResult Review(
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
        CommandBuildResult result = ReviewDiff(baseRef, head, staged, includeUnstaged, project, projects, excludeGenerated, symbolLimit, impactLimit, diagnosticLimit, relatedTestLimit, depth, includeSnippets, snippetLines, profile);
        return result.IsValid ? result with { Command = "review" } : result;
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
            !TryAddPositiveInt(args, "--max-documents", maxDocuments, out error))
        {
            return CommandBuildResult.Invalid(error);
        }

        if (cliCommand == "impact")
        {
            if (!TryAddProfile(args, profile, "light", WorkflowProfileValues, out error))
            {
                return CommandBuildResult.Invalid(error);
            }
        }
        else if (!TryAddAllowedValue(args, "--profile", profile, WorkflowProfileValues, out error))
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
            !TryAddProfile(args, profile, "evidence", ProfileValues, out error))
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
            !TryAddProfile(args, profile, "compact", ProfileValues, out error))
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

        bool hasSearchBudgetOptions = !string.IsNullOrWhiteSpace(scope) || maxDocuments is not null;
        if (hasSearchBudgetOptions && normalizedOperation is not ("references" or "callers"))
        {
            return CommandBuildResult.Invalid("scope and maxDocuments are supported only for references and callers.");
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

        if (includeMetadata is not null && normalizedOperation is not ("definition" or "calls"))
        {
            return CommandBuildResult.Invalid("includeMetadata is supported only for definition and calls.");
        }

        AddOptionalFlag(args, "--include-metadata", includeMetadata);
        return CommandBuildResult.Valid(ToCliExactNavigationCommand(normalizedOperation), args);
    }

    public static CommandBuildResult Navigate(
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
        return ExactNavigation(
            operation, candidateId, file, line, column, project, excludeGenerated,
            resultProject, resultProjects, resultPath, resultPaths, resultKind, resultKinds,
            usageKind, usageKinds, groupBy, limit, scope, maxDocuments, includeMetadata);
    }

    public static CommandBuildResult Navigate(
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
        return Navigate(
            operation, candidateId, file, line, column, project, excludeGenerated,
            resultProject, resultProjects, resultPath, resultPaths, resultKind, resultKinds,
            usageKind, usageKinds, groupBy, limit, scope: null, maxDocuments: null, includeMetadata);
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
            !TryAddProfile(args, profile, "compact", ProfileValues, out error))
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
            !TryAddProfile(args, profile, "compact", ProfileValues, out error))
        {
            return CommandBuildResult.Invalid(error);
        }

        AddOptionalFlag(args, "--exclude-generated", excludeGenerated);
        AddOptionalFlag(args, "--include-snippets", includeSnippets);
        return CommandBuildResult.Valid("tests-for-diff", args);
    }

    public static CommandBuildResult Di(
        string? mode,
        string? query,
        string? candidateId,
        string? file,
        int? line,
        int? column,
        string? assumeKind,
        string[]? assumeKinds,
        string? match,
        bool? caseSensitive,
        string? candidatePolicy,
        string? minConfidence,
        bool? explainSelection,
        int? candidateLimit,
        string? project,
        string[]? projects,
        bool? excludeGenerated,
        int? registrationLimit,
        int? dependencyLimit,
        int? riskLimit,
        int? consumerLimit,
        int? depth,
        bool? includeOptions,
        bool? includeHostedServices,
        bool? includeRisks,
        bool? includeSnippets,
        int? snippetLines,
        string? profile)
    {
        if (mode is not ("graph" or "registrations" or "impact"))
        {
            return CommandBuildResult.Invalid("mode must be graph, registrations, or impact.");
        }

        List<string> args = [];
        string command;
        string? error;

        switch (mode)
        {
            case "graph":
                if (!string.IsNullOrWhiteSpace(query) ||
                    !string.IsNullOrWhiteSpace(candidateId) ||
                    HasAnyPosition(file, line, column) ||
                    HasFuzzySelectionOptions(assumeKind, assumeKinds, match, caseSensitive, candidateLimit, candidatePolicy, minConfidence, explainSelection) ||
                    consumerLimit is not null ||
                    depth is not null)
                {
                    return CommandBuildResult.Invalid("graph mode does not accept target, fuzzy, candidateLimit, consumerLimit, or depth inputs.");
                }

                if (!TryAddProjects(args, project, projects, out error))
                {
                    return CommandBuildResult.Invalid(error);
                }

                AddOptionalFlag(args, "--exclude-generated", excludeGenerated);
                if (!TryAddPositiveInt(args, "--registration-limit", registrationLimit, out error) ||
                    !TryAddPositiveInt(args, "--dependency-limit", dependencyLimit, out error) ||
                    !TryAddPositiveInt(args, "--risk-limit", riskLimit, out error))
                {
                    return CommandBuildResult.Invalid(error);
                }

                AddOptionalBoolValue(args, "--include-options", includeOptions);
                AddOptionalBoolValue(args, "--include-hosted-services", includeHostedServices);
                AddOptionalBoolValue(args, "--include-risks", includeRisks);
                command = "di-graph";
                break;

            case "registrations":
                if (consumerLimit is not null || riskLimit is not null || depth is not null)
                {
                    return CommandBuildResult.Invalid("registrations mode does not accept consumerLimit, riskLimit, or depth.");
                }

                if (includeOptions is not null || includeHostedServices is not null || includeRisks is not null)
                {
                    return CommandBuildResult.Invalid("registrations mode does not accept graph-only includeOptions, includeHostedServices, or includeRisks.");
                }

                if (!TryAddSymbolOrPositionInput(args, query, candidateId, file, line, column, out args, out error))
                {
                    return CommandBuildResult.Invalid(error);
                }

                bool registrationsSourcePosition = HasAnyPosition(file, line, column);
                if (registrationsSourcePosition)
                {
                    if (!TryAddSourcePositionOptions(args, "where-registered", assumeKind, assumeKinds, match, caseSensitive, project, projects, excludeGenerated, candidateLimit, candidatePolicy, minConfidence, explainSelection, out error))
                    {
                        return CommandBuildResult.Invalid(error);
                    }
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(candidateId) && HasQueryOnlyDiFuzzyOptions(assumeKind, assumeKinds, match, caseSensitive))
                    {
                        return CommandBuildResult.Invalid("candidateId mode does not accept assumeKind, assumeKinds, match, or caseSensitive.");
                    }

                    if (!TryAddFuzzyOptions(args, assumeKind, assumeKinds, match, caseSensitive, project, projects, excludeGenerated, limit: null, candidatePolicy, minConfidence, explainSelection, allowGroupPolicy: false, out error) ||
                        !TryAddPositiveInt(args, "--candidate-limit", candidateLimit, out error))
                    {
                        return CommandBuildResult.Invalid(error);
                    }
                }

                if (!TryAddPositiveInt(args, "--registration-limit", registrationLimit, out error) ||
                    !TryAddPositiveInt(args, "--dependency-limit", dependencyLimit, out error))
                {
                    return CommandBuildResult.Invalid(error);
                }

                command = "where-registered";
                break;

            case "impact":
                if (includeOptions is not null || includeHostedServices is not null || includeRisks is not null)
                {
                    return CommandBuildResult.Invalid("impact mode does not accept graph-only includeOptions, includeHostedServices, or includeRisks.");
                }

                if (!TryAddSymbolOrPositionInput(args, query, candidateId, file, line, column, out args, out error))
                {
                    return CommandBuildResult.Invalid(error);
                }

                bool impactSourcePosition = HasAnyPosition(file, line, column);
                if (impactSourcePosition)
                {
                    if (!TryAddSourcePositionOptions(args, "di-impact", assumeKind, assumeKinds, match, caseSensitive, project, projects, excludeGenerated, candidateLimit, candidatePolicy, minConfidence, explainSelection, out error))
                    {
                        return CommandBuildResult.Invalid(error);
                    }
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(candidateId) && HasQueryOnlyDiFuzzyOptions(assumeKind, assumeKinds, match, caseSensitive))
                    {
                        return CommandBuildResult.Invalid("candidateId mode does not accept assumeKind, assumeKinds, match, or caseSensitive.");
                    }

                    if (!TryAddFuzzyOptions(args, assumeKind, assumeKinds, match, caseSensitive, project, projects, excludeGenerated, limit: null, candidatePolicy, minConfidence, explainSelection, allowGroupPolicy: false, out error) ||
                        !TryAddPositiveInt(args, "--candidate-limit", candidateLimit, out error))
                    {
                        return CommandBuildResult.Invalid(error);
                    }
                }

                if (!TryAddPositiveInt(args, "--registration-limit", registrationLimit, out error) ||
                    !TryAddPositiveInt(args, "--dependency-limit", dependencyLimit, out error) ||
                    !TryAddPositiveInt(args, "--risk-limit", riskLimit, out error) ||
                    !TryAddPositiveInt(args, "--consumer-limit", consumerLimit, out error) ||
                    !TryAddNonNegativeInt(args, "--depth", depth, out error))
                {
                    return CommandBuildResult.Invalid(error);
                }

                command = "di-impact";
                break;

            default:
                return CommandBuildResult.Invalid("mode must be graph, registrations, or impact.");
        }

        AddOptionalFlag(args, "--include-snippets", includeSnippets);
        if (!TryAddNonNegativeInt(args, "--snippet-lines", snippetLines, out error))
        {
            return CommandBuildResult.Invalid(error);
        }

        if (profile is not null && string.IsNullOrWhiteSpace(profile))
        {
            return CommandBuildResult.Invalid("profile must be one of: compact, evidence, full.");
        }

        string effectiveProfile = profile ?? "compact";
        if (!TryAddAllowedValue(args, "--profile", effectiveProfile, ProfileValues, out error))
        {
            return CommandBuildResult.Invalid(error);
        }

        return CommandBuildResult.Valid(command, args);
    }

    public static CommandBuildResult Routes(
        string? mode,
        string? route,
        string[]? routes,
        string[]? endpointKinds,
        string? auth,
        string? project,
        string[]? projects,
        bool? excludeGenerated,
        int? routeLimit,
        int? evidenceLimit,
        bool? includeSnippets,
        int? snippetLines,
        string? profile)
    {
        if (mode is not ("map" or "impact"))
        {
            return CommandBuildResult.Invalid("mode must be map or impact.");
        }

        List<string> args = [];
        string? error;
        string command;

        if (mode == "map")
        {
            if (route is not null)
            {
                return CommandBuildResult.Invalid("map mode does not accept route; use routes.");
            }

            string? invalidKind = endpointKinds?.FirstOrDefault(value => string.IsNullOrWhiteSpace(value) || !EndpointKindValues.Contains(value, StringComparer.Ordinal));
            if (invalidKind is not null)
            {
                return CommandBuildResult.Invalid($"endpointKinds values must be one of: {string.Join(", ", EndpointKindValues)}.");
            }

            if (auth is not null && !RouteAuthValues.Contains(auth, StringComparer.Ordinal))
            {
                return CommandBuildResult.Invalid($"auth must be one of: {string.Join(", ", RouteAuthValues)}.");
            }

            if (!TryAddProjects(args, project, projects, out error) ||
                !TryAddPositiveInt(args, "--route-limit", routeLimit, out error) ||
                !TryAddPositiveInt(args, "--evidence-limit", evidenceLimit, out error) ||
                !TryAddAllowedValue(args, "--auth", auth, RouteAuthValues, out error))
            {
                return CommandBuildResult.Invalid(error);
            }

            AddOptionalRepeated(args, "--route", NormalizeValues(routes));
            AddOptionalRepeated(args, "--endpoint-kind", NormalizeValues(endpointKinds));
            AddOptionalFlag(args, "--exclude-generated", excludeGenerated);
            command = "route-map";
        }
        else
        {
            if (string.IsNullOrWhiteSpace(route))
            {
                return CommandBuildResult.Invalid("impact mode requires a nonblank route.");
            }

            if (routes is not null || endpointKinds is not null || auth is not null)
            {
                return CommandBuildResult.Invalid("impact mode accepts route only; routes, endpointKinds, and auth are map-only.");
            }

            if (!TryAddProjects(args, project, projects, out error) ||
                !TryAddPositiveInt(args, "--route-limit", routeLimit, out error) ||
                !TryAddPositiveInt(args, "--evidence-limit", evidenceLimit, out error))
            {
                return CommandBuildResult.Invalid(error);
            }

            args.Add("--route");
            args.Add(route.Trim());
            AddOptionalFlag(args, "--exclude-generated", excludeGenerated);
            command = "route-impact";
        }

        AddOptionalFlag(args, "--include-snippets", includeSnippets);
        if (!TryAddNonNegativeInt(args, "--snippet-lines", snippetLines, out error))
        {
            return CommandBuildResult.Invalid(error);
        }

        if (profile is not null && string.IsNullOrWhiteSpace(profile))
        {
            return CommandBuildResult.Invalid("profile must be one of: compact, evidence, full.");
        }

        if (!TryAddAllowedValue(args, "--profile", profile ?? "compact", ProfileValues, out error))
        {
            return CommandBuildResult.Invalid(error);
        }

        return CommandBuildResult.Valid(command, args);
    }

    public static CommandBuildResult Options(
        string? mode,
        string? query,
        string? project,
        string[]? projects,
        bool? excludeGenerated,
        int? optionLimit,
        int? consumerLimit,
        int? bindingLimit,
        int? evidenceLimit,
        bool? includeSnippets,
        int? snippetLines,
        string? profile)
    {
        if (mode is not ("graph" or "impact"))
        {
            return CommandBuildResult.Invalid("mode must be graph or impact.");
        }

        if (mode == "impact" && string.IsNullOrWhiteSpace(query))
        {
            return CommandBuildResult.Invalid("impact mode requires a nonblank query.");
        }

        List<string> args = [];
        string? error;
        if (!TryAddProjects(args, project, projects, out error) ||
            !TryAddPositiveInt(args, "--option-limit", optionLimit, out error) ||
            !TryAddPositiveInt(args, "--consumer-limit", consumerLimit, out error) ||
            !TryAddPositiveInt(args, "--binding-limit", bindingLimit, out error) ||
            !TryAddPositiveInt(args, "--evidence-limit", evidenceLimit, out error))
        {
            return CommandBuildResult.Invalid(error);
        }

        AddOptionalValue(args, "--query", query);
        AddOptionalFlag(args, "--exclude-generated", excludeGenerated);
        AddOptionalFlag(args, "--include-snippets", includeSnippets);
        if (!TryAddNonNegativeInt(args, "--snippet-lines", snippetLines, out error))
        {
            return CommandBuildResult.Invalid(error);
        }

        if (profile is not null && string.IsNullOrWhiteSpace(profile))
        {
            return CommandBuildResult.Invalid("profile must be one of: compact, evidence, full.");
        }

        if (!TryAddAllowedValue(args, "--profile", profile ?? "compact", ProfileValues, out error))
        {
            return CommandBuildResult.Invalid(error);
        }

        return CommandBuildResult.Valid(mode == "graph" ? "options-graph" : "config-impact", args);
    }

    public static CommandBuildResult Messages(
        string? mode,
        string? query,
        string? candidateId,
        string? file,
        int? line,
        int? column,
        string? assumeKind,
        string[]? assumeKinds,
        string? match,
        bool? caseSensitive,
        string? candidatePolicy,
        string? minConfidence,
        bool? explainSelection,
        string? project,
        string[]? projects,
        bool? excludeGenerated,
        int? candidateLimit,
        int? handlerLimit,
        int? callSiteLimit,
        int? evidenceLimit,
        bool? includeSnippets,
        int? snippetLines,
        string? profile)
    {
        if (mode is not ("handlers" or "flow"))
        {
            return CommandBuildResult.Invalid("mode must be handlers or flow.");
        }

        if (mode == "handlers" && callSiteLimit is not null)
        {
            return CommandBuildResult.Invalid("handlers mode does not accept callSiteLimit.");
        }

        List<string> args = [];
        string command = mode == "handlers" ? "where-handled" : "message-flow";
        string? error;
        if (!TryAddSymbolOrPositionInput(args, query, candidateId, file, line, column, out args, out error))
        {
            return CommandBuildResult.Invalid(error);
        }

        bool sourcePosition = HasAnyPosition(file, line, column);
        if (sourcePosition)
        {
            if (!TryAddSourcePositionOptions(args, command, assumeKind, assumeKinds, match, caseSensitive, project, projects, excludeGenerated, candidateLimit, candidatePolicy, minConfidence, explainSelection, out error))
            {
                return CommandBuildResult.Invalid(error);
            }
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(candidateId) && HasQueryOnlyDiFuzzyOptions(assumeKind, assumeKinds, match, caseSensitive))
            {
                return CommandBuildResult.Invalid("candidateId mode does not accept assumeKind, assumeKinds, match, or caseSensitive.");
            }

            if (match is not null && !MatchValues.Contains(match, StringComparer.Ordinal) ||
                candidatePolicy is not null && !MessageCandidatePolicyValues.Contains(candidatePolicy, StringComparer.Ordinal) ||
                minConfidence is not null && !MinConfidenceValues.Contains(minConfidence, StringComparer.Ordinal))
            {
                return CommandBuildResult.Invalid("match, candidatePolicy, and minConfidence must use their exact allowed values.");
            }

            if (!TryAddSingleOrMany(args, "--assume-kind", assumeKind, assumeKinds, "assumeKind", "assumeKinds", out error) ||
                !TryAddProjects(args, project, projects, out error) ||
                !TryAddAllowedValue(args, "--match", match, MatchValues, out error) ||
                !TryAddAllowedValue(args, "--candidate-policy", candidatePolicy, MessageCandidatePolicyValues, out error) ||
                !TryAddAllowedValue(args, "--min-confidence", minConfidence, MinConfidenceValues, out error))
            {
                return CommandBuildResult.Invalid(error);
            }

            AddOptionalFlag(args, "--case-sensitive", caseSensitive);
            AddOptionalFlag(args, "--exclude-generated", excludeGenerated);
            AddOptionalFlag(args, "--explain-selection", explainSelection);
        }

        if (!TryAddPositiveInt(args, "--candidate-limit", sourcePosition ? null : candidateLimit, out error) ||
            !TryAddPositiveInt(args, "--handler-limit", handlerLimit, out error) ||
            !TryAddPositiveInt(args, "--call-site-limit", mode == "flow" ? callSiteLimit : null, out error) ||
            !TryAddPositiveInt(args, "--evidence-limit", evidenceLimit, out error) ||
            !TryAddNonNegativeInt(args, "--snippet-lines", snippetLines, out error))
        {
            return CommandBuildResult.Invalid(error);
        }

        AddOptionalFlag(args, "--include-snippets", includeSnippets);

        if (profile is not null && string.IsNullOrWhiteSpace(profile))
        {
            return CommandBuildResult.Invalid("profile must be one of: compact, evidence, full.");
        }

        if (!TryAddAllowedValue(args, "--profile", profile ?? "compact", ProfileValues, out error))
        {
            return CommandBuildResult.Invalid(error);
        }

        return CommandBuildResult.Valid(command, args);
    }

    public static CommandBuildResult Ef(
        string? mode,
        string? entity,
        string? dbcontext,
        string? query,
        string? candidateId,
        string? file,
        int? line,
        int? column,
        string? assumeKind,
        string[]? assumeKinds,
        string? match,
        bool? caseSensitive,
        string? candidatePolicy,
        string? minConfidence,
        bool? explainSelection,
        string? project,
        string[]? projects,
        bool? excludeGenerated,
        int? candidateLimit,
        int? entityLimit,
        int? querySiteLimit,
        int? evidenceLimit,
        bool? includeSnippets,
        int? snippetLines,
        string? profile)
    {
        if (mode is not ("model" or "impact"))
        {
            return CommandBuildResult.Invalid("mode must be model or impact.");
        }

        List<string> args = [];
        string? error;
        string command;
        bool sourcePosition = false;

        if (mode == "model")
        {
            if (query is not null || candidateId is not null || file is not null || line is not null || column is not null ||
                assumeKind is not null || assumeKinds is not null || match is not null || caseSensitive is not null ||
                candidatePolicy is not null || minConfidence is not null || explainSelection is not null || candidateLimit is not null)
            {
                return CommandBuildResult.Invalid("model mode accepts entity/dbcontext filters, not selected-target or fuzzy fields.");
            }

            AddOptionalValue(args, "--entity", entity);
            AddOptionalValue(args, "--dbcontext", dbcontext);
            if (!TryAddProjects(args, project, projects, out error))
            {
                return CommandBuildResult.Invalid(error);
            }

            AddOptionalFlag(args, "--exclude-generated", excludeGenerated);
            command = "ef-model";
        }
        else
        {
            if (entity is not null || dbcontext is not null)
            {
                return CommandBuildResult.Invalid("impact mode does not accept entity or dbcontext filters.");
            }

            if (!TryAddSymbolOrPositionInput(args, query, candidateId, file, line, column, out args, out error))
            {
                return CommandBuildResult.Invalid(error);
            }

            sourcePosition = HasAnyPosition(file, line, column);
            if (sourcePosition)
            {
                if (!TryAddSourcePositionOptions(args, "entity-impact", assumeKind, assumeKinds, match, caseSensitive, project, projects, excludeGenerated, candidateLimit, candidatePolicy, minConfidence, explainSelection, out error))
                {
                    return CommandBuildResult.Invalid(error);
                }
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(candidateId) && HasQueryOnlyDiFuzzyOptions(assumeKind, assumeKinds, match, caseSensitive))
                {
                    return CommandBuildResult.Invalid("candidateId mode does not accept assumeKind, assumeKinds, match, or caseSensitive.");
                }

                if (match is not null && !MatchValues.Contains(match, StringComparer.Ordinal) ||
                    candidatePolicy is not null && !MessageCandidatePolicyValues.Contains(candidatePolicy, StringComparer.Ordinal) ||
                    minConfidence is not null && !MinConfidenceValues.Contains(minConfidence, StringComparer.Ordinal))
                {
                    return CommandBuildResult.Invalid("match, candidatePolicy, and minConfidence must use their exact allowed values.");
                }

                if (!TryAddSingleOrMany(args, "--assume-kind", assumeKind, assumeKinds, "assumeKind", "assumeKinds", out error) ||
                    !TryAddProjects(args, project, projects, out error) ||
                    !TryAddAllowedValue(args, "--match", match, MatchValues, out error) ||
                    !TryAddAllowedValue(args, "--candidate-policy", candidatePolicy, MessageCandidatePolicyValues, out error) ||
                    !TryAddAllowedValue(args, "--min-confidence", minConfidence, MinConfidenceValues, out error))
                {
                    return CommandBuildResult.Invalid(error);
                }

                AddOptionalFlag(args, "--case-sensitive", caseSensitive);
                AddOptionalFlag(args, "--exclude-generated", excludeGenerated);
                AddOptionalFlag(args, "--explain-selection", explainSelection);
            }

            command = "entity-impact";
        }

        if (!TryAddPositiveInt(args, "--candidate-limit", mode == "impact" && !sourcePosition ? candidateLimit : null, out error) ||
            !TryAddPositiveInt(args, "--entity-limit", entityLimit, out error) ||
            !TryAddPositiveInt(args, "--query-site-limit", querySiteLimit, out error) ||
            !TryAddPositiveInt(args, "--evidence-limit", evidenceLimit, out error) ||
            !TryAddNonNegativeInt(args, "--snippet-lines", snippetLines, out error))
        {
            return CommandBuildResult.Invalid(error);
        }

        AddOptionalFlag(args, "--include-snippets", includeSnippets);

        if (profile is not null && string.IsNullOrWhiteSpace(profile))
        {
            return CommandBuildResult.Invalid("profile must be one of: compact, evidence, full.");
        }

        if (!TryAddAllowedValue(args, "--profile", profile ?? "compact", ProfileValues, out error))
        {
            return CommandBuildResult.Invalid(error);
        }

        return CommandBuildResult.Valid(command, args);
    }

    public static CommandBuildResult Packages(
        string? mode,
        string? package,
        string[]? namespaces,
        string? project,
        string[]? projects,
        bool? includeTests,
        bool? excludeGenerated,
        int? usageLimit,
        int? referenceLimit,
        string? profile)
    {
        if (mode is not ("usage" or "impact"))
        {
            return CommandBuildResult.Invalid("mode must be usage or impact.");
        }

        if (string.IsNullOrWhiteSpace(package))
        {
            return CommandBuildResult.Invalid("package is required and must be nonblank.");
        }

        List<string> args = ["--package", package.Trim()];
        AddOptionalRepeated(args, "--namespace", NormalizeValues(namespaces));
        if (!TryAddProjects(args, project, projects, out string? error) ||
            !TryAddPositiveInt(args, "--usage-limit", usageLimit, out error) ||
            !TryAddPositiveInt(args, "--reference-limit", referenceLimit, out error))
        {
            return CommandBuildResult.Invalid(error);
        }

        AddOptionalBoolValue(args, "--include-tests", includeTests);
        AddOptionalFlag(args, "--exclude-generated", excludeGenerated);
        if (profile is not null && string.IsNullOrWhiteSpace(profile))
        {
            return CommandBuildResult.Invalid("profile must be one of: compact, evidence, full.");
        }

        if (!TryAddAllowedValue(args, "--profile", profile ?? "compact", ProfileValues, out error))
        {
            return CommandBuildResult.Invalid(error);
        }

        return CommandBuildResult.Valid(mode == "usage" ? "package-usage" : "package-impact", args);
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
            !TryAddProfile(args, profile, "evidence", ProfileValues, out error))
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

        if (requests.Value.GetArrayLength() == 1)
        {
            string? command = TryGetBatchRequestCommand(requests.Value[0]);
            string guidance = GetSingleBatchRequestGuidance(command);
            return CommandBuildResult.Invalid($"navlyn_batch requires at least two requests. {guidance}");
        }

        JsonObject input = [];
        if (defaults is not null && defaults.Value.ValueKind != JsonValueKind.Null)
        {
            input["defaults"] = JsonNode.Parse(defaults.Value.GetRawText());
        }

        input["requests"] = JsonNode.Parse(requests.Value.GetRawText());
        return CommandBuildResult.Valid("batch", [], input.ToJsonString(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    private static string? TryGetBatchRequestCommand(JsonElement request)
    {
        return request.ValueKind == JsonValueKind.Object &&
            request.TryGetProperty("command", out JsonElement command) &&
            command.ValueKind == JsonValueKind.String
                ? command.GetString()?.Trim()
                : null;
    }

    private static string GetSingleBatchRequestGuidance(string? command)
    {
        string? focusedTool = command switch
        {
            "overview" or "repo-graph" => "navlyn_workspace_summary",
            "diagnostics" => "navlyn_diagnostics",
            "symbols" or "symbols-in" or "symbol-at" or "find" or "resolve-target" => "navlyn_target",
            "outline" => "navlyn_file_outline",
            "symbol-info" or "definition" or "references" or "implementations" or "type-hierarchy" or "callers" or "calls" => "navlyn_navigate",
            "symbol-source" => "navlyn_read",
            "where-used" => "navlyn_navigate",
            "about" => "navlyn_read",
            "related" or "impact" => "navlyn_impact",
            "entrypoints" or "framework-entrypoints" => "navlyn_entrypoints",
            "review-diff" => "navlyn_review",
            "context-pack" => "navlyn_context_pack",
            "public-api-diff" => "navlyn_public_api_diff",
            "tests-for-symbol" => "navlyn_tests_for_symbol",
            "tests-for-diff" => "navlyn_tests_for_diff",
            "route-map" or "route-impact" => "navlyn_routes",
            "di-graph" or "where-registered" or "di-impact" => "navlyn_di",
            "options-graph" or "config-impact" => "navlyn_options",
            "where-handled" or "message-flow" => "navlyn_messages",
            "ef-model" or "entity-impact" => "navlyn_ef",
            "package-usage" or "package-impact" => "navlyn_packages",
            _ => null
        };

        if (focusedTool is not null)
        {
            return $"Use focused MCP tool {focusedTool} directly for this single fact.";
        }

        if (!string.IsNullOrWhiteSpace(command))
        {
            return $"Use the matching Navlyn CLI command `navlyn {command}` directly instead of batch.";
        }

        return "Use the matching Navlyn CLI command directly instead of batch.";
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

    private static bool HasAnyTargetInput(string? candidateId, string? file, int? line, int? column)
    {
        return !string.IsNullOrWhiteSpace(candidateId) || HasAnyPosition(file, line, column);
    }

    private static bool HasAnyPosition(string? file, int? line, int? column)
    {
        return !string.IsNullOrWhiteSpace(file) || line is not null || column is not null;
    }

    private static bool HasQueryOnlyDiFuzzyOptions(string? assumeKind, string[]? assumeKinds, string? match, bool? caseSensitive)
    {
        return !string.IsNullOrWhiteSpace(assumeKind) ||
            NormalizeValues(assumeKinds).Count > 0 ||
            !string.IsNullOrWhiteSpace(match) ||
            caseSensitive is not null;
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

    private static bool TryAddDiagnosticSeverities(
        List<string> args,
        string? severity,
        string[]? severities,
        out string? error)
    {
        if (severity is not null && severities is not null)
        {
            error = "severity and severities are mutually exclusive.";
            return false;
        }

        IReadOnlyList<string> values = severity is not null ? [severity] : severities ?? [];
        string? invalid = values.FirstOrDefault(value => !DiagnosticSeverityValues.Contains(value, StringComparer.Ordinal));
        if (invalid is not null)
        {
            error = $"severity values must be one of: {string.Join(", ", DiagnosticSeverityValues)}.";
            return false;
        }

        AddOptionalRepeated(args, "--severity", values);
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

    private static bool TryAddProfile(
        List<string> args,
        string? profile,
        string defaultProfile,
        IReadOnlyList<string> allowed,
        out string? error)
    {
        if (profile is not null && string.IsNullOrWhiteSpace(profile))
        {
            error = $"profile must be one of: {string.Join(", ", allowed)}.";
            return false;
        }

        return TryAddAllowedValue(args, "--profile", profile ?? defaultProfile, allowed, out error);
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
    public string? ResultCommand { get; init; }

    public static CommandBuildResult Valid(
        string command,
        IReadOnlyList<string> arguments,
        string? standardInput = null,
        string? resultCommand = null)
    {
        return new CommandBuildResult(IsValid: true, command, arguments, standardInput, Error: null)
        {
            ResultCommand = resultCommand
        };
    }

    public static CommandBuildResult Invalid(string? error)
    {
        return new CommandBuildResult(IsValid: false, Command: null, Arguments: [], StandardInput: null, error ?? "Invalid tool arguments.");
    }
}

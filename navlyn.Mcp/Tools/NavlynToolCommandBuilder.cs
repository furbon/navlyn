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
    private static readonly string[] SnippetPolicyValues = ["none", "signature", "line", "block"];
    private static readonly string[] EntrypointModeValues = ["symbol", "framework"];
    private static readonly string[] ProfileValues = ["compact", "evidence", "full"];

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
            !TryAddNonNegativeInt(args, "--snippet-lines", snippetLines, out error))
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

        if (!TryAddAllowedValue(args, "--goal", goal, GoalValues, out error) ||
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

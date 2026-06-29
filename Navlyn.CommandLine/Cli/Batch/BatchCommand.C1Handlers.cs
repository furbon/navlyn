using Navlyn.Cli.OutputProfiles;
using Navlyn.Diagnostics;
using Navlyn.Diffs;
using Navlyn.ReviewPacks;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static partial class BatchCommand
{
    private static async Task<BatchRequestResult> ExecuteReviewPackAsync(
        LoadedWorkspace loadedWorkspace,
        BatchDefaults defaults,
        BatchRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetStringArray(request.Payload, "pack", out IReadOnlyList<string> packValues, out BatchError? error, allowStringValue: true) ||
            !TryGetOptionalString(request.Payload, "scope", out string? scopeValue, out error) ||
            !TryGetDiffRequest(request.Payload, out DiffRequest diffRequest, out error) ||
            !TryGetProjectFilters(request.Payload, defaults, out IReadOnlyList<string> projectFilters, out error) ||
            !TryGetEffectiveExcludeGenerated(request.Payload, defaults, out bool excludeGenerated, out error) ||
            !TryGetOptionalInt(request.Payload, "findingLimit", out int? findingLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "evidenceLimit", out int? evidenceLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "symbolLimit", out int? symbolLimit, out error) ||
            !TryGetOptionalInt(request.Payload, "fileLimit", out int? fileLimit, out error) ||
            !TryGetOptionalBool(request.Payload, "includeSnippets", out bool? includeSnippets, out error) ||
            !TryGetOptionalInt(request.Payload, "snippetLines", out int? snippetLines, out error) ||
            !TryGetOptionalString(request.Payload, "architectureConfig", out string? architectureConfig, out error) ||
            !TryGetProfile(request.Payload, out string profile, out error))
        {
            return request.Failed(error!);
        }

        string scope = scopeValue ?? "diff";
        if (scope is not ("diff" or "workspace"))
        {
            return request.Failed(DiagnosticIds.ParseError, "scope must be diff or workspace.");
        }

        if (!TryNormalizeReviewPacksForBatch(packValues, out IReadOnlyList<string> packs, out error))
        {
            return request.Failed(error!);
        }

        if (scope == "workspace" &&
            (request.Payload.TryGetProperty("base", out _) ||
                request.Payload.TryGetProperty("head", out _) ||
                request.Payload.TryGetProperty("staged", out _)))
        {
            return request.Failed(DiagnosticIds.InvalidDiffOptions, "base, head, and staged are only valid with scope diff.");
        }

        int effectiveFindingLimit = findingLimit ?? 100;
        int effectiveEvidenceLimit = evidenceLimit ?? 5;
        int effectiveSymbolLimit = symbolLimit ?? 100;
        int effectiveFileLimit = fileLimit ?? 100;
        int effectiveSnippetLines = snippetLines ?? 1;
        BatchError? limitError =
            GetPositiveBatchLimitError("findingLimit", effectiveFindingLimit) ??
            GetPositiveBatchLimitError("evidenceLimit", effectiveEvidenceLimit) ??
            GetPositiveBatchLimitError("symbolLimit", effectiveSymbolLimit) ??
            GetPositiveBatchLimitError("fileLimit", effectiveFileLimit) ??
            GetNonNegativeBatchLimitError("snippetLines", effectiveSnippetLines);
        if (limitError is not null)
        {
            return request.Failed(limitError);
        }

        ProjectFilterResolutionResult projectResult = new ProjectFilterResolver().ResolveMany(loadedWorkspace.Solution, projectFilters);
        if (projectResult.Error is not null)
        {
            return request.Failed(projectResult.Error);
        }

        IReadOnlyList<ReviewPackProjectFilter>? projectOutputs = projectResult.AppliedFilters.Count == 0
            ? null
            : projectResult.AppliedFilters.Select(filter => new ReviewPackProjectFilter(filter.Filter, filter.Name, filter.Path, filter.TargetFramework)).ToArray();

        ReviewPackExecutionResult result = await new ReviewPackResolver().ResolveAsync(
            loadedWorkspace,
            projectResult.Projects,
            projectOutputs,
            new ReviewPackOptions(
                packs,
                scope,
                scope == "diff" ? diffRequest : null,
                projectFilters,
                excludeGenerated,
                effectiveFindingLimit,
                effectiveEvidenceLimit,
                effectiveSymbolLimit,
                effectiveFileLimit,
                includeSnippets ?? false,
                effectiveSnippetLines,
                architectureConfig),
            cancellationToken);

        return result.Error is not null
            ? request.Failed(result.Error.DiagnosticId, result.Error.Message)
            : request.Success(OutputProfile.Format(loadedWorkspace, "review-pack", profile, result.Result!, new
            {
                packs,
                scope,
                projectFilters,
                excludeGenerated,
                findingLimit = effectiveFindingLimit,
                evidenceLimit = effectiveEvidenceLimit,
                symbolLimit = effectiveSymbolLimit,
                fileLimit = effectiveFileLimit,
                includeSnippets = includeSnippets ?? false,
                snippetLines = effectiveSnippetLines,
                architectureConfig
            }));
    }

    private static bool TryNormalizeReviewPacksForBatch(
        IReadOnlyList<string> values,
        out IReadOnlyList<string> packs,
        out BatchError? error)
    {
        IReadOnlyList<string> split = values.Count == 0
            ? ["all"]
            : [.. values.SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))];

        if (split.Contains("all", StringComparer.Ordinal) && split.Count > 1)
        {
            packs = [];
            error = BatchError.FromDiagnostic(DiagnosticIds.ParseError, "pack all cannot be combined with other pack values.");
            return false;
        }

        if (split.Contains("all", StringComparer.Ordinal) || split.Count == 0)
        {
            packs = ReviewPackNames.Ordered;
            error = null;
            return true;
        }

        string? invalid = split.FirstOrDefault(value => !ReviewPackNames.IsKnown(value));
        if (invalid is not null)
        {
            packs = [];
            error = BatchError.FromDiagnostic(DiagnosticIds.ParseError, $"Unknown review pack: {invalid}.");
            return false;
        }

        packs = [.. ReviewPackNames.Ordered.Where(known => split.Contains(known, StringComparer.Ordinal))];
        error = null;
        return true;
    }
}

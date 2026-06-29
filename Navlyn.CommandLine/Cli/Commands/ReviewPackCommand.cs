using System.CommandLine;
using Navlyn.Cli.OutputProfiles;
using Navlyn.Diagnostics;
using Navlyn.Diffs;
using Navlyn.ReviewPacks;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class ReviewPackCommand
{
    private const int DefaultFindingLimit = 100;
    private const int DefaultEvidenceLimit = 5;
    private const int DefaultSymbolLimit = 100;
    private const int DefaultFileLimit = 100;
    private const int DefaultSnippetLines = 1;

    public static Command Create()
    {
        Option<string[]> packOption = new("--pack")
        {
            Description = "Review pack to run: async, disposal, nullability, security, architecture, or all. Can be specified more than once.",
            AllowMultipleArgumentsPerToken = true
        };
        Option<string> scopeOption = new("--scope")
        {
            Description = "Review scope: diff or workspace.",
            DefaultValueFactory = _ => "diff"
        };
        scopeOption.AcceptOnlyFromAmong("diff", "workspace");
        Option<string?> baseOption = DiffCommandSupport.CreateBaseOption();
        Option<string?> headOption = DiffCommandSupport.CreateHeadOption();
        Option<bool> stagedOption = DiffCommandSupport.CreateStagedOption();
        Option<bool> includeUnstagedOption = DiffCommandSupport.CreateIncludeUnstagedOption();
        Option<string[]> projectOption = SharedOptions.CreateProjectFiltersOption();
        Option<bool> excludeGeneratedOption = SharedOptions.CreateExcludeGeneratedOption();
        Option<int?> findingLimitOption = new("--finding-limit") { Description = $"Maximum number of findings to return. Defaults to {DefaultFindingLimit}." };
        Option<int?> evidenceLimitOption = new("--evidence-limit") { Description = $"Maximum number of evidence locations per finding. Defaults to {DefaultEvidenceLimit}." };
        Option<int?> symbolLimitOption = new("--symbol-limit") { Description = $"Maximum number of symbol-driven facts per pack. Defaults to {DefaultSymbolLimit}." };
        Option<int?> fileLimitOption = new("--file-limit") { Description = $"Maximum number of files to inspect. Defaults to {DefaultFileLimit}." };
        Option<bool> includeSnippetsOption = DiffCommandSupport.CreateIncludeSnippetsOption();
        Option<int?> snippetLinesOption = DiffCommandSupport.CreateSnippetLinesOption();
        Option<string?> architectureConfigOption = new("--architecture-config") { Description = "Optional .navlyn.yml architecture rule config path." };
        Option<string> profileOption = OutputProfile.CreateOption();

        return WorkspaceCommand.Create(
            "review-pack",
            "Run deterministic evidence-backed review packs for agent code review.",
            [packOption, scopeOption, baseOption, headOption, stagedOption, includeUnstagedOption, projectOption, excludeGeneratedOption, findingLimitOption, evidenceLimitOption, symbolLimitOption, fileLimitOption, includeSnippetsOption, snippetLinesOption, architectureConfigOption, profileOption],
            (workspace, parseResult, cancellationToken) => ExecuteAsync(
                workspace,
                parseResult.GetValue(packOption) ?? [],
                parseResult.GetValue(scopeOption)!,
                parseResult.GetValue(baseOption),
                parseResult.GetValue(headOption),
                parseResult.GetValue(stagedOption),
                parseResult.GetValue(includeUnstagedOption),
                parseResult.GetValue(projectOption) ?? [],
                parseResult.GetValue(excludeGeneratedOption),
                parseResult.GetValue(findingLimitOption),
                parseResult.GetValue(evidenceLimitOption),
                parseResult.GetValue(symbolLimitOption),
                parseResult.GetValue(fileLimitOption),
                parseResult.GetValue(includeSnippetsOption),
                parseResult.GetValue(snippetLinesOption),
                parseResult.GetValue(architectureConfigOption),
                parseResult.GetValue(profileOption)!,
                cancellationToken));
    }

    private static async Task<int> ExecuteAsync(
        LoadedWorkspace workspace,
        IReadOnlyList<string> packValues,
        string scope,
        string? baseRef,
        string? headRef,
        bool staged,
        bool includeUnstaged,
        IReadOnlyList<string> projectFilters,
        bool excludeGenerated,
        int? findingLimit,
        int? evidenceLimit,
        int? symbolLimit,
        int? fileLimit,
        bool includeSnippets,
        int? snippetLines,
        string? architectureConfig,
        string profile,
        CancellationToken cancellationToken)
    {
        if (!TryNormalizePacks(packValues, out IReadOnlyList<string> packs, out int exitCode) ||
            !TryValidateScope(scope, baseRef, headRef, staged, out exitCode) ||
            !TryCreateLimits(findingLimit, evidenceLimit, symbolLimit, fileLimit, snippetLines, out int effectiveFindingLimit, out int effectiveEvidenceLimit, out int effectiveSymbolLimit, out int effectiveFileLimit, out int effectiveSnippetLines, out exitCode) ||
            !DiffCommandSupport.TryCreateProjectContext(workspace, projectFilters, out var projects, out var diffProjectOutputs, out exitCode))
        {
            return exitCode;
        }

        DiffRequest? request = null;
        if (scope == "diff" &&
            !DiffCommandSupport.TryCreateRequest(baseRef, headRef, staged, includeUnstaged, out request, out exitCode))
        {
            return exitCode;
        }

        IReadOnlyList<ReviewPackProjectFilter>? projectOutputs = diffProjectOutputs is null
            ? null
            : [.. diffProjectOutputs.Select(filter => new ReviewPackProjectFilter(filter.Filter, filter.Name, filter.Path, filter.TargetFramework))];

        ReviewPackExecutionResult result = await new ReviewPackResolver().ResolveAsync(
            workspace,
            projects,
            projectOutputs,
            new ReviewPackOptions(
                packs,
                scope,
                request,
                projectFilters,
                excludeGenerated,
                effectiveFindingLimit,
                effectiveEvidenceLimit,
                effectiveSymbolLimit,
                effectiveFileLimit,
                includeSnippets,
                effectiveSnippetLines,
                architectureConfig),
            cancellationToken);

        if (result.Error is not null)
        {
            DiagnosticReporter.WriteError(result.Error.DiagnosticId, result.Error.Message);
            return result.Error.ExitCode;
        }

        ConsoleJsonWriter.Write(OutputProfile.Format(workspace, "review-pack", profile, result.Result!, new
        {
            packs,
            scope,
            baseRef,
            headRef,
            staged,
            includeUnstaged,
            projectFilters,
            excludeGenerated,
            findingLimit = effectiveFindingLimit,
            evidenceLimit = effectiveEvidenceLimit,
            symbolLimit = effectiveSymbolLimit,
            fileLimit = effectiveFileLimit,
            includeSnippets,
            snippetLines = effectiveSnippetLines,
            architectureConfig
        }));
        return ExitCodes.Success;
    }

    internal static bool TryNormalizePacks(
        IReadOnlyList<string> values,
        out IReadOnlyList<string> packs,
        out int exitCode)
    {
        IReadOnlyList<string> split = values.Count == 0
            ? ["all"]
            : [.. values.SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))];

        if (split.Count == 0)
        {
            split = ["all"];
        }

        if (split.Contains("all", StringComparer.Ordinal) && split.Count > 1)
        {
            DiagnosticReporter.WriteError(DiagnosticIds.ParseError, "--pack all cannot be combined with other pack values.");
            packs = [];
            exitCode = ExitCodes.UsageError;
            return false;
        }

        if (split.Contains("all", StringComparer.Ordinal))
        {
            packs = ReviewPackNames.Ordered;
            exitCode = ExitCodes.Success;
            return true;
        }

        string? invalid = split.FirstOrDefault(value => !ReviewPackNames.IsKnown(value));
        if (invalid is not null)
        {
            DiagnosticReporter.WriteError(DiagnosticIds.ParseError, $"Unknown review pack: {invalid}.");
            packs = [];
            exitCode = ExitCodes.UsageError;
            return false;
        }

        packs = [.. ReviewPackNames.Ordered.Where(known => split.Contains(known, StringComparer.Ordinal))];
        exitCode = ExitCodes.Success;
        return true;
    }

    private static bool TryValidateScope(string scope, string? baseRef, string? headRef, bool staged, out int exitCode)
    {
        exitCode = ExitCodes.Success;
        if (scope == "workspace" &&
            (!string.IsNullOrWhiteSpace(baseRef) || !string.IsNullOrWhiteSpace(headRef) || staged))
        {
            DiagnosticReporter.WriteError(DiagnosticIds.InvalidDiffOptions, "--base, --head, and --staged are only valid with --scope diff.");
            exitCode = ExitCodes.UsageError;
            return false;
        }

        return true;
    }

    private static bool TryCreateLimits(
        int? findingLimit,
        int? evidenceLimit,
        int? symbolLimit,
        int? fileLimit,
        int? snippetLines,
        out int effectiveFindingLimit,
        out int effectiveEvidenceLimit,
        out int effectiveSymbolLimit,
        out int effectiveFileLimit,
        out int effectiveSnippetLines,
        out int exitCode)
    {
        effectiveFindingLimit = findingLimit ?? DefaultFindingLimit;
        effectiveEvidenceLimit = evidenceLimit ?? DefaultEvidenceLimit;
        effectiveSymbolLimit = symbolLimit ?? DefaultSymbolLimit;
        effectiveFileLimit = fileLimit ?? DefaultFileLimit;
        effectiveSnippetLines = snippetLines ?? DefaultSnippetLines;

        return DiffCommandSupport.TryCreatePositiveOption("--finding-limit", effectiveFindingLimit, out exitCode) &&
            DiffCommandSupport.TryCreatePositiveOption("--evidence-limit", effectiveEvidenceLimit, out exitCode) &&
            DiffCommandSupport.TryCreatePositiveOption("--symbol-limit", effectiveSymbolLimit, out exitCode) &&
            DiffCommandSupport.TryCreatePositiveOption("--file-limit", effectiveFileLimit, out exitCode) &&
            DiffCommandSupport.TryCreateNonNegativeOption("--snippet-lines", effectiveSnippetLines, out exitCode);
    }
}

using System.CommandLine;
using Navlyn.Cli.OutputProfiles;
using Navlyn.Diagnostics;
using Navlyn.Entrypoints;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class FrameworkEntrypointsCommand
{
    private static readonly string[] DefaultFrameworks = ["aspnetcore", "test", "worker"];
    private static readonly string[] AllowedFrameworks = ["aspnetcore", "test", "worker", "azure-functions", "grpc", "mediatr", "messaging"];
    private const int DefaultLimit = 100;
    private const int DefaultEvidenceLimit = 5;

    public static Command Create()
    {
        Option<string[]> projectOption = SharedOptions.CreateProjectFiltersOption();
        Option<string[]> frameworkOption = new("--framework")
        {
            Description = "Framework to inspect: aspnetcore, test, worker, azure-functions, grpc, mediatr, or messaging. Can be specified more than once."
        };
        frameworkOption.AllowMultipleArgumentsPerToken = true;
        Option<string[]> entrypointKindOption = new("--entrypoint-kind")
        {
            Description = "Restrict results to an entrypoint kind. Can be specified more than once."
        };
        entrypointKindOption.AllowMultipleArgumentsPerToken = true;
        Option<bool> excludeGeneratedOption = SharedOptions.CreateExcludeGeneratedOption();
        Option<int?> limitOption = SharedOptions.CreateLimitOption();
        Option<int?> evidenceLimitOption = new("--evidence-limit")
        {
            Description = $"Maximum evidence items per entrypoint. Defaults to {DefaultEvidenceLimit}."
        };
        Option<bool> includeSnippetsOption = FuzzyCommandSupport.CreateIncludeSnippetsOption();
        Option<int?> snippetLinesOption = FuzzyCommandSupport.CreateSnippetLinesOption();
        Option<string> profileOption = OutputProfile.CreateOption();

        return WorkspaceCommand.Create(
            "framework-entrypoints",
            "Discover framework-aware .NET entrypoint declarations.",
            [projectOption, frameworkOption, entrypointKindOption, excludeGeneratedOption, limitOption, evidenceLimitOption, includeSnippetsOption, snippetLinesOption, profileOption],
            (workspace, parseResult, cancellationToken) => ExecuteAsync(
                workspace,
                parseResult.GetValue(projectOption) ?? [],
                parseResult.GetValue(frameworkOption) ?? [],
                parseResult.GetValue(entrypointKindOption) ?? [],
                parseResult.GetValue(excludeGeneratedOption),
                parseResult.GetValue(limitOption),
                parseResult.GetValue(evidenceLimitOption),
                parseResult.GetValue(includeSnippetsOption),
                parseResult.GetValue(snippetLinesOption),
                parseResult.GetValue(profileOption)!,
                cancellationToken));
    }

    private static async Task<int> ExecuteAsync(
        LoadedWorkspace workspace,
        IReadOnlyList<string> projectFilters,
        IReadOnlyList<string> frameworkFilters,
        IReadOnlyList<string> entrypointKindFilters,
        bool excludeGenerated,
        int? limit,
        int? evidenceLimit,
        bool includeSnippets,
        int? snippetLines,
        string profile,
        CancellationToken cancellationToken)
    {
        int effectiveLimit = limit ?? DefaultLimit;
        int effectiveEvidenceLimit = evidenceLimit ?? DefaultEvidenceLimit;
        int effectiveSnippetLines = snippetLines ?? FuzzyDiscoveryResolver.DefaultSnippetLines;
        if (!FuzzyCommandSupport.TryCreatePositiveOption("--limit", effectiveLimit, out int exitCode) ||
            !FuzzyCommandSupport.TryCreatePositiveOption("--evidence-limit", effectiveEvidenceLimit, out exitCode) ||
            !FuzzyCommandSupport.TryCreateNonNegativeOption("--snippet-lines", effectiveSnippetLines, out exitCode))
        {
            return exitCode;
        }

        if (!TryNormalizeFrameworks(frameworkFilters, out IReadOnlyList<string> frameworks))
        {
            return ExitCodes.UsageError;
        }

        ProjectFilterResolutionResult projectResult = new ProjectFilterResolver().ResolveMany(workspace.Solution, projectFilters);
        if (projectResult.Error is not null)
        {
            DiagnosticReporter.WriteError(projectResult.Error.DiagnosticId, projectResult.Error.Message);
            return projectResult.Error.ExitCode;
        }

        FrameworkEntrypointsResult result = await new FrameworkEntrypointDiscoveryResolver().DiscoverAsync(
            workspace,
            projectResult.Projects,
            projectResult.AppliedFilters.Count == 0
                ? null
                : projectResult.AppliedFilters.Select(filter => new FrameworkEntrypointProjectFilter(filter.Filter, filter.Name, filter.Path, filter.TargetFramework)).ToArray(),
            new FrameworkEntrypointOptions(frameworks, effectiveLimit, effectiveEvidenceLimit, includeSnippets, effectiveSnippetLines, excludeGenerated),
            cancellationToken);

        if (entrypointKindFilters.Count > 0)
        {
            HashSet<string> kinds = new(entrypointKindFilters.SelectMany(SplitValues), StringComparer.Ordinal);
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

        ConsoleJsonWriter.Write(OutputProfile.Format(workspace, "framework-entrypoints", profile, result, new
        {
            projectFilters,
            frameworks,
            entrypointKindFilters,
            excludeGenerated,
            limit = effectiveLimit,
            evidenceLimit = effectiveEvidenceLimit,
            includeSnippets,
            snippetLines = effectiveSnippetLines
        }));
        return ExitCodes.Success;
    }

    internal static bool TryNormalizeFrameworks(IReadOnlyList<string> filters, out IReadOnlyList<string> frameworks)
    {
        IReadOnlyList<string> values = filters.Count == 0
            ? DefaultFrameworks
            : [.. filters.SelectMany(SplitValues).Where(value => value.Length > 0)];

        HashSet<string> allowed = new(AllowedFrameworks, StringComparer.Ordinal);
        string? invalid = values.FirstOrDefault(value => !allowed.Contains(value));
        if (invalid is not null)
        {
            DiagnosticReporter.WriteError(DiagnosticIds.ParseError, $"Invalid --framework value '{invalid}'. Allowed values: {string.Join(", ", AllowedFrameworks)}.");
            frameworks = [];
            return false;
        }

        frameworks = [.. values.Distinct(StringComparer.Ordinal).OrderBy(FrameworkOrder).ThenBy(value => value, StringComparer.Ordinal)];
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
}

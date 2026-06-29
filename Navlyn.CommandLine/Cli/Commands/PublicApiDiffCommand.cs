using System.CommandLine;
using Microsoft.CodeAnalysis;
using Navlyn.Cli.OutputProfiles;
using Navlyn.Diagnostics;
using Navlyn.PublicApi;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class PublicApiDiffCommand
{
    private const int DefaultSymbolLimit = 5000;
    private const int DefaultChangeLimit = 200;

    public static Command Create()
    {
        Option<string?> baseOption = DiffCommandSupport.CreateBaseOption();
        Option<string?> headOption = DiffCommandSupport.CreateHeadOption();
        Option<string[]> projectOption = SharedOptions.CreateProjectFiltersOption();
        Option<bool> excludeGeneratedOption = SharedOptions.CreateExcludeGeneratedOption();
        Option<bool> includeAdditionsOption = CreateDefaultTrueOption("--include-additions", "Include public API additions.");
        Option<bool> includeAttributesOption = CreateDefaultTrueOption("--include-attributes", "Include public attribute changes.");
        Option<int?> symbolLimitOption = DiffCommandSupport.CreateSymbolLimitOption(DefaultSymbolLimit);
        Option<int?> changeLimitOption = new("--change-limit")
        {
            Description = $"Maximum number of API changes to return. Defaults to {DefaultChangeLimit}."
        };
        Option<string> profileOption = OutputProfile.CreateOption();

        return WorkspaceCommand.Create(
            "public-api-diff",
            "Report source-level public API differences between Git refs.",
            [baseOption, headOption, projectOption, excludeGeneratedOption, includeAdditionsOption, includeAttributesOption, symbolLimitOption, changeLimitOption, profileOption],
            (workspace, parseResult, cancellationToken) => ExecuteAsync(
                workspace,
                parseResult.GetValue(baseOption),
                parseResult.GetValue(headOption),
                parseResult.GetValue(projectOption) ?? [],
                parseResult.GetValue(excludeGeneratedOption),
                parseResult.GetValue(includeAdditionsOption),
                parseResult.GetValue(includeAttributesOption),
                parseResult.GetValue(symbolLimitOption),
                parseResult.GetValue(changeLimitOption),
                parseResult.GetValue(profileOption)!,
                cancellationToken));
    }

    private static async Task<int> ExecuteAsync(
        LoadedWorkspace workspace,
        string? baseRef,
        string? headRef,
        IReadOnlyList<string> projectFilters,
        bool excludeGenerated,
        bool includeAdditions,
        bool includeAttributes,
        int? symbolLimit,
        int? changeLimit,
        string profile,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(baseRef))
        {
            DiagnosticReporter.WriteError(DiagnosticIds.InvalidDiffOptions, "--base is required.");
            return ExitCodes.UsageError;
        }

        int effectiveSymbolLimit = symbolLimit ?? DefaultSymbolLimit;
        int effectiveChangeLimit = changeLimit ?? DefaultChangeLimit;
        if (!DiffCommandSupport.TryCreatePositiveOption("--symbol-limit", effectiveSymbolLimit, out int exitCode) ||
            !DiffCommandSupport.TryCreatePositiveOption("--change-limit", effectiveChangeLimit, out exitCode))
        {
            return exitCode;
        }

        ProjectFilterResolutionResult projectResult = new ProjectFilterResolver().ResolveMany(workspace.Solution, projectFilters);
        if (projectResult.Error is not null)
        {
            DiagnosticReporter.WriteError(projectResult.Error.DiagnosticId, projectResult.Error.Message);
            return projectResult.Error.ExitCode;
        }

        IReadOnlyList<PublicApiProjectFilter>? projectOutputs = projectResult.AppliedFilters.Count == 0
            ? null
            : projectResult.AppliedFilters.Select(filter => new PublicApiProjectFilter(
                Filter: filter.Filter,
                Name: filter.Name,
                Path: filter.Path,
                TargetFramework: filter.TargetFramework)).ToArray();

        PublicApiDiffExecutionResult result = await new PublicApiDiffResolver().ResolveAsync(
            workspace,
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

        if (result.Error is not null)
        {
            DiagnosticReporter.WriteError(result.Error.DiagnosticId, result.Error.Message);
            return result.Error.ExitCode;
        }

        ConsoleJsonWriter.Write(OutputProfile.Format(workspace, "public-api-diff", profile, result.Result!, new
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
        return ExitCodes.Success;
    }

    private static Option<bool> CreateDefaultTrueOption(string name, string description)
    {
        return new Option<bool>(name)
        {
            Description = description,
            DefaultValueFactory = _ => true
        };
    }
}


using System.CommandLine;
using Microsoft.CodeAnalysis;
using Navlyn.Cli.OutputProfiles;
using Navlyn.Diagnostics;
using Navlyn.Diffs;
using Navlyn.Paths;
using Navlyn.Testing;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class TestsForDiffCommand
{
    private const int DefaultSymbolLimit = 50;
    private const int DefaultTestLimit = 100;
    private const int DefaultReferenceLimit = 200;
    private const int DefaultSnippetLines = 1;

    public static Command Create()
    {
        Option<string?> baseOption = DiffCommandSupport.CreateBaseOption();
        Option<string?> headOption = DiffCommandSupport.CreateHeadOption();
        Option<bool> stagedOption = DiffCommandSupport.CreateStagedOption();
        Option<bool> includeUnstagedOption = DiffCommandSupport.CreateIncludeUnstagedOption();
        Option<string[]> projectOption = SharedOptions.CreateProjectFiltersOption();
        Option<string[]> testProjectOption = TestsForSymbolCommand.CreateTestProjectOption();
        Option<bool> excludeGeneratedOption = SharedOptions.CreateExcludeGeneratedOption();
        Option<int?> symbolLimitOption = DiffCommandSupport.CreateSymbolLimitOption(DefaultSymbolLimit);
        Option<int?> testLimitOption = new("--test-limit") { Description = $"Maximum test candidates. Defaults to {DefaultTestLimit}." };
        Option<int?> referenceLimitOption = new("--reference-limit") { Description = $"Maximum references scanned per changed symbol. Defaults to {DefaultReferenceLimit}." };
        Option<bool> includeSnippetsOption = FuzzyCommandSupport.CreateIncludeSnippetsOption();
        Option<int?> snippetLinesOption = FuzzyCommandSupport.CreateSnippetLinesOption();
        Option<string> profileOption = OutputProfile.CreateOption();

        return WorkspaceCommand.Create(
            "tests-for-diff",
            "Find tests related to changed symbols in a diff.",
            [baseOption, headOption, stagedOption, includeUnstagedOption, projectOption, testProjectOption, excludeGeneratedOption, symbolLimitOption, testLimitOption, referenceLimitOption, includeSnippetsOption, snippetLinesOption, profileOption],
            (workspace, parseResult, cancellationToken) => ExecuteAsync(
                workspace,
                parseResult.GetValue(baseOption),
                parseResult.GetValue(headOption),
                parseResult.GetValue(stagedOption),
                parseResult.GetValue(includeUnstagedOption),
                parseResult.GetValue(projectOption) ?? [],
                parseResult.GetValue(testProjectOption) ?? [],
                parseResult.GetValue(excludeGeneratedOption),
                parseResult.GetValue(symbolLimitOption),
                parseResult.GetValue(testLimitOption),
                parseResult.GetValue(referenceLimitOption),
                parseResult.GetValue(includeSnippetsOption),
                parseResult.GetValue(snippetLinesOption),
                parseResult.GetValue(profileOption)!,
                cancellationToken));
    }

    private static async Task<int> ExecuteAsync(
        LoadedWorkspace workspace,
        string? baseRef,
        string? headRef,
        bool staged,
        bool includeUnstaged,
        IReadOnlyList<string> projectFilters,
        IReadOnlyList<string> testProjectFilters,
        bool excludeGenerated,
        int? symbolLimit,
        int? testLimit,
        int? referenceLimit,
        bool includeSnippets,
        int? snippetLines,
        string profile,
        CancellationToken cancellationToken)
    {
        int effectiveSymbolLimit = symbolLimit ?? DefaultSymbolLimit;
        int effectiveTestLimit = testLimit ?? DefaultTestLimit;
        int effectiveReferenceLimit = referenceLimit ?? DefaultReferenceLimit;
        int effectiveSnippetLines = snippetLines ?? DefaultSnippetLines;
        if (!DiffCommandSupport.TryCreatePositiveOption("--symbol-limit", effectiveSymbolLimit, out int exitCode) ||
            !FuzzyCommandSupport.TryCreatePositiveOption("--test-limit", effectiveTestLimit, out exitCode) ||
            !FuzzyCommandSupport.TryCreatePositiveOption("--reference-limit", effectiveReferenceLimit, out exitCode) ||
            !FuzzyCommandSupport.TryCreateNonNegativeOption("--snippet-lines", effectiveSnippetLines, out exitCode) ||
            !DiffCommandSupport.TryCreateRequest(baseRef, headRef, staged, includeUnstaged, out DiffRequest request, out exitCode) ||
            !TestsForSymbolCommand.ResolveProjects(workspace, projectFilters, testProjectFilters, out IReadOnlyList<Project> projects, out IReadOnlyList<Project>? testProjects, out IReadOnlyList<TestProjectFilter>? projectOutputs, out exitCode))
        {
            return exitCode;
        }

        string? repositoryRoot = PathDisplay.FindRepositoryRoot(workspace.FullPath);
        if (repositoryRoot is null)
        {
            DiagnosticReporter.WriteError(DiagnosticIds.GitRepositoryNotFound, "Git repository root was not found for tests-for-diff.");
            return ExitCodes.UsageError;
        }

        DiffReadResult diffResult = await new GitDiffProvider().ReadAsync(repositoryRoot, request, cancellationToken);
        if (diffResult.Error is not null)
        {
            return DiffCommandSupport.WriteError(diffResult.Error);
        }

        ChangedSymbolsResolution changedSymbols = await new ChangedSymbolResolver().ResolveAsync(
            workspace,
            diffResult.Diff!,
            projects,
            excludeGenerated,
            effectiveSymbolLimit,
            cancellationToken);

        TestImpactResolution impact = await new TestImpactResolver().ResolveForChangedSymbolsAsync(
            workspace,
            projects,
            testProjects,
            changedSymbols.ChangedSymbols.Symbols,
            new TestImpactOptions(effectiveTestLimit, effectiveReferenceLimit, includeSnippets, effectiveSnippetLines, excludeGenerated),
            cancellationToken);

        TestsForDiffResult result = new(
            Workspace: workspace.DisplayPath,
            Kind: workspace.Kind,
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
        ConsoleJsonWriter.Write(OutputProfile.Format(workspace, "tests-for-diff", profile, result, new
        {
            baseRef,
            headRef,
            staged,
            includeUnstaged,
            projectFilters,
            testProjectFilters,
            excludeGenerated,
            symbolLimit = effectiveSymbolLimit,
            testLimit = effectiveTestLimit,
            referenceLimit = effectiveReferenceLimit,
            includeSnippets,
            snippetLines = effectiveSnippetLines
        }));
        return ExitCodes.Success;
    }
}


using System.CommandLine;
using Microsoft.CodeAnalysis;
using Navlyn.Cli.OutputProfiles;
using Navlyn.Diagnostics;
using Navlyn.Symbols;
using Navlyn.Testing;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class TestsForSymbolCommand
{
    private const int DefaultCandidateLimit = 20;
    private const int DefaultTestLimit = 50;
    private const int DefaultReferenceLimit = 200;
    private const int DefaultSnippetLines = 1;

    public static Command Create()
    {
        Option<string?> queryOption = new("--query") { Description = "Symbol query." };
        Option<string?> candidateIdOption = FuzzyCommandSupport.CreateCandidateIdOption();
        Option<FileInfo?> fileOption = new("--file") { Description = "Source file for source-position mode." };
        Option<int?> lineOption = new("--line") { Description = "1-based source line for source-position mode." };
        Option<int?> columnOption = new("--column") { Description = "1-based source column for source-position mode." };
        Option<string[]> assumeKindOption = FuzzyCommandSupport.CreateAssumeKindOption();
        Option<string> matchOption = FuzzyCommandSupport.CreateMatchOption();
        Option<bool> caseSensitiveOption = SharedOptions.CreateCaseSensitiveOption();
        Option<string[]> projectOption = SharedOptions.CreateProjectFiltersOption();
        Option<string[]> testProjectOption = CreateTestProjectOption();
        Option<bool> excludeGeneratedOption = SharedOptions.CreateExcludeGeneratedOption();
        Option<int?> candidateLimitOption = new("--candidate-limit") { Description = $"Maximum fuzzy candidates. Defaults to {DefaultCandidateLimit}." };
        Option<int?> testLimitOption = new("--test-limit") { Description = $"Maximum test candidates. Defaults to {DefaultTestLimit}." };
        Option<int?> referenceLimitOption = new("--reference-limit") { Description = $"Maximum references scanned. Defaults to {DefaultReferenceLimit}." };
        Option<bool> includeSnippetsOption = FuzzyCommandSupport.CreateIncludeSnippetsOption();
        Option<int?> snippetLinesOption = FuzzyCommandSupport.CreateSnippetLinesOption();
        Option<string> candidatePolicyOption = FuzzyCommandSupport.CreateCandidatePolicyOption("fail");
        Option<string> minConfidenceOption = FuzzyCommandSupport.CreateMinConfidenceOption("medium");
        Option<bool> explainSelectionOption = FuzzyCommandSupport.CreateExplainSelectionOption();
        Option<string> profileOption = OutputProfile.CreateOption();

        return WorkspaceCommand.Create(
            "tests-for-symbol",
            "Find tests related to a selected symbol.",
            [
                queryOption,
                candidateIdOption,
                fileOption,
                lineOption,
                columnOption,
                assumeKindOption,
                matchOption,
                caseSensitiveOption,
                projectOption,
                testProjectOption,
                excludeGeneratedOption,
                candidateLimitOption,
                testLimitOption,
                referenceLimitOption,
                includeSnippetsOption,
                snippetLinesOption,
                candidatePolicyOption,
                minConfidenceOption,
                explainSelectionOption,
                profileOption
            ],
            (workspace, parseResult, cancellationToken) => ExecuteAsync(
                workspace,
                parseResult.GetValue(queryOption),
                parseResult.GetValue(candidateIdOption),
                parseResult.GetValue(fileOption),
                parseResult.GetValue(lineOption),
                parseResult.GetValue(columnOption),
                parseResult.GetValue(assumeKindOption) ?? [],
                parseResult.GetValue(matchOption)!,
                parseResult.GetValue(caseSensitiveOption),
                parseResult.GetValue(projectOption) ?? [],
                parseResult.GetValue(testProjectOption) ?? [],
                parseResult.GetValue(excludeGeneratedOption),
                parseResult.GetValue(candidateLimitOption),
                parseResult.GetValue(testLimitOption),
                parseResult.GetValue(referenceLimitOption),
                parseResult.GetValue(includeSnippetsOption),
                parseResult.GetValue(snippetLinesOption),
                parseResult.GetValue(candidatePolicyOption)!,
                parseResult.GetValue(minConfidenceOption)!,
                parseResult.GetValue(explainSelectionOption),
                parseResult.GetValue(profileOption)!,
                cancellationToken));
    }

    private static async Task<int> ExecuteAsync(
        LoadedWorkspace workspace,
        string? query,
        string? candidateId,
        FileInfo? file,
        int? line,
        int? column,
        IReadOnlyList<string> assumeKinds,
        string match,
        bool caseSensitive,
        IReadOnlyList<string> projectFilters,
        IReadOnlyList<string> testProjectFilters,
        bool excludeGenerated,
        int? candidateLimit,
        int? testLimit,
        int? referenceLimit,
        bool includeSnippets,
        int? snippetLines,
        string candidatePolicy,
        string minConfidence,
        bool explainSelection,
        string profile,
        CancellationToken cancellationToken)
    {
        int effectiveCandidateLimit = candidateLimit ?? DefaultCandidateLimit;
        int effectiveTestLimit = testLimit ?? DefaultTestLimit;
        int effectiveReferenceLimit = referenceLimit ?? DefaultReferenceLimit;
        int effectiveSnippetLines = snippetLines ?? DefaultSnippetLines;
        if (!ValidateLimits(effectiveCandidateLimit, effectiveTestLimit, effectiveReferenceLimit, effectiveSnippetLines, out int exitCode))
        {
            return exitCode;
        }

        bool hasQuery = !string.IsNullOrWhiteSpace(query);
        bool hasCandidate = !string.IsNullOrWhiteSpace(candidateId);
        bool hasPosition = file is not null || line is not null || column is not null;
        if ((hasQuery ? 1 : 0) + (hasCandidate ? 1 : 0) + (hasPosition ? 1 : 0) != 1 || hasPosition && (file is null || line is null || column is null))
        {
            DiagnosticReporter.WriteError(DiagnosticIds.ParseError, "Specify exactly one tests-for-symbol input mode: --query, --candidate-id, or --file with --line and --column.");
            return ExitCodes.UsageError;
        }

        if (!ResolveProjects(workspace, projectFilters, testProjectFilters, out IReadOnlyList<Project> projects, out IReadOnlyList<Project>? testProjects, out IReadOnlyList<TestProjectFilter>? projectOutputs, out exitCode))
        {
            return exitCode;
        }

        TestSubject? subject;
        FuzzySelectionSection? selection = null;
        TestSelectionInput selectionInput;
        if (hasPosition)
        {
            if (projectFilters.Count > 1)
            {
                DiagnosticReporter.WriteError(DiagnosticIds.ParseError, "Source-position mode accepts at most one --project filter.");
                return ExitCodes.UsageError;
            }

            string? projectFilter = projectFilters.Count == 0 ? null : projectFilters[0];
            if (!ProjectFilterCommand.TryResolveSingleProject(
                workspace,
                projectFilter,
                out Project? sourceProject,
                out _,
                out int sourceExitCode))
            {
                return sourceExitCode;
            }

            SourceSymbolResolutionResult sourceResult = await new SourceSymbolResolver().ResolveAsync(
                workspace.Solution,
                file!,
                line!.Value,
                column!.Value,
                sourceProject,
                excludeGenerated,
                cancellationToken);
            if (sourceResult.Error is not null)
            {
                DiagnosticReporter.WriteError(sourceResult.Error.DiagnosticId, sourceResult.Error.Message);
                return sourceResult.Error.ExitCode;
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
            if (!FuzzyCommandSupport.TryCreateSelection(
                workspace,
                query,
                candidateId,
                assumeKinds,
                match,
                caseSensitive,
                projectFilters,
                excludeGenerated,
                effectiveCandidateLimit,
                candidatePolicy,
                minConfidence,
                explainSelection,
                allowGroupPolicy: false,
                out FuzzyQueryOptions fuzzyOptions,
                out projects,
                out _,
                out exitCode))
            {
                return exitCode;
            }

            FuzzyCandidateResolution resolution = await new FuzzyDiscoveryResolver().ResolveCandidatesForSelectionAsync(projects, fuzzyOptions, cancellationToken);
            if (resolution.Error is not null)
            {
                DiagnosticReporter.WriteError(resolution.Error.DiagnosticId, resolution.Error.Message);
                return resolution.Error.ExitCode;
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
            selectionInput = new TestSelectionInput(hasCandidate ? "candidateId" : "query", query?.Trim(), candidateId?.Trim(), File: null, Line: null, Column: null);
        }

        TestImpactResolution impact = subject is null
            ? new TestImpactResolution([], new TestCandidatesSection(0, effectiveTestLimit, Truncated: false, Candidates: []), ["no-selected-symbol"])
            : await new TestImpactResolver().ResolveForSymbolAsync(
                workspace,
                projects,
                testProjects,
                subject,
                new TestImpactOptions(effectiveTestLimit, effectiveReferenceLimit, includeSnippets, effectiveSnippetLines, excludeGenerated),
                cancellationToken);

        TestsForSymbolResult result = new(
            Workspace: workspace.DisplayPath,
            Kind: workspace.Kind,
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
        ConsoleJsonWriter.Write(OutputProfile.Format(workspace, "tests-for-symbol", profile, result, new
        {
            inputMode = hasQuery ? "query" : hasCandidate ? "candidateId" : "sourcePosition",
            projectFilters,
            testProjectFilters,
            excludeGenerated,
            candidateLimit = effectiveCandidateLimit,
            testLimit = effectiveTestLimit,
            referenceLimit = effectiveReferenceLimit,
            includeSnippets,
            snippetLines = effectiveSnippetLines
        }));
        return ExitCodes.Success;
    }

    internal static Option<string[]> CreateTestProjectOption()
    {
        return new Option<string[]>("--test-project")
        {
            Description = "Restrict test discovery to a test project name or repository-relative .csproj path. Can be specified more than once.",
            AllowMultipleArgumentsPerToken = true
        };
    }

    internal static bool ResolveProjects(
        LoadedWorkspace workspace,
        IReadOnlyList<string> projectFilters,
        IReadOnlyList<string> testProjectFilters,
        out IReadOnlyList<Project> projects,
        out IReadOnlyList<Project>? testProjects,
        out IReadOnlyList<TestProjectFilter>? projectOutputs,
        out int exitCode)
    {
        ProjectFilterResolutionResult projectResult = new ProjectFilterResolver().ResolveMany(workspace.Solution, projectFilters);
        if (projectResult.Error is not null)
        {
            DiagnosticReporter.WriteError(projectResult.Error.DiagnosticId, projectResult.Error.Message);
            projects = [];
            testProjects = null;
            projectOutputs = null;
            exitCode = projectResult.Error.ExitCode;
            return false;
        }

        ProjectFilterResolutionResult testProjectResult = new ProjectFilterResolver().ResolveMany(workspace.Solution, testProjectFilters);
        if (testProjectResult.Error is not null)
        {
            DiagnosticReporter.WriteError(testProjectResult.Error.DiagnosticId, testProjectResult.Error.Message);
            projects = [];
            testProjects = null;
            projectOutputs = null;
            exitCode = testProjectResult.Error.ExitCode;
            return false;
        }

        projects = projectResult.Projects;
        testProjects = testProjectFilters.Count == 0 ? null : testProjectResult.Projects;
        projectOutputs = projectResult.AppliedFilters.Count == 0
            ? null
            : projectResult.AppliedFilters.Select(filter => new TestProjectFilter(filter.Filter, filter.Name, filter.Path, filter.TargetFramework)).ToArray();
        exitCode = ExitCodes.Success;
        return true;
    }

    internal static bool ValidateLimits(int candidateLimit, int testLimit, int referenceLimit, int snippetLines, out int exitCode)
    {
        return FuzzyCommandSupport.TryCreatePositiveOption("--candidate-limit", candidateLimit, out exitCode) &&
            FuzzyCommandSupport.TryCreatePositiveOption("--test-limit", testLimit, out exitCode) &&
            FuzzyCommandSupport.TryCreatePositiveOption("--reference-limit", referenceLimit, out exitCode) &&
            FuzzyCommandSupport.TryCreateNonNegativeOption("--snippet-lines", snippetLines, out exitCode);
    }
}


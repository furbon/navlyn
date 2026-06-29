using Microsoft.CodeAnalysis;
using Navlyn.Diffs;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.Testing;

internal sealed class TestImpactResolver
{
    public async Task<TestImpactResolution> ResolveForSymbolAsync(
        LoadedWorkspace workspace,
        IReadOnlyList<Project> projects,
        IReadOnlyList<Project>? explicitTestProjects,
        TestSubject subject,
        TestImpactOptions options,
        CancellationToken cancellationToken)
    {
        TestDiscoveryResult discovery = await new TestDiscoveryResolver().DiscoverAsync(
            workspace.Solution.Projects.ToArray(),
            explicitTestProjects,
            options.ExcludeGenerated,
            options.IncludeSnippets,
            options.SnippetLines,
            cancellationToken);

        IReadOnlyList<TestImpactCandidate> candidates = subject.Path is null || subject.Line is null || subject.Column is null
            ? RankByConvention(discovery.Candidates, subject, [])
            : await RankByReferencesAndConventionAsync(
                workspace,
                projects,
                discovery.Candidates,
                subject,
                options,
                cancellationToken);

        return CreateResolution(discovery.TestProjects, candidates, options.TestLimit);
    }

    public async Task<TestImpactResolution> ResolveForChangedSymbolsAsync(
        LoadedWorkspace workspace,
        IReadOnlyList<Project> projects,
        IReadOnlyList<Project>? explicitTestProjects,
        IReadOnlyList<DiffChangedSymbol> changedSymbols,
        TestImpactOptions options,
        CancellationToken cancellationToken)
    {
        TestDiscoveryResult discovery = await new TestDiscoveryResolver().DiscoverAsync(
            workspace.Solution.Projects.ToArray(),
            explicitTestProjects,
            options.ExcludeGenerated,
            options.IncludeSnippets,
            options.SnippetLines,
            cancellationToken);

        List<TestImpactCandidate> all = [];
        foreach (DiffChangedSymbol symbol in changedSymbols)
        {
            TestSubject subject = new(
                symbol.Name,
                symbol.Kind,
                symbol.Container,
                symbol.Facts,
                symbol.Path,
                symbol.Line,
                symbol.Column,
                symbol.EndLine,
                symbol.EndColumn);
            all.AddRange(await RankByReferencesAndConventionAsync(
                workspace,
                projects,
                discovery.Candidates,
                subject,
                options,
                cancellationToken));
        }

        return CreateResolution(discovery.TestProjects, Deduplicate(all), options.TestLimit);
    }

    private static async Task<IReadOnlyList<TestImpactCandidate>> RankByReferencesAndConventionAsync(
        LoadedWorkspace workspace,
        IReadOnlyList<Project> projects,
        IReadOnlyList<TestImpactCandidate> discovered,
        TestSubject subject,
        TestImpactOptions options,
        CancellationToken cancellationToken)
    {
        List<TestEvidence> referenceEvidence = [];
        if (subject.Path is not null && subject.Line is not null && subject.Column is not null)
        {
            ReferencesResolutionResult references = await new ReferencesResolver().ResolveAsync(
                workspace.Solution,
                new FileInfo(subject.Path),
                subject.Line.Value,
                subject.Column.Value,
                FindProject(projects, subject.Facts.Project),
                options.ExcludeGenerated,
                cancellationToken);

            if (references.Error is null)
            {
                referenceEvidence = [.. references.Resolution!.References
                    .Take(options.ReferenceLimit)
                    .Select(reference => new TestEvidence(
                        "reference",
                        reference.Path,
                        reference.Line,
                        reference.Column,
                        reference.EndLine,
                        reference.EndColumn,
                        ["references-selected-symbol"]))];
            }
        }

        return RankByConvention(discovered, subject, referenceEvidence);
    }

    private static IReadOnlyList<TestImpactCandidate> RankByConvention(
        IReadOnlyList<TestImpactCandidate> discovered,
        TestSubject subject,
        IReadOnlyList<TestEvidence> referenceEvidence)
    {
        List<TestImpactCandidate> ranked = [];
        foreach (TestImpactCandidate candidate in discovered)
        {
            List<string> reasons = [.. candidate.ReasonCodes];
            List<TestEvidence> evidence = [.. candidate.Evidence];
            string confidence = "low";
            int score = candidate.Score;

            IReadOnlyList<TestEvidence> candidateReferenceEvidence = [.. referenceEvidence
                .Where(item => item.Path == candidate.Path)];
            if (candidateReferenceEvidence.Count > 0)
            {
                reasons.Add("direct-reference-to-symbol");
                evidence.AddRange(candidateReferenceEvidence);
                confidence = "high";
                score += 80;
            }

            if (MatchesName(candidate, subject))
            {
                reasons.Add("test-class-name-convention");
                confidence = confidence == "high" ? "high" : "medium";
                score += 40;
            }

            if (candidate.Path.Contains(subject.Name, StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add("test-file-name-convention");
                confidence = confidence == "high" ? "high" : "medium";
                score += 30;
            }

            if (reasons.Count == candidate.ReasonCodes.Count)
            {
                continue;
            }

            ranked.Add(candidate with
            {
                Confidence = confidence,
                Score = score,
                ReasonCodes = [.. reasons.Distinct(StringComparer.Ordinal).OrderBy(ReasonPriority).ThenBy(reason => reason, StringComparer.Ordinal)],
                Evidence = [.. evidence
                    .GroupBy(item => (item.Kind, item.Path, item.Line, item.Column, item.EndLine, item.EndColumn))
                    .Select(group => group.First())
                    .OrderBy(item => item.Path, StringComparer.Ordinal)
                    .ThenBy(item => item.Line)
                    .ThenBy(item => item.Column)]
            });
        }

        return Deduplicate(ranked);
    }

    private static TestImpactResolution CreateResolution(
        IReadOnlyList<TestProjectInfo> testProjects,
        IReadOnlyList<TestImpactCandidate> candidates,
        int limit)
    {
        IReadOnlyList<TestImpactCandidate> ordered = [.. candidates
            .OrderBy(candidate => ConfidencePriority(candidate.Confidence))
            .ThenByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Project.Path, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Path, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Line)
            .ThenBy(candidate => candidate.Column)
            .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)];

        return new TestImpactResolution(
            TestProjects: testProjects,
            Tests: new TestCandidatesSection(
                TotalCandidates: ordered.Count,
                Limit: limit,
                Truncated: ordered.Count > limit,
                Candidates: [.. ordered.Take(limit)]),
            Warnings: ordered.Count == 0 ? ["no-related-tests-found"] : []);
    }

    private static IReadOnlyList<TestImpactCandidate> Deduplicate(IReadOnlyList<TestImpactCandidate> candidates)
    {
        return [.. candidates
            .GroupBy(candidate => (candidate.Project.Path, candidate.Path, candidate.Line, candidate.Column, candidate.Name))
            .Select(group => group
                .OrderBy(candidate => ConfidencePriority(candidate.Confidence))
                .ThenByDescending(candidate => candidate.Score)
                .First())];
    }

    private static bool MatchesName(TestImpactCandidate candidate, TestSubject subject)
    {
        string subjectName = subject.Kind == "NamedType"
            ? subject.Name
            : subject.Container?.Split('.').LastOrDefault() ?? subject.Name;
        return candidate.Container?.Contains(subjectName, StringComparison.OrdinalIgnoreCase) == true ||
            candidate.Name.Contains(subjectName, StringComparison.OrdinalIgnoreCase);
    }

    private static Project? FindProject(IReadOnlyList<Project> projects, string? projectName)
    {
        return projectName is null ? null : projects.FirstOrDefault(project => project.Name == projectName);
    }

    private static int ConfidencePriority(string confidence)
    {
        return confidence switch
        {
            "high" => 0,
            "medium" => 1,
            _ => 2
        };
    }

    private static int ReasonPriority(string reason)
    {
        return reason switch
        {
            "direct-reference-to-symbol" => 0,
            "test-method-attribute" => 1,
            "test-class-name-convention" => 2,
            "test-file-name-convention" => 3,
            _ => 10
        };
    }
}

internal sealed record TestImpactResolution(
    IReadOnlyList<TestProjectInfo> TestProjects,
    TestCandidatesSection Tests,
    IReadOnlyList<string> Warnings);


using Microsoft.CodeAnalysis;
using Navlyn.Diagnostics;
using Navlyn.Workspaces;

namespace Navlyn.Symbols;

internal sealed class CandidateTargetResolver
{
    public async Task<CandidateTargetResolutionResult> ResolveAsync(
        Solution solution,
        IReadOnlyList<Project> projects,
        string candidateId,
        bool excludeGenerated,
        CancellationToken cancellationToken)
    {
        string normalizedCandidateId = candidateId.Trim();
        if (!FuzzyCandidateIdentity.TryParseCandidateId(normalizedCandidateId))
        {
            return CandidateTargetResolutionResult.Failed(
                DiagnosticIds.InvalidCandidateId,
                $"Invalid candidate id: {normalizedCandidateId}.",
                ExitCodes.UsageError);
        }

        FuzzyCandidateResolution resolution = await new FuzzyDiscoveryResolver().ResolveCandidatesForSelectionAsync(
            projects,
            new FuzzyQueryOptions(
                Query: normalizedCandidateId,
                AssumeKinds: [],
                Match: "smart",
                CaseSensitive: null,
                ExcludeGenerated: excludeGenerated,
                Limit: null,
                CandidateId: normalizedCandidateId),
            cancellationToken);

        if (resolution.Error is not null)
        {
            return CandidateTargetResolutionResult.Failed(
                resolution.Error.DiagnosticId,
                resolution.Error.Message,
                resolution.Error.ExitCode);
        }

        FuzzySymbolCandidate? candidate = resolution.SelectedCandidate;
        if (candidate is null)
        {
            return CandidateTargetResolutionResult.Failed(
                DiagnosticIds.CandidateIdNotFound,
                $"Candidate id was not found in the current workspace: {normalizedCandidateId}.",
                ExitCodes.UsageError);
        }

        return CandidateTargetResolutionResult.Succeeded(new CandidateTargetResolution(
            CandidateId: normalizedCandidateId,
            File: new FileInfo(candidate.Path),
            Line: candidate.Line,
            Column: candidate.Column,
            Project: FindCandidateProject(projects, candidate)));
    }

    private static Project? FindCandidateProject(IReadOnlyList<Project> projects, FuzzySymbolCandidate candidate)
    {
        string? projectName = candidate.Selector?.Project ?? candidate.Facts.Project;
        string? targetFramework = candidate.Selector?.TargetFramework;
        return projects.FirstOrDefault(project =>
            project.Name == projectName &&
            (targetFramework is null || ProjectContextFacts.GetTargetFramework(project) == targetFramework)) ??
            projects.FirstOrDefault(project => project.Name == projectName);
    }
}

internal sealed record CandidateTargetResolutionResult(CandidateTargetResolution? Resolution, SymbolNavigationError? Error)
{
    public static CandidateTargetResolutionResult Succeeded(CandidateTargetResolution resolution)
    {
        return new CandidateTargetResolutionResult(resolution, Error: null);
    }

    public static CandidateTargetResolutionResult Failed(int diagnosticId, string message, int exitCode)
    {
        return new CandidateTargetResolutionResult(
            Resolution: null,
            Error: new SymbolNavigationError(diagnosticId, message, exitCode));
    }
}

internal sealed record CandidateTargetResolution(
    string CandidateId,
    FileInfo File,
    int Line,
    int Column,
    Project? Project);

internal sealed record CandidateSelectionInput(string Mode, string CandidateId);

using Microsoft.CodeAnalysis;
using Navlyn.DependencyInjection;
using Navlyn.Diagnostics;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class DiCommandSupport
{
    public static async Task<DiSubjectResolution> ResolveSubjectAsync(
        LoadedWorkspace workspace,
        string commandName,
        string? query,
        string? candidateId,
        FileInfo? file,
        int? line,
        int? column,
        IReadOnlyList<string> assumeKinds,
        string match,
        bool caseSensitive,
        IReadOnlyList<string> projectFilters,
        bool excludeGenerated,
        int candidateLimit,
        string candidatePolicy,
        string minConfidence,
        bool explainSelection,
        CancellationToken cancellationToken)
    {
        bool hasQuery = !string.IsNullOrWhiteSpace(query);
        bool hasCandidate = !string.IsNullOrWhiteSpace(candidateId);
        bool hasPosition = file is not null || line is not null || column is not null;
        if ((hasQuery ? 1 : 0) + (hasCandidate ? 1 : 0) + (hasPosition ? 1 : 0) != 1 || hasPosition && (file is null || line is null || column is null))
        {
            return DiSubjectResolution.Failed(DiagnosticIds.ParseError, $"Specify exactly one {commandName} input mode: --query, --candidate-id, or --file with --line and --column.", ExitCodes.UsageError);
        }

        if (hasPosition)
        {
            SourceSymbolResolutionResult sourceResult = await new SourceSymbolResolver().ResolveAsync(
                workspace.Solution,
                file!,
                line!.Value,
                column!.Value,
                project: null,
                excludeGenerated,
                cancellationToken);
            if (sourceResult.Error is not null)
            {
                return DiSubjectResolution.Failed(sourceResult.Error.DiagnosticId, sourceResult.Error.Message, sourceResult.Error.ExitCode);
            }

            SourceSymbolResolution resolved = sourceResult.Resolution!;
            ISymbol symbol = resolved.Symbol is INamedTypeSymbol
                ? resolved.Symbol
                : resolved.Symbol.ContainingType ?? resolved.Symbol;
            if (symbol is not INamedTypeSymbol)
            {
                return DiSubjectResolution.Failed(DiagnosticIds.SymbolNotFoundAtPosition, "Selected symbol is not a type or a member contained by a type.", ExitCodes.UsageError);
            }

            return DiSubjectResolution.Succeeded(
                new DiSelectionInput("sourcePosition", Query: null, CandidateId: null, resolved.File, resolved.Line, resolved.Column),
                selection: null,
                subject: CreateSubject(symbol, resolved.ProjectName, excludeGenerated));
        }

        if (!FuzzyCommandSupport.TryCreateSelection(
            workspace,
            query,
            candidateId,
            assumeKinds,
            match,
            caseSensitive,
            projectFilters,
            excludeGenerated,
            candidateLimit,
            candidatePolicy,
            minConfidence,
            explainSelection,
            allowGroupPolicy: false,
            out FuzzyQueryOptions fuzzyOptions,
            out IReadOnlyList<Project> projects,
            out _,
            out int exitCode))
        {
            return DiSubjectResolution.Failed(null, null, exitCode);
        }

        FuzzyCandidateResolution resolution = await new FuzzyDiscoveryResolver().ResolveCandidatesForSelectionAsync(projects, fuzzyOptions, cancellationToken);
        if (resolution.Error is not null)
        {
            return DiSubjectResolution.Failed(resolution.Error.DiagnosticId, resolution.Error.Message, resolution.Error.ExitCode);
        }

        FuzzySymbolCandidate? selected = resolution.SelectedCandidate;
        DiTypeInfo? subject = selected is null
            ? null
            : new DiTypeInfo(
                selected.Name,
                selected.Kind,
                selected.Container,
                selected.Facts,
                selected.Path,
                selected.Line,
                selected.Column,
                selected.EndLine,
                selected.EndColumn);

        return DiSubjectResolution.Succeeded(
            new DiSelectionInput(hasCandidate ? "candidateId" : "query", query?.Trim(), candidateId?.Trim(), File: null, Line: null, Column: null),
            new DiSelectionSection(
                resolution.Confidence,
                Math.Min(resolution.Candidates.Count, candidateLimit),
                resolution.TotalCandidates,
                selected,
                [.. resolution.Candidates.Take(candidateLimit)],
                resolution.SelectionExplanation),
            subject);
    }

    private static DiTypeInfo CreateSubject(ISymbol symbol, string? projectName, bool excludeGenerated)
    {
        SymbolSourceLocation? location = SymbolNavigationFacts.GetSourceLocations(symbol, excludeGenerated).FirstOrDefault();
        return new DiTypeInfo(
            symbol.Name,
            symbol.Kind.ToString(),
            SymbolNavigationFacts.GetContainer(symbol),
            SymbolFactsBuilder.Create(symbol, projectName),
            location?.Path,
            location?.Line,
            location?.Column,
            location?.EndLine,
            location?.EndColumn);
    }
}

internal sealed record DiSubjectResolution(
    bool Success,
    DiSelectionInput? SelectionInput,
    DiSelectionSection? Selection,
    DiTypeInfo? Subject,
    int ExitCode,
    int? DiagnosticId,
    string? DiagnosticMessage)
{
    public static DiSubjectResolution Succeeded(DiSelectionInput selectionInput, DiSelectionSection? selection, DiTypeInfo? subject)
    {
        return new DiSubjectResolution(true, selectionInput, selection, subject, ExitCodes.Success, DiagnosticId: null, DiagnosticMessage: null);
    }

    public static DiSubjectResolution Failed(int? diagnosticId, string? diagnosticMessage, int exitCode)
    {
        return new DiSubjectResolution(false, SelectionInput: null, Selection: null, Subject: null, exitCode, diagnosticId, diagnosticMessage);
    }
}

using Microsoft.CodeAnalysis;
using Navlyn.ApplicationDomains;
using Navlyn.Diagnostics;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class ApplicationDomainCommandSupport
{
    public const int DefaultCandidateLimit = 20;
    public const int DefaultRouteLimit = 100;
    public const int DefaultEvidenceLimit = 5;
    public const int DefaultOptionLimit = 100;
    public const int DefaultConsumerLimit = 100;
    public const int DefaultBindingLimit = 100;
    public const int DefaultHandlerLimit = 100;
    public const int DefaultCallSiteLimit = 100;
    public const int DefaultEntityLimit = 200;
    public const int DefaultQuerySiteLimit = 200;
    public const int DefaultUsageLimit = 200;
    public const int DefaultReferenceLimit = 100;

    public static bool ResolveProjects(
        LoadedWorkspace workspace,
        IReadOnlyList<string> projectFilters,
        out IReadOnlyList<Project> projects,
        out IReadOnlyList<ApplicationDomainProjectFilter>? projectOutputs,
        out int exitCode)
    {
        ProjectFilterResolutionResult result = new ProjectFilterResolver().ResolveMany(workspace.Solution, projectFilters);
        if (result.Error is not null)
        {
            DiagnosticReporter.WriteError(result.Error.DiagnosticId, result.Error.Message);
            projects = [];
            projectOutputs = null;
            exitCode = result.Error.ExitCode;
            return false;
        }

        projects = result.Projects;
        projectOutputs = result.AppliedFilters.Count == 0
            ? null
            : result.AppliedFilters.Select(filter => new ApplicationDomainProjectFilter(filter.Filter, filter.Name, filter.Path, filter.TargetFramework)).ToArray();
        exitCode = ExitCodes.Success;
        return true;
    }

    public static bool ValidatePositiveLimits(out int exitCode, params (string Name, int Value)[] limits)
    {
        foreach ((string name, int value) in limits)
        {
            if (!FuzzyCommandSupport.TryCreatePositiveOption(name, value, out exitCode))
            {
                return false;
            }
        }

        exitCode = ExitCodes.Success;
        return true;
    }

    public static bool ValidateNonNegativeLimits(out int exitCode, params (string Name, int Value)[] limits)
    {
        foreach ((string name, int value) in limits)
        {
            if (!FuzzyCommandSupport.TryCreateNonNegativeOption(name, value, out exitCode))
            {
                return false;
            }
        }

        exitCode = ExitCodes.Success;
        return true;
    }

    public static async Task<ApplicationDomainSubjectResolution> ResolveSubjectAsync(
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
        if ((hasQuery ? 1 : 0) + (hasCandidate ? 1 : 0) + (hasPosition ? 1 : 0) != 1 ||
            hasPosition && (file is null || line is null || column is null))
        {
            return ApplicationDomainSubjectResolution.Failed(
                DiagnosticIds.ParseError,
                $"Specify exactly one {commandName} input mode: --query, --candidate-id, or --file with --line and --column.",
                ExitCodes.UsageError);
        }

        if (hasPosition)
        {
            if (projectFilters.Count > 1)
            {
                return ApplicationDomainSubjectResolution.Failed(DiagnosticIds.ParseError, "Source-position mode accepts at most one --project filter.", ExitCodes.UsageError);
            }

            string? projectFilter = projectFilters.Count == 0 ? null : projectFilters[0];
            if (!ProjectFilterCommand.TryResolveSingleProject(
                workspace,
                projectFilter,
                out Project? project,
                out _,
                out int sourceExitCode))
            {
                return ApplicationDomainSubjectResolution.Failed(null, null, sourceExitCode);
            }

            SourceSymbolResolutionResult sourceResult = await new SourceSymbolResolver().ResolveAsync(
                workspace.Solution,
                file!,
                line!.Value,
                column!.Value,
                project,
                excludeGenerated,
                cancellationToken);
            if (sourceResult.Error is not null)
            {
                return ApplicationDomainSubjectResolution.Failed(sourceResult.Error.DiagnosticId, sourceResult.Error.Message, sourceResult.Error.ExitCode);
            }

            SourceSymbolResolution resolved = sourceResult.Resolution!;
            ISymbol symbol = resolved.Symbol is INamedTypeSymbol ? resolved.Symbol : resolved.Symbol.ContainingType ?? resolved.Symbol;
            if (symbol is not INamedTypeSymbol)
            {
                return ApplicationDomainSubjectResolution.Failed(DiagnosticIds.SymbolNotFoundAtPosition, "Selected symbol is not a type or a member contained by a type.", ExitCodes.UsageError);
            }

            return ApplicationDomainSubjectResolution.Succeeded(
                new ApplicationDomainSelectionInput("sourcePosition", Query: null, CandidateId: null, resolved.File, resolved.Line, resolved.Column),
                selection: null,
                subject: ApplicationDomainResolver.CreateSymbol(symbol, resolved.ProjectName, excludeGenerated));
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
            return ApplicationDomainSubjectResolution.Failed(null, null, exitCode);
        }

        FuzzyCandidateResolution resolution = await new FuzzyDiscoveryResolver().ResolveCandidatesForSelectionAsync(
            projects,
            fuzzyOptions,
            cancellationToken);
        if (resolution.Error is not null)
        {
            return ApplicationDomainSubjectResolution.Failed(resolution.Error.DiagnosticId, resolution.Error.Message, resolution.Error.ExitCode);
        }

        FuzzySymbolCandidate? selected = resolution.SelectedCandidate;
        ApplicationDomainSymbol? subject = selected is null
            ? null
            : new ApplicationDomainSymbol(
                selected.Name,
                selected.Kind,
                selected.Container,
                selected.Facts,
                selected.Path,
                selected.Line,
                selected.Column,
                selected.EndLine,
                selected.EndColumn);

        return ApplicationDomainSubjectResolution.Succeeded(
            new ApplicationDomainSelectionInput(hasCandidate ? "candidateId" : "query", query?.Trim(), candidateId?.Trim(), File: null, Line: null, Column: null),
            new ApplicationDomainSelectionSection(
                resolution.Confidence,
                Math.Min(resolution.Candidates.Count, candidateLimit),
                resolution.TotalCandidates,
                selected,
                [.. resolution.Candidates.Take(candidateLimit)],
                resolution.SelectionExplanation),
            subject);
    }

    public static IReadOnlyList<string> SplitValues(IReadOnlyList<string> values)
    {
        return [.. values
            .SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)];
    }
}

internal sealed record ApplicationDomainSubjectResolution(
    bool Success,
    ApplicationDomainSelectionInput? SelectionInput,
    ApplicationDomainSelectionSection? Selection,
    ApplicationDomainSymbol? Subject,
    int ExitCode,
    int? DiagnosticId,
    string? DiagnosticMessage)
{
    public static ApplicationDomainSubjectResolution Succeeded(
        ApplicationDomainSelectionInput selectionInput,
        ApplicationDomainSelectionSection? selection,
        ApplicationDomainSymbol? subject)
    {
        return new ApplicationDomainSubjectResolution(true, selectionInput, selection, subject, ExitCodes.Success, DiagnosticId: null, DiagnosticMessage: null);
    }

    public static ApplicationDomainSubjectResolution Failed(int? diagnosticId, string? diagnosticMessage, int exitCode)
    {
        return new ApplicationDomainSubjectResolution(false, SelectionInput: null, Selection: null, Subject: null, exitCode, diagnosticId, diagnosticMessage);
    }
}

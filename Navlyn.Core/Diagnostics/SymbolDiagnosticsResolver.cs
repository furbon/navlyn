using Microsoft.CodeAnalysis;
using Navlyn.Symbols;

namespace Navlyn.Diagnostics;

internal sealed class SymbolDiagnosticsResolver
{
    public async Task<SymbolDiagnosticsResolutionResult> ResolveAsync(
        Solution solution,
        FileInfo file,
        int line,
        int column,
        Project? project,
        bool excludeGenerated,
        IReadOnlyList<string> severities,
        IReadOnlyList<string> ids,
        int? limit,
        CancellationToken cancellationToken)
    {
        SourceSymbolResolutionResult sourceResult = await new SourceSymbolResolver().ResolveAsync(
            solution,
            file,
            line,
            column,
            project,
            excludeGenerated,
            cancellationToken);

        if (sourceResult.Error is not null)
        {
            return SymbolDiagnosticsResolutionResult.Failed(sourceResult.Error);
        }

        SourceSymbolResolution source = sourceResult.Resolution!;
        IReadOnlyList<SymbolSourceLocation> locations = SymbolNavigationFacts.GetSourceLocations(source.Symbol, excludeGenerated);
        IReadOnlyList<Project> projects = project is null ? solution.Projects.ToArray() : [project];
        WorkspaceDiagnosticsResolution diagnostics = await new WorkspaceDiagnosticsResolver().ResolveAsync(
            projects,
            excludeGenerated,
            cancellationToken);

        HashSet<string> severitySet = [.. severities];
        HashSet<string> idSet = [.. ids];
        IReadOnlyList<SymbolDiagnosticItem> scoped = [.. diagnostics.Diagnostics
            .Where(diagnostic =>
                (severitySet.Count == 0 || severitySet.Contains(diagnostic.Severity)) &&
                (idSet.Count == 0 || idSet.Contains(diagnostic.Id)) &&
                locations.Any(location => Intersects(location, diagnostic)))
            .Select(diagnostic => new SymbolDiagnosticItem(
                diagnostic.Project,
                diagnostic.Severity,
                diagnostic.Id,
                diagnostic.Message,
                diagnostic.Path,
                diagnostic.Line,
                diagnostic.Column,
                diagnostic.EndLine,
                diagnostic.EndColumn,
                ["diagnostic-intersects-symbol-span"]))
            .OrderBy(diagnostic => diagnostic.Path, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Line)
            .ThenBy(diagnostic => diagnostic.Column)
            .ThenBy(diagnostic => diagnostic.Project.Path, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Project.Name, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Id, StringComparer.Ordinal)];

        IReadOnlyList<SymbolDiagnosticItem> limited = limit is null ? scoped : [.. scoped.Take(limit.Value)];
        return SymbolDiagnosticsResolutionResult.Succeeded(new SymbolDiagnosticsResolution(
            source.File,
            source.Line,
            source.Column,
            new SymbolDiagnosticsSymbol(
                source.Symbol.Name,
                source.Symbol.Kind.ToString(),
                SymbolNavigationFacts.GetContainer(source.Symbol),
                SymbolFactsBuilder.Create(source.Symbol, source.ProjectName),
                locations),
            new SymbolDiagnosticsFilters(severities, ids, limit),
            scoped.Count,
            limited.Count < scoped.Count,
            limited));
    }

    private static bool Intersects(SymbolSourceLocation symbol, WorkspaceDiagnosticResult diagnostic)
    {
        if (diagnostic.Path is null || diagnostic.Line is null)
        {
            return false;
        }

        if (!string.Equals(symbol.Path, diagnostic.Path, StringComparison.Ordinal))
        {
            return false;
        }

        int diagnosticEndLine = diagnostic.EndLine ?? diagnostic.Line.Value;
        return diagnostic.Line.Value <= symbol.EndLine && diagnosticEndLine >= symbol.Line;
    }
}

internal sealed record SymbolDiagnosticsResolutionResult(SymbolDiagnosticsResolution? Resolution, SymbolNavigationError? Error)
{
    public static SymbolDiagnosticsResolutionResult Succeeded(SymbolDiagnosticsResolution resolution)
    {
        return new SymbolDiagnosticsResolutionResult(resolution, Error: null);
    }

    public static SymbolDiagnosticsResolutionResult Failed(SymbolNavigationError error)
    {
        return new SymbolDiagnosticsResolutionResult(Resolution: null, error);
    }
}

internal sealed record SymbolDiagnosticsResolution(
    string File,
    int Line,
    int Column,
    SymbolDiagnosticsSymbol Symbol,
    SymbolDiagnosticsFilters Filters,
    int TotalDiagnostics,
    bool Truncated,
    IReadOnlyList<SymbolDiagnosticItem> Diagnostics);

internal sealed record SymbolDiagnosticsSymbol(
    string Name,
    string Kind,
    string? Container,
    SymbolFacts Facts,
    IReadOnlyList<SymbolSourceLocation> Declarations);

internal sealed record SymbolDiagnosticsFilters(
    IReadOnlyList<string> Severities,
    IReadOnlyList<string> Ids,
    int? Limit);

internal sealed record SymbolDiagnosticItem(
    WorkspaceDiagnosticProject Project,
    string Severity,
    string Id,
    string Message,
    string? Path,
    int? Line,
    int? Column,
    int? EndLine,
    int? EndColumn,
    IReadOnlyList<string> ReasonCodes);

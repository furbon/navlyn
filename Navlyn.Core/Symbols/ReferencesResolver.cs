using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
namespace Navlyn.Symbols;

internal sealed class ReferencesResolver
{
    public async Task<ReferencesResolutionResult> ResolveAsync(
        Solution solution,
        FileInfo file,
        int line,
        int column,
        Project? project,
        bool excludeGenerated,
        CancellationToken cancellationToken)
    {
        return await ResolveAsync(
            solution,
            file,
            line,
            column,
            project,
            excludeGenerated,
            SymbolNavigationSearchOptions.Default,
            cancellationToken);
    }

    public async Task<ReferencesResolutionResult> ResolveAsync(
        Solution solution,
        FileInfo file,
        int line,
        int column,
        Project? project,
        bool excludeGenerated,
        SymbolNavigationSearchOptions searchOptions,
        CancellationToken cancellationToken)
    {
        SourceSymbolResolutionResult result = await new SourceSymbolResolver().ResolveAsync(
            solution,
            file,
            line,
            column,
            project,
            excludeGenerated,
            cancellationToken);

        if (result.Error is not null)
        {
            return ReferencesResolutionResult.Failed(result.Error);
        }

        SourceSymbolResolution resolution = result.Resolution!;
        SymbolNavigationSearchPlan searchPlan = await SymbolNavigationSearchPlanner.CreateAsync(
            solution,
            resolution,
            searchOptions,
            excludeGenerated,
            cancellationToken);

        IEnumerable<ReferencedSymbol> referencedSymbols = await SymbolFinder.FindReferencesAsync(
            resolution.Symbol,
            solution,
            searchPlan.Documents,
            cancellationToken: cancellationToken);

        List<SymbolReferenceLocation> referenceLocations = [];
        foreach (ReferenceLocation referenceLocation in referencedSymbols.SelectMany(referencedSymbol => referencedSymbol.Locations))
        {
            SymbolReferenceLocation? location = await CreateLocationAsync(
                solution,
                referenceLocation.Location,
                resolution.Symbol,
                excludeGenerated,
                cancellationToken);

            if (location is not null)
            {
                referenceLocations.Add(location);
            }
        }

        IReadOnlyList<SymbolReferenceLocation> references = [.. referenceLocations
            .GroupBy(reference => (reference.Path, reference.Line, reference.Column, reference.EndLine, reference.EndColumn))
            .Select(group => group.First())
            .OrderBy(reference => reference.Path, StringComparer.Ordinal)
            .ThenBy(reference => reference.Line)
            .ThenBy(reference => reference.Column)
            .ThenBy(reference => reference.EndLine)
            .ThenBy(reference => reference.EndColumn)];

        return ReferencesResolutionResult.Succeeded(new ReferencesResolution(
            File: resolution.File,
            Line: resolution.Line,
            Column: resolution.Column,
            Symbol: new ReferencesSymbol(
                Name: resolution.Symbol.Name,
                Kind: resolution.Symbol.Kind.ToString(),
                Container: SymbolNavigationFacts.GetContainer(resolution.Symbol),
                Facts: SymbolFactsBuilder.Create(resolution.Symbol, resolution.ProjectName)),
            References: references,
            Search: searchPlan.Metadata));
    }

    private static async Task<SymbolReferenceLocation?> CreateLocationAsync(
        Solution solution,
        Location location,
        ISymbol selectedSymbol,
        bool excludeGenerated,
        CancellationToken cancellationToken)
    {
        SymbolSourceLocation? sourceLocation = SymbolNavigationFacts.CreateSourceLocation(location, excludeGenerated);
        if (sourceLocation is null)
        {
            return null;
        }

        ReferencesContainingSymbol? containingSymbol = null;
        string? projectName = null;
        string? projectKind = null;
        string usageKind = "read";
        Document? document = solution.GetDocument(location.SourceTree);
        if (document is not null)
        {
            projectName = document.Project.Name;
            projectKind = ReferenceUsageClassifier.ClassifyProject(document.Project.Name, document.Project.FilePath);
            SemanticModel? semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            if (semanticModel is not null)
            {
                usageKind = ReferenceUsageClassifier.Classify(semanticModel, location, selectedSymbol, cancellationToken);
                ISymbol? symbol = semanticModel.GetEnclosingSymbol(location.SourceSpan.Start, cancellationToken);
                if (symbol is not null)
                {
                    containingSymbol = CreateContainingSymbol(symbol, document.Project.Name, excludeGenerated);
                }
            }
        }

        return new SymbolReferenceLocation(
            Path: sourceLocation.Path,
            Line: sourceLocation.Line,
            Column: sourceLocation.Column,
            EndLine: sourceLocation.EndLine,
            EndColumn: sourceLocation.EndColumn,
            UsageKind: usageKind,
            ProjectName: projectName,
            ProjectKind: projectKind ?? "unknown",
            ContainingSymbol: containingSymbol);
    }

    private static ReferencesContainingSymbol CreateContainingSymbol(
        ISymbol symbol,
        string projectName,
        bool excludeGenerated)
    {
        SymbolSourceLocation? sourceLocation =
            SymbolNavigationFacts.GetSourceLocations(symbol, excludeGenerated).FirstOrDefault();

        return new ReferencesContainingSymbol(
            Name: symbol.Name,
            Kind: symbol.Kind.ToString(),
            Container: SymbolNavigationFacts.GetContainer(symbol),
            Facts: SymbolFactsBuilder.Create(symbol, projectName),
            Path: sourceLocation?.Path,
            Line: sourceLocation?.Line,
            Column: sourceLocation?.Column,
            EndLine: sourceLocation?.EndLine,
            EndColumn: sourceLocation?.EndColumn);
    }
}

internal sealed record ReferencesResolutionResult(ReferencesResolution? Resolution, ReferencesResolutionError? Error)
{
    public static ReferencesResolutionResult Succeeded(ReferencesResolution resolution)
    {
        return new ReferencesResolutionResult(resolution, Error: null);
    }

    public static ReferencesResolutionResult Failed(int diagnosticId, string message, int exitCode)
    {
        return new ReferencesResolutionResult(
            Resolution: null,
            Error: new ReferencesResolutionError(diagnosticId, message, exitCode));
    }

    public static ReferencesResolutionResult Failed(SymbolNavigationError error)
    {
        return Failed(error.DiagnosticId, error.Message, error.ExitCode);
    }
}

internal sealed record ReferencesResolution(
    string File,
    int Line,
    int Column,
    ReferencesSymbol Symbol,
    IReadOnlyList<SymbolReferenceLocation> References,
    SymbolNavigationSearchMetadata Search);

internal sealed record ReferencesSymbol(string Name, string Kind, string? Container, SymbolFacts Facts);

internal sealed record SymbolReferenceLocation(
    string Path,
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    string UsageKind,
    string? ProjectName,
    string ProjectKind,
    ReferencesContainingSymbol? ContainingSymbol);

internal sealed record ReferencesContainingSymbol(
    string Name,
    string Kind,
    string? Container,
    SymbolFacts Facts,
    string? Path,
    int? Line,
    int? Column,
    int? EndLine,
    int? EndColumn);

internal sealed record ReferencesResolutionError(int DiagnosticId, string Message, int ExitCode);

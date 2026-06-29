using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace Navlyn.Symbols;

internal sealed class ImplementationsResolver
{
    public async Task<ImplementationsResolutionResult> ResolveAsync(
        Solution solution,
        FileInfo file,
        int line,
        int column,
        Project? project,
        bool excludeGenerated,
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
            return ImplementationsResolutionResult.Failed(result.Error);
        }

        SourceSymbolResolution resolution = result.Resolution!;
        IReadOnlyList<ISymbol> implementationSymbols =
            await FindImplementationSymbolsAsync(solution, resolution.Symbol, cancellationToken);

        IReadOnlyList<ImplementationLocation> implementations = [.. implementationSymbols
            .SelectMany(symbol => CreateLocations(symbol, excludeGenerated))
            .Distinct()
            .OrderBy(implementation => implementation.Path, StringComparer.Ordinal)
            .ThenBy(implementation => implementation.Line)
            .ThenBy(implementation => implementation.Column)
            .ThenBy(implementation => implementation.Name, StringComparer.Ordinal)
            .ThenBy(implementation => implementation.Kind, StringComparer.Ordinal)
            .ThenBy(implementation => implementation.Container, StringComparer.Ordinal)];

        return ImplementationsResolutionResult.Succeeded(new ImplementationsResolution(
            File: resolution.File,
            Line: resolution.Line,
            Column: resolution.Column,
            Symbol: new ImplementationsSymbol(
                Name: resolution.Symbol.Name,
                Kind: resolution.Symbol.Kind.ToString(),
                Container: SymbolNavigationFacts.GetContainer(resolution.Symbol),
                Facts: SymbolFactsBuilder.Create(resolution.Symbol, resolution.ProjectName)),
            Implementations: implementations));
    }

    private static async Task<IReadOnlyList<ISymbol>> FindImplementationSymbolsAsync(
        Solution solution,
        ISymbol symbol,
        CancellationToken cancellationToken)
    {
        ImmutableHashSet<Project> projects = solution.Projects.ToImmutableHashSet();
        List<ISymbol> symbols = [];

        if (symbol is INamedTypeSymbol namedType)
        {
            if (namedType.TypeKind is TypeKind.Interface || namedType.IsAbstract)
            {
                IEnumerable<ISymbol> implementations = await SymbolFinder.FindImplementationsAsync(
                    namedType,
                    solution,
                    transitive: true,
                    projects,
                    cancellationToken);

                symbols.AddRange(implementations);
            }

            return [.. symbols.Distinct(SymbolEqualityComparer.Default)];
        }

        if (symbol is IMethodSymbol or IPropertySymbol or IEventSymbol)
        {
            IEnumerable<ISymbol> implementations = await SymbolFinder.FindImplementationsAsync(
                symbol,
                solution,
                projects,
                cancellationToken);

            IEnumerable<ISymbol> overrides = await SymbolFinder.FindOverridesAsync(
                symbol,
                solution,
                projects,
                cancellationToken);

            symbols.AddRange(implementations);
            symbols.AddRange(overrides);
        }

        return [.. symbols.Distinct(SymbolEqualityComparer.Default)];
    }

    private static IEnumerable<ImplementationLocation> CreateLocations(ISymbol symbol, bool excludeGenerated)
    {
        foreach (SymbolSourceLocation location in SymbolNavigationFacts.GetSourceLocations(symbol, excludeGenerated))
        {
            yield return new ImplementationLocation(
                Name: symbol.Name,
                Kind: symbol.Kind.ToString(),
                Container: SymbolNavigationFacts.GetContainer(symbol),
                Facts: SymbolFactsBuilder.Create(symbol),
                Path: location.Path,
                Line: location.Line,
                Column: location.Column,
                EndLine: location.EndLine,
                EndColumn: location.EndColumn);
        }
    }
}

internal sealed record ImplementationsResolutionResult(
    ImplementationsResolution? Resolution,
    ImplementationsResolutionError? Error)
{
    public static ImplementationsResolutionResult Succeeded(ImplementationsResolution resolution)
    {
        return new ImplementationsResolutionResult(resolution, Error: null);
    }

    public static ImplementationsResolutionResult Failed(int diagnosticId, string message, int exitCode)
    {
        return new ImplementationsResolutionResult(
            Resolution: null,
            Error: new ImplementationsResolutionError(diagnosticId, message, exitCode));
    }

    public static ImplementationsResolutionResult Failed(SymbolNavigationError error)
    {
        return Failed(error.DiagnosticId, error.Message, error.ExitCode);
    }
}

internal sealed record ImplementationsResolution(
    string File,
    int Line,
    int Column,
    ImplementationsSymbol Symbol,
    IReadOnlyList<ImplementationLocation> Implementations);

internal sealed record ImplementationsSymbol(string Name, string Kind, string? Container, SymbolFacts Facts);

internal sealed record ImplementationLocation(
    string Name,
    string Kind,
    string? Container,
    SymbolFacts Facts,
    string Path,
    int Line,
    int Column,
    int EndLine,
    int EndColumn);

internal sealed record ImplementationsResolutionError(int DiagnosticId, string Message, int ExitCode);

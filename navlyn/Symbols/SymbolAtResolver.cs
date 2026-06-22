using Microsoft.CodeAnalysis;
namespace Navlyn.Symbols;

internal sealed class SymbolAtResolver
{
    public async Task<SymbolAtResolutionResult> ResolveAsync(
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
            return SymbolAtResolutionResult.Failed(result.Error);
        }

        SourceSymbolResolution resolution = result.Resolution!;
        SymbolAtSymbol resultSymbol = CreateSymbolResult(
            resolution.Symbol,
            resolution.SyntaxTree,
            resolution.Position,
            resolution.ProjectName,
            excludeGenerated);

        return SymbolAtResolutionResult.Succeeded(new SymbolAtResolution(
            File: resolution.File,
            Line: resolution.Line,
            Column: resolution.Column,
            Symbol: resultSymbol));
    }

    private static SymbolAtSymbol CreateSymbolResult(
        ISymbol symbol,
        SyntaxTree syntaxTree,
        int position,
        string projectName,
        bool excludeGenerated)
    {
        Location? location = SymbolNavigationFacts.GetBestSourceLocation(symbol, syntaxTree, position, excludeGenerated);
        SymbolSourceLocation? sourceLocation = location is null
            ? null
            : SymbolNavigationFacts.CreateSourceLocation(location, excludeGenerated);

        return new SymbolAtSymbol(
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

internal sealed record SymbolAtResolutionResult(SymbolAtResolution? Resolution, SymbolAtResolutionError? Error)
{
    public static SymbolAtResolutionResult Succeeded(SymbolAtResolution resolution)
    {
        return new SymbolAtResolutionResult(resolution, Error: null);
    }

    public static SymbolAtResolutionResult Failed(int diagnosticId, string message, int exitCode)
    {
        return new SymbolAtResolutionResult(
            Resolution: null,
            Error: new SymbolAtResolutionError(diagnosticId, message, exitCode));
    }

    public static SymbolAtResolutionResult Failed(SymbolNavigationError error)
    {
        return Failed(error.DiagnosticId, error.Message, error.ExitCode);
    }
}

internal sealed record SymbolAtResolution(
    string File,
    int Line,
    int Column,
    SymbolAtSymbol Symbol);

internal sealed record SymbolAtSymbol(
    string Name,
    string Kind,
    string? Container,
    SymbolFacts Facts,
    string? Path,
    int? Line,
    int? Column,
    int? EndLine,
    int? EndColumn);

internal sealed record SymbolAtResolutionError(int DiagnosticId, string Message, int ExitCode);

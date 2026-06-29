using Microsoft.CodeAnalysis;
using Navlyn.Diagnostics;

namespace Navlyn.Symbols;

internal sealed class DefinitionResolver
{
    public async Task<DefinitionResolutionResult> ResolveAsync(
        Solution solution,
        FileInfo file,
        int line,
        int column,
        Project? project,
        bool excludeGenerated,
        bool includeMetadata,
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
            return DefinitionResolutionResult.Failed(result.Error);
        }

        SourceSymbolResolution resolution = result.Resolution!;
        IReadOnlyList<SymbolSourceLocation> definitions =
            SymbolNavigationFacts.GetSourceLocations(resolution.Symbol, excludeGenerated);

        if (definitions.Count == 0)
        {
            if (includeMetadata && resolution.Symbol.Locations.Any(location => location.IsInMetadata))
            {
                return DefinitionResolutionResult.Succeeded(new DefinitionResolution(
                    File: resolution.File,
                    Line: resolution.Line,
                    Column: resolution.Column,
                    Symbol: new DefinitionSymbol(
                        Name: resolution.Symbol.Name,
                        Kind: resolution.Symbol.Kind.ToString(),
                        Container: SymbolNavigationFacts.GetContainer(resolution.Symbol),
                        Facts: SymbolFactsBuilder.Create(resolution.Symbol, resolution.ProjectName)),
                    Definitions: []));
            }

            return DefinitionResolutionResult.Failed(
                DiagnosticIds.SourceDefinitionNotFound,
                $"No source definition found for {resolution.Symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)}.",
                ExitCodes.UsageError);
        }

        return DefinitionResolutionResult.Succeeded(new DefinitionResolution(
            File: resolution.File,
            Line: resolution.Line,
            Column: resolution.Column,
            Symbol: new DefinitionSymbol(
                Name: resolution.Symbol.Name,
                Kind: resolution.Symbol.Kind.ToString(),
                Container: SymbolNavigationFacts.GetContainer(resolution.Symbol),
                Facts: SymbolFactsBuilder.Create(resolution.Symbol, resolution.ProjectName)),
            Definitions: definitions));
    }
}

internal sealed record DefinitionResolutionResult(DefinitionResolution? Resolution, DefinitionResolutionError? Error)
{
    public static DefinitionResolutionResult Succeeded(DefinitionResolution resolution)
    {
        return new DefinitionResolutionResult(resolution, Error: null);
    }

    public static DefinitionResolutionResult Failed(int diagnosticId, string message, int exitCode)
    {
        return new DefinitionResolutionResult(
            Resolution: null,
            Error: new DefinitionResolutionError(diagnosticId, message, exitCode));
    }

    public static DefinitionResolutionResult Failed(SymbolNavigationError error)
    {
        return Failed(error.DiagnosticId, error.Message, error.ExitCode);
    }
}

internal sealed record DefinitionResolution(
    string File,
    int Line,
    int Column,
    DefinitionSymbol Symbol,
    IReadOnlyList<SymbolSourceLocation> Definitions);

internal sealed record DefinitionSymbol(string Name, string Kind, string? Container, SymbolFacts Facts);

internal sealed record DefinitionResolutionError(int DiagnosticId, string Message, int ExitCode);

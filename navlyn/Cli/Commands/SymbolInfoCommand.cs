using System.CommandLine;
using System.Text.Json.Serialization;
using Navlyn.Diagnostics;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class SymbolInfoCommand
{
    public static Command Create()
    {
        return SourcePositionCommand.Create(
            "symbol-info",
            "Return symbol, expression, and binding facts at a source position.",
            ExecuteAsync);
    }

    private static async Task<int> ExecuteAsync(
        LoadedWorkspace loadedWorkspace,
        SourcePositionOptions options,
        CancellationToken cancellationToken)
    {
        SymbolInfoResolutionResult result = await new SymbolInfoResolver().ResolveAsync(
            loadedWorkspace.Solution,
            options.File,
            options.Line,
            options.Column,
            options.Project,
            options.ExcludeGenerated,
            cancellationToken);

        if (result.Error is not null)
        {
            DiagnosticReporter.WriteError(result.Error.DiagnosticId, result.Error.Message);
            return result.Error.ExitCode;
        }

        SymbolInfoResolution resolution = result.Resolution!;
        ConsoleJsonWriter.Write(new SymbolInfoResult(
            File: resolution.File,
            Line: resolution.Line,
            Column: resolution.Column,
            Project: options.ProjectFilter is null ? null : ProjectFilterOutput.FromAppliedFilter(options.ProjectFilter),
            ExcludeGenerated: options.ExcludeGenerated,
            Symbol: resolution.Symbol,
            Expression: resolution.Expression,
            ContainingSymbol: resolution.ContainingSymbol,
            Invocation: resolution.Invocation,
            Attribute: resolution.Attribute,
            Return: resolution.Return,
            Lambda: resolution.Lambda));

        return ExitCodes.Success;
    }

    private sealed record SymbolInfoResult(
        string File,
        int Line,
        int Column,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        ProjectFilterOutput? Project,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool ExcludeGenerated,
        SymbolInfoSymbol Symbol,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        SymbolExpressionInfo? Expression,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        SymbolInfoSymbol? ContainingSymbol,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        SymbolInvocationInfo? Invocation,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        SymbolAttributeInfo? Attribute,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        SymbolReturnInfo? Return,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        SymbolLambdaInfo? Lambda);
}

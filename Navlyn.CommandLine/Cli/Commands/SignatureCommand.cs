using System.CommandLine;
using System.Text.Json.Serialization;
using Navlyn.Diagnostics;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class SignatureCommand
{
    public static Command Create()
    {
        return SourcePositionCommand.Create(
            "signature",
            "Return API shape facts for the C# or Visual Basic symbol at a source position.",
            ExecuteAsync);
    }

    private static async Task<int> ExecuteAsync(
        LoadedWorkspace loadedWorkspace,
        SourcePositionOptions options,
        CancellationToken cancellationToken)
    {
        SignatureResolutionResult result = await new SignatureResolver().ResolveAsync(
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

        SignatureResolution resolution = result.Resolution!;
        ConsoleJsonWriter.Write(new SignatureResult(
            File: resolution.File,
            Line: resolution.Line,
            Column: resolution.Column,
            Project: options.ProjectFilter is null ? null : ProjectFilterOutput.FromAppliedFilter(options.ProjectFilter),
            SelectionInput: options.SelectionInput,
            ExcludeGenerated: options.ExcludeGenerated,
            Symbol: resolution.Symbol,
            ApiShape: resolution.ApiShape));

        return ExitCodes.Success;
    }

    private sealed record SignatureResult(
        string File,
        int Line,
        int Column,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        ProjectFilterOutput? Project,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        CandidateSelectionInput? SelectionInput,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool ExcludeGenerated,
        SignatureSymbol Symbol,
        SignatureApiShape ApiShape);
}

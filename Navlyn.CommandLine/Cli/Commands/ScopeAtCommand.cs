using System.CommandLine;
using System.Text.Json.Serialization;
using Navlyn.Diagnostics;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class ScopeAtCommand
{
    public static Command Create()
    {
        return SourcePositionCommand.Create(
            "scope-at",
            "Return enclosing C# or Visual Basic scope facts at a source position.",
            ExecuteAsync);
    }

    private static async Task<int> ExecuteAsync(
        LoadedWorkspace loadedWorkspace,
        SourcePositionOptions options,
        CancellationToken cancellationToken)
    {
        ScopeAtResolutionResult result = await new ScopeAtResolver().ResolveAsync(
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

        ScopeAtResolution resolution = result.Resolution!;
        ConsoleJsonWriter.Write(new ScopeAtResult(
            File: resolution.File,
            Line: resolution.Line,
            Column: resolution.Column,
            Project: options.ProjectFilter is null ? null : ProjectFilterOutput.FromAppliedFilter(options.ProjectFilter),
            SelectionInput: options.SelectionInput,
            ExcludeGenerated: options.ExcludeGenerated,
            ProjectContext: resolution.ProjectContext,
            Scopes: resolution.Scopes,
            ContainingSymbol: resolution.ContainingSymbol));

        return ExitCodes.Success;
    }

    private sealed record ScopeAtResult(
        string File,
        int Line,
        int Column,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        ProjectFilterOutput? Project,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        CandidateSelectionInput? SelectionInput,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool ExcludeGenerated,
        ScopeProjectContext ProjectContext,
        IReadOnlyList<ScopeFrame> Scopes,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        ScopeSymbol? ContainingSymbol);
}

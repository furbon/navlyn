using System.CommandLine;
using System.Text.Json.Serialization;
using Navlyn.Diagnostics;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class TypeHierarchyCommand
{
    public static Command Create()
    {
        return SourcePositionCommand.Create(
            "type-hierarchy",
            "Explore source type and member inheritance relationships.",
            ExecuteAsync);
    }

    private static async Task<int> ExecuteAsync(
        LoadedWorkspace loadedWorkspace,
        SourcePositionOptions options,
        CancellationToken cancellationToken)
    {
        TypeHierarchyResolutionResult result = await new TypeHierarchyResolver().ResolveAsync(
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

        TypeHierarchyResolution resolution = result.Resolution!;
        ConsoleJsonWriter.Write(new TypeHierarchyResult(
            File: resolution.File,
            Line: resolution.Line,
            Column: resolution.Column,
            Project: options.ProjectFilter is null ? null : ProjectFilterOutput.FromAppliedFilter(options.ProjectFilter),
            SelectionInput: options.SelectionInput,
            ExcludeGenerated: options.ExcludeGenerated,
            Symbol: resolution.Symbol,
            BaseTypes: resolution.BaseTypes,
            Interfaces: resolution.Interfaces,
            DerivedTypes: resolution.DerivedTypes,
            ImplementingTypes: resolution.ImplementingTypes,
            BaseMembers: resolution.BaseMembers,
            OverridingMembers: resolution.OverridingMembers,
            ImplementedMembers: resolution.ImplementedMembers));

        return ExitCodes.Success;
    }

    private sealed record TypeHierarchyResult(
        string File,
        int Line,
        int Column,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        ProjectFilterOutput? Project,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        CandidateSelectionInput? SelectionInput,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool ExcludeGenerated,
        HierarchySymbol Symbol,
        IReadOnlyList<HierarchySymbol> BaseTypes,
        IReadOnlyList<HierarchySymbol> Interfaces,
        IReadOnlyList<HierarchySymbol> DerivedTypes,
        IReadOnlyList<HierarchySymbol> ImplementingTypes,
        IReadOnlyList<HierarchySymbol> BaseMembers,
        IReadOnlyList<HierarchySymbol> OverridingMembers,
        IReadOnlyList<HierarchySymbol> ImplementedMembers);
}

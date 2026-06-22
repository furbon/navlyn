using System.CommandLine;
using System.Text.Json.Serialization;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class OverviewCommand
{
    public static Command Create()
    {
        return WorkspaceCommand.Create(
            "overview",
            "Report a compact workspace overview.",
            ExecuteAsync);
    }

    private static Task<int> ExecuteAsync(
        LoadedWorkspace loadedWorkspace,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        ConsoleJsonWriter.Write(new OverviewResult(
            Workspace: loadedWorkspace.DisplayPath,
            Kind: loadedWorkspace.Kind,
            Projects: loadedWorkspace.Projects.Select(project => new OverviewProject(
                Name: project.Name,
                Path: project.Path,
                Language: project.Language,
                AssemblyName: project.AssemblyName,
                TargetFramework: project.TargetFramework,
                LanguageVersion: project.LanguageVersion,
                PreprocessorSymbols: project.PreprocessorSymbols.Count == 0
                    ? null
                    : project.PreprocessorSymbols)).ToArray()));

        return Task.FromResult(ExitCodes.Success);
    }

    private sealed record OverviewResult(
        string Workspace,
        string Kind,
        IReadOnlyList<OverviewProject> Projects);

    private sealed record OverviewProject(
        string Name,
        string? Path,
        string Language,
        string? AssemblyName,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? TargetFramework,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? LanguageVersion,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<string>? PreprocessorSymbols);
}

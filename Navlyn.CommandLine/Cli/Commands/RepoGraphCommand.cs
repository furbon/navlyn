using System.CommandLine;
using Navlyn.Cli.OutputProfiles;
using Navlyn.Diagnostics;
using Navlyn.RepoGraph;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class RepoGraphCommand
{
    private const int DefaultRelationshipLimit = 200;

    public static Command Create()
    {
        Option<string[]> projectOption = SharedOptions.CreateProjectFiltersOption();
        Option<bool> includePackagesOption = CreateDefaultTrueOption("--include-packages", "Include direct package references.");
        Option<bool> includeMsbuildFilesOption = CreateDefaultTrueOption("--include-msbuild-files", "Include repository MSBuild files such as Directory.Build.props.");
        Option<bool> includePreprocessorSymbolsOption = CreateDefaultTrueOption("--include-preprocessor-symbols", "Include project preprocessor symbols.");
        Option<bool> classificationOption = CreateDefaultTrueOption("--classification", "Include project classification facts.");
        Option<int?> relationshipLimitOption = new("--relationship-limit")
        {
            Description = $"Maximum number of inferred relationships to return. Defaults to {DefaultRelationshipLimit}."
        };
        Option<string> profileOption = OutputProfile.CreateOption();

        return WorkspaceCommand.Create(
            "repo-graph",
            "Report deterministic repository, project, package, and relationship facts.",
            [projectOption, includePackagesOption, includeMsbuildFilesOption, includePreprocessorSymbolsOption, classificationOption, relationshipLimitOption, profileOption],
            (workspace, parseResult, cancellationToken) => ExecuteAsync(
                workspace,
                parseResult.GetValue(projectOption) ?? [],
                parseResult.GetValue(includePackagesOption),
                parseResult.GetValue(includeMsbuildFilesOption),
                parseResult.GetValue(includePreprocessorSymbolsOption),
                parseResult.GetValue(classificationOption),
                parseResult.GetValue(relationshipLimitOption),
                parseResult.GetValue(profileOption)!,
                cancellationToken));
    }

    private static Task<int> ExecuteAsync(
        LoadedWorkspace workspace,
        IReadOnlyList<string> projectFilters,
        bool includePackages,
        bool includeMsbuildFiles,
        bool includePreprocessorSymbols,
        bool classification,
        int? relationshipLimit,
        string profile,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        int effectiveRelationshipLimit = relationshipLimit ?? DefaultRelationshipLimit;
        if (effectiveRelationshipLimit <= 0)
        {
            DiagnosticReporter.WriteError(DiagnosticIds.InvalidLimit, "--relationship-limit must be 1 or greater.");
            return Task.FromResult(ExitCodes.UsageError);
        }

        ProjectFilterResolutionResult projectResult = new ProjectFilterResolver().ResolveMany(workspace.Solution, projectFilters);
        if (projectResult.Error is not null)
        {
            DiagnosticReporter.WriteError(projectResult.Error.DiagnosticId, projectResult.Error.Message);
            return Task.FromResult(projectResult.Error.ExitCode);
        }

        RepoGraphResult result = new RepoGraphResolver().Resolve(
            workspace,
            projectResult.Projects,
            new RepoGraphOptions(
                IncludePackages: includePackages,
                IncludeMsbuildFiles: includeMsbuildFiles,
                IncludePreprocessorSymbols: includePreprocessorSymbols,
                IncludeClassification: classification,
                RelationshipLimit: effectiveRelationshipLimit));

        ConsoleJsonWriter.Write(OutputProfile.Format(workspace, "repo-graph", profile, result, new
        {
            projectFilters,
            includePackages,
            includeMsbuildFiles,
            includePreprocessorSymbols,
            classification,
            relationshipLimit = effectiveRelationshipLimit
        }));
        return Task.FromResult(ExitCodes.Success);
    }

    private static Option<bool> CreateDefaultTrueOption(string name, string description)
    {
        return new Option<bool>(name)
        {
            Description = description,
            DefaultValueFactory = _ => true
        };
    }
}

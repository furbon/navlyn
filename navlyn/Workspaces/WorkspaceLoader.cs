using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Navlyn.Diagnostics;
using Navlyn.Paths;

namespace Navlyn.Workspaces;

internal sealed class WorkspaceLoader
{
    public async Task<WorkspaceLoadResult> LoadAsync(FileInfo workspace, CancellationToken cancellationToken)
    {
        string workspacePath;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            workspacePath = ResolveWorkspacePath(workspace);
        }
        catch (OperationCanceledException)
        {
            return WorkspaceLoadResult.Failed(DiagnosticIds.OperationCanceled, "Operation canceled.", ExitCodes.Failure);
        }
        catch (Exception ex)
        {
            return WorkspaceLoadResult.Failed(DiagnosticIds.InvalidWorkspacePath, $"Invalid workspace path: {ex.Message}", ExitCodes.UsageError);
        }

        string? workspaceKind = GetWorkspaceKind(workspacePath);
        if (workspaceKind is null)
        {
            return WorkspaceLoadResult.Failed(
                DiagnosticIds.InvalidWorkspaceExtension,
                "Workspace must be a .slnx, .sln, or .csproj file.",
                ExitCodes.UsageError);
        }

        if (!File.Exists(workspacePath))
        {
            return WorkspaceLoadResult.Failed(
                DiagnosticIds.WorkspaceNotFound,
                $"Workspace file does not exist: {workspacePath}",
                ExitCodes.UsageError);
        }

        try
        {
            RegisterMSBuild();
        }
        catch (Exception ex)
        {
            return WorkspaceLoadResult.Failed(
                DiagnosticIds.MSBuildRegistrationFailed,
                $"Failed to register MSBuild: {ex.Message}",
                ExitCodes.Failure);
        }

        List<WorkspaceLoadDiagnostic> diagnostics = [];

        MSBuildWorkspace? msbuildWorkspace = null;

        try
        {
            msbuildWorkspace = MSBuildWorkspace.Create();
            msbuildWorkspace.RegisterWorkspaceFailedHandler(args =>
            {
                diagnostics.Add(new WorkspaceLoadDiagnostic(
                    Kind: args.Diagnostic.Kind.ToString(),
                    Message: args.Diagnostic.Message));
            });

            Solution solution;
            if (workspaceKind == "solution")
            {
                solution = await msbuildWorkspace.OpenSolutionAsync(workspacePath, cancellationToken: cancellationToken);
            }
            else
            {
                Project project = await msbuildWorkspace.OpenProjectAsync(workspacePath, cancellationToken: cancellationToken);
                solution = project.Solution;
            }

            diagnostics = [.. diagnostics.OrderBy(d => d.Kind, StringComparer.Ordinal).ThenBy(d => d.Message, StringComparer.Ordinal)];
            if (diagnostics.Any(d => d.Kind == WorkspaceDiagnosticKind.Failure.ToString()))
            {
                msbuildWorkspace.Dispose();
                return WorkspaceLoadResult.Failed(
                    DiagnosticIds.WorkspaceFailureDiagnostics,
                    "Workspace load produced failure diagnostics.",
                    ExitCodes.Failure,
                    diagnostics);
            }

            LoadedWorkspace loadedWorkspace = new(
                FullPath: workspacePath,
                DisplayPath: PathDisplay.FromCurrentDirectory(workspacePath),
                Kind: workspaceKind,
                Workspace: msbuildWorkspace,
                Solution: solution,
                Projects: GetProjects(solution));

            return WorkspaceLoadResult.Succeeded(loadedWorkspace, diagnostics);
        }
        catch (OperationCanceledException)
        {
            msbuildWorkspace?.Dispose();
            return WorkspaceLoadResult.Failed(DiagnosticIds.OperationCanceled, "Operation canceled.", ExitCodes.Failure, diagnostics);
        }
        catch (Exception ex)
        {
            msbuildWorkspace?.Dispose();
            return WorkspaceLoadResult.Failed(
                DiagnosticIds.WorkspaceLoadFailed,
                $"Failed to load workspace: {ex.Message}",
                ExitCodes.Failure,
                diagnostics);
        }
    }

    private static string? GetWorkspaceKind(string workspacePath)
    {
        return Path.GetExtension(workspacePath).ToLowerInvariant() switch
        {
            ".slnx" or ".sln" => "solution",
            ".csproj" => "project",
            _ => null
        };
    }

    private static string ResolveWorkspacePath(FileInfo workspace)
    {
        IReadOnlyList<string> candidates = PathDisplay.GetInputPathCandidates(workspace.ToString(), anchorPath: null);
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    private static void RegisterMSBuild()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }
    }

    private static IReadOnlyList<LoadedProject> GetProjects(Solution solution)
    {
        return [.. solution.Projects
            .Select(project => new LoadedProject(
                Name: project.Name,
                Path: project.FilePath is null ? null : PathDisplay.FromCurrentDirectory(project.FilePath),
                Language: project.Language,
                AssemblyName: project.AssemblyName,
                TargetFramework: ProjectContextFacts.GetTargetFramework(project),
                LanguageVersion: ProjectContextFacts.GetLanguageVersion(project),
                PreprocessorSymbols: ProjectContextFacts.GetPreprocessorSymbols(project)))
            .OrderBy(project => project.Path, StringComparer.Ordinal)
            .ThenBy(project => project.Name, StringComparer.Ordinal)];
    }
}

internal sealed record WorkspaceLoadResult(
    LoadedWorkspace? Workspace,
    IReadOnlyList<WorkspaceLoadDiagnostic> Diagnostics,
    WorkspaceLoadError? Error)
{
    public static WorkspaceLoadResult Succeeded(
        LoadedWorkspace workspace,
        IReadOnlyList<WorkspaceLoadDiagnostic> diagnostics)
    {
        return new WorkspaceLoadResult(workspace, diagnostics, Error: null);
    }

    public static WorkspaceLoadResult Failed(
        int diagnosticId,
        string message,
        int exitCode,
        IReadOnlyList<WorkspaceLoadDiagnostic>? diagnostics = null)
    {
        return new WorkspaceLoadResult(
            Workspace: null,
            Diagnostics: diagnostics ?? [],
            Error: new WorkspaceLoadError(diagnosticId, message, exitCode));
    }
}

internal sealed record LoadedWorkspace(
    string FullPath,
    string DisplayPath,
    string Kind,
    MSBuildWorkspace Workspace,
    Solution Solution,
    IReadOnlyList<LoadedProject> Projects)
    : IDisposable
{
    public int ProjectCount => Projects.Count;

    public void Dispose()
    {
        Workspace.Dispose();
    }
}

internal sealed record LoadedProject(
    string Name,
    string? Path,
    string Language,
    string? AssemblyName,
    string? TargetFramework,
    string? LanguageVersion,
    IReadOnlyList<string> PreprocessorSymbols);

internal sealed record WorkspaceLoadDiagnostic(string Kind, string Message);

internal sealed record WorkspaceLoadError(int DiagnosticId, string Message, int ExitCode);

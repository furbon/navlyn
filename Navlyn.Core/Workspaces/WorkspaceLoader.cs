using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Navlyn.Diagnostics;
using Navlyn.Paths;
using System.Text.Json;

namespace Navlyn.Workspaces;

internal sealed class WorkspaceLoader
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public async Task<WorkspaceLoadResult> LoadAsync(FileInfo workspace, CancellationToken cancellationToken)
    {
        WorkspacePathResolution resolution;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            resolution = ResolveWorkspacePath(workspace);
        }
        catch (OperationCanceledException)
        {
            return WorkspaceLoadResult.Failed(DiagnosticIds.OperationCanceled, "Operation canceled.", ExitCodes.Failure);
        }
        catch (CodeWorkspaceException ex)
        {
            return WorkspaceLoadResult.Failed(ex.DiagnosticId, ex.Message, ExitCodes.UsageError);
        }
        catch (Exception ex)
        {
            return WorkspaceLoadResult.Failed(DiagnosticIds.InvalidWorkspacePath, $"Invalid workspace path: {ex.Message}", ExitCodes.UsageError);
        }

        string workspacePath = resolution.FullPath;
        string workspaceKind = resolution.Kind;

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

        List<WorkspaceLoadDiagnostic> diagnostics = [.. resolution.Diagnostics];

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

    public static bool TryResolveAutoWorkspace(
        string searchDirectory,
        out string workspace,
        out string? error)
    {
        workspace = "";
        string fullSearchDirectory = Path.GetFullPath(searchDirectory);
        if (!Directory.Exists(fullSearchDirectory))
        {
            error = $"--workspace auto search directory does not exist: {fullSearchDirectory}.";
            return false;
        }

        IReadOnlyList<WorkspaceCandidate> candidates = FindWorkspaceCandidates(fullSearchDirectory, includeCodeWorkspace: true);
        if (candidates.Count == 0)
        {
            error = $"--workspace auto could not find a .code-workspace, .slnx, .sln, or .csproj in {fullSearchDirectory}.";
            return false;
        }

        int bestRank = candidates.Min(candidate => candidate.Rank);
        IReadOnlyList<WorkspaceCandidate> bestCandidates = [.. candidates.Where(candidate => candidate.Rank == bestRank)];
        if (bestCandidates.Count > 1)
        {
            string candidateList = string.Join(
                ", ",
                bestCandidates.Select(candidate => Path.GetRelativePath(fullSearchDirectory, candidate.FullPath).Replace('\\', '/')));
            error = $"--workspace auto found multiple workspace candidates: {candidateList}. Pass --workspace explicitly.";
            return false;
        }

        workspace = bestCandidates[0].FullPath;
        error = null;
        return true;
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

    private static WorkspacePathResolution ResolveWorkspacePath(FileInfo workspace)
    {
        string input = workspace.ToString();
        string workspacePath;
        if (string.Equals(input.Trim(), "auto", StringComparison.Ordinal))
        {
            string searchDirectory = PathDisplay.FindRepositoryRoot(Directory.GetCurrentDirectory()) ?? Directory.GetCurrentDirectory();
            if (!TryResolveAutoWorkspace(searchDirectory, out workspacePath, out string? error))
            {
                throw new CodeWorkspaceException(DiagnosticIds.InvalidWorkspacePath, error ?? "Failed to resolve --workspace auto.");
            }
        }
        else
        {
            IReadOnlyList<string> candidates = PathDisplay.GetInputPathCandidates(input, anchorPath: null);
            workspacePath = candidates.FirstOrDefault(File.Exists) ?? candidates[0];
        }

        string extension = Path.GetExtension(workspacePath);
        if (extension.Equals(".code-workspace", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveCodeWorkspace(workspacePath);
        }

        string? workspaceKind = GetWorkspaceKind(workspacePath);
        if (workspaceKind is null)
        {
            throw new CodeWorkspaceException(
                DiagnosticIds.InvalidWorkspaceExtension,
                "Workspace must be a .code-workspace, .slnx, .sln, or .csproj file.");
        }

        return new WorkspacePathResolution(Path.GetFullPath(workspacePath), workspaceKind, []);
    }

    private static WorkspacePathResolution ResolveCodeWorkspace(string codeWorkspacePath)
    {
        string fullPath = Path.GetFullPath(codeWorkspacePath);
        if (!File.Exists(fullPath))
        {
            return new WorkspacePathResolution(fullPath, "code-workspace", []);
        }

        List<WorkspaceLoadDiagnostic> diagnostics = [];
        CodeWorkspaceDocument document = ReadCodeWorkspace(fullPath);
        string codeWorkspaceDirectory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        string? repositoryRoot = PathDisplay.FindRepositoryRoot(codeWorkspaceDirectory);

        List<WorkspaceCandidate> candidates = [];
        foreach (string folderPath in document.FolderPaths)
        {
            string folderFullPath = Path.IsPathRooted(folderPath)
                ? Path.GetFullPath(folderPath)
                : Path.GetFullPath(Path.Combine(codeWorkspaceDirectory, folderPath));

            if (repositoryRoot is not null && IsOutsideRoot(folderFullPath, repositoryRoot))
            {
                diagnostics.Add(new WorkspaceLoadDiagnostic(
                    Kind: "Warning",
                    Message: $"VS Code workspace folder is outside repository root: {folderFullPath}"));
            }

            if (File.Exists(folderFullPath) && IsWorkspaceCandidate(folderFullPath, includeCodeWorkspace: false))
            {
                AddCandidate(candidates, folderFullPath, codeWorkspaceDirectory);
                continue;
            }

            if (!Directory.Exists(folderFullPath))
            {
                diagnostics.Add(new WorkspaceLoadDiagnostic(
                    Kind: "Warning",
                    Message: $"VS Code workspace folder does not exist: {folderFullPath}"));
                continue;
            }

            foreach (WorkspaceCandidate candidate in FindWorkspaceCandidates(folderFullPath, includeCodeWorkspace: false))
            {
                AddCandidate(candidates, candidate.FullPath, codeWorkspaceDirectory);
            }
        }

        if (candidates.Count == 0)
        {
            throw new CodeWorkspaceException(
                DiagnosticIds.CodeWorkspaceNoCandidates,
                $"VS Code workspace did not contain a .slnx, .sln, or .csproj candidate: {fullPath}");
        }

        int bestRank = candidates.Min(candidate => candidate.Rank);
        IReadOnlyList<WorkspaceCandidate> bestCandidates = [.. candidates
            .Where(candidate => candidate.Rank == bestRank)
            .OrderBy(candidate => candidate.DisplayPath, StringComparer.OrdinalIgnoreCase)];
        if (bestCandidates.Count > 1)
        {
            string candidateList = string.Join(", ", bestCandidates.Select(candidate => candidate.DisplayPath));
            throw new CodeWorkspaceException(
                DiagnosticIds.AmbiguousCodeWorkspace,
                $"VS Code workspace contains multiple workspace candidates: {candidateList}. Pass --workspace explicitly.");
        }

        string selectedWorkspace = bestCandidates[0].FullPath;
        string? workspaceKind = GetWorkspaceKind(selectedWorkspace);
        if (workspaceKind is null)
        {
            throw new CodeWorkspaceException(
                DiagnosticIds.InvalidWorkspaceExtension,
                "Workspace must be a .slnx, .sln, or .csproj file.");
        }

        return new WorkspacePathResolution(selectedWorkspace, workspaceKind, diagnostics);
    }

    private static CodeWorkspaceDocument ReadCodeWorkspace(string fullPath)
    {
        try
        {
            using FileStream stream = File.OpenRead(fullPath);
            using JsonDocument document = JsonDocument.Parse(
                stream,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });

            if (!document.RootElement.TryGetProperty("folders", out JsonElement folders) ||
                folders.ValueKind != JsonValueKind.Array)
            {
                throw new CodeWorkspaceException(
                    DiagnosticIds.InvalidCodeWorkspace,
                    $"VS Code workspace must contain a folders array: {fullPath}");
            }

            List<string> folderPaths = [];
            foreach (JsonElement folder in folders.EnumerateArray())
            {
                if (folder.ValueKind != JsonValueKind.Object ||
                    !folder.TryGetProperty("path", out JsonElement pathElement) ||
                    pathElement.ValueKind != JsonValueKind.String)
                {
                    throw new CodeWorkspaceException(
                        DiagnosticIds.InvalidCodeWorkspace,
                        $"VS Code workspace folders must contain string path values: {fullPath}");
                }

                string? folderPath = pathElement.GetString();
                if (string.IsNullOrWhiteSpace(folderPath))
                {
                    throw new CodeWorkspaceException(
                        DiagnosticIds.InvalidCodeWorkspace,
                        $"VS Code workspace folders must contain non-empty path values: {fullPath}");
                }

                folderPaths.Add(folderPath);
            }

            return new CodeWorkspaceDocument(folderPaths);
        }
        catch (CodeWorkspaceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new CodeWorkspaceException(
                DiagnosticIds.InvalidCodeWorkspace,
                $"Invalid VS Code workspace file: {ex.Message}");
        }
    }

    private static IReadOnlyList<WorkspaceCandidate> FindWorkspaceCandidates(string searchDirectory, bool includeCodeWorkspace)
    {
        string fullSearchDirectory = Path.GetFullPath(searchDirectory);
        return [.. Directory.EnumerateFiles(fullSearchDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => IsWorkspaceCandidate(path, includeCodeWorkspace))
            .Select(path => CreateWorkspaceCandidate(path, fullSearchDirectory))
            .OrderBy(candidate => candidate.Rank)
            .ThenBy(candidate => candidate.DisplayPath, StringComparer.OrdinalIgnoreCase)];
    }

    private static bool IsWorkspaceCandidate(string path, bool includeCodeWorkspace)
    {
        string extension = Path.GetExtension(path);
        return (includeCodeWorkspace && extension.Equals(".code-workspace", StringComparison.OrdinalIgnoreCase)) ||
            extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".sln", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddCandidate(List<WorkspaceCandidate> candidates, string path, string displayRoot)
    {
        string fullPath = Path.GetFullPath(path);
        if (candidates.Any(candidate => PathComparer.Equals(candidate.FullPath, fullPath)))
        {
            return;
        }

        candidates.Add(CreateWorkspaceCandidate(fullPath, displayRoot));
    }

    private static WorkspaceCandidate CreateWorkspaceCandidate(string path, string displayRoot)
    {
        string fullPath = Path.GetFullPath(path);
        return new WorkspaceCandidate(
            FullPath: fullPath,
            Rank: GetWorkspaceCandidateRank(fullPath),
            DisplayPath: Path.GetRelativePath(displayRoot, fullPath).Replace('\\', '/'));
    }

    private static int GetWorkspaceCandidateRank(string path)
    {
        string extension = Path.GetExtension(path);
        if (extension.Equals(".code-workspace", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (extension.Equals(".sln", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 3;
    }

    private static bool IsOutsideRoot(string path, string root)
    {
        string relativePath = Path.GetRelativePath(root, path);
        return relativePath == ".." ||
            relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal) ||
            Path.IsPathRooted(relativePath);
    }

    private static void RegisterMSBuild()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            try
            {
                MSBuildLocator.RegisterDefaults();
            }
            catch when (TryFindDotNetSdkMSBuildPath(out string? msbuildPath))
            {
                MSBuildLocator.RegisterMSBuildPath(msbuildPath);
            }
        }
    }

    private static bool TryFindDotNetSdkMSBuildPath(out string msbuildPath)
    {
        foreach (string dotnetRoot in GetDotNetRoots())
        {
            string sdkDirectory = Path.Combine(dotnetRoot, "sdk");
            if (!Directory.Exists(sdkDirectory))
            {
                continue;
            }

            foreach (string candidate in Directory.EnumerateDirectories(sdkDirectory).OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                string candidateMSBuildPath = Path.Combine(candidate, "MSBuild.dll");
                if (File.Exists(candidateMSBuildPath))
                {
                    msbuildPath = candidate;
                    return true;
                }
            }
        }

        msbuildPath = "";
        return false;
    }

    private static IReadOnlyList<string> GetDotNetRoots()
    {
        List<string> roots = [];
        AddDotNetRoot(roots, Environment.GetEnvironmentVariable("DOTNET_ROOT"));
        AddDotNetRoot(roots, Environment.GetEnvironmentVariable("DOTNET_ROOT(x86)"));

        string? path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            string executableName = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
            foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string candidate = Path.Combine(directory, executableName);
                if (File.Exists(candidate))
                {
                    AddDotNetRoot(roots, Path.GetDirectoryName(candidate));
                }
            }
        }

        return roots;
    }

    private static void AddDotNetRoot(List<string> roots, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        string fullPath = Path.GetFullPath(path);
        if (!roots.Contains(fullPath, PathComparer))
        {
            roots.Add(fullPath);
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

internal sealed record WorkspacePathResolution(
    string FullPath,
    string Kind,
    IReadOnlyList<WorkspaceLoadDiagnostic> Diagnostics);

internal sealed record CodeWorkspaceDocument(IReadOnlyList<string> FolderPaths);

internal sealed record WorkspaceCandidate(string FullPath, int Rank, string DisplayPath);

internal sealed class CodeWorkspaceException(int diagnosticId, string message) : Exception(message)
{
    public int DiagnosticId { get; } = diagnosticId;
}

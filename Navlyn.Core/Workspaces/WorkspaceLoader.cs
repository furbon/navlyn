using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Navlyn.Diagnostics;
using Navlyn.Languages;
using Navlyn.Paths;
using System.IO.Enumeration;
using System.Text.Json;

namespace Navlyn.Workspaces;

internal sealed class WorkspaceLoader
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public Task<WorkspaceLoadResult> LoadAsync(FileInfo workspace, CancellationToken cancellationToken)
    {
        return LoadAsync(workspace, WorkspaceLoadOptions.Default, cancellationToken);
    }

    public async Task<WorkspaceLoadResult> LoadAsync(
        FileInfo workspace,
        WorkspaceLoadOptions options,
        CancellationToken cancellationToken)
    {
        WorkspacePathResolution resolution;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using IDisposable? timing = options.Timing?.Measure("workspace.discovery");
            resolution = ResolveWorkspacePath(workspace, options);
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
            using IDisposable? timing = options.Timing?.Measure("workspace.msbuild-registration");
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
            using (options.Timing?.Measure("workspace.msbuild-load"))
            {
                if (workspaceKind == "solution")
                {
                    solution = await msbuildWorkspace.OpenSolutionAsync(workspacePath, cancellationToken: cancellationToken);
                }
                else
                {
                    Project project = await msbuildWorkspace.OpenProjectAsync(workspacePath, cancellationToken: cancellationToken);
                    solution = project.Solution;
                }
            }

            IReadOnlyList<LoadedProject> projects;
            using (options.Timing?.Measure("workspace.project-selection"))
            {
                projects = GetProjects(solution);
            }

            DocumentIndex documentIndex;
            using (options.Timing?.Measure("workspace.document-index"))
            {
                documentIndex = DocumentIndexProvider.GetOrCreate(solution);
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
                Projects: projects,
                DocumentIndex: documentIndex);

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

        IReadOnlyList<WorkspaceCandidate> candidates = FindWorkspaceCandidates(
            fullSearchDirectory,
            includeCodeWorkspace: true,
            includeNavlynWorkspace: true);
        if (candidates.Count == 0)
        {
            error = $"--workspace auto could not find a {SourceLanguageFacts.WorkspaceExtensionDisplay} in {fullSearchDirectory}.";
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
            ".csproj" or ".vbproj" => "project",
            _ => null
        };
    }

    private static WorkspacePathResolution ResolveWorkspacePath(FileInfo workspace, WorkspaceLoadOptions options)
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

        if (IsNavlynWorkspaceFile(workspacePath))
        {
            return ResolveNavlynWorkspace(workspacePath, options);
        }

        string extension = Path.GetExtension(workspacePath);
        if (extension.Equals(".code-workspace", StringComparison.OrdinalIgnoreCase))
        {
            WorkspaceRootPolicyContext context = CreateDefaultPolicyContext(
                workspacePath,
                options.RootPolicyOverride ?? WorkspaceRootPolicy.All);
            return ResolveCodeWorkspace(workspacePath, context);
        }

        string? workspaceKind = GetWorkspaceKind(workspacePath);
        if (workspaceKind is null)
        {
            throw new CodeWorkspaceException(
                DiagnosticIds.InvalidWorkspaceExtension,
                $"Workspace must be a {SourceLanguageFacts.WorkspaceExtensionDisplay} file.");
        }

        return new WorkspacePathResolution(Path.GetFullPath(workspacePath), workspaceKind, []);
    }

    private static WorkspacePathResolution ResolveCodeWorkspace(
        string codeWorkspacePath,
        WorkspaceRootPolicyContext policyContext)
    {
        string fullPath = Path.GetFullPath(codeWorkspacePath);
        if (!File.Exists(fullPath))
        {
            return new WorkspacePathResolution(fullPath, "code-workspace", []);
        }

        List<WorkspaceLoadDiagnostic> diagnostics = [];
        CodeWorkspaceDocument document = ReadCodeWorkspace(fullPath);
        string codeWorkspaceDirectory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();

        List<WorkspaceCandidate> candidates = [];
        foreach (string folderPath in document.FolderPaths)
        {
            string folderFullPath = Path.IsPathRooted(folderPath)
                ? Path.GetFullPath(folderPath)
                : Path.GetFullPath(Path.Combine(codeWorkspaceDirectory, folderPath));

            if (!TryApplyRootPolicy(
                folderFullPath,
                policyContext,
                "VS Code workspace folder",
                diagnostics,
                out CodeWorkspaceException? policyError))
            {
                throw policyError!;
            }

            if (File.Exists(folderFullPath) && IsWorkspaceCandidate(folderFullPath, includeCodeWorkspace: false, includeNavlynWorkspace: false))
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

            foreach (WorkspaceCandidate candidate in FindWorkspaceCandidates(
                folderFullPath,
                includeCodeWorkspace: false,
                includeNavlynWorkspace: false))
            {
                AddCandidate(candidates, candidate.FullPath, codeWorkspaceDirectory);
            }
        }

        if (candidates.Count == 0)
        {
            throw new CodeWorkspaceException(
                DiagnosticIds.CodeWorkspaceNoCandidates,
                $"VS Code workspace did not contain a {SourceLanguageFacts.SolutionProjectExtensionDisplay} candidate: {fullPath}");
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
                $"Workspace must be a {SourceLanguageFacts.SolutionProjectExtensionDisplay} file.");
        }

        return new WorkspacePathResolution(selectedWorkspace, workspaceKind, diagnostics);
    }

    private static WorkspacePathResolution ResolveNavlynWorkspace(
        string navlynWorkspacePath,
        WorkspaceLoadOptions options)
    {
        string fullPath = Path.GetFullPath(navlynWorkspacePath);
        if (!File.Exists(fullPath))
        {
            return new WorkspacePathResolution(fullPath, "navlyn-workspace", []);
        }

        List<WorkspaceLoadDiagnostic> diagnostics = [];
        NavlynWorkspaceDocument document = ReadNavlynWorkspace(fullPath);
        string workspaceDirectory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        WorkspaceRootPolicyContext policyContext = CreateNavlynWorkspacePolicyContext(
            workspaceDirectory,
            document,
            options.RootPolicyOverride);

        if (!string.IsNullOrWhiteSpace(document.PrimaryWorkspace))
        {
            string primaryWorkspace = ResolveConfigPath(workspaceDirectory, document.PrimaryWorkspace);
            if (!TryApplyRootPolicy(
                primaryWorkspace,
                policyContext,
                "Navlyn workspace primaryWorkspace",
                diagnostics,
                out CodeWorkspaceException? policyError))
            {
                throw policyError!;
            }

            return ResolveSelectedWorkspace(primaryWorkspace, diagnostics, policyContext);
        }

        List<WorkspaceCandidate> candidates = [];
        IReadOnlyList<string> candidateInputs = document.WorkspaceCandidates.Count == 0
            ? ["."]
            : document.WorkspaceCandidates;

        foreach (string candidateInput in candidateInputs)
        {
            string candidatePath = ResolveConfigPath(workspaceDirectory, candidateInput);
            if (!TryApplyRootPolicy(
                candidatePath,
                policyContext,
                "Navlyn workspace candidate",
                diagnostics,
                out CodeWorkspaceException? policyError))
            {
                throw policyError!;
            }

            if (File.Exists(candidatePath) && IsWorkspaceCandidate(candidatePath, includeCodeWorkspace: true, includeNavlynWorkspace: false))
            {
                AddNavlynWorkspaceCandidate(candidates, candidatePath, workspaceDirectory, document);
                continue;
            }

            if (!Directory.Exists(candidatePath))
            {
                diagnostics.Add(new WorkspaceLoadDiagnostic(
                    Kind: "Warning",
                    Message: $"Navlyn workspace candidate does not exist: {candidatePath}"));
                continue;
            }

            foreach (WorkspaceCandidate candidate in FindWorkspaceCandidates(
                candidatePath,
                includeCodeWorkspace: true,
                includeNavlynWorkspace: false))
            {
                AddNavlynWorkspaceCandidate(candidates, candidate.FullPath, workspaceDirectory, document);
            }
        }

        if (candidates.Count == 0)
        {
            throw new CodeWorkspaceException(
                DiagnosticIds.NavlynWorkspaceNoCandidates,
                $"Navlyn workspace did not contain a .code-workspace, {SourceLanguageFacts.SolutionProjectExtensionDisplay} candidate: {fullPath}");
        }

        int bestRank = candidates.Min(candidate => candidate.Rank);
        IReadOnlyList<WorkspaceCandidate> bestCandidates = [.. candidates
            .Where(candidate => candidate.Rank == bestRank)
            .OrderBy(candidate => candidate.DisplayPath, StringComparer.OrdinalIgnoreCase)];
        if (bestCandidates.Count > 1)
        {
            string candidateList = string.Join(", ", bestCandidates.Select(candidate => candidate.DisplayPath));
            throw new CodeWorkspaceException(
                DiagnosticIds.AmbiguousNavlynWorkspace,
                $"Navlyn workspace contains multiple workspace candidates: {candidateList}. Set primaryWorkspace or pass --workspace explicitly.");
        }

        return ResolveSelectedWorkspace(bestCandidates[0].FullPath, diagnostics, policyContext);
    }

    private static WorkspacePathResolution ResolveSelectedWorkspace(
        string selectedWorkspace,
        List<WorkspaceLoadDiagnostic> diagnostics,
        WorkspaceRootPolicyContext policyContext)
    {
        if (IsNavlynWorkspaceFile(selectedWorkspace))
        {
            throw new CodeWorkspaceException(
                DiagnosticIds.InvalidNavlynWorkspace,
                "navlyn.workspace.json cannot select another navlyn.workspace.json file.");
        }

        if (Path.GetExtension(selectedWorkspace).Equals(".code-workspace", StringComparison.OrdinalIgnoreCase))
        {
            WorkspacePathResolution codeWorkspaceResolution = ResolveCodeWorkspace(selectedWorkspace, policyContext);
            diagnostics.AddRange(codeWorkspaceResolution.Diagnostics);
            return codeWorkspaceResolution with { Diagnostics = diagnostics };
        }

        string? workspaceKind = GetWorkspaceKind(selectedWorkspace);
        if (workspaceKind is null)
        {
            throw new CodeWorkspaceException(
                DiagnosticIds.InvalidWorkspaceExtension,
                $"Workspace must be a .code-workspace, {SourceLanguageFacts.SolutionProjectExtensionDisplay} file.");
        }

        return new WorkspacePathResolution(Path.GetFullPath(selectedWorkspace), workspaceKind, diagnostics);
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

    private static NavlynWorkspaceDocument ReadNavlynWorkspace(string fullPath)
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

            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new CodeWorkspaceException(
                    DiagnosticIds.InvalidNavlynWorkspace,
                    $"Navlyn workspace must be a JSON object: {fullPath}");
            }

            string? primaryWorkspace = ReadOptionalString(root, "primaryWorkspace", fullPath);
            IReadOnlyList<string> workspaceCandidates = ReadOptionalStringArray(root, "workspaceCandidates", fullPath);
            IReadOnlyList<string> excludes = ReadOptionalStringArray(root, "excludes", fullPath);
            IReadOnlyList<string> generatedFolders = ReadOptionalStringArray(root, "generatedFolders", fullPath);
            IReadOnlyList<string> allowRoots = ReadOptionalStringArray(root, "allowRoots", fullPath);
            WorkspaceRootPolicy? defaultRootPolicy = ReadOptionalRootPolicy(root, "defaultRootPolicy", fullPath);
            NavlynWorkspaceTests tests = ReadOptionalTests(root, fullPath);
            NavlynWorkspaceCacheHints cacheHints = ReadOptionalCacheHints(root, fullPath);

            return new NavlynWorkspaceDocument(
                primaryWorkspace,
                workspaceCandidates,
                excludes,
                generatedFolders,
                allowRoots,
                defaultRootPolicy,
                tests,
                cacheHints);
        }
        catch (CodeWorkspaceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new CodeWorkspaceException(
                DiagnosticIds.InvalidNavlynWorkspace,
                $"Invalid Navlyn workspace file: {ex.Message}");
        }
    }

    private static IReadOnlyList<WorkspaceCandidate> FindWorkspaceCandidates(
        string searchDirectory,
        bool includeCodeWorkspace,
        bool includeNavlynWorkspace)
    {
        string fullSearchDirectory = Path.GetFullPath(searchDirectory);
        return [.. Directory.EnumerateFiles(fullSearchDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => IsWorkspaceCandidate(path, includeCodeWorkspace, includeNavlynWorkspace))
            .Select(path => CreateWorkspaceCandidate(path, fullSearchDirectory))
            .OrderBy(candidate => candidate.Rank)
            .ThenBy(candidate => candidate.DisplayPath, StringComparer.OrdinalIgnoreCase)];
    }

    private static bool IsWorkspaceCandidate(string path, bool includeCodeWorkspace, bool includeNavlynWorkspace)
    {
        string extension = Path.GetExtension(path);
        return (includeNavlynWorkspace && IsNavlynWorkspaceFile(path)) ||
            (includeCodeWorkspace && extension.Equals(".code-workspace", StringComparison.OrdinalIgnoreCase)) ||
            extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".sln", StringComparison.OrdinalIgnoreCase) ||
            SourceLanguageFacts.IsSupportedProjectFile(path);
    }

    private static bool IsNavlynWorkspaceFile(string path)
    {
        return Path.GetFileName(path).Equals("navlyn.workspace.json", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddNavlynWorkspaceCandidate(
        List<WorkspaceCandidate> candidates,
        string path,
        string displayRoot,
        NavlynWorkspaceDocument document)
    {
        string fullPath = Path.GetFullPath(path);
        if (IsExcludedByNavlynWorkspace(fullPath, displayRoot, document))
        {
            return;
        }

        AddCandidate(candidates, fullPath, displayRoot);
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
        if (IsNavlynWorkspaceFile(path))
        {
            return 0;
        }

        if (extension.Equals(".code-workspace", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (extension.Equals(".sln", StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        return 4;
    }

    private static bool TryApplyRootPolicy(
        string path,
        WorkspaceRootPolicyContext context,
        string description,
        List<WorkspaceLoadDiagnostic> diagnostics,
        out CodeWorkspaceException? error)
    {
        string fullPath = Path.GetFullPath(path);
        bool outsideRoot = IsOutsideRoot(fullPath, context.Root);
        bool allowedByList = !outsideRoot || context.AllowRoots.Any(root => !IsOutsideRoot(fullPath, root));

        error = null;
        if (context.Policy == WorkspaceRootPolicy.All)
        {
            if (outsideRoot && context.WarnWhenOutsideRoot)
            {
                diagnostics.Add(new WorkspaceLoadDiagnostic(
                    Kind: "Warning",
                    Message: $"{description} is outside repository root: {fullPath}"));
            }

            return true;
        }

        if (context.Policy == WorkspaceRootPolicy.AllowListed && allowedByList)
        {
            return true;
        }

        if (context.Policy == WorkspaceRootPolicy.RepoRelative && !outsideRoot)
        {
            return true;
        }

        error = new CodeWorkspaceException(
            DiagnosticIds.WorkspaceRootPolicyViolation,
            $"{description} is outside the allowed workspace roots for policy {FormatWorkspaceRootPolicy(context.Policy)}: {fullPath}");
        return false;
    }

    private static WorkspaceRootPolicyContext CreateDefaultPolicyContext(
        string workspacePath,
        WorkspaceRootPolicy policy)
    {
        string workspaceDirectory = Path.GetDirectoryName(Path.GetFullPath(workspacePath)) ?? Directory.GetCurrentDirectory();
        string? repositoryRoot = PathDisplay.FindRepositoryRoot(workspaceDirectory);
        string root = repositoryRoot ?? workspaceDirectory;
        return new WorkspaceRootPolicyContext(policy, root, [], WarnWhenOutsideRoot: repositoryRoot is not null);
    }

    private static WorkspaceRootPolicyContext CreateNavlynWorkspacePolicyContext(
        string workspaceDirectory,
        NavlynWorkspaceDocument document,
        WorkspaceRootPolicy? rootPolicyOverride)
    {
        string root = PathDisplay.FindRepositoryRoot(workspaceDirectory) ?? workspaceDirectory;
        WorkspaceRootPolicy policy = rootPolicyOverride ?? document.DefaultRootPolicy ?? WorkspaceRootPolicy.All;
        IReadOnlyList<string> allowRoots = [.. document.AllowRoots
            .Select(path => ResolveConfigPath(workspaceDirectory, path))
            .Select(Path.GetFullPath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)];
        return new WorkspaceRootPolicyContext(policy, root, allowRoots, WarnWhenOutsideRoot: true);
    }

    internal static string FormatWorkspaceRootPolicy(WorkspaceRootPolicy policy)
    {
        return policy switch
        {
            WorkspaceRootPolicy.RepoRelative => "repo-relative",
            WorkspaceRootPolicy.AllowListed => "allow-listed",
            WorkspaceRootPolicy.All => "all",
            _ => "all"
        };
    }

    internal static bool TryParseWorkspaceRootPolicy(string value, out WorkspaceRootPolicy policy)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "repo-relative":
                policy = WorkspaceRootPolicy.RepoRelative;
                return true;
            case "allow-listed":
                policy = WorkspaceRootPolicy.AllowListed;
                return true;
            case "all":
                policy = WorkspaceRootPolicy.All;
                return true;
            default:
                policy = WorkspaceRootPolicy.All;
                return false;
        }
    }

    private static string ResolveConfigPath(string configDirectory, string path)
    {
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(configDirectory, path));
    }

    private static bool IsExcludedByNavlynWorkspace(
        string candidatePath,
        string configDirectory,
        NavlynWorkspaceDocument document)
    {
        string relativePath = Path.GetRelativePath(configDirectory, candidatePath).Replace('\\', '/');
        if (document.Excludes.Any(pattern => MatchesPathPattern(relativePath, pattern)) ||
            document.GeneratedFolders.Any(pattern => MatchesPathPattern(relativePath, pattern)))
        {
            return true;
        }

        return !document.Tests.Include && IsTestCandidate(relativePath);
    }

    private static bool MatchesPathPattern(string relativePath, string pattern)
    {
        string normalizedPattern = pattern.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalizedPattern))
        {
            return false;
        }

        if (normalizedPattern.Contains('*') ||
            normalizedPattern.Contains('?'))
        {
            return FileSystemName.MatchesSimpleExpression(normalizedPattern, relativePath, ignoreCase: true);
        }

        return relativePath.Equals(normalizedPattern, StringComparison.OrdinalIgnoreCase) ||
            relativePath.StartsWith($"{normalizedPattern}/", StringComparison.OrdinalIgnoreCase) ||
            relativePath.Contains($"/{normalizedPattern}/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTestCandidate(string relativePath)
    {
        string fileName = Path.GetFileNameWithoutExtension(relativePath);
        if (fileName.Contains("Test", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment.Equals("test", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("tests", StringComparison.OrdinalIgnoreCase) ||
                segment.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase));
    }

    private static string? ReadOptionalString(JsonElement root, string propertyName, string fullPath)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement property))
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            throw new CodeWorkspaceException(
                DiagnosticIds.InvalidNavlynWorkspace,
                $"Navlyn workspace {propertyName} must be a string: {fullPath}");
        }

        string? value = property.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new CodeWorkspaceException(
                DiagnosticIds.InvalidNavlynWorkspace,
                $"Navlyn workspace {propertyName} must not be empty: {fullPath}");
        }

        return value;
    }

    private static IReadOnlyList<string> ReadOptionalStringArray(JsonElement root, string propertyName, string fullPath)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement property))
        {
            return [];
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            throw new CodeWorkspaceException(
                DiagnosticIds.InvalidNavlynWorkspace,
                $"Navlyn workspace {propertyName} must be an array of strings: {fullPath}");
        }

        List<string> values = [];
        foreach (JsonElement item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new CodeWorkspaceException(
                    DiagnosticIds.InvalidNavlynWorkspace,
                    $"Navlyn workspace {propertyName} must be an array of strings: {fullPath}");
            }

            string? value = item.GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new CodeWorkspaceException(
                    DiagnosticIds.InvalidNavlynWorkspace,
                    $"Navlyn workspace {propertyName} values must not be empty: {fullPath}");
            }

            values.Add(value);
        }

        return values;
    }

    private static WorkspaceRootPolicy? ReadOptionalRootPolicy(JsonElement root, string propertyName, string fullPath)
    {
        string? value = ReadOptionalString(root, propertyName, fullPath);
        if (value is null)
        {
            return null;
        }

        if (!TryParseWorkspaceRootPolicy(value, out WorkspaceRootPolicy policy))
        {
            throw new CodeWorkspaceException(
                DiagnosticIds.InvalidNavlynWorkspace,
                $"Navlyn workspace {propertyName} must be one of: repo-relative, allow-listed, all.");
        }

        return policy;
    }

    private static NavlynWorkspaceTests ReadOptionalTests(JsonElement root, string fullPath)
    {
        if (!root.TryGetProperty("tests", out JsonElement tests))
        {
            return new NavlynWorkspaceTests(Include: true, Projects: []);
        }

        if (tests.ValueKind != JsonValueKind.Object)
        {
            throw new CodeWorkspaceException(
                DiagnosticIds.InvalidNavlynWorkspace,
                $"Navlyn workspace tests must be an object: {fullPath}");
        }

        bool include = true;
        if (tests.TryGetProperty("include", out JsonElement includeElement))
        {
            if (includeElement.ValueKind != JsonValueKind.True && includeElement.ValueKind != JsonValueKind.False)
            {
                throw new CodeWorkspaceException(
                    DiagnosticIds.InvalidNavlynWorkspace,
                    $"Navlyn workspace tests.include must be a boolean: {fullPath}");
            }

            include = includeElement.GetBoolean();
        }

        IReadOnlyList<string> projects = ReadOptionalStringArray(tests, "projects", fullPath);
        return new NavlynWorkspaceTests(include, projects);
    }

    private static NavlynWorkspaceCacheHints ReadOptionalCacheHints(JsonElement root, string fullPath)
    {
        if (!root.TryGetProperty("cacheHints", out JsonElement cacheHints))
        {
            return new NavlynWorkspaceCacheHints(Enabled: null, Directory: null);
        }

        if (cacheHints.ValueKind != JsonValueKind.Object)
        {
            throw new CodeWorkspaceException(
                DiagnosticIds.InvalidNavlynWorkspace,
                $"Navlyn workspace cacheHints must be an object: {fullPath}");
        }

        bool? enabled = null;
        if (cacheHints.TryGetProperty("enabled", out JsonElement enabledElement))
        {
            if (enabledElement.ValueKind != JsonValueKind.True && enabledElement.ValueKind != JsonValueKind.False)
            {
                throw new CodeWorkspaceException(
                    DiagnosticIds.InvalidNavlynWorkspace,
                    $"Navlyn workspace cacheHints.enabled must be a boolean: {fullPath}");
            }

            enabled = enabledElement.GetBoolean();
        }

        string? directory = ReadOptionalString(cacheHints, "directory", fullPath);
        return new NavlynWorkspaceCacheHints(enabled, directory);
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
    IReadOnlyList<LoadedProject> Projects,
    DocumentIndex? DocumentIndex = null)
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

internal sealed record NavlynWorkspaceDocument(
    string? PrimaryWorkspace,
    IReadOnlyList<string> WorkspaceCandidates,
    IReadOnlyList<string> Excludes,
    IReadOnlyList<string> GeneratedFolders,
    IReadOnlyList<string> AllowRoots,
    WorkspaceRootPolicy? DefaultRootPolicy,
    NavlynWorkspaceTests Tests,
    NavlynWorkspaceCacheHints CacheHints);

internal sealed record NavlynWorkspaceTests(bool Include, IReadOnlyList<string> Projects);

internal sealed record NavlynWorkspaceCacheHints(bool? Enabled, string? Directory);

internal sealed record WorkspaceRootPolicyContext(
    WorkspaceRootPolicy Policy,
    string Root,
    IReadOnlyList<string> AllowRoots,
    bool WarnWhenOutsideRoot);

internal sealed record WorkspaceCandidate(string FullPath, int Rank, string DisplayPath);

internal sealed class CodeWorkspaceException(int diagnosticId, string message) : Exception(message)
{
    public int DiagnosticId { get; } = diagnosticId;
}

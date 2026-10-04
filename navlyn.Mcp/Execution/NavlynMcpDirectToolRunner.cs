using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Navlyn.Cli.Commands;
using Navlyn.Cli;
using Navlyn.Cli.OutputProfiles;
using Navlyn.Diagnostics;
using Navlyn.Mcp.Configuration;
using Navlyn.Mcp.Tools;
using Navlyn.Paths;
using Navlyn.RepoGraph;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.Mcp.Execution;

internal sealed class NavlynMcpDirectToolRunner(
    NavlynMcpServerOptions options,
    NavlynMcpWorkspaceCache workspaceCache)
{
    private const int DefaultSymbolSourceMaxLines = 80;
    private const int DefaultSymbolSourceBudgetTokens = 4000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly Action<int>? beforeValidation;

    internal NavlynMcpDirectToolRunner(
        NavlynMcpServerOptions options,
        NavlynMcpWorkspaceCache workspaceCache,
        Action<int> beforeValidation)
        : this(options, workspaceCache)
    {
        this.beforeValidation = beforeValidation;
    }

    public bool CanRun(CommandBuildResult command)
    {
        return command.StandardInput is null &&
            (command.Command is "repo-graph" or "outline" or "read" or "symbol-source" or "workspace-status" or "workspace-refresh" ||
             command.Command == "target" && IsSimpleTargetQuery(command.Arguments) && HasRepositoryDisplayRoot());
    }

    public async Task<NavlynToolResult> RunAsync(
        string toolName,
        CommandBuildResult command,
        CancellationToken cancellationToken)
    {
        NavlynSourceCommand sourceCommand = CreateSourceCommand(command);
        using CliInvocationContext invocation = CliInvocationContext.Begin(sourceCommand.Arguments, options.WorkingDirectory);
        try
        {
            if (command.Command == "workspace-status" && !string.IsNullOrWhiteSpace(options.DaemonPipe))
            {
                NavlynToolResult? daemonStatus = await TryRunDaemonWorkspaceToolAsync(
                    toolName, sourceCommand, command, cancellationToken);
                if (daemonStatus is not null)
                {
                    return daemonStatus;
                }
            }

            for (int attempt = 0; attempt < 2; attempt++)
            {
                NavlynMcpWorkspaceCacheResult cacheResult = command.Command == "workspace-refresh" && attempt == 0
                    ? await workspaceCache.RefreshAsync(cancellationToken)
                    : await workspaceCache.GetAsync(cancellationToken);
                if (cacheResult.Error is not null)
                {
                    return Failed(
                        toolName,
                        sourceCommand,
                        DiagnosticCode(cacheResult.Error.DiagnosticId),
                        cacheResult.Error.Message,
                        cacheResult.Error.ExitCode);
                }

                await using NavlynMcpWorkspaceCache.WorkspaceLease lease = cacheResult.Lease!;
                NavlynMcpWorkspaceCache.CachedWorkspace cachedWorkspace = lease.CachedWorkspace;
                ExternalMemberSnapshot? externalSnapshot = null;
                NavlynToolResult result;
                if (command.Command is "read" or "symbol-source")
                {
                    (result, externalSnapshot) = await RunSymbolSourceAsync(toolName, sourceCommand, cachedWorkspace, lease.CacheHit, command.Arguments, cancellationToken);
                }
                else
                {
                    result = command.Command switch
                    {
                    "workspace-status" => await RunWorkspaceStatusAsync(toolName, sourceCommand, cachedWorkspace, lease.CacheHit, command.Arguments, cancellationToken),
                    "workspace-refresh" => await RunWorkspaceRefreshAsync(toolName, sourceCommand, cachedWorkspace, lease.CacheHit, command.Arguments, cancellationToken),
                    "repo-graph" => RunRepoGraph(toolName, sourceCommand, cachedWorkspace, lease.CacheHit, command.Arguments),
                    "outline" => await RunOutlineAsync(toolName, sourceCommand, cachedWorkspace, lease.CacheHit, command.Arguments, cancellationToken),
                    "target" => await RunTargetAsync(toolName, sourceCommand, cachedWorkspace, lease.CacheHit, command.Arguments, cancellationToken),
                        _ => Failed(toolName, sourceCommand, "NAVLYN_MCP_DIRECT_UNSUPPORTED", $"Direct MCP execution is not available for {command.Command}.")
                    };
                }

                beforeValidation?.Invoke(attempt);
                if (!await workspaceCache.ValidateAsync(lease, cancellationToken))
                {
                    continue;
                }

                if (result.Ok && externalSnapshot is not null &&
                    !await ExternalMemberSourceResolver.ValidateSnapshotAsync(externalSnapshot, cancellationToken))
                {
                    return Failed(toolName, sourceCommand, DiagnosticCode(DiagnosticIds.ExternalMemberStale),
                        "External member binaries changed during the tool call. Retry against the current workspace state.");
                }

                if (command.Command == "workspace-refresh" && result.Ok && !string.IsNullOrWhiteSpace(options.DaemonPipe))
                {
                    NavlynToolResult? daemonRefresh = await TryRunDaemonWorkspaceToolAsync(
                        toolName, sourceCommand, command, cancellationToken);
                    if (daemonRefresh is not null)
                    {
                        return daemonRefresh;
                    }
                }

                return result;
            }

            return Failed(toolName, sourceCommand, "NAVLYN_MCP_STALE_WORKSPACE",
                "Workspace inputs changed during the tool call. Wait for edits to finish, then retry the call.");
        }
        catch (OperationCanceledException)
        {
            return Failed(toolName, sourceCommand, "NAVLYN_MCP_CANCELED", "Tool call was canceled.");
        }
        catch (WorkspaceInputStaleException ex)
        {
            return Failed(toolName, sourceCommand, "NAVLYN_MCP_STALE_WORKSPACE", ex.Message);
        }
        catch (Exception ex)
        {
            return Failed(toolName, sourceCommand, "NAVLYN_MCP_SERVER_ERROR", $"Unexpected MCP direct runner error: {ex.Message}");
        }
    }

    private async Task<NavlynToolResult?> TryRunDaemonWorkspaceToolAsync(
        string toolName,
        NavlynSourceCommand sourceCommand,
        CommandBuildResult command,
        CancellationToken cancellationToken)
    {
        WorkspaceDiskCacheRequest request = CreateCacheRequest(command.Arguments, writeDefault: false);
        string method = command.Command == "workspace-refresh"
            ? WorkspaceDaemonProtocol.RefreshMethod
            : WorkspaceDaemonProtocol.StatusMethod;
        WorkspaceDaemonClientResult daemonResult = await WorkspaceDaemonProtocol.SendAsync(
            options.DaemonPipe!,
            new WorkspaceDaemonRequest(null, method, request),
            WorkspaceDaemonProtocol.DefaultConnectTimeoutMilliseconds,
            cancellationToken);
        if (daemonResult.Response?.Ok != true || daemonResult.Response.Result is null)
        {
            return null;
        }

        return Succeeded(
            toolName,
            sourceCommand,
            daemonResult.Response.Result.Value,
            new NavlynToolMetadata(
                ExecutionPath: "daemon",
                WorkspaceCacheStatus: "daemon",
                WorkspaceCacheHit: true,
                WorkspaceFingerprint: "daemon",
                IndexStatus: "daemon",
                SnapshotId: null,
                CostClass: "workspace-lifecycle"));
    }

    private async Task<NavlynToolResult> RunWorkspaceStatusAsync(
        string toolName,
        NavlynSourceCommand sourceCommand,
        NavlynMcpWorkspaceCache.CachedWorkspace cachedWorkspace,
        bool cacheHit,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        WorkspaceStatusResult output = await WorkspaceDiskCache.CreateStatusAsync(
            "workspace-status",
            cachedWorkspace.Workspace,
            snapshot: null,
            new FileInfo(options.Workspace),
            CreateCacheRequest(arguments, writeDefault: false),
            cancellationToken);
        return Succeeded(toolName, sourceCommand, output, CreateMetadata(cachedWorkspace, cacheHit, "workspace-lifecycle"));
    }

    private async Task<NavlynToolResult> RunWorkspaceRefreshAsync(
        string toolName,
        NavlynSourceCommand sourceCommand,
        NavlynMcpWorkspaceCache.CachedWorkspace cachedWorkspace,
        bool cacheHit,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        WorkspaceStatusResult output = await WorkspaceDiskCache.CreateStatusAsync(
            "workspace-refresh",
            cachedWorkspace.Workspace,
            snapshot: null,
            new FileInfo(options.Workspace),
            CreateCacheRequest(arguments, writeDefault: false),
            cancellationToken);
        return Succeeded(toolName, sourceCommand, output, CreateMetadata(cachedWorkspace, cacheHit, "workspace-lifecycle"));
    }

    private NavlynToolResult RunRepoGraph(
        string toolName,
        NavlynSourceCommand sourceCommand,
        NavlynMcpWorkspaceCache.CachedWorkspace cachedWorkspace,
        bool cacheHit,
        IReadOnlyList<string> arguments)
    {
        IReadOnlyList<string> projectFilters = GetValues(arguments, "--project");
        bool includePackages = GetBoolValue(arguments, "--include-packages") ?? true;
        bool includeMsbuildFiles = GetBoolValue(arguments, "--include-msbuild-files") ?? true;
        bool includePreprocessorSymbols = GetBoolValue(arguments, "--include-preprocessor-symbols") ?? true;
        bool classification = GetBoolValue(arguments, "--classification") ?? true;
        int relationshipLimit = GetIntValue(arguments, "--relationship-limit") ?? 200;
        string profile = GetValue(arguments, "--profile") ?? OutputProfile.Default;

        ProjectFilterResolutionResult projectResolution = new ProjectFilterResolver().ResolveMany(
            cachedWorkspace.Workspace.Solution,
            projectFilters);
        if (projectResolution.Error is not null)
        {
            return Failed(toolName, sourceCommand, projectResolution.Error);
        }

        RepoGraphResult result = new RepoGraphResolver().Resolve(
            cachedWorkspace.Workspace,
            projectResolution.Projects,
            new RepoGraphOptions(
                IncludePackages: includePackages,
                IncludeMsbuildFiles: includeMsbuildFiles,
                IncludePreprocessorSymbols: includePreprocessorSymbols,
                IncludeClassification: classification,
                RelationshipLimit: relationshipLimit));

        object output = OutputProfile.Format(cachedWorkspace.Workspace, "repo-graph", profile, result, new
        {
            projectFilters,
            includePackages,
            includeMsbuildFiles,
            includePreprocessorSymbols,
            classification,
            relationshipLimit
        });

        return Succeeded(
            toolName,
            sourceCommand,
            output,
            CreateMetadata(cachedWorkspace, cacheHit, "workspace-summary"));
    }

    private async Task<NavlynToolResult> RunOutlineAsync(
        string toolName,
        NavlynSourceCommand sourceCommand,
        NavlynMcpWorkspaceCache.CachedWorkspace cachedWorkspace,
        bool cacheHit,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        string file = GetRequiredValue(arguments, "--file");
        string? projectFilter = GetValue(arguments, "--project");
        bool excludeGenerated = HasFlag(arguments, "--exclude-generated");

        ProjectFilterResolutionResult projectResolution = ResolveSingleProject(cachedWorkspace, projectFilter);
        if (projectResolution.Error is not null)
        {
            return Failed(toolName, sourceCommand, projectResolution.Error);
        }

        Project? project = string.IsNullOrWhiteSpace(projectFilter)
            ? null
            : projectResolution.Projects.Single();
        AppliedProjectFilter? appliedProjectFilter = string.IsNullOrWhiteSpace(projectFilter)
            ? null
            : projectResolution.AppliedFilters.Single();

        OutlineResolutionResult result = await new OutlineResolver().ResolveAsync(
            cachedWorkspace.Workspace.Solution,
            CreateFileInfo(file, cachedWorkspace, project),
            project,
            excludeGenerated,
            cancellationToken);

        if (result.Error is not null)
        {
            return Failed(toolName, sourceCommand, result.Error);
        }

        OutlineResolution resolution = result.Resolution!;
        foreach (OutlineEntry entry in resolution.Entries)
        {
            cachedWorkspace.RecordCandidateTarget(entry);
        }

        McpOutlineResult output = new(
            File: resolution.File,
            Project: appliedProjectFilter is null ? null : ProjectFilterOutput.FromAppliedFilter(appliedProjectFilter),
            ExcludeGenerated: excludeGenerated,
            Entries: [.. resolution.Entries.Select(entry => new McpOutlineEntryResult(
                Name: entry.Name,
                Kind: entry.Kind,
                Container: entry.Container,
                Facts: entry.Facts,
                CandidateId: entry.CandidateId,
                Path: entry.Path,
                Line: entry.Line,
                Column: entry.Column,
                EndLine: entry.EndLine,
                EndColumn: entry.EndColumn))]);

        return Succeeded(toolName, sourceCommand, output, CreateMetadata(cachedWorkspace, cacheHit, "cheap-file-first"));
    }

    private async Task<NavlynToolResult> RunTargetAsync(
        string toolName,
        NavlynSourceCommand sourceCommand,
        NavlynMcpWorkspaceCache.CachedWorkspace cachedWorkspace,
        bool cacheHit,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        string query = arguments[1].Trim();
        ProjectFilterResolutionResult projectResolution = new ProjectFilterResolver().ResolveMany(
            cachedWorkspace.Workspace.Solution, []);
        if (projectResolution.Error is not null)
        {
            return Failed(toolName, sourceCommand, projectResolution.Error);
        }

        FuzzyQueryOptions selection = new(
            Query: query,
            AssumeKinds: [],
            Match: "smart",
            CaseSensitive: null,
            ExcludeGenerated: false,
            Limit: null,
            Selection: new FuzzySelectionOptions("select", "medium", false));
        ResolveTargetResult result = await new ResolveTargetResolver().ResolveFuzzyAsync(
            cachedWorkspace.Workspace,
            selection,
            projectResolution.Projects,
            projectFilters: null,
            cancellationToken);

        return Succeeded(toolName, sourceCommand, result with { Command = "target" },
            CreateMetadata(cachedWorkspace, cacheHit, "semantic-target"));
    }

    private static bool IsSimpleTargetQuery(IReadOnlyList<string> arguments)
    {
        return arguments.Count == 2 && arguments[0] == "--query" &&
            !string.IsNullOrWhiteSpace(arguments[1]);
    }

    private bool HasRepositoryDisplayRoot()
    {
        string workspace = options.Workspace;
        string anchor = string.IsNullOrWhiteSpace(workspace) || workspace == "auto"
            ? options.WorkingDirectory
            : Path.IsPathRooted(workspace)
                ? workspace
                : Path.Combine(options.WorkingDirectory, workspace);
        return PathDisplay.FindRepositoryRoot(anchor) is not null;
    }

    private async Task<(NavlynToolResult Result, ExternalMemberSnapshot? Snapshot)> RunSymbolSourceAsync(
        string toolName,
        NavlynSourceCommand sourceCommand,
        NavlynMcpWorkspaceCache.CachedWorkspace cachedWorkspace,
        bool cacheHit,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        string? candidateId = GetValue(arguments, "--candidate-id");
        string? file = GetValue(arguments, "--file");
        int? line = GetIntValue(arguments, "--line");
        int? column = GetIntValue(arguments, "--column");
        string externalSource = GetValue(arguments, "--external-source") ?? "none";
        string? projectFilter = GetValue(arguments, "--project");
        bool excludeGenerated = HasFlag(arguments, "--exclude-generated");
        string view = GetValue(arguments, "--view") ?? "declaration";
        int maxLines = GetIntValue(arguments, "--max-lines") ?? DefaultSymbolSourceMaxLines;
        int budgetTokens = GetIntValue(arguments, "--budget-tokens") ?? DefaultSymbolSourceBudgetTokens;

        ProjectFilterResolutionResult projectResolution = ResolveSingleProject(cachedWorkspace, projectFilter);
        if (projectResolution.Error is not null)
        {
            return (Failed(toolName, sourceCommand, projectResolution.Error), null);
        }

        Project? project = string.IsNullOrWhiteSpace(projectFilter)
            ? null
            : projectResolution.Projects.Single();
        AppliedProjectFilter? appliedProjectFilter = string.IsNullOrWhiteSpace(projectFilter)
            ? null
            : projectResolution.AppliedFilters.Single();
        CandidateSelectionInput? selectionInput = null;

        if (!string.IsNullOrWhiteSpace(candidateId))
        {
            CandidateTargetResolutionResult targetResult = await ResolveCandidateTargetAsync(
                cachedWorkspace,
                candidateId,
                project,
                excludeGenerated,
                cancellationToken);
            if (targetResult.Error is not null)
            {
                return (Failed(toolName, sourceCommand, targetResult.Error), null);
            }

            CandidateTargetResolution target = targetResult.Resolution!;
            file = target.File.ToString();
            line = target.Line;
            column = target.Column;
            project ??= target.Project;
            selectionInput = new CandidateSelectionInput("candidateId", target.CandidateId);
        }

        SymbolSourceResolutionResult result = await new SymbolSourceResolver().ResolveAsync(
            cachedWorkspace.Workspace.Solution,
            CreateFileInfo(file!, cachedWorkspace, project),
            line!.Value,
            column!.Value,
            project,
            excludeGenerated,
            new SymbolSourceOptions(view, maxLines, budgetTokens, externalSource),
            cancellationToken);

        if (result.Error is not null)
        {
            return (Failed(toolName, sourceCommand, result.Error), null);
        }

        SymbolSourceResolution resolution = result.Resolution!;
        McpSymbolSourceResult output = new(
            File: resolution.File,
            Line: resolution.Line,
            Column: resolution.Column,
            Project: appliedProjectFilter is null ? null : ProjectFilterOutput.FromAppliedFilter(appliedProjectFilter),
            SelectionInput: selectionInput,
            ExcludeGenerated: excludeGenerated,
            View: resolution.View,
            Limits: resolution.Limits,
            Symbol: resolution.Symbol,
            Slices: resolution.Slices,
            Truncated: resolution.Truncated,
            Warnings: resolution.Warnings,
            SourceOrigin: resolution.SourceOrigin,
            ExternalAssembly: resolution.ExternalAssembly);

        return (Succeeded(toolName, sourceCommand, output, CreateMetadata(cachedWorkspace, cacheHit, "cheap-file-first")), resolution.ExternalSnapshot);
    }

    private async Task<CandidateTargetResolutionResult> ResolveCandidateTargetAsync(
        NavlynMcpWorkspaceCache.CachedWorkspace cachedWorkspace,
        string candidateId,
        Project? project,
        bool excludeGenerated,
        CancellationToken cancellationToken)
    {
        string normalizedCandidateId = candidateId.Trim();
        if (!FuzzyCandidateIdentity.TryParseCandidateId(normalizedCandidateId))
        {
            return CandidateTargetResolutionResult.Failed(
                DiagnosticIds.InvalidCandidateId,
                $"Invalid candidate id: {normalizedCandidateId}.",
                ExitCodes.UsageError);
        }

        if (cachedWorkspace.TryGetCandidateTarget(normalizedCandidateId, out NavlynMcpCandidateTarget cachedTarget) &&
            (project is null || string.Equals(cachedTarget.ProjectName, project.Name, StringComparison.Ordinal)))
        {
            Project? targetProject = project ?? cachedWorkspace.FindProject(cachedTarget.ProjectName);
            return CandidateTargetResolutionResult.Succeeded(new CandidateTargetResolution(
                normalizedCandidateId,
                CreateFileInfo(cachedTarget.Path, cachedWorkspace, targetProject),
                cachedTarget.Line,
                cachedTarget.Column,
                targetProject));
        }

        IReadOnlyList<Project> projects = project is null
            ? cachedWorkspace.Workspace.Solution.Projects.ToArray()
            : [project];
        CandidateTargetResolutionResult result = await new CandidateTargetResolver().ResolveAsync(
            cachedWorkspace.Workspace.Solution,
            projects,
            normalizedCandidateId,
            excludeGenerated,
            cancellationToken);
        if (result.Resolution is not null)
        {
            cachedWorkspace.RecordCandidateTarget(new NavlynMcpCandidateTarget(
                result.Resolution.CandidateId,
                result.Resolution.File.ToString(),
                result.Resolution.Line,
                result.Resolution.Column,
                result.Resolution.Project?.Name));
        }

        return result;
    }

    private ProjectFilterResolutionResult ResolveSingleProject(
        NavlynMcpWorkspaceCache.CachedWorkspace cachedWorkspace,
        string? projectFilter)
    {
        return new ProjectFilterResolver().ResolveSingle(cachedWorkspace.Workspace.Solution, projectFilter);
    }

    private NavlynToolResult Succeeded<T>(
        string toolName,
        NavlynSourceCommand sourceCommand,
        T result,
        NavlynToolMetadata metadata)
    {
        string json = JsonSerializer.Serialize(result, JsonOptions);
        if (json.Length > options.MaxJsonChars)
        {
            return Failed(
                toolName,
                sourceCommand,
                "NAVLYN_MCP_OUTPUT_TOO_LARGE",
                $"Navlyn direct result JSON exceeded --max-json-chars ({options.MaxJsonChars}). Lower the tool limits and retry.");
        }

        using JsonDocument document = JsonDocument.Parse(json);
        return NavlynToolResult.Succeeded(
            toolName,
            sourceCommand,
            options.WorkspaceArgument,
            document.RootElement,
            metadata);
    }

    private static NavlynToolMetadata CreateMetadata(
        NavlynMcpWorkspaceCache.CachedWorkspace cachedWorkspace,
        bool cacheHit,
        string costClass)
    {
        DocumentIndex documentIndex = cachedWorkspace.Workspace.DocumentIndex ??
            DocumentIndexProvider.GetOrCreate(cachedWorkspace.Workspace.Solution);
        return new NavlynToolMetadata(
            ExecutionPath: "direct",
            WorkspaceCacheStatus: "warm",
            WorkspaceCacheHit: cacheHit,
            WorkspaceFingerprint: cachedWorkspace.Fingerprint,
            IndexStatus: "warm",
            SnapshotId: cachedWorkspace.SnapshotId,
            CostClass: costClass,
            FreshnessStatus: cachedWorkspace.FreshnessStatus,
            DocumentIndexDocumentCount: documentIndex.DocumentCount,
            DocumentIndexEstimatedBytes: documentIndex.EstimatedMemoryBytes);
    }

    private NavlynToolResult Failed(
        string toolName,
        NavlynSourceCommand sourceCommand,
        SymbolNavigationError error)
    {
        return Failed(toolName, sourceCommand, DiagnosticCode(error.DiagnosticId), error.Message, error.ExitCode);
    }

    private NavlynToolResult Failed(
        string toolName,
        NavlynSourceCommand sourceCommand,
        OutlineResolutionError error)
    {
        return Failed(toolName, sourceCommand, DiagnosticCode(error.DiagnosticId), error.Message, error.ExitCode);
    }

    private NavlynToolResult Failed(
        string toolName,
        NavlynSourceCommand sourceCommand,
        ProjectFilterResolutionError error)
    {
        return Failed(toolName, sourceCommand, DiagnosticCode(error.DiagnosticId), error.Message, error.ExitCode);
    }

    private NavlynToolResult Failed(
        string toolName,
        NavlynSourceCommand sourceCommand,
        string code,
        string message,
        int? exitCode = null)
    {
        return NavlynToolResult.Failed(
            toolName,
            sourceCommand,
            options.WorkspaceArgument,
            new NavlynToolError(code, message, exitCode));
    }

    internal NavlynSourceCommand CreateSourceCommand(CommandBuildResult command)
    {
        return new NavlynSourceCommand(
            command.Command!,
            [
                command.Command!,
                "--workspace",
                options.WorkspaceArgument,
                "--workspace-root-policy",
                WorkspaceLoader.FormatWorkspaceRootPolicy(options.WorkspaceRootPolicy),
                .. command.Arguments
            ]);
    }

    private FileInfo CreateFileInfo(
        string path,
        NavlynMcpWorkspaceCache.CachedWorkspace cachedWorkspace,
        Project? project)
    {
        FileInfo workingFile = Path.IsPathRooted(path)
            ? new FileInfo(path)
            : new FileInfo(Path.Combine(options.WorkingDirectory, path));
        Solution solution = cachedWorkspace.Workspace.Solution;
        string? anchor = project?.FilePath ?? solution.FilePath ?? solution.Projects
            .Select(item => item.FilePath)
            .FirstOrDefault(item => item is not null);
        IReadOnlyList<string> candidates =
        [
            workingFile.FullName,
            .. PathDisplay.GetInputPathCandidates(path, anchor),
            .. PathDisplay.GetInputPathCandidates(path, options.WorkingDirectory)
        ];
        DocumentIndex documentIndex = DocumentIndexProvider.GetOrCreate(solution);
        DocumentIndexEntry? entry = documentIndex.Find(candidates, project).Entry ??
            documentIndex.Find(candidates, project: null).Entry;
        return entry is null ? workingFile : new FileInfo(entry.FullPath);
    }

    private static string DiagnosticCode(int diagnosticId)
    {
        return $"{DiagnosticIds.Prefix}{diagnosticId.ToString("D4", System.Globalization.CultureInfo.InvariantCulture)}";
    }

    private static string GetRequiredValue(IReadOnlyList<string> arguments, string option)
    {
        return GetValue(arguments, option) ?? throw new InvalidOperationException($"{option} is required.");
    }

    private static string? GetValue(IReadOnlyList<string> arguments, string option)
    {
        for (int index = 0; index < arguments.Count - 1; index++)
        {
            if (arguments[index] == option)
            {
                return arguments[index + 1];
            }
        }

        return null;
    }

    private static IReadOnlyList<string> GetValues(IReadOnlyList<string> arguments, string option)
    {
        List<string> values = [];
        for (int index = 0; index < arguments.Count - 1; index++)
        {
            if (arguments[index] == option)
            {
                values.Add(arguments[index + 1]);
            }
        }

        return values;
    }

    private static bool? GetBoolValue(IReadOnlyList<string> arguments, string option)
    {
        string? value = GetValue(arguments, option);
        return value is null
            ? null
            : bool.Parse(value);
    }

    private static int? GetIntValue(IReadOnlyList<string> arguments, string option)
    {
        string? value = GetValue(arguments, option);
        return value is null
            ? null
            : int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static WorkspaceDiskCacheRequest CreateCacheRequest(
        IReadOnlyList<string> arguments,
        bool writeDefault)
    {
        return new WorkspaceDiskCacheRequest(
            CacheMode: GetValue(arguments, "--cache") ?? WorkspaceDiskCache.DefaultCacheMode,
            Write: HasFlag(arguments, "--write-cache") || writeDefault,
            Clear: HasFlag(arguments, "--clear-cache"),
            DirectoryOverride: GetValue(arguments, "--cache-directory"));
    }

    private static bool HasFlag(IReadOnlyList<string> arguments, string option)
    {
        return arguments.Contains(option, StringComparer.Ordinal);
    }

    private sealed record McpOutlineResult(
        string File,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        ProjectFilterOutput? Project,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool ExcludeGenerated,
        IReadOnlyList<McpOutlineEntryResult> Entries);

    private sealed record McpOutlineEntryResult(
        string Name,
        string Kind,
        string? Container,
        SymbolFacts Facts,
        string CandidateId,
        string Path,
        int Line,
        int Column,
        int EndLine,
        int EndColumn);

    private sealed record McpSymbolSourceResult(
        string File,
        int Line,
        int Column,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        ProjectFilterOutput? Project,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        CandidateSelectionInput? SelectionInput,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool ExcludeGenerated,
        string View,
        SymbolSourceLimits Limits,
        SymbolSourceSymbol Symbol,
        IReadOnlyList<SymbolSourceSlice> Slices,
        bool Truncated,
        IReadOnlyList<string> Warnings,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? SourceOrigin,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        ExternalAssemblyProvenance? ExternalAssembly);
}

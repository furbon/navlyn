using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Navlyn.Cli.Commands;
using Navlyn.Diagnostics;
using Navlyn.Mcp.Configuration;
using Navlyn.Mcp.Tools;
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

    public bool CanRun(CommandBuildResult command)
    {
        return command.StandardInput is null &&
            command.Command is "outline" or "symbol-source";
    }

    public async Task<NavlynToolResult> RunAsync(
        string toolName,
        CommandBuildResult command,
        CancellationToken cancellationToken)
    {
        NavlynSourceCommand sourceCommand = CreateSourceCommand(command);
        NavlynMcpWorkspaceCacheResult cacheResult;
        try
        {
            cacheResult = await workspaceCache.GetAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return Failed(toolName, sourceCommand, "NAVLYN_MCP_CANCELED", "Tool call was canceled.");
        }
        catch (Exception ex)
        {
            return Failed(toolName, sourceCommand, "NAVLYN_MCP_SERVER_ERROR", $"Unexpected MCP direct runner error: {ex.Message}");
        }

        if (cacheResult.Error is not null)
        {
            return Failed(
                toolName,
                sourceCommand,
                DiagnosticCode(cacheResult.Error.DiagnosticId),
                cacheResult.Error.Message,
                cacheResult.Error.ExitCode);
        }

        NavlynMcpWorkspaceCache.CachedWorkspace cachedWorkspace = cacheResult.CachedWorkspace!;
        NavlynToolMetadata metadata = new(
            ExecutionPath: "direct",
            WorkspaceCacheStatus: "warm",
            WorkspaceCacheHit: cacheResult.CacheHit,
            WorkspaceFingerprint: cachedWorkspace.Fingerprint,
            SnapshotId: cachedWorkspace.Fingerprint,
            CostClass: "cheap-file-first");

        try
        {
            return command.Command switch
            {
                "outline" => await RunOutlineAsync(toolName, sourceCommand, cachedWorkspace, metadata, command.Arguments, cancellationToken),
                "symbol-source" => await RunSymbolSourceAsync(toolName, sourceCommand, cachedWorkspace, metadata, command.Arguments, cancellationToken),
                _ => Failed(toolName, sourceCommand, "NAVLYN_MCP_DIRECT_UNSUPPORTED", $"Direct MCP execution is not available for {command.Command}.")
            };
        }
        catch (OperationCanceledException)
        {
            return Failed(toolName, sourceCommand, "NAVLYN_MCP_CANCELED", "Tool call was canceled.");
        }
        catch (Exception ex)
        {
            return Failed(toolName, sourceCommand, "NAVLYN_MCP_SERVER_ERROR", $"Unexpected MCP direct runner error: {ex.Message}");
        }
    }

    private async Task<NavlynToolResult> RunOutlineAsync(
        string toolName,
        NavlynSourceCommand sourceCommand,
        NavlynMcpWorkspaceCache.CachedWorkspace cachedWorkspace,
        NavlynToolMetadata metadata,
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
            CreateFileInfo(file),
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

        return Succeeded(toolName, sourceCommand, output, metadata);
    }

    private async Task<NavlynToolResult> RunSymbolSourceAsync(
        string toolName,
        NavlynSourceCommand sourceCommand,
        NavlynMcpWorkspaceCache.CachedWorkspace cachedWorkspace,
        NavlynToolMetadata metadata,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        string? candidateId = GetValue(arguments, "--candidate-id");
        string? file = GetValue(arguments, "--file");
        int? line = GetIntValue(arguments, "--line");
        int? column = GetIntValue(arguments, "--column");
        string? projectFilter = GetValue(arguments, "--project");
        bool excludeGenerated = HasFlag(arguments, "--exclude-generated");
        string view = GetValue(arguments, "--view") ?? "declaration";
        int maxLines = GetIntValue(arguments, "--max-lines") ?? DefaultSymbolSourceMaxLines;
        int budgetTokens = GetIntValue(arguments, "--budget-tokens") ?? DefaultSymbolSourceBudgetTokens;

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
                return Failed(toolName, sourceCommand, targetResult.Error);
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
            CreateFileInfo(file!),
            line!.Value,
            column!.Value,
            project,
            excludeGenerated,
            new SymbolSourceOptions(view, maxLines, budgetTokens),
            cancellationToken);

        if (result.Error is not null)
        {
            return Failed(toolName, sourceCommand, result.Error);
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
            Warnings: resolution.Warnings);

        return Succeeded(toolName, sourceCommand, output, metadata);
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
                CreateFileInfo(cachedTarget.Path),
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

    private NavlynSourceCommand CreateSourceCommand(CommandBuildResult command)
    {
        return new NavlynSourceCommand(
            command.Command!,
            [command.Command!, "--workspace", options.WorkspaceArgument, .. command.Arguments]);
    }

    private FileInfo CreateFileInfo(string path)
    {
        return Path.IsPathRooted(path)
            ? new FileInfo(path)
            : new FileInfo(Path.Combine(options.WorkingDirectory, path));
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

    private static int? GetIntValue(IReadOnlyList<string> arguments, string option)
    {
        string? value = GetValue(arguments, option);
        return value is null
            ? null
            : int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
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
        IReadOnlyList<string> Warnings);
}

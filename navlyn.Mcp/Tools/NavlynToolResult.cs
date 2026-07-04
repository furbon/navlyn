using System.Text.Json;
using System.Text.Json.Serialization;

namespace Navlyn.Mcp.Tools;

internal sealed record NavlynToolResult(
    bool Ok,
    string Tool,
    NavlynSourceCommand? SourceCommand,
    string Workspace,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    NavlynToolMetadata? Metadata,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    NavlynFollowUpAction? RecommendedNextAction,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<NavlynFollowUpAction>? OptionalFollowUps,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    JsonElement? Result,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    NavlynToolError? Error)
{
    public static NavlynToolResult Succeeded(
        string tool,
        NavlynSourceCommand sourceCommand,
        string workspace,
        JsonElement result,
        NavlynToolMetadata? metadata = null)
    {
        NavlynFollowUpAction[] followUps = CreateFollowUps(result, metadata);
        return new NavlynToolResult(
            Ok: true,
            tool,
            sourceCommand,
            workspace,
            metadata,
            followUps.FirstOrDefault(),
            followUps.Length > 1 ? followUps.Skip(1).ToArray() : null,
            result.Clone(),
            Error: null);
    }

    public static NavlynToolResult Failed(
        string tool,
        NavlynSourceCommand? sourceCommand,
        string workspace,
        NavlynToolError error)
    {
        return new NavlynToolResult(
            Ok: false,
            tool,
            sourceCommand,
            workspace,
            Metadata: null,
            RecommendedNextAction: null,
            OptionalFollowUps: null,
            Result: null,
            error);
    }

    private static NavlynFollowUpAction[] CreateFollowUps(
        JsonElement result,
        NavlynToolMetadata? metadata)
    {
        if (!result.TryGetProperty("nextActions", out JsonElement nextActions) ||
            nextActions.ValueKind is not JsonValueKind.Array)
        {
            return [];
        }

        return nextActions
            .EnumerateArray()
            .Where(action => action.ValueKind is JsonValueKind.Object)
            .Select(action => new NavlynFollowUpAction(
                action.Clone(),
                GetWhen(action),
                GetCostClass(action, metadata),
                RunByDefault: false))
            .ToArray();
    }

    private static string GetWhen(JsonElement action)
    {
        if (action.TryGetProperty("when", out JsonElement when) &&
            when.ValueKind is JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(when.GetString()))
        {
            return when.GetString()!;
        }

        return "Run only if the current result does not answer the user's question.";
    }

    private static string GetCostClass(JsonElement action, NavlynToolMetadata? metadata)
    {
        if (!action.TryGetProperty("command", out JsonElement commandProperty) ||
            commandProperty.ValueKind is not JsonValueKind.String)
        {
            return metadata?.CostClass ?? "unknown";
        }

        return commandProperty.GetString() switch
        {
            "outline" or "symbol-source" or "inspect-file" => "cheap-file-first",
            "find" or "resolve-target" => "semantic-lookup",
            "repo-graph" or "related-files" => "workspace-summary",
            "impact" or "review-diff" or "review-pack" or "context-pack" => "analysis",
            _ => metadata?.CostClass ?? "analysis"
        };
    }
}

internal sealed record NavlynToolMetadata(
    string ExecutionPath,
    string WorkspaceCacheStatus,
    bool WorkspaceCacheHit,
    string WorkspaceFingerprint,
    string IndexStatus = "warm",
    string? SnapshotId = null,
    string CostClass = "analysis");

internal sealed record NavlynFollowUpAction(
    JsonElement Action,
    string When,
    string CostClass,
    bool RunByDefault);

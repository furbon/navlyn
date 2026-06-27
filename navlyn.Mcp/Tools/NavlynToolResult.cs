using System.Text.Json;
using System.Text.Json.Serialization;

namespace Navlyn.Mcp.Tools;

internal sealed record NavlynToolResult(
    bool Ok,
    string Tool,
    NavlynSourceCommand? SourceCommand,
    string Workspace,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    JsonElement? Result,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    NavlynToolError? Error)
{
    public static NavlynToolResult Succeeded(
        string tool,
        NavlynSourceCommand sourceCommand,
        string workspace,
        JsonElement result)
    {
        return new NavlynToolResult(Ok: true, tool, sourceCommand, workspace, result.Clone(), Error: null);
    }

    public static NavlynToolResult Failed(
        string tool,
        NavlynSourceCommand? sourceCommand,
        string workspace,
        NavlynToolError error)
    {
        return new NavlynToolResult(Ok: false, tool, sourceCommand, workspace, Result: null, error);
    }
}

using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace Navlyn.Mcp.Tools;

internal static class NavlynToolResultFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static CallToolResult ToCallToolResult(NavlynToolResult result)
    {
        string json = JsonSerializer.Serialize(result, JsonOptions);
        using JsonDocument document = JsonDocument.Parse(json);
        return new CallToolResult
        {
            IsError = !result.Ok,
            StructuredContent = document.RootElement.Clone(),
            Content =
            [
                new TextContentBlock
                {
                    Text = json
                }
            ]
        };
    }
}

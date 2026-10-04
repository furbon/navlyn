using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace Navlyn.Mcp.Tools;

internal static class NavlynToolSchemaFormatter
{
    public static JsonElement Compact(JsonElement schema, bool includeFramework = false, bool output = false)
    {
        JsonNode root = JsonNode.Parse(schema.GetRawText())!;
        Normalize(root);
        if (includeFramework && root["properties"] is JsonObject properties)
        {
            properties["targetFramework"] = new JsonObject
            {
                ["type"] = new JsonArray("string", "null"),
                ["description"] = "Select an exact project target framework, such as net10.0 or net10.0-windows. Omit to retain all loaded frameworks."
            };
        }
        if (output && root["required"] is JsonArray required)
        {
            // The SDK infers required constructor parameters without honoring null omission.
            HashSet<string> omitted = typeof(NavlynToolResult).GetProperties()
                .Where(property => property.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition == JsonIgnoreCondition.WhenWritingNull)
                .Select(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name)).ToHashSet(StringComparer.Ordinal);
            for (int i = required.Count - 1; i >= 0; i--)
                if (omitted.Contains(required[i]!.GetValue<string>())) required.RemoveAt(i);
        }
        return JsonSerializer.SerializeToElement(root);
    }

    private static void Normalize(JsonNode? node)
    {
        if (node is JsonArray array)
        {
            foreach (JsonNode? child in array) Normalize(child);
        }
        else if (node is JsonObject value)
        {
            foreach (JsonNode? child in value.Select(property => property.Value).ToArray()) Normalize(child);
            if (value.ContainsKey("default") && value["default"] is null) value.Remove("default");
            if (value["additionalProperties"] is JsonValue additional && additional.TryGetValue(out bool allowed) && allowed)
                value.Remove("additionalProperties");
            // JSON Schema constraints for arrays/objects/numbers do not apply to null.
            // Retain refs and enums, whose constraints can exclude null.
            if (value["anyOf"] is JsonArray { Count: 2 } alternatives)
            {
                JsonObject? nullable = alternatives.OfType<JsonObject>().FirstOrDefault(item => item.Count == 1 && item["type"]?.ToString() == "null");
                JsonObject? typed = alternatives.OfType<JsonObject>().FirstOrDefault(item => item["type"] is JsonValue && item["type"]!.ToString() != "null" &&
                    !item.ContainsKey("$ref") && !item.ContainsKey("enum") && !item.ContainsKey("const") &&
                    item.All(property => !value.ContainsKey(property.Key)));
                if (nullable is not null && typed is not null)
                {
                    value.Remove("anyOf");
                    foreach ((string key, JsonNode? child) in typed) value[key] = child?.DeepClone();
                    value["type"] = new JsonArray(typed["type"]!.DeepClone(), JsonValue.Create("null"));
                }
            }
        }
    }
}

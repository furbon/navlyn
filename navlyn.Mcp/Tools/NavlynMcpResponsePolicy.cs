using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;

namespace Navlyn.Mcp.Tools;

internal static class NavlynMcpResponsePolicy
{
    public static bool SupportsTypeKind(string tool) => tool is NavlynMcpTools.TargetTool or NavlynMcpTools.ImpactTool or
        NavlynMcpTools.PrepareEditTool or NavlynMcpTools.VerifyEditTool or NavlynMcpTools.EntrypointsTool or
        NavlynMcpTools.TestsForSymbolTool or NavlynMcpTools.ContextPackTool;

    public static bool TryReadControls(string tool, IDictionary<string, JsonElement>? arguments, string defaultProfile,
        out string profile, out int? entryLimit, out int entryOffset, out string? error)
    {
        profile = defaultProfile;
        entryLimit = null;
        entryOffset = 0;
        error = null;
        if (arguments?.TryGetValue("resultProfile", out JsonElement mode) == true && mode.ValueKind != JsonValueKind.Null)
        {
            if (mode.ValueKind != JsonValueKind.String || mode.GetString() is not ("compact" or "full"))
            {
                error = "resultProfile must be compact, full, or null.";
                return false;
            }
            profile = mode.GetString()!;
        }
        foreach (string key in new[] { "entryLimit", "entryOffset" })
        {
            if (arguments?.TryGetValue(key, out JsonElement value) != true || value.ValueKind == JsonValueKind.Null) continue;
            if (tool != NavlynMcpTools.FileOutlineTool || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int number) ||
                number < (key == "entryLimit" ? 1 : 0) || (key == "entryLimit" && number > 1000))
            {
                error = "entryLimit (1..1000) and entryOffset (0 or greater) are integers for file outline only.";
                return false;
            }
            if (key == "entryLimit") entryLimit = number;
            else entryOffset = number;
        }
        if (tool == NavlynMcpTools.FileOutlineTool && profile == "compact") entryLimit ??= 100;
        return true;
    }

    public static CallToolResult Project(CallToolResult response, string profile, int? entryLimit, int entryOffset)
    {
        using IDisposable? timing = Navlyn.Mcp.Execution.NavlynMcpTimingScope.Measure("response.projection");
        if (response.StructuredContent is not JsonElement data || (profile == "full" && entryLimit is null && entryOffset == 0))
            return response;
        JsonObject root = JsonNode.Parse(data.GetRawText())!.AsObject();
        if (profile == "compact")
        {
            root["resultProfile"] = "compact";
            root["sourceCommand"] = null;
            root.Remove("recommendedNextAction");
            root.Remove("optionalFollowUps");
            Compact(root["result"]);
        }
        if (root["result"] is JsonObject result && !result.ContainsKey("entriesTotal") &&
            result["entries"] is JsonArray entries && (entryLimit is not null || entryOffset != 0))
        {
            int total = entries.Count;
            int count = entryLimit ?? total;
            result["entries"] = new JsonArray(entries.Skip(entryOffset).Take(count).Select(item => item?.DeepClone()).ToArray());
            result["entriesTotal"] = total;
            result["entryOffset"] = entryOffset;
            result["entriesTruncated"] = entryOffset > 0 || count < total;
            if ((long)entryOffset + count < total) result["nextEntryOffset"] = entryOffset + count;
        }
        JsonElement projected = JsonSerializer.SerializeToElement(root);
        response.StructuredContent = projected;
        response.Content = [new TextContentBlock { Text = projected.GetRawText() }];
        return response;
    }

    private static void Compact(JsonNode? node)
    {
        if (node is JsonArray array)
        {
            foreach (JsonNode? item in array) Compact(item);
        }
        else if (node is JsonObject value)
        {
            value.Remove("nextActions");
            value.Remove("recommendedNextActions");
            if (value["facts"] is JsonObject facts)
            {
                // Signatures retain parameter/return identity. Full facts remain available per call.
                if (facts.ContainsKey("signature"))
                {
                    facts.Remove("parameters");
                    facts.Remove("returnType");
                }
                if (facts.ContainsKey("fullyQualifiedName")) facts.Remove("displayName");
                foreach (string key in facts.Select(pair => pair.Key).ToArray())
                    if (key.StartsWith("is", StringComparison.Ordinal) && key is not ("isSource" or "isMetadata") &&
                        facts[key] is JsonValue flag && flag.TryGetValue(out bool enabled) && !enabled) facts.Remove(key);
            }
            foreach (JsonNode? child in value.Select(pair => pair.Value).ToArray()) Compact(child);
        }
    }

    public static JsonElement InputSchema(string tool, JsonElement schema, string defaultProfile = "compact")
    {
        JsonObject root = JsonNode.Parse(schema.GetRawText())!.AsObject();
        JsonObject properties = root["properties"]!.AsObject();
        properties["resultProfile"] = JsonNode.Parse("""{"type":["string","null"],"enum":["compact","full",null],"description":"Compact includes the selected signature, complete bounded source/body, context and warnings. Use full only for a specific omitted structured field; source bounds are the same in both profiles."}""");
        properties["resultProfile"]!["default"] = defaultProfile;
        if (tool == NavlynMcpTools.FileOutlineTool)
        {
            properties["entryLimit"] = JsonNode.Parse("""{"type":["integer","null"],"minimum":1,"maximum":1000,"description":"Page size; compact defaults to 100. Full is unpaged unless supplied."}""");
            properties["entryOffset"] = JsonNode.Parse("""{"type":["integer","null"],"minimum":0,"description":"Page offset from nextEntryOffset. Only within unchanged source/context."}""");
        }
        if (SupportsTypeKind(tool))
            properties["typeKind"] = JsonNode.Parse("""{"type":["string","null"],"enum":["class","interface","struct","enum","delegate","record","record-class","record-struct",null],"description":"Independent type-kind filter in query mode. Existing assumeKind values remain ranking hints."}""");
        if (tool == NavlynMcpTools.TargetTool)
        {
            properties["mode"]!["enum"] = new JsonArray("select", "list", (JsonNode?)null);
            JsonObject excluded = new();
            foreach (string field in new[] { "typeKind", "assumeKind", "assumeKinds", "match", "caseSensitive", "limit", "candidatePolicy", "minConfidence", "explainSelection" })
                excluded[field] = new JsonObject { ["type"] = "null" };
            root["allOf"] = new JsonArray(new JsonObject
            {
                ["if"] = JsonNode.Parse("""{"required":["file"],"properties":{"file":{"type":"string"}}}"""),
                ["then"] = new JsonObject { ["required"] = new JsonArray("line", "column"), ["properties"] = excluded }
            });
        }
        if (tool == NavlynMcpTools.NavigateTool)
        {
            properties["operation"]!["enum"] = new JsonArray("definition", "references", "callers", "calls", "implementations", "type_hierarchy", "symbol_info");
            root["allOf"] = JsonNode.Parse("""[{"if":{"required":["operation"],"properties":{"operation":{"enum":["definition","calls","implementations","type_hierarchy","symbol_info"]}}},"then":{"properties":{"scope":{"type":"null"},"maxDocuments":{"type":"null"}}}}]""");
        }
        return JsonSerializer.SerializeToElement(root);
    }

    public static JsonElement OutputSchema(JsonElement schema, bool focusedCompact = false)
    {
        if (focusedCompact)
        {
            // The canonical envelope schema remains available in full discovery/docs.
            // Extra fields permit per-call full results without repeating their tree per tool.
            using JsonDocument compact = JsonDocument.Parse("""
                {"type":"object","additionalProperties":true,"required":["ok","tool","sourceCommand","workspace"],"properties":{
                  "ok":{"type":"boolean"},"tool":{"type":"string"},"workspace":{"type":"string"},
                  "sourceCommand":{"type":["object","null"],"additionalProperties":true},
                  "result":{"type":"object","additionalProperties":true},
                  "error":{"type":"object","required":["code","message"],"additionalProperties":true,"properties":{"code":{"type":"string"},"message":{"type":"string"}}},
                  "metadata":{"type":"object","additionalProperties":true,"description":"Execution context and freshness; inspect result scope, bounds and warnings."},
                  "resultProfile":{"type":"string","enum":["compact"]}}}
                """);
            return compact.RootElement.Clone();
        }
        JsonObject root = JsonNode.Parse(schema.GetRawText())!.AsObject();
        root["properties"]!["resultProfile"] = JsonNode.Parse("""{"type":"string","enum":["compact"]}""");
        return JsonSerializer.SerializeToElement(root);
    }
}

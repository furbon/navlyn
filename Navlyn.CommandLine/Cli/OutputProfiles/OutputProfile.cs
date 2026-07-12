using System.CommandLine;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Navlyn.Workspaces;

namespace Navlyn.Cli.OutputProfiles;

internal static class OutputProfile
{
    public const string Compact = "compact";
    public const string Evidence = "evidence";
    public const string Full = "full";
    public const string Default = Full;
    public const string SchemaVersion = "navlyn.workflow.v1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly string[] Values = [Compact, Evidence, Full];
    private static readonly string[] EvidenceSections =
    [
        "comparison",
        "mode",
        "goal",
        "scope",
        "diff",
        "selectionInput",
        "selection",
        "subject",
        "packs",
        "projects",
        "testProjects",
        "changedSymbols",
        "unresolvedChanges",
        "publicContractChanges",
        "changes",
        "findings",
        "relatedTests",
        "tests",
        "entrypoints",
        "diagnosticsScope",
        "diagnostics",
        "registrations",
        "constructorDependencies",
        "consumers",
        "risks",
        "routes",
        "options",
        "bindings",
        "validations",
        "handlers",
        "callSites",
        "dbContexts",
        "entities",
        "dbSets",
        "configurations",
        "querySites",
        "packageReferences",
        "usages",
        "packResults"
    ];

    private static readonly string[] CompactSections =
    [
        "findings",
        "publicContractChanges",
        "changes",
        "relatedTests",
        "tests",
        "entrypoints",
        "diagnostics",
        "registrations",
        "consumers",
        "risks",
        "routes",
        "options",
        "bindings",
        "handlers",
        "callSites",
        "dbContexts",
        "entities",
        "dbSets",
        "querySites",
        "packageReferences",
        "usages"
    ];

    public static Option<string> CreateOption()
    {
        Option<string> option = new("--profile")
        {
            Description = "Output profile: compact, evidence, or full.",
            DefaultValueFactory = _ => Default
        };

        option.AcceptOnlyFromAmong(Values);
        return option;
    }

    public static bool IsValid(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
            Values.Contains(value.Trim(), StringComparer.Ordinal);
    }

    public static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? Default : value.Trim();
    }

    public static JsonObject Format(
        LoadedWorkspace workspace,
        string command,
        string profile,
        object result,
        object? configuration = null)
    {
        string effectiveProfile = Normalize(profile);
        JsonObject root = ToJsonObject(result);
        return effectiveProfile switch
        {
            Compact => CreateCompact(workspace, command, root, configuration),
            Evidence => CreateEvidence(workspace, command, root, configuration),
            _ => CreateFull(workspace, command, root, configuration)
        };
    }

    private static JsonObject CreateFull(
        LoadedWorkspace workspace,
        string command,
        JsonObject root,
        object? configuration)
    {
        JsonObject output = CreateMetadata(workspace, command, Full, configuration);
        CopyProperties(root, output);
        return output;
    }

    private static JsonObject CreateEvidence(
        LoadedWorkspace workspace,
        string command,
        JsonObject root,
        object? configuration)
    {
        JsonObject output = CreateMetadata(workspace, command, Evidence, configuration);
        CopyIdentityFields(root, output, itemLimit: 50, evidenceLimit: 5, removeSnippetText: true);
        output["summary"] = CreateSummary(root);

        foreach (string section in EvidenceSections)
        {
            if (root.TryGetPropertyValue(section, out JsonNode? value) && value is not null)
            {
                output[section] = TrimForProfile(value, itemLimit: 50, evidenceLimit: 5, removeSnippetText: true);
            }
        }

        CopyCommonTail(root, output);
        return output;
    }

    private static JsonObject CreateCompact(
        LoadedWorkspace workspace,
        string command,
        JsonObject root,
        object? configuration)
    {
        JsonObject output = CreateMetadata(workspace, command, Compact, configuration);
        CopyIdentityFields(root, output, itemLimit: 10, evidenceLimit: 3, removeSnippetText: true);
        output["summary"] = CreateSummary(root);

        JsonObject highlights = [];
        foreach (string section in CompactSections)
        {
            if (root.TryGetPropertyValue(section, out JsonNode? value) && value is not null)
            {
                highlights[section] = TrimForProfile(value, itemLimit: 10, evidenceLimit: 3, removeSnippetText: true);
            }
        }

        if (highlights.Count > 0)
        {
            output["highlights"] = highlights;
        }

        output["profileLimits"] = new JsonObject
        {
            ["itemLimit"] = 10,
            ["evidenceLimit"] = 3,
            ["omitsSourceSnippets"] = true
        };
        CopyCommonTail(root, output);
        return output;
    }

    private static JsonObject CreateMetadata(
        LoadedWorkspace workspace,
        string command,
        string profile,
        object? configuration)
    {
        return new JsonObject
        {
            ["schemaVersion"] = SchemaVersion,
            ["navlynVersion"] = GetVersion(),
            ["workspace"] = workspace.DisplayPath,
            ["kind"] = workspace.Kind,
            ["command"] = command,
            ["profile"] = profile,
            ["configuration"] = configuration is null ? new JsonObject() : ToNode(configuration),
            ["reproCommand"] = new JsonObject
            {
                ["executable"] = "navlyn",
                ["arguments"] = new JsonArray(command, "--workspace", workspace.DisplayPath, "--profile", profile)
            }
        };
    }

    private static void CopyIdentityFields(
        JsonObject source,
        JsonObject target,
        int? itemLimit = null,
        int? evidenceLimit = null,
        bool removeSnippetText = false)
    {
        foreach (string property in new[] { "mode", "goal", "changeKind", "scope", "packs", "comparison", "selectionInput", "query", "diff" })
        {
            if (source.TryGetPropertyValue(property, out JsonNode? value) && value is not null && !target.ContainsKey(property))
            {
                target[property] = itemLimit is null || evidenceLimit is null
                    ? Copy(value)
                    : TrimForProfile(value, itemLimit.Value, evidenceLimit.Value, removeSnippetText);
            }
        }
    }

    private static void CopyCommonTail(JsonObject source, JsonObject target)
    {
        foreach (string property in new[] { "limits", "budget", "truncated", "warnings", "nextActions" })
        {
            if (source.TryGetPropertyValue(property, out JsonNode? value) && value is not null)
            {
                target[property] = Copy(value);
            }
        }
    }

    private static JsonObject CreateSummary(JsonObject root)
    {
        JsonObject summary = [];
        foreach (KeyValuePair<string, JsonNode?> property in root.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (property.Value is JsonObject section)
            {
                JsonObject counts = ExtractCounts(section);
                if (counts.Count > 0)
                {
                    summary[property.Key] = counts;
                }
            }
            else if (property.Value is JsonValue value &&
                (property.Key.StartsWith("total", StringComparison.Ordinal) ||
                    property.Key.EndsWith("Count", StringComparison.Ordinal) ||
                    property.Key is "truncated"))
            {
                summary[property.Key] = Copy(value);
            }
        }

        return summary;
    }

    private static JsonObject ExtractCounts(JsonObject section)
    {
        JsonObject counts = [];
        foreach (KeyValuePair<string, JsonNode?> property in section.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (property.Value is not JsonValue value)
            {
                continue;
            }

            if (property.Key.StartsWith("total", StringComparison.Ordinal) ||
                property.Key.EndsWith("Count", StringComparison.Ordinal) ||
                property.Key is "limit" or "truncated")
            {
                counts[property.Key] = Copy(value);
            }
        }

        return counts;
    }

    private static JsonNode TrimForProfile(JsonNode node, int itemLimit, int evidenceLimit, bool removeSnippetText)
    {
        JsonNode copy = Copy(node);
        TrimInPlace(copy, itemLimit, evidenceLimit, removeSnippetText);
        return copy;
    }

    private static void TrimInPlace(JsonNode node, int itemLimit, int evidenceLimit, bool removeSnippetText)
    {
        if (node is JsonObject obj)
        {
            List<string> remove = [];
            foreach (KeyValuePair<string, JsonNode?> property in obj.ToArray())
            {
                if (property.Value is null)
                {
                    continue;
                }

                if (removeSnippetText &&
                    (property.Key is "snippet" or "content" ||
                        property.Key is "lines" && property.Value is JsonArray))
                {
                    remove.Add(property.Key);
                    continue;
                }

                if (property.Value is JsonArray array)
                {
                    int limit = property.Key is "evidence" or "sourceLocations" ? evidenceLimit : itemLimit;
                    TrimArray(array, limit, itemLimit, evidenceLimit, removeSnippetText);
                }
                else
                {
                    TrimInPlace(property.Value, itemLimit, evidenceLimit, removeSnippetText);
                }
            }

            foreach (string key in remove)
            {
                obj.Remove(key);
            }
        }
        else if (node is JsonArray array)
        {
            TrimArray(array, itemLimit, itemLimit, evidenceLimit, removeSnippetText);
        }
    }

    private static void TrimArray(JsonArray array, int limit, int itemLimit, int evidenceLimit, bool removeSnippetText)
    {
        for (int index = array.Count - 1; index >= limit; index--)
        {
            array.RemoveAt(index);
        }

        foreach (JsonNode? item in array)
        {
            if (item is not null)
            {
                TrimInPlace(item, itemLimit, evidenceLimit, removeSnippetText);
            }
        }
    }

    private static JsonObject ToJsonObject(object value)
    {
        return ToNode(value).AsObject();
    }

    private static JsonNode ToNode(object value)
    {
        string json = JsonSerializer.Serialize(value, JsonOptions);
        return JsonNode.Parse(json) ?? new JsonObject();
    }

    private static JsonNode Copy(JsonNode node)
    {
        return JsonNode.Parse(node.ToJsonString(JsonOptions)) ?? new JsonObject();
    }

    private static void CopyProperties(JsonObject source, JsonObject target)
    {
        foreach (KeyValuePair<string, JsonNode?> property in source)
        {
            target[property.Key] = property.Value is null ? null : Copy(property.Value);
        }
    }

    private static string GetVersion()
    {
        Assembly assembly = typeof(OutputProfile).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
            assembly.GetName().Version?.ToString() ??
            "unknown";
    }
}

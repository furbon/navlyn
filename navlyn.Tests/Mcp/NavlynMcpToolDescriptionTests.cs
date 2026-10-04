using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Navlyn.Mcp.Tools;

namespace Navlyn.Tests.Mcp;

public sealed class NavlynMcpToolDescriptionTests
{
    [Fact]
    public void FocusedDiscoveryShortensProseWithoutChangingInputsOrConstraints()
    {
        using JsonDocument original = JsonDocument.Parse("""
            {"type":"object","required":["operation"],"properties":{
              "operation":{"type":"string","description":"Relationship operation"},
              "file":{"type":["string","null"],"description":"Source path"},
              "scope":{"type":["string","null"],"enum":["workspace","project","file",null]},
              "maxDocuments":{"type":["integer","null"],"minimum":1}}}
            """);
        JsonNode full = JsonNode.Parse(NavlynMcpResponsePolicy.InputSchema(NavlynMcpTools.NavigateTool, original.RootElement).GetRawText())!;
        JsonNode focused = JsonNode.Parse(NavlynMcpResponsePolicy.InputSchema(NavlynMcpTools.NavigateTool, original.RootElement, focusedCompact: true).GetRawText())!;
        RemoveDescriptions(full);
        RemoveDescriptions(focused);
        Assert.True(JsonNode.DeepEquals(full, focused));

        static void RemoveDescriptions(JsonNode? node)
        {
            if (node is JsonObject value)
            {
                value.Remove("description");
                foreach (JsonNode? child in value.Select(property => property.Value)) RemoveDescriptions(child);
            }
            else if (node is JsonArray array) foreach (JsonNode? child in array) RemoveDescriptions(child);
        }
    }
    private static readonly string[] ExpectedToolNames =
    [
        "navlyn_target",
        "navlyn_read",
        "navlyn_file_outline",
        "navlyn_navigate",
        "navlyn_prepare_edit",
        "navlyn_verify_edit",
        "navlyn_review",
        "navlyn_workspace_summary",
        "navlyn_workspace_status",
        "navlyn_workspace_refresh",
        "navlyn_doctor",
        "navlyn_impact",
        "navlyn_context_pack",
        "navlyn_entrypoints",
        "navlyn_tests_for_symbol",
        "navlyn_tests_for_diff",
        "navlyn_diagnostics",
        "navlyn_di",
        "navlyn_public_api_diff",
        "navlyn_routes",
        "navlyn_options",
        "navlyn_messages",
        "navlyn_ef",
        "navlyn_packages",
        "navlyn_batch"
    ];

    private static readonly string[] RemovedToolNames =
    [
        "navlyn_resolve_target",
        "navlyn_find_symbol",
        "navlyn_inspect_file",
        "navlyn_symbol_source",
        "navlyn_symbol_edges",
        "navlyn_about_symbol",
        "navlyn_related_files",
        "navlyn_exact_navigation",
        "navlyn_review_diff",
        "navlyn_edit_preflight",
        "navlyn_post_edit_guard",
        "navlyn_wrong_symbol_guard",
        "navlyn_change_intent_pack",
        "navlyn_agent_handoff_pack",
        "navlyn_confidence_ledger",
        "navlyn_di_impact"
    ];

    [Fact]
    public void EveryV08ToolAndModelVisibleParameterHasADescription()
    {
        MethodInfo[] methods = GetRegisteredMethods();
        Dictionary<string, MethodInfo> methodsByTool = methods
            .Select(method => (Method: method, ToolName: GetRegisteredToolName(method)))
            .Where(entry => entry.ToolName is not null)
            .ToDictionary(entry => entry.ToolName!, entry => entry.Method, StringComparer.Ordinal);

        List<string> missing = [];
        foreach (string toolName in ExpectedToolNames)
        {
            if (!methodsByTool.TryGetValue(toolName, out MethodInfo? method))
            {
                missing.Add($"{toolName}::<MCP tool registration>");
                continue;
            }

            DescriptionAttribute? methodDescription = method.GetCustomAttribute<DescriptionAttribute>();
            if (methodDescription is null || string.IsNullOrWhiteSpace(methodDescription.Description))
            {
                missing.Add($"{toolName}::<tool description>");
            }

            foreach (ParameterInfo parameter in method.GetParameters())
            {
                if (parameter.ParameterType == typeof(IServiceProvider) ||
                    parameter.ParameterType == typeof(CancellationToken))
                {
                    continue;
                }

                DescriptionAttribute? description = parameter.GetCustomAttribute<DescriptionAttribute>();
                if (description is null || string.IsNullOrWhiteSpace(description.Description))
                {
                    missing.Add($"{toolName}::{parameter.Name}");
                }
            }
        }

        Assert.True(missing.Count == 0, "Missing v0.8 MCP descriptions or registrations: " + string.Join(", ", missing));
    }

    [Fact]
    public void WorkspaceSummaryProfileDescriptionStatesCompactOmissionDefault()
    {
        Assert.Contains("omitted defaults to compact", GetParameterDescription("navlyn_workspace_summary", "profile"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReviewProfileDescriptionStatesEvidenceOmissionDefault()
    {
        Assert.Contains("omitted defaults to evidence", GetParameterDescription("navlyn_review", "profile"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToolDescriptionsStayWithinNinetyWordsAndPreserveExactBatchGuidance()
    {
        foreach (string toolName in ExpectedToolNames)
        {
            string description = GetToolDescription(toolName);
            int wordCount = description.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
            Assert.True(wordCount <= 90, $"{toolName} has {wordCount} words: {description}");
        }

        Assert.Equal(
            "Advanced optimization for two or more already-selected, batch-supported facts from the same workspace. Prefer focused MCP tools for a single fact. Do not use batch for initial discovery or as a checklist.",
            GetToolDescription("navlyn_batch"));
    }

    [Fact]
    public void ToolDescriptionsStateRoutingInputsAndEvidenceBoundaries()
    {
        AssertDescriptionContains("navlyn_target", "workspace source declaration", "select normally", "list only", "DLL internals", "ambiguity");
        AssertDescriptionContains("navlyn_read", "bounded C#", "candidateId", "exact file/line/column", "broad reading");
        AssertDescriptionContains("navlyn_file_outline", "one known", "semantic", "ordinary reading");
        AssertDescriptionContains("navlyn_navigate", "one definition", "candidateId", "references", "callers", "partial", "DLL internals");
        AssertDescriptionContains("navlyn_prepare_edit", "immediately before editing", "source", "context", "test evidence");
        AssertDescriptionContains("navlyn_verify_edit", "post-edit", "diff-to-intent", "mismatch", "not proof");
        AssertDescriptionContains("navlyn_review", "actual Git diff", "not", "static");
        AssertDescriptionContains("navlyn_workspace_summary", "not a default", "named project file", "static");
        AssertDescriptionContains("navlyn_workspace_status", "not a default", "static");
        AssertDescriptionContains("navlyn_workspace_refresh", "default preamble", "static");
        AssertDescriptionContains("navlyn_doctor", "routine preamble", "read-only");
        AssertDescriptionContains("navlyn_context_pack", "escalation", "smaller Navlyn facts", "bounded reading queue");
        AssertDescriptionContains("navlyn_tests_for_symbol", "test candidates", "never", "test runner", "static");
        AssertDescriptionContains("navlyn_tests_for_diff", "test candidates", "never", "test runner", "static");
        AssertDescriptionContains("navlyn_diagnostics", "already present", "does not run a build", "fix suggestions", "runtime");
        AssertDescriptionContains("navlyn_di", "source", "not runtime", "source patterns");
        AssertDescriptionContains("navlyn_routes", "static source evidence", "runtime route tables", "effective authorization");
        AssertDescriptionContains("navlyn_options", "static source evidence", "runtime configuration", "secrets");
        AssertDescriptionContains("navlyn_messages", "static source facts", "runtime delivery", "dispatch");
        AssertDescriptionContains("navlyn_ef", "static source patterns", "runtime model", "execute queries");
        AssertDescriptionContains("navlyn_packages", "source facts", "runtime loading", "compatibility");
        AssertDescriptionContains("navlyn_public_api_diff", "base", "source-level", "not", "binary compatibility");
    }

    [Fact]
    public void ImpactDescriptionMatchesItsSupportedTargetInputs()
    {
        string description = GetToolDescription("navlyn_impact");
        Assert.Contains("query or candidateId", description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("source position", description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EntrypointsDescriptionDistinguishesSymbolAndFrameworkInputs()
    {
        string description = GetToolDescription("navlyn_entrypoints");
        Assert.Contains("symbol mode", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("query or candidateId", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("framework mode", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("framework discovery inputs", description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("source position", description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PrepareEditCandidatePolicyDescriptionMatchesSupportedValues()
    {
        string description = GetParameterDescription("navlyn_prepare_edit", "candidatePolicy");
        Assert.Contains("fail or select", description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("require-exact", description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PrepareEditExplainSelectionDescriptionExcludesSourcePositionMode()
    {
        string description = GetParameterDescription("navlyn_prepare_edit", "explainSelection");
        Assert.Contains("query or candidate", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("omit for exact source-position mode", description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnifiedMcpSurfaceMatchesTheExactV08Order()
    {
        IReadOnlyList<string> tools = NavlynMcpToolProfilePolicy.GetToolNames(Navlyn.Mcp.Configuration.NavlynMcpToolProfile.Full);

        Assert.Equal(ExpectedToolNames, tools);
    }

    [Fact]
    public void RemovedCompatibilityNamesAreNotMcpRegistered()
    {
        HashSet<string> registeredNames = GetRegisteredMethods()
            .Select(GetRegisteredToolName)
            .Where(name => name is not null)
            .Select(name => name!)
            .ToHashSet(StringComparer.Ordinal);

        string[] registeredRemovedNames = RemovedToolNames.Where(registeredNames.Contains).ToArray();

        Assert.Empty(registeredRemovedNames);
    }

    private static MethodInfo[] GetRegisteredMethods()
    {
        return typeof(NavlynMcpTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.GetCustomAttributesData().Any(attribute => attribute.AttributeType.Name == "McpServerToolAttribute"))
            .ToArray();
    }

    private static string? GetRegisteredToolName(MethodInfo method)
    {
        CustomAttributeData? attribute = method.GetCustomAttributesData()
            .FirstOrDefault(candidate => candidate.AttributeType.Name == "McpServerToolAttribute");
        if (attribute is null)
        {
            return null;
        }

        CustomAttributeNamedArgument name = attribute.NamedArguments
            .FirstOrDefault(argument => argument.MemberName == "Name");
        return name.TypedValue.Value as string;
    }

    private static string GetParameterDescription(string toolName, string parameterName)
    {
        MethodInfo method = GetRegisteredMethods()
            .Single(candidate => GetRegisteredToolName(candidate) == toolName);
        ParameterInfo parameter = method.GetParameters()
            .Single(candidate => candidate.Name == parameterName);
        return parameter.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty;
    }

    private static string GetToolDescription(string toolName)
    {
        MethodInfo method = GetRegisteredMethods()
            .Single(candidate => GetRegisteredToolName(candidate) == toolName);
        return method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty;
    }

    private static void AssertDescriptionContains(string toolName, params string[] fragments)
    {
        string description = GetToolDescription(toolName);
        foreach (string fragment in fragments)
        {
            Assert.Contains(fragment, description, StringComparison.OrdinalIgnoreCase);
        }
    }
}

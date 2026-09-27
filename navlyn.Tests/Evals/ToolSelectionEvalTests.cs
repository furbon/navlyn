using System.Diagnostics;
using System.Text.Json;

using System.Text.Json.Nodes;

namespace Navlyn.Tests.Evals;

using System.Reflection;
using Navlyn.Mcp.Tools;

public sealed class ToolSelectionEvalTests
{
    private static readonly string[] RequiredRoutingTaskClasses =
    [
        "ambiguous-symbol-identity", "exact-position", "overload", "partial-declaration", "references-callers",
        "known-file-outline", "multi-project", "multi-target", "linked-file", "generated-code-avoidance",
        "pre-edit", "post-edit", "actual-diff-review", "diagnostics", "di", "routes", "options", "messages",
        "ef", "packages", "context-escalation", "two-fact-batch", "comments", "strings", "markdown",
        "configuration", "generated-artifact-text", "arbitrary-text-search", "simple-file-read",
        "build-test-execution", "ambiguity", "stale-candidate", "stale-workspace", "missing-workspace",
        "unavailable-mcp", "partial-result", "explicit-no-navlyn-override"
    ];

    private static readonly string[] CurrentMcpTools =
    [
        "navlyn_target", "navlyn_read", "navlyn_file_outline", "navlyn_navigate", "navlyn_prepare_edit",
        "navlyn_verify_edit", "navlyn_review", "navlyn_workspace_summary", "navlyn_workspace_status",
        "navlyn_workspace_refresh", "navlyn_doctor", "navlyn_impact", "navlyn_context_pack", "navlyn_entrypoints",
        "navlyn_tests_for_symbol", "navlyn_tests_for_diff", "navlyn_diagnostics", "navlyn_di", "navlyn_public_api_diff",
        "navlyn_routes", "navlyn_options", "navlyn_messages", "navlyn_ef", "navlyn_packages", "navlyn_batch"
    ];

    private static readonly HashSet<string> OrdinaryActionNames = new(StringComparer.Ordinal)
    {
        "file-read", "rg", "git", "build", "test"
    };

    [Fact]
    public void ToolSelectionScenarioFile_IsMachineReadable()
    {
        string repoRoot = FindRepositoryRoot();
        string scenarioPath = Path.Combine(repoRoot, "docs", "evals", "tool-selection.scenarios.json");

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(scenarioPath));
        JsonElement root = document.RootElement;

        Assert.Equal("navlyn.tool-selection-eval.v2", root.GetProperty("schemaVersion").GetString());
        JsonElement scenarios = root.GetProperty("scenarios");
        Assert.True(scenarios.GetArrayLength() >= 37);
        HashSet<string> taskClasses = new(StringComparer.Ordinal);
        HashSet<string> scenarioIds = new(StringComparer.Ordinal);
        Dictionary<string, HashSet<string>> parametersByTool = GetMcpParameterNames();
        foreach (JsonElement scenario in scenarios.EnumerateArray())
        {
            string scenarioId = scenario.GetProperty("id").GetString()!;
            Assert.False(string.IsNullOrWhiteSpace(scenarioId));
            Assert.True(scenarioIds.Add(scenarioId), $"Duplicate scenario id: {scenarioId}");
            string taskClass = scenario.GetProperty("taskClass").GetString()!;
            Assert.True(taskClasses.Add(taskClass), $"Duplicate task class: {taskClass}");
            string fixture = scenario.GetProperty("fixture").GetString()!;
            string workspace = scenario.GetProperty("workspace").GetString()!;
            Assert.False(string.IsNullOrWhiteSpace(fixture));
            Assert.False(string.IsNullOrWhiteSpace(workspace));
            Assert.True(File.Exists(Path.Combine(repoRoot, fixture.Replace('/', Path.DirectorySeparatorChar))), fixture);
            if (taskClass == "missing-workspace")
            {
                Assert.False(File.Exists(Path.Combine(repoRoot, workspace.Replace('/', Path.DirectorySeparatorChar))));
            }
            else
            {
                Assert.True(File.Exists(Path.Combine(repoRoot, workspace.Replace('/', Path.DirectorySeparatorChar))), workspace);
            }
            Assert.True(scenario.GetProperty("expectedSkillActivation").ValueKind is JsonValueKind.True or JsonValueKind.False);
            ValidateAction(scenario.GetProperty("expectedFirstAction"), parametersByTool);
            Assert.True(scenario.GetProperty("acceptedSequences").GetArrayLength() >= 1);
            ValidateActionArray(scenario.GetProperty("forbiddenTools"), parametersByTool);
            ValidateSequenceArray(scenario.GetProperty("forbiddenSequences"), parametersByTool);
            foreach (JsonElement sequence in scenario.GetProperty("acceptedSequences").EnumerateArray())
            {
                ValidateActionArray(sequence, parametersByTool);
            }
            Assert.Equal(JsonValueKind.Array, scenario.GetProperty("requiredArguments").ValueKind);
            Assert.True(scenario.GetProperty("expectedStopEvidence").GetProperty("acceptedStopReasons").GetArrayLength() >= 1);
            Assert.True(scenario.GetProperty("expectedStopEvidence").GetProperty("alternatives").GetArrayLength() >= 1);
            foreach (JsonElement alternative in scenario.GetProperty("expectedStopEvidence").GetProperty("alternatives").EnumerateArray())
            {
                JsonElement requiredFields = alternative.GetProperty("requiredFields");
                JsonElement predicates = alternative.GetProperty("predicates");
                Assert.Equal(JsonValueKind.Array, requiredFields.ValueKind);
                Assert.True(requiredFields.GetArrayLength() > 0, scenarioId);
                Assert.Equal(JsonValueKind.Array, predicates.ValueKind);
                Assert.Equal(requiredFields.GetArrayLength(), predicates.GetArrayLength());
                Assert.False(
                    requiredFields.GetArrayLength() == 1 && requiredFields[0].GetString() == "result.command",
                    $"Stop evidence cannot rely on result.command alone: {scenarioId}");
                int callIndex = alternative.GetProperty("callIndex").GetInt32();
                JsonElement baselineCall = scenario.GetProperty("baselineTrace").GetProperty("calls")[callIndex];
                string[] requiredPaths = requiredFields.EnumerateArray().Select(field => field.GetString()!).Order(StringComparer.Ordinal).ToArray();
                string[] predicatePaths = predicates.EnumerateArray().Select(predicate => predicate.GetProperty("path").GetString()!).Order(StringComparer.Ordinal).ToArray();
                Assert.Equal(requiredPaths, predicatePaths);
                foreach (JsonElement predicate in predicates.EnumerateArray())
                {
                    ValidateStopPredicate(predicate, scenarioId);
                    AssertStopPredicateMatches(baselineCall.GetProperty("selectedResultFields"), predicate, scenarioId);
                }
            }
            Assert.True(scenario.GetProperty("semanticCorrectnessChecks").GetArrayLength() >= 1);
            Assert.True(scenario.GetProperty("maxCalls").GetInt32() > 0);
            Assert.True(scenario.GetProperty("stdoutBudgetChars").GetInt32() > 0);
            Assert.True(scenario.GetProperty("latencyBudgetMs").GetInt32() > 0);
            Assert.True(scenario.GetProperty("unsupportedClaims").ValueKind == JsonValueKind.Array);
            Assert.False(string.IsNullOrWhiteSpace(scenario.GetProperty("availabilityFreshnessSetup").GetProperty("mcpAvailability").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(scenario.GetProperty("availabilityFreshnessSetup").GetProperty("workspaceFreshness").GetString()));

            JsonElement trace = scenario.GetProperty("baselineTrace");
            Assert.True(trace.GetProperty("skillActivated").ValueKind is JsonValueKind.True or JsonValueKind.False);
            Assert.True(trace.GetProperty("calls").GetArrayLength() > 0);
            Assert.False(string.IsNullOrWhiteSpace(trace.GetProperty("stopReason").GetString()));
            Assert.True(trace.GetProperty("semanticChecks").ValueKind == JsonValueKind.Object);
            foreach (JsonProperty check in trace.GetProperty("semanticChecks").EnumerateObject())
            {
                Assert.True(check.Value.ValueKind is JsonValueKind.True or JsonValueKind.False);
            }
            foreach (JsonElement check in scenario.GetProperty("semanticCorrectnessChecks").EnumerateArray())
            {
                Assert.Equal(JsonValueKind.True, trace.GetProperty("semanticChecks").GetProperty(check.GetString()!).ValueKind);
            }
            Assert.Equal(JsonValueKind.Array, trace.GetProperty("claims").ValueKind);
            Assert.All(trace.GetProperty("claims").EnumerateArray(), claim => Assert.Equal(JsonValueKind.String, claim.ValueKind));
            Assert.True(trace.GetProperty("environment").ValueKind == JsonValueKind.Object);
            Assert.True(trace.GetProperty("outputValid").ValueKind is JsonValueKind.True or JsonValueKind.False);
            Assert.True(trace.GetProperty("stderrClean").ValueKind is JsonValueKind.True or JsonValueKind.False);
            Assert.True(trace.GetProperty("stdoutChars").TryGetInt64(out long stdoutChars) && stdoutChars >= 0);
            Assert.True(trace.GetProperty("latencyMs").TryGetInt64(out long latencyMs) && latencyMs >= 0);
            foreach (JsonElement call in trace.GetProperty("calls").EnumerateArray())
            {
                string kind = call.GetProperty("kind").GetString()!;
                Assert.Contains(kind, new[] { "mcp", "ordinary" });
                string name = call.GetProperty("name").GetString()!;
                if (kind == "mcp")
                {
                    Assert.Contains(name, CurrentMcpTools);
                }
                else
                {
                    Assert.Contains(name, OrdinaryActionNames);
                }

                Assert.True(call.GetProperty("arguments").ValueKind == JsonValueKind.Object);
                Assert.True(call.GetProperty("selectedResultFields").ValueKind == JsonValueKind.Object);
                Assert.True(call.GetProperty("selectedResultFields").EnumerateObject().Any());
                if (kind == "mcp")
                {
                    JsonElement mcpFields = call.GetProperty("selectedResultFields");
                    bool hasResult = mcpFields.TryGetProperty("result", out JsonElement mcpResult);
                    bool hasError = mcpFields.TryGetProperty("error", out JsonElement mcpError);
                    Assert.True(hasResult || hasError);
                    Assert.Equal(JsonValueKind.Object, hasResult ? mcpResult.ValueKind : mcpError.ValueKind);
                    JsonElement arguments = call.GetProperty("arguments");
                    if (name == "navlyn_batch")
                    {
                        ValidateBatchCliPayload(arguments);
                    }
                    else
                    {
                        foreach (JsonProperty argument in arguments.EnumerateObject())
                        {
                            Assert.Contains(argument.Name, parametersByTool[name]);
                        }
                    }

                    if (name == "navlyn_target" && arguments.TryGetProperty("mode", out JsonElement mode) && mode.GetString() == "list")
                    {
                        Assert.Equal("exact", arguments.GetProperty("match").GetString());
                        Assert.False(mcpResult.TryGetProperty("command", out _), $"Target list results do not expose command: {scenarioId}");
                        Assert.Contains(
                            scenario.GetProperty("requiredArguments").EnumerateArray(),
                            argument => argument.GetProperty("callIndex").GetInt32() == 0 &&
                                argument.GetProperty("path").GetString() == "match" &&
                                argument.GetProperty("equals").GetString() == "exact");
                    }

                    if (name is "navlyn_read" or "navlyn_file_outline")
                    {
                        Assert.False(mcpResult.TryGetProperty("command", out _), $"{name} result does not expose command: {scenarioId}");
                    }

                    AssertTaskResultShape(taskClass, mcpResult);
                }
            }

            Assert.Equal(JsonValueKind.Number, trace.GetProperty("stdoutChars").ValueKind);
            Assert.Equal(JsonValueKind.Number, trace.GetProperty("latencyMs").ValueKind);
            Assert.Equal(JsonValueKind.True, trace.GetProperty("outputValid").ValueKind);
        }

        Assert.All(RequiredRoutingTaskClasses, taskClass => Assert.Contains(taskClass, taskClasses));
        Assert.Equal(RequiredRoutingTaskClasses.Length, taskClasses.Count);
    }

    private static void AssertTaskResultShape(string taskClass, JsonElement result)
    {
        switch (taskClass)
        {
            case "exact-position":
                Assert.False(result.TryGetProperty("selectionInput", out _));
                Assert.Equal(JsonValueKind.Array, result.GetProperty("slices").ValueKind);
                JsonElement sourceSlice = result.GetProperty("slices")[0];
                Assert.Equal(JsonValueKind.Number, sourceSlice.GetProperty("startLine").ValueKind);
                Assert.Equal("declaration", sourceSlice.GetProperty("textKind").GetString());
                Assert.Equal(JsonValueKind.Array, sourceSlice.GetProperty("lines").ValueKind);
                Assert.False(sourceSlice.TryGetProperty("line", out _));
                Assert.False(sourceSlice.TryGetProperty("kind", out _));
                Assert.False(sourceSlice.TryGetProperty("text", out _));
                break;
            case "linked-file":
                Assert.False(result.TryGetProperty("selectionInput", out _));
                JsonElement linkedProject = result.GetProperty("project");
                Assert.Equal(JsonValueKind.Object, linkedProject.ValueKind);
                Assert.Equal("LinkedAlpha", linkedProject.GetProperty("name").GetString());
                Assert.Equal("LinkedAlpha", linkedProject.GetProperty("filter").GetString());
                Assert.Equal("tests/fixtures/WorkspaceSemanticsFixture/LinkedAlpha/LinkedAlpha.csproj", linkedProject.GetProperty("path").GetString());
                Assert.Equal("net10.0", linkedProject.GetProperty("targetFramework").GetString());
                Assert.Equal("WorkspaceSemantics.Linked", result.GetProperty("symbol").GetProperty("container").GetString());
                Assert.Equal(JsonValueKind.Array, result.GetProperty("slices").ValueKind);
                JsonElement linkedSlice = result.GetProperty("slices")[0];
                Assert.Equal("declaration", linkedSlice.GetProperty("textKind").GetString());
                Assert.Equal(JsonValueKind.Array, linkedSlice.GetProperty("lines").ValueKind);
                Assert.False(linkedSlice.TryGetProperty("kind", out _));
                Assert.False(linkedSlice.TryGetProperty("text", out _));
                break;
            case "multi-project":
                JsonElement selectedTarget = result.GetProperty("selectedTarget");
                Assert.Equal("CrossProjectRunner", selectedTarget.GetProperty("name").GetString());
                Assert.False(result.TryGetProperty("candidate", out _));
                break;
            case "generated-code-avoidance":
                Assert.Equal("target", result.GetProperty("command").GetString());
                Assert.Equal(0, result.GetProperty("candidateCount").GetInt32());
                Assert.Equal(JsonValueKind.Array, result.GetProperty("candidates").ValueKind);
                Assert.False(result.TryGetProperty("candidate", out _));
                break;
            case "actual-diff-review":
                JsonElement changedSymbols = result.GetProperty("changedSymbols");
                Assert.Equal(JsonValueKind.Number, changedSymbols.GetProperty("totalSymbols").ValueKind);
                Assert.Equal(JsonValueKind.Number, changedSymbols.GetProperty("limit").ValueKind);
                Assert.Equal(JsonValueKind.False, changedSymbols.GetProperty("truncated").ValueKind);
                Assert.Equal(JsonValueKind.Array, changedSymbols.GetProperty("symbols").ValueKind);
                Assert.Equal(JsonValueKind.Array, result.GetProperty("findings").ValueKind);
                break;
            case "di":
                JsonElement registrations = result.GetProperty("registrations");
                JsonElement registrationItems = AssertCollectionWrapper(registrations, "totalRegistrations");
                Assert.False(string.IsNullOrWhiteSpace(registrationItems[0].GetProperty("serviceType").GetProperty("name").GetString()));
                break;
            case "routes":
                JsonElement routes = result.GetProperty("routes");
                JsonElement routeItems = AssertCollectionWrapper(routes, "totalItems");
                Assert.Equal(4, routes.GetProperty("totalItems").GetInt32());
                Assert.Equal(100, routes.GetProperty("limit").GetInt32());
                Assert.Equal("controller-action", routeItems[0].GetProperty("endpointKind").GetString());
                Assert.Equal("orders/{id}", routeItems[0].GetProperty("routePattern").GetString());
                Assert.Equal("/orders/{id}", routeItems[0].GetProperty("normalizedRoutePattern").GetString());
                Assert.Equal("tests/fixtures/ApplicationDomainFixture/FixtureCode.cs", routeItems[0].GetProperty("path").GetString());
                Assert.Equal(168, routeItems[0].GetProperty("line").GetInt32());
                break;
            case "options":
                JsonElement options = result.GetProperty("options");
                JsonElement optionItems = AssertCollectionWrapper(options, "totalItems");
                Assert.False(string.IsNullOrWhiteSpace(optionItems[0].GetProperty("type").GetProperty("name").GetString()));
                JsonElement bindings = result.GetProperty("bindings");
                JsonElement bindingItems = AssertCollectionWrapper(bindings, "totalItems");
                Assert.Equal(3, bindings.GetProperty("totalItems").GetInt32());
                Assert.Equal(100, bindings.GetProperty("limit").GetInt32());
                Assert.Equal("PaymentOptions", bindingItems[0].GetProperty("optionType").GetProperty("name").GetString());
                Assert.Equal("Payments", bindingItems[0].GetProperty("configurationKey").GetString());
                Assert.Equal("configure", bindingItems[0].GetProperty("bindingKind").GetString());
                JsonElement consumers = result.GetProperty("consumers");
                JsonElement consumerItems = AssertCollectionWrapper(consumers, "totalItems");
                Assert.Equal(2, consumers.GetProperty("totalItems").GetInt32());
                Assert.Equal(100, consumers.GetProperty("limit").GetInt32());
                Assert.Equal("OrdersController", consumerItems[0].GetProperty("consumerType").GetProperty("name").GetString());
                Assert.Equal("PaymentOptions", consumerItems[0].GetProperty("optionType").GetProperty("name").GetString());
                Assert.Equal("IOptions", consumerItems[0].GetProperty("consumerKind").GetString());
                break;
            case "messages":
                JsonElement handlers = result.GetProperty("handlers");
                JsonElement handlerItems = AssertCollectionWrapper(handlers, "totalItems");
                Assert.False(string.IsNullOrWhiteSpace(handlerItems[0].GetProperty("messageType").GetProperty("name").GetString()));
                break;
            case "ef":
                JsonElement entities = result.GetProperty("entities");
                JsonElement entityItems = AssertCollectionWrapper(entities, "totalItems");
                Assert.Equal("Order", entityItems[0].GetProperty("type").GetProperty("name").GetString());
                break;
            case "packages":
                JsonElement packages = result.GetProperty("packageReferences");
                JsonElement packageItems = AssertCollectionWrapper(packages, "totalItems");
                Assert.Equal(1, packages.GetProperty("totalItems").GetInt32());
                Assert.Equal(100, packages.GetProperty("limit").GetInt32());
                Assert.False(string.IsNullOrWhiteSpace(packageItems[0].GetProperty("name").GetString()));
                Assert.False(result.TryGetProperty("usages", out _));
                break;
            case "diagnostics":
                Assert.Equal(17, result.GetProperty("totalDiagnostics").GetInt32());
                Assert.Equal("CS0246", result.GetProperty("diagnostics")[0].GetProperty("id").GetString());
                break;
            case "multi-target":
                JsonElement projects = result.GetProperty("projects");
                Assert.Equal(JsonValueKind.Number, projects.GetProperty("totalProjects").ValueKind);
                JsonElement projectItems = projects.GetProperty("items");
                Assert.Equal(JsonValueKind.Array, projectItems.ValueKind);
                Assert.Contains(projectItems.EnumerateArray(), project =>
                    project.GetProperty("name").GetString() == "MultiTarget(net10.0)" &&
                    project.GetProperty("targetFrameworks").EnumerateArray().Any(framework => framework.GetString() == "netstandard2.0"));
                break;
        }
    }

    private static JsonElement AssertCollectionWrapper(JsonElement collection, string totalCountName)
    {
        Assert.Equal(JsonValueKind.Number, collection.GetProperty(totalCountName).ValueKind);
        Assert.Equal(JsonValueKind.Number, collection.GetProperty("limit").ValueKind);
        Assert.True(collection.GetProperty("truncated").ValueKind is JsonValueKind.True or JsonValueKind.False);
        JsonElement items = collection.GetProperty("items");
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        return items;
    }

    private static Dictionary<string, HashSet<string>> GetMcpParameterNames()
    {
        Dictionary<string, HashSet<string>> result = new(StringComparer.Ordinal);
        foreach (MethodInfo method in typeof(NavlynMcpTools).GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            CustomAttributeData? registration = method.GetCustomAttributesData()
                .FirstOrDefault(attribute => attribute.AttributeType.Name == "McpServerToolAttribute");
            if (registration is null)
            {
                continue;
            }

            string toolName = registration.NamedArguments
                .Single(argument => argument.MemberName == "Name")
                .TypedValue.Value!.ToString()!;
            HashSet<string> parameterNames = method.GetParameters()
                .Where(parameter => parameter.Name is not ("services" or "cancellationToken"))
                .Select(parameter => parameter.Name!)
                .ToHashSet(StringComparer.Ordinal);
            result.Add(toolName, parameterNames);
        }

        Assert.Equal(CurrentMcpTools.Order(), result.Keys.Order());
        return result;
    }

    private static void ValidateActionArray(JsonElement actions, IReadOnlyDictionary<string, HashSet<string>> parametersByTool)
    {
        Assert.Equal(JsonValueKind.Array, actions.ValueKind);
        foreach (JsonElement action in actions.EnumerateArray())
        {
            ValidateAction(action, parametersByTool);
        }
    }

    private static void ValidateSequenceArray(JsonElement sequences, IReadOnlyDictionary<string, HashSet<string>> parametersByTool)
    {
        Assert.Equal(JsonValueKind.Array, sequences.ValueKind);
        foreach (JsonElement sequence in sequences.EnumerateArray())
        {
            ValidateActionArray(sequence, parametersByTool);
        }
    }

    private static void ValidateAction(JsonElement action, IReadOnlyDictionary<string, HashSet<string>> parametersByTool)
    {
        string kind = action.GetProperty("kind").GetString()!;
        string name = action.GetProperty("name").GetString()!;
        Assert.Contains(kind, new[] { "mcp", "ordinary" });
        if (kind == "mcp")
        {
            Assert.Contains(name, parametersByTool.Keys);
        }
        else
        {
            Assert.Contains(name, OrdinaryActionNames);
        }
    }

    private static void ValidateStopPredicate(JsonElement predicate, string scenarioId)
    {
        HashSet<string> allowedKeys = new(StringComparer.Ordinal)
        {
            "path", "type", "equals", "minimum", "minItems", "nonBlank", "requiredProperties", "minProperties"
        };
        foreach (JsonProperty property in predicate.EnumerateObject())
        {
            Assert.Contains(property.Name, allowedKeys);
        }

        Assert.False(string.IsNullOrWhiteSpace(predicate.GetProperty("path").GetString()), scenarioId);
        string type = predicate.GetProperty("type").GetString()!;
        Assert.Contains(type, new[] { "string", "number", "boolean", "array", "object" });
        string[] constraints = allowedKeys.Where(key => key is not ("path" or "type") && predicate.TryGetProperty(key, out _)).ToArray();
        Assert.Single(constraints);
        JsonElement constraint = predicate.GetProperty(constraints[0]);
        switch (constraints[0])
        {
            case "equals":
                Assert.Equal(type switch
                {
                    "string" => JsonValueKind.String,
                    "number" => JsonValueKind.Number,
                    "boolean" => constraint.ValueKind,
                    "array" => JsonValueKind.Array,
                    "object" => JsonValueKind.Object,
                    _ => JsonValueKind.Undefined
                }, constraint.ValueKind);
                if (type == "number") Assert.True(constraint.TryGetInt64(out _));
                if (type == "boolean") Assert.True(constraint.ValueKind is JsonValueKind.True or JsonValueKind.False);
                break;
            case "minimum":
                Assert.Equal("number", type);
                Assert.True(constraint.TryGetInt64(out long minimumValue) && minimumValue >= 0);
                break;
            case "minItems":
                Assert.Equal("array", type);
                Assert.True(constraint.TryGetInt32(out int minimumItems) && minimumItems > 0);
                break;
            case "nonBlank":
                Assert.Equal("string", type);
                Assert.Equal(JsonValueKind.True, constraint.ValueKind);
                break;
            case "requiredProperties":
                Assert.Equal("object", type);
                Assert.Equal(JsonValueKind.Array, constraint.ValueKind);
                Assert.NotEmpty(constraint.EnumerateArray());
                Assert.All(constraint.EnumerateArray(), name => Assert.False(string.IsNullOrWhiteSpace(name.GetString())));
                break;
            case "minProperties":
                Assert.Equal("object", type);
                Assert.True(constraint.TryGetInt32(out int minimumProperties) && minimumProperties > 0);
                break;
        }
    }

    private static void AssertStopPredicateMatches(JsonElement fields, JsonElement predicate, string scenarioId)
    {
        Assert.True(TryGetJsonPath(fields, predicate.GetProperty("path").GetString()!, out JsonElement value), scenarioId);
        string type = predicate.GetProperty("type").GetString()!;
        Assert.Equal(type switch
        {
            "string" => JsonValueKind.String,
            "number" => JsonValueKind.Number,
            "boolean" => value.ValueKind,
            "array" => JsonValueKind.Array,
            "object" => JsonValueKind.Object,
            _ => JsonValueKind.Undefined
        }, value.ValueKind);

        if (predicate.TryGetProperty("equals", out JsonElement equals))
        {
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(value.GetRawText()), JsonNode.Parse(equals.GetRawText())), scenarioId);
        }
        else if (predicate.TryGetProperty("minimum", out JsonElement minimum))
        {
            Assert.True(value.GetInt64() >= minimum.GetInt64(), scenarioId);
        }
        else if (predicate.TryGetProperty("minItems", out JsonElement minItems))
        {
            Assert.True(value.GetArrayLength() >= minItems.GetInt32(), scenarioId);
        }
        else if (predicate.TryGetProperty("nonBlank", out _))
        {
            Assert.False(string.IsNullOrWhiteSpace(value.GetString()), scenarioId);
        }
        else if (predicate.TryGetProperty("requiredProperties", out JsonElement requiredProperties))
        {
            foreach (JsonElement property in requiredProperties.EnumerateArray()) Assert.True(value.TryGetProperty(property.GetString()!, out _), scenarioId);
        }
        else if (predicate.TryGetProperty("minProperties", out JsonElement minProperties))
        {
            Assert.True(value.EnumerateObject().Count() >= minProperties.GetInt32(), scenarioId);
        }
    }

    private static bool TryGetJsonPath(JsonElement root, string path, out JsonElement value)
    {
        value = root;
        foreach (string part in path.Split('.'))
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(part, out value)) return false;
        }

        return true;
    }

    private static void ValidateBatchCliPayload(JsonElement arguments)
    {
        JsonElement requests = arguments.GetProperty("requests");
        Assert.Equal(JsonValueKind.Array, requests.ValueKind);
        Assert.Equal(2, requests.GetArrayLength());
        Assert.Equal("resolve-target", requests[0].GetProperty("command").GetString());
        Assert.Equal("outline", requests[1].GetProperty("command").GetString());
        Assert.Equal("WidgetRecord", requests[0].GetProperty("query").GetString());
        Assert.Equal("tests/fixtures/SymbolNavigationFixture/FixtureCode.cs", requests[1].GetProperty("file").GetString());
        foreach (JsonElement request in requests.EnumerateArray())
        {
            Assert.True(request.TryGetProperty("id", out _));
            Assert.False(request.TryGetProperty("tool", out _));
            Assert.False(request.TryGetProperty("arguments", out _));
        }
    }

    [Fact]
    public async Task ToolSelectionScorer_StopReasonWithoutResultEvidenceCannotEarnFullCredit()
    {
        string repoRoot = FindRepositoryRoot();
        string tempStem = Path.Combine(Path.GetTempPath(), "navlyn-stop-evidence-" + Guid.NewGuid().ToString("N"));
        string scenarioPath = tempStem + ".scenarios.json";
        string tracePath = tempStem + ".trace.json";
        string outputPath = tempStem + ".report.json";
        string scenarioJson = """
        {
          "schemaVersion": "navlyn.tool-selection-eval.v2",
          "scenarios": [{
            "id": "evidence-required",
            "taskClass": "exact-position",
            "workspace": "tests/fixtures/SymbolNavigationFixture/SymbolNavigationFixture.csproj",
            "prompt": "Read one known declaration.",
            "expectedSkillActivation": true,
            "expectedFirstAction": { "kind": "mcp", "name": "navlyn_read" },
            "acceptedSequences": [[{ "kind": "mcp", "name": "navlyn_read" }]],
            "forbiddenTools": [],
            "forbiddenSequences": [],
            "requiredArguments": [{ "callIndex": 0, "path": "candidateId", "equals": "sym:v1:test" }],
            "expectedStopEvidence": { "acceptedStopReasons": ["source-returned"], "alternatives": [{ "callIndex": 0, "requiredFields": ["result.slices"], "predicates": [{ "path": "result.slices", "type": "array", "minItems": 1 }] }] },
            "semanticCorrectnessChecks": ["targetAnchored"],
            "maxCalls": 1,
            "stdoutBudgetChars": 20000,
            "latencyBudgetMs": 20000,
            "unsupportedClaims": ["runtime proof"],
            "availabilityFreshnessSetup": { "mcpAvailability": "available", "workspaceAvailability": "available", "workspaceFreshness": "fresh" }
          }]
        }
        """;
        string traceJson = """
        {
          "schemaVersion": "navlyn.tool-selection-eval.trace.v2",
          "traces": [{
            "scenarioId": "evidence-required",
            "skillActivated": true,
            "calls": [{ "kind": "mcp", "name": "navlyn_read", "arguments": { "candidateId": "sym:v1:test" }, "selectedResultFields": { "result": { "command": "symbol-source" } } }],
            "stopReason": "source-returned",
            "semanticChecks": { "targetAnchored": true },
            "claims": [],
            "environment": { "mcpAvailability": "available", "workspaceAvailability": "available", "workspaceFreshness": "fresh" },
            "stdoutChars": 100,
            "latencyMs": 100,
            "outputValid": true,
            "stderrClean": true
          }]
        }
        """;

        try
        {
            await File.WriteAllTextAsync(scenarioPath, scenarioJson);
            await File.WriteAllTextAsync(tracePath, traceJson);
            using Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "pwsh",
                    WorkingDirectory = repoRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };
            process.StartInfo.ArgumentList.Add("-NoProfile");
            process.StartInfo.ArgumentList.Add("-File");
            process.StartInfo.ArgumentList.Add(Path.Combine(repoRoot, "scripts", "test-tool-selection-eval.ps1"));
            process.StartInfo.ArgumentList.Add("-ScenarioFile");
            process.StartInfo.ArgumentList.Add(scenarioPath);
            process.StartInfo.ArgumentList.Add("-TraceFile");
            process.StartInfo.ArgumentList.Add(tracePath);
            process.StartInfo.ArgumentList.Add("-Output");
            process.StartInfo.ArgumentList.Add(outputPath);

            process.Start();
            string stdout = await process.StandardOutput.ReadToEndAsync();
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            Assert.NotEqual(0, process.ExitCode);
            Assert.True(File.Exists(outputPath), $"stdout:{Environment.NewLine}{stdout}{Environment.NewLine}stderr:{Environment.NewLine}{stderr}");
            using JsonDocument report = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
            JsonElement result = report.RootElement.GetProperty("results")[0];
            Assert.False(result.GetProperty("criteria").GetProperty("stopEvidence").GetBoolean());
            Assert.True(report.RootElement.GetProperty("score").GetDouble() < 1.0);
        }
        finally
        {
            File.Delete(scenarioPath);
            File.Delete(tracePath);
            File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task ToolSelectionScorer_TypedStopPredicatesAndExternalTraceTypesAreEnforced()
    {
        string repoRoot = FindRepositoryRoot();
        string sourcePath = Path.Combine(repoRoot, "docs", "evals", "tool-selection.scenarios.json");
        JsonObject source = JsonNode.Parse(await File.ReadAllTextAsync(sourcePath))!.AsObject();
        var stopMutations = new (string ScenarioId, Action<JsonObject> Apply)[]
        {
            ("exact-position-02", trace => trace["calls"]![0]! ["selectedResultFields"]!["result"]!["slices"] = new JsonArray()),
            ("ambiguous-symbol-identity-01", trace => trace["calls"]![0]! ["selectedResultFields"]!["result"]!["candidates"] = new JsonArray()),
            ("exact-position-02", trace => trace["calls"]![0]! ["selectedResultFields"]!["result"]!["slices"] = false),
            ("linked-file-09", trace => trace["calls"]![0]! ["selectedResultFields"]!["result"]!["project"] = new JsonObject { ["wrong"] = "shape" }),
            ("generated-code-avoidance-10", trace => trace["calls"]![0]! ["selectedResultFields"]!["result"]!["candidateCount"] = 1),
            ("missing-workspace-34", trace => trace["calls"]![0]! ["selectedResultFields"]!["result"]!["workspace"]!["loaded"] = true)
        };

        foreach (var mutation in stopMutations)
        {
            JsonObject scenario = (JsonObject)source["scenarios"]!.AsArray().First(item => (string?)item!["id"] == mutation.ScenarioId)!.DeepClone();
            JsonObject trace = (JsonObject)scenario["baselineTrace"]!.DeepClone();
            trace["scenarioId"] = mutation.ScenarioId;
            mutation.Apply(trace);
            (int exitCode, JsonDocument? report, string stderr) = await ScoreExternalScenarioAsync(repoRoot, scenario, trace);
            Assert.NotEqual(0, exitCode);
            Assert.NotNull(report);
            Assert.False(report!.RootElement.GetProperty("results")[0].GetProperty("criteria").GetProperty("stopEvidence").GetBoolean(), stderr);
            report.Dispose();
        }

        JsonObject missingScenario = (JsonObject)source["scenarios"]!.AsArray().First(item => (string?)item!["id"] == "missing-workspace-34")!.DeepClone();
        JsonObject missingTrace = (JsonObject)missingScenario["baselineTrace"]!.DeepClone();
        missingTrace["scenarioId"] = "missing-workspace-34";
        (int positiveExit, JsonDocument? positiveReport, _) = await ScoreExternalScenarioAsync(repoRoot, missingScenario, missingTrace);
        Assert.Equal(0, positiveExit);
        Assert.True(positiveReport!.RootElement.GetProperty("results")[0].GetProperty("criteria").GetProperty("stopEvidence").GetBoolean());
        positiveReport.Dispose();

        var strictMutations = new (string ScenarioId, Action<JsonObject> Apply, string ErrorFragment)[]
        {
            ("exact-position-02", trace => trace["skillActivated"] = "false", "Malformed required v2 trace fields"),
            ("ambiguous-symbol-identity-01", trace => trace["semanticChecks"]!["ambiguityReported"] = "false", "Semantic checks must be JSON booleans"),
            ("exact-position-02", trace => trace["outputValid"] = "false", "Malformed required v2 trace fields"),
            ("comments-23", trace => trace["stdoutChars"] = "1500", "Malformed required v2 trace fields"),
            ("comments-23", trace => trace["latencyMs"] = "500", "Malformed required v2 trace fields"),
            ("comments-23", trace => trace["claims"] = new JsonArray(JsonValue.Create(1)), "Claims must be JSON strings"),
            ("comments-23", trace => trace["environment"]!["workspaceFreshness"] = true, "Environment values must be JSON strings"),
            ("comments-23", trace => trace["stopReason"] = 1, "Malformed required v2 trace fields"),
            ("comments-23", trace => trace["calls"]![0]!["name"] = 1, "Malformed call in v2 trace"),
            ("comments-23", trace => trace["calls"]![0]!["arguments"] = "not-an-object", "Malformed call in v2 trace")
        };
        foreach (var mutation in strictMutations)
        {
            JsonObject scenario = (JsonObject)source["scenarios"]!.AsArray().First(item => (string?)item!["id"] == mutation.ScenarioId)!.DeepClone();
            JsonObject trace = (JsonObject)scenario["baselineTrace"]!.DeepClone();
            trace["scenarioId"] = mutation.ScenarioId;
            mutation.Apply(trace);
            (int exitCode, JsonDocument? report, string stderr) = await ScoreExternalScenarioAsync(repoRoot, scenario, trace);
            Assert.NotEqual(0, exitCode);
            Assert.Null(report);
            Assert.True(stderr.Contains(mutation.ErrorFragment, StringComparison.Ordinal), $"{mutation.ScenarioId}: {stderr}");
        }

        JsonObject duplicateScenario = (JsonObject)source["scenarios"]!.AsArray().First(item => (string?)item!["id"] == "comments-23")!.DeepClone();
        JsonObject duplicateTrace = (JsonObject)duplicateScenario["baselineTrace"]!.DeepClone();
        duplicateTrace["scenarioId"] = "comments-23";
        (int duplicateExit, JsonDocument? duplicateReport, string duplicateError) = await ScoreExternalScenarioAsync(repoRoot, duplicateScenario, duplicateTrace, duplicateCount: 2);
        Assert.NotEqual(0, duplicateExit);
        Assert.Null(duplicateReport);
        Assert.Contains("Duplicate trace scenarioId", duplicateError);

        JsonObject malformedScenario = (JsonObject)source["scenarios"]!.AsArray().First(item => (string?)item!["id"] == "exact-position-02")!.DeepClone();
        JsonObject malformedTrace = (JsonObject)malformedScenario["baselineTrace"]!.DeepClone();
        malformedTrace["scenarioId"] = "exact-position-02";
        malformedScenario["expectedStopEvidence"]!["alternatives"]![0]!["predicates"]![0]!["unboundedExpression"] = true;
        (int malformedExit, JsonDocument? malformedReport, string malformedError) = await ScoreExternalScenarioAsync(repoRoot, malformedScenario, malformedTrace);
        Assert.NotEqual(0, malformedExit);
        Assert.Null(malformedReport);
        Assert.Contains("Unsupported stop predicate key", malformedError);

        JsonObject invalidCombination = (JsonObject)source["scenarios"]!.AsArray().First(item => (string?)item!["id"] == "exact-position-02")!.DeepClone();
        JsonObject invalidCombinationTrace = (JsonObject)invalidCombination["baselineTrace"]!.DeepClone();
        invalidCombinationTrace["scenarioId"] = "exact-position-02";
        invalidCombination["expectedStopEvidence"]!["alternatives"]![0]!["predicates"]![0]!["minimum"] = 1;
        (int combinationExit, JsonDocument? combinationReport, string combinationError) = await ScoreExternalScenarioAsync(repoRoot, invalidCombination, invalidCombinationTrace);
        Assert.NotEqual(0, combinationExit);
        Assert.Null(combinationReport);
        Assert.Contains("exactly one supported constraint", combinationError);
    }

    private static async Task<(int ExitCode, JsonDocument? Report, string Stderr)> ScoreExternalScenarioAsync(
        string repoRoot,
        JsonObject scenario,
        JsonObject trace,
        int duplicateCount = 1)
    {
        string tempStem = Path.Combine(Path.GetTempPath(), "navlyn-predicate-trace-" + Guid.NewGuid().ToString("N"));
        string scenarioPath = tempStem + ".scenarios.json";
        string tracePath = tempStem + ".trace.json";
        string outputPath = tempStem + ".report.json";
        JsonObject scenarioDocument = new()
        {
            ["schemaVersion"] = "navlyn.tool-selection-eval.v2",
            ["scenarios"] = new JsonArray((JsonObject)scenario.DeepClone())
        };
        JsonArray traceArray = new();
        for (int index = 0; index < duplicateCount; index++) traceArray.Add((JsonObject)trace.DeepClone());
        JsonObject traceDocument = new()
        {
            ["schemaVersion"] = "navlyn.tool-selection-eval.trace.v2",
            ["traces"] = traceArray
        };
        try
        {
            await File.WriteAllTextAsync(scenarioPath, scenarioDocument.ToJsonString());
            await File.WriteAllTextAsync(tracePath, traceDocument.ToJsonString());
            using Process process = StartScorer(repoRoot, scenarioPath, tracePath, outputPath);
            _ = await process.StandardOutput.ReadToEndAsync();
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            JsonDocument? report = File.Exists(outputPath) ? JsonDocument.Parse(await File.ReadAllTextAsync(outputPath)) : null;
            return (process.ExitCode, report, stderr);
        }
        finally
        {
            File.Delete(scenarioPath);
            File.Delete(tracePath);
            File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task ToolSelectionScorer_NegativeOutputMeasurementsFailBudgets()
    {
        string repoRoot = FindRepositoryRoot();
        string tempStem = Path.Combine(Path.GetTempPath(), "navlyn-negative-budget-" + Guid.NewGuid().ToString("N"));
        string scenarioPath = tempStem + ".scenarios.json";
        string tracePath = tempStem + ".trace.json";
        string outputPath = tempStem + ".report.json";
        string scenarioJson = """
        {
          "schemaVersion": "navlyn.tool-selection-eval.v2",
          "scenarios": [{
            "id": "negative-measurement",
            "taskClass": "markdown",
            "workspace": "docs/evals/tool-selection.md",
            "prompt": "Find the eval heading.",
            "expectedSkillActivation": false,
            "expectedFirstAction": { "kind": "ordinary", "name": "rg" },
            "acceptedSequences": [[{ "kind": "ordinary", "name": "rg" }]],
            "forbiddenTools": [],
            "forbiddenSequences": [],
            "requiredArguments": [],
            "expectedStopEvidence": { "acceptedStopReasons": ["text-search-complete"], "alternatives": [{ "callIndex": 0, "requiredFields": ["stdout"], "predicates": [{ "path": "stdout", "type": "string", "nonBlank": true }] }] },
            "semanticCorrectnessChecks": ["foundHeading"],
            "maxCalls": 1,
            "stdoutBudgetChars": 1000,
            "latencyBudgetMs": 1000,
            "unsupportedClaims": [],
            "availabilityFreshnessSetup": { "mcpAvailability": "available", "workspaceAvailability": "available", "workspaceFreshness": "fresh" }
          }]
        }
        """;
        string traceJson = """
        {
          "schemaVersion": "navlyn.tool-selection-eval.trace.v2",
          "traces": [{
            "scenarioId": "negative-measurement",
            "skillActivated": false,
            "calls": [{ "kind": "ordinary", "name": "rg", "arguments": {}, "selectedResultFields": { "stdout": "# Navlyn Tool-Selection Eval" } }],
            "stopReason": "text-search-complete",
            "semanticChecks": { "foundHeading": true },
            "claims": [],
            "environment": { "mcpAvailability": "available", "workspaceAvailability": "available", "workspaceFreshness": "fresh" },
            "stdoutChars": -1,
            "latencyMs": -1,
            "outputValid": true,
            "stderrClean": true
          }]
        }
        """;

        try
        {
            await File.WriteAllTextAsync(scenarioPath, scenarioJson);
            await File.WriteAllTextAsync(tracePath, traceJson);
            using Process process = StartScorer(repoRoot, scenarioPath, tracePath, outputPath);
            _ = await process.StandardOutput.ReadToEndAsync();
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            Assert.NotEqual(0, process.ExitCode);
            Assert.Contains("Malformed required v2 trace fields", stderr);
            Assert.False(File.Exists(outputPath));
        }
        finally
        {
            File.Delete(scenarioPath);
            File.Delete(tracePath);
            File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task ToolSelectionScorer_DeepComparesArgumentsAndRejectsUnsupportedClaimsCaseInsensitively()
    {
        string repoRoot = FindRepositoryRoot();
        string tempStem = Path.Combine(Path.GetTempPath(), "navlyn-routing-deep-" + Guid.NewGuid().ToString("N"));
        string scenarioPath = tempStem + ".scenarios.json";
        string tracePath = tempStem + ".trace.json";
        string outputPath = tempStem + ".report.json";
        string scenarioJson = """
        {
          "schemaVersion": "navlyn.tool-selection-eval.v2",
          "scenarios": [{
            "id": "deep-contract",
            "taskClass": "two-fact-batch",
            "workspace": "tests/fixtures/SymbolNavigationFixture/SymbolNavigationFixture.csproj",
            "prompt": "Search for two fixture terms.",
            "expectedSkillActivation": false,
            "expectedFirstAction": { "kind": "ordinary", "name": "rg" },
            "acceptedSequences": [[{ "kind": "ordinary", "name": "rg" }]],
            "forbiddenTools": [],
            "forbiddenSequences": [],
            "requiredArguments": [{ "callIndex": 0, "path": "patterns", "equals": ["Factory members", "Widget"] }],
            "expectedStopEvidence": { "acceptedStopReasons": ["text-match"], "alternatives": [{ "callIndex": 0, "requiredFields": ["stdout"], "predicates": [{ "path": "stdout", "type": "string", "nonBlank": true }] }] },
            "semanticCorrectnessChecks": ["textMatch"],
            "maxCalls": 1,
            "stdoutBudgetChars": 1000,
            "latencyBudgetMs": 1000,
            "unsupportedClaims": ["runtime behavior", "security guarantee"],
            "availabilityFreshnessSetup": { "mcpAvailability": "available", "workspaceAvailability": "available", "workspaceFreshness": "fresh" }
          }]
        }
        """;
        string traceJson = """
        {
          "schemaVersion": "navlyn.tool-selection-eval.trace.v2",
          "traces": [{
            "scenarioId": "deep-contract",
            "skillActivated": false,
            "calls": [{ "kind": "ordinary", "name": "rg", "arguments": { "patterns": ["Factory members", "Wrong"] }, "selectedResultFields": { "stdout": "Factory members" } }],
            "stopReason": "text-match",
            "semanticChecks": { "textMatch": true },
            "claims": ["RUNTIME BEHAVIOR is verified"],
            "environment": { "mcpAvailability": "available", "workspaceAvailability": "available", "workspaceFreshness": "fresh" },
            "stdoutChars": 80,
            "latencyMs": 10,
            "outputValid": true,
            "stderrClean": true
          }]
        }
        """;

        try
        {
            await File.WriteAllTextAsync(scenarioPath, scenarioJson);
            await File.WriteAllTextAsync(tracePath, traceJson);
            using Process process = StartScorer(repoRoot, scenarioPath, tracePath, outputPath);
            string stdout = await process.StandardOutput.ReadToEndAsync();
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            Assert.NotEqual(0, process.ExitCode);
            Assert.True(File.Exists(outputPath), $"stdout:{Environment.NewLine}{stdout}{Environment.NewLine}stderr:{Environment.NewLine}{stderr}");
            using JsonDocument report = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
            JsonElement criteria = report.RootElement.GetProperty("results")[0].GetProperty("criteria");
            Assert.False(criteria.GetProperty("requiredArguments").GetBoolean());
            Assert.False(criteria.GetProperty("unsupportedClaims").GetBoolean());

            await File.WriteAllTextAsync(tracePath, traceJson.Replace("navlyn.tool-selection-eval.trace.v2", "navlyn.tool-selection-eval.trace.v1", StringComparison.Ordinal));
            using Process unsupportedSchema = StartScorer(repoRoot, scenarioPath, tracePath, outputPath);
            _ = await unsupportedSchema.StandardOutput.ReadToEndAsync();
            string schemaStderr = await unsupportedSchema.StandardError.ReadToEndAsync();
            await unsupportedSchema.WaitForExitAsync();
            Assert.NotEqual(0, unsupportedSchema.ExitCode);
            Assert.Contains("Unsupported tool-selection trace schemaVersion", schemaStderr, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(scenarioPath);
            File.Delete(tracePath);
            File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task ToolSelectionEvalRunner_BaselineTracesPass()
    {
        string repoRoot = FindRepositoryRoot();
        string outputPath = Path.Combine(Path.GetTempPath(), "navlyn-tool-selection-" + Guid.NewGuid().ToString("N") + ".json");

        try
        {
            using Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "pwsh",
                    WorkingDirectory = repoRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };
            process.StartInfo.ArgumentList.Add("-NoProfile");
            process.StartInfo.ArgumentList.Add("-File");
            process.StartInfo.ArgumentList.Add(Path.Combine(repoRoot, "scripts", "test-tool-selection-eval.ps1"));
            process.StartInfo.ArgumentList.Add("-UseBaselineTraces");
            process.StartInfo.ArgumentList.Add("-Output");
            process.StartInfo.ArgumentList.Add(outputPath);

            process.Start();
            string stdout = await process.StandardOutput.ReadToEndAsync();
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            Assert.True(process.ExitCode == 0, $"stdout:{Environment.NewLine}{stdout}{Environment.NewLine}stderr:{Environment.NewLine}{stderr}");
            using JsonDocument report = JsonDocument.Parse(File.ReadAllText(outputPath));
            JsonElement root = report.RootElement;
            Assert.True(root.GetProperty("passed").GetBoolean());
            Assert.Equal(1.0, root.GetProperty("score").GetDouble());
            Assert.Equal("navlyn.tool-selection-eval.report.v2", root.GetProperty("schemaVersion").GetString());
            Assert.Equal(37, root.GetProperty("scenarioCount").GetInt32());
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    private static Process StartScorer(string repoRoot, string scenarioPath, string tracePath, string outputPath)
    {
        Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "pwsh",
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-File");
        process.StartInfo.ArgumentList.Add(Path.Combine(repoRoot, "scripts", "test-tool-selection-eval.ps1"));
        process.StartInfo.ArgumentList.Add("-ScenarioFile");
        process.StartInfo.ArgumentList.Add(scenarioPath);
        process.StartInfo.ArgumentList.Add("-TraceFile");
        process.StartInfo.ArgumentList.Add(tracePath);
        process.StartInfo.ArgumentList.Add("-Output");
        process.StartInfo.ArgumentList.Add(outputPath);
        process.Start();
        return process;
    }

    [Fact]
    public void WrongSymbolAvoidanceScenarioFile_IsMachineReadable()
    {
        string repoRoot = FindRepositoryRoot();
        string scenarioPath = Path.Combine(repoRoot, "docs", "evals", "wrong-symbol-avoidance.scenarios.json");

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(scenarioPath));
        JsonElement root = document.RootElement;

        Assert.Equal("navlyn.wrong-symbol-avoidance-eval.v1", root.GetProperty("schemaVersion").GetString());
        Assert.Equal(1.0, root.GetProperty("minimumScore").GetDouble());
        JsonElement scenarios = root.GetProperty("scenarios");
        Assert.True(scenarios.GetArrayLength() >= 2);
        foreach (JsonElement scenario in scenarios.EnumerateArray())
        {
            Assert.False(string.IsNullOrWhiteSpace(scenario.GetProperty("id").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(scenario.GetProperty("workspace").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(scenario.GetProperty("file").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(scenario.GetProperty("query").GetString()));
            Assert.True(scenario.TryGetProperty("textSearchExpectedWrongLine", out JsonElement line) && line.GetInt32() > 0);
            Assert.False(string.IsNullOrWhiteSpace(scenario.GetProperty("navlynExpectedFirstConfidence").GetString()));
        }
    }

    [Fact]
    public async Task WrongSymbolAvoidanceEvalRunner_Passes()
    {
        string repoRoot = FindRepositoryRoot();
        string outputPath = Path.Combine(Path.GetTempPath(), "navlyn-wrong-symbol-" + Guid.NewGuid().ToString("N") + ".json");

        try
        {
            using Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "pwsh",
                    WorkingDirectory = repoRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };
            process.StartInfo.ArgumentList.Add("-NoProfile");
            process.StartInfo.ArgumentList.Add("-File");
            process.StartInfo.ArgumentList.Add(Path.Combine(repoRoot, "scripts", "test-wrong-symbol-avoidance-eval.ps1"));
            process.StartInfo.ArgumentList.Add("-NoBuild");
            process.StartInfo.ArgumentList.Add("-Output");
            process.StartInfo.ArgumentList.Add(outputPath);

            process.Start();
            string stdout = await process.StandardOutput.ReadToEndAsync();
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            Assert.True(process.ExitCode == 0, $"stdout:{Environment.NewLine}{stdout}{Environment.NewLine}stderr:{Environment.NewLine}{stderr}");
            using JsonDocument report = JsonDocument.Parse(File.ReadAllText(outputPath));
            JsonElement root = report.RootElement;
            Assert.Equal("navlyn.wrong-symbol-avoidance-eval.report.v1", root.GetProperty("schemaVersion").GetString());
            Assert.Equal(1.0, root.GetProperty("score").GetDouble());
            Assert.Equal(root.GetProperty("total").GetInt32(), root.GetProperty("passed").GetInt32());
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public void McpAgentTraceFile_IsMachineReadable()
    {
        string repoRoot = FindRepositoryRoot();
        string tracePath = Path.Combine(repoRoot, "docs", "evals", "mcp-agent-traces.replay.json");

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(tracePath));
        JsonElement root = document.RootElement;

        Assert.Equal("navlyn.mcp-agent-trace-eval.v2", root.GetProperty("schemaVersion").GetString());
        JsonElement traces = root.GetProperty("traces");
        Assert.Equal(34, traces.GetArrayLength());
        string scenarioPath = Path.Combine(repoRoot, "docs", "evals", "tool-selection.scenarios.json");
        using JsonDocument scenarioDocument = JsonDocument.Parse(File.ReadAllText(scenarioPath));
        JsonElement[] scenarios = scenarioDocument.RootElement.GetProperty("scenarios").EnumerateArray().ToArray();
        Dictionary<string, JsonElement> scenariosById = scenarios.ToDictionary(scenario => scenario.GetProperty("id").GetString()!, StringComparer.Ordinal);
        Dictionary<string, HashSet<string>> parametersByTool = GetMcpParameterNames();
        HashSet<string> seenScenarioIds = new(StringComparer.Ordinal);
        foreach (JsonElement trace in traces.EnumerateArray())
        {
            Assert.Equal("navlyn.mcp-agent-trace.v2", trace.GetProperty("schemaVersion").GetString());
            string scenarioId = trace.GetProperty("scenarioId").GetString()!;
            Assert.Contains(scenarioId, scenariosById.Keys);
            Assert.True(seenScenarioIds.Add(scenarioId));
            Assert.True(trace.GetProperty("skillActivated").ValueKind is JsonValueKind.True or JsonValueKind.False);
            Assert.True(trace.GetProperty("calls").GetArrayLength() > 0);
            JsonElement authoritativeCalls = scenariosById[scenarioId].GetProperty("baselineTrace").GetProperty("calls");
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(trace.GetProperty("calls").GetRawText()), JsonNode.Parse(authoritativeCalls.GetRawText())), $"Calls must remain equal to P6A baseline: {scenarioId}");
            foreach (JsonElement call in trace.GetProperty("calls").EnumerateArray())
            {
                string kind = call.GetProperty("kind").GetString()!;
                string name = call.GetProperty("name").GetString()!;
                Assert.True(kind == "mcp" ? CurrentMcpTools.Contains(name) : kind == "ordinary" && OrdinaryActionNames.Contains(name), $"{kind}:{name}");
                Assert.Equal(JsonValueKind.Object, call.GetProperty("arguments").ValueKind);
                Assert.Equal(JsonValueKind.Object, call.GetProperty("selectedResultFields").ValueKind);
                if (kind == "mcp")
                {
                    if (name == "navlyn_batch")
                    {
                        ValidateBatchCliPayload(call.GetProperty("arguments"));
                    }
                    else
                    {
                        foreach (JsonProperty argument in call.GetProperty("arguments").EnumerateObject())
                        {
                            Assert.Contains(argument.Name, parametersByTool[name]);
                        }
                    }
                }
            }
            Assert.True(trace.GetProperty("stdoutChars").GetInt32() >= 0);
            Assert.True(trace.GetProperty("latencyMs").GetInt32() >= 0);
            Assert.Equal(JsonValueKind.Object, trace.GetProperty("semanticChecks").ValueKind);
            Assert.Equal(JsonValueKind.Object, trace.GetProperty("environment").ValueKind);
            Assert.Equal(JsonValueKind.Object, trace.GetProperty("editTiming").ValueKind);
            Assert.True(trace.GetProperty("editTiming").GetProperty("editAttempted").ValueKind is JsonValueKind.True or JsonValueKind.False);
            Assert.True(trace.GetProperty("editTiming").GetProperty("anchorBeforeEdit").ValueKind is JsonValueKind.True or JsonValueKind.False);
            Assert.False(string.IsNullOrWhiteSpace(trace.GetProperty("anchorState").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(trace.GetProperty("stopReason").GetString()));
            Assert.True(trace.GetProperty("outputValid").ValueKind is JsonValueKind.True or JsonValueKind.False);
            Assert.True(trace.GetProperty("stderrClean").ValueKind is JsonValueKind.True or JsonValueKind.False);
            Assert.True(trace.GetProperty("broadChecklist").ValueKind is JsonValueKind.True or JsonValueKind.False);
            Assert.Equal(JsonValueKind.Array, trace.GetProperty("claims").ValueKind);
            Assert.All(trace.GetProperty("claims").EnumerateArray(), claim => Assert.Equal(JsonValueKind.String, claim.ValueKind));
            Assert.All(trace.GetProperty("semanticChecks").EnumerateObject(), check => Assert.True(check.Value.ValueKind is JsonValueKind.True or JsonValueKind.False));
            foreach (JsonProperty environmentValue in trace.GetProperty("environment").EnumerateObject())
            {
                Assert.Equal(JsonValueKind.String, environmentValue.Value.ValueKind);
            }
            Assert.All(trace.GetProperty("calls").EnumerateArray(), call =>
            {
                Assert.Equal(JsonValueKind.String, call.GetProperty("kind").ValueKind);
                Assert.Equal(JsonValueKind.String, call.GetProperty("name").ValueKind);
            });
        }
    }

    [Fact]
    public async Task McpAgentTraceEvalRunner_Passes()
    {
        string repoRoot = FindRepositoryRoot();
        string outputPath = Path.Combine(Path.GetTempPath(), "navlyn-agent-trace-" + Guid.NewGuid().ToString("N") + ".json");

        try
        {
            using Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "pwsh",
                    WorkingDirectory = repoRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };
            process.StartInfo.ArgumentList.Add("-NoProfile");
            process.StartInfo.ArgumentList.Add("-File");
            process.StartInfo.ArgumentList.Add(Path.Combine(repoRoot, "scripts", "test-mcp-agent-trace-eval.ps1"));
            process.StartInfo.ArgumentList.Add("-Output");
            process.StartInfo.ArgumentList.Add(outputPath);

            process.Start();
            string stdout = await process.StandardOutput.ReadToEndAsync();
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            Assert.True(process.ExitCode == 0, $"stdout:{Environment.NewLine}{stdout}{Environment.NewLine}stderr:{Environment.NewLine}{stderr}");
            using JsonDocument report = JsonDocument.Parse(File.ReadAllText(outputPath));
            JsonElement root = report.RootElement;
            Assert.Equal("navlyn.mcp-agent-trace-eval.report.v2", root.GetProperty("schemaVersion").GetString());
            Assert.True(root.GetProperty("passed").GetBoolean());
            Assert.Equal(34, root.GetProperty("traceCount").GetInt32());
            Assert.Equal(34, root.GetProperty("results").GetArrayLength());
            JsonElement metrics = root.GetProperty("metrics");
            Assert.Equal(27, metrics.GetProperty("semanticNavlynRecall").GetProperty("denominator").GetInt32());
            Assert.Equal(34, metrics.GetProperty("evidenceBackedStop").GetProperty("numerator").GetInt32());
            Assert.Equal(2, metrics.GetProperty("identityCriticalEditAnchoredBeforeEdit").GetProperty("denominator").GetInt32());
            Assert.Equal("not-applicable-live-only", root.GetProperty("notApplicable").GetProperty("skillOnLatencyRegressionVsSkillOff").GetProperty("status").GetString());
            Assert.Equal("not-applicable", root.GetProperty("notApplicable").GetProperty("newFocusedToolInvalidInputCoverage").GetProperty("status").GetString());
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task McpAgentTraceEvalRunner_RejectsInvalidEvidenceAndActions()
    {
        string repoRoot = FindRepositoryRoot();
        string tracePath = Path.Combine(repoRoot, "docs", "evals", "mcp-agent-traces.replay.json");
        string scenarioPath = Path.Combine(repoRoot, "docs", "evals", "tool-selection.scenarios.json");
        JsonObject original = JsonNode.Parse(await File.ReadAllTextAsync(tracePath))!.AsObject();
        string retiredName = "navlyn_" + "symbol_edges";
        var mutations = new (string Name, Action<JsonObject> Apply, string Criterion)[]
        {
            ("missing-evidence", root => root["traces"]![0]!["calls"]![0]!["selectedResultFields"] = new JsonObject { ["result"] = new JsonObject { ["command"] = "target" } }, "stopEvidence"),
            ("empty-source-slices", root => root["traces"]!.AsArray().First(item => (string?)item!["scenarioId"] == "exact-position-02")!["calls"]![0]!["selectedResultFields"]!["result"]!["slices"] = new JsonArray(), "stopEvidence"),
            ("empty-ambiguous-candidates", root => root["traces"]!.AsArray().First(item => (string?)item!["scenarioId"] == "ambiguous-symbol-identity-01")!["calls"]![0]!["selectedResultFields"]!["result"]!["candidates"] = new JsonArray(), "stopEvidence"),
            ("wrong-source-slice-type", root => root["traces"]!.AsArray().First(item => (string?)item!["scenarioId"] == "exact-position-02")!["calls"]![0]!["selectedResultFields"]!["result"]!["slices"] = false, "stopEvidence"),
            ("wrong-project-wrapper", root => root["traces"]!.AsArray().First(item => (string?)item!["scenarioId"] == "linked-file-09")!["calls"]![0]!["selectedResultFields"]!["result"]!["project"] = new JsonObject { ["wrong"] = "shape" }, "stopEvidence"),
            ("wrong-zero-count", root => root["traces"]!.AsArray().First(item => (string?)item!["scenarioId"] == "generated-code-avoidance-10")!["calls"]![0]!["selectedResultFields"]!["result"]!["candidateCount"] = 1, "stopEvidence"),
            ("missing-workspace-loaded-true", root => root["traces"]!.AsArray().First(item => (string?)item!["scenarioId"] == "missing-workspace-34")!["calls"]![0]!["selectedResultFields"]!["result"]!["workspace"]!["loaded"] = true, "stopEvidence"),
            ("removed-tool", root => root["traces"]![0]!["calls"]![0]!["name"] = retiredName, "validActionNames"),
            ("cli-command-as-mcp", root => root["traces"]![0]!["calls"]![0]!["name"] = "route-map", "validActionNames"),
            ("edit-before-anchor", root => { JsonObject trace = root["traces"]!.AsArray().First(item => (string?)item!["scenarioId"] == "pre-edit-11")!.AsObject(); trace["editTiming"]!["anchorBeforeEdit"] = false; trace["anchorState"] = "missing"; }, "identityCriticalEditAnchoredBeforeEdit"),
            ("non-pre-edit-without-anchor", root => { JsonObject trace = root["traces"]!.AsArray().First(item => (string?)item!["scenarioId"] == "exact-position-02")!.AsObject(); trace["editTiming"]!["editAttempted"] = true; trace["editTiming"]!["anchorBeforeEdit"] = false; }, "identityCriticalEditAnchoredBeforeEdit"),
            ("ambiguous-edit", root => { JsonObject trace = root["traces"]!.AsArray().First(item => (string?)item!["scenarioId"] == "ambiguous-symbol-identity-01")!.AsObject(); trace["editTiming"]!["editAttempted"] = true; }, "ambiguousTargetFollowedByEdit"),
            ("stale-candidate-edit", root => { JsonObject trace = root["traces"]!.AsArray().First(item => (string?)item!["scenarioId"] == "stale-candidate-32")!.AsObject(); trace["editTiming"]!["editAttempted"] = true; }, "staleCandidateSilentReuse"),
            ("case-insensitive-unsupported-claim", root => root["traces"]![0]!["claims"]!.AsArray().Add("RUNTIME BEHAVIOR is guaranteed"), "unsupportedClaims")
        };

        foreach (var mutation in mutations)
        {
            string input = Path.Combine(Path.GetTempPath(), "navlyn-mcp-trace-" + Guid.NewGuid().ToString("N") + ".json");
            string output = Path.Combine(Path.GetTempPath(), "navlyn-mcp-report-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                JsonObject changed = (JsonObject)original.DeepClone();
                mutation.Apply(changed);
                await File.WriteAllTextAsync(input, changed.ToJsonString());
                using Process process = StartMcpAgentTraceScorer(repoRoot, input, scenarioPath, output);
                string stdout = await process.StandardOutput.ReadToEndAsync();
                string stderr = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                Assert.NotEqual(0, process.ExitCode);
                using JsonDocument report = JsonDocument.Parse(await File.ReadAllTextAsync(output));
                JsonElement result = report.RootElement.GetProperty("results").EnumerateArray()
                    .First(item => item.GetProperty("criteria").TryGetProperty(mutation.Criterion, out JsonElement value) && !value.GetBoolean());
                Assert.False(result.GetProperty("criteria").GetProperty(mutation.Criterion).GetBoolean(), $"{mutation.Name}: {stdout} {stderr}");
            }
            finally
            {
                File.Delete(input);
                File.Delete(output);
            }
        }

        var malformedMutations = new (string Name, Action<JsonObject> Apply, string ErrorFragment)[]
        {
            ("skill-activation-string", root => root["traces"]![0]!["skillActivated"] = "false", "Malformed required v2 replay trace fields"),
            ("semantic-check-string", root => root["traces"]!.AsArray().First(item => (string?)item!["scenarioId"] == "ambiguous-symbol-identity-01")!["semanticChecks"]!["ambiguityReported"] = "false", "Semantic checks must be JSON booleans"),
            ("output-valid-string", root => root["traces"]![0]!["outputValid"] = "false", "Malformed required v2 replay trace fields"),
            ("stdout-number-string", root => root["traces"]![0]!["stdoutChars"] = "1500", "Malformed required v2 replay trace fields"),
            ("latency-number-string", root => root["traces"]![0]!["latencyMs"] = "500", "Malformed required v2 replay trace fields"),
            ("edit-boolean-string", root => root["traces"]![0]!["editTiming"]!["editAttempted"] = "false", "Malformed required v2 replay trace fields"),
            ("negative-measurement", root => root["traces"]![0]!["latencyMs"] = -1, "Malformed required v2 replay trace fields"),
            ("claim-not-string", root => root["traces"]![0]!["claims"] = new JsonArray(JsonValue.Create(1)), "Claims must be JSON strings"),
            ("environment-not-string", root => root["traces"]![0]!["environment"]!["workspaceFreshness"] = true, "Environment values must be JSON strings"),
            ("stop-reason-not-string", root => root["traces"]![0]!["stopReason"] = 1, "Malformed required v2 replay trace fields"),
            ("anchor-state-not-string", root => root["traces"]![0]!["anchorState"] = 1, "Malformed required v2 replay trace fields"),
            ("call-kind-not-string", root => root["traces"]![0]!["calls"]![0]!["kind"] = 1, "Malformed v2 replay call"),
            ("arguments-not-object", root => root["traces"]![0]!["calls"]![0]!["arguments"] = "not-an-object", "Malformed v2 replay call")
        };
        foreach (var mutation in malformedMutations)
        {
            string malformedInput = Path.Combine(Path.GetTempPath(), "navlyn-mcp-malformed-" + Guid.NewGuid().ToString("N") + ".json");
            string malformedOutput = Path.Combine(Path.GetTempPath(), "navlyn-mcp-malformed-report-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                JsonObject malformed = (JsonObject)original.DeepClone();
                mutation.Apply(malformed);
                await File.WriteAllTextAsync(malformedInput, malformed.ToJsonString());
                using Process process = StartMcpAgentTraceScorer(repoRoot, malformedInput, scenarioPath, malformedOutput);
                _ = await process.StandardOutput.ReadToEndAsync();
                string stderr = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                Assert.NotEqual(0, process.ExitCode);
                Assert.True(stderr.Contains(mutation.ErrorFragment, StringComparison.Ordinal), $"{mutation.Name}: {stderr}");
                Assert.False(File.Exists(malformedOutput));
            }
            finally { File.Delete(malformedInput); File.Delete(malformedOutput); }
        }

        string invalidScenarioPath = Path.Combine(Path.GetTempPath(), "navlyn-mcp-invalid-scenario-" + Guid.NewGuid().ToString("N") + ".json");
        string invalidScenarioOutput = Path.Combine(Path.GetTempPath(), "navlyn-mcp-invalid-scenario-report-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            JsonObject invalidScenarioDoc = JsonNode.Parse(await File.ReadAllTextAsync(scenarioPath))!.AsObject();
            JsonObject exactScenario = invalidScenarioDoc["scenarios"]!.AsArray().First(item => (string?)item!["id"] == "exact-position-02")!.AsObject();
            exactScenario["expectedStopEvidence"]!["alternatives"]![0]!["predicates"]![0]!["unexpected"] = true;
            await File.WriteAllTextAsync(invalidScenarioPath, invalidScenarioDoc.ToJsonString());
            using Process process = StartMcpAgentTraceScorer(repoRoot, tracePath, invalidScenarioPath, invalidScenarioOutput);
            _ = await process.StandardOutput.ReadToEndAsync();
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.NotEqual(0, process.ExitCode);
            Assert.Contains("Unsupported stop predicate key", stderr);
            Assert.False(File.Exists(invalidScenarioOutput));
        }
        finally { File.Delete(invalidScenarioPath); File.Delete(invalidScenarioOutput); }

        string duplicateInput = Path.Combine(Path.GetTempPath(), "navlyn-mcp-duplicate-" + Guid.NewGuid().ToString("N") + ".json");
        string duplicateOutput = Path.Combine(Path.GetTempPath(), "navlyn-mcp-duplicate-report-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            JsonObject duplicate = (JsonObject)original.DeepClone();
            duplicate["traces"]![1]!["scenarioId"] = duplicate["traces"]![0]!["scenarioId"]!.GetValue<string>();
            await File.WriteAllTextAsync(duplicateInput, duplicate.ToJsonString());
            using Process process = StartMcpAgentTraceScorer(repoRoot, duplicateInput, scenarioPath, duplicateOutput);
            _ = await process.StandardOutput.ReadToEndAsync();
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.NotEqual(0, process.ExitCode);
            Assert.Contains("Duplicate trace scenarioId", stderr);
            Assert.False(File.Exists(duplicateOutput));
        }
        finally { File.Delete(duplicateInput); File.Delete(duplicateOutput); }

        string v1Input = Path.Combine(Path.GetTempPath(), "navlyn-mcp-v1-" + Guid.NewGuid().ToString("N") + ".json");
        string v1Output = Path.Combine(Path.GetTempPath(), "navlyn-mcp-v1-report-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            JsonObject v1 = (JsonObject)original.DeepClone();
            v1["schemaVersion"] = "navlyn.mcp-agent-trace-eval.v1";
            await File.WriteAllTextAsync(v1Input, v1.ToJsonString());
            using Process process = StartMcpAgentTraceScorer(repoRoot, v1Input, scenarioPath, v1Output);
            _ = await process.StandardOutput.ReadToEndAsync();
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.NotEqual(0, process.ExitCode);
            Assert.Contains("Unsupported MCP trace eval schemaVersion", stderr);
        }
        finally
        {
            File.Delete(v1Input);
            File.Delete(v1Output);
        }
    }

    private static Process StartMcpAgentTraceScorer(string repoRoot, string tracePath, string scenarioPath, string outputPath)
    {
        Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "pwsh", WorkingDirectory = repoRoot, RedirectStandardOutput = true,
                RedirectStandardError = true, UseShellExecute = false
            }
        };
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-File");
        process.StartInfo.ArgumentList.Add(Path.Combine(repoRoot, "scripts", "test-mcp-agent-trace-eval.ps1"));
        process.StartInfo.ArgumentList.Add("-TraceFile"); process.StartInfo.ArgumentList.Add(tracePath);
        process.StartInfo.ArgumentList.Add("-ScenarioFile"); process.StartInfo.ArgumentList.Add(scenarioPath);
        process.StartInfo.ArgumentList.Add("-Output"); process.StartInfo.ArgumentList.Add(outputPath);
        process.Start();
        return process;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "navlyn.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}

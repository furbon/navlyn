using System.Text.Json;
using System.Reflection;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Navlyn.Mcp.Configuration;
using Navlyn.Mcp.Tools;

namespace Navlyn.Tests.Mcp;

public sealed class NavlynMcpStdioTests
{
    [Fact]
    public async Task StdioServer_ListsToolsAndMapsSuccessAndCliErrors()
    {
        string repoRoot = FindRepositoryRoot();
        string serverDll = Path.Combine(repoRoot, "navlyn.Mcp", "bin", Directory.GetParent(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory))!.Name, GetCurrentTargetFramework(), "navlyn.Mcp.dll");
        Assert.True(File.Exists(serverDll), $"MCP server assembly does not exist: {serverDll}");

        // This test makes many independent calls; allow slower hosted runners to complete all assertions.
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(60));
        StdioClientTransport transport = new(
            new StdioClientTransportOptions
            {
                Command = "dotnet",
                Arguments =
                [
                    serverDll,
                    "--workspace", Path.Combine(repoRoot, "tests", "fixtures", "FuzzyDiscoveryFixture", "FuzzyDiscoveryFixture.csproj"),
                    "--working-directory", repoRoot,
                    "--timeout-ms", "60000",
                    "--max-json-chars", "4000000"
                ],
                WorkingDirectory = repoRoot
            },
            NullLoggerFactory.Instance);

        await using McpClient client = await McpClient.CreateAsync(
            transport,
            new McpClientOptions
            {
                ClientInfo = new Implementation
                {
                    Name = "navlyn-tests",
                    Version = "0.7.0"
                }
            },
            NullLoggerFactory.Instance,
            timeout.Token);

        IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        Assert.Contains(tools, tool => tool.Name == NavlynMcpTools.WorkspaceSummaryTool);
        Assert.Contains(tools, tool => tool.Name == NavlynMcpTools.TargetTool);
        Assert.Contains(tools, tool => tool.Name == NavlynMcpTools.ReadTool);
        Assert.Contains(tools, tool => tool.Name == NavlynMcpTools.PrepareEditTool);
        Assert.Contains(tools, tool => tool.Name == NavlynMcpTools.VerifyEditTool);
        Assert.Contains(tools, tool => tool.Name == NavlynMcpTools.ReviewTool);
        Assert.Contains(tools, tool => tool.Name == NavlynMcpTools.WorkspaceStatusTool);
        Assert.Contains(tools, tool => tool.Name == NavlynMcpTools.WorkspaceRefreshTool);
        Assert.Contains(tools, tool => tool.Name == NavlynMcpTools.DoctorTool);
        Assert.Contains(tools, tool => tool.Name == NavlynMcpTools.FileOutlineTool);
        Assert.Contains(tools, tool => tool.Name == NavlynMcpTools.NavigateTool);
        Assert.Contains(tools, tool => tool.Name == NavlynMcpTools.BatchTool);
        Assert.Empty(RemovedToolNames.Intersect(tools.Select(tool => tool.Name), StringComparer.Ordinal));
        McpClientTool targetTool = Assert.Single(tools, tool => tool.Name == NavlynMcpTools.TargetTool);
        Assert.True(targetTool.JsonSchema.TryGetProperty("properties", out JsonElement targetProperties), targetTool.JsonSchema.ToString());
        Assert.True(
            targetProperties.TryGetProperty("mode", out _),
            $"Server assembly: {serverDll}; target schema: {targetTool.JsonSchema}");
        McpClientTool workspaceTool = Assert.Single(tools, tool => tool.Name == NavlynMcpTools.WorkspaceSummaryTool);
        Assert.NotNull(workspaceTool.ReturnJsonSchema);
        McpClientTool diagnosticsTool = Assert.Single(tools, tool => tool.Name == NavlynMcpTools.DiagnosticsTool);
        Assert.True(diagnosticsTool.JsonSchema.TryGetProperty("properties", out JsonElement diagnosticsProperties), diagnosticsTool.JsonSchema.ToString());
        foreach (string property in new[] { "mode", "project", "projects", "excludeGenerated", "severity", "severities", "limit", "diagnosticId", "diagnosticIds", "candidateId", "file", "line", "column" })
        {
            Assert.True(diagnosticsProperties.TryGetProperty(property, out _), $"Missing diagnostics schema property {property}: {diagnosticsTool.JsonSchema}");
        }

        Assert.True(diagnosticsTool.JsonSchema.TryGetProperty("required", out JsonElement diagnosticsRequired), diagnosticsTool.JsonSchema.ToString());
        Assert.Contains(diagnosticsRequired.EnumerateArray(), property => property.GetString() == "mode");

        CallToolResult diagnosticsResult = await client.CallToolAsync(
            NavlynMcpTools.DiagnosticsTool,
            new Dictionary<string, object?> { ["mode"] = "workspace" },
            cancellationToken: timeout.Token);
        Assert.False(diagnosticsResult.IsError, diagnosticsResult.StructuredContent?.ToString());
        JsonElement diagnosticsStructured = diagnosticsResult.StructuredContent!.Value;
        Assert.True(diagnosticsStructured.GetProperty("ok").GetBoolean());
        Assert.Equal(NavlynMcpTools.DiagnosticsTool, diagnosticsStructured.GetProperty("tool").GetString());
        Assert.Equal("diagnostics", diagnosticsStructured.GetProperty("sourceCommand").GetProperty("command").GetString());
        Assert.True(diagnosticsStructured.GetProperty("result").GetProperty("totalDiagnostics").GetInt32() >= 0);

        McpClientTool diTool = Assert.Single(tools, tool => tool.Name == NavlynMcpTools.DiTool);
        Assert.True(diTool.JsonSchema.TryGetProperty("properties", out JsonElement diProperties), diTool.JsonSchema.ToString());
        foreach (string property in new[] { "mode", "query", "candidateId", "file", "line", "column", "assumeKind", "assumeKinds", "match", "caseSensitive", "candidatePolicy", "minConfidence", "explainSelection", "candidateLimit", "project", "projects", "excludeGenerated", "registrationLimit", "dependencyLimit", "riskLimit", "consumerLimit", "depth", "includeOptions", "includeHostedServices", "includeRisks", "includeSnippets", "snippetLines", "profile" })
        {
            Assert.True(diProperties.TryGetProperty(property, out _), $"Missing DI schema property {property}: {diTool.JsonSchema}");
        }

        Assert.True(diTool.JsonSchema.TryGetProperty("required", out JsonElement diRequired), diTool.JsonSchema.ToString());
        Assert.Contains(diRequired.EnumerateArray(), property => property.GetString() == "mode");

        CallToolResult diGraphResult = await client.CallToolAsync(
            NavlynMcpTools.DiTool,
            new Dictionary<string, object?> { ["mode"] = "graph" },
            cancellationToken: timeout.Token);
        Assert.False(diGraphResult.IsError, diGraphResult.StructuredContent?.ToString());
        JsonElement diGraphStructured = diGraphResult.StructuredContent!.Value;
        Assert.True(diGraphStructured.GetProperty("ok").GetBoolean());
        Assert.Equal(NavlynMcpTools.DiTool, diGraphStructured.GetProperty("tool").GetString());
        Assert.Equal("di-graph", diGraphStructured.GetProperty("sourceCommand").GetProperty("command").GetString());
        Assert.Equal("di-graph", diGraphStructured.GetProperty("result").GetProperty("command").GetString());
        Assert.Equal("compact", diGraphStructured.GetProperty("result").GetProperty("profile").GetString());

        McpClientTool routesTool = Assert.Single(tools, tool => tool.Name == NavlynMcpTools.RoutesTool);
        Assert.True(routesTool.JsonSchema.TryGetProperty("properties", out JsonElement routesProperties), routesTool.JsonSchema.ToString());
        foreach (string property in new[] { "mode", "route", "routes", "endpointKinds", "auth", "project", "projects", "excludeGenerated", "routeLimit", "evidenceLimit", "includeSnippets", "snippetLines", "profile" })
        {
            Assert.True(routesProperties.TryGetProperty(property, out _), $"Missing routes schema property {property}: {routesTool.JsonSchema}");
        }

        Assert.True(routesTool.JsonSchema.TryGetProperty("required", out JsonElement routesRequired), routesTool.JsonSchema.ToString());
        Assert.Contains(routesRequired.EnumerateArray(), property => property.GetString() == "mode");
        CallToolResult routesResult = await client.CallToolAsync(
            NavlynMcpTools.RoutesTool,
            new Dictionary<string, object?>
            {
                ["mode"] = "map"
            },
            cancellationToken: timeout.Token);
        Assert.False(routesResult.IsError, routesResult.StructuredContent?.ToString());
        JsonElement routesStructured = routesResult.StructuredContent!.Value;
        Assert.True(routesStructured.GetProperty("ok").GetBoolean());
        Assert.Equal(NavlynMcpTools.RoutesTool, routesStructured.GetProperty("tool").GetString());
        Assert.Equal("route-map", routesStructured.GetProperty("sourceCommand").GetProperty("command").GetString());
        Assert.Equal("route-map", routesStructured.GetProperty("result").GetProperty("command").GetString());
        Assert.Equal("compact", routesStructured.GetProperty("result").GetProperty("profile").GetString());

        McpClientTool optionsTool = Assert.Single(tools, tool => tool.Name == NavlynMcpTools.OptionsTool);
        Assert.True(optionsTool.JsonSchema.TryGetProperty("properties", out JsonElement optionsProperties), optionsTool.JsonSchema.ToString());
        foreach (string property in new[] { "mode", "query", "project", "projects", "excludeGenerated", "optionLimit", "consumerLimit", "bindingLimit", "evidenceLimit", "includeSnippets", "snippetLines", "profile" })
        {
            Assert.True(optionsProperties.TryGetProperty(property, out _), $"Missing options schema property {property}: {optionsTool.JsonSchema}");
        }

        Assert.True(optionsTool.JsonSchema.TryGetProperty("required", out JsonElement optionsRequired), optionsTool.JsonSchema.ToString());
        Assert.Contains(optionsRequired.EnumerateArray(), property => property.GetString() == "mode");
        CallToolResult optionsResult = await client.CallToolAsync(
            NavlynMcpTools.OptionsTool,
            new Dictionary<string, object?>
            {
                ["mode"] = "graph"
            },
            cancellationToken: timeout.Token);
        Assert.False(optionsResult.IsError, optionsResult.StructuredContent?.ToString());
        JsonElement optionsStructured = optionsResult.StructuredContent!.Value;
        Assert.True(optionsStructured.GetProperty("ok").GetBoolean());
        Assert.Equal(NavlynMcpTools.OptionsTool, optionsStructured.GetProperty("tool").GetString());
        Assert.Equal("options-graph", optionsStructured.GetProperty("sourceCommand").GetProperty("command").GetString());
        Assert.Equal("options-graph", optionsStructured.GetProperty("result").GetProperty("command").GetString());
        Assert.Equal("compact", optionsStructured.GetProperty("result").GetProperty("profile").GetString());

        McpClientTool messagesTool = Assert.Single(tools, tool => tool.Name == NavlynMcpTools.MessagesTool);
        Assert.True(messagesTool.JsonSchema.TryGetProperty("properties", out JsonElement messagesProperties), messagesTool.JsonSchema.ToString());
        foreach (string property in new[] { "mode", "query", "candidateId", "file", "line", "column", "assumeKind", "assumeKinds", "match", "caseSensitive", "candidatePolicy", "minConfidence", "explainSelection", "project", "projects", "excludeGenerated", "candidateLimit", "handlerLimit", "callSiteLimit", "evidenceLimit", "includeSnippets", "snippetLines", "profile" })
        {
            Assert.True(messagesProperties.TryGetProperty(property, out _), $"Missing messages schema property {property}: {messagesTool.JsonSchema}");
        }

        Assert.True(messagesTool.JsonSchema.TryGetProperty("required", out JsonElement messagesRequired), messagesTool.JsonSchema.ToString());
        Assert.Contains(messagesRequired.EnumerateArray(), property => property.GetString() == "mode");
        CallToolResult messagesResult = await client.CallToolAsync(
            NavlynMcpTools.MessagesTool,
            new Dictionary<string, object?>
            {
                ["mode"] = "handlers",
                ["query"] = "ApplicationDomainResolver",
                ["project"] = "FuzzyDiscoveryFixture",
                ["candidateLimit"] = 1,
                ["handlerLimit"] = 1,
                ["evidenceLimit"] = 1
            },
            cancellationToken: timeout.Token);
        Assert.False(messagesResult.IsError, messagesResult.StructuredContent?.ToString());
        JsonElement messagesStructured = messagesResult.StructuredContent!.Value;
        Assert.True(messagesStructured.GetProperty("ok").GetBoolean());
        Assert.Equal(NavlynMcpTools.MessagesTool, messagesStructured.GetProperty("tool").GetString());
        Assert.Equal("where-handled", messagesStructured.GetProperty("sourceCommand").GetProperty("command").GetString());
        Assert.Equal("where-handled", messagesStructured.GetProperty("result").GetProperty("command").GetString());
        Assert.Equal("compact", messagesStructured.GetProperty("result").GetProperty("profile").GetString());
        string[] messagesArguments = messagesStructured.GetProperty("sourceCommand").GetProperty("arguments").EnumerateArray().Select(argument => argument.GetString()!).ToArray();
        int candidateLimitIndex = Array.IndexOf(messagesArguments, "--candidate-limit");
        Assert.True(candidateLimitIndex >= 0, string.Join(" ", messagesArguments));
        Assert.Equal("1", messagesArguments[candidateLimitIndex + 1]);
        Assert.DoesNotContain("--limit", messagesArguments);

        McpClientTool efTool = Assert.Single(tools, tool => tool.Name == NavlynMcpTools.EfTool);
        Assert.True(efTool.JsonSchema.TryGetProperty("properties", out JsonElement efProperties), efTool.JsonSchema.ToString());
        foreach (string property in new[] { "mode", "entity", "dbcontext", "query", "candidateId", "file", "line", "column", "assumeKind", "assumeKinds", "match", "caseSensitive", "candidatePolicy", "minConfidence", "explainSelection", "project", "projects", "excludeGenerated", "candidateLimit", "entityLimit", "querySiteLimit", "evidenceLimit", "includeSnippets", "snippetLines", "profile" })
        {
            Assert.True(efProperties.TryGetProperty(property, out _), $"Missing EF schema property {property}: {efTool.JsonSchema}");
        }

        Assert.True(efTool.JsonSchema.TryGetProperty("required", out JsonElement efRequired), efTool.JsonSchema.ToString());
        Assert.Contains(efRequired.EnumerateArray(), property => property.GetString() == "mode");
        CallToolResult efResult = await client.CallToolAsync(
            NavlynMcpTools.EfTool,
            new Dictionary<string, object?>
            {
                ["mode"] = "model",
                ["project"] = "FuzzyDiscoveryFixture",
                ["entityLimit"] = 1,
                ["querySiteLimit"] = 1,
                ["evidenceLimit"] = 1
            },
            cancellationToken: timeout.Token);
        Assert.False(efResult.IsError, efResult.StructuredContent?.ToString());
        JsonElement efStructured = efResult.StructuredContent!.Value;
        Assert.True(efStructured.GetProperty("ok").GetBoolean());
        Assert.Equal(NavlynMcpTools.EfTool, efStructured.GetProperty("tool").GetString());
        Assert.Equal("ef-model", efStructured.GetProperty("sourceCommand").GetProperty("command").GetString());
        Assert.Equal("ef-model", efStructured.GetProperty("result").GetProperty("command").GetString());
        Assert.Equal("compact", efStructured.GetProperty("result").GetProperty("profile").GetString());

        McpClientTool packagesTool = Assert.Single(tools, tool => tool.Name == NavlynMcpTools.PackagesTool);
        Assert.True(packagesTool.JsonSchema.TryGetProperty("properties", out JsonElement packagesProperties), packagesTool.JsonSchema.ToString());
        foreach (string property in new[] { "mode", "package", "namespaces", "project", "projects", "includeTests", "excludeGenerated", "usageLimit", "referenceLimit", "profile" })
        {
            Assert.True(packagesProperties.TryGetProperty(property, out _), $"Missing packages schema property {property}: {packagesTool.JsonSchema}");
        }

        Assert.True(packagesTool.JsonSchema.TryGetProperty("required", out JsonElement packagesRequired), packagesTool.JsonSchema.ToString());
        Assert.Contains(packagesRequired.EnumerateArray(), property => property.GetString() == "mode");
        Assert.Contains(packagesRequired.EnumerateArray(), property => property.GetString() == "package");
        CallToolResult packagesResult = await client.CallToolAsync(
            NavlynMcpTools.PackagesTool,
            new Dictionary<string, object?>
            {
                ["mode"] = "usage",
                ["package"] = "Microsoft.CodeAnalysis",
                ["project"] = "FuzzyDiscoveryFixture",
                ["includeTests"] = false,
                ["usageLimit"] = 1,
                ["referenceLimit"] = 1
            },
            cancellationToken: timeout.Token);
        Assert.False(packagesResult.IsError, packagesResult.StructuredContent?.ToString());
        JsonElement packagesStructured = packagesResult.StructuredContent!.Value;
        Assert.True(packagesStructured.GetProperty("ok").GetBoolean());
        Assert.Equal(NavlynMcpTools.PackagesTool, packagesStructured.GetProperty("tool").GetString());
        Assert.Equal("package-usage", packagesStructured.GetProperty("sourceCommand").GetProperty("command").GetString());
        Assert.Equal("package-usage", packagesStructured.GetProperty("result").GetProperty("command").GetString());
        Assert.Equal("compact", packagesStructured.GetProperty("result").GetProperty("profile").GetString());
        string[] packageArguments = packagesStructured.GetProperty("sourceCommand").GetProperty("arguments").EnumerateArray().Select(argument => argument.GetString()!).ToArray();
        int includeTestsIndex = Array.IndexOf(packageArguments, "--include-tests");
        Assert.True(includeTestsIndex >= 0, string.Join(" ", packageArguments));
        Assert.Equal("false", packageArguments[includeTestsIndex + 1]);

        CallToolResult selectTargetResult = await client.CallToolAsync(
            NavlynMcpTools.TargetTool,
            new Dictionary<string, object?>
            {
                ["query"] = "EnemyManagerTools"
            },
            cancellationToken: timeout.Token);
        Assert.False(selectTargetResult.IsError, selectTargetResult.StructuredContent?.ToString());
        JsonElement selectTargetStructured = selectTargetResult.StructuredContent!.Value;
        Assert.Equal(NavlynMcpTools.TargetTool, selectTargetStructured.GetProperty("tool").GetString());
        Assert.Equal("target", selectTargetStructured.GetProperty("sourceCommand").GetProperty("command").GetString());
        Assert.Equal("target", selectTargetStructured.GetProperty("result").GetProperty("command").GetString());

        CallToolResult listTargetResult = await client.CallToolAsync(
            NavlynMcpTools.TargetTool,
            new Dictionary<string, object?>
            {
                ["mode"] = "list",
                ["query"] = "EnemyManagerTools"
            },
            cancellationToken: timeout.Token);
        Assert.False(listTargetResult.IsError, listTargetResult.StructuredContent?.ToString());
        JsonElement listTargetStructured = listTargetResult.StructuredContent!.Value;
        Assert.Equal(NavlynMcpTools.TargetTool, listTargetStructured.GetProperty("tool").GetString());
        Assert.Equal("find", listTargetStructured.GetProperty("sourceCommand").GetProperty("command").GetString());
        JsonElement listTargetResultPayload = listTargetStructured.GetProperty("result");
        Assert.True(listTargetResultPayload.GetProperty("candidateCount").GetInt32() > 0);
        Assert.False(listTargetResultPayload.TryGetProperty("selectedTarget", out _));

        string selectedCandidateId = listTargetResultPayload.GetProperty("candidates")[0]
            .GetProperty("candidateId")
            .GetString()!;
        (string Operation, string Command)[] navigateCases =
        [
            ("definition", "definition"),
            ("references", "references"),
            ("callers", "callers"),
            ("calls", "calls"),
            ("implementations", "implementations"),
            ("type_hierarchy", "type-hierarchy"),
            ("symbol_info", "symbol-info")
        ];
        foreach ((string operation, string command) in navigateCases)
        {
            CallToolResult navigateResult = await client.CallToolAsync(
                NavlynMcpTools.NavigateTool,
                new Dictionary<string, object?>
                {
                    ["operation"] = operation,
                    ["candidateId"] = selectedCandidateId,
                    ["limit"] = operation is "references" or "callers" or "calls" or "implementations" ? 5 : null
                },
                cancellationToken: timeout.Token);
            Assert.False(navigateResult.IsError, $"{operation}: {navigateResult.StructuredContent?.ToString()}");
            JsonElement navigateStructured = navigateResult.StructuredContent!.Value;
            Assert.Equal(NavlynMcpTools.NavigateTool, navigateStructured.GetProperty("tool").GetString());
            Assert.Equal(command, navigateStructured.GetProperty("sourceCommand").GetProperty("command").GetString());
        }

        IList<McpClientResource> resources = await client.ListResourcesAsync(cancellationToken: timeout.Token);
        Assert.Contains(resources, resource => resource.Uri == "navlyn://workspace/summary");

        IList<McpClientResourceTemplate> resourceTemplates = await client.ListResourceTemplatesAsync(cancellationToken: timeout.Token);
        Assert.Contains(resourceTemplates, resource => resource.UriTemplate == "navlyn://symbol/{candidateId}");
        Assert.Contains(resourceTemplates, resource => resource.Name == "navlyn_read_source");
        Assert.DoesNotContain(resourceTemplates, resource => resource.Name == "navlyn_symbol_source");

        IList<McpClientPrompt> prompts = await client.ListPromptsAsync(cancellationToken: timeout.Token);
        Assert.Contains(prompts, prompt => prompt.Name == "navlyn_understand_symbol");
        Assert.Contains(prompts, prompt => prompt.Name == "navlyn_prepare_edit");
        Assert.Contains(prompts, prompt => prompt.Name == "navlyn_review_changes");
        Assert.DoesNotContain(prompts, prompt => prompt.Name == "navlyn_review_diff");
        string[] discoveredNames = tools.Select(tool => tool.Name)
            .Concat(resources.Select(resource => resource.Name))
            .Concat(resourceTemplates.Select(resource => resource.Name))
            .Concat(prompts.Select(prompt => prompt.Name))
            .ToArray();
        Assert.DoesNotContain("navlyn_review_diff", discoveredNames);
        Assert.DoesNotContain("navlyn_symbol_source", discoveredNames);

        CallToolResult result = await client.CallToolAsync(
            NavlynMcpTools.WorkspaceSummaryTool,
            new Dictionary<string, object?>
            {
                ["project"] = "FuzzyDiscoveryFixture",
                ["relationshipLimit"] = 20
            },
            cancellationToken: timeout.Token);

        Assert.False(result.IsError, result.StructuredContent?.ToString());
        Assert.NotNull(result.StructuredContent);
        JsonElement structured = result.StructuredContent.Value;
        Assert.True(structured.GetProperty("ok").GetBoolean());
        Assert.Equal(NavlynMcpTools.WorkspaceSummaryTool, structured.GetProperty("tool").GetString());
        Assert.Equal("direct", structured.GetProperty("metadata").GetProperty("executionPath").GetString());
        Assert.True(structured.GetProperty("metadata").GetProperty("workspaceCacheHit").ValueKind is JsonValueKind.True or JsonValueKind.False);
        Assert.Equal("fresh", structured.GetProperty("metadata").GetProperty("freshnessStatus").GetString());
        Assert.True(structured.GetProperty("metadata").GetProperty("documentIndexDocumentCount").GetInt32() > 0);
        Assert.Equal("repo-graph", structured.GetProperty("sourceCommand").GetProperty("command").GetString());
        Assert.Equal("repo-graph", structured.GetProperty("result").GetProperty("command").GetString());

        CallToolResult statusResult = await client.CallToolAsync(
            NavlynMcpTools.WorkspaceStatusTool,
            new Dictionary<string, object?>
            {
                ["cache"] = "off"
            },
            cancellationToken: timeout.Token);

        Assert.False(statusResult.IsError, statusResult.StructuredContent?.ToString());
        JsonElement statusStructured = statusResult.StructuredContent!.Value;
        Assert.True(statusStructured.GetProperty("ok").GetBoolean());
        Assert.Equal("direct", statusStructured.GetProperty("metadata").GetProperty("executionPath").GetString());
        Assert.True(statusStructured.GetProperty("metadata").GetProperty("workspaceCacheHit").GetBoolean());
        Assert.Equal("workspace-status", statusStructured.GetProperty("result").GetProperty("command").GetString());
        Assert.Equal("disabled", statusStructured.GetProperty("result").GetProperty("cache").GetProperty("status").GetString());

        CallToolResult outlineResult = await client.CallToolAsync(
            NavlynMcpTools.FileOutlineTool,
            new Dictionary<string, object?>
            {
                ["file"] = "FixtureCode.cs"
            },
            cancellationToken: timeout.Token);

        Assert.False(outlineResult.IsError, outlineResult.StructuredContent?.ToString());
        JsonElement outlineStructured = outlineResult.StructuredContent!.Value;
        Assert.True(outlineStructured.GetProperty("ok").GetBoolean());
        Assert.Equal("direct", outlineStructured.GetProperty("metadata").GetProperty("executionPath").GetString());
        Assert.True(outlineStructured.GetProperty("metadata").GetProperty("workspaceCacheHit").GetBoolean());
        Assert.Equal("warm", outlineStructured.GetProperty("metadata").GetProperty("indexStatus").GetString());
        Assert.Equal("cheap-file-first", outlineStructured.GetProperty("metadata").GetProperty("costClass").GetString());
        Assert.NotEqual(
            outlineStructured.GetProperty("metadata").GetProperty("workspaceFingerprint").GetString(),
            outlineStructured.GetProperty("metadata").GetProperty("snapshotId").GetString());
        JsonElement outlineEntry = outlineStructured
            .GetProperty("result")
            .GetProperty("entries")
            .EnumerateArray()
            .First(entry => entry.GetProperty("name").GetString() == "EnemyManagerTools");
        string candidateId = outlineEntry.GetProperty("candidateId").GetString()!;
        Assert.StartsWith("sym:v1:", candidateId, StringComparison.Ordinal);

        CallToolResult sourceResult = await client.CallToolAsync(
            NavlynMcpTools.ReadTool,
            new Dictionary<string, object?>
            {
                ["candidateId"] = candidateId,
                ["view"] = "declaration"
            },
            cancellationToken: timeout.Token);

        Assert.False(sourceResult.IsError, sourceResult.StructuredContent?.ToString());
        JsonElement sourceStructured = sourceResult.StructuredContent!.Value;
        Assert.True(sourceStructured.GetProperty("ok").GetBoolean());
        Assert.Equal("read", sourceStructured.GetProperty("sourceCommand").GetProperty("command").GetString());
        Assert.Equal("direct", sourceStructured.GetProperty("metadata").GetProperty("executionPath").GetString());
        Assert.True(sourceStructured.GetProperty("metadata").GetProperty("workspaceCacheHit").GetBoolean());
        Assert.Equal("cheap-file-first", sourceStructured.GetProperty("metadata").GetProperty("costClass").GetString());
        Assert.Equal("candidateId", sourceStructured.GetProperty("result").GetProperty("selectionInput").GetProperty("mode").GetString());

        ReadResourceResult sourceResourceResult = await client.ReadResourceAsync(
            $"navlyn://symbol/{candidateId}/source?view=declaration",
            cancellationToken: timeout.Token);
        TextResourceContents sourceResourceText = Assert.IsType<TextResourceContents>(Assert.Single(sourceResourceResult.Contents));
        using JsonDocument sourceResourceJson = JsonDocument.Parse(sourceResourceText.Text);
        Assert.True(sourceResourceJson.RootElement.GetProperty("ok").GetBoolean(), sourceResourceText.Text);
        Assert.Equal("read", sourceResourceJson.RootElement.GetProperty("sourceCommand").GetProperty("command").GetString());

        ReadResourceResult resourceResult = await client.ReadResourceAsync("navlyn://workspace/summary", cancellationToken: timeout.Token);
        Assert.NotEmpty(resourceResult.Contents);
        TextResourceContents resourceText = Assert.IsType<TextResourceContents>(resourceResult.Contents[0]);
        using JsonDocument resourceJson = JsonDocument.Parse(resourceText.Text);
        Assert.True(resourceJson.RootElement.GetProperty("ok").GetBoolean(), resourceText.Text);
        Assert.Equal("repo-graph", resourceJson.RootElement.GetProperty("result").GetProperty("command").GetString());

        ReadResourceResult fileResourceResult = await client.ReadResourceAsync("navlyn://file/README.md", cancellationToken: timeout.Token);
        TextResourceContents fileResourceText = Assert.IsType<TextResourceContents>(Assert.Single(fileResourceResult.Contents));
        Assert.Contains("navlyn_read", fileResourceText.Text, StringComparison.Ordinal);
        Assert.Contains("navlyn_navigate", fileResourceText.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("navlyn_exact_navigation", fileResourceText.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("navlyn_symbol_edges", fileResourceText.Text, StringComparison.Ordinal);

        GetPromptResult promptResult = await client.GetPromptAsync(
            "navlyn_prepare_edit",
            new Dictionary<string, object?>
            {
                ["query"] = "CheckCommand",
                ["changeKind"] = "behavior"
            },
            cancellationToken: timeout.Token);
        Assert.NotEmpty(promptResult.Messages);

        CallToolResult errorResult = await client.CallToolAsync(
            NavlynMcpTools.TargetTool,
            new Dictionary<string, object?>
            {
                ["mode"] = "list",
                ["query"] = "CheckCommand",
                ["candidateId"] = "sym:v1:00000000000000000000000000000000"
            },
            cancellationToken: timeout.Token);

        Assert.True(errorResult.IsError);
        Assert.NotNull(errorResult.StructuredContent);
        JsonElement errorStructured = errorResult.StructuredContent.Value;
        Assert.False(errorStructured.GetProperty("ok").GetBoolean());
        Assert.Equal(NavlynMcpTools.TargetTool, errorStructured.GetProperty("tool").GetString());
        Assert.Equal("NAVLYN_MCP_INVALID_ARGUMENT", errorStructured.GetProperty("error").GetProperty("code").GetString());
        string[] actualToolNames = tools.Select(tool => tool.Name).ToArray();
        Assert.True(
            ExpectedUnifiedTools.SequenceEqual(actualToolNames, StringComparer.Ordinal),
            $"Server assembly: {serverDll}; actual tools: {string.Join(", ", actualToolNames)}");
    }

    [Fact]
    public async Task StdioServer_BatchRequiresTwoRequestsAndPreservesCandidateIdFromChain()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(180));
        await using McpClient client = await CreateClientAsync(profile: null);

        IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        McpClientTool batchTool = Assert.Single(tools, tool => tool.Name == NavlynMcpTools.BatchTool);
        const string expectedDescription = "Advanced optimization for two or more already-selected, batch-supported facts from the same workspace. Prefer focused MCP tools for a single fact. Do not use batch for initial discovery or as a checklist.";
        Assert.Equal(expectedDescription, batchTool.Description);
        Assert.True(batchTool.JsonSchema.TryGetProperty("properties", out JsonElement batchProperties), batchTool.JsonSchema.ToString());
        Assert.True(batchProperties.GetProperty("defaults").TryGetProperty("description", out _), batchTool.JsonSchema.ToString());
        Assert.True(batchProperties.GetProperty("requests").TryGetProperty("description", out _), batchTool.JsonSchema.ToString());

        CallToolResult singleResult = await client.CallToolAsync(
            NavlynMcpTools.BatchTool,
            new Dictionary<string, object?>
            {
                ["requests"] = new object[]
                {
                    new Dictionary<string, object?>
                    {
                        ["id"] = "diagnostics",
                        ["command"] = "diagnostics",
                        ["mode"] = "workspace"
                    }
                }
            },
            cancellationToken: timeout.Token);
        Assert.True(singleResult.IsError, singleResult.StructuredContent?.ToString());
        JsonElement singleStructured = singleResult.StructuredContent!.Value;
        Assert.Equal("NAVLYN_MCP_INVALID_ARGUMENT", singleStructured.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains(NavlynMcpTools.DiagnosticsTool, singleStructured.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);

        CallToolResult chainedResult = await client.CallToolAsync(
            NavlynMcpTools.BatchTool,
            new Dictionary<string, object?>
            {
                ["requests"] = new object[]
                {
                    new Dictionary<string, object?>
                    {
                        ["id"] = "target",
                        ["command"] = "resolve-target",
                        ["query"] = "CheckCommand",
                        ["assumeKind"] = "NamedType",
                        ["project"] = "Navlyn.CommandLine(net10.0)"
                    },
                    new Dictionary<string, object?>
                    {
                        ["id"] = "source",
                        ["command"] = "symbol-source",
                        ["candidateIdFrom"] = "target",
                        ["view"] = "declaration"
                    }
                }
            },
            cancellationToken: timeout.Token);
        Assert.False(chainedResult.IsError, chainedResult.StructuredContent?.ToString());
        JsonElement chainedStructured = chainedResult.StructuredContent!.Value;
        Assert.True(chainedStructured.GetProperty("ok").GetBoolean());
        JsonElement batchResult = chainedStructured.GetProperty("result");
        Assert.Equal(2, batchResult.GetProperty("totalRequests").GetInt32());
        Assert.Equal(2, batchResult.GetProperty("succeededRequests").GetInt32());
        Assert.Equal(0, batchResult.GetProperty("failedRequests").GetInt32());
        JsonElement requestResults = batchResult.GetProperty("results");
        Assert.Equal("target", requestResults[0].GetProperty("id").GetString());
        Assert.True(requestResults[0].GetProperty("ok").GetBoolean());
        Assert.True(requestResults[0].GetProperty("result").TryGetProperty("candidateId", out _));
        Assert.Equal("source", requestResults[1].GetProperty("id").GetString());
        Assert.True(requestResults[1].GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task StdioServer_NaturalKindAliasesMatchAcrossSingleAndBatchCalls()
    {
        string workspace = Path.Combine(FindRepositoryRoot(), "tests", "fixtures", "FuzzyDiscoveryFixture", "FuzzyDiscoveryFixture.csproj");
        await using McpClient client = await CreateClientAsync(profile: null, workspace);
        foreach (KeyValuePair<string, object?> hint in new Dictionary<string, object?>
        {
            ["assumeKind"] = " InTeRfAcE ",
            ["assumeKinds"] = new[] { " class ", "RECORD", "NamedType", "class" }
        })
        {
            CallToolResult response = await client.CallToolAsync(NavlynMcpTools.TargetTool,
                new Dictionary<string, object?> { ["query"] = "EnemyManagerTools", [hint.Key] = hint.Value });
            Assert.False(response.IsError, response.StructuredContent?.ToString());
            JsonElement payload = response.StructuredContent!.Value.GetProperty("result");
            Assert.Contains("NamedType", payload.ToString(), StringComparison.Ordinal);
            Assert.Contains("Alpha.EnemyManagerTools", payload.ToString(), StringComparison.Ordinal);
        }

        foreach (KeyValuePair<string, object?> filter in new Dictionary<string, object?>
        {
            ["resultKind"] = " mEtHoD ",
            ["resultKinds"] = new[] { "Method", " method " }
        })
        {
            CallToolResult response = await client.CallToolAsync(NavlynMcpTools.NavigateTool,
                new Dictionary<string, object?> { ["operation"] = "calls", ["file"] = "FixtureCode.cs", ["line"] = 27, ["column"] = 9, [filter.Key] = filter.Value });
            Assert.False(response.IsError, response.StructuredContent?.ToString());
            JsonElement payload = response.StructuredContent!.Value.GetProperty("result");
            Assert.Contains("Alpha.EnemyManager.Spawn()", payload.ToString(), StringComparison.Ordinal);
            Assert.Equal("Method", Assert.Single(payload.GetProperty("resultKinds").EnumerateArray()).GetString());
        }

        CallToolResult batch = await client.CallToolAsync(NavlynMcpTools.BatchTool,
            new Dictionary<string, object?>
            {
                ["requests"] = new object[]
                {
                    new { id = "kinds", command = "symbols", query = "EnemyManagerTools", kinds = new[] { "interface", " CLASS " } },
                    new { id = "target", command = "resolve-target", query = "EnemyManagerTools", assumeKinds = new[] { "RECORD", "class" } },
                    new { id = "calls", command = "calls", file = "FixtureCode.cs", line = 27, column = 9, resultKinds = new[] { "METHOD", " method " } }
                }
            });
        Assert.False(batch.IsError, batch.StructuredContent?.ToString());
        JsonElement batchPayload = batch.StructuredContent!.Value.GetProperty("result");
        Assert.Equal(3, batchPayload.GetProperty("succeededRequests").GetInt32());
        Assert.Equal("NamedType", Assert.Single(batchPayload.GetProperty("results")[0].GetProperty("result").GetProperty("kinds").EnumerateArray()).GetString());

        foreach (string invalid in new[] { "1", "not-a-kind" })
        {
            CallToolResult response = await client.CallToolAsync(NavlynMcpTools.TargetTool,
                new Dictionary<string, object?> { ["query"] = "EnemyManagerTools", ["assumeKind"] = invalid });
            Assert.True(response.IsError, response.StructuredContent?.ToString());
            Assert.Contains("Unknown symbol kind", response.StructuredContent!.Value.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task StdioServer_VerifyEditExposesModesAndRejectsMixedIntentsBeforeExecution()
    {
        await using McpClient client = await CreateClientAsync(profile: null);

        IList<McpClientTool> tools = await client.ListToolsAsync();
        McpClientTool verifyEdit = Assert.Single(tools, tool => tool.Name == NavlynMcpTools.VerifyEditTool);
        Assert.True(verifyEdit.JsonSchema.TryGetProperty("properties", out JsonElement properties), verifyEdit.JsonSchema.ToString());
        foreach (string property in new[] { "query", "candidateId", "preflight", "file", "line", "column" })
        {
            Assert.True(properties.TryGetProperty(property, out _), $"Missing VerifyEdit schema property {property}: {verifyEdit.JsonSchema}");
        }

        CallToolResult result = await client.CallToolAsync(
            NavlynMcpTools.VerifyEditTool,
            new Dictionary<string, object?>
            {
                ["candidateId"] = "sym:v1:00000000000000000000000000000000",
                ["query"] = "Widget"
            });

        Assert.True(result.IsError, result.StructuredContent?.ToString());
        Assert.NotNull(result.StructuredContent);
        JsonElement structured = result.StructuredContent.Value;
        Assert.False(structured.GetProperty("ok").GetBoolean());
        Assert.Equal(NavlynMcpTools.VerifyEditTool, structured.GetProperty("tool").GetString());
        Assert.Equal("NAVLYN_MCP_INVALID_ARGUMENT", structured.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task StdioServer_ProfileGatedToolsAreCallableWithoutToolProfile()
    {
        await using McpClient client = await CreateClientAsync(profile: null);

        IList<McpClientTool> tools = await client.ListToolsAsync();
        Assert.Contains(tools, tool => tool.Name == NavlynMcpTools.VerifyEditTool);

        CallToolResult result = await client.CallToolAsync(
            NavlynMcpTools.VerifyEditTool,
            new Dictionary<string, object?>());

        Assert.True(result.IsError, result.StructuredContent?.ToString());
        Assert.NotNull(result.StructuredContent);
        JsonElement structured = result.StructuredContent.Value;
        Assert.False(structured.GetProperty("ok").GetBoolean());
        Assert.Equal(NavlynMcpTools.VerifyEditTool, structured.GetProperty("tool").GetString());
        Assert.Equal("NAVLYN_MCP_INVALID_ARGUMENT", structured.GetProperty("error").GetProperty("code").GetString());
    }

    [Theory]
    [MemberData(nameof(ProfileToolData))]
    public async Task StdioServer_ListsUnifiedToolsForDeprecatedProfileAlias(string? profile)
    {
        await using McpClient client = await CreateClientAsync(profile);

        IList<McpClientTool> tools = await client.ListToolsAsync();

        Assert.Equal(ExpectedUnifiedTools, tools.Select(tool => tool.Name));
    }

    [Fact]
    public async Task StdioServer_ToolDescriptionsKeepNeedTriggeredGuidance()
    {
        await using McpClient client = await CreateClientAsync(profile: null);

        IList<McpClientTool> tools = await client.ListToolsAsync();

        McpClientTool fileOutline = Assert.Single(tools, tool => tool.Name == NavlynMcpTools.FileOutlineTool);
        Assert.Contains("ordinary reading", fileOutline.Description, StringComparison.Ordinal);
        McpClientTool target = Assert.Single(tools, tool => tool.Name == NavlynMcpTools.TargetTool);
        Assert.Contains("Use mode select normally", target.Description, StringComparison.Ordinal);
        Assert.Contains("mode list only for explicit broader candidate discovery", target.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StdioServer_DirectSnapshotRefreshesAfterStableSourceAndProjectEdits()
    {
        string fixtureRoot = Path.Combine(Path.GetTempPath(), $"navlyn-mcp-freshness-{Guid.NewGuid():N}");
        Directory.CreateDirectory(fixtureRoot);
        string projectPath = Path.Combine(fixtureRoot, "Fixture.csproj");
        string sourcePath = Path.Combine(fixtureRoot, "Fixture.cs");
        string originalProject = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="Fixture.cs" />
              </ItemGroup>
            </Project>
            """;
        const string originalSource = "namespace Fixture; public sealed class Alpha { }\n#if ENABLE_BETA\npublic sealed class Beta { }\n#endif\n";
        await File.WriteAllTextAsync(projectPath, originalProject);
        await File.WriteAllTextAsync(sourcePath, originalSource);

        try
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(180));
            await using McpClient client = await CreateClientAsync(profile: null, projectPath);

            JsonElement initial = await CallOutlineAsync(client, timeout.Token);
            string initialSnapshotId = initial.GetProperty("metadata").GetProperty("snapshotId").GetString()!;
            Assert.Contains(initial.GetProperty("result").GetProperty("entries").EnumerateArray(), entry => entry.GetProperty("name").GetString() == "Alpha");
            string candidateId = initial.GetProperty("result").GetProperty("entries").EnumerateArray()
                .Single(entry => entry.GetProperty("name").GetString() == "Alpha")
                .GetProperty("candidateId").GetString()!;

            DateTime originalWriteTimeUtc = File.GetLastWriteTimeUtc(sourcePath);
            string changedSource = originalSource.Replace("Alpha", "Bravo", StringComparison.Ordinal);
            Assert.Equal(originalSource.Length, changedSource.Length);
            await File.WriteAllTextAsync(sourcePath, changedSource);
            File.SetLastWriteTimeUtc(sourcePath, originalWriteTimeUtc);

            JsonElement changed = await CallOutlineAsync(client, timeout.Token);
            Assert.Contains(changed.GetProperty("result").GetProperty("entries").EnumerateArray(), entry => entry.GetProperty("name").GetString() == "Bravo");
            Assert.DoesNotContain(changed.GetProperty("result").GetProperty("entries").EnumerateArray(), entry => entry.GetProperty("name").GetString() == "Alpha");
            Assert.NotEqual(initialSnapshotId, changed.GetProperty("metadata").GetProperty("snapshotId").GetString());
            Assert.Equal("fresh", changed.GetProperty("metadata").GetProperty("freshnessStatus").GetString());
            string survivingCandidateId = changed.GetProperty("result").GetProperty("entries").EnumerateArray()
                .Single(entry => entry.GetProperty("name").GetString() == "Bravo")
                .GetProperty("candidateId").GetString()!;

            CallToolResult target = await client.CallToolAsync(
                NavlynMcpTools.TargetTool,
                new Dictionary<string, object?> { ["mode"] = "list", ["query"] = "Bravo", ["assumeKind"] = "NamedType" },
                cancellationToken: timeout.Token);
            Assert.False(target.IsError, target.StructuredContent?.ToString());
            string adapterCandidateId = target.StructuredContent!.Value.GetProperty("result")
                .GetProperty("candidates")[0].GetProperty("candidateId").GetString()!;
            CallToolResult adapterFollowUp = await client.CallToolAsync(
                NavlynMcpTools.ReadTool,
                new Dictionary<string, object?> { ["candidateId"] = adapterCandidateId, ["view"] = "declaration" },
                cancellationToken: timeout.Token);
            Assert.False(adapterFollowUp.IsError, adapterFollowUp.StructuredContent?.ToString());
            Assert.Contains("Bravo", adapterFollowUp.StructuredContent!.Value.GetProperty("result").ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("Alpha", adapterFollowUp.StructuredContent!.Value.GetProperty("result").ToString(), StringComparison.Ordinal);

            ReadResourceResult summaryResource = await client.ReadResourceAsync("navlyn://workspace/summary", cancellationToken: timeout.Token);
            TextResourceContents summaryText = Assert.IsType<TextResourceContents>(Assert.Single(summaryResource.Contents));
            using JsonDocument summaryJson = JsonDocument.Parse(summaryText.Text);
            Assert.Equal(changed.GetProperty("metadata").GetProperty("snapshotId").GetString(),
                summaryJson.RootElement.GetProperty("metadata").GetProperty("snapshotId").GetString());

            CallToolResult staleCandidateResult = await client.CallToolAsync(
                NavlynMcpTools.ReadTool,
                new Dictionary<string, object?> { ["candidateId"] = candidateId, ["view"] = "declaration" },
                cancellationToken: timeout.Token);
            Assert.True(staleCandidateResult.IsError, staleCandidateResult.StructuredContent?.ToString());
            JsonElement staleEnvelope = staleCandidateResult.StructuredContent!.Value;
            Assert.False(staleEnvelope.GetProperty("ok").GetBoolean());
            Assert.Equal("NAVLYN1702", staleEnvelope.GetProperty("error").GetProperty("code").GetString());
            Assert.True(!staleEnvelope.TryGetProperty("result", out JsonElement staleResult) ||
                staleResult.ValueKind == JsonValueKind.Null);

            string projectWithBeta = originalProject.Replace(
                "<EnableDefaultCompileItems>false</EnableDefaultCompileItems>",
                "<EnableDefaultCompileItems>false</EnableDefaultCompileItems>\n    <DefineConstants>$(DefineConstants);ENABLE_BETA</DefineConstants>",
                StringComparison.Ordinal);
            await File.WriteAllTextAsync(projectPath, projectWithBeta);
            JsonElement afterProjectEdit = await CallOutlineAsync(client, timeout.Token);
            Assert.Contains(afterProjectEdit.GetProperty("result").GetProperty("entries").EnumerateArray(), entry => entry.GetProperty("name").GetString() == "Beta");
            Assert.NotEqual(changed.GetProperty("metadata").GetProperty("snapshotId").GetString(), afterProjectEdit.GetProperty("metadata").GetProperty("snapshotId").GetString());
            Assert.Equal("fresh", afterProjectEdit.GetProperty("metadata").GetProperty("freshnessStatus").GetString());

            CallToolResult survivingCandidateRead = await client.CallToolAsync(
                NavlynMcpTools.ReadTool,
                new Dictionary<string, object?> { ["candidateId"] = survivingCandidateId, ["view"] = "declaration" },
                cancellationToken: timeout.Token);
            Assert.False(survivingCandidateRead.IsError, survivingCandidateRead.StructuredContent?.ToString());
            Assert.Contains("Bravo", survivingCandidateRead.StructuredContent!.Value.GetProperty("result").ToString(), StringComparison.Ordinal);

            JsonElement freshCandidate = afterProjectEdit.GetProperty("result").GetProperty("entries").EnumerateArray()
                .Single(entry => entry.GetProperty("name").GetString() == "Bravo");
            string freshCandidateId = freshCandidate.GetProperty("candidateId").GetString()!;

            CallToolResult refreshResult = await client.CallToolAsync(
                NavlynMcpTools.WorkspaceRefreshTool,
                new Dictionary<string, object?>(),
                cancellationToken: timeout.Token);
            Assert.False(refreshResult.IsError, refreshResult.StructuredContent?.ToString());
            JsonElement refreshed = refreshResult.StructuredContent!.Value;
            Assert.True(refreshed.GetProperty("ok").GetBoolean(), refreshed.ToString());
            Assert.Equal(afterProjectEdit.GetProperty("metadata").GetProperty("snapshotId").GetString(), refreshed.GetProperty("metadata").GetProperty("snapshotId").GetString());
            Assert.False(refreshed.GetProperty("metadata").GetProperty("workspaceCacheHit").GetBoolean());

            CallToolResult freshCandidateRead = await client.CallToolAsync(
                NavlynMcpTools.ReadTool,
                new Dictionary<string, object?> { ["candidateId"] = freshCandidateId, ["view"] = "declaration" },
                cancellationToken: timeout.Token);
            Assert.False(freshCandidateRead.IsError, freshCandidateRead.StructuredContent?.ToString());
            Assert.Contains("Bravo", freshCandidateRead.StructuredContent!.Value.GetProperty("result").ToString(), StringComparison.Ordinal);

            ReadResourceResult sourceResource = await client.ReadResourceAsync(
                $"navlyn://symbol/{freshCandidateId}/source?view=declaration",
                cancellationToken: timeout.Token);
            TextResourceContents sourceText = Assert.IsType<TextResourceContents>(Assert.Single(sourceResource.Contents));
            using JsonDocument resourceJson = JsonDocument.Parse(sourceText.Text);
            Assert.True(resourceJson.RootElement.GetProperty("ok").GetBoolean(), sourceText.Text);
            Assert.Contains("Bravo", resourceJson.RootElement.GetProperty("result").ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("Alpha", resourceJson.RootElement.GetProperty("result").ToString(), StringComparison.Ordinal);
        }
        finally
        {
            TemporaryDirectoryCleanup.Delete(fixtureRoot);
        }
    }

    [Fact]
    public async Task StdioServer_DirectSnapshotTracksImplicitSourceAddRenameAndDeleteInOneProcess()
    {
        string fixtureRoot = Path.Combine(Path.GetTempPath(), $"navlyn-mcp-membership-{Guid.NewGuid():N}");
        Directory.CreateDirectory(fixtureRoot);
        string projectPath = Path.Combine(fixtureRoot, "Fixture.csproj");
        string originalPath = Path.Combine(fixtureRoot, "Fixture.cs");
        await File.WriteAllTextAsync(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(originalPath, "namespace Fixture; public sealed class Stable { }\n");

        try
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(180));
            await using McpClient client = await CreateClientAsync(profile: null, projectPath);
            await CallOutlineAsync(client, timeout.Token, "Fixture.cs");

            string addedPath = Path.Combine(fixtureRoot, "Added.cs");
            await File.WriteAllTextAsync(addedPath, "namespace Fixture; public sealed class AddedType { }\n");
            JsonElement added = await CallOutlineAsync(client, timeout.Token, "Added.cs");
            Assert.Contains(added.GetProperty("result").GetProperty("entries").EnumerateArray(), entry => entry.GetProperty("name").GetString() == "AddedType");

            string renamedPath = Path.Combine(fixtureRoot, "Renamed.cs");
            File.Move(addedPath, renamedPath);
            JsonElement renamed = await CallOutlineAsync(client, timeout.Token, "Renamed.cs");
            Assert.Contains(renamed.GetProperty("result").GetProperty("entries").EnumerateArray(), entry => entry.GetProperty("name").GetString() == "AddedType");

            File.Delete(renamedPath);
            CallToolResult deleted = await client.CallToolAsync(
                NavlynMcpTools.FileOutlineTool,
                new Dictionary<string, object?> { ["file"] = "Renamed.cs" },
                cancellationToken: timeout.Token);
            Assert.True(deleted.IsError, deleted.StructuredContent?.ToString());
        }
        finally
        {
            TemporaryDirectoryCleanup.Delete(fixtureRoot);
        }
    }

    [Fact]
    public async Task StdioServer_DirectSnapshotDetectsLinkedVisualBasicEditOutsideWorkspaceRoot()
    {
        string fixtureRoot = Path.Combine(Path.GetTempPath(), $"navlyn-mcp-linked-vb-{Guid.NewGuid():N}");
        string projectRoot = Path.Combine(fixtureRoot, "project");
        string linkedRoot = Path.Combine(fixtureRoot, "linked");
        Directory.CreateDirectory(projectRoot);
        Directory.CreateDirectory(linkedRoot);
        string projectPath = Path.Combine(projectRoot, "Fixture.vbproj");
        string sourcePath = Path.Combine(linkedRoot, "Shared.vb");
        await File.WriteAllTextAsync(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="..\linked\Shared.vb" Link="Shared.vb" />
              </ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(sourcePath, "Namespace Fixture\n    Public Class Alpha\n    End Class\nEnd Namespace\n");

        try
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(180));
            await using McpClient client = await CreateClientAsync(profile: null, projectPath);
            JsonElement initial = await CallOutlineAsync(client, timeout.Token, sourcePath);
            Assert.Contains(initial.GetProperty("result").GetProperty("entries").EnumerateArray(), entry => entry.GetProperty("name").GetString() == "Alpha");

            DateTime originalWriteTimeUtc = File.GetLastWriteTimeUtc(sourcePath);
            await File.WriteAllTextAsync(sourcePath, "Namespace Fixture\n    Public Class Bravo\n    End Class\nEnd Namespace\n");
            File.SetLastWriteTimeUtc(sourcePath, originalWriteTimeUtc);
            JsonElement edited = await CallOutlineAsync(client, timeout.Token, sourcePath);
            Assert.Contains(edited.GetProperty("result").GetProperty("entries").EnumerateArray(), entry => entry.GetProperty("name").GetString() == "Bravo");
            Assert.DoesNotContain(edited.GetProperty("result").GetProperty("entries").EnumerateArray(), entry => entry.GetProperty("name").GetString() == "Alpha");
            Assert.NotEqual(initial.GetProperty("metadata").GetProperty("snapshotId").GetString(),
                edited.GetProperty("metadata").GetProperty("snapshotId").GetString());
        }
        finally
        {
            TemporaryDirectoryCleanup.Delete(fixtureRoot);
        }
    }

    private static async Task<JsonElement> CallOutlineAsync(McpClient client, CancellationToken cancellationToken, string file = "Fixture.cs")
    {
        CallToolResult result = await client.CallToolAsync(
            NavlynMcpTools.FileOutlineTool,
            new Dictionary<string, object?> { ["file"] = file },
            cancellationToken: cancellationToken);
        Assert.False(result.IsError, result.StructuredContent?.ToString());
        JsonElement structured = result.StructuredContent!.Value;
        Assert.True(structured.GetProperty("ok").GetBoolean(), structured.ToString());
        Assert.Equal("direct", structured.GetProperty("metadata").GetProperty("executionPath").GetString());
        return structured;
    }

    public static IEnumerable<object?[]> ProfileToolData()
    {
        yield return [null];
        yield return ["reader"];
        yield return ["review"];
        yield return ["edit"];
        yield return ["full"];
    }

    private static async Task<McpClient> CreateClientAsync(string? profile)
    {
        string repoRoot = FindRepositoryRoot();
        return await CreateClientAsync(profile, Path.Combine(repoRoot, "navlyn.slnx"));
    }

    private static async Task<McpClient> CreateClientAsync(string? profile, string workspacePath)
    {
        string repoRoot = FindRepositoryRoot();
        string serverDll = Path.Combine(repoRoot, "navlyn.Mcp", "bin", Directory.GetParent(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory))!.Name, GetCurrentTargetFramework(), "navlyn.Mcp.dll");
        Assert.True(File.Exists(serverDll), $"MCP server assembly does not exist: {serverDll}");

        List<string> arguments =
        [
            serverDll,
            "--workspace", workspacePath,
            "--working-directory", Path.GetDirectoryName(workspacePath)!,
            "--timeout-ms", "60000",
            "--max-json-chars", "4000000"
        ];
        if (!string.IsNullOrWhiteSpace(profile))
        {
            arguments.Add("--tool-profile");
            arguments.Add(profile);
        }

        StdioClientTransport transport = new(
            new StdioClientTransportOptions
            {
                Command = "dotnet",
                Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(workspacePath)!
            },
            NullLoggerFactory.Instance);

        return await McpClient.CreateAsync(
            transport,
            new McpClientOptions
            {
                ClientInfo = new Implementation
                {
                    Name = "navlyn-tests",
                    Version = "0.7.0"
                }
            },
            NullLoggerFactory.Instance);
    }

    private static readonly string[] ExpectedUnifiedTools =
    [
        NavlynMcpTools.TargetTool,
        NavlynMcpTools.ReadTool,
        NavlynMcpTools.FileOutlineTool,
        NavlynMcpTools.NavigateTool,
        NavlynMcpTools.PrepareEditTool,
        NavlynMcpTools.VerifyEditTool,
        NavlynMcpTools.ReviewTool,
        NavlynMcpTools.WorkspaceSummaryTool,
        NavlynMcpTools.WorkspaceStatusTool,
        NavlynMcpTools.WorkspaceRefreshTool,
        NavlynMcpTools.DoctorTool,
        NavlynMcpTools.ImpactTool,
        NavlynMcpTools.ContextPackTool,
        NavlynMcpTools.EntrypointsTool,
        NavlynMcpTools.TestsForSymbolTool,
        NavlynMcpTools.TestsForDiffTool,
        NavlynMcpTools.DiagnosticsTool,
        NavlynMcpTools.DiTool,
        NavlynMcpTools.PublicApiDiffTool,
        NavlynMcpTools.RoutesTool,
        NavlynMcpTools.OptionsTool,
        NavlynMcpTools.MessagesTool,
        NavlynMcpTools.EfTool,
        NavlynMcpTools.PackagesTool,
        NavlynMcpTools.BatchTool
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

    private static string GetCurrentTargetFramework()
    {
        string? frameworkName = typeof(NavlynMcpStdioTests).Assembly
            .GetCustomAttribute<TargetFrameworkAttribute>()?
            .FrameworkName;

        return frameworkName switch
        {
            ".NETCoreApp,Version=v8.0" => "net8.0",
            ".NETCoreApp,Version=v10.0" => "net10.0",
            _ => throw new InvalidOperationException($"Unsupported test target framework: {frameworkName ?? "unknown"}.")
        };
    }
}

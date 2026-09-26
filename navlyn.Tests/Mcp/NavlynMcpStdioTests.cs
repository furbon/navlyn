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
        string serverDll = Path.Combine(repoRoot, "navlyn.Mcp", "bin", "Debug", GetCurrentTargetFramework(), "navlyn.Mcp.dll");
        Assert.True(File.Exists(serverDll), $"MCP server assembly does not exist: {serverDll}");

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(180));
        StdioClientTransport transport = new(
            new StdioClientTransportOptions
            {
                Command = "dotnet",
                Arguments =
                [
                    serverDll,
                    "--workspace", Path.Combine(repoRoot, "navlyn.slnx"),
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
                ["project"] = "Navlyn.Core(net10.0)",
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
                ["project"] = "Navlyn.Core(net10.0)",
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
                ["project"] = "Navlyn.Core(net10.0)",
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
                ["query"] = "OutlineCommand"
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
                ["query"] = "OutlineCommand"
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
                ["project"] = "navlyn(net10.0)",
                ["relationshipLimit"] = 20
            },
            cancellationToken: timeout.Token);

        Assert.False(result.IsError, result.StructuredContent?.ToString());
        Assert.NotNull(result.StructuredContent);
        JsonElement structured = result.StructuredContent.Value;
        Assert.True(structured.GetProperty("ok").GetBoolean());
        Assert.Equal(NavlynMcpTools.WorkspaceSummaryTool, structured.GetProperty("tool").GetString());
        Assert.Equal("direct", structured.GetProperty("metadata").GetProperty("executionPath").GetString());
        Assert.False(structured.GetProperty("metadata").GetProperty("workspaceCacheHit").GetBoolean());
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
                ["file"] = "Navlyn.CommandLine/Cli/Commands/OutlineCommand.cs"
            },
            cancellationToken: timeout.Token);

        Assert.False(outlineResult.IsError, outlineResult.StructuredContent?.ToString());
        JsonElement outlineStructured = outlineResult.StructuredContent!.Value;
        Assert.True(outlineStructured.GetProperty("ok").GetBoolean());
        Assert.Equal("direct", outlineStructured.GetProperty("metadata").GetProperty("executionPath").GetString());
        Assert.True(outlineStructured.GetProperty("metadata").GetProperty("workspaceCacheHit").GetBoolean());
        Assert.Equal("warm", outlineStructured.GetProperty("metadata").GetProperty("indexStatus").GetString());
        Assert.Equal("cheap-file-first", outlineStructured.GetProperty("metadata").GetProperty("costClass").GetString());
        Assert.Equal(
            outlineStructured.GetProperty("metadata").GetProperty("workspaceFingerprint").GetString(),
            outlineStructured.GetProperty("metadata").GetProperty("snapshotId").GetString());
        JsonElement outlineEntry = outlineStructured
            .GetProperty("result")
            .GetProperty("entries")
            .EnumerateArray()
            .First(entry => entry.GetProperty("name").GetString() == "OutlineCommand");
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
        string serverDll = Path.Combine(repoRoot, "navlyn.Mcp", "bin", "Debug", GetCurrentTargetFramework(), "navlyn.Mcp.dll");
        Assert.True(File.Exists(serverDll), $"MCP server assembly does not exist: {serverDll}");

        List<string> arguments =
        [
            serverDll,
            "--workspace", Path.Combine(repoRoot, "navlyn.slnx"),
            "--working-directory", repoRoot,
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
                WorkingDirectory = repoRoot
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

using System.Text.Json;
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

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(90));
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
                    "--max-json-chars", "4000000",
                    "--tool-profile", "full"
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
                    Version = "0.6.0"
                }
            },
            NullLoggerFactory.Instance,
            timeout.Token);

        IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        Assert.Contains(tools, tool => tool.Name == NavlynMcpTools.WorkspaceSummaryTool);
        Assert.Contains(tools, tool => tool.Name == NavlynMcpTools.WorkspaceStatusTool);
        Assert.Contains(tools, tool => tool.Name == NavlynMcpTools.WorkspaceRefreshTool);
        Assert.Contains(tools, tool => tool.Name == NavlynMcpTools.ResolveTargetTool);
        Assert.Contains(tools, tool => tool.Name == NavlynMcpTools.FindSymbolTool);
        Assert.Contains(tools, tool => tool.Name == NavlynMcpTools.FileOutlineTool);
        Assert.Contains(tools, tool => tool.Name == NavlynMcpTools.SymbolSourceTool);
        Assert.Contains(tools, tool => tool.Name == NavlynMcpTools.SymbolEdgesTool);
        Assert.Contains(tools, tool => tool.Name == NavlynMcpTools.InspectFileTool);
        Assert.Contains(tools, tool => tool.Name == NavlynMcpTools.ExactNavigationTool);
        Assert.Contains(tools, tool => tool.Name == NavlynMcpTools.BatchTool);
        Assert.Equal(ExpectedFullTools, tools.Select(tool => tool.Name));
        McpClientTool workspaceTool = Assert.Single(tools, tool => tool.Name == NavlynMcpTools.WorkspaceSummaryTool);
        Assert.NotNull(workspaceTool.ReturnJsonSchema);

        IList<McpClientResource> resources = await client.ListResourcesAsync(cancellationToken: timeout.Token);
        Assert.Contains(resources, resource => resource.Uri == "navlyn://workspace/summary");

        IList<McpClientResourceTemplate> resourceTemplates = await client.ListResourceTemplatesAsync(cancellationToken: timeout.Token);
        Assert.Contains(resourceTemplates, resource => resource.UriTemplate == "navlyn://symbol/{candidateId}");

        IList<McpClientPrompt> prompts = await client.ListPromptsAsync(cancellationToken: timeout.Token);
        Assert.Contains(prompts, prompt => prompt.Name == "navlyn_understand_symbol");
        Assert.Contains(prompts, prompt => prompt.Name == "navlyn_prepare_edit");

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
            NavlynMcpTools.SymbolSourceTool,
            new Dictionary<string, object?>
            {
                ["candidateId"] = candidateId,
                ["view"] = "declaration"
            },
            cancellationToken: timeout.Token);

        Assert.False(sourceResult.IsError, sourceResult.StructuredContent?.ToString());
        JsonElement sourceStructured = sourceResult.StructuredContent!.Value;
        Assert.True(sourceStructured.GetProperty("ok").GetBoolean());
        Assert.Equal("direct", sourceStructured.GetProperty("metadata").GetProperty("executionPath").GetString());
        Assert.True(sourceStructured.GetProperty("metadata").GetProperty("workspaceCacheHit").GetBoolean());
        Assert.Equal("candidateId", sourceStructured.GetProperty("result").GetProperty("selectionInput").GetProperty("mode").GetString());

        ReadResourceResult resourceResult = await client.ReadResourceAsync("navlyn://workspace/summary", cancellationToken: timeout.Token);
        Assert.NotEmpty(resourceResult.Contents);
        TextResourceContents resourceText = Assert.IsType<TextResourceContents>(resourceResult.Contents[0]);
        using JsonDocument resourceJson = JsonDocument.Parse(resourceText.Text);
        Assert.True(resourceJson.RootElement.GetProperty("ok").GetBoolean(), resourceText.Text);
        Assert.Equal("repo-graph", resourceJson.RootElement.GetProperty("result").GetProperty("command").GetString());

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
            NavlynMcpTools.FindSymbolTool,
            new Dictionary<string, object?>
            {
                ["query"] = "CheckCommand",
                ["assumeKind"] = "NotAKind"
            },
            cancellationToken: timeout.Token);

        Assert.True(errorResult.IsError);
        Assert.NotNull(errorResult.StructuredContent);
        JsonElement errorStructured = errorResult.StructuredContent.Value;
        Assert.False(errorStructured.GetProperty("ok").GetBoolean());
        Assert.Equal(NavlynMcpTools.FindSymbolTool, errorStructured.GetProperty("tool").GetString());
        Assert.Equal("NAVLYN1004", errorStructured.GetProperty("error").GetProperty("code").GetString());
    }

    [Theory]
    [MemberData(nameof(ProfileToolData))]
    public async Task StdioServer_ListsToolsForProfileInDeterministicOrder(string profile, string[] expectedTools)
    {
        await using McpClient client = await CreateClientAsync(profile);

        IList<McpClientTool> tools = await client.ListToolsAsync();

        Assert.Equal(expectedTools, tools.Select(tool => tool.Name));
    }

    [Fact]
    public async Task StdioServer_BlocksCallsOutsideSelectedToolProfile()
    {
        await using McpClient client = await CreateClientAsync("reader");

        CallToolResult result = await client.CallToolAsync(
            NavlynMcpTools.BatchTool,
            new Dictionary<string, object?>());

        Assert.True(result.IsError);
        Assert.NotNull(result.StructuredContent);
        JsonElement structured = result.StructuredContent.Value;
        Assert.False(structured.GetProperty("ok").GetBoolean());
        Assert.Equal(NavlynMcpTools.BatchTool, structured.GetProperty("tool").GetString());
        Assert.Equal("NAVLYN_MCP_TOOL_PROFILE", structured.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task StdioServer_ReaderToolDescriptionsKeepNeedTriggeredGuidance()
    {
        await using McpClient client = await CreateClientAsync("reader");

        IList<McpClientTool> tools = await client.ListToolsAsync();

        McpClientTool fileOutline = Assert.Single(tools, tool => tool.Name == NavlynMcpTools.FileOutlineTool);
        Assert.Contains("Do not use for tests", fileOutline.Description, StringComparison.Ordinal);
        McpClientTool resolveTarget = Assert.Single(tools, tool => tool.Name == NavlynMcpTools.ResolveTargetTool);
        Assert.Contains("standard first symbol entry", resolveTarget.Description, StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> ProfileToolData()
    {
        yield return ["reader", ExpectedReaderTools];
        yield return ["review", ExpectedReviewTools];
        yield return ["edit", ExpectedEditTools];
        yield return ["full", ExpectedFullTools];
    }

    private static async Task<McpClient> CreateClientAsync(string profile)
    {
        string repoRoot = FindRepositoryRoot();
        string serverDll = Path.Combine(repoRoot, "navlyn.Mcp", "bin", "Debug", GetCurrentTargetFramework(), "navlyn.Mcp.dll");
        Assert.True(File.Exists(serverDll), $"MCP server assembly does not exist: {serverDll}");

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
                    "--max-json-chars", "4000000",
                    "--tool-profile", profile
                ],
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
                    Version = "0.6.0"
                }
            },
            NullLoggerFactory.Instance);
    }

    private static readonly string[] ExpectedReaderTools =
    [
        NavlynMcpTools.WorkspaceSummaryTool,
        NavlynMcpTools.WorkspaceStatusTool,
        NavlynMcpTools.WorkspaceRefreshTool,
        NavlynMcpTools.ResolveTargetTool,
        NavlynMcpTools.FindSymbolTool,
        NavlynMcpTools.FileOutlineTool,
        NavlynMcpTools.InspectFileTool,
        NavlynMcpTools.SymbolSourceTool,
        NavlynMcpTools.SymbolEdgesTool,
        NavlynMcpTools.AboutSymbolTool,
        NavlynMcpTools.RelatedFilesTool,
        NavlynMcpTools.ExactNavigationTool
    ];

    private static readonly string[] ExpectedReviewTools =
    [
        NavlynMcpTools.WorkspaceSummaryTool,
        NavlynMcpTools.WorkspaceStatusTool,
        NavlynMcpTools.WorkspaceRefreshTool,
        NavlynMcpTools.ResolveTargetTool,
        NavlynMcpTools.FindSymbolTool,
        NavlynMcpTools.FileOutlineTool,
        NavlynMcpTools.InspectFileTool,
        NavlynMcpTools.SymbolSourceTool,
        NavlynMcpTools.SymbolEdgesTool,
        NavlynMcpTools.AboutSymbolTool,
        NavlynMcpTools.RelatedFilesTool,
        NavlynMcpTools.ImpactTool,
        NavlynMcpTools.EntrypointsTool,
        NavlynMcpTools.ExactNavigationTool,
        NavlynMcpTools.TestsForDiffTool,
        NavlynMcpTools.PublicApiDiffTool,
        NavlynMcpTools.ReviewDiffTool,
        NavlynMcpTools.ContextPackTool
    ];

    private static readonly string[] ExpectedEditTools =
    [
        NavlynMcpTools.WorkspaceSummaryTool,
        NavlynMcpTools.WorkspaceStatusTool,
        NavlynMcpTools.WorkspaceRefreshTool,
        NavlynMcpTools.ResolveTargetTool,
        NavlynMcpTools.FindSymbolTool,
        NavlynMcpTools.FileOutlineTool,
        NavlynMcpTools.InspectFileTool,
        NavlynMcpTools.SymbolSourceTool,
        NavlynMcpTools.SymbolEdgesTool,
        NavlynMcpTools.AboutSymbolTool,
        NavlynMcpTools.RelatedFilesTool,
        NavlynMcpTools.ImpactTool,
        NavlynMcpTools.EntrypointsTool,
        NavlynMcpTools.ExactNavigationTool,
        NavlynMcpTools.TestsForSymbolTool,
        NavlynMcpTools.DiImpactTool,
        NavlynMcpTools.ContextPackTool
    ];

    private static readonly string[] ExpectedFullTools =
    [
        NavlynMcpTools.WorkspaceSummaryTool,
        NavlynMcpTools.WorkspaceStatusTool,
        NavlynMcpTools.WorkspaceRefreshTool,
        NavlynMcpTools.ResolveTargetTool,
        NavlynMcpTools.FindSymbolTool,
        NavlynMcpTools.FileOutlineTool,
        NavlynMcpTools.InspectFileTool,
        NavlynMcpTools.SymbolSourceTool,
        NavlynMcpTools.SymbolEdgesTool,
        NavlynMcpTools.AboutSymbolTool,
        NavlynMcpTools.RelatedFilesTool,
        NavlynMcpTools.ImpactTool,
        NavlynMcpTools.EntrypointsTool,
        NavlynMcpTools.ExactNavigationTool,
        NavlynMcpTools.TestsForSymbolTool,
        NavlynMcpTools.TestsForDiffTool,
        NavlynMcpTools.DiImpactTool,
        NavlynMcpTools.PublicApiDiffTool,
        NavlynMcpTools.ReviewDiffTool,
        NavlynMcpTools.ContextPackTool,
        NavlynMcpTools.BatchTool
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
        string? frameworkName = AppContext.TargetFrameworkName;
        if (frameworkName is not null && frameworkName.Contains("Version=v8.0", StringComparison.Ordinal))
        {
            return "net8.0";
        }

        return "net10.0";
    }
}

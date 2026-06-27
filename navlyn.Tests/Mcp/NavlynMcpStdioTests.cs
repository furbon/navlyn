using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Navlyn.Mcp.Tools;

namespace Navlyn.Tests.Mcp;

public sealed class NavlynMcpStdioTests
{
    [Fact]
    public async Task StdioServer_ListsToolsAndMapsSuccessAndCliErrors()
    {
        string repoRoot = FindRepositoryRoot();
        string serverDll = Path.Combine(repoRoot, "navlyn.Mcp", "bin", "Debug", "net10.0", "navlyn.Mcp.dll");
        string cliDll = Path.Combine(repoRoot, "navlyn", "bin", "Debug", "net10.0", "navlyn.dll");
        Assert.True(File.Exists(serverDll), $"MCP server assembly does not exist: {serverDll}");
        Assert.True(File.Exists(cliDll), $"Navlyn CLI assembly does not exist: {cliDll}");

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(60));
        StdioClientTransport transport = new(
            new StdioClientTransportOptions
            {
                Command = "dotnet",
                Arguments =
                [
                    serverDll,
                    "--workspace", Path.Combine(repoRoot, "navlyn.slnx"),
                    "--navlyn-executable", "dotnet",
                    "--navlyn-arg", cliDll,
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
                    Version = "0.1.0"
                }
            },
            NullLoggerFactory.Instance,
            timeout.Token);

        IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        Assert.Contains(tools, tool => tool.Name == NavlynMcpTools.WorkspaceSummaryTool);
        Assert.Contains(tools, tool => tool.Name == NavlynMcpTools.FindSymbolTool);
        Assert.Contains(tools, tool => tool.Name == NavlynMcpTools.ExactNavigationTool);
        Assert.Contains(tools, tool => tool.Name == NavlynMcpTools.BatchTool);
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
                ["project"] = "navlyn",
                ["relationshipLimit"] = 20
            },
            cancellationToken: timeout.Token);

        Assert.False(result.IsError);
        Assert.NotNull(result.StructuredContent);
        JsonElement structured = result.StructuredContent.Value;
        Assert.True(structured.GetProperty("ok").GetBoolean());
        Assert.Equal(NavlynMcpTools.WorkspaceSummaryTool, structured.GetProperty("tool").GetString());
        Assert.Equal("repo-graph", structured.GetProperty("result").GetProperty("command").GetString());

        ReadResourceResult resourceResult = await client.ReadResourceAsync("navlyn://workspace/summary", cancellationToken: timeout.Token);
        Assert.NotEmpty(resourceResult.Contents);
        TextResourceContents resourceText = Assert.IsType<TextResourceContents>(resourceResult.Contents[0]);
        using JsonDocument resourceJson = JsonDocument.Parse(resourceText.Text);
        Assert.True(resourceJson.RootElement.GetProperty("ok").GetBoolean());
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

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis.MSBuild;
using Navlyn.Cli.OutputProfiles;
using Navlyn.Mcp.Tools;
using Navlyn.Workspaces;

namespace Navlyn.Tests.Contracts;

public sealed class NavlynContractSchemaTests
{
    [Fact]
    public void SchemaDocuments_AreParseableDraft2020JsonSchemas()
    {
        foreach (string schemaPath in Directory.EnumerateFiles(GetSchemaDirectory(), "*.schema.json").Order(StringComparer.Ordinal))
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(schemaPath));
            JsonElement root = document.RootElement;

            Assert.Equal(JsonValueKind.Object, root.ValueKind);
            Assert.Equal("https://json-schema.org/draft/2020-12/schema", root.GetProperty("$schema").GetString());
            Assert.StartsWith("https://schemas.navlyn.dev/", root.GetProperty("$id").GetString(), StringComparison.Ordinal);
            Assert.Equal("object", root.GetProperty("type").GetString());
        }
    }

    [Fact]
    public void WorkflowEnvelopeSchema_MatchesOutputProfileContract()
    {
        JsonObject schema = ReadSchema("navlyn-workflow-envelope.schema.json");
        JsonArray required = schema["required"]!.AsArray();

        Assert.Contains(required, value => value!.GetValue<string>() == "schemaVersion");
        Assert.Contains(required, value => value!.GetValue<string>() == "reproCommand");
        Assert.Equal(OutputProfile.SchemaVersion, schema["properties"]!["schemaVersion"]!["const"]!.GetValue<string>());

        using MSBuildWorkspace workspaceHandle = MSBuildWorkspace.Create();
        JsonObject output = OutputProfile.Format(
            CreateWorkspace(workspaceHandle),
            "review-diff",
            OutputProfile.Compact,
            new
            {
                workspace = "navlyn.slnx",
                kind = "solution",
                command = "review-diff",
                findings = Array.Empty<object>(),
                limits = new { symbolLimit = 20 },
                truncated = false,
                warnings = Array.Empty<string>(),
                nextActions = new[]
                {
                    new { command = "context-pack", query = "Review changed symbols" }
                }
            });

        foreach (JsonNode? property in required)
        {
            Assert.True(output.ContainsKey(property!.GetValue<string>()), $"Missing workflow envelope property '{property}'.");
        }

        Assert.Equal("navlyn", output["reproCommand"]!["executable"]!.GetValue<string>());
        Assert.Equal("review-diff", output["command"]!.GetValue<string>());
        Assert.Equal("compact", output["profile"]!.GetValue<string>());
        Assert.Single(output["nextActions"]!.AsArray());
    }

    [Fact]
    public void McpEnvelopeSchema_MatchesSerializedToolResultContract()
    {
        JsonObject schema = ReadSchema("navlyn-mcp-tool-result.schema.json");
        JsonArray required = schema["required"]!.AsArray();

        Assert.Contains(required, value => value!.GetValue<string>() == "ok");
        Assert.Contains(required, value => value!.GetValue<string>() == "tool");
        Assert.Contains(required, value => value!.GetValue<string>() == "workspace");

        using JsonDocument resultDocument = JsonDocument.Parse("""
            {
              "command": "find",
              "nextActions": [
                {
                  "command": "symbol-source",
                  "file": "Navlyn.Core/Symbols/SymbolSourceResolver.cs",
                  "line": 12,
                  "column": 1
                },
                {
                  "command": "related-files",
                  "symbol": "Navlyn.Symbols.SymbolSourceResolver"
                }
              ]
            }
            """);
        NavlynToolResult result = NavlynToolResult.Succeeded(
            "navlyn_find_symbol",
            new NavlynSourceCommand("find", ["find", "--workspace", "navlyn.slnx", "--query", "SymbolSourceResolver"]),
            "navlyn.slnx",
            resultDocument.RootElement,
            new NavlynToolMetadata(
                ExecutionPath: "direct",
                WorkspaceCacheStatus: "warm",
                WorkspaceCacheHit: true,
                WorkspaceFingerprint: "abc123",
                SnapshotId: "abc123",
                CostClass: "semantic-lookup"));

        using JsonDocument envelope = JsonDocument.Parse(NavlynToolResultFormatter.ToJson(result));
        JsonElement root = envelope.RootElement;

        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("navlyn_find_symbol", root.GetProperty("tool").GetString());
        Assert.Equal("navlyn.slnx", root.GetProperty("workspace").GetString());
        Assert.Equal("abc123", root.GetProperty("metadata").GetProperty("snapshotId").GetString());
        Assert.False(root.GetProperty("recommendedNextAction").GetProperty("runByDefault").GetBoolean());
        Assert.Equal("cheap-file-first", root.GetProperty("recommendedNextAction").GetProperty("costClass").GetString());
        Assert.Single(root.GetProperty("optionalFollowUps").EnumerateArray());
    }

    private static JsonObject ReadSchema(string fileName)
    {
        return JsonNode.Parse(File.ReadAllText(Path.Combine(GetSchemaDirectory(), fileName)))!.AsObject();
    }

    private static string GetSchemaDirectory()
    {
        return Path.Combine(FindRepositoryRoot(), "docs", "schemas");
    }

    private static LoadedWorkspace CreateWorkspace(MSBuildWorkspace workspace)
    {
        return new LoadedWorkspace(
            FullPath: "D:/repo/navlyn.slnx",
            DisplayPath: "navlyn.slnx",
            Kind: "solution",
            Workspace: workspace,
            Solution: workspace.CurrentSolution,
            Projects: []);
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

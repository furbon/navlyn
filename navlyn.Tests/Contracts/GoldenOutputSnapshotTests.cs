using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis.MSBuild;
using Navlyn.Cli.OutputProfiles;
using Navlyn.Mcp.Tools;
using Navlyn.Workspaces;

namespace Navlyn.Tests.Contracts;

public sealed class GoldenOutputSnapshotTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    [Fact]
    public void WorkflowCompactEnvelope_MatchesGoldenSnapshot()
    {
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
                findings = new[]
                {
                    new
                    {
                        ruleId = "sample.rule",
                        severity = "info",
                        claim = "Sample finding"
                    }
                },
                changedSymbols = new { totalSymbols = 1, limit = 20, truncated = false },
                limits = new { symbolLimit = 20, impactLimit = 20 },
                truncated = false,
                warnings = Array.Empty<string>(),
                nextActions = new[]
                {
                    new { command = "context-pack", query = "Sample finding" }
                }
            });

        output["navlynVersion"] = "<version>";

        Assert.Equal(ReadSnapshot("workflow-compact-envelope.json"), NormalizeJson(output));
    }

    [Fact]
    public void McpToolResultEnvelope_MatchesGoldenSnapshot()
    {
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
                WorkspaceFingerprint: "fingerprint",
                SnapshotId: "fingerprint",
                CostClass: "semantic-lookup"));

        using JsonDocument envelope = JsonDocument.Parse(NavlynToolResultFormatter.ToJson(result));

        Assert.Equal(ReadSnapshot("mcp-tool-result-envelope.json"), NormalizeJson(envelope.RootElement));
    }

    private static string ReadSnapshot(string fileName)
    {
        return File.ReadAllText(Path.Combine(FindRepositoryRoot(), "navlyn.Tests", "Contracts", "GoldenSnapshots", fileName))
            .ReplaceLineEndings("\n")
            .Trim();
    }

    private static string NormalizeJson(JsonNode node)
    {
        return node.ToJsonString(JsonOptions).ReplaceLineEndings("\n").Trim();
    }

    private static string NormalizeJson(JsonElement element)
    {
        return JsonSerializer.Serialize(element, JsonOptions).ReplaceLineEndings("\n").Trim();
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

using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis.MSBuild;
using Navlyn.Cli.OutputProfiles;
using Navlyn.Workspaces;

namespace Navlyn.Tests.Cli.Commands;

public sealed class OutputProfileTests
{
    [Fact]
    public void FullProfileAddsMetadataAndPreservesResultFields()
    {
        using MSBuildWorkspace workspaceHandle = MSBuildWorkspace.Create();
        LoadedWorkspace workspace = CreateWorkspace(workspaceHandle);

        JsonObject result = OutputProfile.Format(
            workspace,
            "repo-graph",
            OutputProfile.Full,
            new
            {
                workspace = "navlyn.slnx",
                kind = "solution",
                command = "repo-graph",
                limits = new { relationshipLimit = 5 },
                truncated = false
            });

        Assert.Equal(OutputProfile.SchemaVersion, result["schemaVersion"]!.GetValue<string>());
        Assert.Equal(OutputProfile.Full, result["profile"]!.GetValue<string>());
        Assert.Equal("repo-graph", result["command"]!.GetValue<string>());
        Assert.Equal(5, result["limits"]!["relationshipLimit"]!.GetValue<int>());
    }

    [Fact]
    public void CompactProfileCreatesSummaryAndTrimsSnippetText()
    {
        using MSBuildWorkspace workspaceHandle = MSBuildWorkspace.Create();
        LoadedWorkspace workspace = CreateWorkspace(workspaceHandle);

        JsonObject result = OutputProfile.Format(
            workspace,
            "review-diff",
            OutputProfile.Compact,
            new
            {
                workspace = "navlyn.slnx",
                kind = "solution",
                command = "review-diff",
                findings = Enumerable.Range(0, 12).Select(index => new
                {
                    code = $"risk-{index}",
                    evidence = new[]
                    {
                        new
                        {
                            path = "A.cs",
                            line = index + 1,
                            snippet = new { lines = new[] { "source" } }
                        }
                    }
                }).ToArray(),
                changedSymbols = new { totalSymbols = 12, limit = 50, truncated = false },
                truncated = false,
                warnings = Array.Empty<string>(),
                nextActions = Array.Empty<object>()
            });

        Assert.Equal(OutputProfile.Compact, result["profile"]!.GetValue<string>());
        Assert.Equal(12, result["summary"]!["changedSymbols"]!["totalSymbols"]!.GetValue<int>());
        JsonArray findings = result["highlights"]!["findings"]!.AsArray();
        Assert.Equal(10, findings.Count);
        Assert.Null(findings[0]!["evidence"]![0]!["snippet"]);
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
}

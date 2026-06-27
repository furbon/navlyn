using System.Text.Json;
using Navlyn.Mcp.Tools;

namespace Navlyn.Tests.Mcp;

public sealed class NavlynToolCommandBuilderTests
{
    [Fact]
    public void WorkspaceSummary_BoolDefaultsCanBeOverridden()
    {
        CommandBuildResult result = NavlynToolCommandBuilder.WorkspaceSummary(
            project: null,
            projects: null,
            includePackages: false,
            includeMsbuildFiles: true,
            includePreprocessorSymbols: null,
            classification: false,
            relationshipLimit: 25,
            profile: null);

        Assert.True(result.IsValid);
        Assert.Equal("repo-graph", result.Command);
        Assert.Equal(
            ["--include-packages", "false", "--include-msbuild-files", "true", "--classification", "false", "--relationship-limit", "25"],
            result.Arguments);
    }

    [Fact]
    public void FindSymbol_ProjectAndProjectsAreMutuallyExclusive()
    {
        CommandBuildResult result = NavlynToolCommandBuilder.FindSymbol(
            query: "WorkspaceLoader",
            assumeKind: null,
            assumeKinds: null,
            match: null,
            caseSensitive: null,
            project: "navlyn",
            projects: ["navlyn.Tests"],
            excludeGenerated: null,
            limit: null,
            candidatePolicy: null,
            minConfidence: null,
            explainSelection: null);

        Assert.False(result.IsValid);
        Assert.Equal("project and projects are mutually exclusive.", result.Error);
    }

    [Fact]
    public void ReviewDiff_IncludeUnstagedFalseIsForwardedAsBoolValue()
    {
        CommandBuildResult result = NavlynToolCommandBuilder.ReviewDiff(
            baseRef: null,
            head: null,
            staged: null,
            includeUnstaged: false,
            project: null,
            projects: null,
            excludeGenerated: null,
            symbolLimit: null,
            impactLimit: null,
            diagnosticLimit: null,
            relatedTestLimit: null,
            depth: null,
            includeSnippets: null,
            snippetLines: null,
            profile: null);

        Assert.True(result.IsValid);
        Assert.Equal("review-diff", result.Command);
        Assert.Equal(["--include-unstaged", "false"], result.Arguments);
    }

    [Fact]
    public void ContextPack_RejectsDiffOptionsOutsideDiffMode()
    {
        CommandBuildResult result = NavlynToolCommandBuilder.ContextPack(
            query: "WorkspaceLoader",
            candidateId: null,
            diff: null,
            baseRef: "HEAD~1",
            head: null,
            staged: null,
            includeUnstaged: null,
            goal: null,
            budgetTokens: null,
            itemLimit: null,
            snippetPolicy: null,
            snippetLines: null,
            candidateLimit: null,
            memberLimit: null,
            referenceLimit: null,
            relationLimit: null,
            fileLimit: null,
            diagnosticLimit: null,
            symbolLimit: null,
            impactLimit: null,
            relatedTestLimit: null,
            depth: null,
            candidatePolicy: null,
            minConfidence: null,
            explainSelection: null,
            assumeKind: null,
            assumeKinds: null,
            match: null,
            caseSensitive: null,
            project: null,
            projects: null,
            excludeGenerated: null,
            profile: null);

        Assert.False(result.IsValid);
        Assert.Equal("Diff options require diff: true.", result.Error);
    }

    [Fact]
    public void ReviewDiff_ProfileIsForwarded()
    {
        CommandBuildResult result = NavlynToolCommandBuilder.ReviewDiff(
            baseRef: null,
            head: null,
            staged: null,
            includeUnstaged: null,
            project: null,
            projects: null,
            excludeGenerated: null,
            symbolLimit: null,
            impactLimit: null,
            diagnosticLimit: null,
            relatedTestLimit: null,
            depth: null,
            includeSnippets: null,
            snippetLines: null,
            profile: "evidence");

        Assert.True(result.IsValid);
        Assert.Equal(["--profile", "evidence"], result.Arguments);
    }

    [Fact]
    public void Batch_SerializesDefaultsAndRequestsForStandardInput()
    {
        using JsonDocument defaults = JsonDocument.Parse("""{"project":"navlyn"}""");
        using JsonDocument requests = JsonDocument.Parse("""[{"id":"find-loader","command":"find","query":"WorkspaceLoader"}]""");

        CommandBuildResult result = NavlynToolCommandBuilder.Batch(defaults.RootElement, requests.RootElement);

        Assert.True(result.IsValid);
        Assert.Equal("batch", result.Command);
        Assert.Equal([], result.Arguments);
        Assert.NotNull(result.StandardInput);
        using JsonDocument input = JsonDocument.Parse(result.StandardInput);
        Assert.Equal("navlyn", input.RootElement.GetProperty("defaults").GetProperty("project").GetString());
        Assert.Equal("find-loader", input.RootElement.GetProperty("requests")[0].GetProperty("id").GetString());
    }
}

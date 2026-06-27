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
            changeKind: null,
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
    public void ContextPack_ForwardsChangeKind()
    {
        CommandBuildResult result = NavlynToolCommandBuilder.ContextPack(
            query: null,
            candidateId: "sym:v1:00000000000000000000000000000000",
            diff: null,
            baseRef: null,
            head: null,
            staged: null,
            includeUnstaged: null,
            goal: "modify",
            changeKind: "signature",
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
            profile: "compact");

        Assert.True(result.IsValid);
        Assert.Equal("context-pack", result.Command);
        Assert.Equal(
            ["--candidate-id", "sym:v1:00000000000000000000000000000000", "--goal", "modify", "--change-kind", "signature", "--profile", "compact"],
            result.Arguments);
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

    [Fact]
    public void ExactNavigation_CandidateDefinitionBuildsCliCommand()
    {
        CommandBuildResult result = NavlynToolCommandBuilder.ExactNavigation(
            operation: "definition",
            candidateId: "sym:v1:00000000000000000000000000000000",
            file: null,
            line: null,
            column: null,
            project: null,
            excludeGenerated: true,
            resultProject: null,
            resultProjects: null,
            resultPath: null,
            resultPaths: null,
            resultKind: null,
            resultKinds: null,
            usageKind: null,
            usageKinds: null,
            groupBy: null,
            limit: null,
            includeMetadata: true);

        Assert.True(result.IsValid);
        Assert.Equal("definition", result.Command);
        Assert.Equal(
            ["--candidate-id", "sym:v1:00000000000000000000000000000000", "--exclude-generated", "--include-metadata"],
            result.Arguments);
    }

    [Fact]
    public void ExactNavigation_TypeHierarchyMapsUnderscoreOperation()
    {
        CommandBuildResult result = NavlynToolCommandBuilder.ExactNavigation(
            operation: "type_hierarchy",
            candidateId: null,
            file: "tests/fixtures/SymbolNavigationFixture/FixtureCode.cs",
            line: 50,
            column: 18,
            project: null,
            excludeGenerated: null,
            resultProject: null,
            resultProjects: null,
            resultPath: null,
            resultPaths: null,
            resultKind: null,
            resultKinds: null,
            usageKind: null,
            usageKinds: null,
            groupBy: null,
            limit: null,
            includeMetadata: null);

        Assert.True(result.IsValid);
        Assert.Equal("type-hierarchy", result.Command);
        Assert.Equal(
            ["--file", "tests/fixtures/SymbolNavigationFixture/FixtureCode.cs", "--line", "50", "--column", "18"],
            result.Arguments);
    }

    [Fact]
    public void ExactNavigation_RejectsMixedTargets()
    {
        CommandBuildResult result = NavlynToolCommandBuilder.ExactNavigation(
            operation: "references",
            candidateId: "sym:v1:00000000000000000000000000000000",
            file: "tests/fixtures/SymbolNavigationFixture/FixtureCode.cs",
            line: 50,
            column: 18,
            project: null,
            excludeGenerated: null,
            resultProject: null,
            resultProjects: null,
            resultPath: null,
            resultPaths: null,
            resultKind: null,
            resultKinds: null,
            usageKind: null,
            usageKinds: null,
            groupBy: null,
            limit: null,
            includeMetadata: null);

        Assert.False(result.IsValid);
        Assert.Equal("Specify exactly one target: candidateId or file with line and column.", result.Error);
    }

    [Fact]
    public void ExactNavigation_RejectsResultFiltersForDefinition()
    {
        CommandBuildResult result = NavlynToolCommandBuilder.ExactNavigation(
            operation: "definition",
            candidateId: "sym:v1:00000000000000000000000000000000",
            file: null,
            line: null,
            column: null,
            project: null,
            excludeGenerated: null,
            resultProject: null,
            resultProjects: null,
            resultPath: "navlyn",
            resultPaths: null,
            resultKind: null,
            resultKinds: null,
            usageKind: null,
            usageKinds: null,
            groupBy: null,
            limit: null,
            includeMetadata: null);

        Assert.False(result.IsValid);
        Assert.Equal("result filters are supported only for references, callers, calls, and implementations.", result.Error);
    }

    [Fact]
    public void ExactNavigation_ReferencesForwardsUsageFiltersAndGrouping()
    {
        CommandBuildResult result = NavlynToolCommandBuilder.ExactNavigation(
            operation: "references",
            candidateId: "sym:v1:00000000000000000000000000000000",
            file: null,
            line: null,
            column: null,
            project: null,
            excludeGenerated: null,
            resultProject: null,
            resultProjects: null,
            resultPath: null,
            resultPaths: null,
            resultKind: null,
            resultKinds: null,
            usageKind: null,
            usageKinds: ["construct,invoke"],
            groupBy: ["usage-kind", "test-vs-production"],
            limit: 20,
            includeMetadata: null);

        Assert.True(result.IsValid);
        Assert.Equal("references", result.Command);
        Assert.Equal(
            ["--candidate-id", "sym:v1:00000000000000000000000000000000", "--usage-kind", "construct", "--usage-kind", "invoke", "--group-by", "usage-kind", "--group-by", "test-vs-production", "--limit", "20"],
            result.Arguments);
    }

    [Fact]
    public void ExactNavigation_RejectsReferenceUsageFiltersForOtherOperations()
    {
        CommandBuildResult result = NavlynToolCommandBuilder.ExactNavigation(
            operation: "calls",
            candidateId: "sym:v1:00000000000000000000000000000000",
            file: null,
            line: null,
            column: null,
            project: null,
            excludeGenerated: null,
            resultProject: null,
            resultProjects: null,
            resultPath: null,
            resultPaths: null,
            resultKind: null,
            resultKinds: null,
            usageKind: "invoke",
            usageKinds: null,
            groupBy: null,
            limit: null,
            includeMetadata: null);

        Assert.False(result.IsValid);
        Assert.Equal("usageKind, usageKinds, and groupBy are supported only for references.", result.Error);
    }

    [Fact]
    public void TestsForSymbol_CandidateBuildsCliCommand()
    {
        CommandBuildResult result = NavlynToolCommandBuilder.TestsForSymbol(
            query: null,
            candidateId: "sym:v1:00000000000000000000000000000000",
            file: null,
            line: null,
            column: null,
            assumeKind: null,
            assumeKinds: null,
            match: null,
            caseSensitive: null,
            project: "navlyn",
            projects: null,
            testProject: "navlyn.Tests",
            testProjects: null,
            excludeGenerated: true,
            candidateLimit: null,
            testLimit: 5,
            referenceLimit: null,
            includeSnippets: null,
            snippetLines: null,
            candidatePolicy: null,
            minConfidence: null,
            explainSelection: null,
            profile: "compact");

        Assert.True(result.IsValid);
        Assert.Equal("tests-for-symbol", result.Command);
        Assert.Equal(
            ["--candidate-id", "sym:v1:00000000000000000000000000000000", "--project", "navlyn", "--exclude-generated", "--test-project", "navlyn.Tests", "--test-limit", "5", "--profile", "compact"],
            result.Arguments);
    }

    [Fact]
    public void TestsForDiff_ForwardsDiffAndTestFilters()
    {
        CommandBuildResult result = NavlynToolCommandBuilder.TestsForDiff(
            baseRef: "main",
            head: "HEAD",
            staged: null,
            includeUnstaged: false,
            project: null,
            projects: ["navlyn"],
            testProject: null,
            testProjects: ["navlyn.Tests"],
            excludeGenerated: null,
            symbolLimit: 10,
            testLimit: null,
            referenceLimit: null,
            includeSnippets: true,
            snippetLines: 0,
            profile: null);

        Assert.True(result.IsValid);
        Assert.Equal("tests-for-diff", result.Command);
        Assert.Equal(
            ["--base", "main", "--head", "HEAD", "--include-unstaged", "false", "--project", "navlyn", "--test-project", "navlyn.Tests", "--symbol-limit", "10", "--snippet-lines", "0", "--include-snippets"],
            result.Arguments);
    }

    [Fact]
    public void DiImpact_SourcePositionBuildsCliCommand()
    {
        CommandBuildResult result = NavlynToolCommandBuilder.DiImpact(
            query: null,
            candidateId: null,
            file: "Service.cs",
            line: 12,
            column: 8,
            assumeKind: null,
            assumeKinds: null,
            match: null,
            caseSensitive: null,
            project: null,
            projects: null,
            excludeGenerated: null,
            candidateLimit: null,
            registrationLimit: 3,
            consumerLimit: null,
            dependencyLimit: null,
            riskLimit: null,
            depth: 1,
            includeSnippets: null,
            snippetLines: null,
            candidatePolicy: null,
            minConfidence: null,
            explainSelection: null,
            profile: null);

        Assert.True(result.IsValid);
        Assert.Equal("di-impact", result.Command);
        Assert.Equal(
            ["--file", "Service.cs", "--line", "12", "--column", "8", "--registration-limit", "3", "--depth", "1"],
            result.Arguments);
    }

    [Fact]
    public void PublicApiDiff_RequiresBase()
    {
        CommandBuildResult result = NavlynToolCommandBuilder.PublicApiDiff(
            baseRef: null,
            head: null,
            project: null,
            projects: null,
            excludeGenerated: null,
            includeAdditions: null,
            includeAttributes: null,
            symbolLimit: null,
            changeLimit: null,
            profile: null);

        Assert.False(result.IsValid);
        Assert.Equal("base is required.", result.Error);
    }

    [Fact]
    public void PublicApiDiff_ForwardsOptions()
    {
        CommandBuildResult result = NavlynToolCommandBuilder.PublicApiDiff(
            baseRef: "v0.1.0",
            head: "HEAD",
            project: "navlyn",
            projects: null,
            excludeGenerated: true,
            includeAdditions: false,
            includeAttributes: true,
            symbolLimit: null,
            changeLimit: 20,
            profile: "evidence");

        Assert.True(result.IsValid);
        Assert.Equal("public-api-diff", result.Command);
        Assert.Equal(
            ["--base", "v0.1.0", "--head", "HEAD", "--project", "navlyn", "--change-limit", "20", "--profile", "evidence", "--exclude-generated", "--include-additions", "false", "--include-attributes", "true"],
            result.Arguments);
    }
}

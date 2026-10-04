using System.Text.Json;
using Navlyn.Mcp.Tools;

namespace Navlyn.Tests.Mcp;

public sealed class NavlynToolCommandBuilderTests
{
    [Fact]
    public void RoutesAndOptionsBuildersAreAvailable()
    {
        Assert.NotNull(typeof(NavlynToolCommandBuilder).GetMethod("Routes"));
        Assert.NotNull(typeof(NavlynToolCommandBuilder).GetMethod("Options"));
    }

    [Fact]
    public void MessagesBuilderIsAvailable()
    {
        Assert.NotNull(typeof(NavlynToolCommandBuilder).GetMethod("Messages"));
    }

    [Fact]
    public void EfBuilderIsAvailable()
    {
        Assert.NotNull(typeof(NavlynToolCommandBuilder).GetMethod("Ef"));
    }

    [Fact]
    public void PackagesBuilderIsAvailable()
    {
        Assert.NotNull(typeof(NavlynToolCommandBuilder).GetMethod("Packages"));
    }

    [Fact]
    public void Routes_MapMapsRouteFiltersAndCommonOptionsWithCompactDefault()
    {
        CommandBuildResult result = BuildRoutes(
            "map",
            routes: ["/orders", "/health"],
            endpointKinds: ["controller-action", "minimal-api"],
            auth: "required",
            project: "ApplicationDomainFixture",
            excludeGenerated: true,
            routeLimit: 11,
            evidenceLimit: 7,
            includeSnippets: true,
            snippetLines: 0);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal("route-map", result.Command);
        Assert.Equal(
            ["--project", "ApplicationDomainFixture", "--route-limit", "11", "--evidence-limit", "7", "--auth", "required", "--route", "/orders", "--route", "/health", "--endpoint-kind", "controller-action", "--endpoint-kind", "minimal-api", "--exclude-generated", "--include-snippets", "--snippet-lines", "0", "--profile", "compact"],
            result.Arguments);
    }

    [Fact]
    public void Routes_ImpactRequiresOneRouteAndMapsToRouteImpact()
    {
        CommandBuildResult result = BuildRoutes(
            "impact",
            route: "/orders",
            project: "ApplicationDomainFixture",
            routeLimit: 5,
            evidenceLimit: 3,
            excludeGenerated: true);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal("route-impact", result.Command);
        Assert.Equal(
            ["--project", "ApplicationDomainFixture", "--route-limit", "5", "--evidence-limit", "3", "--route", "/orders", "--exclude-generated", "--profile", "compact"],
            result.Arguments);
    }

    [Fact]
    public void Options_GraphMapsOptionalQueryAndLimitsWithCompactDefault()
    {
        CommandBuildResult result = BuildOptions(
            "graph",
            query: "PaymentOptions",
            project: "ApplicationDomainFixture",
            excludeGenerated: true,
            optionLimit: 12,
            consumerLimit: 9,
            bindingLimit: 8,
            evidenceLimit: 4,
            includeSnippets: true,
            snippetLines: 0);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal("options-graph", result.Command);
        Assert.Equal(
            ["--project", "ApplicationDomainFixture", "--option-limit", "12", "--consumer-limit", "9", "--binding-limit", "8", "--evidence-limit", "4", "--query", "PaymentOptions", "--exclude-generated", "--include-snippets", "--snippet-lines", "0", "--profile", "compact"],
            result.Arguments);
    }

    [Fact]
    public void Options_ImpactRequiresQueryAndMapsToConfigImpact()
    {
        CommandBuildResult result = BuildOptions(
            "impact",
            query: "Payments",
            projects: ["ApplicationDomainFixture"],
            optionLimit: 5,
            consumerLimit: 6,
            bindingLimit: 7,
            evidenceLimit: 8);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal("config-impact", result.Command);
        Assert.Equal(
            ["--project", "ApplicationDomainFixture", "--option-limit", "5", "--consumer-limit", "6", "--binding-limit", "7", "--evidence-limit", "8", "--query", "Payments", "--profile", "compact"],
            result.Arguments);
    }

    [Fact]
    public void Messages_HandlersMapsQueryAndCandidateLimitToWhereHandled()
    {
        CommandBuildResult result = BuildMessages(
            "handlers",
            query: "GetOrderHandler",
            assumeKind: "NamedType",
            match: "exact",
            candidatePolicy: "select",
            minConfidence: "medium",
            explainSelection: true,
            project: "Navlyn.Core",
            excludeGenerated: true,
            candidateLimit: 3,
            handlerLimit: 7,
            evidenceLimit: 5,
            includeSnippets: true,
            snippetLines: 0);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal("where-handled", result.Command);
        Assert.Equal(
            ["--query", "GetOrderHandler", "--assume-kind", "NamedType", "--project", "Navlyn.Core", "--match", "exact", "--candidate-policy", "select", "--min-confidence", "medium", "--exclude-generated", "--explain-selection", "--candidate-limit", "3", "--handler-limit", "7", "--evidence-limit", "5", "--snippet-lines", "0", "--include-snippets", "--profile", "compact"],
            result.Arguments);
    }

    [Fact]
    public void Messages_HandlersCandidateIdAcceptsCandidateSelectionControls()
    {
        CommandBuildResult result = BuildMessages(
            "handlers",
            candidateId: "sym:v1:00000000000000000000000000000000",
            candidateLimit: 2,
            candidatePolicy: "fail",
            minConfidence: "high",
            explainSelection: true);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal("where-handled", result.Command);
        Assert.Equal(
            ["--candidate-id", "sym:v1:00000000000000000000000000000000", "--candidate-policy", "fail", "--min-confidence", "high", "--explain-selection", "--candidate-limit", "2", "--profile", "compact"],
            result.Arguments);
    }

    [Fact]
    public void Messages_FlowMapsQueryCallSitesAndCompactDefault()
    {
        CommandBuildResult result = BuildMessages(
            "flow",
            query: "CreateOrderCommand",
            candidateLimit: 4,
            handlerLimit: 6,
            callSiteLimit: 9,
            evidenceLimit: 3);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal("message-flow", result.Command);
        Assert.Equal(
            ["--query", "CreateOrderCommand", "--candidate-limit", "4", "--handler-limit", "6", "--call-site-limit", "9", "--evidence-limit", "3", "--profile", "compact"],
            result.Arguments);
    }

    [Fact]
    public void Messages_FlowSourcePositionAllowsAtMostOneProjectAndMapsPosition()
    {
        CommandBuildResult result = BuildMessages(
            "flow",
            file: "src/Messages.cs",
            line: 14,
            column: 9,
            projects: ["Navlyn.Core"],
            callSiteLimit: 5);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal("message-flow", result.Command);
        Assert.Equal(
            ["--file", "src/Messages.cs", "--line", "14", "--column", "9", "--project", "Navlyn.Core", "--call-site-limit", "5", "--profile", "compact"],
            result.Arguments);
    }

    [Fact]
    public void Ef_ModelMapsInventoryFiltersAndLimitsWithCompactDefault()
    {
        CommandBuildResult result = BuildEf(
            "model",
            entity: "Order",
            dbcontext: "OrdersContext",
            project: "ApplicationDomainFixture(net10.0)",
            excludeGenerated: true,
            entityLimit: 12,
            querySiteLimit: 8,
            evidenceLimit: 5,
            includeSnippets: true,
            snippetLines: 0);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal("ef-model", result.Command);
        Assert.Equal(
            ["--entity", "Order", "--dbcontext", "OrdersContext", "--project", "ApplicationDomainFixture(net10.0)", "--exclude-generated", "--entity-limit", "12", "--query-site-limit", "8", "--evidence-limit", "5", "--snippet-lines", "0", "--include-snippets", "--profile", "compact"],
            result.Arguments);
    }

    [Fact]
    public void Ef_ImpactMapsQueryFuzzyOptionsAndCandidateLimitToEntityImpact()
    {
        CommandBuildResult result = BuildEf(
            "impact",
            query: "Order",
            assumeKind: "NamedType",
            match: "exact",
            candidatePolicy: "select",
            minConfidence: "medium",
            explainSelection: true,
            project: "Navlyn.Core",
            candidateLimit: 3,
            entityLimit: 7,
            querySiteLimit: 9,
            evidenceLimit: 4);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal("entity-impact", result.Command);
        Assert.Equal(
            ["--query", "Order", "--assume-kind", "NamedType", "--project", "Navlyn.Core", "--match", "exact", "--candidate-policy", "select", "--min-confidence", "medium", "--explain-selection", "--candidate-limit", "3", "--entity-limit", "7", "--query-site-limit", "9", "--evidence-limit", "4", "--profile", "compact"],
            result.Arguments);
    }

    [Fact]
    public void Ef_ImpactCandidateIdAcceptsSelectionControlsAndCandidateLimit()
    {
        CommandBuildResult result = BuildEf(
            "impact",
            candidateId: "sym:v1:00000000000000000000000000000000",
            candidatePolicy: "fail",
            minConfidence: "high",
            explainSelection: true,
            candidateLimit: 2);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal("entity-impact", result.Command);
        Assert.Equal(
            ["--candidate-id", "sym:v1:00000000000000000000000000000000", "--candidate-policy", "fail", "--min-confidence", "high", "--explain-selection", "--candidate-limit", "2", "--profile", "compact"],
            result.Arguments);
    }

    [Fact]
    public void Ef_ImpactSourcePositionAcceptsOneProjectAndPreservesDefaults()
    {
        CommandBuildResult result = BuildEf(
            "impact",
            file: "src/Order.cs",
            line: 11,
            column: 6,
            projects: ["Navlyn.Core"],
            entityLimit: 1);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal("entity-impact", result.Command);
        Assert.Equal(
            ["--file", "src/Order.cs", "--line", "11", "--column", "6", "--project", "Navlyn.Core", "--entity-limit", "1", "--profile", "compact"],
            result.Arguments);
    }

    [Theory]
    [InlineData("missing-mode")]
    [InlineData("empty-mode")]
    [InlineData("padded-mode")]
    [InlineData("unknown-mode")]
    [InlineData("model-query")]
    [InlineData("model-candidate")]
    [InlineData("model-file")]
    [InlineData("model-line")]
    [InlineData("model-column")]
    [InlineData("model-assume-kind")]
    [InlineData("model-assume-kinds")]
    [InlineData("model-match")]
    [InlineData("model-case-sensitive")]
    [InlineData("model-policy")]
    [InlineData("model-confidence")]
    [InlineData("model-explain")]
    [InlineData("model-candidate-limit")]
    [InlineData("impact-entity")]
    [InlineData("impact-dbcontext")]
    [InlineData("impact-missing-target")]
    [InlineData("impact-mixed-target")]
    [InlineData("impact-incomplete-target")]
    [InlineData("impact-file-only")]
    [InlineData("impact-line-column-without-file")]
    [InlineData("candidate-assume-kind")]
    [InlineData("candidate-assume-kinds")]
    [InlineData("candidate-match")]
    [InlineData("candidate-case-sensitive")]
    [InlineData("source-assume-kind")]
    [InlineData("source-assume-kinds")]
    [InlineData("source-match")]
    [InlineData("source-case-sensitive")]
    [InlineData("source-candidate-limit")]
    [InlineData("source-candidate-policy")]
    [InlineData("source-confidence")]
    [InlineData("source-explain")]
    [InlineData("source-projects")]
    [InlineData("project-collision")]
    [InlineData("assume-kind-collision")]
    [InlineData("invalid-match")]
    [InlineData("padded-match")]
    [InlineData("invalid-policy")]
    [InlineData("empty-policy")]
    [InlineData("group-policy")]
    [InlineData("invalid-confidence")]
    [InlineData("empty-confidence")]
    [InlineData("invalid-profile")]
    [InlineData("empty-profile")]
    [InlineData("candidate-limit-zero")]
    [InlineData("entity-limit-zero")]
    [InlineData("query-site-limit-zero")]
    [InlineData("evidence-limit-zero")]
    [InlineData("snippet-lines-negative")]
    public void Ef_RejectsInvalidModeOwnershipTargetsAndOptionShapesBeforeExecution(string shape)
    {
        const string candidate = "sym:v1:00000000000000000000000000000000";
        CommandBuildResult result = shape switch
        {
            "missing-mode" => BuildEf(null),
            "empty-mode" => BuildEf(""),
            "padded-mode" => BuildEf(" model "),
            "unknown-mode" => BuildEf("entity-impact"),
            "model-query" => BuildEf("model", query: "Order"),
            "model-candidate" => BuildEf("model", candidateId: candidate),
            "model-file" => BuildEf("model", file: "src/Order.cs"),
            "model-line" => BuildEf("model", line: 11),
            "model-column" => BuildEf("model", column: 6),
            "model-assume-kind" => BuildEf("model", assumeKind: "NamedType"),
            "model-assume-kinds" => BuildEf("model", assumeKinds: []),
            "model-match" => BuildEf("model", match: "exact"),
            "model-case-sensitive" => BuildEf("model", caseSensitive: false),
            "model-policy" => BuildEf("model", candidatePolicy: "fail"),
            "model-confidence" => BuildEf("model", minConfidence: "high"),
            "model-explain" => BuildEf("model", explainSelection: false),
            "model-candidate-limit" => BuildEf("model", candidateLimit: 1),
            "impact-entity" => BuildEf("impact", query: "Order", entity: "Order"),
            "impact-dbcontext" => BuildEf("impact", query: "Order", dbcontext: "OrdersContext"),
            "impact-missing-target" => BuildEf("impact"),
            "impact-mixed-target" => BuildEf("impact", query: "Order", candidateId: candidate),
            "impact-incomplete-target" => BuildEf("impact", file: "src/Order.cs", line: 11),
            "impact-file-only" => BuildEf("impact", file: "src/Order.cs"),
            "impact-line-column-without-file" => BuildEf("impact", line: 11, column: 6),
            "candidate-assume-kind" => BuildEf("impact", candidateId: candidate, assumeKind: "NamedType"),
            "candidate-assume-kinds" => BuildEf("impact", candidateId: candidate, assumeKinds: ["NamedType"]),
            "candidate-match" => BuildEf("impact", candidateId: candidate, match: "exact"),
            "candidate-case-sensitive" => BuildEf("impact", candidateId: candidate, caseSensitive: false),
            "source-assume-kind" => BuildEf("impact", file: "src/Order.cs", line: 11, column: 6, assumeKind: "NamedType"),
            "source-assume-kinds" => BuildEf("impact", file: "src/Order.cs", line: 11, column: 6, assumeKinds: ["NamedType"]),
            "source-match" => BuildEf("impact", file: "src/Order.cs", line: 11, column: 6, match: "exact"),
            "source-case-sensitive" => BuildEf("impact", file: "src/Order.cs", line: 11, column: 6, caseSensitive: false),
            "source-candidate-limit" => BuildEf("impact", file: "src/Order.cs", line: 11, column: 6, candidateLimit: 1),
            "source-candidate-policy" => BuildEf("impact", file: "src/Order.cs", line: 11, column: 6, candidatePolicy: "select"),
            "source-confidence" => BuildEf("impact", file: "src/Order.cs", line: 11, column: 6, minConfidence: "high"),
            "source-explain" => BuildEf("impact", file: "src/Order.cs", line: 11, column: 6, explainSelection: true),
            "source-projects" => BuildEf("impact", file: "src/Order.cs", line: 11, column: 6, projects: ["Navlyn.Core", "navlyn"]),
            "project-collision" => BuildEf("impact", query: "Order", project: "Navlyn.Core", projects: ["navlyn"]),
            "assume-kind-collision" => BuildEf("impact", query: "Order", assumeKind: "NamedType", assumeKinds: ["Class"]),
            "invalid-match" => BuildEf("impact", query: "Order", match: "fuzzy"),
            "padded-match" => BuildEf("impact", query: "Order", match: " exact "),
            "invalid-policy" => BuildEf("impact", query: "Order", candidatePolicy: "unknown"),
            "empty-policy" => BuildEf("impact", query: "Order", candidatePolicy: ""),
            "group-policy" => BuildEf("impact", query: "Order", candidatePolicy: "group"),
            "invalid-confidence" => BuildEf("impact", query: "Order", minConfidence: "certain"),
            "empty-confidence" => BuildEf("impact", query: "Order", minConfidence: ""),
            "invalid-profile" => BuildEf("model", profile: "verbose"),
            "empty-profile" => BuildEf("impact", query: "Order", profile: ""),
            "candidate-limit-zero" => BuildEf("impact", query: "Order", candidateLimit: 0),
            "entity-limit-zero" => BuildEf("model", entityLimit: 0),
            "query-site-limit-zero" => BuildEf("impact", query: "Order", querySiteLimit: 0),
            "evidence-limit-zero" => BuildEf("model", evidenceLimit: 0),
            "snippet-lines-negative" => BuildEf("impact", query: "Order", snippetLines: -1),
            _ => throw new InvalidOperationException($"Unknown EF shape {shape}."),
        };

        Assert.False(result.IsValid, shape);
        Assert.Null(result.Command);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Packages_UsageMapsRepeatedNamespacesAndFalseOverridesCliDefault()
    {
        CommandBuildResult result = BuildPackages(
            "usage",
            " Microsoft.EntityFrameworkCore ",
            namespaces: [" Microsoft.EntityFrameworkCore", "Microsoft.Extensions.DependencyInjection ", ""],
            project: "Navlyn.Core",
            includeTests: false,
            excludeGenerated: true,
            usageLimit: 12,
            referenceLimit: 8);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal("package-usage", result.Command);
        Assert.Equal(
            ["--package", "Microsoft.EntityFrameworkCore", "--namespace", "Microsoft.EntityFrameworkCore", "--namespace", "Microsoft.Extensions.DependencyInjection", "--project", "Navlyn.Core", "--usage-limit", "12", "--reference-limit", "8", "--include-tests", "false", "--exclude-generated", "--profile", "compact"],
            result.Arguments);
    }

    [Fact]
    public void Packages_ImpactMapsProjectsAndTrueIncludeTests()
    {
        CommandBuildResult result = BuildPackages(
            "impact",
            "Microsoft.EntityFrameworkCore",
            namespaces: ["Microsoft.EntityFrameworkCore"],
            projects: ["Navlyn.Core"],
            includeTests: true,
            usageLimit: 5,
            referenceLimit: 4,
            profile: "evidence");

        Assert.True(result.IsValid, result.Error);
        Assert.Equal("package-impact", result.Command);
        Assert.Equal(
            ["--package", "Microsoft.EntityFrameworkCore", "--namespace", "Microsoft.EntityFrameworkCore", "--project", "Navlyn.Core", "--usage-limit", "5", "--reference-limit", "4", "--include-tests", "true", "--profile", "evidence"],
            result.Arguments);
    }

    [Theory]
    [InlineData("missing-mode")]
    [InlineData("empty-mode")]
    [InlineData("padded-mode")]
    [InlineData("unknown-mode")]
    [InlineData("missing-package")]
    [InlineData("empty-package")]
    [InlineData("blank-package")]
    [InlineData("project-collision")]
    [InlineData("usage-limit-zero")]
    [InlineData("reference-limit-zero")]
    [InlineData("invalid-profile")]
    [InlineData("empty-profile")]
    public void Packages_RejectsInvalidModePackageAndOptionShapesBeforeExecution(string shape)
    {
        CommandBuildResult result = shape switch
        {
            "missing-mode" => BuildPackages(null, "Microsoft.EntityFrameworkCore"),
            "empty-mode" => BuildPackages("", "Microsoft.EntityFrameworkCore"),
            "padded-mode" => BuildPackages(" usage ", "Microsoft.EntityFrameworkCore"),
            "unknown-mode" => BuildPackages("list", "Microsoft.EntityFrameworkCore"),
            "missing-package" => BuildPackages("usage", null),
            "empty-package" => BuildPackages("usage", ""),
            "blank-package" => BuildPackages("impact", "   "),
            "project-collision" => BuildPackages("usage", "EntityFramework", project: "Navlyn.Core", projects: ["navlyn"]),
            "usage-limit-zero" => BuildPackages("usage", "EntityFramework", usageLimit: 0),
            "reference-limit-zero" => BuildPackages("impact", "EntityFramework", referenceLimit: 0),
            "invalid-profile" => BuildPackages("usage", "EntityFramework", profile: "verbose"),
            "empty-profile" => BuildPackages("impact", "EntityFramework", profile: ""),
            _ => throw new InvalidOperationException($"Unknown packages shape {shape}."),
        };

        Assert.False(result.IsValid, shape);
        Assert.Null(result.Command);
        Assert.NotNull(result.Error);
    }

    [Theory]
    [InlineData("workspace-summary", "compact")]
    [InlineData("review", "evidence")]
    [InlineData("impact", "light")]
    [InlineData("context-pack", "compact")]
    [InlineData("tests-for-symbol", "compact")]
    [InlineData("tests-for-diff", "compact")]
    [InlineData("public-api-diff", "evidence")]
    public void V08ProfileDefaultsAreAppliedWhenOmitted(string tool, string expectedProfile)
    {
        CommandBuildResult result = BuildProfileCommand(tool, profile: null);

        Assert.True(result.IsValid, result.Error);
        int profileIndex = Array.IndexOf(result.Arguments.ToArray(), "--profile");
        Assert.True(profileIndex >= 0, $"{tool} omitted --profile: {string.Join(" ", result.Arguments)}");
        Assert.Equal(expectedProfile, result.Arguments[profileIndex + 1]);
    }

    [Theory]
    [InlineData("workspace-summary", "full")]
    [InlineData("review", "compact")]
    [InlineData("impact", "full")]
    [InlineData("context-pack", "evidence")]
    [InlineData("tests-for-symbol", "evidence")]
    [InlineData("tests-for-diff", "full")]
    [InlineData("public-api-diff", "compact")]
    public void V08ProfileOverridesArePreserved(string tool, string overrideProfile)
    {
        CommandBuildResult result = BuildProfileCommand(tool, overrideProfile);

        Assert.True(result.IsValid, result.Error);
        int profileIndex = Array.IndexOf(result.Arguments.ToArray(), "--profile");
        Assert.True(profileIndex >= 0, $"{tool} omitted --profile: {string.Join(" ", result.Arguments)}");
        Assert.Equal(overrideProfile, result.Arguments[profileIndex + 1]);
    }

    [Theory]
    [InlineData("workspace-summary", "")]
    [InlineData("review", " ")]
    [InlineData("impact", "")]
    [InlineData("context-pack", "invalid")]
    [InlineData("tests-for-symbol", "invalid")]
    [InlineData("tests-for-diff", " ")]
    [InlineData("public-api-diff", "invalid")]
    public void V08ProfileValuesRejectBlankOrInvalidBeforeExecution(string tool, string profile)
    {
        CommandBuildResult result = BuildProfileCommand(tool, profile);

        Assert.False(result.IsValid, tool);
        Assert.Null(result.Command);
        Assert.NotNull(result.Error);
    }

    [Theory]
    [InlineData("missing-mode")]
    [InlineData("empty-mode")]
    [InlineData("padded-mode")]
    [InlineData("unknown-mode")]
    [InlineData("missing-target")]
    [InlineData("mixed-target")]
    [InlineData("incomplete-target")]
    [InlineData("file-only")]
    [InlineData("line-column-without-file")]
    [InlineData("handlers-call-site-limit")]
    [InlineData("candidate-assume-kind")]
    [InlineData("candidate-assume-kinds")]
    [InlineData("candidate-match")]
    [InlineData("candidate-case-sensitive")]
    [InlineData("source-assume-kind")]
    [InlineData("source-assume-kinds")]
    [InlineData("source-match")]
    [InlineData("source-case-sensitive")]
    [InlineData("source-candidate-limit")]
    [InlineData("source-candidate-policy")]
    [InlineData("source-min-confidence")]
    [InlineData("source-explain-selection")]
    [InlineData("source-projects")]
    [InlineData("project-collision")]
    [InlineData("assume-kind-collision")]
    [InlineData("invalid-match")]
    [InlineData("empty-match")]
    [InlineData("padded-match")]
    [InlineData("invalid-policy")]
    [InlineData("empty-policy")]
    [InlineData("group-policy")]
    [InlineData("group-policy-candidate-id")]
    [InlineData("invalid-confidence")]
    [InlineData("empty-confidence")]
    [InlineData("invalid-profile")]
    [InlineData("empty-profile")]
    [InlineData("candidate-limit-zero")]
    [InlineData("handler-limit-zero")]
    [InlineData("call-site-limit-zero")]
    [InlineData("evidence-limit-zero")]
    [InlineData("snippet-lines-negative")]
    public void Messages_RejectInvalidModeTargetOwnershipAndOptionShapesBeforeExecution(string shape)
    {
        const string candidate = "sym:v1:00000000000000000000000000000000";
        CommandBuildResult result = shape switch
        {
            "missing-mode" => BuildMessages(null),
            "empty-mode" => BuildMessages(""),
            "padded-mode" => BuildMessages(" handlers "),
            "unknown-mode" => BuildMessages("flowing"),
            "missing-target" => BuildMessages("handlers"),
            "mixed-target" => BuildMessages("flow", query: "Message", candidateId: candidate),
            "incomplete-target" => BuildMessages("handlers", file: "src/Messages.cs", line: 14),
            "file-only" => BuildMessages("flow", file: "src/Messages.cs"),
            "line-column-without-file" => BuildMessages("handlers", line: 14, column: 9),
            "handlers-call-site-limit" => BuildMessages("handlers", query: "Message", callSiteLimit: 1),
            "candidate-assume-kind" => BuildMessages("handlers", candidateId: candidate, assumeKind: "NamedType"),
            "candidate-assume-kinds" => BuildMessages("flow", candidateId: candidate, assumeKinds: ["NamedType"]),
            "candidate-match" => BuildMessages("handlers", candidateId: candidate, match: "exact"),
            "candidate-case-sensitive" => BuildMessages("flow", candidateId: candidate, caseSensitive: false),
            "source-assume-kind" => BuildMessages("handlers", file: "src/Messages.cs", line: 14, column: 9, assumeKind: "NamedType"),
            "source-assume-kinds" => BuildMessages("flow", file: "src/Messages.cs", line: 14, column: 9, assumeKinds: ["NamedType"]),
            "source-match" => BuildMessages("handlers", file: "src/Messages.cs", line: 14, column: 9, match: "exact"),
            "source-case-sensitive" => BuildMessages("flow", file: "src/Messages.cs", line: 14, column: 9, caseSensitive: false),
            "source-candidate-limit" => BuildMessages("handlers", file: "src/Messages.cs", line: 14, column: 9, candidateLimit: 1),
            "source-candidate-policy" => BuildMessages("flow", file: "src/Messages.cs", line: 14, column: 9, candidatePolicy: "select"),
            "source-min-confidence" => BuildMessages("handlers", file: "src/Messages.cs", line: 14, column: 9, minConfidence: "high"),
            "source-explain-selection" => BuildMessages("flow", file: "src/Messages.cs", line: 14, column: 9, explainSelection: true),
            "source-projects" => BuildMessages("flow", file: "src/Messages.cs", line: 14, column: 9, projects: ["Navlyn.Core", "Navlyn.Tests"]),
            "project-collision" => BuildMessages("handlers", query: "Message", project: "Navlyn.Core", projects: ["Navlyn.Tests"]),
            "assume-kind-collision" => BuildMessages("flow", query: "Message", assumeKind: "NamedType", assumeKinds: ["Method"]),
            "invalid-match" => BuildMessages("handlers", query: "Message", match: "fuzzy"),
            "empty-match" => BuildMessages("flow", query: "Message", match: ""),
            "padded-match" => BuildMessages("handlers", query: "Message", match: " exact "),
            "invalid-policy" => BuildMessages("flow", query: "Message", candidatePolicy: "unknown"),
            "empty-policy" => BuildMessages("handlers", query: "Message", candidatePolicy: ""),
            "group-policy" => BuildMessages("handlers", query: "Message", candidatePolicy: "group"),
            "group-policy-candidate-id" => BuildMessages("flow", candidateId: "sym:v1:00000000000000000000000000000000", candidatePolicy: "group"),
            "invalid-confidence" => BuildMessages("handlers", query: "Message", minConfidence: "certain"),
            "empty-confidence" => BuildMessages("flow", query: "Message", minConfidence: " "),
            "invalid-profile" => BuildMessages("flow", query: "Message", profile: "verbose"),
            "empty-profile" => BuildMessages("handlers", query: "Message", profile: ""),
            "candidate-limit-zero" => BuildMessages("flow", query: "Message", candidateLimit: 0),
            "handler-limit-zero" => BuildMessages("handlers", query: "Message", handlerLimit: 0),
            "call-site-limit-zero" => BuildMessages("flow", query: "Message", callSiteLimit: 0),
            "evidence-limit-zero" => BuildMessages("flow", query: "Message", evidenceLimit: 0),
            "snippet-lines-negative" => BuildMessages("handlers", query: "Message", snippetLines: -1),
            _ => throw new InvalidOperationException($"Unknown messages shape {shape}."),
        };

        Assert.False(result.IsValid, shape);
        Assert.Null(result.Command);
        Assert.NotNull(result.Error);
    }

    [Theory]
    [InlineData("missing-mode")]
    [InlineData("empty-mode")]
    [InlineData("padded-mode")]
    [InlineData("unknown-mode")]
    [InlineData("map-route")]
    [InlineData("impact-missing-route")]
    [InlineData("impact-blank-route")]
    [InlineData("impact-routes")]
    [InlineData("impact-empty-routes")]
    [InlineData("impact-endpoint-kinds")]
    [InlineData("impact-empty-endpoint-kinds")]
    [InlineData("impact-auth")]
    [InlineData("invalid-endpoint-kind")]
    [InlineData("empty-endpoint-kind")]
    [InlineData("invalid-auth")]
    [InlineData("empty-auth")]
    [InlineData("project-collision")]
    [InlineData("route-limit-zero")]
    [InlineData("evidence-limit-zero")]
    [InlineData("snippet-lines-negative")]
    [InlineData("invalid-profile")]
    [InlineData("empty-profile")]
    public void Routes_RejectInvalidModeOwnershipEnumsAndBoundsBeforeExecution(string shape)
    {
        CommandBuildResult result = shape switch
        {
            "missing-mode" => BuildRoutes(null),
            "empty-mode" => BuildRoutes(""),
            "padded-mode" => BuildRoutes(" map "),
            "unknown-mode" => BuildRoutes("graph"),
            "map-route" => BuildRoutes("map", route: ""),
            "impact-missing-route" => BuildRoutes("impact"),
            "impact-blank-route" => BuildRoutes("impact", route: "  "),
            "impact-routes" => BuildRoutes("impact", route: "/orders", routes: ["/health"]),
            "impact-empty-routes" => BuildRoutes("impact", route: "/orders", routes: []),
            "impact-endpoint-kinds" => BuildRoutes("impact", route: "/orders", endpointKinds: ["any"]),
            "impact-empty-endpoint-kinds" => BuildRoutes("impact", route: "/orders", endpointKinds: []),
            "impact-auth" => BuildRoutes("impact", route: "/orders", auth: "any"),
            "invalid-endpoint-kind" => BuildRoutes("map", endpointKinds: ["grpc"]),
            "empty-endpoint-kind" => BuildRoutes("map", endpointKinds: [""]),
            "invalid-auth" => BuildRoutes("map", auth: "protected"),
            "empty-auth" => BuildRoutes("map", auth: ""),
            "project-collision" => BuildRoutes("map", project: "app", projects: ["other"]),
            "route-limit-zero" => BuildRoutes("map", routeLimit: 0),
            "evidence-limit-zero" => BuildRoutes("impact", route: "/orders", evidenceLimit: 0),
            "snippet-lines-negative" => BuildRoutes("impact", route: "/orders", snippetLines: -1),
            "invalid-profile" => BuildRoutes("map", profile: "verbose"),
            "empty-profile" => BuildRoutes("impact", route: "/orders", profile: ""),
            _ => throw new InvalidOperationException($"Unknown routes shape {shape}."),
        };

        Assert.False(result.IsValid, shape);
        Assert.Null(result.Command);
        Assert.NotNull(result.Error);
    }

    [Theory]
    [InlineData("missing-mode")]
    [InlineData("empty-mode")]
    [InlineData("padded-mode")]
    [InlineData("unknown-mode")]
    [InlineData("impact-missing-query")]
    [InlineData("impact-blank-query")]
    [InlineData("project-collision")]
    [InlineData("option-limit-zero")]
    [InlineData("consumer-limit-zero")]
    [InlineData("binding-limit-zero")]
    [InlineData("evidence-limit-zero")]
    [InlineData("snippet-lines-negative")]
    [InlineData("invalid-profile")]
    [InlineData("empty-profile")]
    public void Options_RejectInvalidModeRequiredQueryAndBoundsBeforeExecution(string shape)
    {
        CommandBuildResult result = shape switch
        {
            "missing-mode" => BuildOptions(null),
            "empty-mode" => BuildOptions(""),
            "padded-mode" => BuildOptions(" graph "),
            "unknown-mode" => BuildOptions("routes"),
            "impact-missing-query" => BuildOptions("impact"),
            "impact-blank-query" => BuildOptions("impact", query: "  "),
            "project-collision" => BuildOptions("graph", project: "app", projects: ["other"]),
            "option-limit-zero" => BuildOptions("graph", optionLimit: 0),
            "consumer-limit-zero" => BuildOptions("impact", query: "Payments", consumerLimit: 0),
            "binding-limit-zero" => BuildOptions("graph", bindingLimit: 0),
            "evidence-limit-zero" => BuildOptions("impact", query: "Payments", evidenceLimit: 0),
            "snippet-lines-negative" => BuildOptions("graph", snippetLines: -1),
            "invalid-profile" => BuildOptions("graph", profile: "verbose"),
            "empty-profile" => BuildOptions("impact", query: "Payments", profile: ""),
            _ => throw new InvalidOperationException($"Unknown options shape {shape}."),
        };

        Assert.False(result.IsValid, shape);
        Assert.Null(result.Command);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Di_GraphMapsToDiGraphForwardsExplicitFalseAndDefaultsProfileToCompact()
    {
        CommandBuildResult result = BuildDi(
            "graph",
            project: "navlyn",
            excludeGenerated: true,
            registrationLimit: 10,
            dependencyLimit: 20,
            riskLimit: 30,
            includeOptions: false,
            includeHostedServices: false,
            includeRisks: false);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal("di-graph", result.Command);
        Assert.Equal(
            ["--project", "navlyn", "--exclude-generated", "--registration-limit", "10", "--dependency-limit", "20", "--risk-limit", "30", "--include-options", "false", "--include-hosted-services", "false", "--include-risks", "false", "--profile", "compact"],
            result.Arguments);
    }

    [Fact]
    public void Di_RegistrationsMapsQueryAndSelectionOptionsToWhereRegistered()
    {
        CommandBuildResult result = BuildDi(
            "registrations",
            query: "Widget",
            assumeKind: "NamedType",
            match: "exact",
            candidatePolicy: "select",
            minConfidence: "medium",
            explainSelection: true,
            candidateLimit: 3,
            project: "navlyn",
            registrationLimit: 5,
            dependencyLimit: 6,
            includeSnippets: true,
            snippetLines: 0);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal("where-registered", result.Command);
        Assert.Equal(
            ["--query", "Widget", "--assume-kind", "NamedType", "--project", "navlyn", "--match", "exact", "--candidate-policy", "select", "--min-confidence", "medium", "--explain-selection", "--candidate-limit", "3", "--registration-limit", "5", "--dependency-limit", "6", "--include-snippets", "--snippet-lines", "0", "--profile", "compact"],
            result.Arguments);
    }

    [Fact]
    public void Di_RegistrationsCandidateIdForwardsCandidateSelectionControlsButRejectsQueryOnlyFields()
    {
        CommandBuildResult result = BuildDi(
            "registrations",
            candidateId: "sym:v1:00000000000000000000000000000000",
            candidateLimit: 2,
            candidatePolicy: "fail",
            minConfidence: "high",
            explainSelection: true);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal("where-registered", result.Command);
        Assert.Equal(
            ["--candidate-id", "sym:v1:00000000000000000000000000000000", "--candidate-policy", "fail", "--min-confidence", "high", "--explain-selection", "--candidate-limit", "2", "--profile", "compact"],
            result.Arguments);
    }

    [Fact]
    public void Di_RegistrationsSourcePositionAllowsOneProjectAndMapsToWhereRegistered()
    {
        CommandBuildResult result = BuildDi(
            "registrations",
            file: "src/Service.cs",
            line: 12,
            column: 8,
            project: "navlyn");

        Assert.True(result.IsValid, result.Error);
        Assert.Equal("where-registered", result.Command);
        Assert.Equal(["--file", "src/Service.cs", "--line", "12", "--column", "8", "--project", "navlyn", "--profile", "compact"], result.Arguments);
    }

    [Fact]
    public void Di_ImpactMapsConsumerRiskAndDepthLimitsToDiImpact()
    {
        CommandBuildResult result = BuildDi(
            "impact",
            query: "Widget",
            registrationLimit: 5,
            dependencyLimit: 6,
            riskLimit: 7,
            consumerLimit: 8,
            depth: 0,
            candidateLimit: 4,
            snippetLines: 0);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal("di-impact", result.Command);
        Assert.Equal(
            ["--query", "Widget", "--candidate-limit", "4", "--registration-limit", "5", "--dependency-limit", "6", "--risk-limit", "7", "--consumer-limit", "8", "--depth", "0", "--snippet-lines", "0", "--profile", "compact"],
            result.Arguments);
    }

    [Fact]
    public void Di_ImpactCandidateIdUsesCandidateLimitOption()
    {
        CommandBuildResult result = BuildDi(
            "impact",
            candidateId: "sym:v1:00000000000000000000000000000000",
            candidateLimit: 2);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal("di-impact", result.Command);
        Assert.Equal(
            ["--candidate-id", "sym:v1:00000000000000000000000000000000", "--candidate-limit", "2", "--profile", "compact"],
            result.Arguments);
    }

    [Theory]
    [InlineData("missing-mode")]
    [InlineData("empty-mode")]
    [InlineData("padded-mode")]
    [InlineData("unknown-mode")]
    [InlineData("graph-query")]
    [InlineData("graph-candidate")]
    [InlineData("graph-file")]
    [InlineData("graph-line")]
    [InlineData("graph-column")]
    [InlineData("graph-assume")]
    [InlineData("graph-assumes")]
    [InlineData("graph-match")]
    [InlineData("graph-case")]
    [InlineData("graph-policy")]
    [InlineData("graph-confidence")]
    [InlineData("graph-explain")]
    [InlineData("graph-candidate-limit")]
    [InlineData("graph-consumer-limit")]
    [InlineData("graph-depth")]
    [InlineData("registrations-missing-target")]
    [InlineData("registrations-mixed-target")]
    [InlineData("registrations-incomplete-target")]
    [InlineData("registrations-file-only")]
    [InlineData("registrations-line-column-without-file")]
    [InlineData("registrations-consumer-limit")]
    [InlineData("registrations-risk-limit")]
    [InlineData("registrations-depth")]
    [InlineData("registrations-include-options")]
    [InlineData("registrations-include-hosted")]
    [InlineData("registrations-include-risks")]
    [InlineData("impact-include-options")]
    [InlineData("impact-include-hosted")]
    [InlineData("impact-include-risks")]
    [InlineData("candidate-assume")]
    [InlineData("candidate-assumes")]
    [InlineData("candidate-match")]
    [InlineData("candidate-case")]
    [InlineData("source-assume")]
    [InlineData("source-assumes")]
    [InlineData("source-match")]
    [InlineData("source-case")]
    [InlineData("source-candidate-limit")]
    [InlineData("source-policy")]
    [InlineData("source-confidence")]
    [InlineData("source-explain")]
    [InlineData("source-projects")]
    [InlineData("project-collision")]
    [InlineData("assume-kind-collision")]
    [InlineData("invalid-match")]
    [InlineData("invalid-policy")]
    [InlineData("invalid-confidence")]
    [InlineData("invalid-profile")]
    [InlineData("empty-profile")]
    [InlineData("candidate-limit-zero")]
    [InlineData("registration-limit-zero")]
    [InlineData("dependency-limit-zero")]
    [InlineData("risk-limit-zero")]
    [InlineData("consumer-limit-zero")]
    [InlineData("depth-negative")]
    [InlineData("snippet-lines-negative")]
    public void Di_RejectsInvalidModeOwnershipTargetAndOptionShapesBeforeExecution(string shape)
    {
        const string candidate = "sym:v1:00000000000000000000000000000000";
        CommandBuildResult result = shape switch
        {
            "missing-mode" => BuildDi(null),
            "empty-mode" => BuildDi(""),
            "padded-mode" => BuildDi(" graph "),
            "unknown-mode" => BuildDi("where"),
            "graph-query" => BuildDi("graph", query: "Widget"),
            "graph-candidate" => BuildDi("graph", candidateId: candidate),
            "graph-file" => BuildDi("graph", file: "src/Service.cs"),
            "graph-line" => BuildDi("graph", line: 12),
            "graph-column" => BuildDi("graph", column: 8),
            "graph-assume" => BuildDi("graph", assumeKind: "NamedType"),
            "graph-assumes" => BuildDi("graph", assumeKinds: ["NamedType"]),
            "graph-match" => BuildDi("graph", match: "exact"),
            "graph-case" => BuildDi("graph", caseSensitive: false),
            "graph-policy" => BuildDi("graph", candidatePolicy: "fail"),
            "graph-confidence" => BuildDi("graph", minConfidence: "high"),
            "graph-explain" => BuildDi("graph", explainSelection: false),
            "graph-candidate-limit" => BuildDi("graph", candidateLimit: 2),
            "graph-consumer-limit" => BuildDi("graph", consumerLimit: 2),
            "graph-depth" => BuildDi("graph", depth: 1),
            "registrations-missing-target" => BuildDi("registrations"),
            "registrations-mixed-target" => BuildDi("registrations", query: "Widget", candidateId: candidate),
            "registrations-incomplete-target" => BuildDi("registrations", file: "src/Service.cs", line: 12),
            "registrations-file-only" => BuildDi("registrations", file: "src/Service.cs"),
            "registrations-line-column-without-file" => BuildDi("registrations", line: 12, column: 8),
            "registrations-consumer-limit" => BuildDi("registrations", query: "Widget", consumerLimit: 1),
            "registrations-risk-limit" => BuildDi("registrations", query: "Widget", riskLimit: 1),
            "registrations-depth" => BuildDi("registrations", query: "Widget", depth: 1),
            "registrations-include-options" => BuildDi("registrations", query: "Widget", includeOptions: false),
            "registrations-include-hosted" => BuildDi("registrations", query: "Widget", includeHostedServices: false),
            "registrations-include-risks" => BuildDi("registrations", query: "Widget", includeRisks: false),
            "impact-include-options" => BuildDi("impact", query: "Widget", includeOptions: false),
            "impact-include-hosted" => BuildDi("impact", query: "Widget", includeHostedServices: false),
            "impact-include-risks" => BuildDi("impact", query: "Widget", includeRisks: false),
            "candidate-assume" => BuildDi("registrations", candidateId: candidate, assumeKind: "NamedType"),
            "candidate-assumes" => BuildDi("impact", candidateId: candidate, assumeKinds: ["NamedType"]),
            "candidate-match" => BuildDi("registrations", candidateId: candidate, match: "exact"),
            "candidate-case" => BuildDi("impact", candidateId: candidate, caseSensitive: false),
            "source-assume" => BuildDi("registrations", file: "src/Service.cs", line: 12, column: 8, assumeKind: "NamedType"),
            "source-assumes" => BuildDi("impact", file: "src/Service.cs", line: 12, column: 8, assumeKinds: ["NamedType"]),
            "source-match" => BuildDi("registrations", file: "src/Service.cs", line: 12, column: 8, match: "exact"),
            "source-case" => BuildDi("impact", file: "src/Service.cs", line: 12, column: 8, caseSensitive: false),
            "source-candidate-limit" => BuildDi("registrations", file: "src/Service.cs", line: 12, column: 8, candidateLimit: 1),
            "source-policy" => BuildDi("impact", file: "src/Service.cs", line: 12, column: 8, candidatePolicy: "select"),
            "source-confidence" => BuildDi("registrations", file: "src/Service.cs", line: 12, column: 8, minConfidence: "high"),
            "source-explain" => BuildDi("impact", file: "src/Service.cs", line: 12, column: 8, explainSelection: true),
            "source-projects" => BuildDi("registrations", file: "src/Service.cs", line: 12, column: 8, projects: ["navlyn", "navlyn.Tests"]),
            "project-collision" => BuildDi("registrations", query: "Widget", project: "navlyn", projects: ["navlyn.Tests"]),
            "assume-kind-collision" => BuildDi("impact", query: "Widget", assumeKind: "NamedType", assumeKinds: ["Method"]),
            "invalid-match" => BuildDi("impact", query: "Widget", match: "fuzzy"),
            "invalid-policy" => BuildDi("registrations", query: "Widget", candidatePolicy: "group"),
            "invalid-confidence" => BuildDi("impact", query: "Widget", minConfidence: "certain"),
            "invalid-profile" => BuildDi("graph", profile: "verbose"),
            "empty-profile" => BuildDi("impact", query: "Widget", profile: ""),
            "candidate-limit-zero" => BuildDi("impact", query: "Widget", candidateLimit: 0),
            "registration-limit-zero" => BuildDi("graph", registrationLimit: 0),
            "dependency-limit-zero" => BuildDi("registrations", query: "Widget", dependencyLimit: 0),
            "risk-limit-zero" => BuildDi("impact", query: "Widget", riskLimit: 0),
            "consumer-limit-zero" => BuildDi("impact", query: "Widget", consumerLimit: 0),
            "depth-negative" => BuildDi("impact", query: "Widget", depth: -1),
            "snippet-lines-negative" => BuildDi("graph", snippetLines: -1),
            _ => throw new InvalidOperationException($"Unknown DI shape {shape}."),
        };

        Assert.False(result.IsValid, shape);
        Assert.Null(result.Command);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Diagnostics_WorkspaceMapsFiltersAndPreservesOmittedDefaults()
    {
        CommandBuildResult defaults = BuildDiagnostics("workspace");
        Assert.True(defaults.IsValid, defaults.Error);
        Assert.Equal("diagnostics", defaults.Command);
        Assert.Empty(defaults.Arguments);

        CommandBuildResult filtered = BuildDiagnostics(
            "workspace",
            project: "navlyn",
            excludeGenerated: true,
            severities: ["Hidden", "Info", "Warning", "Error"],
            limit: 8,
            diagnosticIds: ["CS0168", "CS0219"]);

        Assert.True(filtered.IsValid, filtered.Error);
        Assert.Equal("diagnostics", filtered.Command);
        Assert.Equal(
            ["--project", "navlyn", "--exclude-generated", "--severity", "Hidden", "--severity", "Info", "--severity", "Warning", "--severity", "Error", "--limit", "8", "--id", "CS0168", "--id", "CS0219"],
            filtered.Arguments);
    }

    [Fact]
    public void Diagnostics_SymbolMapsCandidateAndDiagnosticFilters()
    {
        CommandBuildResult result = BuildDiagnostics(
            "symbol",
            project: "navlyn",
            excludeGenerated: true,
            severity: "Warning",
            limit: 4,
            diagnosticId: "CS0168",
            candidateId: "sym:v1:00000000000000000000000000000000");

        Assert.True(result.IsValid, result.Error);
        Assert.Equal("symbol-diagnostics", result.Command);
        Assert.Equal(
            ["--candidate-id", "sym:v1:00000000000000000000000000000000", "--project", "navlyn", "--exclude-generated", "--severity", "Warning", "--limit", "4", "--id", "CS0168"],
            result.Arguments);
    }

    [Fact]
    public void Diagnostics_SymbolMapsCompleteSourcePositionAndPluralDiagnosticFilters()
    {
        CommandBuildResult result = BuildDiagnostics(
            "symbol",
            severities: ["Error", "Warning"],
            diagnosticIds: ["CS0168", "CS0219"],
            file: "src/Service.cs",
            line: 12,
            column: 8);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal("symbol-diagnostics", result.Command);
        Assert.Equal(
            ["--file", "src/Service.cs", "--line", "12", "--column", "8", "--severity", "Error", "--severity", "Warning", "--id", "CS0168", "--id", "CS0219"],
            result.Arguments);
    }

    [Fact]
    public void Diagnostics_PackMapsDiagnosticIdAndPreservesPackDefaults()
    {
        CommandBuildResult result = BuildDiagnostics("pack", diagnosticId: "CS0103");

        Assert.True(result.IsValid, result.Error);
        Assert.Equal("diagnostic-pack", result.Command);
        Assert.Equal(["--id", "CS0103"], result.Arguments);
    }

    [Fact]
    public void Diagnostics_PackMapsCompleteSourcePosition()
    {
        CommandBuildResult result = BuildDiagnostics(
            "pack",
            project: "navlyn",
            limit: 3,
            file: "src/Service.cs",
            line: 12,
            column: 8);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal("diagnostic-pack", result.Command);
        Assert.Equal(["--file", "src/Service.cs", "--line", "12", "--column", "8", "--project", "navlyn", "--limit", "3"], result.Arguments);
    }

    [Theory]
    [InlineData("missing-mode")]
    [InlineData("empty-mode")]
    [InlineData("padded-mode")]
    [InlineData("unknown-mode")]
    [InlineData("workspace-candidate")]
    [InlineData("workspace-position")]
    [InlineData("workspace-line")]
    [InlineData("symbol-missing-target")]
    [InlineData("symbol-mixed-target")]
    [InlineData("symbol-incomplete-target")]
    [InlineData("symbol-line-column-without-file")]
    [InlineData("symbol-projects")]
    [InlineData("pack-missing-input")]
    [InlineData("pack-mixed-input")]
    [InlineData("pack-incomplete-input")]
    [InlineData("pack-candidate-id")]
    [InlineData("pack-diagnostic-ids")]
    [InlineData("pack-projects")]
    [InlineData("workspace-projects-and-project")]
    [InlineData("severity-collision")]
    [InlineData("diagnostic-id-collision")]
    [InlineData("invalid-severity")]
    [InlineData("invalid-severity-empty")]
    [InlineData("invalid-severity-padded")]
    [InlineData("invalid-severity-array")]
    [InlineData("invalid-severity-array-empty")]
    [InlineData("nonpositive-limit")]
    [InlineData("negative-limit")]
    public void Diagnostics_RejectsInvalidModesInputsAndFiltersBeforeExecution(string shape)
    {
        CommandBuildResult result = shape switch
        {
            "missing-mode" => BuildDiagnostics(null),
            "empty-mode" => BuildDiagnostics(""),
            "padded-mode" => BuildDiagnostics(" workspace "),
            "unknown-mode" => BuildDiagnostics("other"),
            "workspace-candidate" => BuildDiagnostics("workspace", candidateId: "sym:v1:00000000000000000000000000000000"),
            "workspace-position" => BuildDiagnostics("workspace", file: "src/Service.cs", line: 12, column: 8),
            "workspace-line" => BuildDiagnostics("workspace", line: 12),
            "symbol-missing-target" => BuildDiagnostics("symbol"),
            "symbol-mixed-target" => BuildDiagnostics("symbol", candidateId: "sym:v1:00000000000000000000000000000000", file: "src/Service.cs", line: 12, column: 8),
            "symbol-incomplete-target" => BuildDiagnostics("symbol", file: "src/Service.cs", line: 12),
            "symbol-line-column-without-file" => BuildDiagnostics("symbol", line: 12, column: 8),
            "symbol-projects" => BuildDiagnostics("symbol", candidateId: "sym:v1:00000000000000000000000000000000", projects: ["navlyn"]),
            "pack-missing-input" => BuildDiagnostics("pack"),
            "pack-mixed-input" => BuildDiagnostics("pack", diagnosticId: "CS0103", file: "src/Service.cs", line: 12, column: 8),
            "pack-incomplete-input" => BuildDiagnostics("pack", file: "src/Service.cs", line: 12),
            "pack-candidate-id" => BuildDiagnostics("pack", diagnosticId: "CS0103", candidateId: "sym:v1:00000000000000000000000000000000"),
            "pack-diagnostic-ids" => BuildDiagnostics("pack", diagnosticIds: ["CS0103"]),
            "pack-projects" => BuildDiagnostics("pack", diagnosticId: "CS0103", projects: ["navlyn"]),
            "workspace-projects-and-project" => BuildDiagnostics("workspace", project: "navlyn", projects: ["navlyn.Tests"]),
            "severity-collision" => BuildDiagnostics("workspace", severity: "Warning", severities: ["Error"]),
            "diagnostic-id-collision" => BuildDiagnostics("workspace", diagnosticId: "CS0168", diagnosticIds: ["CS0219"]),
            "invalid-severity" => BuildDiagnostics("workspace", severity: "warning"),
            "invalid-severity-empty" => BuildDiagnostics("workspace", severity: ""),
            "invalid-severity-padded" => BuildDiagnostics("workspace", severity: " Warning "),
            "invalid-severity-array" => BuildDiagnostics("workspace", severities: ["Warning", "Critical"]),
            "invalid-severity-array-empty" => BuildDiagnostics("workspace", severities: ["Warning", ""]),
            "nonpositive-limit" => BuildDiagnostics("workspace", limit: 0),
            "negative-limit" => BuildDiagnostics("pack", diagnosticId: "CS0103", limit: -1),
            _ => throw new InvalidOperationException($"Unknown diagnostics shape {shape}."),
        };

        Assert.False(result.IsValid, shape);
        Assert.Null(result.Command);
        Assert.NotNull(result.Error);
    }

    private static CommandBuildResult BuildDiagnostics(
        string? mode,
        string? project = null,
        string[]? projects = null,
        bool? excludeGenerated = null,
        string? severity = null,
        string[]? severities = null,
        int? limit = null,
        string? diagnosticId = null,
        string[]? diagnosticIds = null,
        string? candidateId = null,
        string? file = null,
        int? line = null,
        int? column = null)
    {
        return NavlynToolCommandBuilder.Diagnostics(
            mode,
            project,
            projects,
            excludeGenerated,
            severity,
            severities,
            limit,
            diagnosticId,
            diagnosticIds,
            candidateId,
            file,
            line,
            column);
    }

    private static CommandBuildResult BuildRoutes(
        string? mode,
        string? route = null,
        string[]? routes = null,
        string[]? endpointKinds = null,
        string? auth = null,
        string? project = null,
        string[]? projects = null,
        bool? excludeGenerated = null,
        int? routeLimit = null,
        int? evidenceLimit = null,
        bool? includeSnippets = null,
        int? snippetLines = null,
        string? profile = null)
    {
        return NavlynToolCommandBuilder.Routes(mode, route, routes, endpointKinds, auth, project, projects, excludeGenerated, routeLimit, evidenceLimit, includeSnippets, snippetLines, profile);
    }

    private static CommandBuildResult BuildOptions(
        string? mode,
        string? query = null,
        string? project = null,
        string[]? projects = null,
        bool? excludeGenerated = null,
        int? optionLimit = null,
        int? consumerLimit = null,
        int? bindingLimit = null,
        int? evidenceLimit = null,
        bool? includeSnippets = null,
        int? snippetLines = null,
        string? profile = null)
    {
        return NavlynToolCommandBuilder.Options(mode, query, project, projects, excludeGenerated, optionLimit, consumerLimit, bindingLimit, evidenceLimit, includeSnippets, snippetLines, profile);
    }

    private static CommandBuildResult BuildMessages(
        string? mode,
        string? query = null,
        string? candidateId = null,
        string? file = null,
        int? line = null,
        int? column = null,
        string? assumeKind = null,
        string[]? assumeKinds = null,
        string? match = null,
        bool? caseSensitive = null,
        string? candidatePolicy = null,
        string? minConfidence = null,
        bool? explainSelection = null,
        string? project = null,
        string[]? projects = null,
        bool? excludeGenerated = null,
        int? candidateLimit = null,
        int? handlerLimit = null,
        int? callSiteLimit = null,
        int? evidenceLimit = null,
        bool? includeSnippets = null,
        int? snippetLines = null,
        string? profile = null)
    {
        return NavlynToolCommandBuilder.Messages(mode, query, candidateId, file, line, column, assumeKind, assumeKinds, match, caseSensitive, candidatePolicy, minConfidence, explainSelection, project, projects, excludeGenerated, candidateLimit, handlerLimit, callSiteLimit, evidenceLimit, includeSnippets, snippetLines, profile);
    }

    private static CommandBuildResult BuildEf(
        string? mode,
        string? entity = null,
        string? dbcontext = null,
        string? query = null,
        string? candidateId = null,
        string? file = null,
        int? line = null,
        int? column = null,
        string? assumeKind = null,
        string[]? assumeKinds = null,
        string? match = null,
        bool? caseSensitive = null,
        string? candidatePolicy = null,
        string? minConfidence = null,
        bool? explainSelection = null,
        string? project = null,
        string[]? projects = null,
        bool? excludeGenerated = null,
        int? candidateLimit = null,
        int? entityLimit = null,
        int? querySiteLimit = null,
        int? evidenceLimit = null,
        bool? includeSnippets = null,
        int? snippetLines = null,
        string? profile = null)
    {
        return NavlynToolCommandBuilder.Ef(mode, entity, dbcontext, query, candidateId, file, line, column, assumeKind, assumeKinds, match, caseSensitive, candidatePolicy, minConfidence, explainSelection, project, projects, excludeGenerated, candidateLimit, entityLimit, querySiteLimit, evidenceLimit, includeSnippets, snippetLines, profile);
    }

    private static CommandBuildResult BuildPackages(
        string? mode,
        string? package,
        string[]? namespaces = null,
        string? project = null,
        string[]? projects = null,
        bool? includeTests = null,
        bool? excludeGenerated = null,
        int? usageLimit = null,
        int? referenceLimit = null,
        string? profile = null)
    {
        return NavlynToolCommandBuilder.Packages(mode, package, namespaces, project, projects, includeTests, excludeGenerated, usageLimit, referenceLimit, profile);
    }

    private static CommandBuildResult BuildProfileCommand(string tool, string? profile)
    {
        return tool switch
        {
            "workspace-summary" => NavlynToolCommandBuilder.WorkspaceSummary(null, null, null, null, null, null, null, profile),
            "review" => NavlynToolCommandBuilder.Review(null, null, null, null, null, null, null, null, null, null, null, null, null, null, profile),
            "impact" => NavlynToolCommandBuilder.FuzzySymbolCommand("impact", query: "Widget", candidateId: null, assumeKind: null, assumeKinds: null, match: null, caseSensitive: null, project: null, projects: null, excludeGenerated: null, memberLimit: null, referenceLimit: null, relationLimit: null, include: null, limit: null, depth: null, includeSnippets: null, snippetLines: null, scope: null, maxDocuments: null, profile: profile, candidatePolicy: null, minConfidence: null, explainSelection: null),
            "context-pack" => NavlynToolCommandBuilder.ContextPack(query: "Widget", candidateId: null, diff: null, baseRef: null, head: null, staged: null, includeUnstaged: null, goal: null, changeKind: null, budgetTokens: null, itemLimit: null, snippetPolicy: null, snippetLines: null, candidateLimit: null, memberLimit: null, referenceLimit: null, relationLimit: null, fileLimit: null, diagnosticLimit: null, symbolLimit: null, impactLimit: null, relatedTestLimit: null, depth: null, candidatePolicy: null, minConfidence: null, explainSelection: null, assumeKind: null, assumeKinds: null, match: null, caseSensitive: null, project: null, projects: null, excludeGenerated: null, profile: profile),
            "tests-for-symbol" => NavlynToolCommandBuilder.TestsForSymbol("Widget", null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, profile),
            "tests-for-diff" => NavlynToolCommandBuilder.TestsForDiff(null, null, null, null, null, null, null, null, null, null, null, null, null, null, profile),
            "public-api-diff" => NavlynToolCommandBuilder.PublicApiDiff("main", null, null, null, null, null, null, null, null, profile),
            _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, null)
        };
    }

    private static CommandBuildResult BuildDi(
        string? mode,
        string? query = null,
        string? candidateId = null,
        string? file = null,
        int? line = null,
        int? column = null,
        string? assumeKind = null,
        string[]? assumeKinds = null,
        string? match = null,
        bool? caseSensitive = null,
        string? candidatePolicy = null,
        string? minConfidence = null,
        bool? explainSelection = null,
        int? candidateLimit = null,
        string? project = null,
        string[]? projects = null,
        bool? excludeGenerated = null,
        int? registrationLimit = null,
        int? dependencyLimit = null,
        int? riskLimit = null,
        int? consumerLimit = null,
        int? depth = null,
        bool? includeOptions = null,
        bool? includeHostedServices = null,
        bool? includeRisks = null,
        bool? includeSnippets = null,
        int? snippetLines = null,
        string? profile = null)
    {
        return NavlynToolCommandBuilder.Di(
            mode,
            query,
            candidateId,
            file,
            line,
            column,
            assumeKind,
            assumeKinds,
            match,
            caseSensitive,
            candidatePolicy,
            minConfidence,
            explainSelection,
            candidateLimit,
            project,
            projects,
            excludeGenerated,
            registrationLimit,
            dependencyLimit,
            riskLimit,
            consumerLimit,
            depth,
            includeOptions,
            includeHostedServices,
            includeRisks,
            includeSnippets,
            snippetLines,
            profile);
    }

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
            ["--include-packages", "false", "--include-msbuild-files", "true", "--classification", "false", "--relationship-limit", "25", "--profile", "compact"],
            result.Arguments);
    }

    [Fact]
    public void WorkspaceRefresh_ForwardsCacheControls()
    {
        CommandBuildResult result = NavlynToolCommandBuilder.WorkspaceRefresh(
            cache: "on",
            cacheDirectory: ".navlyn/custom-cache",
            clearCache: true,
            writeCache: true);

        Assert.True(result.IsValid);
        Assert.Equal("workspace-refresh", result.Command);
        Assert.Equal(
            ["--cache", "on", "--cache-directory", ".navlyn/custom-cache", "--clear-cache", "--write-cache"],
            result.Arguments);
    }

    [Fact]
    public void Doctor_BuildsCliCommandWithoutArguments()
    {
        CommandBuildResult result = NavlynToolCommandBuilder.Doctor();

        Assert.True(result.IsValid);
        Assert.Equal("doctor", result.Command);
        Assert.Equal([], result.Arguments);
        Assert.Null(result.StandardInput);
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
    public void ResolveTarget_QueryBuildsCliCommand()
    {
        CommandBuildResult result = NavlynToolCommandBuilder.ResolveTarget(
            query: "CheckCommand",
            candidateId: null,
            file: null,
            line: null,
            column: null,
            assumeKind: "NamedType",
            assumeKinds: null,
            match: null,
            caseSensitive: null,
            project: null,
            projects: null,
            excludeGenerated: true,
            limit: 5,
            candidatePolicy: null,
            minConfidence: null,
            explainSelection: null);

        Assert.True(result.IsValid);
        Assert.Equal("resolve-target", result.Command);
        Assert.Equal(
            ["--query", "CheckCommand", "--assume-kind", "NamedType", "--limit", "5", "--exclude-generated"],
            result.Arguments);
    }

    [Fact]
    public void CanonicalTarget_MapsToTargetCliCommand()
    {
        CommandBuildResult result = NavlynToolCommandBuilder.Target(
            mode: null,
            query: "CheckCommand",
            candidateId: null,
            file: null,
            line: null,
            column: null,
            assumeKind: "NamedType",
            assumeKinds: null,
            match: null,
            caseSensitive: null,
            project: null,
            projects: null,
            excludeGenerated: null,
            limit: 5,
            candidatePolicy: null,
            minConfidence: null,
            explainSelection: null);

        Assert.True(result.IsValid);
        Assert.Equal("target", result.Command);
        Assert.Equal(["--query", "CheckCommand", "--assume-kind", "NamedType", "--limit", "5"], result.Arguments);
    }

    [Fact]
    public void VerifyEdit_PreflightUsesExistingVerifyEditCliAlias()
    {
        CommandBuildResult result = BuildVerifyEdit(preflight: "prepare-edit.json");

        Assert.True(result.IsValid, result.Error);
        Assert.Equal("verify-edit", result.Command);
        Assert.Equal("verify-edit", result.ResultCommand);
        Assert.Equal(["--preflight", "prepare-edit.json"], result.Arguments);
    }

    [Fact]
    public void VerifyEdit_CandidateIdUsesExistingVerifyEditCliAlias()
    {
        CommandBuildResult result = BuildVerifyEdit(candidateId: "sym:v1:00000000000000000000000000000000");

        Assert.True(result.IsValid, result.Error);
        Assert.Equal("verify-edit", result.Command);
        Assert.Equal("verify-edit", result.ResultCommand);
        Assert.Equal(["--candidate-id", "sym:v1:00000000000000000000000000000000"], result.Arguments);
    }

    [Fact]
    public void VerifyEdit_QueryUsesWrongSymbolCliCommandAndCanonicalResultCommand()
    {
        CommandBuildResult result = BuildVerifyEdit(
            query: "OutlineCommand",
            assumeKind: "NamedType",
            match: "exact",
            candidatePolicy: "select");

        Assert.True(result.IsValid, result.Error);
        Assert.Equal("wrong-symbol-guard", result.Command);
        Assert.Equal("verify-edit", result.ResultCommand);
        Assert.Equal(["--query", "OutlineCommand", "--assume-kind", "NamedType"], result.Arguments.Take(4));
    }

    [Fact]
    public void VerifyEdit_SourcePositionUsesWrongSymbolCliCommandAndCanonicalResultCommand()
    {
        CommandBuildResult result = BuildVerifyEdit(
            file: "src/Service.cs",
            line: 12,
            column: 8);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal("wrong-symbol-guard", result.Command);
        Assert.Equal("verify-edit", result.ResultCommand);
        Assert.Equal(["--file", "src/Service.cs", "--line", "12", "--column", "8"], result.Arguments);
    }

    [Theory]
    [InlineData("candidate-query")]
    [InlineData("preflight-source")]
    [InlineData("source-incomplete")]
    [InlineData("no-intent")]
    [InlineData("anchor-fuzzy")]
    [InlineData("source-fuzzy")]
    public void VerifyEdit_RejectsMixedIncompleteAndUnsupportedIntentOptions(string shape)
    {
        CommandBuildResult result = shape switch
        {
            "candidate-query" => BuildVerifyEdit(query: "OutlineCommand", candidateId: "sym:v1:00000000000000000000000000000000"),
            "preflight-source" => BuildVerifyEdit(preflight: "prepare-edit.json", file: "src/Service.cs", line: 12, column: 8),
            "source-incomplete" => BuildVerifyEdit(file: "src/Service.cs", line: 12),
            "no-intent" => BuildVerifyEdit(),
            "anchor-fuzzy" => BuildVerifyEdit(candidateId: "sym:v1:00000000000000000000000000000000", match: "exact"),
            "source-fuzzy" => BuildVerifyEdit(file: "src/Service.cs", line: 12, column: 8, assumeKind: "NamedType"),
            _ => throw new InvalidOperationException($"Unknown verify-edit shape {shape}.")
        };

        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void NavlynToolResult_WithResultCommandPreservesGuardSchemaAndSourceCommand()
    {
        using JsonDocument document = JsonDocument.Parse("""
            { "schemaVersion": "navlyn.agent-guard.v1", "command": "wrong-symbol-guard", "ok": true }
            """);
        NavlynToolResult result = NavlynToolResult.Succeeded(
            "navlyn_verify_edit",
            new NavlynSourceCommand("wrong-symbol-guard", ["wrong-symbol-guard", "--workspace", "navlyn.slnx"]),
            "navlyn.slnx",
            document.RootElement);

        NavlynToolResult normalized = result.WithResultCommand("verify-edit");

        Assert.Equal("wrong-symbol-guard", normalized.SourceCommand!.Command);
        Assert.Equal("navlyn.agent-guard.v1", normalized.Result!.Value.GetProperty("schemaVersion").GetString());
        Assert.Equal("verify-edit", normalized.Result.Value.GetProperty("command").GetString());
    }

    [Fact]
    public void Target_OmittedModeDefaultsToSelect()
    {
        CommandBuildResult result = BuildTarget(mode: null, query: "CheckCommand");

        Assert.True(result.IsValid);
        Assert.Equal("target", result.Command);
        Assert.Equal(["--query", "CheckCommand"], result.Arguments);
    }

    [Fact]
    public void Target_ListMapsToFindAndForwardsFuzzyFilters()
    {
        CommandBuildResult result = BuildTarget(
            mode: "list",
            query: "CheckCommand",
            assumeKind: "NamedType",
            match: "exact",
            project: "navlyn.Tests",
            excludeGenerated: true,
            limit: 7);

        Assert.True(result.IsValid);
        Assert.Equal("find", result.Command);
        Assert.Equal(
            ["--query", "CheckCommand", "--assume-kind", "NamedType", "--project", "navlyn.Tests", "--limit", "7", "--match", "exact", "--candidate-policy", "group", "--exclude-generated"],
            result.Arguments);
    }

    [Fact]
    public void Target_ListAcceptsExplicitGroupPolicy()
    {
        CommandBuildResult result = BuildTarget(mode: "list", query: "CheckCommand", candidatePolicy: "group");

        Assert.True(result.IsValid);
        Assert.Equal("find", result.Command);
        Assert.Equal(["--query", "CheckCommand", "--candidate-policy", "group"], result.Arguments);
    }

    [Fact]
    public void Target_RejectsUnsupportedMode()
    {
        CommandBuildResult result = BuildTarget(mode: "discover", query: "CheckCommand");

        Assert.False(result.IsValid);
        Assert.Equal("mode must be select or list.", result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t")]
    [InlineData(" list ")]
    public void Target_RejectsNonExactModeValues(string mode)
    {
        CommandBuildResult result = BuildTarget(mode, query: "CheckCommand");

        Assert.False(result.IsValid);
        Assert.Equal("mode must be select or list.", result.Error);
    }

    [Fact]
    public void Target_ListRequiresQuery()
    {
        CommandBuildResult result = BuildTarget(mode: "list", query: "  ");

        Assert.False(result.IsValid);
        Assert.Equal("query is required in list mode.", result.Error);
    }

    [Fact]
    public void Target_ListRejectsCandidateId()
    {
        CommandBuildResult result = BuildTarget(
            mode: "list",
            query: "CheckCommand",
            candidateId: "sym:v1:00000000000000000000000000000000");

        Assert.False(result.IsValid);
        Assert.Contains("candidateId and source-position inputs are not valid", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("fail")]
    [InlineData("select")]
    [InlineData("")]
    [InlineData(" \t")]
    [InlineData("other")]
    public void Target_ListRejectsNonGroupCandidatePolicies(string candidatePolicy)
    {
        CommandBuildResult result = BuildTarget(mode: "list", query: "CheckCommand", candidatePolicy: candidatePolicy);

        Assert.False(result.IsValid);
        Assert.Equal("candidatePolicy in list mode must be omitted or group.", result.Error);
    }

    [Theory]
    [InlineData("file")]
    [InlineData("line")]
    [InlineData("column")]
    public void Target_ListRejectsEachSourcePositionInput(string field)
    {
        CommandBuildResult result = field switch
        {
            "file" => BuildTarget(mode: "list", query: "CheckCommand", file: "Service.cs"),
            "line" => BuildTarget(mode: "list", query: "CheckCommand", line: 12),
            "column" => BuildTarget(mode: "list", query: "CheckCommand", column: 8),
            _ => throw new InvalidOperationException($"Unknown source-position field {field}.")
        };

        Assert.False(result.IsValid);
        Assert.Contains("candidateId and source-position inputs are not valid", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Target_SelectRejectsQueryAndCandidateIdTogether()
    {
        CommandBuildResult result = BuildTarget(
            mode: "select",
            query: "CheckCommand",
            candidateId: "sym:v1:00000000000000000000000000000000");

        Assert.False(result.IsValid);
        Assert.Equal("Specify exactly one target: query, candidateId, or file with line and column.", result.Error);
    }

    [Fact]
    public void Target_SelectRejectsIncompleteSourcePosition()
    {
        CommandBuildResult result = BuildTarget(mode: "select", file: "Service.cs", line: 12);

        Assert.False(result.IsValid);
        Assert.Equal("Specify exactly one target: query, candidateId, or file with line and column.", result.Error);
    }

    private static CommandBuildResult BuildTarget(
        string? mode,
        string? query = null,
        string? candidateId = null,
        string? file = null,
        int? line = null,
        int? column = null,
        string? assumeKind = null,
        string[]? assumeKinds = null,
        string? match = null,
        bool? caseSensitive = null,
        string? project = null,
        string[]? projects = null,
        bool? excludeGenerated = null,
        int? limit = null,
        string? candidatePolicy = null,
        string? minConfidence = null,
        bool? explainSelection = null)
    {
        return NavlynToolCommandBuilder.Target(mode, query, candidateId, file, line, column, assumeKind, assumeKinds, match, caseSensitive, project, projects, excludeGenerated, limit, candidatePolicy, minConfidence, explainSelection);
    }

    [Fact]
    public void ResolveTarget_SourcePositionRejectsFuzzyOptions()
    {
        CommandBuildResult result = NavlynToolCommandBuilder.ResolveTarget(
            query: null,
            candidateId: null,
            file: "Service.cs",
            line: 12,
            column: 8,
            assumeKind: "NamedType",
            assumeKinds: null,
            match: null,
            caseSensitive: null,
            project: null,
            projects: null,
            excludeGenerated: null,
            limit: null,
            candidatePolicy: null,
            minConfidence: null,
            explainSelection: null);

        Assert.False(result.IsValid);
        Assert.Equal("Source-position resolve-target mode cannot be combined with fuzzy options.", result.Error);
    }

    [Fact]
    public void ResolveTarget_SourcePositionRejectsMultipleProjects()
    {
        CommandBuildResult result = NavlynToolCommandBuilder.ResolveTarget(
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
            projects: ["App", "App.Net10"],
            excludeGenerated: null,
            limit: null,
            candidatePolicy: null,
            minConfidence: null,
            explainSelection: null);

        Assert.False(result.IsValid);
        Assert.Equal("Source-position mode accepts at most one project.", result.Error);
    }

    [Fact]
    public void FuzzySymbolCommand_AboutForwardsLightProfileSearchBudget()
    {
        CommandBuildResult result = NavlynToolCommandBuilder.FuzzySymbolCommand(
            "about",
            query: null,
            candidateId: "sym:v1:00000000000000000000000000000000",
            assumeKind: null,
            assumeKinds: null,
            match: null,
            caseSensitive: null,
            project: null,
            projects: null,
            excludeGenerated: null,
            memberLimit: null,
            referenceLimit: null,
            relationLimit: null,
            include: null,
            limit: null,
            depth: null,
            includeSnippets: null,
            snippetLines: null,
            scope: "file",
            maxDocuments: 5,
            profile: "light",
            candidatePolicy: null,
            minConfidence: null,
            explainSelection: null);

        Assert.True(result.IsValid);
        Assert.Equal("about", result.Command);
        Assert.Equal(
            ["--candidate-id", "sym:v1:00000000000000000000000000000000", "--scope", "file", "--max-documents", "5", "--profile", "light"],
            result.Arguments);
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
        Assert.Equal(["--include-unstaged", "false", "--profile", "evidence"], result.Arguments);
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
    public void ContextPack_DiffModeRejectsFuzzySelectionOptions()
    {
        CommandBuildResult result = NavlynToolCommandBuilder.ContextPack(
            query: null,
            candidateId: null,
            diff: true,
            baseRef: null,
            head: null,
            staged: null,
            includeUnstaged: null,
            goal: null,
            changeKind: null,
            budgetTokens: null,
            itemLimit: null,
            snippetPolicy: null,
            snippetLines: null,
            candidateLimit: 5,
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
        Assert.Equal("Diff context-pack mode cannot be combined with fuzzy selection options.", result.Error);
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
    public void Batch_RejectsSingleDiagnosticsRequestWithFocusedToolGuidance()
    {
        using JsonDocument requests = JsonDocument.Parse("""[{"id":"diagnostics","command":"diagnostics","mode":"workspace"}]""");

        CommandBuildResult result = NavlynToolCommandBuilder.Batch(null, requests.RootElement);

        Assert.False(result.IsValid);
        Assert.Contains("navlyn_diagnostics", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("resolve-target", "navlyn_target")]
    [InlineData("symbol-source", "navlyn_read")]
    [InlineData("definition", "navlyn_navigate")]
    [InlineData("where-used", "navlyn_navigate")]
    [InlineData("review-diff", "navlyn_review")]
    [InlineData("di-graph", "navlyn_di")]
    [InlineData("related", "navlyn_impact")]
    [InlineData("package-usage", "navlyn_packages")]
    [InlineData("ef-model", "navlyn_ef")]
    [InlineData("route-map", "navlyn_routes")]
    [InlineData("options-graph", "navlyn_options")]
    [InlineData("where-handled", "navlyn_messages")]
    [InlineData("review-pack", "navlyn review-pack")]
    [InlineData("future-command", "navlyn future-command")]
    public void Batch_SingleRequestProvidesDeterministicFocusedToolOrCliGuidance(string command, string guidance)
    {
        using JsonDocument requests = JsonDocument.Parse($"[{{\"id\":\"single\",\"command\":\"{command}\"}}]");

        CommandBuildResult result = NavlynToolCommandBuilder.Batch(null, requests.RootElement);

        Assert.False(result.IsValid);
        Assert.Contains(guidance, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Batch_RejectsMissingEmptyAndNonArrayRequests()
    {
        CommandBuildResult missing = NavlynToolCommandBuilder.Batch(null, null);
        using JsonDocument emptyRequests = JsonDocument.Parse("[]");
        using JsonDocument nonArrayRequests = JsonDocument.Parse("{}");

        Assert.False(missing.IsValid);
        Assert.False(NavlynToolCommandBuilder.Batch(null, emptyRequests.RootElement).IsValid);
        Assert.False(NavlynToolCommandBuilder.Batch(null, nonArrayRequests.RootElement).IsValid);
    }

    [Fact]
    public void Batch_SerializesTwoRequestsUnchangedForStandardInput()
    {
        using JsonDocument defaults = JsonDocument.Parse("""{"project":"navlyn"}""");
        using JsonDocument requests = JsonDocument.Parse("""[{"id":"find-loader","command":"find","query":"WorkspaceLoader"},{"id":"source","command":"symbol-source","candidateIdFrom":"find-loader","view":"declaration"}]""");

        CommandBuildResult result = NavlynToolCommandBuilder.Batch(defaults.RootElement, requests.RootElement);

        Assert.True(result.IsValid);
        Assert.Equal("batch", result.Command);
        Assert.Equal([], result.Arguments);
        Assert.NotNull(result.StandardInput);
        using JsonDocument input = JsonDocument.Parse(result.StandardInput);
        Assert.Equal("navlyn", input.RootElement.GetProperty("defaults").GetProperty("project").GetString());
        Assert.Equal("find-loader", input.RootElement.GetProperty("requests")[0].GetProperty("id").GetString());
        Assert.Equal("source", input.RootElement.GetProperty("requests")[1].GetProperty("id").GetString());
        Assert.Equal(requests.RootElement.GetRawText(), input.RootElement.GetProperty("requests").GetRawText());
    }

    [Fact]
    public void AgentTargetPack_BuildsEditPreflightCommand()
    {
        CommandBuildResult result = NavlynToolCommandBuilder.AgentTargetPack(
            "edit-preflight",
            query: "DoctorCommand",
            candidateId: null,
            file: null,
            line: null,
            column: null,
            assumeKind: "NamedType",
            assumeKinds: null,
            match: null,
            caseSensitive: null,
            project: "Navlyn.CommandLine",
            projects: null,
            excludeGenerated: true,
            goal: "modify",
            changeKind: "behavior",
            budgetTokens: 3000,
            itemLimit: 8,
            referenceLimit: 20,
            testLimit: 10,
            candidateLimit: 5,
            candidatePolicy: null,
            minConfidence: null,
            explainSelection: null);

        Assert.True(result.IsValid);
        Assert.Equal("edit-preflight", result.Command);
        Assert.Equal(
            ["--query", "DoctorCommand", "--assume-kind", "NamedType", "--project", "Navlyn.CommandLine", "--limit", "5", "--exclude-generated", "--goal", "modify", "--change-kind", "behavior", "--budget-tokens", "3000", "--item-limit", "8", "--reference-limit", "20", "--test-limit", "10"],
            result.Arguments);
    }

    [Fact]
    public void PostEditGuard_RequiresOneAnchor()
    {
        CommandBuildResult result = NavlynToolCommandBuilder.PostEditGuard(
            candidateId: null,
            preflight: null,
            baseRef: null,
            head: null,
            staged: null,
            includeUnstaged: null,
            project: null,
            projects: null,
            excludeGenerated: null,
            symbolLimit: null,
            failOnRisk: null);

        Assert.False(result.IsValid);
        Assert.Equal("Specify exactly one anchor: candidateId or preflight.", result.Error);
    }

    [Fact]
    public void FileOutline_BuildsOutlineCommand()
    {
        CommandBuildResult result = NavlynToolCommandBuilder.FileOutline(
            file: "src/Service.cs",
            project: "App",
            excludeGenerated: true);

        Assert.True(result.IsValid);
        Assert.Equal("outline", result.Command);
        Assert.Equal(["--file", "src/Service.cs", "--project", "App", "--exclude-generated"], result.Arguments);
    }

    [Fact]
    public void SymbolSource_CandidateBuildsCliCommand()
    {
        CommandBuildResult result = NavlynToolCommandBuilder.SymbolSource(
            candidateId: "sym:v1:00000000000000000000000000000000",
            file: null,
            line: null,
            column: null,
            project: "App",
            excludeGenerated: true,
            view: "body",
            maxLines: 40,
            budgetTokens: 1200);

        Assert.True(result.IsValid);
        Assert.Equal("symbol-source", result.Command);
        Assert.Equal(
            ["--candidate-id", "sym:v1:00000000000000000000000000000000", "--view", "body", "--max-lines", "40", "--budget-tokens", "1200", "--project", "App", "--exclude-generated"],
            result.Arguments);
    }

    [Fact]
    public void CanonicalRead_MapsToReadCliCommand()
    {
        CommandBuildResult result = NavlynToolCommandBuilder.Read(
            candidateId: "sym:v1:00000000000000000000000000000000",
            file: null,
            line: null,
            column: null,
            project: null,
            excludeGenerated: null,
            view: "declaration",
            maxLines: null,
            budgetTokens: null);

        Assert.True(result.IsValid);
        Assert.Equal("read", result.Command);
        Assert.Equal(["--candidate-id", "sym:v1:00000000000000000000000000000000", "--view", "declaration"], result.Arguments);
    }

    [Fact]
    public void SymbolSource_RejectsMissingTarget()
    {
        CommandBuildResult result = NavlynToolCommandBuilder.SymbolSource(
            candidateId: null,
            file: "src/Service.cs",
            line: 12,
            column: null,
            project: null,
            excludeGenerated: null,
            view: null,
            maxLines: null,
            budgetTokens: null);

        Assert.False(result.IsValid);
        Assert.Equal("Specify exactly one target: candidateId or file with line and column.", result.Error);
    }

    [Fact]
    public void SymbolEdges_ReferencesForwardsFilters()
    {
        CommandBuildResult result = NavlynToolCommandBuilder.SymbolEdges(
            operation: "references",
            candidateId: "sym:v1:00000000000000000000000000000000",
            file: null,
            line: null,
            column: null,
            project: null,
            excludeGenerated: null,
            resultProject: null,
            resultProjects: ["App"],
            resultPath: null,
            resultPaths: ["src"],
            resultKind: null,
            resultKinds: ["Method"],
            usageKind: "invoke",
            usageKinds: null,
            groupBy: ["file"],
            limit: 25,
            includeMetadata: null);

        Assert.True(result.IsValid);
        Assert.Equal("references", result.Command);
        Assert.Equal(
            ["--candidate-id", "sym:v1:00000000000000000000000000000000", "--result-project", "App", "--result-path", "src", "--result-kind", "Method", "--usage-kind", "invoke", "--group-by", "file", "--limit", "25"],
            result.Arguments);
    }

    [Fact]
    public void SymbolEdges_ReferencesForwardsSearchBudget()
    {
        CommandBuildResult result = NavlynToolCommandBuilder.SymbolEdges(
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
            usageKinds: null,
            groupBy: null,
            limit: null,
            scope: "solution",
            maxDocuments: 25,
            includeMetadata: null);

        Assert.True(result.IsValid);
        Assert.Equal("references", result.Command);
        Assert.Equal(
            ["--candidate-id", "sym:v1:00000000000000000000000000000000", "--scope", "solution", "--max-documents", "25"],
            result.Arguments);
    }

    [Fact]
    public void SymbolEdges_RejectsNonEdgeOperation()
    {
        CommandBuildResult result = NavlynToolCommandBuilder.SymbolEdges(
            operation: "definition",
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
            usageKinds: null,
            groupBy: null,
            limit: null,
            includeMetadata: null);

        Assert.False(result.IsValid);
        Assert.Equal("operation must be one of: references, callers, calls, implementations.", result.Error);
    }

    [Fact]
    public void InspectFile_BuildsOutlineCommand()
    {
        CommandBuildResult result = NavlynToolCommandBuilder.InspectFile(
            file: "src/Service.cs",
            project: null,
            excludeGenerated: null);

        Assert.True(result.IsValid);
        Assert.Equal("outline", result.Command);
        Assert.Equal(["--file", "src/Service.cs"], result.Arguments);
    }

    [Fact]
    public void Navigate_CandidateDefinitionBuildsCliCommand()
    {
        CommandBuildResult result = NavlynToolCommandBuilder.Navigate(
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
    public void Navigate_TypeHierarchyMapsUnderscoreOperation()
    {
        CommandBuildResult result = NavlynToolCommandBuilder.Navigate(
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
    public void Navigate_RejectsMixedTargets()
    {
        CommandBuildResult result = NavlynToolCommandBuilder.Navigate(
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
    public void Navigate_RejectsResultFiltersForDefinition()
    {
        CommandBuildResult result = NavlynToolCommandBuilder.Navigate(
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
    public void Navigate_ReferencesForwardsUsageFiltersAndGrouping()
    {
        CommandBuildResult result = NavlynToolCommandBuilder.Navigate(
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
    public void Navigate_RejectsReferenceUsageFiltersForOtherOperations()
    {
        CommandBuildResult result = NavlynToolCommandBuilder.Navigate(
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
    public void Navigate_RejectsSearchBudgetForUnsupportedOperation()
    {
        CommandBuildResult result = NavlynToolCommandBuilder.Navigate(
            operation: "definition",
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
            usageKinds: null,
            groupBy: null,
            limit: null,
            scope: "solution",
            maxDocuments: null,
            includeMetadata: null);

        Assert.False(result.IsValid);
        Assert.Equal("scope and maxDocuments are supported only for references and callers.", result.Error);
    }

    [Theory]
    [InlineData("definition", "definition")]
    [InlineData("references", "references")]
    [InlineData("callers", "callers")]
    [InlineData("calls", "calls")]
    [InlineData("implementations", "implementations")]
    [InlineData("type_hierarchy", "type-hierarchy")]
    [InlineData("symbol_info", "symbol-info")]
    public void Navigate_MapsEachOperationToExistingCliCommand(string operation, string expectedCommand)
    {
        CommandBuildResult result = NavlynToolCommandBuilder.Navigate(
            operation,
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
            usageKinds: null,
            groupBy: null,
            limit: null,
            scope: null,
            maxDocuments: null,
            includeMetadata: null);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(expectedCommand, result.Command);
    }

    [Fact]
    public void Navigate_RejectsUnknownOperationBeforeExecution()
    {
        CommandBuildResult result = NavlynToolCommandBuilder.Navigate(
            "symbol_edges", "sym:v1:00000000000000000000000000000000", null, null, null,
            null, null, null, null, null, null, null, null, null, null, null, null,
            null, null, null);

        Assert.False(result.IsValid);
        Assert.Equal("operation must be one of: definition, references, callers, calls, implementations, type_hierarchy, symbol_info.", result.Error);
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
    public void TestsForSymbol_SourcePositionRejectsFuzzyOptions()
    {
        CommandBuildResult result = NavlynToolCommandBuilder.TestsForSymbol(
            query: null,
            candidateId: null,
            file: "Service.cs",
            line: 12,
            column: 8,
            assumeKind: "NamedType",
            assumeKinds: null,
            match: null,
            caseSensitive: null,
            project: null,
            projects: null,
            testProject: null,
            testProjects: null,
            excludeGenerated: null,
            candidateLimit: null,
            testLimit: null,
            referenceLimit: null,
            includeSnippets: null,
            snippetLines: null,
            candidatePolicy: null,
            minConfidence: null,
            explainSelection: null,
            profile: null);

        Assert.False(result.IsValid);
        Assert.Equal("Source-position tests-for-symbol mode cannot be combined with fuzzy options.", result.Error);
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
            ["--base", "main", "--head", "HEAD", "--include-unstaged", "false", "--project", "navlyn", "--test-project", "navlyn.Tests", "--symbol-limit", "10", "--snippet-lines", "0", "--profile", "compact", "--include-snippets"],
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
    public void DiImpact_SourcePositionRejectsFuzzyOptions()
    {
        CommandBuildResult result = NavlynToolCommandBuilder.DiImpact(
            query: null,
            candidateId: null,
            file: "Service.cs",
            line: 12,
            column: 8,
            assumeKind: null,
            assumeKinds: null,
            match: "contains",
            caseSensitive: null,
            project: null,
            projects: null,
            excludeGenerated: null,
            candidateLimit: null,
            registrationLimit: null,
            consumerLimit: null,
            dependencyLimit: null,
            riskLimit: null,
            depth: null,
            includeSnippets: null,
            snippetLines: null,
            candidatePolicy: null,
            minConfidence: null,
            explainSelection: null,
            profile: null);

        Assert.False(result.IsValid);
        Assert.Equal("Source-position di-impact mode cannot be combined with fuzzy options.", result.Error);
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

    private static CommandBuildResult BuildVerifyEdit(
        string? query = null,
        string? candidateId = null,
        string? preflight = null,
        string? file = null,
        int? line = null,
        int? column = null,
        string? assumeKind = null,
        string[]? assumeKinds = null,
        string? match = null,
        bool? caseSensitive = null,
        string? candidatePolicy = null)
    {
        return NavlynToolCommandBuilder.VerifyEdit(
            query,
            candidateId,
            preflight,
            file,
            line,
            column,
            assumeKind,
            assumeKinds,
            match,
            caseSensitive,
            baseRef: null,
            head: null,
            staged: null,
            includeUnstaged: null,
            project: null,
            projects: null,
            excludeGenerated: null,
            symbolLimit: null,
            failOnRisk: null,
            candidateLimit: null,
            candidatePolicy: candidatePolicy,
            minConfidence: null,
            explainSelection: null);
    }
}

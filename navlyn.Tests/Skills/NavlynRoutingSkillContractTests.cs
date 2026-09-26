using System.Text.RegularExpressions;

namespace Navlyn.Tests.Skills;

public sealed class NavlynRoutingSkillContractTests
{
    private static readonly string[] RequiredHeadings =
    [
        "Authority And Boundary",
        "Decide Whether Navlyn Is Needed",
        "Choose The First Tool",
        "Anchor, Follow Up, And Stop",
        "Edit And Review Loops",
        "Ambiguity, Freshness, And Failure",
        "Proof Boundaries",
        "References"
    ];

    private static readonly string[] ExpectedTools =
    [
        "navlyn_target", "navlyn_read", "navlyn_file_outline", "navlyn_navigate", "navlyn_prepare_edit",
        "navlyn_verify_edit", "navlyn_review", "navlyn_workspace_summary", "navlyn_workspace_status",
        "navlyn_workspace_refresh", "navlyn_doctor", "navlyn_impact", "navlyn_context_pack",
        "navlyn_entrypoints", "navlyn_tests_for_symbol", "navlyn_tests_for_diff", "navlyn_diagnostics",
        "navlyn_di", "navlyn_public_api_diff", "navlyn_routes", "navlyn_options", "navlyn_messages",
        "navlyn_ef", "navlyn_packages", "navlyn_batch"
    ];

    private static readonly string[] RemovedTools =
    [
        "navlyn_resolve_target", "navlyn_find_symbol", "navlyn_inspect_file", "navlyn_symbol_source",
        "navlyn_symbol_edges", "navlyn_about_symbol", "navlyn_related_files", "navlyn_exact_navigation",
        "navlyn_review_diff", "navlyn_edit_preflight", "navlyn_post_edit_guard", "navlyn_wrong_symbol_guard",
        "navlyn_change_intent_pack", "navlyn_agent_handoff_pack", "navlyn_confidence_ledger", "navlyn_di_impact"
    ];

    [Fact]
    public void SkillFilesExistAtTheExactPlanPaths()
    {
        string root = FindRepositoryRoot();
        Assert.True(File.Exists(Path.Combine(root, SkillPath)), $"Missing required skill file: {SkillPath}");
        Assert.True(File.Exists(Path.Combine(root, RoutingMatrixPath)), $"Missing required reference: {RoutingMatrixPath}");
        Assert.True(File.Exists(Path.Combine(root, EvidenceBoundariesPath)), $"Missing required reference: {EvidenceBoundariesPath}");
    }

    [Fact]
    public void SkillMetadataIsExact()
    {
        string skill = ReadSkill().Replace("\r\n", "\n", StringComparison.Ordinal);
        string expectedFrontmatter = """
---
name: navlyn-semantic-routing
description: Use Navlyn for semantic C# or Visual Basic decisions that require resolving a symbol or source position, selecting an overload, declaration, or binding context across projects or target frameworks, inspecting semantic structure or relationships, distinguishing partial, linked, or generated source, checking workspace freshness or diagnostics, assessing edit or Git-diff risk, or querying supported .NET domain facts. When one of these decisions is requested, load this skill before any repository action and follow its first-tool routing. Never load merely to locate or read a supplied literal or path, even inside source code; use one ordinary read or search for that, and honor explicit user exclusion. Reading a declared value from a named project file is the same ordinary-read case unless the value must guide a requested semantic binding decision.
---
""".Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.StartsWith(expectedFrontmatter, skill, StringComparison.Ordinal);
    }

    [Fact]
    public void MainSkillHasTheExactOrderedHeadingsAndWordBudget()
    {
        string body = GetMainBody(ReadSkill());
        int previous = -1;
        foreach (string heading in RequiredHeadings)
        {
            int index = body.IndexOf($"## {heading}", previous + 1, StringComparison.Ordinal);
            Assert.True(index > previous, $"Missing or out-of-order heading: {heading}");
            previous = index;
        }

        int wordCount = Regex.Matches(body, @"\b[\p{L}\p{N}][\p{L}\p{N}'’:-]*\b").Count;
        Assert.InRange(wordCount, 700, 1_100);
    }

    [Fact]
    public void MainSkillContainsRequiredRoutingRulesAndReferences()
    {
        string body = ReadSkill();
        string[] requiredPhrases =
        [
            "Explicit user instructions win",
            "ordinary reads and `rg`",
            "`navlyn_target`",
            "approximate symbol with a supplied project or target framework still starts with `navlyn_target`",
            "do not escalate it to `navlyn_context_pack`",
            "`navlyn_target(mode: \"list\")`",
            "`navlyn_read`",
            "`navlyn_file_outline`",
            "`navlyn_navigate`",
            "requested relationship with a supplied source position starts directly with `navlyn_navigate`",
            "do not pre-read the declaration or signature",
            "`candidateId`",
            "fail closed",
            "`navlyn_prepare_edit`",
            "edit outside Navlyn",
            "`navlyn_verify_edit`",
            "`navlyn_review`",
            "warnings",
            "confidence",
            "freshness",
            "partial",
            "truncated",
            "scope",
            "cost",
            "rerun hints",
            "Next actions are optional",
            "Stop when the current fact answers the question",
            "Do not claim runtime proof",
            "routing-matrix.md",
            "evidence-boundaries.md",
            "Read the [routing matrix]",
            "Read the [evidence boundaries]"
        ];
        foreach (string phrase in requiredPhrases)
        {
            Assert.Contains(phrase, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void SkillUsesOnlyCurrentToolsAndAvoidsRemovedNamesAndCapabilityClaims()
    {
        string skill = string.Join("\n", ReadSkill(), ReadReference(RoutingMatrixPath), ReadReference(EvidenceBoundariesPath));
        HashSet<string> allowed = ExpectedTools.ToHashSet(StringComparer.Ordinal);
        string[] usedTools = Regex.Matches(skill, @"\bnavlyn_[a-z_]+\b")
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Assert.NotEmpty(usedTools);
        Assert.All(usedTools, tool => Assert.Contains(tool, allowed));

        foreach (string removed in RemovedTools)
        {
            Assert.DoesNotContain(removed, skill, StringComparison.Ordinal);
        }

        string[] forbiddenClaims =
        [
            "Navlyn edits files",
            "Navlyn runs tests",
            "Navlyn formats files",
            "Navlyn publishes",
            "guarantees activation",
            "proves runtime behavior",
            "test results prove"
        ];
        foreach (string claim in forbiddenClaims)
        {
            Assert.DoesNotContain(claim, skill, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void SkillHasPositiveAndNegativeActivationCues()
    {
        string skill = ReadSkill();
        string[] positiveCues =
        [
            "symbol identity", "source position", "overloads", "partial declarations", "project",
            "target-framework", "relationships", "edit risk", "diagnostics", "Git diff", "supported .NET domain"
        ];
        string[] negativeCues =
        [
            "Text intent stays ordinary", "exact string", "comment", "document", "configuration value",
            "read a known file", "does not edit, build, test, format, publish"
        ];
        foreach (string cue in positiveCues.Concat(negativeCues))
        {
            Assert.Contains(cue, skill, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void RoutingMatrixCoversEveryTaskClassAndDecisionColumn()
    {
        string matrix = ReadReference(RoutingMatrixPath);
        string[] expectedHeaders =
        [
            "Task class", "Activation decision", "First action", "Required inputs", "Allowed follow-ups",
            "Stop condition", "Prohibited shortcut", "Permitted conclusion", "Prohibited conclusion"
        ];
        string[] expectedTaskClasses =
        [
            "Approximate name", "Exact source position", "Known-file outline", "Source reading", "Definition",
            "References", "Callers", "Calls", "Implementations", "Type hierarchy", "Overloads",
            "Partial declarations", "Multiple projects", "Multiple target frameworks", "Linked files", "Generated code",
            "Workspace/project/package/MSBuild facts", "Diagnostics", "Tests", "DI", "Routes", "Options/config binding",
            "Messages/handlers", "EF", "Packages", "Framework entrypoints", "Pre-edit", "Post-edit",
            "Actual diff review", "Bounded context escalation", "Batch optimization", "Text-only and ordinary-tool cases"
        ];

        Assert.True(TryParseRoutingMatrix(matrix, out string[] headers, out string[][] rows, out string error), error);
        Assert.Equal(expectedHeaders, headers);
        Assert.Equal(32, rows.Length);
        string[] actualTaskClasses = rows.Select(row => row[0]).ToArray();
        Assert.Equal(32, actualTaskClasses.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(
            expectedTaskClasses.OrderBy(value => value, StringComparer.OrdinalIgnoreCase),
            actualTaskClasses.OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
        Assert.All(rows, row =>
        {
            Assert.Equal(9, row.Length);
            Assert.All(row, cell => Assert.False(string.IsNullOrWhiteSpace(cell)));
        });
    }

    [Fact]
    public void RoutingMatrixParserRejectsAnIncompleteInMemoryRow()
    {
        const string incompleteTable = """
| Task class | Activation decision | First action | Required inputs | Allowed follow-ups | Stop condition | Prohibited shortcut | Permitted conclusion | Prohibited conclusion |
|---|---|---|---|---|---|---|---|---|
| Approximate name | Activate when semantic identity matters | `navlyn_target` |
""";

        Assert.False(TryParseRoutingMatrix(incompleteTable, out _, out _, out string error));
        Assert.Contains("exactly nine cells", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvidenceReferenceCoversEveryRequiredBoundary()
    {
        string boundaries = ReadReference(EvidenceBoundariesPath);
        string[] requiredBoundaries =
        [
            "confidence values", "ambiguity handling", "candidate freshness", "stale-ID behavior",
            "workspace cache freshness", "refresh triggers", "partial", "truncated", "generated-code distinction",
            "linked-file project context", "static", "runtime", "unavailable MCP fallback",
            "missing workspace", "ambiguous workspace", "explicit user override"
        ];
        foreach (string boundary in requiredBoundaries)
        {
            Assert.Contains(boundary, boundaries, StringComparison.OrdinalIgnoreCase);
        }
    }

    private const string SkillPath = ".agents/skills/navlyn-semantic-routing/SKILL.md";
    private const string RoutingMatrixPath = ".agents/skills/navlyn-semantic-routing/references/routing-matrix.md";
    private const string EvidenceBoundariesPath = ".agents/skills/navlyn-semantic-routing/references/evidence-boundaries.md";

    private static string ReadSkill() => File.ReadAllText(Path.Combine(FindRepositoryRoot(), SkillPath));

    private static string ReadReference(string path) => File.ReadAllText(Path.Combine(FindRepositoryRoot(), path));

    private static string GetMainBody(string skill)
    {
        string normalized = skill.Replace("\r\n", "\n", StringComparison.Ordinal);
        int bodyStart = normalized.IndexOf("\n---\n", StringComparison.Ordinal);
        Assert.True(bodyStart >= 0, "Skill frontmatter must end with a YAML delimiter.");
        return normalized[(bodyStart + 5)..];
    }

    private static bool TryParseRoutingMatrix(
        string markdown,
        out string[] headers,
        out string[][] dataRows,
        out string error)
    {
        headers = [];
        dataRows = [];
        string[] tableLines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.StartsWith('|') && line.EndsWith('|'))
            .ToArray();

        if (tableLines.Length < 3)
        {
            error = "Routing matrix must contain a header, separator, and at least one data row.";
            return false;
        }

        static string[] ParseCells(string line) => line.Trim().Trim('|')
            .Split('|')
            .Select(cell => cell.Trim())
            .ToArray();

        headers = ParseCells(tableLines[0]);
        if (headers.Length != 9 || headers.Any(string.IsNullOrWhiteSpace))
        {
            error = "Routing matrix header must contain exactly nine nonblank cells.";
            return false;
        }

        if (ParseCells(tableLines[1]).Length != 9)
        {
            error = "Routing matrix separator must contain exactly nine cells.";
            return false;
        }

        List<string[]> parsedRows = [];
        foreach (string line in tableLines.Skip(2))
        {
            string[] cells = ParseCells(line);
            if (cells.Length != 9)
            {
                error = "Each routing matrix data row must contain exactly nine cells.";
                return false;
            }

            if (cells.Any(string.IsNullOrWhiteSpace))
            {
                error = "Every routing matrix data cell must be nonblank.";
                return false;
            }

            parsedRows.Add(cells);
        }

        dataRows = parsedRows.ToArray();
        error = string.Empty;
        return true;
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

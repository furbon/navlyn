using Navlyn.Mcp.Prompts;

namespace Navlyn.Tests.Mcp;

public sealed class NavlynMcpPromptContractTests
{
    [Fact]
    public void Prompts_DescribeUnifiedSurfaceAndNeedTriggeredEscalation()
    {
        string understand = NavlynMcpPrompts.UnderstandSymbol(query: "CheckCommand");
        Assert.Contains("call navlyn_target", understand, StringComparison.Ordinal);
        Assert.Contains("Call navlyn_read", understand, StringComparison.Ordinal);
        Assert.Contains("navlyn_context_pack with goal understand only when", understand, StringComparison.Ordinal);
        Assert.Contains("only when normal file reads or smaller symbol facts are not enough", understand, StringComparison.Ordinal);
        Assert.DoesNotContain("navlyn_resolve_target", understand, StringComparison.Ordinal);
        Assert.DoesNotContain("navlyn_symbol_source", understand, StringComparison.Ordinal);
        Assert.DoesNotContain("--tool-profile", understand, StringComparison.Ordinal);

        string edit = NavlynMcpPrompts.PrepareEdit(query: "CheckCommand", changeKind: "behavior");
        Assert.Contains("unified read-only MCP surface", edit, StringComparison.Ordinal);
        Assert.Contains("Call navlyn_prepare_edit", edit, StringComparison.Ordinal);
        Assert.Contains("only when a bounded reading queue is still needed", edit, StringComparison.Ordinal);
        Assert.DoesNotContain("navlyn_resolve_target", edit, StringComparison.Ordinal);
        Assert.DoesNotContain("navlyn_edit_preflight", edit, StringComparison.Ordinal);
        Assert.DoesNotContain("--tool-profile", edit, StringComparison.Ordinal);

        string review = NavlynMcpPrompts.ReviewDiff(@base: "main", head: "HEAD", staged: null);
        Assert.Contains("unified read-only MCP surface", review, StringComparison.Ordinal);
        Assert.Contains("Call navlyn_review", review, StringComparison.Ordinal);
        Assert.Contains("Call navlyn_tests_for_diff only if test impact needs a smaller focused result", review, StringComparison.Ordinal);
        Assert.DoesNotContain("navlyn_review_diff with profile", review, StringComparison.Ordinal);
        Assert.DoesNotContain("--tool-profile", review, StringComparison.Ordinal);

        string diagnostic = NavlynMcpPrompts.FixDiagnostic(file: "Sample.cs", line: 1, column: 1, diagnosticId: "CS8602");
        Assert.Contains("Use navlyn_batch only", diagnostic, StringComparison.Ordinal);
        Assert.Contains("after deciding several facts are needed", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("--tool-profile", diagnostic, StringComparison.Ordinal);
    }
}

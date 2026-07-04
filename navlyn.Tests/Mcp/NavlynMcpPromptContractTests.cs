using Navlyn.Mcp.Prompts;

namespace Navlyn.Tests.Mcp;

public sealed class NavlynMcpPromptContractTests
{
    [Fact]
    public void Prompts_DescribeRequiredToolProfilesAndNeedTriggeredEscalation()
    {
        string understand = NavlynMcpPrompts.UnderstandSymbol(query: "CheckCommand");
        Assert.Contains("--tool-profile edit, review, or full", understand, StringComparison.Ordinal);
        Assert.Contains("only when normal file reads or smaller symbol facts are not enough", understand, StringComparison.Ordinal);

        string edit = NavlynMcpPrompts.PrepareEdit(query: "CheckCommand", changeKind: "behavior");
        Assert.Contains("--tool-profile edit", edit, StringComparison.Ordinal);
        Assert.Contains("only when a bounded reading queue is needed", edit, StringComparison.Ordinal);

        string review = NavlynMcpPrompts.ReviewDiff(@base: "main", head: "HEAD", staged: null);
        Assert.Contains("--tool-profile review", review, StringComparison.Ordinal);
        Assert.Contains("Call navlyn_tests_for_diff only if test impact needs a smaller focused result", review, StringComparison.Ordinal);

        string diagnostic = NavlynMcpPrompts.FixDiagnostic(file: "Sample.cs", line: 1, column: 1, diagnosticId: "CS8602");
        Assert.Contains("--tool-profile full", diagnostic, StringComparison.Ordinal);
        Assert.Contains("after deciding several facts are needed", diagnostic, StringComparison.Ordinal);
    }
}

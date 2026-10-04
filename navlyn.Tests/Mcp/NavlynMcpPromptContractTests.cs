using System.Text.RegularExpressions;
using Navlyn.Mcp.Prompts;
using Navlyn.Mcp.Tools;

namespace Navlyn.Tests.Mcp;

public sealed class NavlynMcpPromptContractTests
{
    [Fact]
    public void Prompts_ReferenceRegisteredToolsAndExplicitAdvancedSurface()
    {
        string[] prompts =
        [
            NavlynMcpPrompts.UnderstandSymbol(query: "CheckCommand"),
            NavlynMcpPrompts.PrepareEdit(query: "CheckCommand", changeKind: "behavior"),
            NavlynMcpPrompts.ReviewDiff(@base: "main", head: "HEAD", staged: null),
            NavlynMcpPrompts.FixDiagnostic(file: "Sample.cs", line: 1, column: 1, diagnosticId: "CS8602")
        ];
        HashSet<string> registered = new(NavlynMcpToolProfilePolicy.GetToolNames("full", "full"), StringComparer.Ordinal);
        foreach (string prompt in prompts)
        {
            Assert.Contains("--surface full", prompt, StringComparison.Ordinal);
            Assert.DoesNotContain("--tool-profile", prompt, StringComparison.Ordinal);
            foreach (Match reference in Regex.Matches(prompt, @"navlyn_[a-z_]+"))
                Assert.Contains(reference.Value, registered);
        }
    }
}

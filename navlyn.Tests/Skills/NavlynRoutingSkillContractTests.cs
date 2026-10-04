using System.Reflection;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using Navlyn.Mcp.Tools;

namespace Navlyn.Tests.Skills;

public sealed class NavlynRoutingSkillContractTests
{
    private const string SkillPath = ".agents/skills/navlyn-semantic-routing";

    [Fact]
    public void SkillHasDiscoverableMetadata()
    {
        string skill = Read("SKILL.md").Replace("\r\n", "\n", StringComparison.Ordinal);
        Match frontmatter = Regex.Match(skill, "\\A---\\n(?<metadata>.*?)\\n---\\n", RegexOptions.Singleline);
        Assert.True(frontmatter.Success, "Skill must have YAML frontmatter.");
        string metadata = frontmatter.Groups["metadata"].Value;
        Assert.Matches(@"(?m)^name: navlyn-semantic-routing$", metadata);
        Assert.Matches(@"(?m)^description: \S.+$", metadata);
    }

    [Theory]
    [InlineData("SKILL.md")]
    [InlineData("references/routing-matrix.md")]
    [InlineData("references/evidence-boundaries.md")]
    public void SkillResourcesReferenceRegisteredTools(string resource)
    {
        HashSet<string> registered = typeof(NavlynMcpTools).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(method => method.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        string text = Read(resource);
        Assert.False(string.IsNullOrWhiteSpace(text));
        foreach (Match tool in Regex.Matches(text, @"\bnavlyn_[a-z_]+\b"))
        {
            Assert.Contains(tool.Value, registered);
        }
    }

    [Fact]
    public void SkillLinksTheInstalledReferences()
    {
        string skill = Read("SKILL.md");
        string[] links = Regex.Matches(skill, @"\]\((references/[^)]+)\)")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(new[] { "references/evidence-boundaries.md", "references/routing-matrix.md" }, links);
        Assert.All(links, link => Assert.False(string.IsNullOrWhiteSpace(Read(link))));
    }

    private static string Read(string resource) => File.ReadAllText(Path.Combine(FindRepositoryRoot(), SkillPath, resource));

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
        throw new InvalidOperationException("Repository root not found.");
    }
}

using Navlyn.Symbols;

namespace Navlyn.Tests.Symbols;

public sealed class SymbolNameMatcherTests
{
    [Fact]
    public void IsMatch_ContainsMode_UsesOrdinalIgnoreCaseByDefault()
    {
        SymbolNameMatcher matcher = CreateMatcher(new SymbolSearchOptions(
            Query: "widget",
            MatchMode: SymbolMatchMode.Contains,
            CaseSensitive: false));

        Assert.True(matcher.IsMatch("DefaultWidgetFormatter"));
    }

    [Fact]
    public void IsMatch_ExactMode_RespectsCaseSensitivity()
    {
        SymbolNameMatcher matcher = CreateMatcher(new SymbolSearchOptions(
            Query: "Widget",
            MatchMode: SymbolMatchMode.Exact,
            CaseSensitive: true));

        Assert.True(matcher.IsMatch("Widget"));
        Assert.False(matcher.IsMatch("widget"));
    }

    [Fact]
    public void TryCreate_RegexMode_ReturnsDiagnosticTextForInvalidPattern()
    {
        bool created = SymbolNameMatcher.TryCreate(
            new SymbolSearchOptions(Query: "[", MatchMode: SymbolMatchMode.Regex, CaseSensitive: false),
            out SymbolNameMatcher matcher,
            out string? errorMessage);

        Assert.False(created);
        Assert.Null(matcher);
        Assert.Contains("Invalid --query regular expression:", errorMessage);
    }

    [Fact]
    public void IsMatch_RegexMode_UsesConfiguredCaseSensitivity()
    {
        SymbolNameMatcher matcher = CreateMatcher(new SymbolSearchOptions(
            Query: "^widget$",
            MatchMode: SymbolMatchMode.Regex,
            CaseSensitive: false));

        Assert.True(matcher.IsMatch("Widget"));
    }

    private static SymbolNameMatcher CreateMatcher(SymbolSearchOptions options)
    {
        bool created = SymbolNameMatcher.TryCreate(options, out SymbolNameMatcher matcher, out string? errorMessage);

        Assert.True(created, errorMessage);
        Assert.Null(errorMessage);
        return matcher;
    }
}

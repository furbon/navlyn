using Navlyn.Cli.Commands;

namespace Navlyn.Tests.Cli.Commands;

public sealed class FuzzyCommandSupportTests
{
    [Fact]
    public void ParseInclude_EmptyInput_ReturnsDefaults()
    {
        string[] defaults = ["declarations", "references"];

        IReadOnlyList<string> include = FuzzyCommandSupport.ParseInclude(" ", defaults);

        Assert.Same(defaults, include);
    }

    [Fact]
    public void ParseInclude_TrimsDeduplicatesAndSortsValues()
    {
        IReadOnlyList<string> include = FuzzyCommandSupport.ParseInclude(
            "references, declarations,references",
            ["calls"]);

        Assert.Equal(["declarations", "references"], include);
    }

    [Fact]
    public void ValidateInclude_AcceptsKnownModes()
    {
        bool valid = FuzzyCommandSupport.ValidateInclude(
            ["declarations", "references", "callers", "calls", "implementations", "hierarchy"],
            out string? error);

        Assert.True(valid);
        Assert.Null(error);
    }

    [Fact]
    public void ValidateInclude_RejectsUnknownMode()
    {
        bool valid = FuzzyCommandSupport.ValidateInclude(["declarations", "unknown"], out string? error);

        Assert.False(valid);
        Assert.Equal("Unknown include mode: unknown.", error);
    }
}

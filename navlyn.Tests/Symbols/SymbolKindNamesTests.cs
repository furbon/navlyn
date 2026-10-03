using Navlyn.Symbols;

namespace Navlyn.Tests.Symbols;

public sealed class SymbolKindNamesTests
{
    [Theory]
    [InlineData("class", "NamedType")]
    [InlineData("INTERFACE", "NamedType")]
    [InlineData("struct", "NamedType")]
    [InlineData("record", "NamedType")]
    [InlineData("enum", "NamedType")]
    [InlineData("delegate", "NamedType")]
    [InlineData("type", "NamedType")]
    [InlineData("namedtype", "NamedType")]
    [InlineData(" method ", "Method")]
    [InlineData("PROPERTY", "Property")]
    [InlineData("field", "Field")]
    public void TryNormalize_AcceptsNaturalNames(string input, string expected)
    {
        Assert.True(SymbolKindNames.TryNormalize(input, out string kind));
        Assert.Equal(expected, kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("1")]
    [InlineData("Unknown")]
    [InlineData("Method,Field")]
    public void TryNormalize_RejectsInvalidNames(string? input)
    {
        Assert.False(SymbolKindNames.TryNormalize(input, out _));
    }

    [Fact]
    public void NormalizeMany_ReturnsDistinctCanonicalKinds()
    {
        Assert.Equal(["Method", "NamedType"], SymbolKindNames.NormalizeMany(["class", "interface", "NamedType", "method", "METHOD"]));
    }
}

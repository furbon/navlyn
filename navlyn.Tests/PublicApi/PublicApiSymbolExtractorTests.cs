using Navlyn.PublicApi;

namespace Navlyn.Tests.PublicApi;

public sealed class PublicApiSymbolExtractorTests
{
    [Fact]
    public void Extract_PublicSurface_ReturnsTypesMembersAndInterfaceMembers()
    {
        const string source = """
            namespace Sample;

            public interface IWidget
            {
                string Name { get; }
                void Render(int count = 1);
            }

            public class Widget<T> where T : class
            {
                [Obsolete("old")]
                public string? Name { get; init; }
                protected virtual int Count(int value) => value;
                internal void Hidden() { }
            }
            """;

        IReadOnlyList<PublicApiSymbol> symbols = new PublicApiSymbolExtractor().Extract(source, "src/Widget.cs", "net10.0", excludeGenerated: false);

        Assert.Contains(symbols, symbol => symbol.Kind == "NamedType" && symbol.Name == "IWidget");
        Assert.Contains(symbols, symbol => symbol.Kind == "Property" && symbol.Name == "Name" && symbol.Container == "Sample.IWidget");
        Assert.Contains(symbols, symbol => symbol.Kind == "Method" && symbol.Name == "Render" && symbol.DefaultValues.Contains("count=1"));
        Assert.Contains(symbols, symbol => symbol.Kind == "Property" && symbol.Name == "Name" && symbol.NullableAnnotations.Contains("annotated") && symbol.Attributes.Contains("Obsolete(\"old\")"));
        Assert.Contains(symbols, symbol => symbol.Kind == "Method" && symbol.Name == "Count" && symbol.Accessibility == "Protected" && symbol.Modifiers.Contains("virtual"));
        Assert.DoesNotContain(symbols, symbol => symbol.Name == "Hidden");
    }

    [Fact]
    public void Extract_PublicEnum_ReturnsEnumMembers()
    {
        const string source = """
            namespace Sample;
            public enum Mode
            {
                Fast = 1,
                Slow = 2
            }
            """;

        IReadOnlyList<PublicApiSymbol> symbols = new PublicApiSymbolExtractor().Extract(source, "src/Mode.cs", targetFramework: null, excludeGenerated: false);

        Assert.Contains(symbols, symbol => symbol.Kind == "NamedType" && symbol.Name == "Mode");
        Assert.Contains(symbols, symbol => symbol.Kind == "EnumMember" && symbol.Name == "Fast" && symbol.Signature.EndsWith("Fast = 1", StringComparison.Ordinal));
        Assert.Contains(symbols, symbol => symbol.Kind == "EnumMember" && symbol.Name == "Slow" && symbol.Signature.EndsWith("Slow = 2", StringComparison.Ordinal));
    }
}


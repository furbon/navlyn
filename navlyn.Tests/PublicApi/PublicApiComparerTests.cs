using Navlyn.PublicApi;

namespace Navlyn.Tests.PublicApi;

public sealed class PublicApiComparerTests
{
    [Fact]
    public void Compare_RemovedPublicMember_ReturnsSourceAndBinaryBreaking()
    {
        PublicApiSnapshot before = Snapshot("""
            namespace Sample;
            public class Widget
            {
                public void Removed() { }
            }
            """);
        PublicApiSnapshot after = Snapshot("""
            namespace Sample;
            public class Widget
            {
            }
            """);

        PublicApiChangesSection changes = new PublicApiComparer().Compare(before, after, includeAdditions: true, includeAttributes: true, changeLimit: 20);

        PublicApiChange change = Assert.Single(changes.Items, change => change.Code == "public-member-removed");
        Assert.Equal("breaking", change.SourceCompatibility.Risk);
        Assert.Equal("breaking", change.BinaryCompatibility.Risk);
    }

    [Fact]
    public void Compare_MethodParameterTypeChange_ReturnsSignatureChange()
    {
        PublicApiSnapshot before = Snapshot("""
            namespace Sample;
            public class Widget
            {
                public void Format(int value) { }
            }
            """);
        PublicApiSnapshot after = Snapshot("""
            namespace Sample;
            public class Widget
            {
                public void Format(string value) { }
            }
            """);

        PublicApiChangesSection changes = new PublicApiComparer().Compare(before, after, includeAdditions: true, includeAttributes: true, changeLimit: 20);

        PublicApiChange change = Assert.Single(changes.Items, change => change.Code == "public-signature-changed");
        Assert.Equal("breaking", change.SourceCompatibility.Risk);
        Assert.Equal("breaking", change.BinaryCompatibility.Risk);
    }

    [Fact]
    public void Compare_InterfaceMemberAddition_ReturnsBreakingAddition()
    {
        PublicApiSnapshot before = Snapshot("""
            namespace Sample;
            public interface IWidget
            {
            }
            """);
        PublicApiSnapshot after = Snapshot("""
            namespace Sample;
            public interface IWidget
            {
                void Added();
            }
            """);

        PublicApiChangesSection changes = new PublicApiComparer().Compare(before, after, includeAdditions: true, includeAttributes: true, changeLimit: 20);

        PublicApiChange change = Assert.Single(changes.Items, change => change.Code == "interface-member-added");
        Assert.Equal("breaking", change.SourceCompatibility.Risk);
        Assert.Equal("breaking", change.BinaryCompatibility.Risk);
    }

    [Fact]
    public void Compare_AttributeDefaultAndNullableChanges_ReturnsSpecificCodes()
    {
        PublicApiSnapshot before = Snapshot("""
            namespace Sample;
            public class Widget
            {
                [Obsolete("old")]
                public string Name(int count = 1) => "";
            }
            """);
        PublicApiSnapshot after = Snapshot("""
            namespace Sample;
            public class Widget
            {
                [Obsolete("new")]
                public string? Name(int count = 2) => "";
            }
            """);

        PublicApiChangesSection changes = new PublicApiComparer().Compare(before, after, includeAdditions: true, includeAttributes: true, changeLimit: 20);

        Assert.Contains(changes.Items, change => change.Code == "nullable-annotation-changed");
        Assert.Contains(changes.Items, change => change.Code == "default-parameter-changed");
        Assert.Contains(changes.Items, change => change.Code == "attribute-argument-changed");
    }

    private static PublicApiSnapshot Snapshot(string source)
    {
        IReadOnlyList<PublicApiSymbol> symbols = new PublicApiSymbolExtractor().Extract(source, "src/Widget.cs", targetFramework: null, excludeGenerated: false);
        return new PublicApiSnapshot("test", "test", symbols, Truncated: false, Warnings: []);
    }
}

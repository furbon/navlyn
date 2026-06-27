using Navlyn.ContextPacks;

namespace Navlyn.Tests.ContextPacks;

public sealed class ContextPackBudgeterTests
{
    [Fact]
    public void Apply_TruncatesByItemLimitDeterministically()
    {
        IReadOnlyList<ContextPackItem> items =
        [
            Item("a", "definition", "alpha"),
            Item("b", "reference", "beta"),
            Item("c", "diagnostic", "gamma")
        ];

        ContextPackBudgetResult result = new ContextPackBudgeter().Apply(
            items,
            budgetTokens: 100,
            itemLimit: 2,
            omittedNextAction: null);

        Assert.False(result.Budget.Truncated);
        Assert.True(result.Truncated);
        Assert.Equal(["a", "b"], result.Items.Select(item => item.Id));
        ContextPackOmitted omitted = Assert.Single(result.Omitted);
        Assert.Equal("item-limit", omitted.Reason);
        Assert.Equal("c", omitted.FirstOmittedItemId);
    }

    [Fact]
    public void Apply_TruncatesByBudgetAndDropsContentFromFirstOmittedItem()
    {
        IReadOnlyList<ContextPackItem> items =
        [
            Item("a", "definition", "ok"),
            Item("b", "reference", "abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyz")
        ];

        ContextPackBudgetResult result = new ContextPackBudgeter().Apply(
            items,
            budgetTokens: 10,
            itemLimit: 10,
            omittedNextAction: null);

        Assert.True(result.Budget.Truncated);
        Assert.True(result.Truncated);
        Assert.Equal(2, result.Items.Count);
        Assert.NotNull(result.Items[0].Content);
        Assert.Null(result.Items[1].Content);
        Assert.Equal("b", Assert.Single(result.Omitted).FirstOmittedItemId);
    }

    private static ContextPackItem Item(string id, string kind, string line)
    {
        return new ContextPackItem(
            Id: id,
            Kind: kind,
            Priority: 0,
            ReasonCodes: ["test"],
            Symbol: null,
            SourceLocation: new ContextPackSourceLocation("file.cs", 1, 1, 1, 2),
            Content: new ContextPackItemContent("line", 1, 1, [line]),
            EstimatedTokens: 0);
    }
}

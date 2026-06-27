using Navlyn.Cli.Commands;

namespace Navlyn.Tests.Cli.Commands;

public sealed class NavigationResultOptionsTests
{
    [Fact]
    public void ApplyLimit_NoLimit_ReturnsOriginalItems()
    {
        string[] items = ["a", "b", "c"];

        IReadOnlyList<string> result = NavigationResultOptions.ApplyLimit(items, limit: null);

        Assert.Same(items, result);
    }

    [Fact]
    public void ApplyLimit_Limit_ReturnsLeadingItems()
    {
        IReadOnlyList<string> result = NavigationResultOptions.ApplyLimit(["a", "b", "c"], 2);

        Assert.Equal(["a", "b"], result);
    }
}

namespace Navlyn.ContextPacks;

internal sealed class ContextPackBudgeter
{
    public const string Estimator = "chars-div-4-v1";

    public ContextPackBudgetResult Apply(
        IReadOnlyList<ContextPackItem> rankedItems,
        int budgetTokens,
        int itemLimit,
        ContextPackNextAction? omittedNextAction)
    {
        int charLimit = budgetTokens * 4;
        int usedChars = 0;
        List<ContextPackItem> included = [];
        ContextPackItem? firstBudgetOmitted = null;

        foreach (ContextPackItem item in rankedItems)
        {
            if (included.Count >= itemLimit)
            {
                break;
            }

            int itemChars = CountMaterialChars(item);
            if (usedChars + itemChars > charLimit)
            {
                firstBudgetOmitted ??= item;
                included.Add(item with
                {
                    Content = null,
                    EstimatedTokens = 0
                });
                break;
            }

            usedChars += itemChars;
            included.Add(item with
            {
                EstimatedTokens = EstimateTokens(itemChars)
            });
        }

        bool itemLimitTruncated = rankedItems.Count > included.Count && included.Count >= itemLimit;
        bool budgetTruncated = firstBudgetOmitted is not null;
        List<ContextPackOmitted> omitted = [];
        if (budgetTruncated)
        {
            omitted.Add(new ContextPackOmitted(
                Reason: "budget-exhausted",
                Kind: firstBudgetOmitted!.Kind,
                TotalItems: rankedItems.Count - included.Count + 1,
                FirstOmittedItemId: firstBudgetOmitted.Id,
                NextAction: omittedNextAction));
        }
        else if (itemLimitTruncated)
        {
            ContextPackItem firstOmitted = rankedItems[included.Count];
            omitted.Add(new ContextPackOmitted(
                Reason: "item-limit",
                Kind: firstOmitted.Kind,
                TotalItems: rankedItems.Count - included.Count,
                FirstOmittedItemId: firstOmitted.Id,
                NextAction: omittedNextAction));
        }

        ContextPackBudget budget = new(
            RequestedTokens: budgetTokens,
            Estimator,
            CharLimit: charLimit,
            EstimatedTokensUsed: EstimateTokens(usedChars),
            CharsUsed: usedChars,
            Truncated: budgetTruncated);

        return new ContextPackBudgetResult(
            budget,
            included,
            omitted,
            budgetTruncated || itemLimitTruncated);
    }

    public static int EstimateTokens(int charCount)
    {
        return (int)Math.Ceiling(charCount / 4.0);
    }

    public static int CountMaterialChars(ContextPackItem item)
    {
        int count = item.Content?.Lines.Sum(line => line.Length) ?? 0;
        count += item.SourceLocation?.Path.Length ?? 0;
        count += item.Kind.Length;
        foreach (string reason in item.ReasonCodes)
        {
            count += reason.Length;
        }

        return count;
    }
}

internal sealed record ContextPackBudgetResult(
    ContextPackBudget Budget,
    IReadOnlyList<ContextPackItem> Items,
    IReadOnlyList<ContextPackOmitted> Omitted,
    bool Truncated);

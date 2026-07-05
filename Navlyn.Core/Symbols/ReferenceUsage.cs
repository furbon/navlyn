using Microsoft.CodeAnalysis;
using Navlyn.Languages;

namespace Navlyn.Symbols;

internal static class ReferenceUsageTaxonomy
{
    public static readonly IReadOnlyList<string> UsageKinds =
    [
        "read",
        "write",
        "invoke",
        "construct",
        "inherit",
        "implement",
        "override",
        "attribute",
        "nameof",
        "typeof"
    ];

    public static readonly IReadOnlyList<string> GroupKinds =
    [
        "file",
        "project",
        "containing-symbol",
        "usage-kind",
        "test-vs-production"
    ];

    public static bool TryNormalizeUsageKinds(
        IReadOnlyList<string> values,
        out IReadOnlyList<string> usageKinds,
        out string? error)
    {
        return TryNormalize(values, UsageKinds, "usage kind", out usageKinds, out error);
    }

    public static bool TryNormalizeGroupKinds(
        IReadOnlyList<string> values,
        out IReadOnlyList<string> groupKinds,
        out string? error)
    {
        return TryNormalize(values, GroupKinds, "group-by value", out groupKinds, out error);
    }

    public static IReadOnlyList<ReferenceUsageCount> CreateCounts(IReadOnlyList<SymbolReferenceLocation> references)
    {
        return [.. references
            .GroupBy(reference => reference.UsageKind, StringComparer.Ordinal)
            .Select(group => new ReferenceUsageCount(group.Key, group.Count()))
            .OrderBy(count => UsageKindOrder(count.UsageKind))
            .ThenBy(count => count.UsageKind, StringComparer.Ordinal)];
    }

    public static IReadOnlyList<ReferenceUsageGroup> CreateGroups(
        IReadOnlyList<SymbolReferenceLocation> references,
        IReadOnlyList<string> groupKinds)
    {
        List<ReferenceUsageGroup> groups = [];
        foreach (string groupKind in groupKinds)
        {
            IEnumerable<IGrouping<string, SymbolReferenceLocation>> grouped = groupKind switch
            {
                "file" => references.GroupBy(reference => reference.Path, StringComparer.Ordinal),
                "project" => references.GroupBy(reference => reference.ProjectName ?? "unknown", StringComparer.Ordinal),
                "containing-symbol" => references.GroupBy(reference => ContainingSymbolKey(reference), StringComparer.Ordinal),
                "usage-kind" => references.GroupBy(reference => reference.UsageKind, StringComparer.Ordinal),
                "test-vs-production" => references.GroupBy(reference => reference.ProjectKind, StringComparer.Ordinal),
                _ => []
            };

            groups.AddRange(grouped
                .Select(group => new ReferenceUsageGroup(
                    GroupBy: groupKind,
                    Key: group.Key,
                    ReferenceCount: group.Count(),
                    UsageKindCounts: CreateCounts([.. group]),
                    FirstReference: CreateFirstReference(group)))
                .OrderBy(group => GroupKindOrder(group.GroupBy))
                .ThenBy(group => group.Key, StringComparer.Ordinal));
        }

        return groups;
    }

    internal static int UsageKindOrder(string usageKind)
    {
        int index = IndexOf(UsageKinds, usageKind);
        return index < 0 ? 100 : index;
    }

    private static bool TryNormalize(
        IReadOnlyList<string> values,
        IReadOnlyList<string> allowed,
        string label,
        out IReadOnlyList<string> normalized,
        out string? error)
    {
        List<string> result = [];
        foreach (string value in values.SelectMany(SplitValues))
        {
            if (!allowed.Contains(value, StringComparer.Ordinal))
            {
                normalized = [];
                error = $"Invalid {label}: {value}. Allowed values: {string.Join(", ", allowed)}.";
                return false;
            }

            result.Add(value);
        }

        normalized = [.. result.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal)];
        error = null;
        return true;
    }

    private static IEnumerable<string> SplitValues(string value)
    {
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => part.Length > 0);
    }

    private static int GroupKindOrder(string groupKind)
    {
        int index = IndexOf(GroupKinds, groupKind);
        return index < 0 ? 100 : index;
    }

    private static int IndexOf(IReadOnlyList<string> values, string value)
    {
        for (int i = 0; i < values.Count; i++)
        {
            if (string.Equals(values[i], value, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static string ContainingSymbolKey(SymbolReferenceLocation reference)
    {
        if (reference.ContainingSymbol is null)
        {
            return "unknown";
        }

        string container = string.IsNullOrWhiteSpace(reference.ContainingSymbol.Container)
            ? reference.ContainingSymbol.Name
            : $"{reference.ContainingSymbol.Container}.{reference.ContainingSymbol.Name}";
        return $"{reference.ContainingSymbol.Kind}:{container}";
    }

    private static ReferenceUsageFirstLocation CreateFirstReference(IEnumerable<SymbolReferenceLocation> references)
    {
        SymbolReferenceLocation first = references
            .OrderBy(reference => reference.Path, StringComparer.Ordinal)
            .ThenBy(reference => reference.Line)
            .ThenBy(reference => reference.Column)
            .ThenBy(reference => reference.EndLine)
            .ThenBy(reference => reference.EndColumn)
            .First();
        return new ReferenceUsageFirstLocation(
            first.Path,
            first.Line,
            first.Column,
            first.EndLine,
            first.EndColumn);
    }
}

internal static class ReferenceUsageClassifier
{
    public static string Classify(
        SemanticModel semanticModel,
        Location location,
        ISymbol selectedSymbol,
        CancellationToken cancellationToken)
    {
        SyntaxNode root = semanticModel.SyntaxTree.GetRoot(cancellationToken);
        SyntaxNode node = root.FindNode(location.SourceSpan, getInnermostNodeForTie: true);

        if (node.AncestorsAndSelf().Any(IsAttributeNode))
        {
            return "attribute";
        }

        if (node.AncestorsAndSelf().Any(IsTypeOfNode))
        {
            return "typeof";
        }

        if (IsInsideNameOf(node))
        {
            return "nameof";
        }

        if (node.AncestorsAndSelf().Any(IsBaseOrImplementsNode))
        {
            return selectedSymbol is INamedTypeSymbol { TypeKind: TypeKind.Interface }
                ? "implement"
                : "inherit";
        }

        if (IsOverrideDeclaration(node))
        {
            return "override";
        }

        if (IsObjectCreation(node))
        {
            return "construct";
        }

        if (IsWrite(node))
        {
            return "write";
        }

        if (IsInvocation(node))
        {
            return "invoke";
        }

        return "read";
    }

    public static string ClassifyProject(string projectName, string? projectPath)
    {
        return ContainsTestToken(projectName) || projectPath is not null && ContainsTestToken(projectPath)
            ? "test"
            : "production";
    }

    private static bool IsInsideNameOf(SyntaxNode node)
    {
        return node.AncestorsAndSelf()
            .Any(ancestor =>
                ancestor.GetType().Name == "InvocationExpressionSyntax" &&
                ancestor.ToString().TrimStart().StartsWith("nameof", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsOverrideDeclaration(SyntaxNode node)
    {
        return node.AncestorsAndSelf()
            .Any(ancestor => SourceLanguageFacts.GetModifierTexts(ancestor)
                .Any(modifier => modifier.Equals("override", StringComparison.OrdinalIgnoreCase) ||
                    modifier.Equals("overrides", StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsObjectCreation(SyntaxNode node)
    {
        return node.AncestorsAndSelf().Any(ancestor =>
            ancestor.GetType().Name is "ObjectCreationExpressionSyntax" or "ImplicitObjectCreationExpressionSyntax");
    }

    private static bool IsWrite(SyntaxNode node)
    {
        foreach (SyntaxNode ancestor in node.AncestorsAndSelf())
        {
            if (ancestor.GetType().Name is "PrefixUnaryExpressionSyntax" or "PostfixUnaryExpressionSyntax")
            {
                return true;
            }

            if (ancestor.GetType().Name is "AssignmentExpressionSyntax" or "AssignmentStatementSyntax")
            {
                return IsWithinAssignmentLeft(node, ancestor);
            }
        }

        return false;
    }

    private static bool IsWithinAssignmentLeft(SyntaxNode node, SyntaxNode assignment)
    {
        if (assignment.GetType().GetProperty("Left")?.GetValue(assignment) is not SyntaxNode left)
        {
            return false;
        }

        return left.Span.Contains(node.SpanStart) && node.Span.End <= left.Span.End;
    }

    private static bool IsInvocation(SyntaxNode node)
    {
        return node.AncestorsAndSelf().Any(ancestor => ancestor.GetType().Name == "InvocationExpressionSyntax");
    }

    private static bool IsAttributeNode(SyntaxNode node)
    {
        return node.GetType().Name == "AttributeSyntax";
    }

    private static bool IsTypeOfNode(SyntaxNode node)
    {
        return node.GetType().Name is "TypeOfExpressionSyntax" or "GetTypeExpressionSyntax";
    }

    private static bool IsBaseOrImplementsNode(SyntaxNode node)
    {
        return node.GetType().Name is "BaseListSyntax" or "InheritsStatementSyntax" or "ImplementsStatementSyntax";
    }

    private static bool ContainsTestToken(string value)
    {
        return value.Contains("test", StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed record ReferenceUsageCount(string UsageKind, int Count);

internal sealed record ReferenceUsageGroup(
    string GroupBy,
    string Key,
    int ReferenceCount,
    IReadOnlyList<ReferenceUsageCount> UsageKindCounts,
    ReferenceUsageFirstLocation FirstReference);

internal sealed record ReferenceUsageFirstLocation(
    string Path,
    int Line,
    int Column,
    int EndLine,
    int EndColumn);

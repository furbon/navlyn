namespace Navlyn.PublicApi;

internal sealed class PublicApiComparer
{
    public PublicApiChangesSection Compare(
        PublicApiSnapshot baseSnapshot,
        PublicApiSnapshot headSnapshot,
        bool includeAdditions,
        bool includeAttributes,
        int changeLimit)
    {
        Dictionary<string, PublicApiSymbol> baseById = baseSnapshot.Symbols
            .GroupBy(symbol => symbol.DocumentationCommentId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        Dictionary<string, PublicApiSymbol> headById = headSnapshot.Symbols
            .GroupBy(symbol => symbol.DocumentationCommentId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        List<PublicApiChange> changes = [];
        HashSet<string> matchedBase = new(StringComparer.Ordinal);
        HashSet<string> matchedHead = new(StringComparer.Ordinal);

        foreach ((string id, PublicApiSymbol before) in baseById.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (!headById.TryGetValue(id, out PublicApiSymbol? after))
            {
                continue;
            }

            matchedBase.Add(id);
            matchedHead.Add(id);
            changes.AddRange(CompareMatched(before, after, includeAttributes));
        }

        foreach (PublicApiSymbol before in baseById.Values.Where(symbol => !matchedBase.Contains(symbol.DocumentationCommentId)))
        {
            PublicApiSymbol? after = FindSignatureCandidate(before, headById.Values.Where(symbol => !matchedHead.Contains(symbol.DocumentationCommentId)));
            if (after is not null)
            {
                matchedBase.Add(before.DocumentationCommentId);
                matchedHead.Add(after.DocumentationCommentId);
                changes.Add(CreateChange(
                    code: "public-signature-changed",
                    kind: "change",
                    symbol: after,
                    before,
                    after,
                    sourceRisk: "breaking",
                    binaryRisk: "breaking",
                    confidence: "high",
                    reasonCodes: ["public-signature-changed"]));
            }
        }

        foreach (PublicApiSymbol before in baseById.Values.Where(symbol => !matchedBase.Contains(symbol.DocumentationCommentId)))
        {
            changes.Add(CreateChange(
                RemovalCode(before),
                "removal",
                before,
                before,
                after: null,
                sourceRisk: "breaking",
                binaryRisk: "breaking",
                confidence: "high",
                reasonCodes: [RemovalCode(before)]));
        }

        if (includeAdditions)
        {
            foreach (PublicApiSymbol after in headById.Values.Where(symbol => !matchedHead.Contains(symbol.DocumentationCommentId)))
            {
                bool interfaceMemberAddition = after.Container is not null &&
                    after.Kind is not "NamedType" &&
                    headById.Values.Any(symbol => symbol.Kind == "NamedType" && symbol.Signature.Contains("interface", StringComparison.Ordinal) && after.Container.EndsWith(symbol.Name, StringComparison.Ordinal));
                string code = interfaceMemberAddition ? "interface-member-added" : AdditionCode(after);
                changes.Add(CreateChange(
                    code,
                    "addition",
                    after,
                    before: null,
                    after,
                    sourceRisk: interfaceMemberAddition ? "breaking" : "compatible",
                    binaryRisk: interfaceMemberAddition ? "breaking" : "compatible",
                    confidence: interfaceMemberAddition ? "high" : "high",
                    reasonCodes: [code]));
            }
        }

        IReadOnlyList<PublicApiChange> ordered = [.. changes
            .OrderBy(change => RiskPriority(change), Comparer<int>.Default)
            .ThenBy(change => change.Code, StringComparer.Ordinal)
            .ThenBy(change => change.Symbol.Container, StringComparer.Ordinal)
            .ThenBy(change => change.Symbol.Name, StringComparer.Ordinal)
            .ThenBy(change => change.Symbol.Path, StringComparer.Ordinal)
            .ThenBy(change => change.Symbol.Line)
            .ThenBy(change => change.Symbol.Column)];

        return new PublicApiChangesSection(
            TotalChanges: ordered.Count,
            Limit: changeLimit,
            Truncated: ordered.Count > changeLimit,
            Items: [.. ordered.Take(changeLimit)]);
    }

    private static IEnumerable<PublicApiChange> CompareMatched(
        PublicApiSymbol before,
        PublicApiSymbol after,
        bool includeAttributes)
    {
        if (!StringEquals(before.GenericConstraints, after.GenericConstraints))
        {
            yield return CreateChange("generic-constraints-changed", "change", after, before, after, "breaking", "risk", "medium", ["generic-constraints-changed"]);
        }

        if (!StringEquals(before.NullableAnnotations, after.NullableAnnotations))
        {
            yield return CreateChange("nullable-annotation-changed", "change", after, before, after, "risk", "compatible", "medium", ["nullable-annotation-changed"]);
        }

        if (!StringEquals(before.DefaultValues, after.DefaultValues))
        {
            yield return CreateChange("default-parameter-changed", "change", after, before, after, "risk", "risk", "medium", ["default-parameter-changed"]);
        }

        if (!StringEquals(before.Modifiers, after.Modifiers) &&
            HasAbstractVirtualSealedChange(before.Modifiers, after.Modifiers))
        {
            yield return CreateChange("abstract-virtual-sealed-changed", "change", after, before, after, "breaking", "risk", "medium", ["abstract-virtual-sealed-changed"]);
        }

        if (before.Kind == "EnumMember" && before.Signature != after.Signature)
        {
            yield return CreateChange("enum-member-value-changed", "change", after, before, after, "risk", "breaking", "high", ["enum-member-value-changed"]);
        }

        if (includeAttributes && !StringEquals(before.Attributes, after.Attributes))
        {
            string code = before.Attributes.Count < after.Attributes.Count
                ? "attribute-added"
                : before.Attributes.Count > after.Attributes.Count
                    ? "attribute-removed"
                    : "attribute-argument-changed";
            yield return CreateChange(code, "change", after, before, after, "risk", "risk", "medium", [code]);
        }
    }

    private static PublicApiSymbol? FindSignatureCandidate(
        PublicApiSymbol before,
        IEnumerable<PublicApiSymbol> candidates)
    {
        return candidates
            .Where(candidate => candidate.Kind == before.Kind)
            .Where(candidate => candidate.Container == before.Container)
            .Where(candidate => candidate.Name == before.Name)
            .Where(candidate => candidate.Parameters.Count == before.Parameters.Count)
            .OrderBy(candidate => candidate.DocumentationCommentId, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static PublicApiChange CreateChange(
        string code,
        string kind,
        PublicApiSymbol symbol,
        PublicApiSymbol? before,
        PublicApiSymbol? after,
        string sourceRisk,
        string binaryRisk,
        string confidence,
        IReadOnlyList<string> reasonCodes)
    {
        return new PublicApiChange(
            Code: code,
            Kind: kind,
            SourceCompatibility: new PublicApiCompatibility(sourceRisk, confidence, reasonCodes),
            BinaryCompatibility: new PublicApiCompatibility(binaryRisk, confidence, reasonCodes),
            Symbol: symbol,
            Before: before,
            After: after,
            Evidence: Evidence(before, after),
            ReasonCodes: reasonCodes);
    }

    private static IReadOnlyList<PublicApiEvidence> Evidence(PublicApiSymbol? before, PublicApiSymbol? after)
    {
        List<PublicApiEvidence> evidence = [];
        if (before is not null)
        {
            evidence.Add(new PublicApiEvidence("base", before.Path, before.Line, before.Column, ["symbol-present-in-base"]));
        }

        if (after is not null)
        {
            evidence.Add(new PublicApiEvidence("head", after.Path, after.Line, after.Column, ["symbol-present-in-head"]));
        }

        return evidence;
    }

    private static string AdditionCode(PublicApiSymbol symbol)
    {
        return symbol.Kind == "NamedType" ? "public-type-added" :
            symbol.Kind == "EnumMember" ? "enum-member-added" :
            "public-member-added";
    }

    private static string RemovalCode(PublicApiSymbol symbol)
    {
        return symbol.Kind == "NamedType" ? "public-type-removed" :
            symbol.Kind == "EnumMember" ? "enum-member-removed" :
            "public-member-removed";
    }

    private static bool StringEquals(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        return left.SequenceEqual(right, StringComparer.Ordinal);
    }

    private static bool HasAbstractVirtualSealedChange(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        string[] interesting = ["abstract", "virtual", "sealed", "override"];
        return interesting.Any(value => left.Contains(value, StringComparer.Ordinal) != right.Contains(value, StringComparer.Ordinal));
    }

    private static int RiskPriority(PublicApiChange change)
    {
        if (change.BinaryCompatibility.Risk == "breaking")
        {
            return 0;
        }

        if (change.SourceCompatibility.Risk == "breaking")
        {
            return 1;
        }

        return change.SourceCompatibility.Risk == "risk" || change.BinaryCompatibility.Risk == "risk" ? 2 : 3;
    }
}


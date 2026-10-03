using Microsoft.CodeAnalysis;

namespace Navlyn.Symbols;

public static class SymbolKindNames
{
    private static readonly IReadOnlyDictionary<string, string> Names = CreateNames();

    public static bool TryNormalize(string? value, out string kind)
    {
        if (value is not null && Names.TryGetValue(value.Trim(), out string? canonical))
        {
            kind = canonical;
            return true;
        }

        kind = string.Empty;
        return false;
    }

    public static IReadOnlyList<string> NormalizeMany(IReadOnlyList<string> values)
    {
        return [.. values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => TryNormalize(value, out string kind) ? kind : value.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)];
    }

    private static IReadOnlyDictionary<string, string> CreateNames()
    {
        Dictionary<string, string> names = Enum.GetNames<SymbolKind>()
            .ToDictionary(name => name, name => name, StringComparer.OrdinalIgnoreCase);
        foreach (string alias in new[] { "class", "interface", "struct", "record", "enum", "delegate", "type" })
        {
            names[alias] = nameof(SymbolKind.NamedType);
        }

        return names;
    }
}

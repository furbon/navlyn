using System.Text.RegularExpressions;

namespace Navlyn.Symbols;

internal sealed class SymbolNameMatcher
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private readonly SymbolSearchOptions _options;
    private readonly Regex? _regex;

    private SymbolNameMatcher(SymbolSearchOptions options, Regex? regex)
    {
        _options = options;
        _regex = regex;
    }

    public static bool TryCreate(
        SymbolSearchOptions options,
        out SymbolNameMatcher matcher,
        out string? errorMessage)
    {
        if (options.MatchMode != SymbolMatchMode.Regex)
        {
            matcher = new SymbolNameMatcher(options, regex: null);
            errorMessage = null;
            return true;
        }

        try
        {
            RegexOptions regexOptions = RegexOptions.CultureInvariant;
            if (!options.CaseSensitive)
            {
                regexOptions |= RegexOptions.IgnoreCase;
            }

            matcher = new SymbolNameMatcher(options, new Regex(options.Query, regexOptions, RegexTimeout));
            errorMessage = null;
            return true;
        }
        catch (ArgumentException ex)
        {
            matcher = null!;
            errorMessage = $"Invalid --query regular expression: {ex.Message}";
            return false;
        }
    }

    public bool IsMatch(string symbolName)
    {
        return _options.MatchMode switch
        {
            SymbolMatchMode.Contains => symbolName.IndexOf(_options.Query, GetStringComparison()) >= 0,
            SymbolMatchMode.Exact => string.Equals(symbolName, _options.Query, GetStringComparison()),
            SymbolMatchMode.Regex => IsRegexMatch(symbolName),
            _ => false
        };
    }

    private bool IsRegexMatch(string symbolName)
    {
        try
        {
            return _regex!.IsMatch(symbolName);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private StringComparison GetStringComparison()
    {
        return _options.CaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
    }
}

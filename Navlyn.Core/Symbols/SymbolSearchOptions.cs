namespace Navlyn.Symbols;

internal sealed record SymbolSearchOptions(
    string Query,
    SymbolMatchMode MatchMode,
    bool CaseSensitive);

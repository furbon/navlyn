using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Navlyn.Workspaces;

internal static partial class ProjectContextFacts
{
    public static string? GetTargetFramework(Project project)
    {
        string? targetFramework = GetTargetFrameworkFromProjectName(project.Name);
        if (targetFramework is not null)
        {
            return targetFramework;
        }

        if (project.ParseOptions is not CSharpParseOptions parseOptions)
        {
            return null;
        }

        return parseOptions.PreprocessorSymbolNames
            .Select(GetTargetFrameworkFromPreprocessorSymbol)
            .Where(value => value is not null)
            .OrderBy(value => value, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    public static string? GetLanguageVersion(Project project)
    {
        return project.ParseOptions is CSharpParseOptions parseOptions
            ? parseOptions.LanguageVersion.ToString()
            : null;
    }

    public static IReadOnlyList<string> GetPreprocessorSymbols(Project project)
    {
        return project.ParseOptions is CSharpParseOptions parseOptions
            ? [.. parseOptions.PreprocessorSymbolNames
                .OrderBy(symbol => symbol, StringComparer.Ordinal)]
            : [];
    }

    private static string? GetTargetFrameworkFromProjectName(string projectName)
    {
        Match match = TargetFrameworkProjectNameRegex().Match(projectName);
        return match.Success ? match.Groups["tfm"].Value : null;
    }

    private static string? GetTargetFrameworkFromPreprocessorSymbol(string symbol)
    {
        Match netMatch = NetTargetFrameworkSymbolRegex().Match(symbol);
        if (netMatch.Success)
        {
            return $"net{netMatch.Groups["major"].Value}.{netMatch.Groups["minor"].Value}";
        }

        Match netStandardMatch = NetStandardTargetFrameworkSymbolRegex().Match(symbol);
        if (netStandardMatch.Success)
        {
            return $"netstandard{netStandardMatch.Groups["major"].Value}.{netStandardMatch.Groups["minor"].Value}";
        }

        Match netCoreAppMatch = NetCoreAppTargetFrameworkSymbolRegex().Match(symbol);
        if (netCoreAppMatch.Success)
        {
            return $"netcoreapp{netCoreAppMatch.Groups["major"].Value}.{netCoreAppMatch.Groups["minor"].Value}";
        }

        Match netFrameworkMatch = NetFrameworkTargetFrameworkSymbolRegex().Match(symbol);
        if (netFrameworkMatch.Success)
        {
            string version = netFrameworkMatch.Groups["version"].Value;
            return $"net{version[0]}.{string.Join('.', version[1..].ToCharArray())}";
        }

        return null;
    }

    [GeneratedRegex(@"\((?<tfm>net[^)]+)\)$", RegexOptions.CultureInvariant)]
    private static partial Regex TargetFrameworkProjectNameRegex();

    [GeneratedRegex(@"^NET(?<major>\d+)_(?<minor>\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex NetTargetFrameworkSymbolRegex();

    [GeneratedRegex(@"^NETSTANDARD(?<major>\d+)_(?<minor>\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex NetStandardTargetFrameworkSymbolRegex();

    [GeneratedRegex(@"^NETCOREAPP(?<major>\d+)_(?<minor>\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex NetCoreAppTargetFrameworkSymbolRegex();

    [GeneratedRegex(@"^NET(?<version>4\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex NetFrameworkTargetFrameworkSymbolRegex();
}

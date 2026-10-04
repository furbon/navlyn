using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.VisualBasic;

namespace Navlyn.Workspaces;

internal static partial class ProjectContextFacts
{
    private const long MaxAssetsBytes = 16L * 1024 * 1024;

    public static string? GetTargetFramework(Project project)
    {
        string? targetFramework = GetTargetFrameworkFromProjectName(project.Name);
        if (targetFramework is not null)
        {
            return targetFramework;
        }

        string? fromOutput = GetTargetFrameworkFromOutputPath(project);
        if (fromOutput is not null) { return fromOutput; }
        string? fromSymbols = null;
        if (project.ParseOptions is CSharpParseOptions parseOptions)
        {
            fromSymbols = parseOptions.PreprocessorSymbolNames
                .Select(GetTargetFrameworkFromPreprocessorSymbol)
                .Where(value => value is not null)
                .OrderBy(value => value, StringComparer.Ordinal)
                .FirstOrDefault();
        }
        else if (project.ParseOptions is VisualBasicParseOptions visualBasicParseOptions)
        {
            fromSymbols = visualBasicParseOptions.PreprocessorSymbols
                .Select(symbol => symbol.Key)
                .Select(GetTargetFrameworkFromPreprocessorSymbol)
                .Where(value => value is not null)
                .OrderBy(value => value, StringComparer.Ordinal)
                .FirstOrDefault();
        }

        return fromSymbols;
    }

    public static string? GetLanguageVersion(Project project)
    {
        return project.ParseOptions switch
        {
            CSharpParseOptions parseOptions => parseOptions.LanguageVersion.ToString(),
            VisualBasicParseOptions parseOptions => parseOptions.LanguageVersion.ToString(),
            _ => null
        };
    }

    public static IReadOnlyList<string> GetPreprocessorSymbols(Project project)
    {
        return project.ParseOptions switch
        {
            CSharpParseOptions parseOptions => [.. parseOptions.PreprocessorSymbolNames
                .OrderBy(symbol => symbol, StringComparer.Ordinal)],
            VisualBasicParseOptions parseOptions => [.. parseOptions.PreprocessorSymbols
                .Select(symbol => symbol.Key)
                .OrderBy(symbol => symbol, StringComparer.Ordinal)],
            _ => []
        };
    }

    private static string? GetTargetFrameworkFromProjectName(string projectName)
    {
        Match match = TargetFrameworkProjectNameRegex().Match(projectName);
        return match.Success ? match.Groups["tfm"].Value : null;
    }

    private static string? GetTargetFrameworkFromOutputPath(Project project)
    {
        string? outputFilePath = project.OutputFilePath;
        if (string.IsNullOrWhiteSpace(outputFilePath))
        {
            return null;
        }

        string? directory = Path.GetDirectoryName(outputFilePath);
        if (directory is null)
        {
            return null;
        }

        string finalDirectory = Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
        if (!OutputTargetFrameworkRegex().IsMatch(finalDirectory) || project.FilePath is null)
        {
            return null;
        }

        string? assetsPath = ProjectAssetsLocator.Find(project);
        if (assetsPath is null) return null;
        try
        {
            using FileStream stream = new(assetsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length == 0 || stream.Length > MaxAssetsBytes)
            {
                return null;
            }

            byte[] bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            using JsonDocument assets = JsonDocument.Parse(bytes);
            if (!assets.RootElement.TryGetProperty("targets", out JsonElement targets) || targets.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            string[] frameworks = targets.EnumerateObject()
                .Select(target => target.Name.Split('/')[0])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (assets.RootElement.TryGetProperty("project", out JsonElement projectFacts) &&
                projectFacts.TryGetProperty("restore", out JsonElement restore) &&
                restore.TryGetProperty("originalTargetFrameworks", out JsonElement originalFrameworks) &&
                originalFrameworks.ValueKind == JsonValueKind.Array &&
                originalFrameworks.EnumerateArray().Any(value => value.ValueKind == JsonValueKind.String &&
                    string.Equals(value.GetString(), finalDirectory, StringComparison.OrdinalIgnoreCase)))
            {
                return finalDirectory;
            }
            return frameworks.Contains(finalDirectory, StringComparer.OrdinalIgnoreCase) ? finalDirectory : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return null;
        }
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

    [GeneratedRegex(@"^net(?:standard|coreapp)?\d+(?:\.\d+)*(?:-[A-Za-z0-9][A-Za-z0-9.-]*)?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex OutputTargetFrameworkRegex();

    [GeneratedRegex(@"^NET(?<major>\d+)_(?<minor>\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex NetTargetFrameworkSymbolRegex();

    [GeneratedRegex(@"^NETSTANDARD(?<major>\d+)_(?<minor>\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex NetStandardTargetFrameworkSymbolRegex();

    [GeneratedRegex(@"^NETCOREAPP(?<major>\d+)_(?<minor>\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex NetCoreAppTargetFrameworkSymbolRegex();

    [GeneratedRegex(@"^NET(?<version>4\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex NetFrameworkTargetFrameworkSymbolRegex();
}

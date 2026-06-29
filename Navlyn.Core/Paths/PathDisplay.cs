namespace Navlyn.Paths;

internal static class PathDisplay
{
    public static string FromCurrentDirectory(string path)
    {
        return FromRepositoryRoot(path);
    }

    public static string FromRepositoryRoot(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string displayRoot = FindRepositoryRoot(fullPath) ?? Directory.GetCurrentDirectory();
        string relativePath = Path.GetRelativePath(displayRoot, fullPath);
        bool isOutsideDisplayRoot =
            relativePath == ".." ||
            relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);

        return isOutsideDisplayRoot || Path.IsPathRooted(relativePath)
            ? NormalizeDisplaySeparators(fullPath)
            : NormalizeDisplaySeparators(relativePath);
    }

    public static IReadOnlyList<string> GetInputPathCandidates(string path, string? anchorPath)
    {
        string normalizedPath = NormalizeInputSeparators(path);

        if (Path.IsPathRooted(normalizedPath))
        {
            return [Path.GetFullPath(normalizedPath)];
        }

        List<string> candidates = [];
        AddCandidate(candidates, Path.GetFullPath(normalizedPath));

        string? currentRepositoryRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
        if (currentRepositoryRoot is not null)
        {
            AddCandidate(candidates, Path.GetFullPath(Path.Combine(currentRepositoryRoot, normalizedPath)));
        }

        if (!string.IsNullOrWhiteSpace(anchorPath))
        {
            string anchorFullPath = Path.GetFullPath(anchorPath);
            string anchorDirectory = Directory.Exists(anchorFullPath)
                ? anchorFullPath
                : Path.GetDirectoryName(anchorFullPath) ?? anchorFullPath;

            AddCandidate(candidates, Path.GetFullPath(Path.Combine(anchorDirectory, normalizedPath)));

            string? anchorRepositoryRoot = FindRepositoryRoot(anchorDirectory);
            if (anchorRepositoryRoot is not null)
            {
                AddCandidate(candidates, Path.GetFullPath(Path.Combine(anchorRepositoryRoot, normalizedPath)));
            }
        }

        return candidates;
    }

    public static string? FindRepositoryRoot(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string? directory = Directory.Exists(fullPath)
            ? fullPath
            : Path.GetDirectoryName(fullPath);

        while (!string.IsNullOrEmpty(directory))
        {
            if (Directory.Exists(Path.Combine(directory, ".git")) ||
                File.Exists(Path.Combine(directory, ".git")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        return null;
    }

    private static void AddCandidate(List<string> candidates, string candidate)
    {
        StringComparer comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        if (!candidates.Contains(candidate, comparer))
        {
            candidates.Add(candidate);
        }
    }

    private static string NormalizeDisplaySeparators(string path)
    {
        return path
            .Replace('\\', '/')
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static string NormalizeInputSeparators(string path)
    {
        return path
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
    }
}

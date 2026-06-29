namespace Navlyn.GeneratedCode;

internal static class GeneratedCodeFacts
{
    public static bool IsGeneratedPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string fileName = Path.GetFileName(path);
        if (fileName.StartsWith("TemporaryGeneratedFile_", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".AssemblyAttributes.cs", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (string segment in GetPathSegments(path))
        {
            if (string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> GetPathSegments(string path)
    {
        return path.Split(
            ['/', '\\'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}

using System.Text.Json;
using Microsoft.CodeAnalysis;

namespace Navlyn.Workspaces;

internal static class ProjectAssetsLocator
{
    // Use paths reported by the loaded compiler project, without another MSBuild evaluation or tree scan.
    public static string? Find(Project project)
    {
        if (project.FilePath is null) return null;
        StringComparer comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        HashSet<string> visited = new(comparer);
        IEnumerable<string?> anchors = new[] { project.OutputRefFilePath }
            .Concat(project.AnalyzerConfigDocuments.Select(document => document.FilePath))
            .Concat(project.Documents.Where(document => document.FilePath?.EndsWith(".AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase) == true)
                .Select(document => document.FilePath));
        foreach (string? anchor in anchors)
        {
            if (string.IsNullOrWhiteSpace(anchor)) continue;
            DirectoryInfo? directory = Directory.GetParent(Path.GetFullPath(anchor));
            for (int depth = 0; directory is not null && depth < 6; depth++, directory = directory.Parent)
            {
                string candidate = Path.Combine(directory.FullName, "project.assets.json");
                if (!visited.Add(candidate) || !File.Exists(candidate)) continue;
                try
                {
                    using FileStream stream = File.OpenRead(candidate);
                    if (stream.Length > 16L * 1024 * 1024) continue;
                    using JsonDocument assets = JsonDocument.Parse(stream);
                    if (assets.RootElement.TryGetProperty("project", out JsonElement projectFacts) &&
                        projectFacts.TryGetProperty("restore", out JsonElement restore) &&
                        restore.TryGetProperty("projectPath", out JsonElement path) && path.ValueKind == JsonValueKind.String &&
                        comparer.Equals(Path.GetFullPath(path.GetString()!), Path.GetFullPath(project.FilePath)))
                        return candidate;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { }
            }
        }
        // Retain the conventional path, including its existing size/staleness diagnostics.
        return Path.Combine(Path.GetDirectoryName(project.FilePath)!, "obj", "project.assets.json");
    }
}

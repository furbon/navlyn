using System.Text.RegularExpressions;

namespace Navlyn.Diffs;

internal sealed partial class UnifiedDiffParser
{
    public DiffReadResult Parse(string text, DiffRequest request)
    {
        List<DiffFileBuilder> files = [];
        DiffFileBuilder? current = null;

        foreach (string rawLine in SplitLines(text))
        {
            string line = rawLine.TrimEnd('\r');

            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                current = new DiffFileBuilder();
                files.Add(current);
                continue;
            }

            if (current is null)
            {
                continue;
            }

            if (line.StartsWith("new file mode ", StringComparison.Ordinal))
            {
                current.Status = "added";
                continue;
            }

            if (line.StartsWith("deleted file mode ", StringComparison.Ordinal))
            {
                current.Status = "deleted";
                continue;
            }

            if (line.StartsWith("rename from ", StringComparison.Ordinal))
            {
                current.OldPath = NormalizePath(line["rename from ".Length..]);
                current.Status = "renamed";
                continue;
            }

            if (line.StartsWith("rename to ", StringComparison.Ordinal))
            {
                current.Path = NormalizePath(line["rename to ".Length..]);
                current.Status = "renamed";
                continue;
            }

            if (line.StartsWith("--- ", StringComparison.Ordinal))
            {
                string oldPath = ParseFileMarkerPath(line["--- ".Length..]);
                if (oldPath != "/dev/null")
                {
                    current.OldPath ??= oldPath;
                }

                continue;
            }

            if (line.StartsWith("+++ ", StringComparison.Ordinal))
            {
                string newPath = ParseFileMarkerPath(line["+++ ".Length..]);
                if (newPath == "/dev/null")
                {
                    current.Path ??= current.OldPath;
                    current.Status = "deleted";
                }
                else
                {
                    current.Path = newPath;
                    current.Status ??= current.OldPath is null || current.OldPath == newPath ? "modified" : "renamed";
                }

                continue;
            }

            Match hunk = HunkHeaderRegex().Match(line);
            if (hunk.Success)
            {
                current.Hunks.Add(new DiffHunk(
                    OldStart: int.Parse(hunk.Groups["oldStart"].Value),
                    OldLineCount: ParseCount(hunk.Groups["oldCount"].Value),
                    NewStart: int.Parse(hunk.Groups["newStart"].Value),
                    NewLineCount: ParseCount(hunk.Groups["newCount"].Value)));
            }
        }

        IReadOnlyList<DiffFile> parsedFiles = [.. files
            .Select(file => file.Build())
            .OfType<DiffFile>()
            .OrderBy(file => file.Path, StringComparer.Ordinal)
            .ThenBy(file => file.Status, StringComparer.Ordinal)];

        return DiffReadResult.Succeeded(new DiffSet(
            Mode: request.Mode,
            Base: request.Base,
            Head: request.Head,
            Staged: request.Staged,
            IncludeUnstaged: request.IncludeUnstaged,
            TotalFiles: parsedFiles.Count,
            Files: parsedFiles));
    }

    private static IEnumerable<string> SplitLines(string text)
    {
        using StringReader reader = new(text);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            yield return line;
        }
    }

    private static int ParseCount(string value)
    {
        return string.IsNullOrEmpty(value) ? 1 : int.Parse(value);
    }

    private static string ParseFileMarkerPath(string marker)
    {
        string path = marker.Split('\t')[0].Trim();
        if (path == "/dev/null")
        {
            return path;
        }

        return NormalizePath(path);
    }

    internal static string NormalizePath(string path)
    {
        string value = Unquote(path.Trim());
        if (value.StartsWith("a/", StringComparison.Ordinal) ||
            value.StartsWith("b/", StringComparison.Ordinal))
        {
            value = value[2..];
        }

        return value.Replace('\\', '/');
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            return Regex.Unescape(value[1..^1]);
        }

        return value;
    }

    [GeneratedRegex(@"^@@ -(?<oldStart>\d+)(?:,(?<oldCount>\d+))? \+(?<newStart>\d+)(?:,(?<newCount>\d+))? @@")]
    private static partial Regex HunkHeaderRegex();

    private sealed class DiffFileBuilder
    {
        public string? Path { get; set; }

        public string? OldPath { get; set; }

        public string? Status { get; set; }

        public List<DiffHunk> Hunks { get; } = [];

        public DiffFile? Build()
        {
            string? path = Path ?? OldPath;
            if (path is null)
            {
                return null;
            }

            string? oldPath = OldPath is not null && OldPath != path ? OldPath : null;
            string status = Status ?? (oldPath is null ? "modified" : "renamed");
            return new DiffFile(path, oldPath, status, [.. Hunks]);
        }
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Navlyn.Mcp.Execution;

/// <summary>Content inventory used to determine whether a loaded workspace still represents its inputs.</summary>
internal sealed class WorkspaceInputState
{
    private static readonly HashSet<string> InputExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".vb", ".sln", ".slnx", ".csproj", ".vbproj", ".props", ".targets", ".config", ".editorconfig",
        ".code-workspace", ".globalconfig",
    };

    private static readonly HashSet<string> InputNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "global.json", "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "NuGet.Config",
        "nuget.config", "navlyn.workspace.json", ".editorconfig", "Directory.Build.rsp", "MSBuild.rsp",
        "project.assets.json",
    };

    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "bin", "obj",
    };

    private WorkspaceInputState(IReadOnlyList<WorkspaceInputFile> files, IReadOnlyList<string> errors)
    {
        Files = files;
        Errors = errors;
        DiscoveredPaths = files.Where(file => file.Exists).Select(file => file.Path).ToArray();
        Digest = ComputeDigest(files, errors);
    }

    public IReadOnlyList<WorkspaceInputFile> Files { get; }

    public IReadOnlyList<string> Errors { get; }

    public IReadOnlyList<string> DiscoveredPaths { get; }

    public string Digest { get; }

    public bool IsComplete => Errors.Count == 0 && Files.All(file => file.Exists && file.Error is null);

    public static WorkspaceInputState Capture(
        string workspaceRoot,
        IEnumerable<string> selectedInputs,
        IEnumerable<string> loadedInputs,
        IEnumerable<string>? additionalRoots = null,
        CancellationToken cancellationToken = default,
        IEnumerable<string>? projectDirectories = null)
    {
        string root = Path.GetFullPath(workspaceRoot);
        HashSet<string> paths = new(PathComparer);
        List<string> inventoryErrors = [];
        List<string> roots = GetSweepRoots(root, additionalRoots);
        foreach (string path in selectedInputs.Concat(loadedInputs))
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddPath(paths, path);
        }

        foreach (string sweepRoot in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                AddTreeInputs(sweepRoot, paths, inventoryErrors, cancellationToken);
            }
            catch (Exception exception) when (IsFileSystemException(exception))
            {
                inventoryErrors.Add($"{Normalize(sweepRoot)}: inventory failed ({exception.GetType().Name})");
            }

            AddAncestorConfigurationInputs(sweepRoot, paths, cancellationToken);
        }

        foreach (string projectDirectory in projectDirectories?.Distinct(PathComparer) ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                AddBuildAssetsInput(projectDirectory, paths, inventoryErrors);
            }
            catch (Exception exception) when (IsFileSystemException(exception))
            {
                inventoryErrors.Add($"{Normalize(projectDirectory)}: assets inventory failed ({exception.GetType().Name})");
            }
        }

        string[] sortedPaths = paths.OrderBy(path => path, PathComparer).ThenBy(path => path, StringComparer.Ordinal).ToArray();
        WorkspaceInputFile[] files = new WorkspaceInputFile[sortedPaths.Length];
        Parallel.For(0, sortedPaths.Length,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 16)
            },
            index => files[index] = ReadInput(sortedPaths[index], cancellationToken));
        string[] errors = files.Where(file => file.Error is not null).Select(file => $"{file.Path}: {file.Error}")
            .Concat(inventoryErrors).OrderBy(error => error, StringComparer.Ordinal).ToArray();
        return new WorkspaceInputState(files, errors);
    }

    private static void AddTreeInputs(string root, HashSet<string> paths, List<string> inventoryErrors, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        if (IsReparsePoint(new DirectoryInfo(root)))
        {
            inventoryErrors.Add($"{Normalize(root)}: reparse directory cannot be inventoried safely");
            return;
        }

        Stack<string> pending = new();
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = pending.Pop();
            FileSystemInfo[] entries;
            try
            {
                entries = new DirectoryInfo(directory).EnumerateFileSystemInfos().ToArray();
            }
            catch (Exception exception) when (IsFileSystemException(exception))
            {
                inventoryErrors.Add($"{Normalize(directory)}: inventory failed ({exception.GetType().Name})");
                continue;
            }

            foreach (FileSystemInfo entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry is DirectoryInfo)
                {
                    if (IsExcludedDirectory(root, entry.FullName))
                    {
                        continue;
                    }

                    if (IsReparsePoint(entry))
                    {
                        inventoryErrors.Add($"{Normalize(entry.FullName)}: reparse directory cannot be inventoried safely");
                    }
                    else
                    {
                        pending.Push(entry.FullName);
                    }

                    continue;
                }

                if (IsInputFile(entry.FullName))
                {
                    paths.Add(Normalize(entry.FullName));
                }
            }
        }
    }

    private static void AddAncestorConfigurationInputs(string root, HashSet<string> paths, CancellationToken cancellationToken)
    {
        DirectoryInfo? directory = new(root);
        while (directory is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (string name in InputNames)
            {
                string path = Path.Combine(directory.FullName, name);
                if (File.Exists(path))
                {
                    paths.Add(Normalize(path));
                }
            }

            directory = directory.Parent;
        }
    }

    private static void AddBuildAssetsInput(string root, HashSet<string> paths, List<string> inventoryErrors)
    {
        string intermediateDirectory = Path.Combine(root, "obj");
        if (!Directory.Exists(intermediateDirectory))
        {
            return;
        }

        if (IsReparsePoint(new DirectoryInfo(intermediateDirectory)))
        {
            inventoryErrors.Add($"{Normalize(intermediateDirectory)}: reparse directory cannot be inventoried safely");
            return;
        }

        string assetsPath = Path.Combine(intermediateDirectory, "project.assets.json");
        if (File.Exists(assetsPath))
        {
            paths.Add(Normalize(assetsPath));
        }
    }

    private static List<string> GetSweepRoots(string workspaceRoot, IEnumerable<string>? additionalRoots)
    {
        List<string> candidates = additionalRoots is null
            ? []
            : [.. additionalRoots.Where(path => !string.IsNullOrWhiteSpace(path)).Select(Path.GetFullPath)];
        if (candidates.Count == 0) candidates.Add(workspaceRoot);

        List<string> roots = [];
        foreach (string candidate in candidates.Select(Normalize).Distinct(PathComparer).OrderBy(path => path.Length).ThenBy(path => path, PathComparer))
        {
            if (!roots.Any(parent => IsSameOrDescendant(candidate, parent)))
            {
                roots.Add(candidate);
            }
        }

        return roots;
    }

    private static bool IsSameOrDescendant(string path, string parent)
    {
        if (PathComparer.Equals(path, parent))
        {
            return true;
        }

        string prefix = parent.EndsWith(Path.DirectorySeparatorChar) || parent.EndsWith(Path.AltDirectorySeparatorChar)
            ? parent
            : parent + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static bool IsReparsePoint(FileSystemInfo path)
    {
        try
        {
            return (path.Attributes & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception exception) when (IsFileSystemException(exception))
        {
            return true;
        }
    }

    private static void AddPath(HashSet<string> paths, string path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            paths.Add(Normalize(path));
        }
    }

    private static WorkspaceInputFile ReadInput(string path, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(path))
            {
                return new WorkspaceInputFile(path, Exists: false, Hash: null, Error: "missing");
            }

            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return new WorkspaceInputFile(path, Exists: true, HashFile(stream, cancellationToken), Error: null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsFileSystemException(exception))
        {
            return new WorkspaceInputFile(path, Exists: true, Hash: null, Error: exception.GetType().Name);
        }
    }

    private static string HashFile(Stream stream, CancellationToken cancellationToken)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hash.AppendData(buffer, 0, read);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string ComputeDigest(IEnumerable<WorkspaceInputFile> files, IEnumerable<string> errors)
    {
        StringBuilder value = new();
        foreach (WorkspaceInputFile file in files)
        {
            value.Append(file.Path).Append('\0').Append(file.Exists ? '1' : '0').Append('\0')
                .Append(file.Hash ?? string.Empty).Append('\0').Append(file.Error ?? string.Empty).Append('\n');
        }

        foreach (string error in errors)
        {
            value.Append("error\0").Append(error).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToString())));
    }

    private static bool IsInputFile(string path) => InputExtensions.Contains(Path.GetExtension(path)) || InputNames.Contains(Path.GetFileName(path));

    private static bool IsExcludedDirectory(string root, string path)
    {
        string relativePath = Path.GetRelativePath(root, path);
        string[] segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(ExcludedDirectories.Contains) ||
            (segments.Length >= 2 && segments[^2].Equals(".navlyn", StringComparison.OrdinalIgnoreCase) && segments[^1].Equals("cache", StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string path)
    {
        string fullPath = Path.GetFullPath(path);
        return OperatingSystem.IsWindows() ? fullPath.ToUpperInvariant() : fullPath;
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static bool IsFileSystemException(Exception exception) => exception is IOException or UnauthorizedAccessException or System.Security.SecurityException;
}

internal sealed record WorkspaceInputFile(string Path, bool Exists, string? Hash, string? Error);

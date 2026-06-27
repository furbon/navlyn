using System.Xml.Linq;
using Navlyn.Paths;

namespace Navlyn.RepoGraph;

internal sealed class ProjectFileReader
{
    public ProjectFileFacts Read(string? projectPath, string repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return new ProjectFileFacts(
                Sdk: null,
                OutputType: null,
                Nullable: null,
                ImplicitUsings: null,
                TargetFramework: null,
                TargetFrameworks: [],
                PackAsTool: false,
                PackageReferences: [],
                ProjectReferences: [],
                Warnings: ["project-path-missing"]);
        }

        string fullPath = GetFullPath(projectPath, repositoryRoot);
        if (!File.Exists(fullPath))
        {
            return new ProjectFileFacts(
                Sdk: null,
                OutputType: null,
                Nullable: null,
                ImplicitUsings: null,
                TargetFramework: null,
                TargetFrameworks: [],
                PackAsTool: false,
                PackageReferences: [],
                ProjectReferences: [],
                Warnings: ["project-file-not-found"]);
        }

        try
        {
            XDocument document = XDocument.Load(fullPath, LoadOptions.PreserveWhitespace);
            XElement root = document.Root!;
            IReadOnlyDictionary<string, string> centralVersions = ReadCentralVersions(fullPath, repositoryRoot);

            string? targetFramework = GetProperty(root, "TargetFramework");
            IReadOnlyList<string> targetFrameworks = GetTargetFrameworks(targetFramework, GetProperty(root, "TargetFrameworks"));

            IReadOnlyList<ProjectFilePackageReference> packages = [.. Descendants(root, "PackageReference")
                .Select(element => CreatePackageReference(element, centralVersions))
                .Where(reference => !string.IsNullOrWhiteSpace(reference.Name))
                .OrderBy(reference => reference.Name, StringComparer.Ordinal)
                .ThenBy(reference => reference.Version, StringComparer.Ordinal)];

            string projectDirectory = Path.GetDirectoryName(fullPath) ?? repositoryRoot;
            IReadOnlyList<ProjectFileProjectReference> projectReferences = [.. Descendants(root, "ProjectReference")
                .Select(element => CreateProjectReference(element, projectDirectory))
                .Where(reference => !string.IsNullOrWhiteSpace(reference.Include))
                .OrderBy(reference => reference.Path, StringComparer.Ordinal)
                .ThenBy(reference => reference.Include, StringComparer.Ordinal)];

            return new ProjectFileFacts(
                Sdk: Attribute(root, "Sdk"),
                OutputType: GetProperty(root, "OutputType"),
                Nullable: GetProperty(root, "Nullable"),
                ImplicitUsings: GetProperty(root, "ImplicitUsings"),
                TargetFramework: targetFramework,
                TargetFrameworks: targetFrameworks,
                PackAsTool: IsTrue(GetProperty(root, "PackAsTool")),
                PackageReferences: packages,
                ProjectReferences: projectReferences,
                Warnings: []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return new ProjectFileFacts(
                Sdk: null,
                OutputType: null,
                Nullable: null,
                ImplicitUsings: null,
                TargetFramework: null,
                TargetFrameworks: [],
                PackAsTool: false,
                PackageReferences: [],
                ProjectReferences: [],
                Warnings: ["project-file-parse-failed"]);
        }
    }

    public IReadOnlyList<RepoGraphMsbuildFile> DiscoverMsbuildFiles(
        string repositoryRoot,
        IReadOnlyList<ProjectWithFacts> projects)
    {
        string[] names = ["Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props"];
        List<RepoGraphMsbuildFile> files = [];

        foreach (string name in names)
        {
            foreach (string path in EnumerateRepositoryFiles(repositoryRoot, name))
            {
                string fullPath = Path.GetFullPath(path);
                IReadOnlyList<string> appliesTo = [.. projects
                    .Where(project => ProjectIsUnderDirectory(project, Path.GetDirectoryName(fullPath)))
                    .Select(project => project.Project.Id)
                    .OrderBy(id => id, StringComparer.Ordinal)];

                files.Add(new RepoGraphMsbuildFile(
                    Kind: name,
                    Path: PathDisplay.FromCurrentDirectory(fullPath),
                    AppliesToProjectIds: appliesTo));
            }
        }

        return [.. files
            .OrderBy(file => file.Path, StringComparer.Ordinal)
            .ThenBy(file => file.Kind, StringComparer.Ordinal)];
    }

    public RepoGraphCentralPackageManagement GetCentralPackageManagement(string repositoryRoot)
    {
        IReadOnlyList<string> files = [.. EnumerateRepositoryFiles(repositoryRoot, "Directory.Packages.props")
            .Select(PathDisplay.FromCurrentDirectory)
            .OrderBy(path => path, StringComparer.Ordinal)];

        bool enabled = files
            .Select(path => GetFullPath(path, repositoryRoot))
            .Any(IsCentralPackageManagementEnabled);

        return new RepoGraphCentralPackageManagement(enabled, files);
    }

    private static ProjectFilePackageReference CreatePackageReference(
        XElement element,
        IReadOnlyDictionary<string, string> centralVersions)
    {
        string name = Attribute(element, "Include") ?? Attribute(element, "Update") ?? string.Empty;
        string? version = Attribute(element, "Version") ?? ChildValue(element, "Version");
        bool isCentralVersion = false;

        if (string.IsNullOrWhiteSpace(version) && centralVersions.TryGetValue(name, out string? centralVersion))
        {
            version = centralVersion;
            isCentralVersion = true;
        }

        return new ProjectFilePackageReference(
            Name: name,
            Version: version,
            IsCentralVersion: isCentralVersion,
            PrivateAssets: Attribute(element, "PrivateAssets") ?? ChildValue(element, "PrivateAssets"),
            IncludeAssets: Attribute(element, "IncludeAssets") ?? ChildValue(element, "IncludeAssets"),
            ExcludeAssets: Attribute(element, "ExcludeAssets") ?? ChildValue(element, "ExcludeAssets"));
    }

    private static ProjectFileProjectReference CreateProjectReference(XElement element, string projectDirectory)
    {
        string include = Attribute(element, "Include") ?? string.Empty;
        string? path = string.IsNullOrWhiteSpace(include)
            ? null
            : PathDisplay.FromCurrentDirectory(Path.GetFullPath(Path.Combine(projectDirectory, include)));

        return new ProjectFileProjectReference(include, path);
    }

    private static IReadOnlyDictionary<string, string> ReadCentralVersions(string projectFullPath, string repositoryRoot)
    {
        Dictionary<string, string> versions = new(StringComparer.Ordinal);
        string? directory = Path.GetDirectoryName(projectFullPath);
        while (!string.IsNullOrWhiteSpace(directory) &&
            Path.GetFullPath(directory).StartsWith(Path.GetFullPath(repositoryRoot), StringComparison.OrdinalIgnoreCase))
        {
            string centralPath = Path.Combine(directory, "Directory.Packages.props");
            if (File.Exists(centralPath))
            {
                foreach ((string name, string version) in ReadPackageVersions(centralPath))
                {
                    versions.TryAdd(name, version);
                }
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        return versions;
    }

    private static IEnumerable<(string Name, string Version)> ReadPackageVersions(string path)
    {
        XDocument document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        foreach (XElement element in Descendants(document.Root!, "PackageVersion"))
        {
            string? name = Attribute(element, "Include") ?? Attribute(element, "Update");
            string? version = Attribute(element, "Version") ?? ChildValue(element, "Version");
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(version))
            {
                yield return (name, version);
            }
        }
    }

    private static bool IsCentralPackageManagementEnabled(string path)
    {
        try
        {
            XDocument document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
            return IsTrue(GetProperty(document.Root!, "ManagePackageVersionsCentrally")) ||
                Descendants(document.Root!, "PackageVersion").Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return false;
        }
    }

    private static string? GetProperty(XElement root, string name)
    {
        return Descendants(root, name)
            .Select(element => element.Value.Trim())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static string? ChildValue(XElement element, string name)
    {
        return element.Elements()
            .Where(child => child.Name.LocalName == name)
            .Select(child => child.Value.Trim())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static IEnumerable<XElement> Descendants(XElement root, string localName)
    {
        return root.Descendants().Where(element => element.Name.LocalName == localName);
    }

    private static string? Attribute(XElement element, string name)
    {
        return element.Attribute(name)?.Value.Trim();
    }

    private static bool IsTrue(string? value)
    {
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> GetTargetFrameworks(string? targetFramework, string? targetFrameworks)
    {
        string combined = string.IsNullOrWhiteSpace(targetFrameworks) ? targetFramework ?? string.Empty : targetFrameworks;
        return [.. combined
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)];
    }

    private static IEnumerable<string> EnumerateRepositoryFiles(string repositoryRoot, string name)
    {
        return Directory.EnumerateFiles(repositoryRoot, name, SearchOption.AllDirectories)
            .Where(path => !IsIgnoredDirectory(path, repositoryRoot))
            .OrderBy(path => path, StringComparer.Ordinal);
    }

    private static bool IsIgnoredDirectory(string path, string repositoryRoot)
    {
        string relative = Path.GetRelativePath(repositoryRoot, path);
        string[] parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(part => part is ".git" or "bin" or "obj");
    }

    private static bool ProjectIsUnderDirectory(ProjectWithFacts project, string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(project.FullPath))
        {
            return false;
        }

        string projectDirectory = Path.GetDirectoryName(project.FullPath) ?? project.FullPath;
        string fullDirectory = Path.GetFullPath(directory);
        return Path.GetFullPath(projectDirectory).StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetFullPath(string path, string repositoryRoot)
    {
        string normalized = path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        return Path.IsPathRooted(normalized)
            ? Path.GetFullPath(normalized)
            : Path.GetFullPath(Path.Combine(repositoryRoot, normalized));
    }
}

internal sealed record ProjectWithFacts(RepoGraphProject Project, ProjectFileFacts FileFacts, string? FullPath);


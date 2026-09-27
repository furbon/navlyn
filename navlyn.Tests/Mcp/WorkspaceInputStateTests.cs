using Navlyn.Mcp.Execution;

namespace Navlyn.Tests.Mcp;

public sealed class WorkspaceInputStateTests
{
    [Fact]
    public void Capture_IsStableAndDetectsSameLengthContentChangeWithRestoredTimestamp()
    {
        using TemporaryDirectory directory = TemporaryDirectory.Create();
        string source = Write(directory.Path, "Program.cs", "class Alpha {}\n");
        DateTime originalTime = File.GetLastWriteTimeUtc(source);

        WorkspaceInputState first = Capture(directory.Path);
        WorkspaceInputState unchanged = Capture(directory.Path);
        File.WriteAllText(source, "class Bravo {}\n");
        File.SetLastWriteTimeUtc(source, originalTime);
        WorkspaceInputState edited = Capture(directory.Path);

        Assert.Equal(first.Digest, unchanged.Digest);
        Assert.NotEqual(first.Digest, edited.Digest);
        Assert.DoesNotContain("Alpha", edited.Digest, StringComparison.Ordinal);
        Assert.DoesNotContain("Bravo", edited.Digest, StringComparison.Ordinal);
        Assert.True(edited.IsComplete);
    }

    [Fact]
    public void Capture_DetectsImplicitSourceAddDeleteAndRename()
    {
        using TemporaryDirectory directory = TemporaryDirectory.Create();
        string firstSource = Write(directory.Path, "One.cs", "class One {}\n");
        string before = Capture(directory.Path).Digest;
        string secondSource = Write(directory.Path, "Two.cs", "class Two {}\n");
        string afterAdd = Capture(directory.Path).Digest;
        File.Move(secondSource, Path.Combine(directory.Path, "Three.cs"));
        string afterRename = Capture(directory.Path).Digest;
        File.Delete(firstSource);
        string afterDelete = Capture(directory.Path).Digest;

        Assert.NotEqual(before, afterAdd);
        Assert.NotEqual(afterAdd, afterRename);
        Assert.NotEqual(afterRename, afterDelete);
    }

    [Fact]
    public void Capture_HashesSelectedProjectAndAncestorConfigurationInputs()
    {
        using TemporaryDirectory directory = TemporaryDirectory.Create();
        string project = Write(directory.Path, "src/App.csproj", "<Project><PropertyGroup><DefineConstants>A</DefineConstants></PropertyGroup></Project>");
        string config = Write(directory.Path, "Directory.Build.props", "<Project><PropertyGroup><LangVersion>12</LangVersion></PropertyGroup></Project>");
        string workspace = Write(directory.Path, "app.slnx", "<Solution />");
        WorkspaceInputState before = WorkspaceInputState.Capture(directory.Path, [workspace, project], []);
        File.WriteAllText(project, "<Project><PropertyGroup><DefineConstants>B</DefineConstants></PropertyGroup></Project>");
        WorkspaceInputState projectChanged = WorkspaceInputState.Capture(directory.Path, [workspace, project], []);
        File.WriteAllText(config, "<Project><PropertyGroup><LangVersion>preview</LangVersion></PropertyGroup></Project>");
        WorkspaceInputState configChanged = WorkspaceInputState.Capture(directory.Path, [workspace, project], []);

        Assert.NotEqual(before.Digest, projectChanged.Digest);
        Assert.NotEqual(projectChanged.Digest, configChanged.Digest);
        Assert.Contains(config, configChanged.DiscoveredPaths, PathComparer);
    }

    [Fact]
    public void Capture_IncludesLoadedLinkedSourceOutsideRootAndReportsMissingKnownInput()
    {
        using TemporaryDirectory directory = TemporaryDirectory.Create();
        using TemporaryDirectory external = TemporaryDirectory.Create();
        string workspace = Write(directory.Path, "app.slnx", "<Solution />");
        string linked = Write(external.Path, "Shared.cs", "class Shared {}\n");
        WorkspaceInputState before = WorkspaceInputState.Capture(directory.Path, [workspace], [linked]);
        File.WriteAllText(linked, "class Changed {}\n");
        WorkspaceInputState changed = WorkspaceInputState.Capture(directory.Path, [workspace], [linked]);
        File.Delete(linked);
        WorkspaceInputState missing = WorkspaceInputState.Capture(directory.Path, [workspace], [linked]);

        Assert.NotEqual(before.Digest, changed.Digest);
        Assert.Contains(linked, before.DiscoveredPaths, PathComparer);
        Assert.False(missing.IsComplete);
        Assert.Contains(missing.Files, file => PathComparer.Equals(file.Path, linked) && !file.Exists && file.Error == "missing");
    }

    [Fact]
    public void Capture_SweepsAdditionalLoadedProjectRootForNewSourceAndConfigurationInputs()
    {
        using TemporaryDirectory directory = TemporaryDirectory.Create();
        using TemporaryDirectory projectRoot = TemporaryDirectory.Create();
        string workspace = Write(directory.Path, "workspace.slnx", "<Solution />");
        string project = Write(projectRoot.Path, "App.csproj", "<Project />");
        WorkspaceInputState before = WorkspaceInputState.Capture(directory.Path, [workspace, project], [], [projectRoot.Path]);
        string addedSource = Write(projectRoot.Path, "Added.cs", "class Added {}\n");
        string config = Write(projectRoot.Path, "Directory.Build.props", "<Project><PropertyGroup><X>1</X></PropertyGroup></Project>");
        WorkspaceInputState after = WorkspaceInputState.Capture(directory.Path, [workspace, project], [], [projectRoot.Path]);

        Assert.NotEqual(before.Digest, after.Digest);
        Assert.Contains(addedSource, after.DiscoveredPaths, PathComparer);
        Assert.Contains(config, after.DiscoveredPaths, PathComparer);
        Assert.DoesNotContain(before.DiscoveredPaths, path => PathComparer.Equals(path, addedSource));
    }

    [Fact]
    public void Capture_SweepsLoadedProjectRootsWithoutUnrelatedSolutionArtifacts()
    {
        using TemporaryDirectory directory = TemporaryDirectory.Create();
        string workspace = Write(directory.Path, "workspace.slnx", "<Solution />");
        string project = Write(directory.Path, "src/App.csproj", "<Project />");
        string projectSource = Write(directory.Path, "src/App.cs", "class App {}\n");
        string unrelated = Write(directory.Path, "artifacts/Clone.cs", "class Clone {}\n");

        WorkspaceInputState state = WorkspaceInputState.Capture(directory.Path, [workspace, project], [],
            [Path.GetDirectoryName(project)!]);

        Assert.Contains(state.DiscoveredPaths, path => PathComparer.Equals(path, Path.GetFullPath(projectSource)));
        Assert.DoesNotContain(state.DiscoveredPaths, path => PathComparer.Equals(path, Path.GetFullPath(unrelated)));
    }

    [Fact]
    public void Capture_ThrowsCancellationInsteadOfReturningPartialInventory()
    {
        using TemporaryDirectory directory = TemporaryDirectory.Create();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => WorkspaceInputState.Capture(directory.Path, [], [], cancellationToken: cancellation.Token));
    }

    [Fact]
    public void Capture_FailsClosedForExternalAndCyclicJunctionDirectories()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TemporaryDirectory directory = TemporaryDirectory.Create();
        using TemporaryDirectory external = TemporaryDirectory.Create();
        Directory.CreateDirectory(directory.Path);
        Write(external.Path, "Existing.cs", "class Existing {}\n");
        string externalJunction = Path.Combine(directory.Path, "External");
        string cyclicJunction = Path.Combine(directory.Path, "Cycle");
        CreateJunction(externalJunction, external.Path);
        try
        {
            CreateJunction(cyclicJunction, directory.Path);
            WorkspaceInputState initial = Capture(directory.Path);
            Write(external.Path, "Added.cs", "class Added {}\n");
            WorkspaceInputState afterAdd = Capture(directory.Path);

            Assert.False(initial.IsComplete);
            Assert.False(afterAdd.IsComplete);
            Assert.Contains(initial.Errors, error => error.Contains("reparse directory", StringComparison.Ordinal));
            Assert.Contains(afterAdd.Errors, error => error.Contains("reparse directory", StringComparison.Ordinal));
        }
        finally
        {
            DeleteJunction(cyclicJunction);
            DeleteJunction(externalJunction);
        }
    }

    [Fact]
    public void Capture_AllowsInputsBelowSymlinkedAncestorOutsideSweepRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using TemporaryDirectory directory = TemporaryDirectory.Create();
        string realRoot = Path.Combine(directory.Path, "real");
        string projectRoot = Path.Combine(realRoot, "project");
        Directory.CreateDirectory(projectRoot);
        string link = Path.Combine(directory.Path, "link");
        Directory.CreateSymbolicLink(link, realRoot);
        try
        {
            string linkedRoot = Path.Combine(link, "project");
            string source = Write(linkedRoot, "Program.cs", "class Program {}\n");

            WorkspaceInputState state = Capture(linkedRoot);

            Assert.True(state.IsComplete, string.Join("\n", state.Errors));
            Assert.Contains(source, state.DiscoveredPaths, PathComparer);
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    private static WorkspaceInputState Capture(string root) => WorkspaceInputState.Capture(root, [], []);

    private static string Write(string directory, string name, string content)
    {
        string path = Path.Combine(directory, name);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static void CreateJunction(string link, string target)
    {
        using System.Diagnostics.Process process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            ArgumentList = { "/c", "mklink", "/J", link, target },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("Could not start mklink.");
        Assert.True(process.WaitForExit(10000), "Junction creation timed out.");
        Assert.Equal(0, process.ExitCode);
        Assert.True((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0);
    }

    private static void DeleteJunction(string link)
    {
        if (Directory.Exists(link))
        {
            Assert.True((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0);
            Directory.Delete(link);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path) => Path = path;

        public string Path { get; }

        public static TemporaryDirectory Create() => new(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"navlyn-inputs-{Guid.NewGuid():N}"));

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}

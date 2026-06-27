using Navlyn.Workspaces;

namespace Navlyn.Tests.TestSupport;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ResolverComponentTestCollection : ICollectionFixture<ResolverComponentTestFixture>
{
    public const string Name = "Resolver component tests";
}

public sealed class ResolverComponentTestFixture : IAsyncLifetime
{
    public string RepoRoot { get; private set; } = string.Empty;

    internal LoadedWorkspace SymbolNavigationWorkspace { get; private set; } = null!;

    internal LoadedWorkspace FuzzyDiscoveryWorkspace { get; private set; } = null!;

    internal LoadedWorkspace DiagnosticWorkspace { get; private set; } = null!;

    public SourceFixtureFile SymbolNavigationSource { get; private set; } = null!;

    public SourceFixtureFile FuzzyDiscoverySource { get; private set; } = null!;

    public SourceFixtureFile DiagnosticSource { get; private set; } = null!;

    public SourceFixtureFile GeneratedDiagnosticSource { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        RepoRoot = FindRepositoryRoot();
        SymbolNavigationSource = SourceFile("tests", "fixtures", "SymbolNavigationFixture", "FixtureCode.cs");
        FuzzyDiscoverySource = SourceFile("tests", "fixtures", "FuzzyDiscoveryFixture", "FixtureCode.cs");
        DiagnosticSource = SourceFile("tests", "fixtures", "DiagnosticFixture", "BrokenCode.cs");
        GeneratedDiagnosticSource = SourceFile("tests", "fixtures", "DiagnosticFixture", "GeneratedBroken.g.cs");

        SymbolNavigationWorkspace = await LoadWorkspaceAsync("tests", "fixtures", "SymbolNavigationFixture", "SymbolNavigationFixture.csproj");
        FuzzyDiscoveryWorkspace = await LoadWorkspaceAsync("tests", "fixtures", "FuzzyDiscoveryFixture", "FuzzyDiscoveryFixture.csproj");
        DiagnosticWorkspace = await LoadWorkspaceAsync("tests", "fixtures", "DiagnosticFixture", "DiagnosticFixture.csproj");
    }

    public Task DisposeAsync()
    {
        SymbolNavigationWorkspace?.Dispose();
        FuzzyDiscoveryWorkspace?.Dispose();
        DiagnosticWorkspace?.Dispose();
        return Task.CompletedTask;
    }

    public SourceFixtureFile SourceFile(params string[] relativeParts)
    {
        return new SourceFixtureFile(RepoRoot, Path.Combine(relativeParts));
    }

    private async Task<LoadedWorkspace> LoadWorkspaceAsync(params string[] relativeParts)
    {
        string path = Path.Combine([RepoRoot, .. relativeParts]);
        WorkspaceLoadResult result = await new WorkspaceLoader().LoadAsync(new FileInfo(path), CancellationToken.None);

        Assert.Null(result.Error);
        return Assert.IsType<LoadedWorkspace>(result.Workspace);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "navlyn.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repository root from the test output directory.");
    }
}

public sealed class SourceFixtureFile
{
    private readonly string[] lines;

    public SourceFixtureFile(string repoRoot, string relativePath)
    {
        RelativePath = relativePath;
        FullPath = Path.GetFullPath(Path.Combine(repoRoot, relativePath));
        File = new FileInfo(FullPath);
        lines = System.IO.File.ReadAllLines(FullPath);
    }

    public FileInfo File { get; }

    public string FullPath { get; }

    public string RelativePath { get; }

    public SourcePosition Position(string lineContains, string target, int occurrence = 1)
    {
        if (occurrence < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(occurrence), occurrence, "Occurrence must be 1 or greater.");
        }

        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            string line = lines[lineIndex];
            if (!line.Contains(lineContains, StringComparison.Ordinal))
            {
                continue;
            }

            int searchStart = 0;
            for (int matchIndex = 1; matchIndex <= occurrence; matchIndex++)
            {
                int columnIndex = line.IndexOf(target, searchStart, StringComparison.Ordinal);
                if (columnIndex < 0)
                {
                    break;
                }

                if (matchIndex == occurrence)
                {
                    return new SourcePosition(
                        Line: lineIndex + 1,
                        Column: columnIndex + 1,
                        EndLine: lineIndex + 1,
                        EndColumn: columnIndex + target.Length + 1);
                }

                searchStart = columnIndex + target.Length;
            }
        }

        throw new InvalidOperationException(
            $"Could not find occurrence {occurrence} of '{target}' on a line containing '{lineContains}'.");
    }

    public int Line(string lineContains)
    {
        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            if (lines[lineIndex].Contains(lineContains, StringComparison.Ordinal))
            {
                return lineIndex + 1;
            }
        }

        throw new InvalidOperationException($"Could not find a line containing '{lineContains}'.");
    }

    public int EndColumn(int line)
    {
        return lines[line - 1].Length + 1;
    }
}

public readonly record struct SourcePosition(int Line, int Column, int EndLine, int EndColumn);

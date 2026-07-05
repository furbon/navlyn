using System.Diagnostics;
using System.Text;
using Navlyn.Diagnostics;
using Navlyn.GeneratedCode;
using Navlyn.Languages;
using Navlyn.Paths;

namespace Navlyn.PublicApi;

internal sealed class GitSourceSnapshotReader
{
    public async Task<GitSourceReadResult> ReadRefAsync(
        string repositoryRoot,
        string gitRef,
        bool excludeGenerated,
        CancellationToken cancellationToken)
    {
        GitCommandResult filesResult = await RunGitAsync(repositoryRoot, ["ls-tree", "-r", "--name-only", gitRef], cancellationToken);
        if (filesResult.ExitCode != 0)
        {
            return GitSourceReadResult.Failed(
                DiagnosticIds.GitCommandFailed,
                $"Git ls-tree failed for ref '{gitRef}': {filesResult.Stderr.Trim()}",
                ExitCodes.UsageError);
        }

        List<GitSourceFile> files = [];
        foreach (string path in filesResult.Stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(SourceLanguageFacts.IsSupportedSourceFile)
            .Where(path => !IsBuildOutputPath(path))
            .Where(path => !excludeGenerated || !GeneratedCodeFacts.IsGeneratedPath(path))
            .OrderBy(path => path, StringComparer.Ordinal))
        {
            GitCommandResult showResult = await RunGitAsync(repositoryRoot, ["show", $"{gitRef}:{path}"], cancellationToken);
            if (showResult.ExitCode != 0)
            {
                return GitSourceReadResult.Failed(
                    DiagnosticIds.GitCommandFailed,
                    $"Git show failed for '{gitRef}:{path}': {showResult.Stderr.Trim()}",
                    ExitCodes.UsageError);
            }

            files.Add(new GitSourceFile(path.Replace('\\', '/'), showResult.Stdout));
        }

        return GitSourceReadResult.Succeeded(files);
    }

    public IReadOnlyList<GitSourceFile> ReadWorkingTree(
        IReadOnlyList<Microsoft.CodeAnalysis.Project> projects,
        bool excludeGenerated)
    {
        return [.. projects
            .SelectMany(project => project.Documents.Select(document => document.FilePath))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(SourceLanguageFacts.IsSupportedSourceFile)
            .Where(path => File.Exists(path))
            .Where(path => !IsBuildOutputPath(PathDisplay.FromCurrentDirectory(path)))
            .Where(path => !excludeGenerated || !GeneratedCodeFacts.IsGeneratedPath(path))
            .OrderBy(path => PathDisplay.FromCurrentDirectory(path), StringComparer.Ordinal)
            .Select(path => new GitSourceFile(PathDisplay.FromCurrentDirectory(path), File.ReadAllText(path)))];
    }

    private static async Task<GitCommandResult> RunGitAsync(
        string repositoryRoot,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "git",
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(repositoryRoot);
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start git process.");
        string stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        string stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new GitCommandResult(process.ExitCode, stdout, stderr);
    }

    private static bool IsBuildOutputPath(string path)
    {
        string[] parts = path.Split('/', '\\');
        return parts.Any(part => part is "bin" or "obj");
    }
}

internal sealed record GitSourceReadResult(IReadOnlyList<GitSourceFile> Files, PublicApiDiffError? Error)
{
    public static GitSourceReadResult Succeeded(IReadOnlyList<GitSourceFile> files)
    {
        return new GitSourceReadResult(files, Error: null);
    }

    public static GitSourceReadResult Failed(int diagnosticId, string message, int exitCode)
    {
        return new GitSourceReadResult([], new PublicApiDiffError(diagnosticId, message, exitCode));
    }
}

internal sealed record GitSourceFile(string Path, string Text);

internal sealed record GitCommandResult(int ExitCode, string Stdout, string Stderr);


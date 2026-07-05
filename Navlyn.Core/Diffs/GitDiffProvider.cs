using System.Diagnostics;
using System.Text;
using Navlyn.Diagnostics;

namespace Navlyn.Diffs;

internal interface IDiffProvider
{
    Task<DiffReadResult> ReadAsync(string repositoryRoot, DiffRequest request, CancellationToken cancellationToken);
}

internal sealed class GitDiffProvider : IDiffProvider
{
    public async Task<DiffReadResult> ReadAsync(
        string repositoryRoot,
        DiffRequest request,
        CancellationToken cancellationToken)
    {
        List<string> arguments = ["-C", repositoryRoot, "diff", "--unified=0", "--no-ext-diff"];

        if (request.Staged)
        {
            arguments.Add("--cached");
        }
        else if (request.Base is not null && request.Head is not null)
        {
            arguments.Add(request.Base);
            arguments.Add(request.Head);
        }
        else if (request.Base is not null)
        {
            arguments.Add(request.Base);
        }

        arguments.Add("--");

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

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start git process.");

            string stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            string stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                return DiffReadResult.Failed(
                    DiagnosticIds.GitCommandFailed,
                    $"Git diff failed with exit code {process.ExitCode}: {stderr.Trim()}",
                    ExitCodes.UsageError);
            }

            DiffReadResult parsed = new UnifiedDiffParser().Parse(stdout, request);
            if (parsed.Error is not null || !request.IncludeUnstaged)
            {
                return parsed;
            }

            IReadOnlyList<DiffFile> untrackedFiles = await ReadUntrackedFilesAsync(repositoryRoot, cancellationToken);
            if (untrackedFiles.Count == 0)
            {
                return parsed;
            }

            IReadOnlyList<DiffFile> files = [.. parsed.Diff!.Files
                .Concat(untrackedFiles)
                .GroupBy(file => file.Path, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(file => file.Path, StringComparer.Ordinal)
                .ThenBy(file => file.Status, StringComparer.Ordinal)];

            return DiffReadResult.Succeeded(parsed.Diff with
            {
                TotalFiles = files.Count,
                Files = files
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            return DiffReadResult.Failed(
                DiagnosticIds.GitCommandFailed,
                $"Failed to run git diff: {ex.Message}",
                ExitCodes.UsageError);
        }
    }

    private static async Task<IReadOnlyList<DiffFile>> ReadUntrackedFilesAsync(
        string repositoryRoot,
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

        foreach (string argument in new[] { "-C", repositoryRoot, "ls-files", "--others", "--exclude-standard" })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start git process.");
        string stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            return [];
        }

        List<DiffFile> files = [];
        foreach (string line in stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string path = line.Replace('\\', '/');
            string fullPath = Path.Combine(repositoryRoot, line.Replace('/', Path.DirectorySeparatorChar));
            int lineCount = Navlyn.Languages.SourceLanguageFacts.IsSupportedSourceFile(path) && File.Exists(fullPath)
                ? File.ReadLines(fullPath).Count()
                : 0;
            files.Add(new DiffFile(
                Path: path,
                OldPath: null,
                Status: "added",
                Hunks: lineCount == 0 ? [] : [new DiffHunk(0, 0, 1, lineCount)]));
        }

        return files;
    }
}

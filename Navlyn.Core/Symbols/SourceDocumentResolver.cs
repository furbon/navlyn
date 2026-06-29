using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Navlyn.Diagnostics;
using Navlyn.GeneratedCode;
using Navlyn.Paths;

namespace Navlyn.Symbols;

internal sealed class SourceDocumentResolver
{
    public async Task<SourceDocumentResolutionResult> ResolveAsync(
        Solution solution,
        FileInfo file,
        Project? project,
        bool excludeGenerated,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> sourcePaths;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            sourcePaths = PathDisplay.GetInputPathCandidates(file.ToString(), GetWorkspaceAnchorPath(solution));
        }
        catch (OperationCanceledException)
        {
            return SourceDocumentResolutionResult.Failed(
                DiagnosticIds.OperationCanceled,
                "Operation canceled.",
                ExitCodes.Failure);
        }
        catch (Exception ex)
        {
            return SourceDocumentResolutionResult.Failed(
                DiagnosticIds.InvalidSourceFilePath,
                $"Invalid source file path: {ex.Message}",
                ExitCodes.UsageError);
        }

        Document? document = FindDocument(solution, sourcePaths, project, out string? sourcePath);
        sourcePath ??= sourcePaths[0];

        if (excludeGenerated && GeneratedCodeFacts.IsGeneratedPath(sourcePath))
        {
            return SourceDocumentResolutionResult.Failed(
                DiagnosticIds.SourceFileExcludedByGeneratedCodeFilter,
                $"Source file is excluded by --exclude-generated: {PathDisplay.FromCurrentDirectory(sourcePath)}",
                ExitCodes.UsageError);
        }

        if (document is null)
        {
            if (project is not null && ContainsDocument(solution, sourcePaths))
            {
                return SourceDocumentResolutionResult.Failed(
                    DiagnosticIds.SourceFileNotInProject,
                    $"Source file is not part of the selected project '{project.Name}': {PathDisplay.FromCurrentDirectory(sourcePath)}",
                    ExitCodes.UsageError);
            }

            return SourceDocumentResolutionResult.Failed(
                DiagnosticIds.SourceFileNotInWorkspace,
                $"Source file is not part of the workspace: {PathDisplay.FromCurrentDirectory(sourcePath)}",
                ExitCodes.UsageError);
        }

        SourceText text = await document.GetTextAsync(cancellationToken);
        return SourceDocumentResolutionResult.Succeeded(new SourceDocumentResolution(
            DisplayPath: PathDisplay.FromCurrentDirectory(sourcePath),
            Document: document,
            Text: text));
    }

    private static Document? FindDocument(
        Solution solution,
        IReadOnlyList<string> sourcePaths,
        Project? project,
        out string? matchedSourcePath)
    {
        matchedSourcePath = null;
        StringComparer pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        IEnumerable<Project> projects = project is null
            ? solution.Projects
            : [project];

        foreach (Document document in projects
            .OrderBy(project => project.FilePath, StringComparer.Ordinal)
            .ThenBy(project => project.Name, StringComparer.Ordinal)
            .SelectMany(project => project.Documents
                .Where(document => document.FilePath is not null)
                .OrderBy(document => document.FilePath, StringComparer.Ordinal)
                .ThenBy(document => document.Name, StringComparer.Ordinal)))
        {
            string documentPath = Path.GetFullPath(document.FilePath!);
            string? matchingSourcePath = sourcePaths.FirstOrDefault(sourcePath => pathComparer.Equals(documentPath, sourcePath));
            if (matchingSourcePath is not null)
            {
                matchedSourcePath = matchingSourcePath;
                return document;
            }
        }

        return null;
    }

    private static bool ContainsDocument(Solution solution, IReadOnlyList<string> sourcePaths)
    {
        return FindDocument(solution, sourcePaths, project: null, out _) is not null;
    }

    private static string? GetWorkspaceAnchorPath(Solution solution)
    {
        return solution.FilePath ?? solution.Projects
            .Select(project => project.FilePath)
            .FirstOrDefault(path => path is not null);
    }
}

internal sealed record SourceDocumentResolutionResult(SourceDocumentResolution? Resolution, SymbolNavigationError? Error)
{
    public static SourceDocumentResolutionResult Succeeded(SourceDocumentResolution resolution)
    {
        return new SourceDocumentResolutionResult(resolution, Error: null);
    }

    public static SourceDocumentResolutionResult Failed(int diagnosticId, string message, int exitCode)
    {
        return new SourceDocumentResolutionResult(
            Resolution: null,
            Error: new SymbolNavigationError(diagnosticId, message, exitCode));
    }
}

internal sealed record SourceDocumentResolution(
    string DisplayPath,
    Document Document,
    SourceText Text);

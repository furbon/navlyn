using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Navlyn.Diagnostics;
using Navlyn.GeneratedCode;
using Navlyn.Paths;
using Navlyn.Workspaces;

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

        DocumentIndex documentIndex = DocumentIndexProvider.GetOrCreate(solution);
        DocumentIndexLookupResult lookup = documentIndex.Find(sourcePaths, project);
        Document? document = lookup.Entry?.Document;
        string sourcePath = lookup.MatchedSourcePath ?? sourcePaths[0];

        if (excludeGenerated && GeneratedCodeFacts.IsGeneratedPath(sourcePath))
        {
            return SourceDocumentResolutionResult.Failed(
                DiagnosticIds.SourceFileExcludedByGeneratedCodeFilter,
                $"Source file is excluded by --exclude-generated: {PathDisplay.FromCurrentDirectory(sourcePath)}",
                ExitCodes.UsageError);
        }

        if (document is null)
        {
            if (project is not null && documentIndex.Contains(sourcePaths))
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

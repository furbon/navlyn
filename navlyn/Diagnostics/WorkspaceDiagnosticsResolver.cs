using Microsoft.CodeAnalysis;
using Navlyn.GeneratedCode;
using Navlyn.Paths;
using Navlyn.Workspaces;

namespace Navlyn.Diagnostics;

internal sealed class WorkspaceDiagnosticsResolver
{
    public async Task<WorkspaceDiagnosticsResolution> ResolveAsync(
        IReadOnlyList<Project> projects,
        bool excludeGenerated,
        CancellationToken cancellationToken)
    {
        List<WorkspaceDiagnosticResult> diagnostics = [];

        foreach (Project project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Compilation? compilation = await project.GetCompilationAsync(cancellationToken);
            if (compilation is null)
            {
                continue;
            }

            WorkspaceDiagnosticProject projectInfo = new(
                Name: project.Name,
                Path: project.FilePath is null ? null : PathDisplay.FromCurrentDirectory(project.FilePath),
                TargetFramework: ProjectContextFacts.GetTargetFramework(project));

            diagnostics.AddRange(compilation
                .GetDiagnostics(cancellationToken)
                .Where(diagnostic => !excludeGenerated || !IsGeneratedDiagnostic(diagnostic))
                .Select(diagnostic => CreateDiagnostic(projectInfo, diagnostic)));
        }

        return new WorkspaceDiagnosticsResolution(
            Diagnostics: [.. diagnostics
                .OrderBy(diagnostic => diagnostic.Path, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.Line)
                .ThenBy(diagnostic => diagnostic.Column)
                .ThenBy(diagnostic => diagnostic.Project.Path, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.Project.Name, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.Id, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)]);
    }

    private static WorkspaceDiagnosticResult CreateDiagnostic(
        WorkspaceDiagnosticProject project,
        Diagnostic diagnostic)
    {
        string? path = null;
        int? line = null;
        int? column = null;
        int? endLine = null;
        int? endColumn = null;

        if (diagnostic.Location.IsInSource)
        {
            FileLinePositionSpan lineSpan = diagnostic.Location.GetLineSpan();
            if (lineSpan.IsValid)
            {
                path = PathDisplay.FromCurrentDirectory(lineSpan.Path);
                line = lineSpan.StartLinePosition.Line + 1;
                column = lineSpan.StartLinePosition.Character + 1;
                endLine = lineSpan.EndLinePosition.Line + 1;
                endColumn = lineSpan.EndLinePosition.Character + 1;
            }
        }

        return new WorkspaceDiagnosticResult(
            Project: project,
            Severity: diagnostic.Severity.ToString(),
            Id: diagnostic.Id,
            Message: diagnostic.GetMessage(),
            Path: path,
            Line: line,
            Column: column,
            EndLine: endLine,
            EndColumn: endColumn);
    }

    private static bool IsGeneratedDiagnostic(Diagnostic diagnostic)
    {
        return diagnostic.Location.IsInSource &&
            GeneratedCodeFacts.IsGeneratedPath(diagnostic.Location.GetLineSpan().Path);
    }
}

internal sealed record WorkspaceDiagnosticsResolution(IReadOnlyList<WorkspaceDiagnosticResult> Diagnostics);

internal sealed record WorkspaceDiagnosticProject(string Name, string? Path, string? TargetFramework);

internal sealed record WorkspaceDiagnosticResult(
    WorkspaceDiagnosticProject Project,
    string Severity,
    string Id,
    string Message,
    string? Path,
    int? Line,
    int? Column,
    int? EndLine,
    int? EndColumn);

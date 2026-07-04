using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Navlyn.Diagnostics;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static partial class BatchCommand
{
    private sealed record BatchInput(BatchDefaults Defaults, IReadOnlyList<BatchRequest> Requests);

    private sealed record BatchDefaults(string? Project, bool? ExcludeGenerated);

    private sealed record BatchRequest(string Id, string Command, JsonElement Payload)
    {
        public BatchRequestResult Success(object result)
        {
            return BatchRequestResult.Success(Id, Command, result);
        }

        public BatchRequestResult Failed(BatchError error)
        {
            return BatchRequestResult.Failed(Id, Command, error);
        }

        public BatchRequestResult Failed(int diagnosticId, string message)
        {
            return BatchRequestResult.Failed(Id, Command, diagnosticId, message);
        }

        public BatchRequestResult Failed(ProjectFilterResolutionError error)
        {
            return BatchRequestResult.Failed(Id, Command, error.DiagnosticId, error.Message);
        }

        public BatchRequestResult Failed(SymbolsInResolutionError error)
        {
            return BatchRequestResult.Failed(Id, Command, error.DiagnosticId, error.Message);
        }

        public BatchRequestResult Failed(SymbolAtResolutionError error)
        {
            return BatchRequestResult.Failed(Id, Command, error.DiagnosticId, error.Message);
        }

        public BatchRequestResult Failed(SymbolInfoResolutionError error)
        {
            return BatchRequestResult.Failed(Id, Command, error.DiagnosticId, error.Message);
        }

        public BatchRequestResult Failed(OutlineResolutionError error)
        {
            return BatchRequestResult.Failed(Id, Command, error.DiagnosticId, error.Message);
        }

        public BatchRequestResult Failed(DefinitionResolutionError error)
        {
            return BatchRequestResult.Failed(Id, Command, error.DiagnosticId, error.Message);
        }

        public BatchRequestResult Failed(ReferencesResolutionError error)
        {
            return BatchRequestResult.Failed(Id, Command, error.DiagnosticId, error.Message);
        }

        public BatchRequestResult Failed(ImplementationsResolutionError error)
        {
            return BatchRequestResult.Failed(Id, Command, error.DiagnosticId, error.Message);
        }

        public BatchRequestResult Failed(TypeHierarchyResolutionError error)
        {
            return BatchRequestResult.Failed(Id, Command, error.DiagnosticId, error.Message);
        }

        public BatchRequestResult Failed(CallHierarchyResolutionError error)
        {
            return BatchRequestResult.Failed(Id, Command, error.DiagnosticId, error.Message);
        }
    }

    private sealed record SourcePositionBatchOptions(
        FileInfo File,
        int Line,
        int Column,
        Project? Project,
        ProjectFilterOutput? ProjectFilter,
        bool ExcludeGenerated,
        CandidateSelectionInput? SelectionInput);

    private sealed record SourcePositionBatchResolution(SourcePositionBatchOptions? Options, BatchError? Error)
    {
        public static SourcePositionBatchResolution Succeeded(SourcePositionBatchOptions options)
        {
            return new SourcePositionBatchResolution(options, Error: null);
        }

        public static SourcePositionBatchResolution Failed(BatchError error)
        {
            return new SourcePositionBatchResolution(Options: null, error);
        }
    }

    private sealed record NavigationResultBatchOptions(
        NavigationResultFilter Filter,
        IReadOnlyList<ProjectFilterOutput>? ProjectFilters);

    private sealed record SymbolSelectionInput(
        string? Query,
        string? CandidateId,
        FileInfo? File,
        int? Line,
        int? Column)
    {
        public bool IsSourcePosition => File is not null || Line is not null || Column is not null;
    }

    private sealed record BatchResult(
        string Workspace,
        string Kind,
        int TotalRequests,
        int SucceededRequests,
        int FailedRequests,
        IReadOnlyList<BatchRequestResult> Results);

    private sealed record BatchRequestResult(
        string Id,
        string Command,
        bool Ok,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        object? Result,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        BatchError? Error)
    {
        public static BatchRequestResult Success(string id, string command, object result)
        {
            return new BatchRequestResult(id, command, Ok: true, result, Error: null);
        }

        public static BatchRequestResult Failed(string id, string command, BatchError error)
        {
            return new BatchRequestResult(id, command, Ok: false, Result: null, error);
        }

        public static BatchRequestResult Failed(string id, string command, int diagnosticId, string message)
        {
            return Failed(id, command, BatchError.FromDiagnostic(diagnosticId, message));
        }
    }

    private sealed record BatchError(string Code, string Message)
    {
        public static BatchError FromDiagnostic(int diagnosticId, string message)
        {
            return new BatchError($"{DiagnosticIds.Prefix}{diagnosticId:D4}", message);
        }
    }

    private sealed record OverviewResult(
        string Workspace,
        string Kind,
        IReadOnlyList<OverviewProjectResult> Projects);

    private sealed record OverviewProjectResult(
        string Name,
        string? Path,
        string Language,
        string? AssemblyName,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? TargetFramework,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? LanguageVersion,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<string>? PreprocessorSymbols);

    private sealed record DiagnosticsResult(
        string Workspace,
        string Kind,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<ProjectFilterOutput>? Projects,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<string>? Severities,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<string>? Ids,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool ExcludeGenerated,
        int? Limit,
        int TotalDiagnostics,
        IReadOnlyList<DiagnosticResult> Diagnostics);

    private sealed record DiagnosticResult(
        DiagnosticProjectResult Project,
        string Severity,
        string Id,
        string Message,
        string? Path,
        int? Line,
        int? Column,
        int? EndLine,
        int? EndColumn);

    private sealed record DiagnosticProjectResult(
        string Name,
        string? Path,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? TargetFramework);

    private sealed record SymbolsResult(
        string Query,
        string Match,
        bool CaseSensitive,
        IReadOnlyList<string> Kinds,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<string>? Namespaces,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? NamespaceMatch,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<string>? Containers,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? ContainerMatch,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<string>? Accessibilities,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<ProjectFilterOutput>? Projects,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool ExcludeGenerated,
        int? Limit,
        int TotalMatches,
        IReadOnlyList<SymbolMatchResult> Matches);

    private sealed record SymbolMatchResult(
        string Name,
        string Kind,
        string? Container,
        SymbolFacts Facts,
        string Path,
        int Line,
        int Column,
        int EndLine,
        int EndColumn);

    private sealed record SymbolsInResult(
        string File,
        int Line,
        int StartColumn,
        int EndColumn,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        ProjectFilterOutput? Project,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool ExcludeGenerated,
        IReadOnlyList<SymbolsInSymbolResult> Symbols);

    private sealed record SymbolsInSymbolResult(
        string Name,
        string Kind,
        string? Container,
        SymbolFacts Facts,
        int Line,
        int Column,
        int EndLine,
        int EndColumn);

    private sealed record SymbolAtResult(
        string File,
        int Line,
        int Column,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        ProjectFilterOutput? Project,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        CandidateSelectionInput? SelectionInput,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool ExcludeGenerated,
        SymbolAtSymbolResult Symbol);

    private sealed record SymbolAtSymbolResult(
        string Name,
        string Kind,
        string? Container,
        SymbolFacts Facts,
        string? Path,
        int? Line,
        int? Column,
        int? EndLine,
        int? EndColumn);

    private sealed record SymbolInfoResult(
        string File,
        int Line,
        int Column,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        ProjectFilterOutput? Project,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        CandidateSelectionInput? SelectionInput,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool ExcludeGenerated,
        SymbolInfoSymbol Symbol,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        SymbolExpressionInfo? Expression,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        SymbolInfoSymbol? ContainingSymbol,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        SymbolInvocationInfo? Invocation,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        SymbolAttributeInfo? Attribute,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        SymbolReturnInfo? Return,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        SymbolLambdaInfo? Lambda);

    private sealed record OutlineResult(
        string File,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        ProjectFilterOutput? Project,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool ExcludeGenerated,
        IReadOnlyList<OutlineEntryResult> Entries);

    private sealed record OutlineEntryResult(
        string Name,
        string Kind,
        string? Container,
        SymbolFacts Facts,
        string Path,
        int Line,
        int Column,
        int EndLine,
        int EndColumn);

    private sealed record DefinitionResult(
        string File,
        int Line,
        int Column,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        ProjectFilterOutput? Project,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        CandidateSelectionInput? SelectionInput,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool ExcludeGenerated,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool IncludeMetadata,
        SourceSymbolResult Symbol,
        IReadOnlyList<SourceLocationResult> Definitions);

    private sealed record ReferencesResult(
        string File,
        int Line,
        int Column,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        ProjectFilterOutput? Project,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        CandidateSelectionInput? SelectionInput,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<ProjectFilterOutput>? ResultProjects,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<string>? ResultPaths,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<string>? ResultKinds,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<string>? UsageKinds,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<string>? GroupBy,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool ExcludeGenerated,
        int? Limit,
        int TotalMatches,
        SymbolNavigationSearchMetadata Search,
        IReadOnlyList<ReferenceUsageCount> UsageKindCounts,
        SourceSymbolResult Symbol,
        IReadOnlyList<ReferenceLocationResult> References,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<ReferenceUsageGroup>? Groups);

    private sealed record ImplementationsResult(
        string File,
        int Line,
        int Column,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        ProjectFilterOutput? Project,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        CandidateSelectionInput? SelectionInput,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<ProjectFilterOutput>? ResultProjects,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<string>? ResultPaths,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<string>? ResultKinds,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool ExcludeGenerated,
        int? Limit,
        int TotalMatches,
        SourceSymbolResult Symbol,
        IReadOnlyList<ImplementationLocationResult> Implementations);

    private sealed record TypeHierarchyResult(
        string File,
        int Line,
        int Column,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        ProjectFilterOutput? Project,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        CandidateSelectionInput? SelectionInput,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool ExcludeGenerated,
        HierarchySymbol Symbol,
        IReadOnlyList<HierarchySymbol> BaseTypes,
        IReadOnlyList<HierarchySymbol> Interfaces,
        IReadOnlyList<HierarchySymbol> DerivedTypes,
        IReadOnlyList<HierarchySymbol> ImplementingTypes,
        IReadOnlyList<HierarchySymbol> BaseMembers,
        IReadOnlyList<HierarchySymbol> OverridingMembers,
        IReadOnlyList<HierarchySymbol> ImplementedMembers);

    private sealed record CallersResult(
        string File,
        int Line,
        int Column,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        ProjectFilterOutput? Project,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        CandidateSelectionInput? SelectionInput,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<ProjectFilterOutput>? ResultProjects,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<string>? ResultPaths,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<string>? ResultKinds,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool ExcludeGenerated,
        int? Limit,
        int TotalGroups,
        SymbolNavigationSearchMetadata Search,
        CallHierarchySymbolResult Symbol,
        IReadOnlyList<CallHierarchyGroupResult> Callers);

    private sealed record CallsResult(
        string File,
        int Line,
        int Column,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        ProjectFilterOutput? Project,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        CandidateSelectionInput? SelectionInput,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<ProjectFilterOutput>? ResultProjects,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<string>? ResultPaths,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<string>? ResultKinds,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool ExcludeGenerated,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool IncludeMetadata,
        int? Limit,
        int TotalGroups,
        SymbolNavigationSearchMetadata Search,
        CallHierarchySymbolResult Caller,
        IReadOnlyList<CallHierarchyGroupResult> Calls);

    private sealed record SourceSymbolResult(string Name, string Kind, string? Container, SymbolFacts Facts);

    private sealed record SourceLocationResult(string Path, int Line, int Column, int EndLine, int EndColumn);

    private sealed record SourceSymbolLocationResult(
        string Name,
        string Kind,
        string? Container,
        SymbolFacts Facts,
        string? Path,
        int? Line,
        int? Column,
        int? EndLine,
        int? EndColumn);

    private sealed record ReferenceLocationResult(
        string Path,
        int Line,
        int Column,
        int EndLine,
        int EndColumn,
        string UsageKind,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        SourceSymbolLocationResult? ContainingSymbol);

    private sealed record ImplementationLocationResult(
        string Name,
        string Kind,
        string? Container,
        SymbolFacts Facts,
        string Path,
        int Line,
        int Column,
        int EndLine,
        int EndColumn);
}

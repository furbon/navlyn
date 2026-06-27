namespace Navlyn.Diagnostics;

internal static class DiagnosticIds
{
    public const string Prefix = "NAVLYN";

    public const int ParseError = 1001;
    public const int InvalidRegex = 1002;
    public const int InvalidLimit = 1003;
    public const int InvalidSymbolKind = 1004;
    public const int InvalidProjectFilter = 1005;
    public const int UnknownProjectFilter = 1006;
    public const int AmbiguousProjectFilter = 1007;
    public const int InvalidBatchInput = 1008;
    public const int InvalidDiagnosticSeverity = 1009;

    public const int InvalidWorkspaceExtension = 1101;
    public const int WorkspaceNotFound = 1102;
    public const int InvalidWorkspacePath = 1103;

    public const int MSBuildRegistrationFailed = 1201;
    public const int WorkspaceDiagnostic = 1202;
    public const int OperationCanceled = 1203;
    public const int WorkspaceLoadFailed = 1204;
    public const int WorkspaceFailureDiagnostics = 1205;

    public const int InvalidSourceFilePath = 1301;
    public const int SourceFileNotInWorkspace = 1302;
    public const int InvalidSourcePosition = 1303;
    public const int SymbolNotFoundAtPosition = 1304;
    public const int SourceDefinitionNotFound = 1305;
    public const int SourceFileNotInProject = 1306;
    public const int SourceFileExcludedByGeneratedCodeFilter = 1307;

    public const int GitRepositoryNotFound = 1501;
    public const int GitCommandFailed = 1502;
    public const int InvalidDiffOptions = 1503;
    public const int DiffParseError = 1504;

    public const int InvalidCandidateId = 1701;
    public const int CandidateIdNotFound = 1702;
    public const int CandidateIdAmbiguous = 1703;
    public const int InvalidCandidatePolicy = 1704;
    public const int ConfidenceBelowMinimum = 1705;
}

using System.CommandLine;
using System.CommandLine.Parsing;
using Navlyn.Diagnostics;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class DoctorCommand
{
    public static Command Create()
    {
        Option<FileInfo?> workspaceOption = new("--workspace")
        {
            Description = "Path to a navlyn.workspace.json, .code-workspace, .slnx, .sln, .csproj, or .vbproj workspace, or auto. Defaults to auto."
        };
        Option<string?> workspaceRootPolicyOption = SharedOptions.CreateWorkspaceRootPolicyOption();
        Option<string> formatOption = new("--format")
        {
            Description = "Output format. Only json is currently supported.",
            DefaultValueFactory = _ => "json"
        };
        formatOption.AcceptOnlyFromAmong("json");

        Command command = new("doctor", "Diagnose Navlyn, .NET SDK, and workspace readiness without changing the repository.")
        {
            workspaceOption,
            workspaceRootPolicyOption,
            formatOption
        };

        command.SetAction((ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            return ExecuteAsync(
                parseResult.GetValue(workspaceOption),
                parseResult.GetValue(workspaceRootPolicyOption),
                parseResult.GetValue(formatOption)!,
                cancellationToken);
        });

        return command;
    }

    private static async Task<int> ExecuteAsync(
        FileInfo? workspace,
        string? workspaceRootPolicy,
        string format,
        CancellationToken cancellationToken)
    {
        if (format != "json")
        {
            DiagnosticReporter.WriteError(DiagnosticIds.ParseError, "--format must be json.");
            return ExitCodes.UsageError;
        }

        FileInfo workspaceInput = workspace ?? new FileInfo("auto");
        string workspaceInputText = workspaceInput.ToString();
        string? repositoryRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
        WorkspaceRootPolicy? rootPolicy = ParseWorkspaceRootPolicy(workspaceRootPolicy);

        DotnetInfo dotnet = GetDotnetInfo();
        List<DoctorCheck> checks = [];
        checks.Add(new DoctorCheck(
            Id: "dotnet-sdk",
            Status: dotnet.Sdks.Count > 0 ? "pass" : "fail",
            Summary: dotnet.Sdks.Count > 0 ? $"Found {dotnet.Sdks.Count} .NET SDK installation(s)." : "No .NET SDK installations were reported by dotnet --list-sdks.",
            RepairHint: dotnet.Sdks.Count > 0 ? null : "Install a supported .NET SDK and ensure dotnet is on PATH."));

        WorkspaceLoadOptions options = new(rootPolicy);
        WorkspaceLoadResult loadResult = await new WorkspaceLoader().LoadAsync(workspaceInput, options, cancellationToken);
        bool workspaceOk = loadResult.Error is null;
        checks.Add(new DoctorCheck(
            Id: "workspace-load",
            Status: workspaceOk ? "pass" : "fail",
            Summary: workspaceOk ? "Workspace loaded successfully." : loadResult.Error!.Message,
            RepairHint: workspaceOk ? null : GetWorkspaceRepairHint(loadResult.Error!.DiagnosticId, workspaceInputText)));

        LoadedWorkspace? loadedWorkspace = loadResult.Workspace;
        IReadOnlyList<DoctorProject> projects = loadedWorkspace is null
            ? []
            : [.. loadedWorkspace.Projects.Select(project => new DoctorProject(
                Name: project.Name,
                Path: project.Path,
                TargetFramework: project.TargetFramework,
                AssetsPresent: ProjectAssetsPresent(project.Path)))];

        checks.Add(new DoctorCheck(
            Id: "restore-assets",
            Status: projects.Count == 0 ? "unknown" : projects.All(project => project.AssetsPresent == true) ? "pass" : "warn",
            Summary: projects.Count == 0
                ? "Restore assets were not checked because the workspace did not load."
                : projects.All(project => project.AssetsPresent == true)
                    ? "Project restore assets are present for loaded projects."
                    : "One or more loaded projects do not have obj/project.assets.json.",
            RepairHint: projects.Count == 0 || projects.All(project => project.AssetsPresent == true)
                ? null
                : "Run dotnet restore for the workspace before semantic navigation."));

        DoctorResult result = new(
            Ok: checks.All(check => check.Status is "pass" or "unknown"),
            Command: "doctor",
            WorkspaceInput: workspaceInputText,
            RepositoryRoot: repositoryRoot,
            Dotnet: dotnet,
            Runtime: new DoctorRuntime(
                CurrentFramework: System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                SupportedTargetFrameworks: ["net8.0", "net10.0"]),
            Workspace: new DoctorWorkspace(
                Loaded: workspaceOk,
                Path: loadedWorkspace?.DisplayPath,
                Kind: loadedWorkspace?.Kind,
                ProjectCount: loadedWorkspace?.ProjectCount ?? 0,
                TargetFrameworks: [.. projects
                    .Select(project => project.TargetFramework)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)!],
                Diagnostics: [.. loadResult.Diagnostics.Select(diagnostic => new DoctorWorkspaceDiagnostic(diagnostic.Kind, diagnostic.Message))],
                Error: loadResult.Error is null ? null : new DoctorError(
                    Code: $"{DiagnosticIds.Prefix}{loadResult.Error.DiagnosticId:D4}",
                    Message: loadResult.Error.Message),
                Projects: projects),
            Policies: new DoctorPolicies(
                WorkspaceRootPolicy: workspaceRootPolicy ?? "default",
                GeneratedCode: "Use --exclude-generated for command-level filtering; navlyn.workspace.json generatedFolders can mark generated folders.",
                OutsideRoot: "CLI allows outside-root workspaces by default; MCP defaults to repo-relative unless configured otherwise."),
            Checks: checks,
            RecommendedFirstCommands: [
                $"navlyn doctor --workspace {QuoteWorkspace(loadedWorkspace?.DisplayPath ?? workspaceInputText)}",
                $"navlyn resolve-target --workspace {QuoteWorkspace(loadedWorkspace?.DisplayPath ?? workspaceInputText)} --query <SymbolName>",
                $"navlyn edit-preflight --workspace {QuoteWorkspace(loadedWorkspace?.DisplayPath ?? workspaceInputText)} --query <SymbolName> --goal \"describe the intended edit\" --change-kind behavior"
            ],
            NextAction: workspaceOk
                ? "Resolve one intended C# or Visual Basic symbol with navlyn resolve-target, then gather bounded evidence before editing."
                : GetWorkspaceRepairHint(loadResult.Error!.DiagnosticId, workspaceInputText));

        ConsoleJsonWriter.Write(result);
        loadedWorkspace?.Dispose();
        return ExitCodes.Success;
    }

    private static string QuoteWorkspace(string workspace)
    {
        return workspace.Contains(' ', StringComparison.Ordinal) ? $"\"{workspace}\"" : workspace;
    }

    private static bool? ProjectAssetsPresent(string? projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return null;
        }

        string fullPath = Path.IsPathRooted(projectPath)
            ? projectPath
            : Path.GetFullPath(projectPath);
        string? directory = Path.GetDirectoryName(fullPath);
        return directory is not null && File.Exists(Path.Combine(directory, "obj", "project.assets.json"));
    }

    private static DotnetInfo GetDotnetInfo()
    {
        string version = RunDotnet(["--version"]).Trim();
        IReadOnlyList<string> sdks = [.. RunDotnet(["--list-sdks"])
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .OrderBy(value => value, StringComparer.Ordinal)];
        return new DotnetInfo(version, sdks);
    }

    private static string RunDotnet(IReadOnlyList<string> arguments)
    {
        try
        {
            System.Diagnostics.ProcessStartInfo startInfo = new()
            {
                FileName = "dotnet",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo)!;
            string stdout = process.StandardOutput.ReadToEnd();
            _ = process.StandardError.ReadToEnd();
            process.WaitForExit(5000);
            return process.ExitCode == 0 ? stdout : "";
        }
        catch
        {
            return "";
        }
    }

    private static string? FindRepositoryRoot(string startDirectory)
    {
        DirectoryInfo? directory = new(Path.GetFullPath(startDirectory));
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName.Replace('\\', '/');
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static string GetWorkspaceRepairHint(int diagnosticId, string workspaceInput)
    {
        return diagnosticId switch
        {
            DiagnosticIds.WorkspaceNotFound => $"Check the path '{workspaceInput}' or pass --workspace auto from the repository root.",
            DiagnosticIds.InvalidWorkspaceExtension => "Pass a navlyn.workspace.json, .code-workspace, .slnx, .sln, .csproj, .vbproj, or auto workspace.",
            DiagnosticIds.AmbiguousCodeWorkspace or DiagnosticIds.AmbiguousNavlynWorkspace or DiagnosticIds.InvalidWorkspacePath => "Pass --workspace explicitly or set primaryWorkspace in navlyn.workspace.json.",
            DiagnosticIds.MSBuildRegistrationFailed => "Install a compatible .NET SDK or repair MSBuild discovery.",
            DiagnosticIds.WorkspaceFailureDiagnostics or DiagnosticIds.WorkspaceLoadFailed => "Run dotnet restore/build and inspect MSBuild diagnostics for the workspace.",
            _ => "Inspect the diagnostic message, then rerun navlyn doctor after fixing the workspace."
        };
    }

    private static WorkspaceRootPolicy? ParseWorkspaceRootPolicy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return WorkspaceLoader.TryParseWorkspaceRootPolicy(value, out WorkspaceRootPolicy policy)
            ? policy
            : null;
    }

    private sealed record DoctorResult(
        bool Ok,
        string Command,
        string WorkspaceInput,
        string? RepositoryRoot,
        DotnetInfo Dotnet,
        DoctorRuntime Runtime,
        DoctorWorkspace Workspace,
        DoctorPolicies Policies,
        IReadOnlyList<DoctorCheck> Checks,
        IReadOnlyList<string> RecommendedFirstCommands,
        string NextAction);

    private sealed record DotnetInfo(string Version, IReadOnlyList<string> Sdks);

    private sealed record DoctorRuntime(string CurrentFramework, IReadOnlyList<string> SupportedTargetFrameworks);

    private sealed record DoctorWorkspace(
        bool Loaded,
        string? Path,
        string? Kind,
        int ProjectCount,
        IReadOnlyList<string> TargetFrameworks,
        IReadOnlyList<DoctorWorkspaceDiagnostic> Diagnostics,
        DoctorError? Error,
        IReadOnlyList<DoctorProject> Projects);

    private sealed record DoctorWorkspaceDiagnostic(string Kind, string Message);

    private sealed record DoctorError(string Code, string Message);

    private sealed record DoctorProject(string Name, string? Path, string? TargetFramework, bool? AssetsPresent);

    private sealed record DoctorPolicies(string WorkspaceRootPolicy, string GeneratedCode, string OutsideRoot);

    private sealed record DoctorCheck(string Id, string Status, string Summary, string? RepairHint);
}

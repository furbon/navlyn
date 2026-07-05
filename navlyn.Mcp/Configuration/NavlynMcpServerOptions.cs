using System.Text;

using Navlyn.Workspaces;

namespace Navlyn.Mcp.Configuration;

internal sealed record NavlynMcpServerOptions(
    string Workspace,
    string WorkspaceArgument,
    string? NavlynExecutable,
    IReadOnlyList<string> NavlynArguments,
    string WorkingDirectory,
    int TimeoutMilliseconds,
    int MaxJsonChars,
    string? DaemonPipe,
    NavlynMcpToolProfile ToolProfile,
    WorkspaceRootPolicy WorkspaceRootPolicy)
{
    public const int DefaultTimeoutMilliseconds = 120000;
    public const int DefaultMaxJsonChars = 4000000;
    public const NavlynMcpToolProfile DefaultToolProfile = NavlynMcpToolProfile.Reader;
    public const WorkspaceRootPolicy DefaultWorkspaceRootPolicy = WorkspaceRootPolicy.RepoRelative;
    public const string ToolProfileEnvironmentVariable = "NAVLYN_MCP_TOOL_PROFILE";

    public bool UseExternalCli => !string.IsNullOrWhiteSpace(NavlynExecutable);

    public static bool TryParse(
        IReadOnlyList<string> args,
        out NavlynMcpServerOptions options,
        out string? error,
        out bool showHelp)
    {
        string? workspace = null;
        string? navlynExecutable = null;
        List<string> navlynArguments = [];
        string? workingDirectory = null;
        int timeoutMilliseconds = DefaultTimeoutMilliseconds;
        int maxJsonChars = DefaultMaxJsonChars;
        string? daemonPipe = null;
        NavlynMcpToolProfile toolProfile = DefaultToolProfile;
        WorkspaceRootPolicy workspaceRootPolicy = DefaultWorkspaceRootPolicy;
        showHelp = false;

        string? environmentProfile = Environment.GetEnvironmentVariable(ToolProfileEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(environmentProfile) && !TryParseToolProfile(environmentProfile, out toolProfile))
        {
            options = CreateEmpty();
            error = $"{ToolProfileEnvironmentVariable} must be one of: reader, review, edit, full.";
            return false;
        }

        for (int index = 0; index < args.Count; index++)
        {
            string arg = args[index];
            switch (arg)
            {
                case "--help":
                case "-h":
                    showHelp = true;
                    options = CreateEmpty();
                    error = null;
                    return true;
                case "--workspace":
                    if (!TryReadValue(args, ref index, arg, out workspace, out error))
                    {
                        options = CreateEmpty();
                        return false;
                    }

                    break;
                case "--navlyn-executable":
                    if (!TryReadValue(args, ref index, arg, out navlynExecutable, out error))
                    {
                        options = CreateEmpty();
                        return false;
                    }

                    break;
                case "--navlyn-arg":
                    if (!TryReadValue(args, ref index, arg, out string value, out error))
                    {
                        options = CreateEmpty();
                        return false;
                    }

                    navlynArguments.Add(value);
                    break;
                case "--working-directory":
                    if (!TryReadValue(args, ref index, arg, out workingDirectory, out error))
                    {
                        options = CreateEmpty();
                        return false;
                    }

                    break;
                case "--timeout-ms":
                    if (!TryReadPositiveInt(args, ref index, arg, out timeoutMilliseconds, out error))
                    {
                        options = CreateEmpty();
                        return false;
                    }

                    break;
                case "--max-json-chars":
                    if (!TryReadPositiveInt(args, ref index, arg, out maxJsonChars, out error))
                    {
                        options = CreateEmpty();
                        return false;
                    }

                    break;
                case "--daemon-pipe":
                    if (!TryReadValue(args, ref index, arg, out daemonPipe, out error))
                    {
                        options = CreateEmpty();
                        return false;
                    }

                    break;
                case "--tool-profile":
                    if (!TryReadValue(args, ref index, arg, out string rawToolProfile, out error))
                    {
                        options = CreateEmpty();
                        return false;
                    }

                    if (!TryParseToolProfile(rawToolProfile, out toolProfile))
                    {
                        options = CreateEmpty();
                        error = "--tool-profile must be one of: reader, review, edit, full.";
                        return false;
                    }

                    break;
                case "--workspace-root-policy":
                    if (!TryReadValue(args, ref index, arg, out string rawWorkspaceRootPolicy, out error))
                    {
                        options = CreateEmpty();
                        return false;
                    }

                    if (!WorkspaceLoader.TryParseWorkspaceRootPolicy(rawWorkspaceRootPolicy, out workspaceRootPolicy))
                    {
                        options = CreateEmpty();
                        error = "--workspace-root-policy must be one of: repo-relative, allow-listed, all.";
                        return false;
                    }

                    break;
                default:
                    options = CreateEmpty();
                    error = $"Unknown option: {arg}.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(workspace))
        {
            options = CreateEmpty();
            error = "--workspace is required.";
            return false;
        }

        if (navlynExecutable is null && navlynArguments.Count > 0)
        {
            options = CreateEmpty();
            error = "--navlyn-arg requires --navlyn-executable.";
            return false;
        }

        bool autoWorkspace = string.Equals(workspace.Trim(), "auto", StringComparison.Ordinal);
        string effectiveWorkingDirectory;
        string fullWorkspace;
        if (autoWorkspace)
        {
            effectiveWorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                ? FindRepositoryRoot(Directory.GetCurrentDirectory()) ?? Directory.GetCurrentDirectory()
                : Path.GetFullPath(workingDirectory);
            if (!WorkspaceLoader.TryResolveAutoWorkspace(effectiveWorkingDirectory, out fullWorkspace, out error))
            {
                options = CreateEmpty();
                return false;
            }
        }
        else
        {
            fullWorkspace = Path.GetFullPath(workspace);
            effectiveWorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                ? FindRepositoryRoot(Path.GetDirectoryName(fullWorkspace) ?? Directory.GetCurrentDirectory()) ?? Directory.GetCurrentDirectory()
                : Path.GetFullPath(workingDirectory);
        }

        string workspaceArgument = File.Exists(fullWorkspace) || Directory.Exists(Path.GetDirectoryName(fullWorkspace))
            ? Path.GetRelativePath(effectiveWorkingDirectory, fullWorkspace)
            : workspace;

        options = new NavlynMcpServerOptions(
            Workspace: fullWorkspace,
            WorkspaceArgument: workspaceArgument,
            NavlynExecutable: navlynExecutable,
            NavlynArguments: navlynArguments,
            WorkingDirectory: effectiveWorkingDirectory,
            TimeoutMilliseconds: timeoutMilliseconds,
            MaxJsonChars: maxJsonChars,
            DaemonPipe: daemonPipe,
            ToolProfile: toolProfile,
            WorkspaceRootPolicy: workspaceRootPolicy);
        error = null;
        return true;
    }

    public static string GetUsage()
    {
        StringBuilder builder = new();
        builder.AppendLine("Usage: navlyn-mcp --workspace <path|auto> [options]");
        builder.AppendLine();
        builder.AppendLine("Options:");
        builder.AppendLine("  --workspace <path|auto>        Required navlyn.workspace.json, .code-workspace, .slnx, .sln, .csproj, or .vbproj path, or auto to discover one.");
        builder.AppendLine("  --navlyn-executable <command>  Legacy external Navlyn CLI command or executable. Omit for standalone in-process execution.");
        builder.AppendLine("  --navlyn-arg <arg>             Prefix argument for the legacy external CLI path, repeatable.");
        builder.AppendLine("  --working-directory <path>     Working directory for in-process execution or the legacy child process.");
        builder.AppendLine("  --timeout-ms <number>          Per-tool timeout. Defaults to 120000.");
        builder.AppendLine("  --max-json-chars <number>      Max command JSON chars. Defaults to 4000000.");
        builder.AppendLine("  --daemon-pipe <name>           Optional local navlyn serve named pipe for workspace status/refresh.");
        builder.AppendLine("  --tool-profile <profile>       MCP tool surface: reader, review, edit, or full. Defaults to reader.");
        builder.AppendLine("                                  Can also be set with NAVLYN_MCP_TOOL_PROFILE.");
        builder.AppendLine("  --workspace-root-policy <mode> Workspace root policy: repo-relative, allow-listed, or all. Defaults to repo-relative.");
        return builder.ToString();
    }

    public static string FormatToolProfile(NavlynMcpToolProfile toolProfile)
    {
        return toolProfile switch
        {
            NavlynMcpToolProfile.Reader => "reader",
            NavlynMcpToolProfile.Review => "review",
            NavlynMcpToolProfile.Edit => "edit",
            NavlynMcpToolProfile.Full => "full",
            _ => "reader"
        };
    }

    public static bool TryParseToolProfile(string value, out NavlynMcpToolProfile toolProfile)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "reader":
                toolProfile = NavlynMcpToolProfile.Reader;
                return true;
            case "review":
                toolProfile = NavlynMcpToolProfile.Review;
                return true;
            case "edit":
                toolProfile = NavlynMcpToolProfile.Edit;
                return true;
            case "full":
                toolProfile = NavlynMcpToolProfile.Full;
                return true;
            default:
                toolProfile = DefaultToolProfile;
                return false;
        }
    }

    private static bool TryReadValue(
        IReadOnlyList<string> args,
        ref int index,
        string option,
        out string value,
        out string? error)
    {
        if (index + 1 >= args.Count)
        {
            value = "";
            error = $"{option} requires a value.";
            return false;
        }

        value = args[++index];
        if (string.IsNullOrWhiteSpace(value))
        {
            error = $"{option} value must not be empty.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryReadPositiveInt(
        IReadOnlyList<string> args,
        ref int index,
        string option,
        out int value,
        out string? error)
    {
        if (!TryReadValue(args, ref index, option, out string rawValue, out error))
        {
            value = 0;
            return false;
        }

        if (!int.TryParse(rawValue, out value) || value <= 0)
        {
            error = $"{option} must be 1 or greater.";
            return false;
        }

        return true;
    }

    private static string? FindRepositoryRoot(string startDirectory)
    {
        DirectoryInfo? current = new(startDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")) || File.Exists(Path.Combine(current.FullName, ".git")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    private static NavlynMcpServerOptions CreateEmpty()
    {
        return new NavlynMcpServerOptions(
            "",
            "",
            "",
            [],
            "",
            DefaultTimeoutMilliseconds,
            DefaultMaxJsonChars,
            DaemonPipe: null,
            DefaultToolProfile,
            DefaultWorkspaceRootPolicy);
    }
}

internal enum NavlynMcpToolProfile
{
    Reader,
    Review,
    Edit,
    Full
}

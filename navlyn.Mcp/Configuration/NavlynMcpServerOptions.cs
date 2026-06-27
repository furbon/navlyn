using System.Text;

namespace Navlyn.Mcp.Configuration;

internal sealed record NavlynMcpServerOptions(
    string Workspace,
    string WorkspaceArgument,
    string NavlynExecutable,
    IReadOnlyList<string> NavlynArguments,
    string WorkingDirectory,
    int TimeoutMilliseconds,
    int MaxJsonChars)
{
    public const int DefaultTimeoutMilliseconds = 120000;
    public const int DefaultMaxJsonChars = 4000000;

    public static bool TryParse(
        IReadOnlyList<string> args,
        out NavlynMcpServerOptions options,
        out string? error,
        out bool showHelp)
    {
        string? workspace = null;
        string navlynExecutable = "navlyn";
        List<string> navlynArguments = [];
        string? workingDirectory = null;
        int timeoutMilliseconds = DefaultTimeoutMilliseconds;
        int maxJsonChars = DefaultMaxJsonChars;
        showHelp = false;

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

        string fullWorkspace = Path.GetFullPath(workspace);
        string effectiveWorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
            ? FindRepositoryRoot(Path.GetDirectoryName(fullWorkspace) ?? Directory.GetCurrentDirectory()) ?? Directory.GetCurrentDirectory()
            : Path.GetFullPath(workingDirectory);
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
            MaxJsonChars: maxJsonChars);
        error = null;
        return true;
    }

    public static string GetUsage()
    {
        StringBuilder builder = new();
        builder.AppendLine("Usage: navlyn-mcp --workspace <path> [options]");
        builder.AppendLine();
        builder.AppendLine("Options:");
        builder.AppendLine("  --workspace <path>             Required .slnx, .sln, or .csproj path.");
        builder.AppendLine("  --navlyn-executable <command>  Navlyn CLI command or executable. Defaults to navlyn.");
        builder.AppendLine("  --navlyn-arg <arg>             Prefix argument for local dev, repeatable.");
        builder.AppendLine("  --working-directory <path>     Child process working directory.");
        builder.AppendLine("  --timeout-ms <number>          Per-tool timeout. Defaults to 120000.");
        builder.AppendLine("  --max-json-chars <number>      Max CLI stdout JSON chars. Defaults to 4000000.");
        return builder.ToString();
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
        return new NavlynMcpServerOptions("", "", "", [], "", DefaultTimeoutMilliseconds, DefaultMaxJsonChars);
    }
}

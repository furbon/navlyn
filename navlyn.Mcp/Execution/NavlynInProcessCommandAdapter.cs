using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Navlyn.Cli;
using Navlyn.Mcp.Configuration;
using Navlyn.Mcp.Tools;
using Navlyn.Workspaces;

namespace Navlyn.Mcp.Execution;

internal sealed class NavlynInProcessCommandAdapter(NavlynMcpServerOptions options) : INavlynCommandAdapter
{
    private static readonly Regex DiagnosticCodeRegex = new(@"\bNAVLYN\d{4}\b", RegexOptions.CultureInvariant);
    private static readonly SemaphoreSlim ConsoleLock = new(1, 1);

    public async Task<NavlynToolResult> RunAsync(
        string toolName,
        string cliCommand,
        IReadOnlyList<string> arguments,
        string? standardInput,
        CancellationToken cancellationToken)
    {
        List<string> fullArguments = BuildArguments(cliCommand, arguments);
        NavlynSourceCommand sourceCommand = new(cliCommand, fullArguments);

        NavlynCliResult executionResult;
        try
        {
            executionResult = await RunCommandAsync(fullArguments, standardInput, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Failed(toolName, sourceCommand, "NAVLYN_MCP_CANCELED", "Tool call was canceled.");
        }
        catch (OperationCanceledException)
        {
            return Failed(
                toolName,
                sourceCommand,
                "NAVLYN_MCP_TIMEOUT",
                $"Navlyn in-process command timed out after {options.TimeoutMilliseconds} ms.");
        }
        catch (Exception ex)
        {
            return Failed(toolName, sourceCommand, "NAVLYN_MCP_SERVER_ERROR", $"Unexpected MCP wrapper error: {ex.Message}");
        }

        if (executionResult.TimedOut)
        {
            return Failed(
                toolName,
                sourceCommand,
                "NAVLYN_MCP_TIMEOUT",
                $"Navlyn in-process command timed out after {options.TimeoutMilliseconds} ms.",
                executionResult.ExitCode,
                executionResult.Stderr);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Failed(toolName, sourceCommand, "NAVLYN_MCP_CANCELED", "Tool call was canceled.", executionResult.ExitCode, executionResult.Stderr);
        }

        if (executionResult.Stdout.Length > options.MaxJsonChars)
        {
            return Failed(
                toolName,
                sourceCommand,
                "NAVLYN_MCP_OUTPUT_TOO_LARGE",
                $"Navlyn in-process stdout exceeded --max-json-chars ({options.MaxJsonChars}). Lower the tool limits and retry.",
                executionResult.ExitCode,
                executionResult.Stderr);
        }

        if (executionResult.ExitCode != 0)
        {
            return Failed(
                toolName,
                sourceCommand,
                ExtractDiagnosticCode(executionResult.Stderr) ?? "NAVLYN_MCP_COMMAND_ERROR",
                ExtractErrorMessage(executionResult.Stderr) ?? $"Navlyn in-process command exited with code {executionResult.ExitCode}.",
                executionResult.ExitCode,
                executionResult.Stderr);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(executionResult.Stdout);
            return NavlynToolResult.Succeeded(
                toolName,
                sourceCommand,
                options.WorkspaceArgument,
                document.RootElement);
        }
        catch (JsonException ex)
        {
            return Failed(
                toolName,
                sourceCommand,
                "NAVLYN_MCP_NON_JSON_STDOUT",
                $"Navlyn in-process command returned success but stdout was not valid JSON: {ex.Message}",
                executionResult.ExitCode,
                executionResult.Stderr);
        }
    }

    internal List<string> BuildArguments(string cliCommand, IReadOnlyList<string> arguments)
    {
        List<string> fullArguments =
        [
            cliCommand,
            "--workspace",
            options.WorkspaceArgument,
            "--workspace-root-policy",
            WorkspaceLoader.FormatWorkspaceRootPolicy(options.WorkspaceRootPolicy)
        ];
        fullArguments.AddRange(arguments);
        return fullArguments;
    }

    private async Task<NavlynCliResult> RunCommandAsync(
        IReadOnlyList<string> fullArguments,
        string? standardInput,
        CancellationToken cancellationToken)
    {
        await ConsoleLock.WaitAsync(cancellationToken);
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;
        TextReader originalIn = Console.In;
        string originalDirectory = Directory.GetCurrentDirectory();
        using StringWriter stdout = new(CultureInfo.InvariantCulture);
        using StringWriter stderr = new(CultureInfo.InvariantCulture);
        using StringReader stdin = new(standardInput ?? "");
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(options.TimeoutMilliseconds);

        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            Console.SetIn(stdin);
            Directory.SetCurrentDirectory(options.WorkingDirectory);

            int exitCode = await NavlynCli.RunAsync([.. fullArguments], timeoutSource.Token);
            bool timedOut = timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested;
            return new NavlynCliResult(exitCode, stdout.ToString(), Cap(stderr.ToString(), NavlynCliRunner.StderrLimit), timedOut);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            Console.SetIn(originalIn);
            ConsoleLock.Release();
        }
    }

    private NavlynToolResult Failed(
        string toolName,
        NavlynSourceCommand sourceCommand,
        string code,
        string message,
        int? exitCode = null,
        string? stderr = null)
    {
        return NavlynToolResult.Failed(
            toolName,
            sourceCommand,
            options.WorkspaceArgument,
            new NavlynToolError(code, message, exitCode, string.IsNullOrEmpty(stderr) ? null : Cap(stderr, NavlynCliRunner.StderrLimit)));
    }

    private static string? ExtractDiagnosticCode(string stderr)
    {
        Match match = DiagnosticCodeRegex.Match(stderr);
        return match.Success ? match.Value : null;
    }

    private static string? ExtractErrorMessage(string stderr)
    {
        string[] lines = stderr.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.FirstOrDefault();
    }

    private static string Cap(string value, int limit)
    {
        if (value.Length <= limit)
        {
            return value;
        }

        return value[..limit] + "\n[stderr truncated]";
    }
}

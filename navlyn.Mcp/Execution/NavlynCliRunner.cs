using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Navlyn.Mcp.Configuration;
using Navlyn.Mcp.Tools;
using Navlyn.Workspaces;

namespace Navlyn.Mcp.Execution;

internal sealed class NavlynCliRunner(NavlynMcpServerOptions options) : INavlynCommandAdapter
{
    internal const int StderrLimit = 16384;
    private static readonly Regex DiagnosticCodeRegex = new(@"\bNAVLYN\d{4}\b", RegexOptions.CultureInvariant);

    public async Task<NavlynToolResult> RunAsync(
        string toolName,
        string cliCommand,
        IReadOnlyList<string> arguments,
        string? standardInput,
        CancellationToken cancellationToken)
    {
        List<string> fullArguments = BuildArguments(cliCommand, arguments);
        NavlynSourceCommand sourceCommand = new(cliCommand, fullArguments);

        NavlynCliResult processResult;
        try
        {
            processResult = await RunProcessAsync(fullArguments, standardInput, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return Failed(toolName, sourceCommand, "NAVLYN_MCP_CANCELED", "Tool call was canceled.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return Failed(toolName, sourceCommand, "NAVLYN_MCP_CLI_NOT_FOUND", $"Failed to start Navlyn CLI: {ex.Message}");
        }
        catch (Exception ex)
        {
            return Failed(toolName, sourceCommand, "NAVLYN_MCP_SERVER_ERROR", $"Unexpected MCP wrapper error: {ex.Message}");
        }

        if (processResult.TimedOut)
        {
            return Failed(
                toolName,
                sourceCommand,
                "NAVLYN_MCP_TIMEOUT",
                $"Navlyn CLI timed out after {options.TimeoutMilliseconds} ms.",
                processResult.ExitCode,
                processResult.Stderr);
        }

        if (processResult.Stdout.Length > options.MaxJsonChars)
        {
            return Failed(
                toolName,
                sourceCommand,
                "NAVLYN_MCP_OUTPUT_TOO_LARGE",
                $"Navlyn CLI stdout exceeded --max-json-chars ({options.MaxJsonChars}). Lower the tool limits and retry.",
                processResult.ExitCode,
                processResult.Stderr);
        }

        if (processResult.ExitCode != 0)
        {
            return Failed(
                toolName,
                sourceCommand,
                ExtractDiagnosticCode(processResult.Stderr) ?? "NAVLYN_MCP_CLI_ERROR",
                ExtractErrorMessage(processResult.Stderr) ?? $"Navlyn CLI exited with code {processResult.ExitCode}.",
                processResult.ExitCode,
                processResult.Stderr);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(processResult.Stdout);
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
                $"Navlyn CLI returned success but stdout was not valid JSON: {ex.Message}",
                processResult.ExitCode,
                processResult.Stderr);
        }
    }

    internal List<string> BuildArguments(string cliCommand, IReadOnlyList<string> arguments)
    {
        List<string> fullArguments =
        [
            .. options.NavlynArguments,
            cliCommand,
            "--workspace",
            options.WorkspaceArgument,
            "--workspace-root-policy",
            WorkspaceLoader.FormatWorkspaceRootPolicy(options.WorkspaceRootPolicy)
        ];
        fullArguments.AddRange(arguments);
        return fullArguments;
    }

    private async Task<NavlynCliResult> RunProcessAsync(
        IReadOnlyList<string> fullArguments,
        string? standardInput,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = options.NavlynExecutable!,
            WorkingDirectory = options.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false
        };

        foreach (string argument in fullArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new()
        {
            StartInfo = startInfo
        };

        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(options.TimeoutMilliseconds);
        timeoutSource.Token.ThrowIfCancellationRequested();
        process.Start();

        Task<string> stdoutTask = ReadBoundedAsync(process.StandardOutput, options.MaxJsonChars, timeoutSource.Token);
        Task<string> stderrTask = ReadBoundedAsync(process.StandardError, StderrLimit, timeoutSource.Token);
        bool timedOut = false;
        try
        {
            if (standardInput is not null)
            {
                await process.StandardInput.WriteAsync(standardInput.AsMemory(), timeoutSource.Token);
                process.StandardInput.Close();
            }

            await process.WaitForExitAsync(timeoutSource.Token);
            await Task.WhenAll(stdoutTask, stderrTask);
            timedOut = timeoutSource.IsCancellationRequested;
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            timedOut = true;
        }
        finally
        {
            // A caller's token can also be the service deadline. Always stop and reap the
            // process before returning, including failures while writing batch stdin.
            TryKill(process);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdoutTask, stderrTask);
        }

        string stdout = await stdoutTask;
        string stderr = await stderrTask;
        int exitCode = process.ExitCode;
        return new NavlynCliResult(exitCode, stdout, Cap(stderr, StderrLimit), timedOut);
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
            new NavlynToolError(code, message, exitCode, string.IsNullOrEmpty(stderr) ? null : Cap(stderr, StderrLimit)));
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

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, int limit, CancellationToken cancellationToken)
    {
        StringBuilder text = new();
        char[] buffer = new char[4096];
        try
        {
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) != 0)
            {
                // Retain one extra character to signal overflow while draining the pipe.
                int retained = (int)Math.Min(count, Math.Max(0L, (long)limit + 1 - text.Length));
                text.Append(buffer, 0, retained);
            }
        }
        catch (OperationCanceledException)
        {
        }

        return text.ToString();
    }
}

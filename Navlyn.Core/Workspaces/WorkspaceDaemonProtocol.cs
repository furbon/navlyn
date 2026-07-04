using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Navlyn.Workspaces;

internal static class WorkspaceDaemonProtocol
{
    public const string StatusMethod = "workspace/status";
    public const string RefreshMethod = "workspace/refresh";
    public const string ShutdownMethod = "daemon/shutdown";
    public const int DefaultConnectTimeoutMilliseconds = 2000;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string SerializeRequest(WorkspaceDaemonRequest request)
    {
        return JsonSerializer.Serialize(request, JsonOptions);
    }

    public static WorkspaceDaemonRequest? DeserializeRequest(string line)
    {
        return JsonSerializer.Deserialize<WorkspaceDaemonRequest>(line, JsonOptions);
    }

    public static string SerializeResponse(WorkspaceDaemonResponse response)
    {
        return JsonSerializer.Serialize(response, JsonOptions);
    }

    public static WorkspaceDaemonResponse? DeserializeResponse(string line)
    {
        return JsonSerializer.Deserialize<WorkspaceDaemonResponse>(line, JsonOptions);
    }

    public static async Task<WorkspaceDaemonClientResult> SendAsync(
        string pipeName,
        WorkspaceDaemonRequest request,
        int timeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        try
        {
            await using NamedPipeClientStream pipe = new(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(timeoutMilliseconds));
            await pipe.ConnectAsync(timeout.Token);

            await using StreamWriter writer = new(pipe, Utf8NoBom, bufferSize: 1024, leaveOpen: true) { AutoFlush = true };
            using StreamReader reader = new(pipe, Utf8NoBom, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            await writer.WriteLineAsync(SerializeRequest(request.AsPipeRequest()));
            string? responseLine = await reader.ReadLineAsync(timeout.Token);
            if (string.IsNullOrWhiteSpace(responseLine))
            {
                return WorkspaceDaemonClientResult.Failed("Daemon closed the pipe without a response.");
            }

            WorkspaceDaemonResponse? response = DeserializeResponse(responseLine);
            return response is null
                ? WorkspaceDaemonClientResult.Failed("Daemon returned an invalid response.")
                : WorkspaceDaemonClientResult.Succeeded(response);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return WorkspaceDaemonClientResult.Failed($"Timed out connecting to daemon pipe '{pipeName}'.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException)
        {
            return WorkspaceDaemonClientResult.Failed($"Failed to connect to daemon pipe '{pipeName}': {ex.Message}");
        }
    }
}

internal sealed record WorkspaceDaemonRequest(
    string? Id,
    string Method,
    WorkspaceDiskCacheRequest? Cache)
{
    public WorkspaceDaemonRequest AsPipeRequest()
    {
        return this with { Method = Method.Trim() };
    }
}

internal sealed record WorkspaceDaemonResponse(
    string? Id,
    bool Ok,
    JsonElement? Result,
    WorkspaceDaemonError? Error);

internal sealed record WorkspaceDaemonError(string Code, string Message);

internal sealed record WorkspaceDaemonClientResult(
    WorkspaceDaemonResponse? Response,
    string? Error)
{
    public static WorkspaceDaemonClientResult Succeeded(WorkspaceDaemonResponse response)
    {
        return new WorkspaceDaemonClientResult(response, Error: null);
    }

    public static WorkspaceDaemonClientResult Failed(string error)
    {
        return new WorkspaceDaemonClientResult(Response: null, error);
    }
}

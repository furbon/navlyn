using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Forbidden.Namespace;

namespace ReviewPacksFixture;

public sealed class AsyncSamples
{
    public async void UnsafeAsyncVoid()
    {
        await Task.Delay(1);
    }

    public Task MissingForwardingAsync(CancellationToken cancellationToken)
    {
        return NeedsTokenAsync();
    }

    public void Blocking(Task<int> task)
    {
        task.Wait();
        _ = task.Result;
        task.GetAwaiter().GetResult();
        NeedsTokenAsync();
    }

    private static Task NeedsTokenAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

public sealed class DisposalSamples
{
    public void Leaks()
    {
        var stream = new MemoryStream();
        stream.WriteByte(1);
    }

    public void SyncDisposesAsyncDisposable()
    {
        using var resource = new DualDisposableResource();
        resource.Dispose();
    }
}

public sealed class DualDisposableResource : IDisposable, IAsyncDisposable
{
    public void Dispose()
    {
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}

public sealed class NullabilitySamples
{
    public required string Name { get; init; }

    public string Force(string? value)
    {
        return value!;
    }
}

[Route("/widgets")]
public sealed class WidgetController : ControllerBase
{
    [HttpGet]
    public string Get(string password)
    {
        Logger.LogInformation(password);
        File.ReadAllText(password);
        return JsonSerializer.Deserialize<string>(password);
    }
}

public abstract class ControllerBase
{
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RouteAttribute(string template) : Attribute;

[AttributeUsage(AttributeTargets.Method)]
public sealed class HttpGetAttribute : Attribute;

public static class Logger
{
    public static void LogInformation(string message)
    {
    }
}

public static class JsonSerializer
{
    public static T Deserialize<T>(string json)
    {
        return default!;
    }
}

namespace Forbidden.Namespace;

public sealed class ForbiddenType;

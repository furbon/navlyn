using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Mvc
{
    public abstract class ControllerBase
    {
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class ApiControllerAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public sealed class RouteAttribute(string template) : Attribute
    {
        public string Template { get; } = template;
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HttpGetAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class NonActionAttribute : Attribute
    {
    }
}

namespace Microsoft.AspNetCore.Builder
{
    public sealed class WebApplication
    {
    }

    public static class EndpointRouteBuilderExtensions
    {
        public static WebApplication MapGet(this WebApplication app, string pattern, Delegate handler) => app;
    }
}

namespace Microsoft.Extensions.Hosting
{
    public interface IHostedService
    {
        Task StartAsync(CancellationToken cancellationToken);
    }

    public abstract class BackgroundService : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => ExecuteAsync(cancellationToken);

        protected abstract Task ExecuteAsync(CancellationToken stoppingToken);
    }
}

namespace FrameworkEntrypointsFixture
{
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Hosting;

    [ApiController]
    [Route("widgets")]
    public sealed class WidgetsController : ControllerBase
    {
        [HttpGet]
        public string Get() => WidgetHandler.Handle();

        [NonAction]
        public string Helper() => "helper";
    }

    public static class EndpointSetup
    {
        public static void Map(WebApplication app)
        {
            app.MapGet("/widgets", WidgetHandler.Handle);
        }
    }

    public static class WidgetHandler
    {
        public static string Handle() => "widget";
    }

    public sealed class WidgetWorker : BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
    }

    public sealed class ManualHostedService : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

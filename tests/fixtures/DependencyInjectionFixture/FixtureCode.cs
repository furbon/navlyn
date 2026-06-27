using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.DependencyInjection
{
    using Microsoft.Extensions.Hosting;

    public interface IServiceCollection
    {
    }

    public static class ServiceCollectionServiceExtensions
    {
        public static IServiceCollection AddScoped<TService, TImplementation>(this IServiceCollection services)
            where TImplementation : TService => services;

        public static IServiceCollection AddScoped<TService>(this IServiceCollection services) => services;

        public static IServiceCollection AddSingleton<TService, TImplementation>(this IServiceCollection services)
            where TImplementation : TService => services;

        public static IServiceCollection AddSingleton<TService>(this IServiceCollection services) => services;

        public static IServiceCollection AddTransient<TService>(this IServiceCollection services) => services;

        public static IServiceCollection AddHostedService<TService>(this IServiceCollection services)
            where TService : IHostedService => services;

        public static IServiceCollection Configure<TOptions>(this IServiceCollection services, Action<TOptions> configure) => services;
    }
}

namespace Microsoft.Extensions.Hosting
{
    public interface IHostedService
    {
        Task StartAsync(CancellationToken cancellationToken);
    }
}

namespace Microsoft.Extensions.Logging
{
    public interface ILogger<T>
    {
    }
}

namespace DependencyInjectionFixture
{
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;

    public interface IWidgetStore
    {
    }

    public sealed class SqlWidgetStore(ILogger<SqlWidgetStore> logger) : IWidgetStore
    {
        public ILogger<SqlWidgetStore> Logger { get; } = logger;
    }

    public sealed class WidgetService(IWidgetStore store)
    {
        public IWidgetStore Store { get; } = store;
    }

    public sealed class RootSingleton(ScopedThing scoped)
    {
        public ScopedThing Scoped { get; } = scoped;
    }

    public sealed class ScopedThing
    {
    }

    public sealed class Worker : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed class WidgetOptions
    {
        public string Name { get; set; } = "";
    }

    public static class CompositionRoot
    {
        public static void Configure(IServiceCollection services)
        {
            services.AddScoped<IWidgetStore, SqlWidgetStore>();
            services.AddTransient<WidgetService>();
            services.AddSingleton<RootSingleton>();
            services.AddScoped<ScopedThing>();
            services.AddHostedService<Worker>();
            services.Configure<WidgetOptions>(options => options.Name = "fixture");
        }
    }
}

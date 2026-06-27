using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.AspNetCore.Authorization
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class AuthorizeAttribute : Attribute
    {
        public string? Policy { get; set; }
        public string? Roles { get; set; }
        public string? AuthenticationSchemes { get; set; }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class AllowAnonymousAttribute : Attribute
    {
    }
}

namespace Microsoft.AspNetCore.Mvc
{
    public abstract class ControllerBase
    {
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class ApiControllerAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class RouteAttribute(string template) : Attribute
    {
        public string Template { get; } = template;
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HttpGetAttribute(string? template = null) : Attribute
    {
        public string? Template { get; } = template;
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HttpPostAttribute(string? template = null) : Attribute
    {
        public string? Template { get; } = template;
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

        public static WebApplication MapPost(this WebApplication app, string pattern, Delegate handler) => app;

        public static WebApplication RequireAuthorization(this WebApplication app, string? policy = null) => app;

        public static WebApplication AllowAnonymous(this WebApplication app) => app;
    }
}

namespace MediatR
{
    public interface IRequest<out TResponse>
    {
    }

    public interface INotification
    {
    }

    public interface IRequestHandler<in TRequest, TResponse>
    {
        Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
    }

    public interface INotificationHandler<in TNotification>
    {
        Task Handle(TNotification notification, CancellationToken cancellationToken);
    }

    public interface ISender
    {
        Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);
    }

    public interface IPublisher
    {
        Task Publish(INotification notification, CancellationToken cancellationToken = default);
    }
}

namespace Microsoft.EntityFrameworkCore
{
    public class DbContext
    {
        public DbSet<TEntity> Set<TEntity>() where TEntity : class => new();
    }

    public class DbSet<TEntity> : List<TEntity>
    {
    }

    public interface IEntityTypeConfiguration<TEntity>
    {
        void Configure(EntityTypeBuilder<TEntity> builder);
    }

    public sealed class EntityTypeBuilder<TEntity>
    {
    }
}

namespace Microsoft.Extensions.DependencyInjection
{
    public interface IServiceCollection
    {
    }

    public sealed class OptionsBuilder<TOptions> where TOptions : class
    {
    }

    public interface IConfiguration
    {
        IConfiguration GetSection(string key);
    }

    public static class OptionsServiceCollectionExtensions
    {
        public static OptionsBuilder<TOptions> AddOptions<TOptions>(this IServiceCollection services) where TOptions : class => new();

        public static IServiceCollection Configure<TOptions>(this IServiceCollection services, IConfiguration configuration) where TOptions : class => services;

        public static OptionsBuilder<TOptions> Bind<TOptions>(this OptionsBuilder<TOptions> builder, IConfiguration configuration) where TOptions : class => builder;

        public static OptionsBuilder<TOptions> ValidateDataAnnotations<TOptions>(this OptionsBuilder<TOptions> builder) where TOptions : class => builder;

        public static OptionsBuilder<TOptions> ValidateOnStart<TOptions>(this OptionsBuilder<TOptions> builder) where TOptions : class => builder;
    }
}

namespace ApplicationDomainFixture
{
    using MediatR;
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.DependencyInjection;

    [ApiController]
    [Route("orders")]
    [Authorize(Policy = "Orders.Read")]
    public sealed class OrdersController(ISender sender, IOptions<PaymentOptions> options) : ControllerBase
    {
        [HttpGet("{id}")]
        public Task<OrderDto> Get(int id) => sender.Send(new GetOrderQuery(id));

        [HttpPost]
        [AllowAnonymous]
        public Task<OrderDto> Create(CreateOrderCommand command) => sender.Send(command);

        public string Currency => options.Value.Currency;
    }

    public static class EndpointSetup
    {
        public static void Map(WebApplication app)
        {
            app.MapGet("/health", () => "ok").AllowAnonymous();
            app.MapPost("/orders", OrderEndpoints.Create).RequireAuthorization("Orders.Write");
        }
    }

    public static class OrderEndpoints
    {
        public static Task<OrderDto> Create(CreateOrderCommand command, ISender sender) => sender.Send(command);
    }

    public sealed class PaymentOptions
    {
        public string Currency { get; set; } = "USD";
    }

    public sealed class PaymentService(IOptionsMonitor<PaymentOptions> options)
    {
        public string Currency => options.CurrentValue.Currency;
    }

    public static class OptionsSetup
    {
        public static void Configure(IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<PaymentOptions>(configuration.GetSection("Payments"));
            services.AddOptions<PaymentOptions>().Bind(configuration.GetSection("Payments")).ValidateDataAnnotations().ValidateOnStart();
        }
    }

    public sealed record GetOrderQuery(int Id) : IRequest<OrderDto>;

    public sealed record CreateOrderCommand(string Number = "A-1") : IRequest<OrderDto>;

    public sealed record OrderCreatedNotification(int Id) : INotification;

    public sealed class GetOrderHandler(OrdersDbContext db) : IRequestHandler<GetOrderQuery, OrderDto>
    {
        public Task<OrderDto> Handle(GetOrderQuery request, CancellationToken cancellationToken)
        {
            Order? order = db.Orders.FirstOrDefault(item => item.Id == request.Id);
            return Task.FromResult(new OrderDto(order?.Id ?? request.Id));
        }
    }

    public sealed class CreateOrderHandler(IPublisher publisher) : IRequestHandler<CreateOrderCommand, OrderDto>
    {
        public async Task<OrderDto> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
        {
            await publisher.Publish(new OrderCreatedNotification(42), cancellationToken);
            return new OrderDto(42);
        }
    }

    public sealed class OrderCreatedHandler : INotificationHandler<OrderCreatedNotification>
    {
        public Task Handle(OrderCreatedNotification notification, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed class OrdersDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; } = new();
    }

    public sealed class Order
    {
        public int Id { get; set; }

        public Customer Customer { get; set; } = new();
    }

    public sealed class Customer
    {
        public int Id { get; set; }
    }

    public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
    {
        public void Configure(EntityTypeBuilder<Order> builder)
        {
        }
    }

    public sealed record OrderDto(int Id);
}

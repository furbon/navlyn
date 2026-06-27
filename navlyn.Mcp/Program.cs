using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Navlyn.Mcp.Configuration;
using Navlyn.Mcp.Execution;
using Navlyn.Mcp.Tools;

if (!NavlynMcpServerOptions.TryParse(args, out NavlynMcpServerOptions options, out string? error, out bool showHelp))
{
    Console.Error.WriteLine(error);
    Console.Error.WriteLine(NavlynMcpServerOptions.GetUsage());
    return 2;
}

if (showHelp)
{
    Console.Error.WriteLine(NavlynMcpServerOptions.GetUsage());
    return 0;
}

HostApplicationBuilder builder = Host.CreateApplicationBuilder([]);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(consoleLogOptions =>
{
    consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<INavlynCommandAdapter, NavlynCliRunner>();
builder.Services.AddSingleton<NavlynMcpToolService>();
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    .WithResourcesFromAssembly()
    .WithPromptsFromAssembly();

await builder.Build().RunAsync();
return 0;

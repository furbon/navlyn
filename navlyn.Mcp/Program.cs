using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
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

if (options.DeprecatedToolProfileSpecified)
{
    Console.Error.WriteLine(
        $"Warning NAVLYN_MCP_TOOL_PROFILE_DEPRECATED: --tool-profile/NAVLYN_MCP_TOOL_PROFILE is deprecated and ignored. Navlyn MCP now exposes one read-only tool surface; supplied profile '{options.DeprecatedToolProfileValue}' is treated as a compatibility alias and may be removed after the next major version.");
}

Directory.SetCurrentDirectory(options.WorkingDirectory);

HostApplicationBuilder builder = Host.CreateApplicationBuilder([]);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(consoleLogOptions =>
{
    consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services.AddSingleton(options);
if (options.UseExternalCli)
{
    builder.Services.AddSingleton<INavlynCommandAdapter, NavlynCliRunner>();
}
else
{
    builder.Services.AddSingleton<INavlynCommandAdapter, NavlynInProcessCommandAdapter>();
}

builder.Services.AddSingleton<NavlynMcpWorkspaceCache>();
builder.Services.AddSingleton<NavlynMcpDirectToolRunner>();
builder.Services.AddSingleton<NavlynMcpToolService>();
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithRequestFilters(filters =>
    {
        filters.AddListToolsFilter(next => async (request, cancellationToken) =>
        {
            ListToolsResult result = await next(request, cancellationToken);
            NavlynMcpServerOptions serverOptions = request.Services!.GetRequiredService<NavlynMcpServerOptions>();
            IReadOnlyList<string> allowedNames = NavlynMcpToolProfilePolicy.GetToolNames(serverOptions.ToolProfile);
            Dictionary<string, Tool> toolsByName = result.Tools.ToDictionary(tool => tool.Name, StringComparer.Ordinal);

            result.Tools = allowedNames
                .Where(toolsByName.ContainsKey)
                .Select(name => toolsByName[name])
                .ToList();

            return result;
        });
    })
    .WithToolsFromAssembly()
    .WithResourcesFromAssembly()
    .WithPromptsFromAssembly();

await builder.Build().RunAsync();
return 0;

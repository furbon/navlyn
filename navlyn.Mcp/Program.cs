using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Navlyn.Mcp.Configuration;
using Navlyn.Mcp.Execution;
using Navlyn.Mcp.Tools;
using Navlyn.Symbols;
using Navlyn.Workspaces;

if (await ExternalMemberWorker.RunIfRequestedAsync(args, CancellationToken.None))
{
    return 0;
}

if (args is ["--version"])
{
    Assembly assembly = typeof(NavlynMcpServerOptions).Assembly;
    Console.Out.WriteLine(
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
        assembly.GetName().Version?.ToString());
    return 0;
}

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
        filters.AddCallToolFilter(next => async (request, cancellationToken) =>
        {
            string? framework = null;
            if (request.Params?.Arguments?.TryGetValue("targetFramework", out JsonElement value) == true)
            {
                if (value.ValueKind != JsonValueKind.Null &&
                    (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())))
                {
                    NavlynMcpServerOptions settings = request.Services!.GetRequiredService<NavlynMcpServerOptions>();
                    return NavlynToolResultFormatter.ToCallToolResult(NavlynToolResult.Failed(
                        request.Params.Name, null, settings.Workspace,
                        new NavlynToolError("NAVLYN_MCP_INVALID_ARGUMENT", "targetFramework must be a non-empty string or null.")));
                }
                framework = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            }
            using WorkspaceSelectionScope selection = WorkspaceSelectionScope.Begin(framework);
            return await next(request, cancellationToken);
        });
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

            foreach (Tool tool in result.Tools)
            {
                tool.InputSchema = NavlynToolSchemaFormatter.Compact(tool.InputSchema, includeFramework: true);
                if (tool.OutputSchema is JsonElement outputSchema)
                {
                    tool.OutputSchema = NavlynToolSchemaFormatter.Compact(outputSchema, output: true);
                }
            }
            return result;
        });
    })
    .WithToolsFromAssembly()
    .WithResourcesFromAssembly()
    .WithPromptsFromAssembly();

await builder.Build().RunAsync();
return 0;

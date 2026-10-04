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
        $"Warning NAVLYN_MCP_TOOL_PROFILE_DEPRECATED: --tool-profile/NAVLYN_MCP_TOOL_PROFILE is deprecated and ignored. use --surface to choose the read-only tool inventory; supplied profile '{options.DeprecatedToolProfileValue}' is treated as a compatibility alias and may be removed after the next major version.");
}

Directory.SetCurrentDirectory(options.WorkingDirectory);
// Keep profiling on transport stderr even while an in-process CLI captures Console.Error.
NavlynMcpTimingScope.ConfigureDiagnostics(Console.Error);

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
    .AddMcpServer(server => server.ServerInstructions =
        NavlynMcpToolProfilePolicy.Allows(options.ToolProfile, NavlynMcpTools.ReadTool, options.Surface)
            ? "Use ordinary reads/search for directly readable facts. For a referenced DLL body at a known call position, navlyn_read(file,line,column,view:body,externalSource:decompiled) returns the bound implementation. No file listing, SDK discovery, outline, target or skill preamble is needed."
            : "Use ordinary reads/search for directly readable facts. Use the advertised Navlyn tools for missing compiler evidence; avoid unrelated preparation.")
    .WithStdioServerTransport()
    .WithRequestFilters(filters =>
    {
        filters.AddCallToolFilter(next => async (request, cancellationToken) =>
        {
            using NavlynMcpTimingScope? timing = NavlynMcpTimingScope.Begin(request.Params!.Name);
            NavlynMcpServerOptions settings = request.Services!.GetRequiredService<NavlynMcpServerOptions>();
            if (!NavlynMcpToolProfilePolicy.Allows(settings.ToolProfile, request.Params!.Name, settings.Surface))
                return NavlynToolResultFormatter.ToCallToolResult(NavlynToolResult.Failed(request.Params.Name, null, settings.WorkspaceArgument,
                    new NavlynToolError("NAVLYN_MCP_TOOL_UNAVAILABLE", "Tool is not exposed by this surface. Use --surface full or the CLI for advanced investigation.")));
            if (!NavlynMcpResponsePolicy.TryReadControls(request.Params.Name, request.Params.Arguments, settings.EffectiveResultProfile,
                out string resultProfile, out int? entryLimit, out int entryOffset, out string? controlError))
                return NavlynToolResultFormatter.ToCallToolResult(NavlynToolResult.Failed(request.Params.Name, null, settings.WorkspaceArgument,
                    new NavlynToolError("NAVLYN_MCP_INVALID_ARGUMENT", controlError!)));
            string? framework = null;
            if (request.Params?.Arguments?.TryGetValue("targetFramework", out JsonElement value) == true)
            {
                if (value.ValueKind != JsonValueKind.Null &&
                    (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())))
                {
                    return NavlynToolResultFormatter.ToCallToolResult(NavlynToolResult.Failed(
                        request.Params.Name, null, settings.Workspace,
                        new NavlynToolError("NAVLYN_MCP_INVALID_ARGUMENT", "targetFramework must be a non-empty string or null.")));
                }
                framework = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            }
            string? typeKind = null;
            if (request.Params!.Arguments?.TryGetValue("typeKind", out JsonElement kindValue) == true && kindValue.ValueKind != JsonValueKind.Null)
            {
                bool queryMode = request.Params.Arguments!.TryGetValue("query", out JsonElement queryValue) &&
                    queryValue.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(queryValue.GetString());
                if (!NavlynMcpResponsePolicy.SupportsTypeKind(request.Params.Name) || kindValue.ValueKind != JsonValueKind.String || !WorkspaceSelectionScope.IsValidTypeKind(kindValue.GetString()) || !queryMode)
                    return NavlynToolResultFormatter.ToCallToolResult(NavlynToolResult.Failed(request.Params.Name, null, settings.WorkspaceArgument,
                        new NavlynToolError("NAVLYN_MCP_INVALID_ARGUMENT", "typeKind requires a query and a supported type kind.")));
                typeKind = kindValue.GetString();
            }
            using WorkspaceSelectionScope selection = WorkspaceSelectionScope.Begin(framework, typeKind);
            using NavlynMcpResponseScope responseScope = NavlynMcpResponseScope.Begin(entryLimit, entryOffset);
            CallToolResult result = await next(request, cancellationToken);
            return NavlynMcpResponsePolicy.Project(result, resultProfile, entryLimit, entryOffset);
        });
        filters.AddListToolsFilter(next => async (request, cancellationToken) =>
        {
            using NavlynMcpTimingScope? timing = NavlynMcpTimingScope.Begin("tools/list");
            using IDisposable? discovery = NavlynMcpTimingScope.Measure("discovery.schema");
            ListToolsResult result = await next(request, cancellationToken);
            NavlynMcpServerOptions serverOptions = request.Services!.GetRequiredService<NavlynMcpServerOptions>();
            IReadOnlyList<string> allowedNames = NavlynMcpToolProfilePolicy.GetToolNames(serverOptions.ToolProfile, serverOptions.Surface);
            Dictionary<string, Tool> toolsByName = result.Tools.ToDictionary(tool => tool.Name, StringComparer.Ordinal);

            result.Tools = allowedNames
                .Where(toolsByName.ContainsKey)
                .Select(name => toolsByName[name])
                .ToList();

            foreach (Tool tool in result.Tools)
            {
                tool.InputSchema = NavlynToolSchemaFormatter.Compact(tool.InputSchema, includeFramework: true);
                tool.InputSchema = NavlynMcpResponsePolicy.InputSchema(tool.Name, tool.InputSchema, serverOptions.EffectiveResultProfile,
                    focusedCompact: serverOptions.Surface == "focused" && serverOptions.EffectiveResultProfile == "compact");
                if (tool.OutputSchema is JsonElement outputSchema)
                {
                    tool.OutputSchema = NavlynMcpResponsePolicy.OutputSchema(NavlynToolSchemaFormatter.Compact(outputSchema, output: true),
                        focusedCompact: serverOptions.Surface == "focused" && serverOptions.EffectiveResultProfile == "compact");
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

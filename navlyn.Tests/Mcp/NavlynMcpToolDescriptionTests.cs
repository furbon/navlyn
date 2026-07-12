using System.ComponentModel;
using System.Reflection;
using Navlyn.Mcp.Tools;

namespace Navlyn.Tests.Mcp;

public sealed class NavlynMcpToolDescriptionTests
{
    [Fact]
    public void HighFrequencyMcpToolsHaveModelVisibleParameterDescriptions()
    {
        string[] methodNames =
        [
            nameof(NavlynMcpTools.Target),
            nameof(NavlynMcpTools.Read),
            nameof(NavlynMcpTools.PrepareEdit),
            nameof(NavlynMcpTools.VerifyEdit),
            nameof(NavlynMcpTools.Review),
            nameof(NavlynMcpTools.FileOutline),
            nameof(NavlynMcpTools.SymbolEdges)
        ];

        List<string> missing = [];
        foreach (string methodName in methodNames)
        {
            MethodInfo method = typeof(NavlynMcpTools).GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException($"Could not find MCP tool method {methodName}.");

            DescriptionAttribute? methodDescription = method.GetCustomAttribute<DescriptionAttribute>();
            if (methodDescription is null || string.IsNullOrWhiteSpace(methodDescription.Description))
            {
                missing.Add($"{methodName}::<tool description>");
            }

            foreach (ParameterInfo parameter in method.GetParameters())
            {
                if (parameter.ParameterType == typeof(IServiceProvider) ||
                    parameter.ParameterType == typeof(CancellationToken))
                {
                    continue;
                }

                DescriptionAttribute? description = parameter.GetCustomAttribute<DescriptionAttribute>();
                if (description is null || string.IsNullOrWhiteSpace(description.Description))
                {
                    missing.Add($"{methodName}::{parameter.Name}");
                }
            }
        }

        Assert.True(missing.Count == 0, "Missing MCP parameter descriptions: " + string.Join(", ", missing));
    }

    [Fact]
    public void UnifiedMcpSurfacePutsCanonicalAgentLoopFirst()
    {
        IReadOnlyList<string> tools = NavlynMcpToolProfilePolicy.GetToolNames(Navlyn.Mcp.Configuration.NavlynMcpToolProfile.Full);

        Assert.Equal(
            [
                NavlynMcpTools.TargetTool,
                NavlynMcpTools.ReadTool,
                NavlynMcpTools.PrepareEditTool,
                NavlynMcpTools.VerifyEditTool,
                NavlynMcpTools.ReviewTool
            ],
            tools.Take(5).ToArray());
    }

    [Fact]
    public void CompatibilityMcpAliasesTellModelsToPreferCanonicalTools()
    {
        string[] methodNames =
        [
            nameof(NavlynMcpTools.ResolveTarget),
            nameof(NavlynMcpTools.SymbolSource),
            nameof(NavlynMcpTools.EditPreflight),
            nameof(NavlynMcpTools.PostEditGuard),
            nameof(NavlynMcpTools.ReviewDiff)
        ];

        List<string> weakDescriptions = [];
        foreach (string methodName in methodNames)
        {
            MethodInfo method = typeof(NavlynMcpTools).GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException($"Could not find MCP tool method {methodName}.");
            string description = method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "";
            if (!description.Contains("Advanced compatibility", StringComparison.Ordinal) ||
                !description.Contains("Prefer canonical", StringComparison.Ordinal))
            {
                weakDescriptions.Add(methodName);
            }
        }

        Assert.True(weakDescriptions.Count == 0, "Compatibility MCP aliases need stronger canonical guidance: " + string.Join(", ", weakDescriptions));
    }
}

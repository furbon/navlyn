using System.Reflection;
using Navlyn.Mcp.Tools;

namespace Navlyn.Tests.Mcp;

public sealed class ExternalLibrarySourceMcpTests
{
    [Fact]
    public void ReadOptIn_PreservesTheTwentyFiveToolSurface()
    {
        Assert.Equal(25, Navlyn.Mcp.Tools.NavlynMcpToolProfilePolicy
            .GetToolNames(Navlyn.Mcp.Configuration.NavlynMcpToolProfile.Full).Count);
    }

    [Fact]
    public void Read_DeclaresExternalSourceOptIn_WithoutChangingToolSurface()
    {
        ParameterInfo parameter = Assert.Single(
            typeof(NavlynMcpTools).GetMethod(nameof(NavlynMcpTools.Read))!.GetParameters(),
            parameter => parameter.Name == "externalSource");

        Assert.Equal(typeof(string), parameter.ParameterType);
    }
}

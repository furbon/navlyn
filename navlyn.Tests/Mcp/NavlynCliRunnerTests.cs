using Navlyn.Mcp.Configuration;
using Navlyn.Mcp.Execution;

namespace Navlyn.Tests.Mcp;

public sealed class NavlynCliRunnerTests
{
    [Fact]
    public void BuildArguments_PrependsConfiguredNavlynArgsAndWorkspace()
    {
        NavlynMcpServerOptions options = new(
            Workspace: @"D:\repo\navlyn.slnx",
            WorkspaceArgument: "navlyn.slnx",
            NavlynExecutable: "dotnet",
            NavlynArguments: ["navlyn.dll"],
            WorkingDirectory: @"D:\repo",
            TimeoutMilliseconds: NavlynMcpServerOptions.DefaultTimeoutMilliseconds,
            MaxJsonChars: NavlynMcpServerOptions.DefaultMaxJsonChars);
        NavlynCliRunner runner = new(options);

        IReadOnlyList<string> arguments = runner.BuildArguments("find", ["--query", "WorkspaceLoader"]);

        Assert.Equal(["navlyn.dll", "find", "--workspace", "navlyn.slnx", "--query", "WorkspaceLoader"], arguments);
    }
}

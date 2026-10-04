using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis.MSBuild;
using Navlyn.Cli;
using Navlyn.Cli.OutputProfiles;
using Navlyn.Workspaces;

namespace Navlyn.Tests.Contracts;

public sealed class ReproductionCommandTests
{
    [Fact]
    public void ProfiledResult_RetainsExactArgumentsDirectoryAndBatchInputWithoutLeakingToNextCall()
    {
        using MSBuildWorkspace handle = MSBuildWorkspace.Create();
        LoadedWorkspace workspace = new(FullPath: "navlyn.slnx", DisplayPath: "navlyn.slnx", Kind: "solution",
            Workspace: handle, Solution: handle.CurrentSolution, Projects: []);
        string[] arguments = ["review-pack", "--workspace", "navlyn.slnx", "--base", "v0.8.4", "--head", "v0.8.5",
            "--pack", "nullability", "--project", "Project With Spaces", "--finding-limit", "3", "--profile", "compact",
            "--workspace-root-policy", "repo-relative"];
        using (CliInvocationContext invocation = CliInvocationContext.Begin(arguments, "/repo with spaces"))
        {
            JsonObject result = OutputProfile.Format(workspace, "review-pack", "compact", new { command = "review-pack" });
            Assert.Equal(arguments, result["reproCommand"]!["arguments"]!.AsArray().Select(value => value!.GetValue<string>()));
            Assert.Equal("/repo with spaces", result["reproCommand"]!["workingDirectory"]!.GetValue<string>());
            using (CliInvocationContext batch = CliInvocationContext.Begin(["batch", "--workspace", "navlyn.slnx"]))
            {
                batch.StandardInput = "{\"requests\":[]}";
                JsonObject nested = OutputProfile.Format(workspace, "review-pack", "compact", new { command = "review-pack" });
                Assert.Equal("batch", nested["reproCommand"]!["arguments"]![0]!.GetValue<string>());
                Assert.Equal(batch.StandardInput, nested["reproCommand"]!["standardInput"]!.GetValue<string>());
            }
            Assert.Same(invocation, CliInvocationContext.Current);
        }
        Assert.Null(CliInvocationContext.Current);
    }
}

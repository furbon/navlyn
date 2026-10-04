// Independent acceptance checks, injected only after the measured session.
// Public fixture APIs use fake credentials; no network or OS credential store access.
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GitCredentialManager.Commands;
using GitCredentialManager.Tests.Objects;
using Moq;
using Xunit;

namespace GitCredentialManager.Tests;

public class NavlynEvaluationAcceptanceTests
{
    [Fact]
    public void Response_PreservesLegacyCallsAndExposesOptionalMetadata()
    {
        var credential = new GitCredential("alice", "fixture-only");
        Assert.Null(new GitResponse(credential).AuthType);
        Assert.False(GitResponse.Ok(credential).IsEphemeral);
        var response = new GitResponse(credential, authType: "Bearer", isEphemeral: true);
        Assert.Equal("Bearer", response.AuthType);
        Assert.True(response.IsEphemeral);
        Assert.Equal("Bearer", GitResponse.Ok(credential, authType: "Bearer").AuthType);
    }

    [Theory]
    [InlineData(true, "Bearer", true, true)]
    [InlineData(true, "Bearer", false, true)]
    [InlineData(false, "Bearer", true, false)]
    [InlineData(true, null, true, false)]
    public async Task Get_NegotiatesTypedOutputAndPreservesState(bool advertised, string authType, bool ephemeral, bool typed)
    {
        const string secret = "fixture-secret-never-log";
        var response = GitResponse.Continue(new GitCredential("alice", secret), authType: authType, isEphemeral: ephemeral).WithState("step", "next");
        var provider = new Mock<IHostProvider>();
        provider.Setup(p => p.GetCredentialAsync(It.IsAny<GitRequest>())).ReturnsAsync(response);
        using var trace = new Trace();
        var traceOutput = new StringWriter();
        trace.AddListener(traceOutput);
        var context = new TestCommandContext
        {
            Trace = trace,
            Streams = { In = "protocol=https\nhost=example.com\ncapability[]=state\n" + (advertised ? "capability[]=authtype\n" : "") + "\n" }
        };
        await new GetCommand(context, new TestHostProviderRegistry { Provider = provider.Object }).ExecuteAsync();
        string output = context.Streams.Out.ToString().Replace("\r\n", "\n");
        Assert.Contains("capability[]=state\n", output);
        Assert.Contains("continue=1\n", output);
        Assert.Contains("state[]=" + Constants.CredentialProtocol.GcmStatePrefix + "step=next\n", output);
        if (typed)
        {
            Assert.Contains("authtype=Bearer\n", output);
            Assert.Contains("credential=" + secret + "\n", output);
            Assert.DoesNotContain("username=", output);
            Assert.DoesNotContain("password=", output);
        }
        else
        {
            Assert.Contains("username=alice\n", output);
            Assert.Contains("password=" + secret + "\n", output);
            Assert.DoesNotContain("authtype=", output);
            Assert.DoesNotContain("credential=", output);
        }
        Assert.Equal(typed && ephemeral, output.Contains("ephemeral=1\n"));
        Assert.EndsWith("\n\n", output);
        Assert.DoesNotContain(secret, traceOutput.ToString());
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("0", false)]
    [InlineData("1", true)]
    [InlineData("true", false)]
    public void Request_ParsesTypedFields(string ephemeral, bool expected)
    {
        var input = new Dictionary<string, string> { ["authtype"] = "Bearer", ["credential"] = "" };
        if (ephemeral != null) input["ephemeral"] = ephemeral;
        var request = new GitRequest(input);
        Assert.Equal("Bearer", request.AuthType);
        Assert.Equal("", request.Credential);
        Assert.Equal(expected, request.IsEphemeral);
        Assert.Equal(GitCapabilities.AuthType, GitCapabilitiesUtils.ParseName("AUTHTYPE"));
        Assert.Equal("authtype", GitCapabilitiesUtils.ToProtocolName(GitCapabilities.AuthType));
        Assert.Equal(GitCapabilities.State | GitCapabilities.AuthType, Constants.SupportedCapabilities);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("fixture-incoming-secret", true)]
    [InlineData(null, false)]
    public async Task Store_RequiresSuppliedTypedCredentialAndRedactsIt(string credential, bool valid)
    {
        var provider = new Mock<IHostProvider>();
        provider.Setup(p => p.StoreCredentialAsync(It.IsAny<GitRequest>())).Returns(Task.CompletedTask);
        using var trace = new Trace();
        var traceOutput = new StringWriter();
        trace.AddListener(traceOutput);
        var context = new TestCommandContext
        {
            Trace = trace,
            Streams = { In = "protocol=https\nhost=example.com\nauthtype=Bearer\n" + (credential != null ? "credential=" + credential + "\n" : "") + "\n" }
        };
        var command = new StoreCommand(context, new TestHostProviderRegistry { Provider = provider.Object });
        if (valid)
        {
            await command.ExecuteAsync();
            provider.Verify(p => p.StoreCredentialAsync(It.Is<GitRequest>(r => r.Credential == credential && r.AuthType == "Bearer")), Times.Once);
            if (!string.IsNullOrEmpty(credential)) Assert.DoesNotContain(credential, traceOutput.ToString());
        }
        else await Assert.ThrowsAsync<InvalidOperationException>(() => command.ExecuteAsync());
    }
}

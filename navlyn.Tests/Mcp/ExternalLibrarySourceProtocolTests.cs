using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Navlyn.Mcp.Tools;
using Navlyn.Tests.TestSupport;

namespace Navlyn.Tests.Mcp;

[Collection(ExternalReadCollection.Name)]
public sealed class ExternalLibrarySourceProtocolTests
{
    [Fact]
    public async Task OversizedImplementationPe_FailsWithoutBody()
    {
        ExternalLibrarySourceFixture fixture = await ExternalLibrarySourceFixture.PrepareAsync();
        using DisposableConsumer consumer = await DisposableConsumer.CreateAsync(fixture);
        using (FileStream stream = new(consumer.ImplementationPe, FileMode.Open, FileAccess.Write))
        {
            stream.SetLength(64L * 1024 * 1024 + 1);
        }

        ExternalLibrarySourceFixture.CliResult response = await fixture.RunReadAsync(
            consumer.Project, consumer.Source, 4, 25, "decompiled", projectName: "Consumer(net10.0)");
        Assert.NotEqual(0, response.ExitCode);
        Assert.Contains("NAVLYN1406", response.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("FIXTURE_NET10_INT_OVERLOAD_BODY", response.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RidSpecificRuntimeAssets_FailClosedWithoutChoosingAFrameworkDll()
    {
        ExternalLibrarySourceFixture fixture = await ExternalLibrarySourceFixture.PrepareAsync();
        using DisposableConsumer consumer = await DisposableConsumer.CreateAsync(fixture);
        string assetsPath = Path.Combine(consumer.Root, "Consumer", "obj", "project.assets.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(assetsPath));
        using MemoryStream buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            using JsonDocument patched = JsonDocument.Parse(document.RootElement.GetRawText());
            WriteWithRidSpecificAsset(patched.RootElement, writer);
        }

        File.WriteAllBytes(assetsPath, buffer.ToArray());
        ExternalLibrarySourceFixture.CliResult response = await fixture.RunReadAsync(
            consumer.Project, consumer.Source, 4, 25, "decompiled", projectName: "Consumer(net10.0)");
        Assert.NotEqual(0, response.ExitCode);
        Assert.Contains("NAVLYN1402", response.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("FIXTURE_NET10_INT_OVERLOAD_BODY", response.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SameProcessRead_UsesExactImplementationAndNeverReturnsReplacedBody()
    {
        ExternalLibrarySourceFixture fixture = await ExternalLibrarySourceFixture.PrepareAsync();
        using DisposableConsumer consumer = await DisposableConsumer.CreateAsync(fixture);
        string serverDll = GetServerAssembly(fixture.CliAssembly);
        Assert.True(File.Exists(serverDll), serverDll);

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(180));
        StdioClientTransport transport = new(
            new StdioClientTransportOptions
            {
                Command = "dotnet",
                Arguments =
                [
                    serverDll,
                    "--workspace", consumer.Project,
                    "--working-directory", consumer.Root,
                    "--timeout-ms", "60000",
                    "--max-json-chars", "4000000"
                ],
                WorkingDirectory = consumer.Root
            },
            NullLoggerFactory.Instance);

        await using McpClient client = await McpClient.CreateAsync(
            transport,
            new McpClientOptions
            {
                ClientInfo = new Implementation { Name = "navlyn-external-protocol-test", Version = "1.0.0" }
            },
            NullLoggerFactory.Instance,
            timeout.Token);

        IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        Assert.Equal(4, tools.Count);
        McpClientTool readTool = Assert.Single(tools, tool => tool.Name == NavlynMcpTools.ReadTool);
        Assert.True(readTool.JsonSchema.GetProperty("properties").TryGetProperty("externalSource", out _));

        Dictionary<string, object?> arguments = new()
        {
            ["file"] = consumer.Source,
            ["line"] = 4,
            ["column"] = 25,
            ["project"] = "Consumer(net10.0)",
            ["view"] = "body"
        };
        CallToolResult defaultResponse = await client.CallToolAsync(NavlynMcpTools.ReadTool, arguments, cancellationToken: timeout.Token);
        JsonElement defaultResult = RequireResult(defaultResponse);
        Assert.Empty(defaultResult.GetProperty("slices").EnumerateArray());
        Assert.False(defaultResult.TryGetProperty("sourceOrigin", out _));

        arguments["externalSource"] = "decompiled";
        CallToolResult firstResponse = await client.CallToolAsync(NavlynMcpTools.ReadTool, arguments, cancellationToken: timeout.Token);
        JsonElement firstResult = RequireResult(firstResponse);
        Assert.Equal("M:Navlyn.ExternalFixture.Probe.Pick(System.Int32)",
            firstResult.GetProperty("symbol").GetProperty("facts").GetProperty("documentationCommentId").GetString());
        Assert.Equal("decompiled", firstResult.GetProperty("sourceOrigin").GetString());
        JsonElement assembly = firstResult.GetProperty("externalAssembly");
        Assert.Equal("net10.0", assembly.GetProperty("targetFramework").GetString());
        Assert.Equal("implementation", assembly.GetProperty("selectedAssembly").GetString());
        Assert.Equal(HashFile(consumer.ReferencePe), assembly.GetProperty("referenceSha256").GetString());
        Assert.Equal(HashFile(consumer.ImplementationPe), assembly.GetProperty("implementationSha256").GetString());
        JsonElement slice = Assert.Single(firstResult.GetProperty("slices").EnumerateArray());
        Assert.Equal("decompiled", slice.GetProperty("origin").GetString());
        Assert.False(slice.GetProperty("editable").GetBoolean());
        Assert.StartsWith("navlyn-decompiled://", slice.GetProperty("path").GetString());
        string firstText = SliceText(firstResult);
        Assert.Contains("FIXTURE_NET10_INT_OVERLOAD_BODY", firstText, StringComparison.Ordinal);
        Assert.DoesNotContain("FIXTURE_STRING_OVERLOAD_BODY", firstText, StringComparison.Ordinal);

        JsonElement cliResult = await consumer.ReadCliAsync(fixture.CliAssembly);
        Assert.Equal(firstResult.GetProperty("sourceOrigin").GetString(), cliResult.GetProperty("sourceOrigin").GetString());
        Assert.Equal(firstResult.GetProperty("externalAssembly").GetProperty("implementationSha256").GetString(),
            cliResult.GetProperty("externalAssembly").GetProperty("implementationSha256").GetString());
        Assert.Equal(SliceText(firstResult), SliceText(cliResult));

        consumer.ReplaceImplementationMarker();
        CallToolResult secondResponse = await client.CallToolAsync(NavlynMcpTools.ReadTool, arguments, cancellationToken: timeout.Token);
        string secondJson = secondResponse.StructuredContent?.ToString() ?? string.Empty;
        Assert.DoesNotContain("FIXTURE_NET10_INT_OVERLOAD_BODY", secondJson, StringComparison.Ordinal);
        if (secondResponse.StructuredContent is { } structured && structured.GetProperty("ok").GetBoolean())
        {
            JsonElement secondResult = structured.GetProperty("result");
            Assert.Contains("CHANGED_NET10_INT_OVERLOAD_BODY", SliceText(secondResult), StringComparison.Ordinal);
            Assert.Equal(HashFile(consumer.ImplementationPe),
                secondResult.GetProperty("externalAssembly").GetProperty("implementationSha256").GetString());
        }
        else
        {
            Assert.Contains("NAVLYN1405", secondJson, StringComparison.Ordinal);
        }

        consumer.ReplaceReferenceMemberName();
        CallToolResult thirdResponse = await client.CallToolAsync(NavlynMcpTools.ReadTool, arguments, cancellationToken: timeout.Token);
        string thirdJson = thirdResponse.StructuredContent?.ToString() ?? string.Empty;
        Assert.DoesNotContain("FIXTURE_NET10_INT_OVERLOAD_BODY", thirdJson, StringComparison.Ordinal);
        Assert.DoesNotContain("CHANGED_NET10_INT_OVERLOAD_BODY", thirdJson, StringComparison.Ordinal);
        if (thirdResponse.StructuredContent is { } thirdStructured && thirdStructured.GetProperty("ok").GetBoolean())
        {
            Assert.False(thirdStructured.GetProperty("metadata").GetProperty("workspaceCacheHit").GetBoolean());
            JsonElement thirdResult = thirdStructured.GetProperty("result");
            Assert.False(thirdResult.GetProperty("symbol").GetProperty("facts").GetProperty("isMetadata").GetBoolean());
            Assert.False(thirdResult.TryGetProperty("sourceOrigin", out _));
            Assert.False(thirdResult.TryGetProperty("externalAssembly", out _));
            Assert.All(thirdResult.GetProperty("slices").EnumerateArray(),
                item => Assert.Equal("Consumer/Program.cs", item.GetProperty("path").GetString()));
        }
        else
        {
            Assert.Contains("NAVLYN1405", thirdJson, StringComparison.Ordinal);
        }
    }

    private static JsonElement RequireResult(CallToolResult response)
    {
        Assert.False(response.IsError, response.StructuredContent?.ToString());
        Assert.True(response.StructuredContent.HasValue);
        JsonElement structured = response.StructuredContent.Value;
        Assert.True(structured.GetProperty("ok").GetBoolean(), structured.ToString());
        return structured.GetProperty("result");
    }

    private static void WriteWithRidSpecificAsset(JsonElement element, Utf8JsonWriter writer)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (JsonProperty property in element.EnumerateObject())
            {
                writer.WritePropertyName(property.Name);
                WriteWithRidSpecificAsset(property.Value, writer);
                if (property.Name == "runtime" && property.Value.ValueKind == JsonValueKind.Object &&
                    property.Value.EnumerateObject().Any(asset => asset.Name.EndsWith("ExternalFixture.dll", StringComparison.Ordinal)))
                {
                    writer.WritePropertyName("runtimeTargets");
                    writer.WriteStartObject();
                    writer.WritePropertyName("runtimes/win-x64/lib/net10.0/ExternalFixture.dll");
                    writer.WriteStartObject();
                    writer.WriteString("rid", "win-x64");
                    writer.WriteString("assetType", "runtime");
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                }
            }

            writer.WriteEndObject();
            return;
        }

        element.WriteTo(writer);
    }

    private static string SliceText(JsonElement result) => string.Join("\n",
        result.GetProperty("slices").EnumerateArray().SelectMany(slice =>
            slice.GetProperty("lines").EnumerateArray().Select(line => line.GetString())));

    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static string GetServerAssembly(string cliAssembly)
    {
        DirectoryInfo output = new(Path.GetDirectoryName(cliAssembly)!);
        string targetFramework = output.Name;
        string configuration = output.Parent!.Name;
        string repository = output.Parent.Parent!.Parent!.Parent!.FullName;
        return Path.Combine(repository, "navlyn.Mcp", "bin", configuration, targetFramework, "navlyn.Mcp.dll");
    }

    private sealed class DisposableConsumer(string root) : IDisposable
    {
        private const string Owner = "navlyn-external-library-source-protocol-test";

        public string Root { get; } = root;
        public string Project => Path.Combine(Root, "Consumer", "Consumer.csproj");
        public string Source => Path.Combine(Root, "Consumer", "Program.cs");
        public string ReferencePe => Path.Combine(Root, "nuget", "navlyn.externallibrarysourcefixture", "1.0.0", "ref", "net10.0", "ExternalFixture.dll");
        public string ImplementationPe => Path.Combine(Root, "nuget", "navlyn.externallibrarysourcefixture", "1.0.0", "lib", "net10.0", "ExternalFixture.dll");

        public static async Task<DisposableConsumer> CreateAsync(ExternalLibrarySourceFixture fixture)
        {
            string root = Path.Combine(Path.GetTempPath(), "navlyn-external-protocol-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, ".owner"), Owner);
            DisposableConsumer consumer = new(root);
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Consumer"));
                Directory.CreateDirectory(Path.Combine(root, "feed"));
                string fixtureRoot = Directory.GetParent(Path.GetDirectoryName(fixture.ConsumerProject)!)!.FullName;
                File.Copy(Path.Combine(fixtureRoot, "global.json"), Path.Combine(root, "global.json"));
                File.Copy(fixture.ConsumerProject, consumer.Project);
                File.Copy(fixture.ConsumerSource, consumer.Source);
                File.Copy(Path.Combine(fixtureRoot, "packages", "Navlyn.ExternalLibrarySourceFixture.1.0.0.nupkg"),
                    Path.Combine(root, "feed", "Navlyn.ExternalLibrarySourceFixture.1.0.0.nupkg"));
                await RunProcessAsync("dotnet", ["restore", consumer.Project, "--source", Path.Combine(root, "feed"),
                    "--packages", Path.Combine(root, "nuget")], root);
                await RunProcessAsync("dotnet", ["build", consumer.Project, "-c", "Release", "--no-restore"], root);
                Assert.True(File.Exists(consumer.ReferencePe), consumer.ReferencePe);
                Assert.True(File.Exists(consumer.ImplementationPe), consumer.ImplementationPe);
                return consumer;
            }
            catch
            {
                consumer.Dispose();
                throw;
            }
        }

        public async Task<JsonElement> ReadCliAsync(string cliAssembly)
        {
            string stdout = await RunProcessAsync("dotnet",
            [
                cliAssembly, "read", "--workspace", Project, "--file", Source,
                "--line", "4", "--column", "25", "--project", "Consumer(net10.0)",
                "--view", "body", "--external-source", "decompiled"
            ], Root);
            using JsonDocument document = JsonDocument.Parse(stdout);
            return document.RootElement.Clone();
        }

        public void ReplaceImplementationMarker()
        {
            byte[] bytes = File.ReadAllBytes(ImplementationPe);
            byte[] oldMarker = Encoding.Unicode.GetBytes("FIXTURE_NET10_INT_OVERLOAD_BODY");
            byte[] newMarker = Encoding.Unicode.GetBytes("CHANGED_NET10_INT_OVERLOAD_BODY");
            Assert.Equal(oldMarker.Length, newMarker.Length);
            int match = FindBytes(bytes, oldMarker);
            Assert.True(match >= 0, "Expected a UTF-16 implementation marker in the PE.");
            DateTime lastWrite = File.GetLastWriteTimeUtc(ImplementationPe);
            string oldHash = HashFile(ImplementationPe);
            do
            {
                Array.Copy(newMarker, 0, bytes, match, newMarker.Length);
                match = FindBytes(bytes, oldMarker, match + oldMarker.Length);
            }
            while (match >= 0);
            File.WriteAllBytes(ImplementationPe, bytes);
            File.SetLastWriteTimeUtc(ImplementationPe, lastWrite);
            Assert.Equal((long)bytes.Length, new FileInfo(ImplementationPe).Length);
            Assert.Equal(lastWrite, File.GetLastWriteTimeUtc(ImplementationPe));
            Assert.NotEqual(oldHash, HashFile(ImplementationPe));
        }

        public void ReplaceReferenceMemberName()
        {
            byte[] bytes = File.ReadAllBytes(ReferencePe);
            byte[] oldName = Encoding.ASCII.GetBytes("Pick\0");
            byte[] newName = Encoding.ASCII.GetBytes("Pock\0");
            int match = FindBytes(bytes, oldName);
            Assert.True(match >= 0, "Expected exact method name in reference metadata.");
            DateTime lastWrite = File.GetLastWriteTimeUtc(ReferencePe);
            string oldHash = HashFile(ReferencePe);
            do
            {
                Array.Copy(newName, 0, bytes, match, newName.Length);
                match = FindBytes(bytes, oldName, match + oldName.Length);
            }
            while (match >= 0);
            File.WriteAllBytes(ReferencePe, bytes);
            File.SetLastWriteTimeUtc(ReferencePe, lastWrite);
            Assert.Equal((long)bytes.Length, new FileInfo(ReferencePe).Length);
            Assert.Equal(lastWrite, File.GetLastWriteTimeUtc(ReferencePe));
            Assert.NotEqual(oldHash, HashFile(ReferencePe));
        }

        public void Dispose()
        {
            string tempRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
            string fullRoot = Path.GetFullPath(Root);
            if (!fullRoot.StartsWith(tempRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(fullRoot).StartsWith("navlyn-external-protocol-", StringComparison.Ordinal) ||
                !File.Exists(Path.Combine(fullRoot, ".owner")) ||
                File.ReadAllText(Path.Combine(fullRoot, ".owner")) != Owner)
            {
                throw new InvalidOperationException($"Refusing to remove unowned protocol fixture: {fullRoot}");
            }

            TemporaryDirectoryCleanup.Delete(fullRoot);
        }

        private static int FindBytes(byte[] haystack, byte[] needle, int start = 0)
        {
            for (int index = start; index <= haystack.Length - needle.Length; index++)
            {
                if (haystack.AsSpan(index, needle.Length).SequenceEqual(needle)) return index;
            }

            return -1;
        }

        private static async Task<string> RunProcessAsync(string executable, IReadOnlyList<string> arguments, string workingDirectory)
        {
            using Process process = new();
            process.StartInfo = new ProcessStartInfo(executable)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            process.StartInfo.Environment.Remove("DOTNET_MSBUILD_SDK_RESOLVER_SDKS_DIR");
            process.StartInfo.Environment.Remove("DOTNET_MSBUILD_SDK_RESOLVER_SDKS_VER");
            process.StartInfo.Environment.Remove("DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR");
            process.StartInfo.Environment.Remove("MSBuildSDKsPath");
            process.StartInfo.Environment.Remove("MSBUILD_EXE_PATH");
            foreach (string argument in arguments) process.StartInfo.ArgumentList.Add(argument);
            Assert.True(process.Start());
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(120));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }

            string output = await stdout;
            string errors = await stderr;
            Assert.True(process.ExitCode == 0, $"{executable} {string.Join(' ', arguments)} failed: {errors}\n{output}");
            return output;
        }
    }
}

using System.Diagnostics;

using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using Navlyn.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;

namespace Navlyn.Tests.TestSupport;

internal sealed class ExternalLibrarySourceFixture
{
    private static readonly SemaphoreSlim SetupLock = new(1, 1);
    private static Task? setupTask;
    private static ExternalLibrarySourceFixture? shared;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task<McpClient>> readers = new();

    private readonly string root;
    private readonly string fixtureRoot;

    private ExternalLibrarySourceFixture(string root)
    {
        this.root = root;
        fixtureRoot = Path.Combine(root, "tests", "fixtures", "ExternalLibrarySourceFixture");
    }

    public string ConsumerProject => Path.Combine(fixtureRoot, "Consumer", "Consumer.csproj");
    public string ConsumerSource => Path.Combine(fixtureRoot, "Consumer", "Program.cs");
    public string ArtifactsProject => Path.Combine(fixtureRoot, "RedirectedOutput", "RedirectedOutput.csproj");
    public string ArtifactsSource => Path.Combine(fixtureRoot, "RedirectedOutput", "Program.cs");
    public string DirectProject => Path.Combine(fixtureRoot, "Direct", "Direct.csproj");
    public string DirectSource => Path.Combine(fixtureRoot, "Direct", "Program.cs");
    public string VisualBasicProject => Path.Combine(fixtureRoot, "VisualBasic", "VisualBasic.vbproj");
    public string VisualBasicSource => Path.Combine(fixtureRoot, "VisualBasic", "Program.vb");
    public string ReferenceOnlyProject => Path.Combine(fixtureRoot, "ReferenceOnly", "ReferenceOnly.csproj");
    public string ReferenceOnlySource => Path.Combine(fixtureRoot, "ReferenceOnly", "Program.cs");
    public string CliAssembly
    {
        get
        {
            string targetFramework = Path.GetFileName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
            string configuration = Directory.GetParent(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory))?.Name
                ?? throw new InvalidOperationException("Could not determine the test build configuration.");
            return Path.Combine(root, "navlyn", "bin", configuration, targetFramework, "navlyn.dll");
        }
    }

    private string PackagesHomeRoot => Path.Combine(fixtureRoot, ".nuget-home");
    private string PackagesHome => Path.Combine(PackagesHomeRoot, "packages");

    public static async Task<ExternalLibrarySourceFixture> PrepareAsync()
    {
        string root = FindRepositoryRoot();
        await SetupLock.WaitAsync();
        try
        {
            ExternalLibrarySourceFixture fixture = shared ??= new(root);
            setupTask ??= fixture.BuildFromSourceAsync();
            await setupTask;
            return fixture;
        }
        finally
        {
            SetupLock.Release();
        }

    }

    public async Task<CliResult> RunReadAsync(
        string project,
        string source,
        int line,
        int column,
        string? externalSource,
        string view = "body",
        string? projectName = null,
        int? maxLines = null,
        int? budgetTokens = null,
        string? externalMember = null)
    {
        bool sharedProject = project.StartsWith(fixtureRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        McpClient client = sharedProject ? await readers.GetOrAdd(project, CreateReaderAsync) : await CreateReaderAsync(project);
        try
        {
            using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(20));
            var response = await client.CallToolAsync(NavlynMcpTools.ReadTool, new Dictionary<string, object?>
            {
                ["file"] = source, ["line"] = line, ["column"] = column, ["view"] = view,
                ["project"] = projectName, ["maxLines"] = maxLines, ["budgetTokens"] = budgetTokens,
                ["externalSource"] = externalSource, ["externalMember"] = externalMember
            }, cancellationToken: deadline.Token);
            JsonElement result = response.StructuredContent!.Value;
            return result.GetProperty("ok").GetBoolean()
                ? new(0, result.GetProperty("result").GetRawText(), "")
                : new(1, "", result.GetProperty("error").ToString());
        }
        finally
        {
            if (!sharedProject) { await client.DisposeAsync(); }
        }
    }

    public Task<CliResult> RunBatchAsync(object payload) => RunProcessAsync("dotnet",
        [CliAssembly, "batch", "--workspace", ConsumerProject], root, TimeSpan.FromSeconds(60),
        JsonSerializer.Serialize(payload));

    private async Task<McpClient> CreateReaderAsync(string project)
    {
        string framework = Path.GetFileName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
        string configuration = Directory.GetParent(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory))!.Name;
        StdioClientTransport transport = new(new StdioClientTransportOptions
        {
            Command = "dotnet",
            Arguments = [Path.Combine(root, "navlyn.Mcp", "bin", configuration, framework, "navlyn.Mcp.dll"),
                "--workspace", project, "--working-directory", root, "--timeout-ms", "15000"],
            WorkingDirectory = root,
            EnvironmentVariables = new Dictionary<string, string?> { ["NUGET_PACKAGES"] = PackagesHome }
        }, NullLoggerFactory.Instance);
        return await McpClient.CreateAsync(transport);
    }

    internal static async Task DisposeReadersAsync()
    {
        if (shared is null) { return; }
        foreach (Task<McpClient> reader in shared.readers.Values) { await (await reader).DisposeAsync(); }
        shared.readers.Clear();
    }

    public (int Line, int Column) Position(string sourcePath, string lineMarker, string target)
    {
        string[] lines = File.ReadAllLines(sourcePath);
        for (int index = 0; index < lines.Length; index++)
        {
            if (lines[index].Contains(lineMarker, StringComparison.Ordinal))
            {
                int column = lines[index].IndexOf(target, StringComparison.Ordinal);
                if (column >= 0)
                {
                    return (index + 1, column + 1);
                }
            }
        }

        throw new InvalidOperationException($"Could not find '{target}' on a line containing '{lineMarker}' in {sourcePath}.");
    }

    private async Task BuildFromSourceAsync()
    {
        EnsurePackagesHomeOwnership();
        foreach (string relative in new[]
        {
            "Library/bin", "Library/obj", "Consumer/bin", "Consumer/obj", "Direct/bin", "Direct/obj",
            "VisualBasic/bin", "VisualBasic/obj", "ReferenceOnly/bin", "ReferenceOnly/obj",
            "ReferenceOnlyPackage/bin", "ReferenceOnlyPackage/obj", "RedirectedOutput/obj", "out", "packages", ".nuget-home/packages"
        })
        {
            string path = Path.GetFullPath(Path.Combine(fixtureRoot, relative));
            if (!path.StartsWith(fixtureRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Fixture cleanup path escaped its root: {path}");
            }

            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }

        string packageFeed = Path.Combine(fixtureRoot, "packages");
        Directory.CreateDirectory(packageFeed);
        await RequireSuccessAsync(await RunProcessAsync("dotnet",
            ["pack", Path.Combine(fixtureRoot, "Library", "Library.csproj"), "-c", "Release", "-o", packageFeed, "--source", packageFeed, "--ignore-failed-sources"],
            fixtureRoot, TimeSpan.FromSeconds(180)), "fixture package pack");

        await RequireSuccessAsync(await RunProcessAsync("dotnet",
            ["pack", Path.Combine(fixtureRoot, "ReferenceOnlyPackage", "ReferenceOnlyPackage.csproj"), "-c", "Release", "-o", packageFeed, "--source", packageFeed, "--ignore-failed-sources"],
            fixtureRoot, TimeSpan.FromSeconds(180)), "reference-only fixture package pack");

        ValidatePackages(packageFeed);

        await RestoreAndBuildAsync(ConsumerProject, packageFeed);
        await RestoreAndBuildAsync(ArtifactsProject, packageFeed);
        await RestoreAndBuildAsync(DirectProject, packageFeed);
        await RestoreAndBuildAsync(VisualBasicProject, packageFeed);
        await RestoreAndBuildAsync(ReferenceOnlyProject, packageFeed);
        (string refWindows, string libWindows) = ValidateConsumerAssets(ConsumerProject, "net10.0-windows7.0", "Navlyn.ExternalLibrarySourceFixture");
        (string ref10, string lib10) = ValidateConsumerAssets(ConsumerProject, "net10.0", "Navlyn.ExternalLibrarySourceFixture");
        if (refWindows == ref10 || libWindows == lib10)
        {
            throw new InvalidOperationException("Expected different reference and implementation PE hashes across net10.0-windows7.0 and net10.0.");
        }

        ValidateConsumerAssets(ReferenceOnlyProject, "net10.0", "Navlyn.ExternalLibrarySourceFixture.ReferenceOnly", referenceOnly: true);
        if (!File.Exists(CliAssembly) || !File.Exists(Path.ChangeExtension(CliAssembly, ".runtimeconfig.json")))
        {
            throw new InvalidOperationException($"CLI assembly and runtimeconfig must exist for {CliAssembly}.");
        }
    }

    private async Task RestoreAndBuildAsync(string project, string packageFeed)
    {
        await RequireSuccessAsync(await RunProcessAsync("dotnet",
            ["restore", project, "--source", packageFeed, "--packages", PackagesHome, "--ignore-failed-sources"], fixtureRoot, TimeSpan.FromSeconds(180)),
            $"restore {Path.GetFileName(project)}");
        await RequireSuccessAsync(await RunProcessAsync("dotnet",
            ["build", project, "-c", "Release", "--no-restore"], fixtureRoot, TimeSpan.FromSeconds(180)),
            $"build {Path.GetFileName(project)}");
    }

    private void EnsurePackagesHomeOwnership()
    {
        Directory.CreateDirectory(PackagesHomeRoot);
        string markerPath = Path.Combine(PackagesHomeRoot, ".owner");
        const string markerValue = "navlyn-external-library-source-fixture";
        if (File.Exists(markerPath))
        {
            if (File.ReadAllText(markerPath).Trim() != markerValue)
            {
                throw new InvalidOperationException($"NuGet home owner marker does not match: {markerPath}");
            }
        }
        else
        {
            if (Directory.EnumerateFileSystemEntries(PackagesHomeRoot).Any())
            {
                throw new InvalidOperationException($"Refusing to claim a non-empty unmarked NuGet home: {PackagesHomeRoot}");
            }

            File.WriteAllText(markerPath, markerValue + Environment.NewLine);
        }
    }

    private void ValidatePackages(string packageFeed)
    {
        string regularPackage = Path.Combine(packageFeed, "Navlyn.ExternalLibrarySourceFixture.1.0.0.nupkg");
        string referencePackage = Path.Combine(packageFeed, "Navlyn.ExternalLibrarySourceFixture.ReferenceOnly.1.0.0.nupkg");
        ValidatePackageEntries(regularPackage,
        [
            "ref/net10.0-windows7.0/ExternalFixture.dll", "lib/net10.0-windows7.0/ExternalFixture.dll",
            "ref/net10.0/ExternalFixture.dll", "lib/net10.0/ExternalFixture.dll"
        ], requireNoLibraryAssets: false);
        ValidatePackageEntries(referencePackage,
        [
            "ref/net10.0-windows7.0/ExternalFixture.dll",
            "ref/net10.0/ExternalFixture.dll"
        ], requireNoLibraryAssets: true);
    }

    private static void ValidatePackageEntries(string packagePath, IReadOnlyList<string> expectedEntries, bool requireNoLibraryAssets)
    {
        using ZipArchive archive = ZipFile.OpenRead(packagePath);
        string[] entries = archive.Entries.Select(entry => entry.FullName).ToArray();
        foreach (string expected in expectedEntries)
        {
            if (!entries.Contains(expected, StringComparer.Ordinal))
            {
                throw new InvalidOperationException($"Package {packagePath} is missing {expected}. Entries: {string.Join(", ", entries)}");
            }
        }

        if (requireNoLibraryAssets && entries.Any(entry => entry.StartsWith("lib/", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Reference-only package unexpectedly contains lib assets: {packagePath}");
        }
    }

    private (string CompileHash, string RuntimeHash) ValidateConsumerAssets(string project, string targetFramework, string packageId, bool referenceOnly = false)
    {
        string assetsPath = Path.Combine(Path.GetDirectoryName(project)!, "obj", "project.assets.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(assetsPath));
        JsonElement rootElement = document.RootElement;
        string packageKey = packageId + "/1.0.0";
        JsonElement target = rootElement.GetProperty("targets").GetProperty(targetFramework).GetProperty(packageKey);
        string compileRelative = target.GetProperty("compile").EnumerateObject()
            .Select(property => property.Name.Replace('\\', '/'))
            .Single(path => path.EndsWith("ExternalFixture.dll", StringComparison.Ordinal));
        string expectedCompilePrefix = referenceOnly ? "ref/" : "ref/";
        if (!compileRelative.StartsWith(expectedCompilePrefix + targetFramework + "/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Compile asset for {packageKey} {targetFramework} is {compileRelative}, expected a {expectedCompilePrefix} asset.");
        }

        string packageRelative = rootElement.GetProperty("libraries").GetProperty(packageKey).GetProperty("path").GetString()!;
        string packageDirectory = Path.Combine(PackagesHome, packageRelative.Replace('/', Path.DirectorySeparatorChar));
        string compilePe = Path.Combine(packageDirectory, compileRelative.Replace('/', Path.DirectorySeparatorChar));
        string compileHash = HashFile(compilePe);
        string nupkgPath = Path.Combine(fixtureRoot, "packages", packageId + ".1.0.0.nupkg");
        if (compileHash != HashPackageEntry(nupkgPath, compileRelative))
        {
            throw new InvalidOperationException($"Compile PE hash does not match the produced package entry {compileRelative}.");
        }

        if (!HasReferenceAssemblyAttribute(compilePe))
        {
            throw new InvalidOperationException($"Compile PE is missing ReferenceAssemblyAttribute: {compilePe}");
        }

        string runtimeRelative = string.Empty;
        if (target.TryGetProperty("runtime", out JsonElement runtime))
        {
            runtimeRelative = runtime.EnumerateObject().Select(property => property.Name.Replace('\\', '/'))
                .FirstOrDefault(path => path.EndsWith("ExternalFixture.dll", StringComparison.Ordinal)) ?? string.Empty;
        }

        if (referenceOnly)
        {
            if (!string.IsNullOrEmpty(runtimeRelative))
            {
                throw new InvalidOperationException($"Reference-only package selected runtime asset {runtimeRelative}.");
            }

            return (compileHash, string.Empty);
        }

        if (!runtimeRelative.StartsWith("lib/" + targetFramework + "/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Runtime asset for {packageKey} {targetFramework} is {runtimeRelative}, expected lib/{targetFramework}.");
        }

        string runtimePe = Path.Combine(packageDirectory, runtimeRelative.Replace('/', Path.DirectorySeparatorChar));
        string runtimeHash = HashFile(runtimePe);
        if (runtimeHash != HashPackageEntry(nupkgPath, runtimeRelative))
        {
            throw new InvalidOperationException($"Runtime PE hash does not match the produced package entry {runtimeRelative}.");
        }

        if (compileHash == runtimeHash)
        {
            throw new InvalidOperationException($"Compile and runtime PE hashes are identical for {packageKey} {targetFramework}.");
        }

        if (HasReferenceAssemblyAttribute(runtimePe))
        {
            throw new InvalidOperationException($"Runtime PE is marked ReferenceAssembly: {runtimePe}");
        }

        return (compileHash, runtimeHash);
    }

    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static string HashPackageEntry(string packagePath, string entryName)
    {
        using ZipArchive archive = ZipFile.OpenRead(packagePath);
        ZipArchiveEntry entry = archive.GetEntry(entryName)
            ?? throw new InvalidOperationException($"Package {packagePath} is missing {entryName}.");
        using Stream stream = entry.Open();
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool HasReferenceAssemblyAttribute(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using PEReader peReader = new(stream);
        MetadataReader metadata = peReader.GetMetadataReader();
        foreach (CustomAttributeHandle handle in metadata.GetAssemblyDefinition().GetCustomAttributes())
        {
            CustomAttribute attribute = metadata.GetCustomAttribute(handle);
            EntityHandle parent = attribute.Constructor.Kind switch
            {
                HandleKind.MemberReference => metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent,
                HandleKind.MethodDefinition => metadata.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType(),
                _ => default
            };
            if (parent.Kind is HandleKind.TypeReference or HandleKind.TypeDefinition)
            {
                (string ns, string name) = parent.Kind == HandleKind.TypeReference
                    ? (metadata.GetString(metadata.GetTypeReference((TypeReferenceHandle)parent).Namespace), metadata.GetString(metadata.GetTypeReference((TypeReferenceHandle)parent).Name))
                    : (metadata.GetString(metadata.GetTypeDefinition((TypeDefinitionHandle)parent).Namespace), metadata.GetString(metadata.GetTypeDefinition((TypeDefinitionHandle)parent).Name));
                if (ns == "System.Runtime.CompilerServices" && name == "ReferenceAssemblyAttribute")
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static async Task<CliResult> RunProcessAsync(string executable, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeout, string? standardInput = null)
    {
        using Process process = new();
        process.StartInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            UseShellExecute = false
        };
        string fixturesDirectory = Path.Combine(FindRepositoryRoot(), "tests", "fixtures", "ExternalLibrarySourceFixture");
        process.StartInfo.Environment["NUGET_PACKAGES"] = Path.Combine(fixturesDirectory, ".nuget-home", "packages");
        // Test hosts launched through an older target framework can pass their SDK resolver
        // selection to child processes. The fixture also builds net10.0, so let dotnet
        // select the installed SDK independently for each child invocation.
        process.StartInfo.Environment.Remove("DOTNET_MSBUILD_SDK_RESOLVER_SDKS_DIR");
        process.StartInfo.Environment.Remove("DOTNET_MSBUILD_SDK_RESOLVER_SDKS_VER");
        process.StartInfo.Environment.Remove("DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR");
        process.StartInfo.Environment.Remove("MSBuildSDKsPath");
        process.StartInfo.Environment.Remove("MSBUILD_EXE_PATH");
        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        if (!process.Start())
        {
            throw new InvalidOperationException($"Could not start '{executable}'.");
        }

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource timeoutSource = new(timeout);
        try
        {
            if (standardInput is not null)
            {
                await process.StandardInput.WriteAsync(standardInput.AsMemory(), timeoutSource.Token);
                process.StandardInput.Close();
            }
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"Process exceeded {timeout.TotalSeconds:0} seconds: {executable} {string.Join(' ', arguments)}");
        }

        return new CliResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static async Task RequireSuccessAsync(CliResult result, string operation)
    {
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"{operation} failed ({result.ExitCode}). stdout:\n{result.Stdout}\nstderr:\n{result.Stderr}");
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "navlyn.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repository root from the test output directory.");
    }

    internal sealed record CliResult(int ExitCode, string Stdout, string Stderr);
}

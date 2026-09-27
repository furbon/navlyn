using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.Documentation;
using ICSharpCode.Decompiler.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Navlyn.Symbols;

/// <summary>Runs external asset selection and ILSpy in a disposable process with a hard deadline.</summary>
internal static class ExternalMemberWorker
{
    private const string WorkerArgument = "--navlyn-external-member-worker";
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private const int MaxWorkerOutputBytes = 4 * 1024 * 1024;
    private const int MaxWorkerRequestBytes = 64 * 1024;
    private const int MaxAssetsBytes = 16 * 1024 * 1024;
    private const long MaxPeBytes = 64L * 1024 * 1024;
    private const int MaxIlBytes = 1024 * 1024;

    public static async Task<bool> RunIfRequestedAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0 || args[0] != WorkerArgument)
        {
            return false;
        }

        WorkerResponse response;
        try
        {
            byte[] input = await ReadBoundedAsync(Console.OpenStandardInput(), MaxWorkerRequestBytes, cancellationToken);
            WorkerRequest? request = JsonSerializer.Deserialize<WorkerRequest>(input);
            response = request is null ? new WorkerResponse(null, null, null, null, "invalid-request") : Decompile(request);
        }
        catch (LimitExceededException)
        {
            response = new WorkerResponse(null, null, null, null, "limit");
        }
        catch (Exception exception) when (exception is BadImageFormatException or IOException or ArgumentException or JsonException or UnauthorizedAccessException)
        {
            response = new WorkerResponse(null, null, null, null, "malformed-image");
        }

        await JsonSerializer.SerializeAsync(Console.OpenStandardOutput(), response, cancellationToken: cancellationToken);
        return true;
    }

    public static async Task<WorkerResponse> DecompileAsync(
        WorkerRequest request,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        string entryAssembly = Assembly.GetEntryAssembly()?.Location ?? string.Empty;
        if (string.IsNullOrWhiteSpace(entryAssembly))
        {
            return new WorkerResponse(null, null, null, null, "unavailable");
        }

        startInfo.ArgumentList.Add(entryAssembly);
        startInfo.ArgumentList.Add(WorkerArgument);
        using Process process = new() { StartInfo = startInfo, EnableRaisingEvents = true };
        using CancellationTokenSource deadline = new(Deadline);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            if (!process.Start())
            {
                return new WorkerResponse(null, null, null, null, "unavailable");
            }

            Task<string> stdout = process.StandardOutput.ReadToEndAsync(linked.Token);
            _ = process.StandardError.ReadToEndAsync(linked.Token);
            await JsonSerializer.SerializeAsync(process.StandardInput.BaseStream, request, cancellationToken: linked.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(linked.Token);

            string output = await stdout;
            if (Encoding.UTF8.GetByteCount(output) > MaxWorkerOutputBytes)
            {
                return new WorkerResponse(null, null, null, null, "limit");
            }

            WorkerResponse? response = JsonSerializer.Deserialize<WorkerResponse>(output);
            return process.ExitCode == 0 && response is not null
                ? response
                : new WorkerResponse(null, null, null, null, "malformed-image");
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                TryKill(process);
            }

            if (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return new WorkerResponse(null, null, null, null, "limit");
            }

            throw;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or JsonException or IOException)
        {
            if (!process.HasExited)
            {
                TryKill(process);
            }

            return new WorkerResponse(null, null, null, null, "unavailable");
        }
    }

    private static WorkerResponse Decompile(WorkerRequest request)
    {
        if (!string.Equals(HashFile(request.ReferencePath, MaxPeBytes), request.ReferenceSha256, StringComparison.Ordinal) ||
            !string.Equals(HashMetadata(request.ReferencePath), request.LoadedMetadataSha256, StringComparison.Ordinal))
        {
            return new WorkerResponse(null, null, null, null, "stale");
        }

        string? currentAssetsHash = null;
        if (request.AssetsPath is not null)
        {
            FileInfo assetsInfo = new(request.AssetsPath);
            if (!assetsInfo.Exists || assetsInfo.Length > MaxAssetsBytes)
            {
                return new WorkerResponse(null, null, null, null, assetsInfo.Exists ? "limit" : "stale");
            }

            currentAssetsHash = HashFile(request.AssetsPath, MaxAssetsBytes);
            if (!string.Equals(currentAssetsHash, request.AssetsSha256, StringComparison.Ordinal))
            {
                return new WorkerResponse(null, null, null, null, "stale");
            }
        }

        string? implementationPath = ResolveImplementationPath(request, out bool ambiguous);
        if (implementationPath is null)
        {
            return new WorkerResponse(null, null, null, null, ambiguous ? "ambiguous" : "unavailable");
        }

        FileInfo implementationInfo = new(implementationPath);
        if (!implementationInfo.Exists || implementationInfo.Length > MaxPeBytes)
        {
            return new WorkerResponse(null, null, null, null, "limit");
        }

        string implementationHash = HashFile(implementationPath, MaxPeBytes);
        using PEFile peFile = new(implementationPath);
        Guid mvid = peFile.Metadata.GetGuid(peFile.Metadata.GetModuleDefinition().Mvid);
        string identity = AssemblyName.GetAssemblyName(implementationPath).FullName!;
        if (identity != request.Identity || HasReferenceAssemblyAttribute(peFile))
        {
            return new WorkerResponse(null, null, null, null, "unavailable");
        }

        EntityHandle[] matches = peFile.Metadata.MethodDefinitions
            .Where(handle => IdStringProvider.GetIdString(peFile, handle) == request.DocumentationCommentId)
            .Select(handle => (EntityHandle)handle)
            .ToArray();
        if (matches.Length != 1)
        {
            return new WorkerResponse(null, null, implementationPath, implementationHash, matches.Length == 0 ? "member-not-found" : "ambiguous");
        }

        MethodDefinition selectedMethod = peFile.Metadata.GetMethodDefinition((MethodDefinitionHandle)matches[0]);
        int rva = selectedMethod.RelativeVirtualAddress;
        int ilLength = rva == 0 ? 0 : peFile.Reader.GetMethodBody(rva).GetILBytes()?.Length ?? 0;
        if (ilLength > MaxIlBytes)
        {
            return new WorkerResponse(null, null, implementationPath, implementationHash, "limit");
        }

        bool hasBody = rva != 0 && ilLength > 0 && !selectedMethod.Attributes.HasFlag(MethodAttributes.Abstract) &&
            !selectedMethod.Attributes.HasFlag(MethodAttributes.PinvokeImpl);
        if (request.View == "body" && !hasBody)
        {
            return new WorkerResponse(null, null, implementationPath, implementationHash, "no-body");
        }

        CSharpDecompiler decompiler = new(implementationPath, new DecompilerSettings());
        string reconstructed = decompiler.DecompileAsString(matches[0]);
        string? selectedText = SelectView(reconstructed, request.View, hasBody);
        if (selectedText is null)
        {
            return new WorkerResponse(null, null, implementationPath, implementationHash, request.View == "body" ? "no-body" : "malformed-image");
        }

        if (Encoding.UTF8.GetByteCount(selectedText) > MaxWorkerOutputBytes)
        {
            return new WorkerResponse(null, null, implementationPath, implementationHash, "limit");
        }

        if (!string.Equals(HashFile(implementationPath, MaxPeBytes), implementationHash, StringComparison.Ordinal) ||
            !string.Equals(HashFile(request.ReferencePath, MaxPeBytes), request.ReferenceSha256, StringComparison.Ordinal) ||
            !string.Equals(HashMetadata(request.ReferencePath), request.LoadedMetadataSha256, StringComparison.Ordinal) ||
            (request.AssetsPath is not null && !string.Equals(HashFile(request.AssetsPath, MaxAssetsBytes), currentAssetsHash, StringComparison.Ordinal)))
        {
            return new WorkerResponse(null, null, implementationPath, implementationHash, "stale");
        }

        return new WorkerResponse(selectedText, mvid, implementationPath, implementationHash, null);
    }

    private static string? SelectView(string text, string view, bool hasBody)
    {
        CompilationUnitSyntax root = CSharpSyntaxTree.ParseText(text).GetCompilationUnitRoot();
        if (root.Members.Count == 1 &&
            root.Members[0] is GlobalStatementSyntax { Statement: BlockSyntax accessorBlock })
        {
            // ILSpy emits an exact accessor handle as a bare block. Select the
            // outer block, even when it contains a nested local function.
            return view == "body" && hasBody ? accessorBlock.ToFullString().Trim() : null;
        }

        SyntaxNode? member = root.DescendantNodesAndSelf()
            .FirstOrDefault(node => node is BaseMethodDeclarationSyntax or LocalFunctionStatementSyntax);
        if (member is LocalFunctionStatementSyntax localFunction)
        {
            if (view == "body")
            {
                return hasBody ? localFunction.Body?.ToFullString().Trim() ?? localFunction.ExpressionBody?.Expression.ToFullString().Trim() : null;
            }

            return view == "signature"
                ? localFunction.WithBody(null).WithExpressionBody(null).WithSemicolonToken(default).ToFullString().Trim()
                : localFunction.ToFullString().Trim();
        }

        if (member is not BaseMethodDeclarationSyntax method)
        {
            return null;
        }

        if (view == "body")
        {
            if (!hasBody)
            {
                return null;
            }

            return method switch
            {
                MethodDeclarationSyntax { Body: not null } declaration => declaration.Body.ToFullString().Trim(),
                MethodDeclarationSyntax { ExpressionBody: not null } declaration => declaration.ExpressionBody.Expression.ToFullString().Trim(),
                ConstructorDeclarationSyntax { Body: not null } constructor => constructor.Body.ToFullString().Trim(),
                OperatorDeclarationSyntax { Body: not null } op => op.Body.ToFullString().Trim(),
                OperatorDeclarationSyntax { ExpressionBody: not null } op => op.ExpressionBody.Expression.ToFullString().Trim(),
                ConversionOperatorDeclarationSyntax { Body: not null } conversion => conversion.Body.ToFullString().Trim(),
                ConversionOperatorDeclarationSyntax { ExpressionBody: not null } conversion => conversion.ExpressionBody.Expression.ToFullString().Trim(),
                _ => null
            };
        }

        if (view == "signature")
        {
            BaseMethodDeclarationSyntax signature = method switch
            {
                MethodDeclarationSyntax declaration => declaration.WithBody(null).WithExpressionBody(null).WithSemicolonToken(default),
                ConstructorDeclarationSyntax constructor => constructor.WithBody(null).WithExpressionBody(null).WithSemicolonToken(default),
                OperatorDeclarationSyntax op => op.WithBody(null).WithExpressionBody(null).WithSemicolonToken(default),
                ConversionOperatorDeclarationSyntax conversion => conversion.WithBody(null).WithExpressionBody(null).WithSemicolonToken(default),
                _ => method
            };
            return signature.ToFullString().Trim();
        }

        return method.ToFullString().Trim();
    }

    private static string? ResolveImplementationPath(WorkerRequest request, out bool ambiguous)
    {
        ambiguous = false;
        if (request.AssetsPath is null)
        {
            return IsImplementation(request.ReferencePath) ? request.ReferencePath : null;
        }

        using JsonDocument assets = JsonDocument.Parse(ReadBoundedFile(request.AssetsPath, MaxAssetsBytes));
        JsonElement root = assets.RootElement;
        if (!root.GetProperty("targets").TryGetProperty(request.TargetFramework, out JsonElement target))
        {
            return null;
        }

        string[] packageFolders = root.GetProperty("packageFolders").EnumerateObject().Select(property => property.Name).ToArray();
        List<JsonProperty> matchingPackages = [];
        foreach (JsonProperty package in target.EnumerateObject())
        {
            if (!package.Value.TryGetProperty("compile", out JsonElement compile)) continue;
            foreach (JsonProperty asset in compile.EnumerateObject())
            {
                string[] candidates = ResolvePackageAssetCandidates(packageFolders, root, package.Name, asset.Name);
                if (candidates.Any(candidate => PathEquals(candidate, request.ReferencePath)))
                {
                    if (candidates.Length != 1)
                    {
                        ambiguous = true;
                        return null;
                    }

                    matchingPackages.Add(package);
                    break;
                }
            }
        }

        if (matchingPackages.Count != 1)
        {
            if (matchingPackages.Count > 1)
            {
                ambiguous = true;
                return null;
            }

            return IsImplementation(request.ReferencePath) ? request.ReferencePath : null;
        }

        JsonElement selected = matchingPackages[0].Value;
        if (selected.TryGetProperty("runtimeTargets", out JsonElement runtimeTargets) &&
            runtimeTargets.EnumerateObject().Any(asset => asset.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        if (!selected.TryGetProperty("runtime", out JsonElement runtime)) return null;
        List<string> candidatesForIdentity = [];
        foreach (JsonProperty asset in runtime.EnumerateObject())
        {
            string[] candidates = ResolvePackageAssetCandidates(packageFolders, root, matchingPackages[0].Name, asset.Name);
            if (candidates.Length > 1)
            {
                ambiguous = true;
                return null;
            }

            candidatesForIdentity.AddRange(candidates);
        }

        string[] matching = candidatesForIdentity.Where(path =>
        {
            try { return AssemblyName.GetAssemblyName(path).FullName == request.Identity; }
            catch (BadImageFormatException) { return false; }
        }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        ambiguous = matching.Length > 1;
        return matching.Length == 1 ? matching[0] : null;
    }

    private static string[] ResolvePackageAssetCandidates(string[] packageFolders, JsonElement root, string packageKey, string relativeAsset)
    {
        if (!root.GetProperty("libraries").TryGetProperty(packageKey, out JsonElement library) ||
            !library.TryGetProperty("path", out JsonElement packagePath)) return [];
        string relative = Path.Combine(packagePath.GetString()!.Replace('/', Path.DirectorySeparatorChar), relativeAsset.Replace('/', Path.DirectorySeparatorChar));
        return [.. packageFolders.Select(folder => Path.GetFullPath(Path.Combine(folder, relative))).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private static bool IsImplementation(string path)
    {
        using PEFile peFile = new(path);
        return !HasReferenceAssemblyAttribute(peFile);
    }

    private static bool HasReferenceAssemblyAttribute(PEFile peFile) => peFile.Metadata.GetAssemblyDefinition().GetCustomAttributes()
        .Any(handle =>
        {
            CustomAttribute attribute = peFile.Metadata.GetCustomAttribute(handle);
            EntityHandle parent = attribute.Constructor.Kind switch
            {
                HandleKind.MemberReference => peFile.Metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent,
                HandleKind.MethodDefinition => peFile.Metadata.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType(),
                _ => default
            };
            return parent.Kind switch
            {
                HandleKind.TypeReference => IsReferenceAssemblyAttribute(
                    peFile.Metadata.GetString(peFile.Metadata.GetTypeReference((TypeReferenceHandle)parent).Namespace),
                    peFile.Metadata.GetString(peFile.Metadata.GetTypeReference((TypeReferenceHandle)parent).Name)),
                HandleKind.TypeDefinition => IsReferenceAssemblyAttribute(
                    peFile.Metadata.GetString(peFile.Metadata.GetTypeDefinition((TypeDefinitionHandle)parent).Namespace),
                    peFile.Metadata.GetString(peFile.Metadata.GetTypeDefinition((TypeDefinitionHandle)parent).Name)),
                _ => false
            };
        });

    private static bool IsReferenceAssemblyAttribute(string @namespace, string name) =>
        @namespace == "System.Runtime.CompilerServices" && name == "ReferenceAssemblyAttribute";

    private static bool PathEquals(string left, string right) => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static string HashMetadata(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using PEReader pe = new(stream);
        return Convert.ToHexString(SHA256.HashData(pe.GetMetadata().GetContent().AsSpan()));
    }

    private static string HashFile(string path, long maxBytes)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) != 0)
        {
            total += read;
            if (total > maxBytes) throw new LimitExceededException();
            hash.AppendData(buffer, 0, read);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static byte[] ReadBoundedFile(string path, int maxBytes)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using MemoryStream buffer = new();
        byte[] chunk = new byte[81920];
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) != 0)
        {
            if (buffer.Length + read > maxBytes) throw new LimitExceededException();
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, int limit, CancellationToken cancellationToken)
    {
        using MemoryStream buffer = new();
        byte[] chunk = new byte[4096];
        while (true)
        {
            int read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0) return buffer.ToArray();
            if (buffer.Length + read > limit) throw new LimitExceededException();
            buffer.Write(chunk, 0, read);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(2000);
        }
        catch (InvalidOperationException)
        {
        }
    }

    internal sealed record WorkerRequest(string ReferencePath, string ReferenceSha256, string LoadedMetadataSha256,
        string? AssetsPath, string? AssetsSha256, string TargetFramework, string Identity, string DocumentationCommentId, string View);
    internal sealed record WorkerResponse(string? Text, Guid? ImplementationMvid, string? ImplementationPath, string? ImplementationSha256, string? Error);
    private sealed class LimitExceededException : Exception;
}

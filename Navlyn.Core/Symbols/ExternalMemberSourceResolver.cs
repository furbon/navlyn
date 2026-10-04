using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using ICSharpCode.Decompiler.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Navlyn.Diagnostics;
using Navlyn.Workspaces;

namespace Navlyn.Symbols;

internal sealed class ExternalMemberSourceResolver
{
    private const long MaxPeBytes = 64L * 1024 * 1024;
    private const int MaxAssetsBytes = 16 * 1024 * 1024;

    public async Task<ExternalMemberResolutionResult> ResolveAsync(
        Project project,
        ISymbol boundSymbol,
        string view,
        string mode,
        int maxLines,
        int budgetTokens,
        CancellationToken cancellationToken,
        string? externalMember = null)
    {
        if (view is not ("signature" or "declaration" or "body") && !(mode == "decompiled" && view == "members"))
        {
            return ExternalMemberResolutionResult.Failed(DiagnosticIds.InvalidExternalSourceView);
        }

        ISymbol member = Normalize(boundSymbol);
        string? documentationId = member.GetDocumentationCommentId();
        string? anchorId = documentationId;
        if (externalMember is not null) documentationId = externalMember;
        else if (view == "members") documentationId = (member as INamedTypeSymbol ?? member.ContainingType)?.OriginalDefinition.GetDocumentationCommentId();
        string? framework = ProjectContextFacts.GetTargetFramework(project);
        if (string.IsNullOrWhiteSpace(documentationId) || member.ContainingAssembly is null || string.IsNullOrWhiteSpace(framework))
        {
            return ExternalMemberResolutionResult.Failed(DiagnosticIds.ExternalImplementationUnavailable);
        }

        Compilation? compilation = await project.GetCompilationAsync(cancellationToken);
        if (compilation is null)
        {
            return ExternalMemberResolutionResult.Failed(DiagnosticIds.ExternalImplementationUnavailable);
        }

        string identity = member.ContainingAssembly.Identity.ToString();
        PortableExecutableReference[] matchingReferences = compilation.References
            .OfType<PortableExecutableReference>()
            .Where(reference => compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assembly &&
                string.Equals(assembly.Identity.ToString(), identity, StringComparison.Ordinal))
            .ToArray();
        if (matchingReferences.Length != 1 || string.IsNullOrWhiteSpace(matchingReferences[0].FilePath))
        {
            return ExternalMemberResolutionResult.Failed(matchingReferences.Length > 1
                ? DiagnosticIds.ExternalMemberAmbiguous
                : DiagnosticIds.ExternalImplementationUnavailable);
        }

        string referencePath = Path.GetFullPath(matchingReferences[0].FilePath!);
        FileInfo referenceInfo = new(referencePath);
        if (!referenceInfo.Exists || referenceInfo.Length > MaxPeBytes)
        {
            return ExternalMemberResolutionResult.Failed(DiagnosticIds.ExternalMemberLimitExceeded);
        }

        PeIdentity reference;
        string referenceHash;
        string loadedMetadataHash;
        try
        {
            reference = ReadPeIdentity(referencePath);
            referenceHash = await HashAsync(referencePath, MaxPeBytes, cancellationToken);
            loadedMetadataHash = GetLoadedReferenceMetadataHash(matchingReferences[0]);
            string currentMetadataHash = ReadMetadataHash(referencePath);
            if (!string.Equals(loadedMetadataHash, currentMetadataHash, StringComparison.Ordinal) ||
                !string.Equals(reference.Identity, identity, StringComparison.Ordinal))
            {
                return ExternalMemberResolutionResult.Failed(DiagnosticIds.ExternalMemberStale);
            }
        }
        catch (Exception exception) when (exception is BadImageFormatException or IOException or UnauthorizedAccessException)
        {
            return ExternalMemberResolutionResult.Failed(DiagnosticIds.ExternalMemberMalformedImage);
        }

        string? assetsPath = ProjectAssetsLocator.Find(project);
        string? assetsHash = null;
        if (assetsPath is not null && File.Exists(assetsPath))
        {
            FileInfo assetsInfo = new(assetsPath);
            if (assetsInfo.Length > MaxAssetsBytes)
            {
                return ExternalMemberResolutionResult.Failed(DiagnosticIds.ExternalMemberLimitExceeded);
            }

            assetsHash = await HashAsync(assetsPath, MaxAssetsBytes, cancellationToken);
        }

        ExternalMemberSnapshot referenceSnapshot = new(referencePath, referenceHash, reference.Mvid,
            assetsPath is not null && assetsHash is not null ? assetsPath : null, assetsHash, null, null, null, framework, identity);
        if (mode == "metadata")
        {
            return CreateMetadata(member, documentationId, identity, framework, referenceHash, view,
                maxLines, budgetTokens, referenceSnapshot);
        }

        ExternalMemberWorker.WorkerRequest request = new(referencePath, referenceHash, loadedMetadataHash,
            referenceSnapshot.AssetsPath, assetsHash, framework, identity, documentationId, view, maxLines, budgetTokens);
        ExternalMemberWorker.WorkerResponse worker = await ExternalMemberWorker.DecompileAsync(request, cancellationToken);
        int? workerDiagnostic = MapWorkerError(worker.Error, view);
        if (workerDiagnostic is not null)
        {
            return ExternalMemberResolutionResult.Failed(workerDiagnostic.Value);
        }

        if (worker.Text is null || worker.ImplementationPath is null || worker.ImplementationSha256 is null || worker.ImplementationMvid is null)
        {
            return ExternalMemberResolutionResult.Failed(DiagnosticIds.ExternalMemberMalformedImage);
        }

        ExternalMemberSnapshot snapshot = referenceSnapshot with
        {
            ImplementationPath = worker.ImplementationPath,
            ImplementationSha256 = worker.ImplementationSha256,
            ImplementationMvid = worker.ImplementationMvid
        };
        if (!await ValidateSnapshotAsync(snapshot, cancellationToken))
        {
            return ExternalMemberResolutionResult.Failed(DiagnosticIds.ExternalMemberStale);
        }

        string memberHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(documentationId)));
        string virtualPath = $"navlyn-decompiled://{worker.ImplementationSha256.ToLowerInvariant()}/{memberHash.ToLowerInvariant()}";
        IReadOnlyList<string> lines = BoundLines(worker.Text, maxLines, budgetTokens, out bool truncated);
        SymbolSourceSlice slice = new(view, virtualPath, 1, 1, Math.Max(1, lines.Count), 1, lines, truncated || worker.Truncated,
            Origin: "decompiled", Editable: false);
        return ExternalMemberResolutionResult.Succeeded(new ExternalMemberResolution(
            "decompiled", new ExternalAssemblyProvenance(identity, framework, "implementation", referenceHash, worker.ImplementationSha256, documentationId,
                externalMember is not null || view == "members" ? anchorId : null,
                externalMember is not null || view == "members" ? worker.Signature : null, worker.MembersTotal),
            [slice], snapshot));
    }

    public static async Task<bool> ValidateSnapshotAsync(ExternalMemberSnapshot? snapshot, CancellationToken cancellationToken)
    {
        if (snapshot is null)
        {
            return true;
        }

        try
        {
            string? referenceHash = await HashWithinLimitAsync(snapshot.ReferencePath, MaxPeBytes, cancellationToken);
            if (!string.Equals(referenceHash, snapshot.ReferenceSha256, StringComparison.Ordinal)) return false;

            string? assetsHash = snapshot.AssetsPath is null
                ? null
                : await HashWithinLimitAsync(snapshot.AssetsPath, MaxAssetsBytes, cancellationToken);
            if (snapshot.AssetsPath is not null && !string.Equals(assetsHash, snapshot.AssetsSha256, StringComparison.Ordinal)) return false;

            string? implementationHash = snapshot.ImplementationPath is null
                ? null
                : await HashWithinLimitAsync(snapshot.ImplementationPath, MaxPeBytes, cancellationToken);
            return ReadPeIdentity(snapshot.ReferencePath).Mvid == snapshot.ReferenceMvid &&
                (snapshot.AssetsPath is null || assetsHash is not null) &&
                (snapshot.ImplementationPath is null ||
                    (string.Equals(implementationHash, snapshot.ImplementationSha256, StringComparison.Ordinal) &&
                     ReadPeIdentity(snapshot.ImplementationPath).Mvid == snapshot.ImplementationMvid));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or BadImageFormatException)
        {
            return false;
        }
    }

    private static int? MapWorkerError(string? error, string view) => error switch
    {
        null => null,
        "ambiguous" => DiagnosticIds.ExternalMemberAmbiguous,
        "member-not-found" => DiagnosticIds.ExternalMemberBodyUnavailable,
        "no-body" => DiagnosticIds.ExternalMemberBodyUnavailable,
        "stale" => DiagnosticIds.ExternalMemberStale,
        "limit" => DiagnosticIds.ExternalMemberLimitExceeded,
        "unavailable" => DiagnosticIds.ExternalImplementationUnavailable,
        "invalid-view" => DiagnosticIds.InvalidExternalSourceView,
        _ => DiagnosticIds.ExternalMemberMalformedImage
    };

    private static ISymbol Normalize(ISymbol symbol) => symbol switch
    {
        IMethodSymbol { ReducedFrom: not null } method => method.ReducedFrom.OriginalDefinition,
        IMethodSymbol method => method.OriginalDefinition,
        IPropertySymbol property => property.OriginalDefinition,
        IEventSymbol eventSymbol => eventSymbol.OriginalDefinition,
        _ => symbol
    };

    private static ExternalMemberResolutionResult CreateMetadata(
        ISymbol member, string documentationId, string identity, string framework,
        string referenceHash, string view, int maxLines, int budgetTokens, ExternalMemberSnapshot snapshot)
    {
        string declaration = SymbolFactsBuilder.CreateMetadataSignature(member);
        if (view == "body")
        {
            return ExternalMemberResolutionResult.Failed(DiagnosticIds.ExternalMemberBodyUnavailable);
        }

        string text = view == "signature" ? declaration : $"{declaration};";
        string memberHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(documentationId)));
        string uri = $"navlyn-metadata://{referenceHash.ToLowerInvariant()}/{memberHash.ToLowerInvariant()}";
        IReadOnlyList<string> lines = BoundLines(text, maxLines, budgetTokens, out bool truncated);
        return ExternalMemberResolutionResult.Succeeded(new ExternalMemberResolution(
            "metadata", new ExternalAssemblyProvenance(identity, framework, "reference", referenceHash, null, documentationId),
            [new SymbolSourceSlice(view, uri, 1, 1, Math.Max(1, lines.Count), 1, lines, truncated, "metadata", false)], snapshot));
    }

    private static IReadOnlyList<string> BoundLines(string text, int maxLines, int budgetTokens, out bool truncated)
    {
        string[] allLines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        int charLimit = (int)Math.Min(int.MaxValue, (long)Math.Max(1, budgetTokens) * 4);
        List<string> result = [];
        int used = 0;
        truncated = allLines.Length > maxLines;
        foreach (string line in allLines.Take(maxLines))
        {
            if (used + line.Length > charLimit)
            {
                int remaining = Math.Max(0, charLimit - used);
                if (remaining > 0) result.Add(line[..Math.Min(line.Length, remaining)]);
                truncated = true;
                break;
            }

            result.Add(line);
            used += line.Length;
        }

        return result;
    }

    private static string GetLoadedReferenceMetadataHash(PortableExecutableReference reference)
    {
        if (reference.GetMetadata() is not AssemblyMetadata assemblyMetadata)
        {
            throw new BadImageFormatException("Could not inspect the loaded Roslyn assembly metadata.");
        }

        ImmutableArray<ModuleMetadata> modules = assemblyMetadata.GetModules();
        if (modules.Length != 1)
        {
            throw new BadImageFormatException("Multi-module reference assemblies are not supported.");
        }

        MetadataReader reader = modules[0].GetMetadataReader();
        unsafe
        {
            ReadOnlySpan<byte> metadata = new(reader.MetadataPointer, reader.MetadataLength);
            return Convert.ToHexString(SHA256.HashData(metadata));
        }
    }

    private static string ReadMetadataHash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using PEReader pe = new(stream);
        return Convert.ToHexString(SHA256.HashData(pe.GetMetadata().GetContent().AsSpan()));
    }

    private static PeIdentity ReadPeIdentity(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using PEReader pe = new(stream);
        MetadataReader reader = pe.GetMetadataReader();
        string identity = AssemblyName.GetAssemblyName(path).FullName!;
        Guid mvid = reader.GetGuid(reader.GetModuleDefinition().Mvid);
        return new PeIdentity(identity, mvid);
    }

    private static async Task<string> HashAsync(string path, long maxBytes, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) != 0)
        {
            total += read;
            if (total > maxBytes) throw new IOException("The external input exceeded its size limit.");
            hash.AppendData(buffer, 0, read);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static async Task<string?> HashWithinLimitAsync(string path, long maximumBytes, CancellationToken cancellationToken)
    {
        FileInfo file = new(path);
        return !file.Exists || file.Length > maximumBytes ? null : await HashAsync(path, maximumBytes, cancellationToken);
    }

    private sealed record PeIdentity(string Identity, Guid Mvid);
}

internal sealed record ExternalMemberSnapshot(string ReferencePath, string ReferenceSha256, Guid ReferenceMvid, string? AssetsPath, string? AssetsSha256, string? ImplementationPath, string? ImplementationSha256, Guid? ImplementationMvid, string TargetFramework, string Identity);
internal sealed record ExternalAssemblyProvenance(string Identity, string TargetFramework, string SelectedAssembly, string ReferenceSha256, string? ImplementationSha256, string MemberDocumentationCommentId,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? AnchorDocumentationCommentId = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? MemberSignature = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] int? MembersTotal = null);
internal sealed record ExternalMemberResolution(string SourceOrigin, ExternalAssemblyProvenance ExternalAssembly, IReadOnlyList<SymbolSourceSlice> Slices, ExternalMemberSnapshot? Snapshot);
internal sealed record ExternalMemberResolutionResult(ExternalMemberResolution? Resolution, int? DiagnosticId)
{
    public static ExternalMemberResolutionResult Succeeded(ExternalMemberResolution resolution) => new(resolution, null);
    public static ExternalMemberResolutionResult Failed(int diagnosticId) => new(null, diagnosticId);
}

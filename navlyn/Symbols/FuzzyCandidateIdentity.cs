using System.Security.Cryptography;
using System.Text;
using Navlyn.Paths;
using Navlyn.Workspaces;
using Microsoft.CodeAnalysis;

namespace Navlyn.Symbols;

internal static class FuzzyCandidateIdentity
{
    private const string Prefix = "sym:v1:";
    private const int HashHexLength = 32;

    public static FuzzyCandidateSelector CreateSelector(
        FuzzySymbolCandidate candidate,
        Project? project)
    {
        return new FuzzyCandidateSelector(
            Version: 1,
            Kind: candidate.Kind,
            Name: candidate.Name,
            Container: candidate.Container,
            FullyQualifiedName: candidate.FullyQualifiedName,
            DocumentationCommentId: candidate.DocumentationCommentId,
            Project: candidate.Facts.Project,
            TargetFramework: project is null ? null : ProjectContextFacts.GetTargetFramework(project),
            Path: PathDisplay.FromRepositoryRoot(candidate.Path),
            Line: candidate.Line,
            Column: candidate.Column,
            EndLine: candidate.EndLine,
            EndColumn: candidate.EndColumn);
    }

    public static string CreateCandidateId(FuzzyCandidateSelector selector)
    {
        string canonical = string.Join(
            "\n",
            [
                "v1",
                selector.DocumentationCommentId ?? "",
                selector.FullyQualifiedName ?? "",
                selector.Kind,
                selector.Project ?? "",
                selector.TargetFramework ?? "",
                selector.Path,
                selector.Line.ToString(System.Globalization.CultureInfo.InvariantCulture),
                selector.Column.ToString(System.Globalization.CultureInfo.InvariantCulture),
                selector.EndLine.ToString(System.Globalization.CultureInfo.InvariantCulture),
                selector.EndColumn.ToString(System.Globalization.CultureInfo.InvariantCulture)
            ]);

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        string hex = Convert.ToHexString(hash).ToLowerInvariant();
        return Prefix + hex[..HashHexLength];
    }

    public static bool TryParseCandidateId(string candidateId)
    {
        if (!candidateId.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        string hash = candidateId[Prefix.Length..];
        return hash.Length == HashHexLength &&
            hash.All(static ch => ch is >= '0' and <= '9' or >= 'a' and <= 'f');
    }
}

internal sealed record FuzzyCandidateSelector(
    int Version,
    string Kind,
    string Name,
    string? Container,
    string? FullyQualifiedName,
    string? DocumentationCommentId,
    string? Project,
    string? TargetFramework,
    string Path,
    int Line,
    int Column,
    int EndLine,
    int EndColumn);


using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Navlyn.Symbols;

namespace Navlyn.Tests.Symbols;

public sealed class OutlineResolverComponentTests
{
    [Theory]
    [InlineData(LanguageNames.CSharp)]
    [InlineData(LanguageNames.VisualBasic)]
    public async Task Pages_PreserveFullSemanticOrderIdentityAndBounds(string language)
    {
        using AdhocWorkspace workspace = new();
        string extension = language == LanguageNames.CSharp ? ".cs" : ".vb";
        string file = Path.Combine(Path.GetTempPath(), "Outline" + extension);
        Project project = workspace.AddProject("Fixture", language)
            .WithMetadataReferences([MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
        if (language == LanguageNames.CSharp)
            project = project.WithParseOptions(new CSharpParseOptions(LanguageVersion.Preview));
        string code = language == LanguageNames.CSharp
            ? "namespace Fixture { public partial class Alpha { public int Run(int value) => value; public string Run(string value) => value; } public class Beta { public void Stop() {} } }"
            : "Namespace Fixture\nPublic Class Alpha\nPublic Function Run(value As Integer) As Integer\nReturn value\nEnd Function\nPublic Function Run(value As String) As String\nReturn value\nEnd Function\nEnd Class\nPublic Class Beta\nPublic Sub Stop()\nEnd Sub\nEnd Class\nEnd Namespace";
        Document document = project.AddDocument("Outline" + extension, SourceText.From(code), filePath: file);
        OutlineResolver resolver = new();
        OutlineResolutionResult fullResult = await resolver.ResolveAsync(document.Project.Solution,
            new FileInfo(file), document.Project, false, CancellationToken.None);
        Assert.Null(fullResult.Error);
        OutlineResolution full = fullResult.Resolution!;
        Assert.Contains(full.Entries, entry => entry.Name == "Run" && entry.Facts.Signature is string signature &&
            (signature.Contains("int", StringComparison.OrdinalIgnoreCase) || signature.Contains("Integer", StringComparison.OrdinalIgnoreCase)));
        List<OutlineEntry> joined = [];
        for (int offset = 0; offset < full.Entries.Count; offset += 2)
        {
            OutlineResolutionResult pageResult = await resolver.ResolveAsync(document.Project.Solution,
                new FileInfo(file), document.Project, false, CancellationToken.None, new OutlinePage(offset, 2));
            Assert.Null(pageResult.Error);
            OutlineResolution page = pageResult.Resolution!;
            Assert.Equal(full.Entries.Count, page.EntriesTotal);
            joined.AddRange(page.Entries);
        }
        Assert.Equal(full.Entries.Select(entry => (entry.CandidateId, entry.Facts.Signature, entry.Line, entry.Column, entry.EndLine, entry.EndColumn)),
            joined.Select(entry => (entry.CandidateId, entry.Facts.Signature, entry.Line, entry.Column, entry.EndLine, entry.EndColumn)));
        OutlineResolutionResult pastEnd = await resolver.ResolveAsync(document.Project.Solution,
            new FileInfo(file), document.Project, false, CancellationToken.None, new OutlinePage(int.MaxValue, 2));
        Assert.Empty(pastEnd.Resolution!.Entries);
        Assert.Equal(full.Entries.Count, pastEnd.Resolution.EntriesTotal);
    }
}

using Navlyn.RepoGraph;

namespace Navlyn.Tests.RepoGraph;

public sealed class ProjectClassifierTests
{
    [Fact]
    public void Classify_TestSdkProject_ReturnsTest()
    {
        ProjectFileFacts facts = CreateFacts(
            outputType: null,
            packages:
            [
                new ProjectFilePackageReference("Microsoft.NET.Test.Sdk", "17.0.0", IsCentralVersion: false, PrivateAssets: null, IncludeAssets: null, ExcludeAssets: null),
                new ProjectFilePackageReference("xunit", "2.9.3", IsCentralVersion: false, PrivateAssets: null, IncludeAssets: null, ExcludeAssets: null)
            ]);

        RepoGraphProjectClassification classification = ProjectClassifier.Classify("Widget.Tests", "tests/Widget.Tests/Widget.Tests.csproj", "Widget.Tests", facts);

        Assert.Equal("test", classification.Kind);
        Assert.Equal("high", classification.Confidence);
        Assert.Contains("test-sdk-package", classification.ReasonCodes);
        Assert.Contains("xunit-package", classification.ReasonCodes);
    }

    [Fact]
    public void Classify_PackAsToolProject_ReturnsTooling()
    {
        ProjectFileFacts facts = CreateFacts(outputType: "Exe", packAsTool: true);

        RepoGraphProjectClassification classification = ProjectClassifier.Classify("navlyn", "navlyn/navlyn.csproj", "navlyn", facts);

        Assert.Equal("tooling", classification.Kind);
        Assert.Equal("high", classification.Confidence);
        Assert.Contains("pack-as-tool", classification.ReasonCodes);
    }

    private static ProjectFileFacts CreateFacts(
        string? outputType,
        bool packAsTool = false,
        IReadOnlyList<ProjectFilePackageReference>? packages = null)
    {
        return new ProjectFileFacts(
            Sdk: "Microsoft.NET.Sdk",
            OutputType: outputType,
            Nullable: "enable",
            ImplicitUsings: "enable",
            TargetFramework: "net10.0",
            TargetFrameworks: ["net10.0"],
            PackAsTool: packAsTool,
            PackageReferences: packages ?? [],
            ProjectReferences: [],
            Warnings: []);
    }
}


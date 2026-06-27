using Navlyn.GeneratedCode;

namespace Navlyn.Tests.GeneratedCode;

public sealed class GeneratedCodeFactsTests
{
    [Theory]
    [InlineData("TemporaryGeneratedFile_abc.cs")]
    [InlineData("Widget.g.cs")]
    [InlineData("Widget.generated.cs")]
    [InlineData("Widget.designer.cs")]
    [InlineData("Widget.AssemblyInfo.cs")]
    [InlineData("Widget.AssemblyAttributes.cs")]
    [InlineData(@"src\obj\Debug\Generated.cs")]
    [InlineData(@"src/bin/Debug/Generated.cs")]
    public void IsGeneratedPath_RecognizesGeneratedFilesAndBuildOutput(string path)
    {
        Assert.True(GeneratedCodeFacts.IsGeneratedPath(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Widget.cs")]
    [InlineData(@"src\ObjectModel\Widget.cs")]
    public void IsGeneratedPath_IgnoresNormalSourcePaths(string? path)
    {
        Assert.False(GeneratedCodeFacts.IsGeneratedPath(path));
    }
}

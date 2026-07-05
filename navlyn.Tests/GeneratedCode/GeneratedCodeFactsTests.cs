using Navlyn.GeneratedCode;

namespace Navlyn.Tests.GeneratedCode;

public sealed class GeneratedCodeFactsTests
{
    [Theory]
    [InlineData("TemporaryGeneratedFile_abc.cs")]
    [InlineData("Widget.g.cs")]
    [InlineData("Widget.g.vb")]
    [InlineData("Widget.generated.cs")]
    [InlineData("Widget.generated.vb")]
    [InlineData("Widget.designer.cs")]
    [InlineData("Widget.designer.vb")]
    [InlineData("Widget.AssemblyInfo.cs")]
    [InlineData("Widget.AssemblyInfo.vb")]
    [InlineData("Widget.AssemblyAttributes.cs")]
    [InlineData("Widget.AssemblyAttributes.vb")]
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

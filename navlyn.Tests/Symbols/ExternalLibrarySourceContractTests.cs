using System.Diagnostics;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Navlyn.Symbols;
using Navlyn.Tests.TestSupport;
using Navlyn.Workspaces;

namespace Navlyn.Tests.Symbols;

[Collection(ExternalReadCollection.Name)]
public sealed class ExternalLibrarySourceContractTests
{
    [Fact]
    public async Task ArtifactsOutput_UsesActiveAssetsDespiteStaleConventionalAssets()
    {
        ExternalLibrarySourceFixture fixture = await ExternalLibrarySourceFixture.PrepareAsync();
        string stale = Path.Combine(Path.GetDirectoryName(fixture.ArtifactsProject)!, "obj", "project.assets.json");
        Directory.CreateDirectory(Path.GetDirectoryName(stale)!);
        File.WriteAllText(stale, "{\"targets\":{}}");
        try
        {
            (int line, int column) = fixture.Position(fixture.ArtifactsSource, "WriteLine", "Pick");
            ExternalLibrarySourceFixture.CliResult response = await fixture.RunReadAsync(fixture.ArtifactsProject,
                fixture.ArtifactsSource, line, column, "decompiled");
            Assert.True(response.ExitCode == 0, response.Stderr);
            Assert.Contains("FIXTURE_NET10_INT_OVERLOAD_BODY", SliceText(response.Stdout), StringComparison.Ordinal);
            WorkspaceLoadResult loaded = await new WorkspaceLoader().LoadAsync(new FileInfo(fixture.ArtifactsProject), CancellationToken.None);
            using LoadedWorkspace workspace = Assert.IsType<LoadedWorkspace>(loaded.Workspace);
            Project project = Assert.Single(workspace.Solution.Projects);
            string assets = Assert.IsType<string>(ProjectAssetsLocator.Find(project));
            Assert.NotEqual(stale, assets);
            Assert.Contains(Path.Combine("out", "obj", "RedirectedOutput"), assets, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("net10.0", ProjectContextFacts.GetTargetFramework(project));
        }
        finally { File.Delete(stale); }
    }

    [Theory]
    [InlineData("normalizedShort", "Normalize", "short", 37)]
    [InlineData("normalizedWide", "NormalizeWide", "short", 100000)]
    [InlineData("normalizedNegative", "NormalizeNegative", "short", -37)]
    public async Task DecompiledArithmetic_PreservesBranchSignsAndCheckedOverflow(
        string marker, string member, string parameterType, int multiplier)
    {
        ExternalLibrarySourceFixture fixture = await ExternalLibrarySourceFixture.PrepareAsync();
        (int line, int column) = fixture.Position(fixture.ConsumerSource, marker, member);
        ExternalLibrarySourceFixture.CliResult response = await fixture.RunReadAsync(
            fixture.ConsumerProject, fixture.ConsumerSource, line, column, "decompiled",
            projectName: "Consumer(net10.0)");
        Assert.True(response.ExitCode == 0, response.Stderr);
        using JsonDocument result = JsonDocument.Parse(response.Stdout);
        Assert.Contains("System.Int16", result.RootElement.GetProperty("symbol").GetProperty("facts")
            .GetProperty("documentationCommentId").GetString());
        string body = SliceText(response.Stdout);
        Assert.Contains("checked", body, StringComparison.Ordinal);
        // Recompile only this trusted fixture's returned body: checking strings alone misses branch errors.
        CSharpCompilation compilation = CSharpCompilation.Create("Arithmetic_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText($"public static class Arithmetic {{ public static int Evaluate({parameterType} value) {body} }}")],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream output = new();
        var emitted = compilation.Emit(output);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        System.Reflection.MethodInfo method = System.Reflection.Assembly.Load(output.ToArray())
            .GetType("Arithmetic")!.GetMethod("Evaluate")!;
        foreach (short value in new short[] { -1, 0, 1, -17, 17, short.MinValue, short.MaxValue })
        {
            long expected = Math.Abs((long)value) * multiplier + 211;
            if (expected > int.MaxValue || expected < int.MinValue)
                Assert.IsType<OverflowException>(Assert.Throws<System.Reflection.TargetInvocationException>(
                    () => method.Invoke(null, [value])).InnerException);
            else
                Assert.Equal((int)expected, method.Invoke(null, [value]));
        }
    }

    [Theory]
    [InlineData("net10.0-windows7.0", "FIXTURE_WINDOWS_INT_OVERLOAD_BODY")]
    [InlineData("net10.0", "FIXTURE_NET10_INT_OVERLOAD_BODY")]
    public async Task NuGetRead_DecompilesExactTfmOverloadWithProvenance(string targetFramework, string expectedMarker)
    {
        ExternalLibrarySourceFixture fixture = await ExternalLibrarySourceFixture.PrepareAsync();
        ExternalLibrarySourceFixture.CliResult response = await fixture.RunReadAsync(
            fixture.ConsumerProject, fixture.ConsumerSource, 4, 25, "decompiled", projectName: $"Consumer({targetFramework})");

        Assert.True(response.ExitCode == 0, response.Stderr);
        using JsonDocument document = JsonDocument.Parse(response.Stdout);
        JsonElement result = document.RootElement;
        Assert.Equal("decompiled", result.GetProperty("sourceOrigin").GetString());
        JsonElement assembly = result.GetProperty("externalAssembly");
        Assert.Equal(targetFramework, assembly.GetProperty("targetFramework").GetString());
        Assert.Equal("implementation", assembly.GetProperty("selectedAssembly").GetString());
        Assert.Equal("ExternalFixture, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null", assembly.GetProperty("identity").GetString());
        AssertHash(assembly.GetProperty("referenceSha256").GetString());
        AssertHash(assembly.GetProperty("implementationSha256").GetString());
        JsonElement slice = Assert.Single(result.GetProperty("slices").EnumerateArray());
        string text = string.Join("\n", slice.GetProperty("lines").EnumerateArray().Select(line => line.GetString()));
        Assert.Contains(expectedMarker, text, StringComparison.Ordinal);
        Assert.DoesNotContain("FIXTURE_STRING_OVERLOAD_BODY", text, StringComparison.Ordinal);
        Assert.Equal(1, slice.GetProperty("startLine").GetInt32());
        Assert.Equal(1, slice.GetProperty("startColumn").GetInt32());
        Assert.Matches("^navlyn-decompiled://[0-9A-Fa-f]{64}/[0-9A-Fa-f]{64}$", slice.GetProperty("path").GetString()!);
        Assert.Equal("decompiled", slice.GetProperty("origin").GetString());
        Assert.False(slice.GetProperty("editable").GetBoolean());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConstructedTypeToken_ReadsExactConstructorBodyInCSharpAndVisualBasic(bool visualBasic)
    {
        ExternalLibrarySourceFixture fixture = await ExternalLibrarySourceFixture.PrepareAsync();
        string file = visualBasic ? fixture.VisualBasicSource : fixture.ConsumerSource;
        string project = visualBasic ? fixture.VisualBasicProject : fixture.ConsumerProject;
        (int line, int column) = fixture.Position(file, "explicitConstructed", "Probe(7)");
        ExternalLibrarySourceFixture.CliResult response = await fixture.RunReadAsync(project, file, line, column, "decompiled",
            projectName: visualBasic ? null : "Consumer(net10.0)");
        Assert.True(response.ExitCode == 0, response.Stderr);
        using JsonDocument result = JsonDocument.Parse(response.Stdout);
        Assert.True(result.RootElement.GetProperty("symbol").GetProperty("facts").GetProperty("isConstructor").GetBoolean());
        Assert.Equal("M:Navlyn.ExternalFixture.Probe.#ctor(System.Int32)", result.RootElement.GetProperty("externalAssembly")
            .GetProperty("memberDocumentationCommentId").GetString());
        Assert.Contains("FIXTURE_CONSTRUCTOR_BODY", SliceText(response.Stdout), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AmbiguousConstructedTypeToken_DoesNotGuessAnOverload()
    {
        ExternalLibrarySourceFixture fixture = await ExternalLibrarySourceFixture.PrepareAsync();
        (int line, int column) = fixture.Position(fixture.ConsumerSource, "ambiguousConstructed", "Probe((string)");
        WorkspaceLoadResult loaded = await new WorkspaceLoader().LoadAsync(new FileInfo(fixture.ConsumerProject), CancellationToken.None);
        using LoadedWorkspace workspace = Assert.IsType<LoadedWorkspace>(loaded.Workspace);
        Project project = workspace.Solution.Projects.Single(p => ProjectContextFacts.GetTargetFramework(p) == "net10.0");
        Document document = project.Documents.Single(d => string.Equals(d.FilePath, fixture.ConsumerSource, StringComparison.OrdinalIgnoreCase));
        string source = (await document.GetTextAsync()).ToString().Replace("new Probe((string)null!)", "new Probe(null)", StringComparison.Ordinal);
        Solution solution = project.Solution.WithDocumentText(document.Id, Microsoft.CodeAnalysis.Text.SourceText.From(source));
        SymbolSourceResolutionResult result = await new SymbolSourceResolver().ResolveAsync(solution, new FileInfo(fixture.ConsumerSource),
            line, column, solution.GetProject(project.Id), false, new SymbolSourceOptions("body", 50, 4000, "decompiled"), CancellationToken.None);
        Assert.Equal(Navlyn.Diagnostics.DiagnosticIds.ExternalMemberAmbiguous, result.Error?.DiagnosticId);
        Assert.Null(result.Resolution);
    }

    [Fact]
    public async Task ExternalEnumConstant_MetadataIncludesValueAndBodyErrorIdentifiesTheSelection()
    {
        ExternalLibrarySourceFixture fixture = await ExternalLibrarySourceFixture.PrepareAsync();
        (int line, int column) = fixture.Position(fixture.ConsumerSource, "brokerPlatforms", "None");
        ExternalLibrarySourceFixture.CliResult body = await fixture.RunReadAsync(fixture.ConsumerProject, fixture.ConsumerSource,
            line, column, "decompiled", projectName: "Consumer(net10.0)");
        Assert.NotEqual(0, body.ExitCode);
        Assert.Contains("NAVLYN1403", body.Stderr, StringComparison.Ordinal);
        Assert.Contains("Field", body.Stderr, StringComparison.Ordinal);
        Assert.Contains("BrokerPlatforms.None", body.Stderr, StringComparison.Ordinal);
        ExternalLibrarySourceFixture.CliResult metadata = await fixture.RunReadAsync(fixture.ConsumerProject, fixture.ConsumerSource,
            line, column, "metadata", view: "declaration", projectName: "Consumer(net10.0)");
        Assert.True(metadata.ExitCode == 0, metadata.Stderr);
        Assert.Contains("= 0", SliceText(metadata.Stdout), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DecompiledSignature_OmitsExecutableBody()
    {
        ExternalLibrarySourceFixture fixture = await ExternalLibrarySourceFixture.PrepareAsync();
        ExternalLibrarySourceFixture.CliResult response = await fixture.RunReadAsync(
            fixture.ConsumerProject, fixture.ConsumerSource, 4, 25, "decompiled", view: "signature",
            projectName: "Consumer(net10.0)");

        Assert.True(response.ExitCode == 0, response.Stderr);
        string text = SliceText(response.Stdout);
        Assert.Contains("Pick(int value)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("FIXTURE_NET10_INT_OVERLOAD_BODY", text, StringComparison.Ordinal);
        Assert.DoesNotContain('{', text);
    }

    [Fact]
    public async Task DecompiledBody_OmitsMemberSignature()
    {
        ExternalLibrarySourceFixture fixture = await ExternalLibrarySourceFixture.PrepareAsync();
        ExternalLibrarySourceFixture.CliResult response = await fixture.RunReadAsync(
            fixture.ConsumerProject, fixture.ConsumerSource, 4, 25, "decompiled", view: "body",
            projectName: "Consumer(net10.0)");

        Assert.True(response.ExitCode == 0, response.Stderr);
        string text = SliceText(response.Stdout);
        Assert.Contains("FIXTURE_NET10_INT_OVERLOAD_BODY", text, StringComparison.Ordinal);
        Assert.DoesNotContain("public string Pick", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DirectDllRead_UsesImplementationBody()
    {
        ExternalLibrarySourceFixture fixture = await ExternalLibrarySourceFixture.PrepareAsync();
        ExternalLibrarySourceFixture.CliResult response = await fixture.RunReadAsync(
            fixture.DirectProject, fixture.DirectSource, 4, 25, "decompiled");

        Assert.True(response.ExitCode == 0, response.Stderr);
        Assert.Contains("FIXTURE_NET10_INT_OVERLOAD_BODY", SliceText(response.Stdout), StringComparison.Ordinal);
    }

    [Fact]
    public async Task VisualBasicCallSite_ReadsExactExternalMember()
    {
        ExternalLibrarySourceFixture fixture = await ExternalLibrarySourceFixture.PrepareAsync();
        ExternalLibrarySourceFixture.CliResult response = await fixture.RunReadAsync(
            fixture.VisualBasicProject, fixture.VisualBasicSource, 6, 40, "decompiled");

        Assert.True(response.ExitCode == 0, response.Stderr);
        Assert.Contains("FIXTURE_NET10_INT_OVERLOAD_BODY", SliceText(response.Stdout), StringComparison.Ordinal);

        WorkspaceLoadResult loaded = await new WorkspaceLoader().LoadAsync(new FileInfo(fixture.VisualBasicProject), CancellationToken.None);
        using LoadedWorkspace workspace = Assert.IsType<LoadedWorkspace>(loaded.Workspace);
        Project project = Assert.Single(workspace.Solution.Projects);
        Assert.Equal("net10.0", ProjectContextFacts.GetTargetFramework(project));
        Project withoutSymbols = project.WithParseOptions(new Microsoft.CodeAnalysis.VisualBasic.VisualBasicParseOptions());
        Assert.Equal("net10.0", ProjectContextFacts.GetTargetFramework(withoutSymbols));
        Project conflictingOutput = withoutSymbols.Solution.WithProjectOutputFilePath(withoutSymbols.Id, Path.Combine(
            Path.GetDirectoryName(fixture.VisualBasicProject)!, "bin", "Debug", "net9.0", "VisualBasic.dll"))
            .GetProject(withoutSymbols.Id)!;
        Assert.Null(ProjectContextFacts.GetTargetFramework(conflictingOutput));
    }

    [Fact]
    public async Task DefaultExternalRead_RemainsMetadataOnlyAndEmpty()
    {
        ExternalLibrarySourceFixture fixture = await ExternalLibrarySourceFixture.PrepareAsync();
        ExternalLibrarySourceFixture.CliResult response = await fixture.RunReadAsync(
            fixture.ConsumerProject, fixture.ConsumerSource, 4, 25, externalSource: null,
            projectName: "Consumer(net10.0)");

        Assert.True(response.ExitCode == 0, response.Stderr);
        using JsonDocument document = JsonDocument.Parse(response.Stdout);
        Assert.Empty(document.RootElement.GetProperty("slices").EnumerateArray());
        Assert.Contains(document.RootElement.GetProperty("warnings").EnumerateArray(),
            warning => warning.GetString() == "metadata-only-symbol");
        Assert.False(document.RootElement.TryGetProperty("sourceOrigin", out _));
    }

    [Fact]
    public async Task MetadataMode_ReturnsDeclarationWithMetadataProvenance()
    {
        ExternalLibrarySourceFixture fixture = await ExternalLibrarySourceFixture.PrepareAsync();
        ExternalLibrarySourceFixture.CliResult response = await fixture.RunReadAsync(
            fixture.ConsumerProject, fixture.ConsumerSource, 4, 25, "metadata", view: "declaration",
            projectName: "Consumer(net10.0)");

        Assert.True(response.ExitCode == 0, response.Stderr);
        using JsonDocument document = JsonDocument.Parse(response.Stdout);
        JsonElement result = document.RootElement;
        Assert.Equal("metadata", result.GetProperty("sourceOrigin").GetString());
        JsonElement slice = Assert.Single(result.GetProperty("slices").EnumerateArray());
        Assert.Contains("Pick(int", SliceText(response.Stdout), StringComparison.Ordinal);
        Assert.Equal(1, slice.GetProperty("startLine").GetInt32());
        Assert.Equal(1, slice.GetProperty("startColumn").GetInt32());
        Assert.Matches("^navlyn-metadata://[0-9A-Fa-f]{64}/[0-9A-Fa-f]{64}$", slice.GetProperty("path").GetString()!);
        Assert.Equal("metadata", slice.GetProperty("origin").GetString());
        Assert.False(slice.GetProperty("editable").GetBoolean());
    }

    [Fact]
    public async Task ReferenceOnlyFixture_BodyRequestReturnsImplementationUnavailable()
    {
        ExternalLibrarySourceFixture fixture = await ExternalLibrarySourceFixture.PrepareAsync();
        ExternalLibrarySourceFixture.CliResult response = await fixture.RunReadAsync(
            fixture.ReferenceOnlyProject, fixture.ReferenceOnlySource, 4, 25, "decompiled");

        Assert.NotEqual(0, response.ExitCode);
        Assert.Contains("NAVLYN1402", response.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("FIXTURE_", response.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AbstractExternalMember_BodyRequestReturnsBodyUnavailable()
    {
        ExternalLibrarySourceFixture fixture = await ExternalLibrarySourceFixture.PrepareAsync();
        (int line, int column) = fixture.Position(fixture.ConsumerSource, "abstractSelected", "Read");
        ExternalLibrarySourceFixture.CliResult response = await fixture.RunReadAsync(
            fixture.ConsumerProject, fixture.ConsumerSource, line, column, "decompiled",
            projectName: "Consumer(net10.0)");

        Assert.NotEqual(0, response.ExitCode);
        Assert.Contains("NAVLYN1403", response.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("FIXTURE_", response.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AbstractExternalDeclaration_DoesNotRequireExecutableBody()
    {
        ExternalLibrarySourceFixture fixture = await ExternalLibrarySourceFixture.PrepareAsync();
        (int line, int column) = fixture.Position(fixture.ConsumerSource, "abstractSelected", "Read");
        ExternalLibrarySourceFixture.CliResult response = await fixture.RunReadAsync(
            fixture.ConsumerProject, fixture.ConsumerSource, line, column, "decompiled", view: "declaration",
            projectName: "Consumer(net10.0)");

        Assert.True(response.ExitCode == 0, response.Stderr);
        Assert.Contains("Read(int", SliceText(response.Stdout), StringComparison.Ordinal);
        Assert.DoesNotContain("FIXTURE_", response.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenericMember_NormalizesConstructedTypeAndMethodToExactGenericDefinition()
    {
        ExternalLibrarySourceFixture fixture = await ExternalLibrarySourceFixture.PrepareAsync();
        (int line, int column) = fixture.Position(fixture.ConsumerSource, "genericSelected", "Select<");
        ExternalLibrarySourceFixture.CliResult response = await fixture.RunReadAsync(
            fixture.ConsumerProject, fixture.ConsumerSource, line, column, "decompiled",
            projectName: "Consumer(net10.0)");

        Assert.True(response.ExitCode == 0, response.Stderr);
        using JsonDocument document = JsonDocument.Parse(response.Stdout);
        JsonElement result = document.RootElement;
        JsonElement facts = result.GetProperty("symbol").GetProperty("facts");
        Assert.Equal("M:Navlyn.ExternalFixture.GenericProbe{System.Int32}.Select``1(System.Int32)",
            facts.GetProperty("documentationCommentId").GetString());
        Assert.Equal("Navlyn.ExternalFixture.GenericProbe<int>.Select<TValue>(int)",
            facts.GetProperty("constructedFrom").GetString());
        Assert.Equal("long", Assert.Single(facts.GetProperty("typeArguments").EnumerateArray())
            .GetProperty("name").GetString());
        Assert.Contains("FIXTURE_GENERIC_INT_OVERLOAD_BODY", SliceText(response.Stdout), StringComparison.Ordinal);
        Assert.DoesNotContain("FIXTURE_GENERIC_STRING_OVERLOAD_BODY", SliceText(response.Stdout), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReducedExtensionMethod_UsesExactUnderlyingOverload()
    {
        ExternalLibrarySourceFixture fixture = await ExternalLibrarySourceFixture.PrepareAsync();
        (int line, int column) = fixture.Position(fixture.ConsumerSource, "extensionSelected", "Extend");
        ExternalLibrarySourceFixture.CliResult response = await fixture.RunReadAsync(
            fixture.ConsumerProject, fixture.ConsumerSource, line, column, "decompiled",
            projectName: "Consumer(net10.0)");

        Assert.True(response.ExitCode == 0, response.Stderr);
        Assert.Contains("FIXTURE_EXTENSION_INT_BODY", SliceText(response.Stdout), StringComparison.Ordinal);
        Assert.DoesNotContain("FIXTURE_EXTENSION_STRING_BODY", SliceText(response.Stdout), StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(response.Stdout);
        string memberId = document.RootElement.GetProperty("externalAssembly").GetProperty("memberDocumentationCommentId").GetString()!;
        Assert.Equal("M:Navlyn.ExternalFixture.ProbeExtensions.Extend(Navlyn.ExternalFixture.Probe,System.Int32)", memberId);
        string expectedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(memberId))).ToLowerInvariant();
        Assert.EndsWith("/" + expectedHash, Assert.Single(document.RootElement.GetProperty("slices").EnumerateArray()).GetProperty("path").GetString());
    }

    [Fact]
    public async Task RefOutMethod_UsesExactOverload()
    {
        ExternalLibrarySourceFixture fixture = await ExternalLibrarySourceFixture.PrepareAsync();
        (int line, int column) = fixture.Position(fixture.ConsumerSource, "byRefSelected", "Adjust");
        ExternalLibrarySourceFixture.CliResult response = await fixture.RunReadAsync(
            fixture.ConsumerProject, fixture.ConsumerSource, line, column, "decompiled",
            projectName: "Consumer(net10.0)");

        Assert.True(response.ExitCode == 0, response.Stderr);
        Assert.Contains("FIXTURE_BYREF_BODY", SliceText(response.Stdout), StringComparison.Ordinal);
        Assert.DoesNotContain("FIXTURE_BYREF_STRING_OVERLOAD_BODY", SliceText(response.Stdout), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OptionalParameter_UsesBoundIntOverload()
    {
        ExternalLibrarySourceFixture fixture = await ExternalLibrarySourceFixture.PrepareAsync();
        (int line, int column) = fixture.Position(fixture.ConsumerSource, "optionalSelected", "Optional");
        ExternalLibrarySourceFixture.CliResult response = await fixture.RunReadAsync(
            fixture.ConsumerProject, fixture.ConsumerSource, line, column, "decompiled",
            projectName: "Consumer(net10.0)");

        Assert.True(response.ExitCode == 0, response.Stderr);
        Assert.Contains("FIXTURE_OPTIONAL_INT_BODY", SliceText(response.Stdout), StringComparison.Ordinal);
        Assert.DoesNotContain("FIXTURE_OPTIONAL_STRING_BODY", SliceText(response.Stdout), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Constructor_UsesSelectedOverloadBody()
    {
        ExternalLibrarySourceFixture fixture = await ExternalLibrarySourceFixture.PrepareAsync();
        (int line, int column) = fixture.Position(fixture.ConsumerSource, "constructed =", "new");
        ExternalLibrarySourceFixture.CliResult response = await fixture.RunReadAsync(
            fixture.ConsumerProject, fixture.ConsumerSource, line, column, "decompiled",
            projectName: "Consumer(net10.0)");

        Assert.True(response.ExitCode == 0, response.Stderr);
        Assert.Contains("FIXTURE_CONSTRUCTOR_BODY", SliceText(response.Stdout), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PropertyGetter_ReturnsExactAccessorBody()
    {
        ExternalLibrarySourceFixture fixture = await ExternalLibrarySourceFixture.PrepareAsync();
        (int line, int column) = fixture.Position(fixture.ConsumerSource, "accessorSelected", "Accessed");
        ExternalLibrarySourceFixture.CliResult response = await fixture.RunReadAsync(
            fixture.ConsumerProject, fixture.ConsumerSource, line, column, "decompiled",
            projectName: "Consumer(net10.0)");

        Assert.True(response.ExitCode == 0, response.Stderr);
        Assert.Contains("FIXTURE_PROPERTY_GETTER_BODY", SliceText(response.Stdout), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MutablePropertyRead_UsesGetterBody()
    {
        ExternalLibrarySourceFixture fixture = await ExternalLibrarySourceFixture.PrepareAsync();
        (int line, int column) = fixture.Position(fixture.ConsumerSource, "mutableRead", "Mutable");
        ExternalLibrarySourceFixture.CliResult response = await fixture.RunReadAsync(
            fixture.ConsumerProject, fixture.ConsumerSource, line, column, "decompiled",
            projectName: "Consumer(net10.0)");

        Assert.True(response.ExitCode == 0, response.Stderr);
        Assert.Contains("FIXTURE_MUTABLE_GETTER_BODY", SliceText(response.Stdout), StringComparison.Ordinal);
        Assert.DoesNotContain("FIXTURE_MUTABLE_SETTER_BODY", SliceText(response.Stdout), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MutablePropertyWrite_UsesSetterBody()
    {
        ExternalLibrarySourceFixture fixture = await ExternalLibrarySourceFixture.PrepareAsync();
        (int line, int column) = fixture.Position(fixture.ConsumerSource, "probe.Mutable =", "Mutable");
        ExternalLibrarySourceFixture.CliResult response = await fixture.RunReadAsync(
            fixture.ConsumerProject, fixture.ConsumerSource, line, column, "decompiled",
            projectName: "Consumer(net10.0)");

        Assert.True(response.ExitCode == 0, response.Stderr);
        Assert.Contains("FIXTURE_MUTABLE_SETTER_BODY", SliceText(response.Stdout), StringComparison.Ordinal);
        Assert.DoesNotContain("FIXTURE_MUTABLE_GETTER_BODY", SliceText(response.Stdout), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MutablePropertyCompoundWrite_FailsClosed()
    {
        ExternalLibrarySourceFixture fixture = await ExternalLibrarySourceFixture.PrepareAsync();
        (int line, int column) = fixture.Position(fixture.ConsumerSource, "probe.Mutable +=", "Mutable");
        ExternalLibrarySourceFixture.CliResult response = await fixture.RunReadAsync(
            fixture.ConsumerProject, fixture.ConsumerSource, line, column, "decompiled",
            projectName: "Consumer(net10.0)");

        Assert.NotEqual(0, response.ExitCode);
        Assert.Contains("NAVLYN1404", response.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("FIXTURE_MUTABLE_GETTER_BODY", response.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("FIXTURE_MUTABLE_SETTER_BODY", response.Stdout, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("(probe.Mutable) =", "Mutable")]
    [InlineData("((probe.Mutable)) =", "Mutable")]
    [InlineData("maybeProbe?.Mutable =", "Mutable")]
    public async Task WrappedOrConditionalPropertyWrite_UsesSetterBody(string anchor, string member)
    {
        ExternalLibrarySourceFixture fixture = await ExternalLibrarySourceFixture.PrepareAsync();
        (int line, int column) = fixture.Position(fixture.ConsumerSource, anchor, member);
        ExternalLibrarySourceFixture.CliResult response = await fixture.RunReadAsync(
            fixture.ConsumerProject, fixture.ConsumerSource, line, column, "decompiled",
            projectName: "Consumer(net10.0)");

        Assert.True(response.ExitCode == 0, response.Stderr);
        Assert.Contains("FIXTURE_MUTABLE_SETTER_BODY", SliceText(response.Stdout), StringComparison.Ordinal);
        Assert.DoesNotContain("FIXTURE_MUTABLE_GETTER_BODY", SliceText(response.Stdout), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("(probe.Mutable) +=", "Mutable")]
    [InlineData("maybeProbe?.Mutable +=", "Mutable")]
    [InlineData("(probe.Counter)++", "Counter")]
    [InlineData("((probe.Counter))--", "Counter")]
    public async Task WrappedCompoundOrIncrementPropertyWrite_FailsClosed(string anchor, string member)
    {
        ExternalLibrarySourceFixture fixture = await ExternalLibrarySourceFixture.PrepareAsync();
        (int line, int column) = fixture.Position(fixture.ConsumerSource, anchor, member);
        ExternalLibrarySourceFixture.CliResult response = await fixture.RunReadAsync(
            fixture.ConsumerProject, fixture.ConsumerSource, line, column, "decompiled",
            projectName: "Consumer(net10.0)");

        Assert.NotEqual(0, response.ExitCode);
        Assert.Contains("NAVLYN1404", response.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("FIXTURE_MUTABLE_GETTER_BODY", response.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("FIXTURE_COUNTER_GETTER_BODY", response.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetterWithNestedLocalFunction_UsesOuterAccessorBody()
    {
        ExternalLibrarySourceFixture fixture = await ExternalLibrarySourceFixture.PrepareAsync();
        (int line, int column) = fixture.Position(fixture.ConsumerSource, "nestedAccessorRead", "NestedAccessor");
        ExternalLibrarySourceFixture.CliResult response = await fixture.RunReadAsync(
            fixture.ConsumerProject, fixture.ConsumerSource, line, column, "decompiled",
            projectName: "Consumer(net10.0)");

        Assert.True(response.ExitCode == 0, response.Stderr);
        Assert.Contains("FIXTURE_NESTED_ACCESSOR_OUTER_BODY", SliceText(response.Stdout), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("declaration")]
    public async Task AccessorNonBodyView_UsesBoundMetadataWithoutClaimingDecompilation(string view)
    {
        ExternalLibrarySourceFixture fixture = await ExternalLibrarySourceFixture.PrepareAsync();
        (int line, int column) = fixture.Position(fixture.ConsumerSource, "accessorSelected", "Accessed");
        ExternalLibrarySourceFixture.CliResult response = await fixture.RunReadAsync(
            fixture.ConsumerProject, fixture.ConsumerSource, line, column, "decompiled", view: view,
            projectName: "Consumer(net10.0)");

        Assert.True(response.ExitCode == 0, response.Stderr);
        using JsonDocument document = JsonDocument.Parse(response.Stdout);
        Assert.Equal("metadata", document.RootElement.GetProperty("sourceOrigin").GetString());
        Assert.Contains("Accessed", SliceText(response.Stdout), StringComparison.Ordinal);
        Assert.DoesNotContain("FIXTURE_PROPERTY_GETTER_BODY", SliceText(response.Stdout), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FrameworkReference_MetadataSucceedsAndBodyFailsClosed()
    {
        ExternalLibrarySourceFixture fixture = await ExternalLibrarySourceFixture.PrepareAsync();
        (int line, int column) = fixture.Position(fixture.ConsumerSource, "frameworkSelected", "Trim");
        ExternalLibrarySourceFixture.CliResult metadata = await fixture.RunReadAsync(
            fixture.ConsumerProject, fixture.ConsumerSource, line, column, "metadata", view: "declaration",
            projectName: "Consumer(net10.0)");
        Assert.True(metadata.ExitCode == 0, metadata.Stderr);
        using (JsonDocument document = JsonDocument.Parse(metadata.Stdout))
        {
            JsonElement result = document.RootElement;
            Assert.Equal("metadata", result.GetProperty("sourceOrigin").GetString());
            Assert.Equal("reference", result.GetProperty("externalAssembly").GetProperty("selectedAssembly").GetString());
            Assert.Contains("Trim", SliceText(metadata.Stdout), StringComparison.Ordinal);
        }

        ExternalLibrarySourceFixture.CliResult body = await fixture.RunReadAsync(
            fixture.ConsumerProject, fixture.ConsumerSource, line, column, "decompiled",
            projectName: "Consumer(net10.0)");
        Assert.NotEqual(0, body.ExitCode);
        Assert.Contains("NAVLYN1402", body.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("Trim(", body.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReferenceOnlyPackage_MetadataDeclarationSucceeds()
    {
        ExternalLibrarySourceFixture fixture = await ExternalLibrarySourceFixture.PrepareAsync();
        ExternalLibrarySourceFixture.CliResult response = await fixture.RunReadAsync(
            fixture.ReferenceOnlyProject, fixture.ReferenceOnlySource, 4, 25, "metadata", view: "declaration");

        Assert.True(response.ExitCode == 0, response.Stderr);
        using JsonDocument document = JsonDocument.Parse(response.Stdout);
        Assert.Equal("metadata", document.RootElement.GetProperty("sourceOrigin").GetString());
        Assert.Equal("reference", document.RootElement.GetProperty("externalAssembly").GetProperty("selectedAssembly").GetString());
        Assert.Contains("Pick(int", SliceText(response.Stdout), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnsupportedExternalView_ReturnsInvalidExternalSourceView()
    {
        ExternalLibrarySourceFixture fixture = await ExternalLibrarySourceFixture.PrepareAsync();
        ExternalLibrarySourceFixture.CliResult response = await fixture.RunReadAsync(
            fixture.ConsumerProject, fixture.ConsumerSource, 4, 25, "decompiled", view: "members",
            projectName: "Consumer(net10.0)");

        Assert.NotEqual(0, response.ExitCode);
        Assert.Contains("NAVLYN1401", response.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VirtualExternalSlicePath_IsRejectedAsSourceInput()
    {
        ExternalLibrarySourceFixture fixture = await ExternalLibrarySourceFixture.PrepareAsync();
        ExternalLibrarySourceFixture.CliResult response = await fixture.RunReadAsync(
            fixture.ConsumerProject,
            "navlyn-decompiled://aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            4, 25, externalSource: null);

        Assert.NotEqual(0, response.ExitCode);
        Assert.Contains("NAVLYN1302", response.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("FIXTURE_", response.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkspaceSourceTakesPriorityOverExternalDecompilation()
    {
        ExternalLibrarySourceFixture fixture = await ExternalLibrarySourceFixture.PrepareAsync();
        (int line, int column) = fixture.Position(fixture.ConsumerSource, "localSelected", "Pick");
        ExternalLibrarySourceFixture.CliResult response = await fixture.RunReadAsync(
            fixture.ConsumerProject, fixture.ConsumerSource, line, column, "decompiled",
            projectName: "Consumer(net10.0)");

        Assert.True(response.ExitCode == 0, response.Stderr);
        using JsonDocument document = JsonDocument.Parse(response.Stdout);
        Assert.False(document.RootElement.TryGetProperty("sourceOrigin", out _));
        string text = SliceText(response.Stdout);
        Assert.Contains("LOCAL_SOURCE_PRIORITY_BODY", text, StringComparison.Ordinal);
        Assert.DoesNotContain("navlyn-decompiled://", response.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExternalBody_RespectsMaxLinesAndTokenBudget()
    {
        ExternalLibrarySourceFixture fixture = await ExternalLibrarySourceFixture.PrepareAsync();
        ExternalLibrarySourceFixture.CliResult lineBounded = await fixture.RunReadAsync(
            fixture.ConsumerProject, fixture.ConsumerSource, 4, 25, "decompiled",
            projectName: "Consumer(net10.0)", maxLines: 1);
        Assert.True(lineBounded.ExitCode == 0, lineBounded.Stderr);
        using (JsonDocument document = JsonDocument.Parse(lineBounded.Stdout))
        {
            JsonElement result = document.RootElement;
            JsonElement slice = Assert.Single(result.GetProperty("slices").EnumerateArray());
            Assert.True(result.GetProperty("truncated").GetBoolean());
            Assert.True(slice.GetProperty("truncated").GetBoolean());
            Assert.Single(slice.GetProperty("lines").EnumerateArray());
        }

        ExternalLibrarySourceFixture.CliResult tokenBounded = await fixture.RunReadAsync(
            fixture.ConsumerProject, fixture.ConsumerSource, 4, 25, "decompiled",
            projectName: "Consumer(net10.0)", budgetTokens: 1);
        Assert.True(tokenBounded.ExitCode == 0, tokenBounded.Stderr);
        using JsonDocument budgetDocument = JsonDocument.Parse(tokenBounded.Stdout);
        JsonElement budgetSlice = Assert.Single(budgetDocument.RootElement.GetProperty("slices").EnumerateArray());
        string budgetText = string.Join("", budgetSlice.GetProperty("lines").EnumerateArray().Select(value => value.GetString()));
        Assert.True(budgetSlice.GetProperty("truncated").GetBoolean());
        Assert.True(budgetText.Length <= 4);

        ExternalLibrarySourceFixture.CliResult largeBudget = await fixture.RunReadAsync(
            fixture.ConsumerProject, fixture.ConsumerSource, 4, 25, "decompiled",
            projectName: "Consumer(net10.0)", budgetTokens: int.MaxValue);
        Assert.True(largeBudget.ExitCode == 0, largeBudget.Stderr);
        Assert.Contains("FIXTURE_NET10_INT_OVERLOAD_BODY", SliceText(largeBudget.Stdout), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("read")]
    [InlineData("symbol-source")]
    public async Task Cli_Help_ExposesExplicitExternalSourceOptIn(string command)
    {
        ExternalLibrarySourceFixture fixture = await ExternalLibrarySourceFixture.PrepareAsync();
        using Process process = StartCli(fixture, [command, "--help"]);
        string stdout = await process.StandardOutput.ReadToEndAsync();
        string stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.Equal(0, process.ExitCode);
        Assert.Contains("--external-source", stdout + stderr, StringComparison.Ordinal);
        Assert.Contains("none", stdout + stderr, StringComparison.Ordinal);
        Assert.Contains("decompiled", stdout + stderr, StringComparison.Ordinal);
    }

    private static string SliceText(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement slices = document.RootElement.GetProperty("slices");
        return string.Join("\n", slices.EnumerateArray().SelectMany(slice =>
            slice.GetProperty("lines").EnumerateArray()).Select(line => line.GetString()));
    }

    private static void AssertHash(string? hash)
    {
        Assert.NotNull(hash);
        Assert.Matches("^[0-9A-Fa-f]{64}$", hash);
    }

    private static Process StartCli(ExternalLibrarySourceFixture fixture, IReadOnlyList<string> arguments)
    {
        Process process = new();
        process.StartInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(fixture.CliAssembly)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        process.StartInfo.ArgumentList.Add(fixture.CliAssembly);
        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        Assert.True(process.Start());
        return process;
    }
}

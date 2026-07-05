using Navlyn.Symbols;
using Navlyn.Tests.TestSupport;

namespace Navlyn.Tests.Symbols;

[Collection(ResolverComponentTestCollection.Name)]
public sealed class RelationshipResolverComponentTests(ResolverComponentTestFixture fixture)
{
    [Fact]
    public async Task ImplementationsResolver_InterfaceMember_ReturnsConcreteImplementation()
    {
        SourcePosition query = fixture.SymbolNavigationSource.Position(
            "string FormatWidget(Widget widget);",
            "FormatWidget");

        ImplementationsResolutionResult result = await new ImplementationsResolver().ResolveAsync(
            fixture.SymbolNavigationWorkspace.Solution,
            fixture.SymbolNavigationSource.File,
            query.Line,
            query.Column,
            project: null,
            excludeGenerated: true,
            CancellationToken.None);

        ImplementationsResolution resolution = ResolverAssert.NoError(result.Resolution, result.Error);
        ImplementationLocation implementation = Assert.Single(resolution.Implementations);
        Assert.Equal("FormatWidget", implementation.Name);
        Assert.Equal("Method", implementation.Kind);
        Assert.Equal("SymbolNavigationFixture.DefaultWidgetFormatter", implementation.Container);
        Assert.Equal("SymbolNavigationFixture.DefaultWidgetFormatter.FormatWidget(SymbolNavigationFixture.Widget)", implementation.Facts.DisplayName);
    }

    [Fact]
    public async Task TypeHierarchyResolver_Interface_ReturnsImplementingType()
    {
        SourcePosition query = fixture.SymbolNavigationSource.Position(
            "public interface IWidgetFormatter",
            "IWidgetFormatter");

        TypeHierarchyResolutionResult result = await new TypeHierarchyResolver().ResolveAsync(
            fixture.SymbolNavigationWorkspace.Solution,
            fixture.SymbolNavigationSource.File,
            query.Line,
            query.Column,
            project: null,
            excludeGenerated: true,
            CancellationToken.None);

        TypeHierarchyResolution resolution = ResolverAssert.NoError(result.Resolution, result.Error);
        HierarchySymbol implementation = Assert.Single(resolution.ImplementingTypes);
        Assert.Equal("DefaultWidgetFormatter", implementation.Name);
        Assert.Equal("NamedType", implementation.Kind);
        Assert.Equal("SymbolNavigationFixture", implementation.Container);
        ResolverAssert.PathEndsWith(implementation.Path, fixture.SymbolNavigationSource);
    }

    [Fact]
    public async Task CallHierarchyResolver_Callers_IncludesInterfaceDispatchCaller()
    {
        SourcePosition query = fixture.SymbolNavigationSource.Position(
            "string FormatWidget(Widget widget);",
            "FormatWidget");
        SourcePosition expectedCall = fixture.SymbolNavigationSource.Position(
            "string viaInterface = formatter.FormatWidget(Widget.CreateDefault());",
            "FormatWidget");

        CallersResolutionResult result = await new CallHierarchyResolver().ResolveCallersAsync(
            fixture.SymbolNavigationWorkspace.Solution,
            fixture.SymbolNavigationSource.File,
            query.Line,
            query.Column,
            project: null,
            excludeGenerated: true,
            CancellationToken.None);

        CallersResolution resolution = ResolverAssert.NoError(result.Resolution, result.Error);
        CallHierarchyGroup caller = Assert.Single(resolution.Callers);
        Assert.Equal("Exercise", caller.Symbol.Name);
        Assert.Equal("Method", caller.Symbol.Kind);
        Assert.Equal("SymbolNavigationFixture.SemanticEdgeCases", caller.Symbol.Container);

        CallHierarchyLocation location = Assert.Single(caller.Locations);
        ResolverAssert.Location(expectedCall, location.Line, location.Column, location.EndLine, location.EndColumn);
    }

    [Fact]
    public async Task CallHierarchyResolver_Callers_ReportsScopedSearchMetadata()
    {
        SourcePosition query = fixture.SymbolNavigationSource.Position(
            "string FormatWidget(Widget widget);",
            "FormatWidget");

        CallersResolutionResult result = await new CallHierarchyResolver().ResolveCallersAsync(
            fixture.SymbolNavigationWorkspace.Solution,
            fixture.SymbolNavigationSource.File,
            query.Line,
            query.Column,
            project: null,
            excludeGenerated: true,
            searchOptions: new SymbolNavigationSearchOptions(SymbolNavigationSearchScopes.File, 10),
            cancellationToken: CancellationToken.None);

        CallersResolution resolution = ResolverAssert.NoError(result.Resolution, result.Error);
        Assert.Equal(SymbolNavigationSearchScopes.File, resolution.Search.Scope);
        Assert.Equal("moderate", resolution.Search.CostClass);
        Assert.False(resolution.Search.Partial);
        Assert.Equal(1, resolution.Search.SearchedDocumentCount);
        Assert.Equal(1, resolution.Search.SearchedProjectCount);
    }

    [Fact]
    public async Task CallHierarchyResolver_Calls_NormalizesPropertyAndEventAccesses()
    {
        SourcePosition query = fixture.SymbolNavigationSource.Position(
            "public void IncrementCounter()",
            "IncrementCounter");

        CallsResolutionResult result = await new CallHierarchyResolver().ResolveCallsAsync(
            fixture.SymbolNavigationWorkspace.Solution,
            fixture.SymbolNavigationSource.File,
            query.Line,
            query.Column,
            project: null,
            excludeGenerated: true,
            includeMetadata: false,
            CancellationToken.None);

        CallsResolution resolution = ResolverAssert.NoError(result.Resolution, result.Error);
        Assert.Contains(
            resolution.Calls,
            group => group.Symbol.Name == "Counter" && group.Symbol.Kind == "Property");
        Assert.Contains(
            resolution.Calls,
            group => group.Symbol.Name == "Changed" && group.Symbol.Kind == "Event");
    }

    [Fact]
    public async Task CallHierarchyResolver_Calls_IncludesDelegateLocalInvocation()
    {
        SourcePosition query = fixture.SymbolNavigationSource.Position(
            "public string Exercise()",
            "Exercise");
        SourcePosition expectedCall = fixture.SymbolNavigationSource.Position(
            "string normalized = normalize(\" value \");",
            "normalize(\" value \")");

        CallsResolutionResult result = await new CallHierarchyResolver().ResolveCallsAsync(
            fixture.SymbolNavigationWorkspace.Solution,
            fixture.SymbolNavigationSource.File,
            query.Line,
            query.Column,
            project: null,
            excludeGenerated: true,
            includeMetadata: false,
            CancellationToken.None);

        CallsResolution resolution = ResolverAssert.NoError(result.Resolution, result.Error);
        CallHierarchyGroup group = Assert.Single(
            resolution.Calls,
            group => group.Symbol.Name == "normalize" &&
                group.Symbol.Kind == "Local" &&
                group.Symbol.Container == "SymbolNavigationFixture.SemanticEdgeCases.Exercise()");

        CallHierarchyLocation location = Assert.Single(group.Locations);
        ResolverAssert.Location(expectedCall, location.Line, location.Column, location.EndLine, location.EndColumn);
    }

    [Fact]
    public async Task ReferencesResolver_MethodInvocation_ReturnsInvokeUsageKind()
    {
        SourcePosition query = fixture.SymbolNavigationSource.Position(
            "public string Format(int count)",
            "Format");

        ReferencesResolutionResult result = await new ReferencesResolver().ResolveAsync(
            fixture.SymbolNavigationWorkspace.Solution,
            fixture.SymbolNavigationSource.File,
            query.Line,
            query.Column,
            project: null,
            excludeGenerated: true,
            CancellationToken.None);

        ReferencesResolution resolution = ResolverAssert.NoError(result.Resolution, result.Error);
        Assert.Contains(resolution.References, reference => reference.UsageKind == "invoke");
    }

    [Fact]
    public async Task ReferencesResolver_DocumentBudget_ReturnsPartialSearchMetadata()
    {
        SourcePosition query = fixture.SymbolNavigationSource.Position(
            "namespace SymbolNavigationFixture;",
            "SymbolNavigationFixture");

        ReferencesResolutionResult result = await new ReferencesResolver().ResolveAsync(
            fixture.SymbolNavigationWorkspace.Solution,
            fixture.SymbolNavigationSource.File,
            query.Line,
            query.Column,
            project: null,
            excludeGenerated: false,
            searchOptions: new SymbolNavigationSearchOptions(SymbolNavigationSearchScopes.Solution, 1),
            cancellationToken: CancellationToken.None);

        ReferencesResolution resolution = ResolverAssert.NoError(result.Resolution, result.Error);
        Assert.Equal(SymbolNavigationSearchScopes.Solution, resolution.Search.Scope);
        Assert.Equal("expensive", resolution.Search.CostClass);
        Assert.True(resolution.Search.LexicalPrefilterApplied);
        Assert.True(resolution.Search.Partial);
        Assert.Equal(1, resolution.Search.SearchedDocumentCount);
        Assert.True(resolution.Search.PrefilteredDocumentCount > resolution.Search.SearchedDocumentCount);
        Assert.Equal("document-budget", resolution.Search.TruncationReason);
        Assert.Contains(resolution.Search.RerunHints, hint => hint.Contains("--max-documents", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReferencesResolver_PropertyAssignment_ReturnsReadAndWriteUsageKinds()
    {
        SourcePosition query = fixture.SymbolNavigationSource.Position(
            "public int Counter { get; private set; }",
            "Counter");

        ReferencesResolutionResult result = await new ReferencesResolver().ResolveAsync(
            fixture.SymbolNavigationWorkspace.Solution,
            fixture.SymbolNavigationSource.File,
            query.Line,
            query.Column,
            project: null,
            excludeGenerated: true,
            CancellationToken.None);

        ReferencesResolution resolution = ResolverAssert.NoError(result.Resolution, result.Error);
        Assert.Contains(resolution.References, reference => reference.UsageKind == "write");
        Assert.Contains(resolution.References, reference => reference.UsageKind == "read");
    }

    [Fact]
    public async Task ReferencesResolver_ConstructorUsage_ReturnsConstructUsageKind()
    {
        SourcePosition query = fixture.SymbolNavigationSource.Position(
            "public Widget(string name)",
            "Widget");

        ReferencesResolutionResult result = await new ReferencesResolver().ResolveAsync(
            fixture.SymbolNavigationWorkspace.Solution,
            fixture.SymbolNavigationSource.File,
            query.Line,
            query.Column,
            project: null,
            excludeGenerated: true,
            CancellationToken.None);

        ReferencesResolution resolution = ResolverAssert.NoError(result.Resolution, result.Error);
        Assert.Contains(resolution.References, reference => reference.UsageKind == "construct");
    }
}

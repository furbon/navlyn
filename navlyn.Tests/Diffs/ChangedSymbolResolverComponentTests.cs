using Navlyn.Diffs;
using Navlyn.Tests.TestSupport;

namespace Navlyn.Tests.Diffs;

[Collection(ResolverComponentTestCollection.Name)]
public sealed class ChangedSymbolResolverComponentTests(ResolverComponentTestFixture fixture)
{
    [Fact]
    public async Task ResolveAsync_MethodBodyChange_ReturnsContainingMethod()
    {
        int line = fixture.SymbolNavigationSource.Line("return $\"{Name}:{count}\";");

        ChangedSymbolsResolution result = await ResolveAsync(new DiffHunk(line, 1, line, 1));

        DiffChangedSymbol symbol = Assert.Single(result.ChangedSymbols.Symbols);
        Assert.Equal("Format", symbol.Name);
        Assert.Equal("Method", symbol.Kind);
        Assert.Equal("body", Assert.Single(symbol.ChangeKinds));
        Assert.Equal(line, Assert.Single(symbol.ChangedLines).Line);
    }

    [Fact]
    public async Task ResolveAsync_TypeBodyChange_ReturnsContainingType()
    {
        int line = fixture.SymbolNavigationSource.Line("public string Name { get; }");

        ChangedSymbolsResolution result = await ResolveAsync(new DiffHunk(line, 1, line, 1));

        Assert.Contains(
            result.ChangedSymbols.Symbols,
            symbol => symbol.Name == "Name" && symbol.Kind == "Property");
    }

    [Fact]
    public async Task ResolveAsync_DeletedOnlyHunk_ReturnsUnresolvedChange()
    {
        ChangedSymbolsResolution result = await ResolveAsync(new DiffHunk(10, 1, 10, 0));

        Assert.Empty(result.ChangedSymbols.Symbols);
        DiffUnresolvedChange unresolved = Assert.Single(result.UnresolvedChanges);
        Assert.Contains("deleted-or-not-in-current-workspace", unresolved.ReasonCodes);
    }

    private async Task<ChangedSymbolsResolution> ResolveAsync(DiffHunk hunk)
    {
        DiffSet diff = new(
            Mode: "workingTree",
            Base: null,
            Head: null,
            Staged: false,
            IncludeUnstaged: true,
            TotalFiles: 1,
            Files:
            [
                new DiffFile(
                    Path: fixture.SymbolNavigationSource.RelativePath.Replace('\\', '/'),
                    OldPath: null,
                    Status: hunk.NewLineCount == 0 ? "deleted" : "modified",
                    Hunks: [hunk])
            ]);

        return await new ChangedSymbolResolver().ResolveAsync(
            fixture.SymbolNavigationWorkspace,
            diff,
            fixture.SymbolNavigationWorkspace.Solution.Projects.ToArray(),
            excludeGenerated: true,
            limit: 10,
            CancellationToken.None);
    }
}

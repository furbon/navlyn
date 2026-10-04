namespace Navlyn.Tests.TestSupport;

[CollectionDefinition(Name)]
public sealed class ExternalReadCollection : ICollectionFixture<ExternalReadSessions>
{
    public const string Name = "External reads";
}

public sealed class ExternalReadSessions : IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => ExternalLibrarySourceFixture.DisposeReadersAsync();
}

using ChatApp.Tests.Integration.Infrastructure;
using Xunit;

namespace ChatApp.Tests.Integration.Attachments;

[Collection(PostgresCollection.Name)]
public class OrphanPurgeTests(PostgresFixture pg) : IAsyncLifetime
{
    private ChatAppFactory _factory = default!;

    public async Task InitializeAsync()
    {
        _factory = new ChatAppFactory(pg.ConnectionString);
        await DbSeeder.ResetAsync(_factory, pg.ConnectionString);
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact(Skip = "Upload without attaching, advance FakeTimeProvider >1h, trigger purger, assert row+file deleted.")]
    public Task Unlinked_attachments_purged_after_ttl() => Task.CompletedTask;
}

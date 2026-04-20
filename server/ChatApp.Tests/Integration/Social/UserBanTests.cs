using ChatApp.Tests.Integration.Infrastructure;
using Xunit;

namespace ChatApp.Tests.Integration.Social;

[Collection(PostgresCollection.Name)]
public class UserBanTests(PostgresFixture pg) : IAsyncLifetime
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

    [Fact(Skip = "Ban user B from A; B's DM POST to A returns 403 with code=blocked.")]
    public Task Ban_blocks_dm_writes() => Task.CompletedTask;
}

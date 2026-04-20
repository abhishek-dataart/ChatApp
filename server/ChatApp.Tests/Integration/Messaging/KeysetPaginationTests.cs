using ChatApp.Tests.Integration.Infrastructure;
using Xunit;

namespace ChatApp.Tests.Integration.Messaging;

[Collection(PostgresCollection.Name)]
public class KeysetPaginationTests(PostgresFixture pg) : IAsyncLifetime
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

    [Fact(Skip = "Seed 120 messages then page back via ?before_created_at=&before_id=; assert page size 50 and cursor stability.")]
    public Task Paginate_back_across_120_messages() => Task.CompletedTask;
}

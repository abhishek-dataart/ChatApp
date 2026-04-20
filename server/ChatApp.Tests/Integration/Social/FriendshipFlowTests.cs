using ChatApp.Tests.Integration.Infrastructure;
using Xunit;

namespace ChatApp.Tests.Integration.Social;

[Collection(PostgresCollection.Name)]
public class FriendshipFlowTests(PostgresFixture pg) : IAsyncLifetime
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

    [Fact(Skip = "Send request -> accept -> list friends -> DM becomes available; author against FriendshipsController.")]
    public Task Send_accept_list() => Task.CompletedTask;
}

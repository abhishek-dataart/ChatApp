using ChatApp.Tests.Integration.Infrastructure;
using Xunit;

namespace ChatApp.Tests.Integration.Signalr;

[Collection(PostgresCollection.Name)]
public class ChatHubBroadcastTests(PostgresFixture pg) : IAsyncLifetime
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

    [Fact(Skip = "HubConnectionBuilder against factory.Server.CreateHandler(); REST POST message; assert client receives MessageCreated.")]
    public Task Rest_post_fans_out_as_message_created() => Task.CompletedTask;
}

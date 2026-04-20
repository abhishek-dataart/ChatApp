using ChatApp.Tests.Integration.Infrastructure;
using Xunit;

namespace ChatApp.Tests.Integration.Signalr;

[Collection(PostgresCollection.Name)]
public class PresenceHubHeartbeatTests(PostgresFixture pg) : IAsyncLifetime
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

    [Fact(Skip = "Connect two users as mutual contacts; heartbeat; assert PresenceChanged fan-out to peer.")]
    public Task Heartbeat_triggers_presence_changed_fanout() => Task.CompletedTask;
}

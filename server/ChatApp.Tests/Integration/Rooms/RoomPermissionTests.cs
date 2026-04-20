using ChatApp.Tests.Integration.Infrastructure;
using Xunit;

namespace ChatApp.Tests.Integration.Rooms;

[Collection(PostgresCollection.Name)]
public class RoomPermissionTests(PostgresFixture pg) : IAsyncLifetime
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

    [Fact(Skip = "Assert non-admin cannot kick; author after reading ModerationController.")]
    public Task Non_admin_cannot_kick() => Task.CompletedTask;

    [Fact(Skip = "Assert banned user cannot post messages; author after reading MessagesController + RoomBan seed path.")]
    public Task Banned_user_cannot_post() => Task.CompletedTask;
}

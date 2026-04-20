using ChatApp.Tests.Integration.Infrastructure;
using Xunit;

namespace ChatApp.Tests.Integration.Messaging;

[Collection(PostgresCollection.Name)]
public class RoomMessageCrudTests(PostgresFixture pg) : IAsyncLifetime
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

    [Fact(Skip = "POST /api/chats/room/{id}/messages then GET/PATCH/DELETE; author against RoomMessagesController.")]
    public Task Create_edit_delete_room_message() => Task.CompletedTask;
}

using ChatApp.Tests.Integration.Infrastructure;
using Xunit;

namespace ChatApp.Tests.Integration.Messaging;

[Collection(PostgresCollection.Name)]
public class DmMessageCrudTests(PostgresFixture pg) : IAsyncLifetime
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

    [Fact(Skip = "Friend-accept -> open DM -> POST message -> edit -> delete; author against PersonalMessagesController.")]
    public Task Create_edit_delete_dm_message() => Task.CompletedTask;
}

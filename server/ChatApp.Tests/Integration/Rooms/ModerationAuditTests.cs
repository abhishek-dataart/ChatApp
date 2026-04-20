using ChatApp.Tests.Integration.Infrastructure;
using Xunit;

namespace ChatApp.Tests.Integration.Rooms;

[Collection(PostgresCollection.Name)]
public class ModerationAuditTests(PostgresFixture pg) : IAsyncLifetime
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

    [Fact(Skip = "Perform ban/kick/role change; assert moderation_audit rows with matching ModerationActions constants.")]
    public Task Audit_row_written_per_action() => Task.CompletedTask;
}

using ChatApp.Tests.Integration.Infrastructure;
using Xunit;

namespace ChatApp.Tests.Integration.Attachments;

[Collection(PostgresCollection.Name)]
public class UploadAttachTests(PostgresFixture pg) : IAsyncLifetime
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

    [Fact(Skip = "Two-step: POST /api/attachments (multipart), then POST message referencing id; assert message_id FK set.")]
    public Task Two_step_upload_and_attach() => Task.CompletedTask;
}

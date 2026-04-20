using ChatApp.Tests.Integration.Infrastructure;
using Xunit;

namespace ChatApp.Tests.Integration.Attachments;

[Collection(PostgresCollection.Name)]
public class MimeSniffRejectionTests(PostgresFixture pg) : IAsyncLifetime
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

    [Fact(Skip = "Upload a .png-named file with JPEG magic bytes; assert 415 + code=unsupported_media_type.")]
    public Task Extension_mismatch_rejected() => Task.CompletedTask;
}

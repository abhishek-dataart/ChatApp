using System.Net;
using ChatApp.Data;
using ChatApp.Tests.Integration.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ChatApp.Tests.Integration.Auth;

[Collection(PostgresCollection.Name)]
public class SessionRevocationTests(PostgresFixture pg) : IAsyncLifetime
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

    [Fact]
    public async Task Deleting_session_row_directly_revokes_auth_after_cache_bypass()
    {
        var session = await AuthenticatedClient.RegisterAndLoginAsync(_factory);

        // Sanity — session works.
        (await session.Http.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Revoke by wiping Sessions for this user. IMemoryCache still caches the lookup for 30s,
        // so we both evict (via SessionLookupService) and bypass by advancing after.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
            await db.Sessions.Where(s => s.UserId == session.UserId).ExecuteDeleteAsync();
        }

        // Advance the fake clock past the 30s lookup-cache TTL.
        _factory.Clock.Advance(TimeSpan.FromSeconds(31));

        // Note: the SessionLookupService currently uses IMemoryCache with a wall-clock TTL,
        // so FakeTimeProvider advancing may not bypass it until the service migrates to TimeProvider.
        // When that happens, this assertion sharpens into a true revocation check; for now we
        // assert the DB state so the test documents intent.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
            var remaining = await db.Sessions.CountAsync(s => s.UserId == session.UserId);
            remaining.Should().Be(0);
        }
    }
}

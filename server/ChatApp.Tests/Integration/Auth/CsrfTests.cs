using System.Net;
using System.Net.Http.Json;
using ChatApp.Api.Contracts.Rooms;
using ChatApp.Tests.Integration.Infrastructure;
using FluentAssertions;
using Xunit;

namespace ChatApp.Tests.Integration.Auth;

[Collection(PostgresCollection.Name)]
public class CsrfTests(PostgresFixture pg) : IAsyncLifetime
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
    public async Task Post_without_csrf_header_is_rejected_with_403()
    {
        var session = await AuthenticatedClient.RegisterAndLoginAsync(_factory);

        // Strip the X-Csrf-Token header we normally attach.
        session.Http.DefaultRequestHeaders.Remove("X-Csrf-Token");

        var resp = await session.Http.PostAsJsonAsync("/api/rooms",
            new CreateRoomRequest("test-room", "desc", "public", 100));

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Post_with_matching_csrf_header_is_allowed()
    {
        var session = await AuthenticatedClient.RegisterAndLoginAsync(_factory);
        var resp = await session.Http.PostAsJsonAsync("/api/rooms",
            new CreateRoomRequest("test-room", "desc", "public", 100));

        resp.IsSuccessStatusCode.Should().BeTrue(
            $"room creation should pass CSRF (got {(int)resp.StatusCode})");
    }
}

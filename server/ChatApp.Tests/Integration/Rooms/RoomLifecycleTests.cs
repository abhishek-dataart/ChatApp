using System.Net;
using System.Net.Http.Json;
using ChatApp.Api.Contracts.Rooms;
using ChatApp.Tests.Integration.Infrastructure;
using FluentAssertions;
using Xunit;

namespace ChatApp.Tests.Integration.Rooms;

[Collection(PostgresCollection.Name)]
public class RoomLifecycleTests(PostgresFixture pg) : IAsyncLifetime
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
    public async Task Create_room_returns_201_and_owner_can_see_it_in_mine()
    {
        var session = await AuthenticatedClient.RegisterAndLoginAsync(_factory);

        var resp = await session.Http.PostAsJsonAsync("/api/rooms",
            new CreateRoomRequest("lifecycle-room", "hello", "public", 50));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        var mine = await session.Http.GetFromJsonAsync<List<MyRoomEntry>>("/api/rooms/mine");
        mine!.Should().ContainSingle(r => r.Name == "lifecycle-room");
    }

    [Fact(Skip = "Covers invite -> accept -> leave; depends on invitation controller contract, author after reading InvitationsController.")]
    public Task Invite_accept_leave_flow() => Task.CompletedTask;
}

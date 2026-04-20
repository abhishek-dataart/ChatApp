using ChatApp.Data;
using ChatApp.Data.Entities.Rooms;
using ChatApp.Data.Services.Rooms;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ChatApp.Tests.Unit;

// Uses EF Core InMemory — fine for the simple AnyAsync predicates in RoomPermissionService.
// Keyset / relational semantics that need Postgres are covered by the integration suite.
public class RoomPermissionMatrixTests
{
    private static ChatDbContext NewDb()
    {
        var opts = new DbContextOptionsBuilder<ChatDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ChatDbContext(opts);
    }

    private static async Task<(Guid roomId, Guid userId)> SeedAsync(ChatDbContext db, RoomRole role)
    {
        var roomId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        db.RoomMembers.Add(new RoomMember
        {
            RoomId = roomId,
            UserId = userId,
            Role = role,
            JoinedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return (roomId, userId);
    }

    [Theory]
    [InlineData(RoomRole.Owner, true, true, true)]
    [InlineData(RoomRole.Admin, true, true, false)]
    [InlineData(RoomRole.Member, true, false, false)]
    public async Task Role_matrix(RoomRole role, bool isMember, bool isAdminOrOwner, bool isOwner)
    {
        using var db = NewDb();
        var (roomId, userId) = await SeedAsync(db, role);
        var svc = new RoomPermissionService(db);

        (await svc.IsMemberAsync(roomId, userId)).Should().Be(isMember);
        (await svc.IsAdminOrOwnerAsync(roomId, userId)).Should().Be(isAdminOrOwner);
        (await svc.IsOwnerAsync(roomId, userId)).Should().Be(isOwner);
        (await svc.GetRoleAsync(roomId, userId)).Should().Be(role);
    }

    [Fact]
    public async Task Non_member_returns_no_role()
    {
        using var db = NewDb();
        var svc = new RoomPermissionService(db);

        (await svc.IsMemberAsync(Guid.NewGuid(), Guid.NewGuid())).Should().BeFalse();
        (await svc.GetRoleAsync(Guid.NewGuid(), Guid.NewGuid())).Should().BeNull();
    }
}

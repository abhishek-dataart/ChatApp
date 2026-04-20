using ChatApp.Data;
using ChatApp.Data.Entities.Rooms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ChatApp.Tests.Integration.Infrastructure;

// Convenience helpers that reach into the running host's DI to seed rows directly.
// Prefer going through the HTTP API for realism; use this only for test-specific state
// that would be tedious to set up via the API (e.g. pre-existing memberships).
public static class DbSeeder
{
    public static async Task AddRoomMemberAsync(ChatAppFactory factory, Guid roomId, Guid userId, RoomRole role)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
        db.RoomMembers.Add(new RoomMember
        {
            RoomId = roomId,
            UserId = userId,
            Role = role,
            JoinedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    public static async Task ResetAsync(ChatAppFactory factory, string connectionString)
    {
        // Respawn-based cleanup is the canonical approach; for a first pass we drop and re-migrate.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
        _ = connectionString; // reserved for future Respawn wiring
    }
}

using ChatApp.Data.Entities.Rooms;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Data.Services.Rooms;

public class RoomPermissionService(ChatDbContext db)
{
    public async Task<RoomRole?> GetRoleAsync(Guid roomId, Guid userId, CancellationToken ct = default)
    {
        var member = await db.RoomMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.RoomId == roomId && m.UserId == userId, ct);
        return member?.Role;
    }

    public async Task<bool> IsMemberAsync(Guid roomId, Guid userId, CancellationToken ct = default) =>
        await db.RoomMembers.AnyAsync(m => m.RoomId == roomId && m.UserId == userId, ct);

    public async Task<bool> IsAdminOrOwnerAsync(Guid roomId, Guid userId, CancellationToken ct = default) =>
        await db.RoomMembers.AnyAsync(
            m => m.RoomId == roomId && m.UserId == userId && m.Role >= RoomRole.Admin, ct);

    public async Task<bool> IsOwnerAsync(Guid roomId, Guid userId, CancellationToken ct = default) =>
        await db.RoomMembers.AnyAsync(
            m => m.RoomId == roomId && m.UserId == userId && m.Role == RoomRole.Owner, ct);
}

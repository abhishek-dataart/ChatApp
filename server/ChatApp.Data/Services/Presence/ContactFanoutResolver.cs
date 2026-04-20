using ChatApp.Data.Entities.Social;
using ChatApp.Domain.Services.Presence;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Data.Services.Presence;

/// <summary>
/// Resolves the set of users who should be notified when <paramref name="userId"/>'s
/// presence changes. The set is the union of:
///   - accepted friends (per the social graph)
///   - co-members of any room the user belongs to
/// The user themselves is excluded.
/// </summary>
public sealed class ContactFanoutResolver(ChatDbContext db) : IPresenceFanoutResolver
{
    public async Task<IReadOnlyCollection<Guid>> ResolveTargetsAsync(Guid userId, CancellationToken ct = default)
    {
        var friendIds = db.Friendships
            .AsNoTracking()
            .Where(f => f.State == FriendshipState.Accepted &&
                        (f.UserIdLow == userId || f.UserIdHigh == userId))
            .Select(f => f.UserIdLow == userId ? f.UserIdHigh : f.UserIdLow);

        var roomCoMemberIds =
            from mine in db.RoomMembers.AsNoTracking().Where(x => x.UserId == userId)
            join peer in db.RoomMembers.AsNoTracking() on mine.RoomId equals peer.RoomId
            where peer.UserId != userId
            select peer.UserId;

        return await friendIds
            .Union(roomCoMemberIds)
            .ToListAsync(ct);
    }
}

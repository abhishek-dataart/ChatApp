using ChatApp.Data.Entities.Messaging;
using ChatApp.Domain.Abstractions;
using ChatApp.Domain.Services.Messaging;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Data.Services.Messaging;

public class UnreadService(ChatDbContext db, IChatBroadcaster broadcaster)
{
    public async Task IncrementAsync(Guid senderId, MessageScope scope, Guid scopeId, CancellationToken ct = default)
    {
        List<Guid> recipientIds;
        if (scope == MessageScope.Personal)
        {
            var chat = await db.PersonalChats.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == scopeId, ct);
            if (chat is null)
            {
                return;
            }
            recipientIds = [chat.UserAId == senderId ? chat.UserBId : chat.UserAId];
        }
        else
        {
            recipientIds = await db.RoomMembers.AsNoTracking()
                .Where(m => m.RoomId == scopeId && m.UserId != senderId)
                .Select(m => m.UserId)
                .ToListAsync(ct);
        }

        if (recipientIds.Count == 0)
        {
            return;
        }

        var scopeInt = (int)scope;
        foreach (var uid in recipientIds)
        {
            await db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO unread_markers (user_id, scope, scope_id, unread_count, last_read_at)
                VALUES ({uid}, {scopeInt}, {scopeId}, 1, null)
                ON CONFLICT (user_id, scope, scope_id)
                DO UPDATE SET unread_count = unread_markers.unread_count + 1
                """, ct);
        }

        var updated = await db.UnreadMarkers.AsNoTracking()
            .Where(m => recipientIds.Contains(m.UserId) && m.Scope == scope && m.ScopeId == scopeId)
            .Select(m => new { m.UserId, m.UnreadCount })
            .ToListAsync(ct);

        var scopeStr = scope == MessageScope.Personal ? "personal" : "room";
        foreach (var row in updated)
        {
            var payload = new UnreadChangedPayload(scopeStr, scopeId, row.UnreadCount);
            await broadcaster.BroadcastUnreadChangedAsync(row.UserId, payload, ct);
        }
    }

    public async Task MarkReadAsync(Guid me, MessageScope scope, Guid scopeId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var scopeInt = (int)scope;
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO unread_markers (user_id, scope, scope_id, unread_count, last_read_at)
            VALUES ({me}, {scopeInt}, {scopeId}, 0, {now})
            ON CONFLICT (user_id, scope, scope_id)
            DO UPDATE SET unread_count = 0, last_read_at = {now}
            """, ct);

        var scopeStr = scope == MessageScope.Personal ? "personal" : "room";
        var payload = new UnreadChangedPayload(scopeStr, scopeId, 0);
        await broadcaster.BroadcastUnreadChangedAsync(me, payload, ct);
    }

    public async Task<List<UnreadChangedPayload>> GetAllAsync(Guid me, CancellationToken ct = default)
    {
        return await db.UnreadMarkers.AsNoTracking()
            .Where(m => m.UserId == me && m.UnreadCount > 0)
            .Select(m => new UnreadChangedPayload(
                m.Scope == MessageScope.Personal ? "personal" : "room",
                m.ScopeId,
                m.UnreadCount))
            .ToListAsync(ct);
    }
}

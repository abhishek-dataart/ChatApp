using ChatApp.Data.Entities.Messaging;
using ChatApp.Domain.Abstractions;
using ChatApp.Domain.Services.Messaging;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

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
        const string incrementSql = """
            INSERT INTO unread_markers (user_id, scope, scope_id, unread_count, last_read_at)
            SELECT uid, @scope, @scope_id, 1, null
            FROM unnest(@user_ids) AS uid
            ON CONFLICT (user_id, scope, scope_id)
            DO UPDATE SET unread_count = unread_markers.unread_count + 1
            RETURNING user_id, unread_count
            """;

        var userIdsArray = recipientIds.ToArray();
        var updated = new List<(Guid UserId, int UnreadCount)>(recipientIds.Count);

        var connection = db.Database.GetDbConnection();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = incrementSql;
            var scopeParam = new NpgsqlParameter("scope", NpgsqlDbType.Integer) { Value = scopeInt };
            var scopeIdParam = new NpgsqlParameter("scope_id", NpgsqlDbType.Uuid) { Value = scopeId };
            var userIdsParam = new NpgsqlParameter("user_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = userIdsArray };
            command.Parameters.Add(scopeParam);
            command.Parameters.Add(scopeIdParam);
            command.Parameters.Add(userIdsParam);

            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync(ct);
            }

            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                updated.Add((reader.GetGuid(0), reader.GetInt32(1)));
            }
        }

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
        const string markReadSql = """
            INSERT INTO unread_markers (user_id, scope, scope_id, unread_count, last_read_at)
            VALUES (@user_id, @scope, @scope_id, 0, @now)
            ON CONFLICT (user_id, scope, scope_id)
            DO UPDATE SET unread_count = 0, last_read_at = @now
            """;
        await db.Database.ExecuteSqlRawAsync(
            markReadSql,
            new[]
            {
                new NpgsqlParameter("user_id", NpgsqlDbType.Uuid) { Value = me },
                new NpgsqlParameter("scope", NpgsqlDbType.Integer) { Value = scopeInt },
                new NpgsqlParameter("scope_id", NpgsqlDbType.Uuid) { Value = scopeId },
                new NpgsqlParameter("now", NpgsqlDbType.TimestampTz) { Value = now },
            },
            ct);

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

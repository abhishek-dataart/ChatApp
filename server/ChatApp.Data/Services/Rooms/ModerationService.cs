using System.Data;
using System.Text.Json;
using ChatApp.Data.Entities.Rooms;
using ChatApp.Domain.Abstractions;
using ChatApp.Domain.Services.Rooms;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ChatApp.Data.Services.Rooms;

public sealed record BanRow(
    Guid BanId,
    Guid UserId, string Username, string DisplayName, string? AvatarPath,
    Guid BannedById, string BannedByUsername, string BannedByDisplayName, string? BannedByAvatarPath,
    DateTimeOffset CreatedAt);

public sealed record AuditRow(
    Guid Id,
    Guid ActorId, string ActorUsername, string ActorDisplayName, string? ActorAvatarPath,
    Guid? TargetId, string? TargetUsername, string? TargetDisplayName, string? TargetAvatarPath,
    string Action, string? Detail, DateTimeOffset CreatedAt);

public class ModerationService(ChatDbContext db, IChatBroadcaster broadcaster, IPresenceStore presenceStore)
{
    public async Task<bool> IsBannedAsync(Guid roomId, Guid userId, CancellationToken ct = default) =>
        await db.RoomBans.AnyAsync(rb => rb.RoomId == roomId && rb.UserId == userId && rb.LiftedAt == null, ct);

    public async Task<(bool Ok, string? Code, string? Message)> BanAsync(
        Guid actorId, Guid roomId, Guid targetId, CancellationToken ct = default)
    {
        var room = await db.Rooms.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roomId, ct);
        if (room is null || room.DeletedAt != null)
        {
            return (false, RoomsErrors.RoomNotFound, "Room not found.");
        }

        var actorMember = await db.RoomMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.RoomId == roomId && m.UserId == actorId, ct);
        if (actorMember is null || actorMember.Role < RoomRole.Admin)
        {
            return (false, RoomsErrors.NotAdminOrOwner, "You must be an admin or owner.");
        }

        if (targetId == actorId)
        {
            return (false, RoomsErrors.CannotBanSelf, "You cannot ban yourself.");
        }

        var targetMember = await db.RoomMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.RoomId == roomId && m.UserId == targetId, ct);
        if (targetMember is null)
        {
            var alreadyBanned = await db.RoomBans.AnyAsync(
                rb => rb.RoomId == roomId && rb.UserId == targetId && rb.LiftedAt == null, ct);
            return alreadyBanned
                ? (false, RoomsErrors.AlreadyBanned, "User is already banned from this room.")
                : (false, RoomsErrors.MemberNotFound, "Target member not found.");
        }

        if (targetMember.Role == RoomRole.Owner)
        {
            return (false, RoomsErrors.CannotBanOwner, "Cannot ban the room owner.");
        }

        if (actorMember.Role == RoomRole.Admin && targetMember.Role == RoomRole.Admin)
        {
            return (false, RoomsErrors.CannotBanPeerAdmin, "Admins cannot ban other admins.");
        }

        var now = DateTimeOffset.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var ban = new RoomBan
        {
            Id = Guid.NewGuid(),
            RoomId = roomId,
            UserId = targetId,
            BannedById = actorId,
            CreatedAt = now,
        };
        db.RoomBans.Add(ban);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == "23505")
        {
            await tx.RollbackAsync(ct);
            return (false, RoomsErrors.AlreadyBanned, "User is already banned from this room.");
        }

        await db.RoomMembers
            .Where(m => m.RoomId == roomId && m.UserId == targetId)
            .ExecuteDeleteAsync(ct);

        db.ModerationAudits.Add(new ModerationAudit
        {
            Id = Guid.NewGuid(),
            RoomId = roomId,
            ActorId = actorId,
            TargetId = targetId,
            Action = ModerationActions.Ban,
            CreatedAt = now,
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var connIds = presenceStore.GetConnectionIds(targetId).ToList();
        foreach (var connId in connIds)
        {
            await broadcaster.RemoveConnectionFromRoomAsync(connId, roomId, ct);
        }

        var actor = await db.Users.AsNoTracking().FirstAsync(u => u.Id == actorId, ct);
        var actorAvatarUrl = actor.AvatarPath is null ? null : $"/api/profile/avatar/{actorId}";

        await broadcaster.BroadcastRoomMemberChangedAsync(roomId,
            new RoomMemberChangedPayload(roomId, targetId, "removed", null), ct);

        await broadcaster.BroadcastRoomBannedToUserAsync(targetId,
            new RoomBannedPayload(roomId, room.Name,
                new UserSummaryPayload(actorId, actor.Username, actor.DisplayName, actorAvatarUrl),
                now), ct);

        return (true, null, null);
    }

    public async Task<(bool Ok, string? Code, string? Message)> UnbanAsync(
        Guid actorId, Guid roomId, Guid targetId, CancellationToken ct = default)
    {
        var room = await db.Rooms.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roomId, ct);
        if (room is null || room.DeletedAt != null)
        {
            return (false, RoomsErrors.RoomNotFound, "Room not found.");
        }

        var actorMember = await db.RoomMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.RoomId == roomId && m.UserId == actorId, ct);
        if (actorMember is null || actorMember.Role < RoomRole.Admin)
        {
            return (false, RoomsErrors.NotAdminOrOwner, "You must be an admin or owner.");
        }

        var now = DateTimeOffset.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var updated = await db.RoomBans
            .Where(rb => rb.RoomId == roomId && rb.UserId == targetId && rb.LiftedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(rb => rb.LiftedAt, now), ct);

        if (updated == 0)
        {
            await tx.RollbackAsync(ct);
            return (false, RoomsErrors.BanNotFound, "Active ban not found.");
        }

        db.ModerationAudits.Add(new ModerationAudit
        {
            Id = Guid.NewGuid(),
            RoomId = roomId,
            ActorId = actorId,
            TargetId = targetId,
            Action = ModerationActions.Unban,
            CreatedAt = now,
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return (true, null, null);
    }

    public async Task<(bool Ok, string? Code, string? Message, List<BanRow>? Value)> ListBansAsync(
        Guid actorId, Guid roomId, CancellationToken ct = default)
    {
        var room = await db.Rooms.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roomId, ct);
        if (room is null || room.DeletedAt != null)
        {
            return (false, RoomsErrors.RoomNotFound, "Room not found.", null);
        }

        var isAdminOrOwner = await db.RoomMembers.AnyAsync(
            m => m.RoomId == roomId && m.UserId == actorId && m.Role >= RoomRole.Admin, ct);
        if (!isAdminOrOwner)
        {
            return (false, RoomsErrors.NotAdminOrOwner, "You must be an admin or owner.", null);
        }

        var rows = await db.RoomBans
            .AsNoTracking()
            .Where(rb => rb.RoomId == roomId && rb.LiftedAt == null)
            .Join(db.Users, rb => rb.UserId, u => u.Id, (rb, u) => new { Ban = rb, User = u })
            .Join(db.Users, x => x.Ban.BannedById, u => u.Id, (x, banner) => new { x.Ban, x.User, Banner = banner })
            .OrderByDescending(x => x.Ban.CreatedAt)
            .ToListAsync(ct);

        var result = rows.Select(x => new BanRow(
            x.Ban.Id,
            x.User.Id, x.User.Username, x.User.DisplayName,
            x.User.AvatarPath is null ? null : $"/api/profile/avatar/{x.User.Id}",
            x.Banner.Id, x.Banner.Username, x.Banner.DisplayName,
            x.Banner.AvatarPath is null ? null : $"/api/profile/avatar/{x.Banner.Id}",
            x.Ban.CreatedAt)).ToList();

        return (true, null, null, result);
    }

    public async Task<(bool Ok, string? Code, string? Message)> ChangeRoleAsync(
        Guid actorId, Guid roomId, Guid targetId, string? rawRole, CancellationToken ct = default)
    {
        if (!TryParseRole(rawRole, out var newRole))
        {
            return (false, RoomsErrors.InvalidRole, "Role must be 'admin' or 'member'.");
        }

        var room = await db.Rooms.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roomId, ct);
        if (room is null || room.DeletedAt != null)
        {
            return (false, RoomsErrors.RoomNotFound, "Room not found.");
        }

        var actorMember = await db.RoomMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.RoomId == roomId && m.UserId == actorId, ct);
        if (actorMember is null || actorMember.Role < RoomRole.Admin)
        {
            return (false, RoomsErrors.NotAdminOrOwner, "You must be an admin or owner.");
        }

        var targetMember = await db.RoomMembers
            .FirstOrDefaultAsync(m => m.RoomId == roomId && m.UserId == targetId, ct);
        if (targetMember is null)
        {
            return (false, RoomsErrors.MemberNotFound, "Member not found.");
        }

        if (targetMember.Role == RoomRole.Owner)
        {
            return (false, RoomsErrors.CannotChangeOwnerRole, "Cannot change the owner's role.");
        }

        if (targetMember.Role == RoomRole.Admin && newRole == RoomRole.Member && actorMember.Role != RoomRole.Owner)
        {
            return (false, RoomsErrors.NotOwner, "Only the owner can demote an admin.");
        }

        if (targetId == actorId && newRole == RoomRole.Admin)
        {
            return (false, RoomsErrors.CannotPromoteSelf, "You cannot promote yourself.");
        }

        if (targetMember.Role == newRole)
        {
            return (true, null, null);
        }

        var fromRole = targetMember.Role.ToString().ToLowerInvariant();
        var toRole = newRole.ToString().ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        targetMember.Role = newRole;
        db.ModerationAudits.Add(new ModerationAudit
        {
            Id = Guid.NewGuid(),
            RoomId = roomId,
            ActorId = actorId,
            TargetId = targetId,
            Action = ModerationActions.RoleChange,
            Detail = JsonSerializer.Serialize(new { from = fromRole, to = toRole }),
            CreatedAt = now,
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await broadcaster.BroadcastRoomMemberChangedAsync(roomId,
            new RoomMemberChangedPayload(roomId, targetId, "role_changed", toRole), ct);

        return (true, null, null);
    }

    public async Task<(bool Ok, string? Code, string? Message, List<AuditRow>? Value, Guid? NextBefore)> ListAuditAsync(
        Guid actorId, Guid roomId, int limit, Guid? before, CancellationToken ct = default)
    {
        var room = await db.Rooms.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roomId, ct);
        if (room is null || room.DeletedAt != null)
        {
            return (false, RoomsErrors.RoomNotFound, "Room not found.", null, null);
        }

        var isAdminOrOwner = await db.RoomMembers.AnyAsync(
            m => m.RoomId == roomId && m.UserId == actorId && m.Role >= RoomRole.Admin, ct);
        if (!isAdminOrOwner)
        {
            return (false, RoomsErrors.NotAdminOrOwner, "You must be an admin or owner.", null, null);
        }

        limit = Math.Clamp(limit, 1, 200);

        var query = db.ModerationAudits.AsNoTracking().Where(a => a.RoomId == roomId);

        if (before.HasValue)
        {
            var cursor = await db.ModerationAudits.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == before.Value, ct);
            if (cursor is not null)
            {
                query = query.Where(a => a.CreatedAt < cursor.CreatedAt ||
                    (a.CreatedAt == cursor.CreatedAt && a.Id.CompareTo(cursor.Id) < 0));
            }
        }

        var rawRows = await query
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id)
            .Take(limit + 1)
            .Join(db.Users, a => a.ActorId, u => u.Id, (a, actor) => new { Audit = a, Actor = actor })
            .ToListAsync(ct);

        Guid? nextBefore = null;
        if (rawRows.Count > limit)
        {
            nextBefore = rawRows[limit - 1].Audit.Id;
            rawRows = rawRows.Take(limit).ToList();
        }

        var targetIds = rawRows.Where(x => x.Audit.TargetId.HasValue)
            .Select(x => x.Audit.TargetId!.Value).Distinct().ToList();
        var targets = await db.Users.AsNoTracking()
            .Where(u => targetIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, ct);

        var result = rawRows.Select(x =>
        {
            var targetUser = x.Audit.TargetId.HasValue && targets.TryGetValue(x.Audit.TargetId.Value, out var t) ? t : null;
            return new AuditRow(
                x.Audit.Id,
                x.Actor.Id, x.Actor.Username, x.Actor.DisplayName,
                x.Actor.AvatarPath is null ? null : $"/api/profile/avatar/{x.Actor.Id}",
                x.Audit.TargetId,
                targetUser?.Username, targetUser?.DisplayName,
                targetUser?.AvatarPath is null ? null : (targetUser.AvatarPath is null ? null : $"/api/profile/avatar/{targetUser.Id}"),
                x.Audit.Action, x.Audit.Detail, x.Audit.CreatedAt);
        }).ToList();

        return (true, null, null, result, nextBefore);
    }

    public async Task<(bool Ok, string? Code, string? Message)> KickAsync(
        Guid actorId, Guid roomId, Guid targetId, CancellationToken ct = default)
    {
        var room = await db.Rooms.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roomId, ct);
        if (room is null || room.DeletedAt != null)
        {
            return (false, RoomsErrors.RoomNotFound, "Room not found.");
        }

        var actorMember = await db.RoomMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.RoomId == roomId && m.UserId == actorId, ct);
        if (actorMember is null || actorMember.Role < RoomRole.Admin)
        {
            return (false, RoomsErrors.NotAdminOrOwner, "You must be an admin or owner.");
        }

        if (targetId == actorId)
        {
            return (false, RoomsErrors.CannotKickSelf, "You cannot remove yourself.");
        }

        var targetMember = await db.RoomMembers
            .FirstOrDefaultAsync(m => m.RoomId == roomId && m.UserId == targetId, ct);
        if (targetMember is null)
        {
            return (false, RoomsErrors.MemberNotFound, "Member not found.");
        }

        if (targetMember.Role == RoomRole.Owner)
        {
            return (false, RoomsErrors.CannotKickOwner, "Cannot remove the room owner.");
        }

        if (actorMember.Role == RoomRole.Admin && targetMember.Role == RoomRole.Admin)
        {
            return (false, RoomsErrors.CannotKickPeerAdmin, "Admins cannot remove other admins.");
        }

        var now = DateTimeOffset.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        db.RoomMembers.Remove(targetMember);
        db.ModerationAudits.Add(new ModerationAudit
        {
            Id = Guid.NewGuid(),
            RoomId = roomId,
            ActorId = actorId,
            TargetId = targetId,
            Action = ModerationActions.Kick,
            CreatedAt = now,
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var connIds = presenceStore.GetConnectionIds(targetId).ToList();
        foreach (var connId in connIds)
        {
            await broadcaster.RemoveConnectionFromRoomAsync(connId, roomId, ct);
        }

        await broadcaster.BroadcastRoomMemberChangedAsync(roomId,
            new RoomMemberChangedPayload(roomId, targetId, "removed", null), ct);

        return (true, null, null);
    }

    private static bool TryParseRole(string? raw, out RoomRole role)
    {
        switch (raw?.Trim().ToLowerInvariant())
        {
            case "admin":
                role = RoomRole.Admin;
                return true;
            case "member":
                role = RoomRole.Member;
                return true;
            default:
                role = default;
                return false;
        }
    }
}

using System.Data;
using ChatApp.Data.Entities.Rooms;
using ChatApp.Data.Services.Social;
using ChatApp.Domain.Abstractions;
using ChatApp.Domain.Services.Rooms;
using ChatApp.Domain.Services.Social;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ChatApp.Data.Services.Rooms;

public sealed record IncomingInvitationRow(
    Guid InvitationId,
    Guid RoomId, string RoomName, string RoomDescription, string RoomVisibility,
    int RoomMemberCount, int RoomCapacity, DateTimeOffset RoomCreatedAt, string? RoomLogoUrl,
    Guid InviterId, string InviterUsername, string InviterDisplayName, string? InviterAvatarPath,
    string? Note, DateTimeOffset CreatedAt);

public sealed record OutgoingInvitationRow(
    Guid InvitationId,
    Guid InviteeId, string InviteeUsername, string InviteeDisplayName, string? InviteeAvatarPath,
    Guid InviterId, string InviterUsername, string InviterDisplayName, string? InviterAvatarPath,
    string? Note, DateTimeOffset CreatedAt);

public class InvitationService(ChatDbContext db, RoomService rooms, UserBanService userBans, IChatBroadcaster broadcaster)
{
    private async Task NotifyAsync(Guid userId, Guid invitationId, string kind, CancellationToken ct)
    {
        var payload = new InvitationChangedPayload(invitationId, kind);
        await broadcaster.BroadcastInvitationChangedAsync(userId, payload, ct);
    }

    public async Task<(bool Ok, string? Code, string? Message, OutgoingInvitationRow? Value)> SendAsync(
        Guid me, Guid roomId, string? rawUsername, string? rawNote, CancellationToken ct = default)
    {
        var note = rawNote?.Trim();
        if (string.IsNullOrEmpty(note))
        {
            note = null;
        }

        if (note is not null && note.Length > 200)
        {
            return (false, RoomsErrors.NoteTooLong, "Note must be 200 characters or fewer.", null);
        }

        var room = await db.Rooms.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roomId, ct);
        if (room is null || room.DeletedAt != null)
        {
            return (false, RoomsErrors.RoomNotFound, "Room not found.", null);
        }

        var isAdminOrOwner = await db.RoomMembers.AnyAsync(
            m => m.RoomId == roomId && m.UserId == me && m.Role >= RoomRole.Admin, ct);
        if (!isAdminOrOwner)
        {
            return (false, RoomsErrors.NotAdminOrOwner, "You must be an admin or owner to invite users.", null);
        }

        var username = rawUsername?.Trim() ?? string.Empty;
        var invitee = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.UsernameNormalized == username.ToLowerInvariant() && u.DeletedAt == null, ct);
        if (invitee is null)
        {
            return (false, RoomsErrors.UserNotFound, "User not found.", null);
        }

        if (invitee.Id == me)
        {
            return (false, RoomsErrors.CannotInviteSelf, "You cannot invite yourself.", null);
        }

        var inviteeBanned = await db.RoomBans.AnyAsync(
            rb => rb.RoomId == roomId && rb.UserId == invitee.Id && rb.LiftedAt == null, ct);
        if (inviteeBanned)
        {
            return (false, RoomsErrors.InviteeRoomBanned, "User is banned from this room. Unban them first.", null);
        }

        if (await userBans.IsActiveAnyDirectionAsync(me, invitee.Id, ct))
        {
            return (false, SocialErrors.UserBanned, "Cannot invite this user.", null);
        }

        var alreadyMember = await db.RoomMembers.AnyAsync(
            m => m.RoomId == roomId && m.UserId == invitee.Id, ct);
        if (alreadyMember)
        {
            return (false, RoomsErrors.AlreadyMember, "User is already a member of this room.", null);
        }

        var invitation = new RoomInvitation
        {
            Id = Guid.NewGuid(),
            RoomId = roomId,
            InviterId = me,
            InviteeId = invitee.Id,
            Note = note,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.RoomInvitations.Add(invitation);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == "23505")
        {
            return (false, RoomsErrors.InvitationExists, "A pending invitation already exists for this user.", null);
        }

        await NotifyAsync(invitee.Id, invitation.Id, "invited", ct);

        var inviter = await db.Users.AsNoTracking().FirstAsync(u => u.Id == me, ct);
        var row = new OutgoingInvitationRow(
            invitation.Id,
            invitee.Id, invitee.Username, invitee.DisplayName,
            invitee.AvatarPath is null ? null : $"/api/profile/avatar/{invitee.Id}",
            me, inviter.Username, inviter.DisplayName,
            inviter.AvatarPath is null ? null : $"/api/profile/avatar/{me}",
            note, invitation.CreatedAt);

        return (true, null, null, row);
    }

    public async Task<List<IncomingInvitationRow>> ListIncomingAsync(Guid me, CancellationToken ct = default)
    {
        var rows = await db.RoomInvitations
            .AsNoTracking()
            .Where(i => i.InviteeId == me)
            .Join(db.Rooms.Where(r => r.DeletedAt == null),
                i => i.RoomId, r => r.Id,
                (i, r) => new { Invitation = i, Room = r })
            .Join(db.Users, x => x.Invitation.InviterId, u => u.Id,
                (x, inviter) => new { x.Invitation, x.Room, Inviter = inviter })
            .OrderByDescending(x => x.Invitation.CreatedAt)
            .ToListAsync(ct);

        var roomIds = rows.Select(r => r.Room.Id).Distinct().ToList();
        var memberCounts = await db.RoomMembers
            .AsNoTracking()
            .Where(m => roomIds.Contains(m.RoomId))
            .GroupBy(m => m.RoomId)
            .Select(g => new { RoomId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var countMap = memberCounts.ToDictionary(x => x.RoomId, x => x.Count);

        return rows.Select(x => new IncomingInvitationRow(
            x.Invitation.Id,
            x.Room.Id, x.Room.Name, x.Room.Description,
            x.Room.Visibility.ToString().ToLowerInvariant(),
            countMap.GetValueOrDefault(x.Room.Id, 0), x.Room.Capacity, x.Room.CreatedAt,
            x.Room.LogoPath is null ? null : $"/api/rooms/{x.Room.Id}/logo",
            x.Inviter.Id, x.Inviter.Username, x.Inviter.DisplayName,
            x.Inviter.AvatarPath is null ? null : $"/api/profile/avatar/{x.Inviter.Id}",
            x.Invitation.Note, x.Invitation.CreatedAt)).ToList();
    }

    public async Task<(bool Ok, string? Code, string? Message, List<OutgoingInvitationRow>? Value)> ListOutgoingForRoomAsync(
        Guid me, Guid roomId, CancellationToken ct = default)
    {
        var room = await db.Rooms.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roomId, ct);
        if (room is null || room.DeletedAt != null)
        {
            return (false, RoomsErrors.RoomNotFound, "Room not found.", null);
        }

        var isAdminOrOwner = await db.RoomMembers.AnyAsync(
            m => m.RoomId == roomId && m.UserId == me && m.Role >= RoomRole.Admin, ct);
        if (!isAdminOrOwner)
        {
            return (false, RoomsErrors.NotAdminOrOwner, "You must be an admin or owner.", null);
        }

        var rows = await db.RoomInvitations
            .AsNoTracking()
            .Where(i => i.RoomId == roomId)
            .Join(db.Users, i => i.InviteeId, u => u.Id, (i, invitee) => new { Invitation = i, Invitee = invitee })
            .Join(db.Users, x => x.Invitation.InviterId, u => u.Id, (x, inviter) => new { x.Invitation, x.Invitee, Inviter = inviter })
            .OrderByDescending(x => x.Invitation.CreatedAt)
            .ToListAsync(ct);

        var result = rows.Select(x => new OutgoingInvitationRow(
            x.Invitation.Id,
            x.Invitee.Id, x.Invitee.Username, x.Invitee.DisplayName,
            x.Invitee.AvatarPath is null ? null : $"/api/profile/avatar/{x.Invitee.Id}",
            x.Inviter.Id, x.Inviter.Username, x.Inviter.DisplayName,
            x.Inviter.AvatarPath is null ? null : $"/api/profile/avatar/{x.Inviter.Id}",
            x.Invitation.Note, x.Invitation.CreatedAt)).ToList();

        return (true, null, null, result);
    }

    public async Task<(bool Ok, string? Code, string? Message, RoomDetailOutcome? Value)> AcceptAsync(
        Guid me, Guid invitationId, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var invitation = await db.RoomInvitations.FirstOrDefaultAsync(i => i.Id == invitationId, ct);
        if (invitation is null || invitation.InviteeId != me)
        {
            await tx.RollbackAsync(ct);
            return (false, RoomsErrors.InvitationNotFound, "Invitation not found.", null);
        }

        var room = await db.Rooms.AsNoTracking().FirstOrDefaultAsync(r => r.Id == invitation.RoomId, ct);
        if (room is null || room.DeletedAt != null)
        {
            await tx.RollbackAsync(ct);
            return (false, RoomsErrors.RoomNotFound, "Room not found.", null);
        }

        var isBanned = await db.RoomBans.AnyAsync(
            rb => rb.RoomId == invitation.RoomId && rb.UserId == me && rb.LiftedAt == null, ct);
        if (isBanned)
        {
            await tx.RollbackAsync(ct);
            return (false, RoomsErrors.RoomBanned, "You are banned from this room.", null);
        }

        if (await userBans.IsActiveAnyDirectionAsync(me, invitation.InviterId, ct))
        {
            await tx.RollbackAsync(ct);
            return (false, SocialErrors.UserBanned, "Cannot accept invitation due to an active ban.", null);
        }

        var alreadyMember = await db.RoomMembers.AnyAsync(
            m => m.RoomId == room.Id && m.UserId == me, ct);
        if (alreadyMember)
        {
            db.RoomInvitations.Remove(invitation);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return (false, RoomsErrors.AlreadyMember, "You are already a member of this room.", null);
        }

        db.RoomMembers.Add(new RoomMember
        {
            RoomId = room.Id,
            UserId = me,
            Role = RoomRole.Member,
            JoinedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(ct);

        var count = await db.RoomMembers.CountAsync(m => m.RoomId == room.Id, ct);
        if (count > room.Capacity)
        {
            await tx.RollbackAsync(ct);
            return (false, RoomsErrors.RoomFull, "This room is at capacity.", null);
        }

        db.RoomInvitations.Remove(invitation);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await NotifyAsync(invitation.InviterId, invitation.Id, "accepted", ct);
        await NotifyAsync(me, invitation.Id, "accepted", ct);

        var detail = await rooms.GetAsync(me, room.Id, ct);
        return (true, null, null, detail.Value);
    }

    public async Task<(bool Ok, string? Code, string? Message)> DeclineAsync(
        Guid me, Guid invitationId, CancellationToken ct = default)
    {
        var invitation = await db.RoomInvitations
            .FirstOrDefaultAsync(i => i.Id == invitationId && i.InviteeId == me, ct);
        if (invitation is null)
        {
            return (false, RoomsErrors.InvitationNotFound, "Invitation not found.");
        }

        var inviterId = invitation.InviterId;
        db.RoomInvitations.Remove(invitation);
        await db.SaveChangesAsync(ct);

        await NotifyAsync(inviterId, invitation.Id, "declined", ct);
        await NotifyAsync(me, invitation.Id, "declined", ct);
        return (true, null, null);
    }

    public async Task<(bool Ok, string? Code, string? Message)> RevokeAsync(
        Guid me, Guid invitationId, CancellationToken ct = default)
    {
        var invitation = await db.RoomInvitations
            .FirstOrDefaultAsync(i => i.Id == invitationId && i.InviterId == me, ct);
        if (invitation is null)
        {
            return (false, RoomsErrors.InvitationNotFound, "Invitation not found.");
        }

        var inviteeId = invitation.InviteeId;
        db.RoomInvitations.Remove(invitation);
        await db.SaveChangesAsync(ct);

        await NotifyAsync(inviteeId, invitation.Id, "revoked", ct);
        await NotifyAsync(me, invitation.Id, "revoked", ct);
        return (true, null, null);
    }

    public async Task<(bool Ok, string? Code, string? Message)> RevokeOrDeclineAsync(
        Guid me, Guid invitationId, CancellationToken ct = default)
    {
        var invitation = await db.RoomInvitations
            .FirstOrDefaultAsync(i => i.Id == invitationId && (i.InviterId == me || i.InviteeId == me), ct);
        if (invitation is null)
        {
            return (false, RoomsErrors.InvitationNotFound, "Invitation not found.");
        }

        var inviterId = invitation.InviterId;
        var inviteeId = invitation.InviteeId;
        var kind = invitation.InviterId == me ? "revoked" : "declined";
        db.RoomInvitations.Remove(invitation);
        await db.SaveChangesAsync(ct);

        await NotifyAsync(inviterId, invitation.Id, kind, ct);
        await NotifyAsync(inviteeId, invitation.Id, kind, ct);
        return (true, null, null);
    }
}

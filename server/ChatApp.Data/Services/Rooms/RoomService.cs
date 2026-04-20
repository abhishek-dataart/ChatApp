using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using ChatApp.Data.Entities.Rooms;
using ChatApp.Domain.Abstractions;
using ChatApp.Domain.Services.Rooms;
using ChatApp.Domain.Services.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ChatApp.Data.Services.Rooms;

public sealed record RoomMemberRow(
    Guid UserId, string Username, string DisplayName, string? AvatarUrl,
    RoomRole Role, DateTimeOffset JoinedAt);

public sealed record RoomDetailOutcome(
    Guid Id, string Name, string Description, RoomVisibility Visibility,
    Guid OwnerId, int Capacity, DateTimeOffset CreatedAt,
    int MemberCount, RoomRole CurrentUserRole, List<RoomMemberRow> Members,
    string? LogoUrl);

public sealed record CatalogItemOutcome(
    Guid Id, string Name, string Description, RoomVisibility Visibility,
    int Capacity, DateTimeOffset CreatedAt,
    int MemberCount, bool IsMember, string? LogoUrl);

public sealed record MyRoomItemOutcome(
    Guid Id, string Name, string Description, RoomVisibility Visibility,
    int Capacity, DateTimeOffset CreatedAt,
    int MemberCount, RoomRole Role, DateTimeOffset JoinedAt, string? LogoUrl);

public class RoomService(
    ChatDbContext db,
    IChatBroadcaster broadcaster,
    IPresenceStore presenceStore,
    IAvatarImageProcessor imageProcessor,
    RoomPermissionService permissions,
    ILogger<RoomService> log)
{
    private static readonly Regex NameRegex =
        new(@"^[A-Za-z0-9][A-Za-z0-9 _-]{1,38}[A-Za-z0-9]$", RegexOptions.Compiled);

    public async Task<(bool Ok, string? Code, string? Message, RoomDetailOutcome? Value)> CreateAsync(
        Guid me, string? rawName, string? rawDescription, string? rawVisibility, int? capacity,
        CancellationToken ct = default)
    {
        var name = rawName?.Trim() ?? string.Empty;
        var description = rawDescription?.Trim() ?? string.Empty;

        if (name.Length < 3 || name.Length > 40 || !NameRegex.IsMatch(name))
        {
            return (false, RoomsErrors.InvalidRoomName,
                "Room name must be 3-40 characters, start and end with alphanumeric, and contain only letters, digits, spaces, underscores, and hyphens.", null);
        }

        if (description.Length < 1 || description.Length > 200)
        {
            return (false, RoomsErrors.InvalidDescription,
                "Description is required and must be 200 characters or fewer.", null);
        }

        if (!TryParseVisibility(rawVisibility, out var visibility))
        {
            return (false, RoomsErrors.InvalidRoomName, "Visibility must be 'public' or 'private'.", null);
        }

        var cap = capacity ?? 1000;
        if (cap < 2 || cap > 1_000)
        {
            return (false, RoomsErrors.InvalidCapacity, "Capacity must be between 2 and 1,000.", null);
        }

        var nameNormalized = name.ToLowerInvariant();

        var taken = await db.Rooms.AnyAsync(r => r.NameNormalized == nameNormalized, ct);
        if (taken)
        {
            return (false, RoomsErrors.RoomNameTaken, "A room with this name already exists.", null);
        }

        var now = DateTimeOffset.UtcNow;
        var room = new Room
        {
            Id = Guid.NewGuid(),
            Name = name,
            NameNormalized = nameNormalized,
            Description = description,
            Visibility = visibility,
            OwnerId = me,
            Capacity = cap,
            CreatedAt = now,
        };
        var ownerMember = new RoomMember
        {
            RoomId = room.Id,
            UserId = me,
            Role = RoomRole.Owner,
            JoinedAt = now,
        };

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.Rooms.Add(room);
        db.RoomMembers.Add(ownerMember);
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == "23505")
        {
            await tx.RollbackAsync(ct);
            return (false, RoomsErrors.RoomNameTaken, "A room with this name already exists.", null);
        }

        return (true, null, null, await BuildDetailAsync(room, me, ct));
    }

    public async Task<(bool Ok, string? Code, string? Message, List<CatalogItemOutcome>? Value)> ListCatalogAsync(
        Guid me, string? q, CancellationToken ct = default)
    {
        var trimmedQ = q?.Trim();

        if (trimmedQ?.Length > 50)
        {
            return (false, RoomsErrors.SearchTooLong, "Search query must be 50 characters or fewer.", null);
        }

        var query = db.Rooms.AsNoTracking()
            .Where(r => r.Visibility == RoomVisibility.Public && r.DeletedAt == null);

        if (!string.IsNullOrEmpty(trimmedQ))
        {
            var pattern = "%" + trimmedQ.ToLowerInvariant() + "%";
            query = query.Where(r => EF.Functions.ILike(r.NameNormalized, pattern));
        }

        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new
            {
                r.Id, r.Name, r.Description, r.Visibility, r.Capacity, r.CreatedAt,
                r.LogoPath,
                MemberCount = db.RoomMembers.Count(m => m.RoomId == r.Id),
                IsMember = db.RoomMembers.Any(m => m.RoomId == r.Id && m.UserId == me),
            })
            .ToListAsync(ct);

        return (true, null, null, items.Select(i => new CatalogItemOutcome(
            i.Id, i.Name, i.Description, i.Visibility, i.Capacity, i.CreatedAt,
            i.MemberCount, i.IsMember, LogoUrlFor(i.Id, i.LogoPath))).ToList());
    }

    public async Task<List<MyRoomItemOutcome>> ListMineAsync(Guid me, CancellationToken ct = default)
    {
        var items = await db.RoomMembers
            .AsNoTracking()
            .Where(m => m.UserId == me)
            .Join(db.Rooms, m => m.RoomId, r => r.Id, (m, r) => new { m, r })
            .Where(x => x.r.DeletedAt == null)
            .OrderByDescending(x => x.m.Role)
            .ThenBy(x => x.r.Name)
            .Select(x => new
            {
                x.r.Id, x.r.Name, x.r.Description, x.r.Visibility, x.r.Capacity, x.r.CreatedAt,
                x.r.LogoPath,
                MemberCount = db.RoomMembers.Count(m => m.RoomId == x.r.Id),
                x.m.Role, x.m.JoinedAt,
            })
            .ToListAsync(ct);

        return items.Select(i => new MyRoomItemOutcome(
            i.Id, i.Name, i.Description, i.Visibility, i.Capacity, i.CreatedAt,
            i.MemberCount, i.Role, i.JoinedAt, LogoUrlFor(i.Id, i.LogoPath))).ToList();
    }

    public async Task<(bool Ok, string? Code, string? Message, RoomDetailOutcome? Value)> GetAsync(
        Guid me, Guid roomId, CancellationToken ct = default)
    {
        var room = await db.Rooms.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roomId, ct);
        if (room is null || room.DeletedAt != null)
        {
            return (false, RoomsErrors.RoomNotFound, "Room not found.", null);
        }

        var myMember = await db.RoomMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.RoomId == roomId && m.UserId == me, ct);
        if (myMember is null)
        {
            return (false, RoomsErrors.NotAMember, "You are not a member of this room.", null);
        }

        return (true, null, null, await BuildDetailAsync(room, me, ct));
    }

    public async Task<(bool Ok, string? Code, string? Message, RoomDetailOutcome? Value)> JoinAsync(
        Guid me, Guid roomId, CancellationToken ct = default)
    {
        var room = await db.Rooms.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roomId, ct);
        if (room is null || room.DeletedAt != null)
        {
            return (false, RoomsErrors.RoomNotFound, "Room not found.", null);
        }

        if (room.Visibility == RoomVisibility.Private)
        {
            // Private rooms: join via InvitationService.AcceptAsync.
            return (false, RoomsErrors.RoomIsPrivate, "This room is private. Join via invitation.", null);
        }

        var isBanned = await db.RoomBans.AnyAsync(
            rb => rb.RoomId == roomId && rb.UserId == me && rb.LiftedAt == null, ct);
        if (isBanned)
        {
            return (false, RoomsErrors.RoomBanned, "You are banned from this room.", null);
        }

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        db.RoomMembers.Add(new RoomMember
        {
            RoomId = roomId,
            UserId = me,
            Role = RoomRole.Member,
            JoinedAt = DateTimeOffset.UtcNow,
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == "23505")
        {
            await tx.RollbackAsync(ct);
            return (false, RoomsErrors.AlreadyMember, "You are already a member of this room.", null);
        }

        var count = await db.RoomMembers.CountAsync(m => m.RoomId == roomId, ct);
        if (count > room.Capacity)
        {
            await tx.RollbackAsync(ct);
            return (false, RoomsErrors.RoomFull, "This room is at capacity.", null);
        }

        await tx.CommitAsync(ct);
        return (true, null, null, await BuildDetailAsync(room, me, ct));
    }

    public async Task<(bool Ok, string? Code, string? Message)> LeaveAsync(
        Guid me, Guid roomId, CancellationToken ct = default)
    {
        var myMember = await db.RoomMembers
            .FirstOrDefaultAsync(m => m.RoomId == roomId && m.UserId == me, ct);
        if (myMember is null)
        {
            return (false, RoomsErrors.RoomNotFound, "Room not found or you are not a member.");
        }

        if (myMember.Role == RoomRole.Owner)
        {
            return (false, RoomsErrors.OwnerCannotLeave, "Owners cannot leave. You may only delete the room.");
        }

        db.RoomMembers.Remove(myMember);
        await db.SaveChangesAsync(ct);
        return (true, null, null);
    }

    public async Task<(bool Ok, string? Code, string? Message)> DeleteAsync(
        Guid actorId, Guid roomId, CancellationToken ct = default)
    {
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId, ct);
        if (room is null || room.DeletedAt != null)
        {
            return (false, RoomsErrors.RoomNotFound, "Room not found.");
        }

        if (room.OwnerId != actorId)
        {
            return (false, RoomsErrors.NotOwner, "Only the owner can delete the room.");
        }

        var memberIds = await db.RoomMembers.AsNoTracking()
            .Where(m => m.RoomId == roomId)
            .Select(m => m.UserId)
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        room.DeletedAt = now;
        db.ModerationAudits.Add(new ModerationAudit
        {
            Id = Guid.NewGuid(),
            RoomId = roomId,
            ActorId = actorId,
            Action = ModerationActions.RoomDelete,
            CreatedAt = now,
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await broadcaster.BroadcastRoomDeletedAsync(roomId, new RoomDeletedPayload(roomId), ct);

        foreach (var memberId in memberIds)
        {
            foreach (var connId in presenceStore.GetConnectionIds(memberId))
            {
                await broadcaster.RemoveConnectionFromRoomAsync(connId, roomId, ct);
            }
        }

        // TODO(slice-15): purge messages + attachment files for soft-deleted rooms via background job.

        return (true, null, null);
    }

    public async Task<(bool Ok, string? Code, string? Message, RoomDetailOutcome? Value)> UpdateAsync(
        Guid actorId, Guid roomId, string? rawName, string? rawDescription, string? rawVisibility,
        CancellationToken ct = default)
    {
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId, ct);
        if (room is null || room.DeletedAt != null)
        {
            return (false, RoomsErrors.RoomNotFound, "Room not found.", null);
        }

        var actorMember = await db.RoomMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.RoomId == roomId && m.UserId == actorId, ct);
        if (actorMember is null || actorMember.Role < RoomRole.Admin)
        {
            return (false, RoomsErrors.NotAdminOrOwner, "You must be an admin or owner.", null);
        }

        if (rawName is not null)
        {
            var name = rawName.Trim();
            if (name.Length < 3 || name.Length > 40 || !NameRegex.IsMatch(name))
            {
                return (false, RoomsErrors.InvalidRoomName,
                    "Room name must be 3-40 characters, start and end with alphanumeric, and contain only letters, digits, spaces, underscores, and hyphens.", null);
            }
            var nameNormalized = name.ToLowerInvariant();
            var taken = await db.Rooms.AnyAsync(r => r.NameNormalized == nameNormalized && r.Id != roomId, ct);
            if (taken)
            {
                return (false, RoomsErrors.RoomNameTaken, "A room with this name already exists.", null);
            }
            room.Name = name;
            room.NameNormalized = nameNormalized;
        }

        if (rawDescription is not null)
        {
            var description = rawDescription.Trim();
            if (description.Length < 1 || description.Length > 200)
            {
                return (false, RoomsErrors.InvalidDescription,
                    "Description is required and must be 200 characters or fewer.", null);
            }
            room.Description = description;
        }

        if (rawVisibility is not null)
        {
            if (!TryParseVisibility(rawVisibility, out var visibility))
            {
                return (false, RoomsErrors.InvalidRoomName, "Visibility must be 'public' or 'private'.", null);
            }
            room.Visibility = visibility;
        }

        await db.SaveChangesAsync(ct);
        return (true, null, null, await BuildDetailAsync(room, actorId, ct));
    }

    public async Task<(bool Ok, string? Code, string? Message, RoomDetailOutcome? Value)> UpdateCapacityAsync(
        Guid actorId, Guid roomId, int capacity, CancellationToken ct = default)
    {
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId, ct);
        if (room is null || room.DeletedAt != null)
        {
            return (false, RoomsErrors.RoomNotFound, "Room not found.", null);
        }

        var actorMember = await db.RoomMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.RoomId == roomId && m.UserId == actorId, ct);
        if (actorMember is null || actorMember.Role < RoomRole.Admin)
        {
            return (false, RoomsErrors.NotAdminOrOwner, "You must be an admin or owner.", null);
        }

        if (capacity < 1)
        {
            return (false, RoomsErrors.InvalidCapacity, "Capacity must be at least 1.", null);
        }

        var memberCount = await db.RoomMembers.CountAsync(m => m.RoomId == roomId, ct);
        if (capacity < memberCount)
        {
            return (false, RoomsErrors.CapacityBelowPopulation,
                $"Capacity cannot be set below the current member count ({memberCount}).", null);
        }

        var oldCapacity = room.Capacity;
        var now = DateTimeOffset.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        room.Capacity = capacity;
        db.ModerationAudits.Add(new ModerationAudit
        {
            Id = Guid.NewGuid(),
            RoomId = roomId,
            ActorId = actorId,
            Action = ModerationActions.CapacityChange,
            Detail = System.Text.Json.JsonSerializer.Serialize(new { from = oldCapacity, to = capacity }),
            CreatedAt = now,
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return (true, null, null, await BuildDetailAsync(room, actorId, ct));
    }

    private async Task<RoomDetailOutcome> BuildDetailAsync(Room room, Guid me, CancellationToken ct)
    {
        var rows = await db.RoomMembers
            .AsNoTracking()
            .Where(m => m.RoomId == room.Id)
            .Join(db.Users, m => m.UserId, u => u.Id,
                (m, u) => new { u.Id, u.Username, u.DisplayName, u.AvatarPath, m.Role, m.JoinedAt })
            .OrderByDescending(x => x.Role)
            .ThenBy(x => x.DisplayName)
            .ToListAsync(ct);

        var myRole = rows.FirstOrDefault(r => r.Id == me)?.Role ?? RoomRole.Member;

        var memberRows = rows.Select(r => new RoomMemberRow(
            r.Id, r.Username, r.DisplayName,
            r.AvatarPath is null ? null : $"/api/profile/avatar/{r.Id}",
            r.Role, r.JoinedAt)).ToList();

        return new RoomDetailOutcome(
            room.Id, room.Name, room.Description, room.Visibility,
            room.OwnerId, room.Capacity, room.CreatedAt,
            rows.Count, myRole, memberRows, LogoUrlFor(room.Id, room.LogoPath));
    }

    private static string? LogoUrlFor(Guid roomId, string? logoPath) =>
        logoPath is null ? null : $"/api/rooms/{roomId}/logo";

    public async Task<(bool Ok, string? Code, string? Message, RoomDetailOutcome? Value)> SetLogoAsync(
        Guid actorId, Guid roomId, Stream source, string filesRoot, CancellationToken ct = default)
    {
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId, ct);
        if (room is null || room.DeletedAt != null)
        {
            return (false, RoomsErrors.RoomNotFound, "Room not found.", null);
        }

        if (!await permissions.IsAdminOrOwnerAsync(roomId, actorId, ct))
        {
            return (false, RoomsErrors.NotAdminOrOwner, "You must be an admin or owner.", null);
        }

        if (!await ImageMagicBytes.IsSupportedImageAsync(source, ct))
        {
            return (false, "unsupported_media_type", "File type not supported.", null);
        }

        var logosDir = Path.Combine(filesRoot, "room-logos");
        Directory.CreateDirectory(logosDir);

        var tmpPath = Path.Combine(logosDir, $"{roomId}.webp.tmp");
        var finalPath = Path.Combine(logosDir, $"{roomId}.webp");

        try
        {
            await using (var tmp = File.Create(tmpPath))
            {
                await imageProcessor.EncodeAsync(source, tmp, ct);
            }
            File.Move(tmpPath, finalPath, overwrite: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Room logo encode failed for room {RoomId}", roomId);
            try { File.Delete(tmpPath); } catch { /* best effort */ }
            return (false, "invalid_image", "Image could not be processed.", null);
        }

        room.LogoPath = $"room-logos/{roomId}.webp";
        await db.SaveChangesAsync(ct);

        return (true, null, null, await BuildDetailAsync(room, actorId, ct));
    }

    public async Task<(bool Ok, string? Code, string? Message, RoomDetailOutcome? Value)> ClearLogoAsync(
        Guid actorId, Guid roomId, string filesRoot, CancellationToken ct = default)
    {
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId, ct);
        if (room is null || room.DeletedAt != null)
        {
            return (false, RoomsErrors.RoomNotFound, "Room not found.", null);
        }

        if (!await permissions.IsAdminOrOwnerAsync(roomId, actorId, ct))
        {
            return (false, RoomsErrors.NotAdminOrOwner, "You must be an admin or owner.", null);
        }

        room.LogoPath = null;
        await db.SaveChangesAsync(ct);

        var filePath = Path.Combine(filesRoot, "room-logos", $"{roomId}.webp");
        try
        {
            File.Delete(filePath);
        }
        catch (FileNotFoundException)
        {
            // already gone
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Best-effort room logo file delete failed for room {RoomId}", roomId);
        }

        return (true, null, null, await BuildDetailAsync(room, actorId, ct));
    }

    public async Task<(string? LogoPath, bool Found)> GetLogoPathAsync(Guid roomId, CancellationToken ct = default)
    {
        var row = await db.Rooms.AsNoTracking()
            .Where(r => r.Id == roomId && r.DeletedAt == null)
            .Select(r => new { r.LogoPath })
            .FirstOrDefaultAsync(ct);
        return (row?.LogoPath, row is not null);
    }

    private static bool TryParseVisibility(string? raw, out RoomVisibility visibility)
    {
        switch (raw?.Trim().ToLowerInvariant())
        {
            case "public":
                visibility = RoomVisibility.Public;
                return true;
            case "private":
                visibility = RoomVisibility.Private;
                return true;
            default:
                visibility = default;
                return false;
        }
    }
}

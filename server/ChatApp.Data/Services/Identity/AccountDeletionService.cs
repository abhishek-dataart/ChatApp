using ChatApp.Data.Entities.Identity;
using ChatApp.Domain.Abstractions;
using ChatApp.Domain.Services.Attachments;
using ChatApp.Domain.Services.Identity;
using ChatApp.Domain.Services.Rooms;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChatApp.Data.Services.Identity;

public interface IAccountDeletionService
{
    Task<(bool Ok, string? Code, string? Message)> DeleteAccountAsync(
        Guid userId, string passwordConfirmation, CancellationToken ct = default);
}

public class AccountDeletionService(
    ChatDbContext db,
    IPasswordHasher<User> hasher,
    IChatBroadcaster broadcaster,
    IOptions<AttachmentsOptions> attachmentsOptions) : IAccountDeletionService
{
    public async Task<(bool Ok, string? Code, string? Message)> DeleteAccountAsync(
        Guid userId, string passwordConfirmation, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.DeletedAt == null, ct);
        if (user is null)
        {
            return (false, AuthErrors.InvalidCredentials, "User not found.");
        }

        var verify = hasher.VerifyHashedPassword(user, user.PasswordHash, passwordConfirmation ?? string.Empty);
        if (verify == PasswordVerificationResult.Failed)
        {
            return (false, AuthErrors.InvalidCurrentPassword, "Incorrect password.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var ownedRooms = await db.Rooms
            .Where(r => r.OwnerId == userId && r.DeletedAt == null)
            .ToListAsync(ct);

        var purgedRoomIds = new List<Guid>();

        foreach (var room in ownedRooms)
        {
            var attachments = await db.Attachments
                .Where(a => db.Messages
                    .Where(m => m.RoomId == room.Id)
                    .Select(m => m.Id)
                    .Contains(a.MessageId!.Value))
                .ToListAsync(ct);

            var filesRoot = attachmentsOptions.Value.FilesRoot;
            foreach (var att in attachments)
            {
                DeleteFileIfExists(Path.Combine(filesRoot, att.StoredPath));
                if (att.ThumbPath is not null)
                    DeleteFileIfExists(Path.Combine(filesRoot, att.ThumbPath));
            }

            db.Attachments.RemoveRange(attachments);

            var messages = await db.Messages.Where(m => m.RoomId == room.Id).ToListAsync(ct);
            db.Messages.RemoveRange(messages);

            room.DeletedAt = DateTimeOffset.UtcNow;
            purgedRoomIds.Add(room.Id);
        }

        var now = DateTimeOffset.UtcNow;
        await db.Sessions
            .Where(s => s.UserId == userId && s.RevokedAt == null)
            .ExecuteUpdateAsync(upd => upd.SetProperty(s => s.RevokedAt, now), ct);

        db.Users.Remove(user);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        foreach (var roomId in purgedRoomIds)
        {
            await broadcaster.BroadcastRoomDeletedAsync(roomId, new RoomDeletedPayload(roomId), ct);
        }

        await broadcaster.BroadcastUserDeletedAsync(userId, new UserDeletedPayload(userId), ct);

        return (true, null, null);
    }

    private static void DeleteFileIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // best-effort; log elsewhere
        }
    }
}

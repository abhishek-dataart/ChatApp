using ChatApp.Domain.Services.Attachments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChatApp.Data.Services.Rooms;

public sealed class RoomPurgeService(
    ChatDbContext db,
    IOptions<AttachmentsOptions> attachmentOptions,
    ILogger<RoomPurgeService> log)
{
    public async Task<int> PurgeOnceAsync(TimeSpan minAge, CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow - minAge;

        var roomIds = await db.Rooms.AsNoTracking()
            .Where(r => r.DeletedAt != null && r.DeletedAt < cutoff)
            .Select(r => r.Id)
            .ToListAsync(ct);

        var filesRoot = attachmentOptions.Value.FilesRoot;
        var purged = 0;

        foreach (var roomId in roomIds)
        {
            try
            {
                var attachments = await db.Attachments.AsNoTracking()
                    .Where(a => a.Message != null && a.Message.RoomId == roomId)
                    .Select(a => new { a.StoredPath, a.ThumbPath })
                    .ToListAsync(ct);

                foreach (var att in attachments)
                {
                    TryDeleteFile(Path.Combine(filesRoot, att.StoredPath));
                    if (att.ThumbPath is not null)
                    {
                        TryDeleteFile(Path.Combine(filesRoot, att.ThumbPath));
                    }
                }

                // FK Attachment.MessageId -> Message has ON DELETE CASCADE, so deleting messages
                // also removes their attachment rows in the same statement.
                var deleted = await db.Messages
                    .Where(m => m.RoomId == roomId)
                    .ExecuteDeleteAsync(ct);

                purged++;
                log.LogInformation(
                    "Purged {MessageCount} messages and {AttachmentCount} attachment files for soft-deleted room {RoomId}.",
                    deleted, attachments.Count, roomId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Failed to purge soft-deleted room {RoomId}.", roomId);
            }
        }

        return purged;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // best effort
        }
    }
}

using ChatApp.Data.Entities.Messaging;
using ChatApp.Domain.Abstractions;
using ChatApp.Domain.Entities;
using ChatApp.Domain.Services.Attachments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChatApp.Data.Services.Attachments;

public class AttachmentService(
    ChatDbContext db,
    IAttachmentScanner scanner,
    IAttachmentImageProcessor imageProcessor,
    IOptions<AttachmentsOptions> options)
{
    private readonly AttachmentsOptions _opts = options.Value;

    private static readonly HashSet<string> ImageMimes =
        new(StringComparer.OrdinalIgnoreCase) { "image/png", "image/jpeg", "image/gif", "image/webp" };

    public async Task<(bool Ok, string? Code, string? Message, AttachmentUploadResult? Value)> UploadAsync(
        Guid uploaderId, string kindRaw, string claimedMime, string originalFilename,
        long size, string? comment, Stream content, CancellationToken ct)
    {
        if (!Enum.TryParse<AttachmentKind>(kindRaw, ignoreCase: true, out var kind))
        {
            return Fail(AttachmentsErrors.UnsupportedKind, "Unsupported attachment kind.");
        }

        var cap = kind == AttachmentKind.Image ? _opts.MaxImageBytes : _opts.MaxFileBytes;
        if (size > cap)
        {
            return Fail(AttachmentsErrors.SizeExceeded, $"File exceeds the {cap}-byte limit.");
        }

        if (comment is { Length: > 500 })
        {
            return Fail(AttachmentsErrors.CommentTooLong, "Comment must be 500 characters or fewer.");
        }

        var tmpPath = Path.Combine(Path.GetTempPath(), $"upload-{Guid.NewGuid():N}");
        await using (var tmp = File.Create(tmpPath))
        {
            await content.CopyToAsync(tmp, ct);
        }

        try
        {
            await using var sniffStream = File.OpenRead(tmpPath);
            var sniffed = MagicBytes.Detect(sniffStream);

            if (kind == AttachmentKind.Image)
            {
                if (sniffed is null || !ImageMimes.Contains(sniffed.Mime) ||
                    !string.Equals(claimedMime, sniffed.Mime, StringComparison.OrdinalIgnoreCase))
                {
                    return Fail(AttachmentsErrors.MimeMismatch, "File content does not match the claimed MIME type.");
                }
            }

            var finalMime = sniffed?.Mime ?? "application/octet-stream";
            var finalExt = sniffed?.Extension ?? SafeExtFrom(originalFilename);

            await using var scanStream = File.OpenRead(tmpPath);
            var scan = await scanner.ScanAsync(scanStream, finalMime, ct);
            if (scan is AttachmentScanResult.Infected)
            {
                return Fail(AttachmentsErrors.ScannerRejected, "File was rejected by the content scanner.");
            }

            var id = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            var relDir = Path.Combine("attachments", now.ToString("yyyy"), now.ToString("MM"));
            Directory.CreateDirectory(Path.Combine(_opts.FilesRoot, relDir));
            var relPath = Path.Combine(relDir, $"{id:N}{finalExt}");
            var absPath = Path.Combine(_opts.FilesRoot, relPath);

            if (kind == AttachmentKind.Image)
            {
                byte[] sanitized;
                try
                {
                    await using var sanitizeStream = File.OpenRead(tmpPath);
                    sanitized = await imageProcessor.SanitizeAsync(sanitizeStream, finalMime, ct);
                }
                catch (Exception)
                {
                    return Fail(AttachmentsErrors.MimeMismatch, "File content does not match the claimed MIME type.");
                }
                await File.WriteAllBytesAsync(absPath, sanitized, ct);
                File.Delete(tmpPath);
                tmpPath = null;
            }
            else
            {
                File.Move(tmpPath, absPath);
                tmpPath = null;
            }

            string? thumbRelPath = null;
            if (kind == AttachmentKind.Image)
            {
                await using var src = File.OpenRead(absPath);
                var thumbBytes = await imageProcessor.CreateThumbAsync(src, ct);
                thumbRelPath = relPath + ".thumb.jpg";
                await File.WriteAllBytesAsync(Path.Combine(_opts.FilesRoot, thumbRelPath), thumbBytes, ct);
            }

            var row = new Attachment
            {
                Id = id,
                UploaderId = uploaderId,
                Kind = kind,
                OriginalFilename = SafeName(originalFilename),
                StoredPath = relPath,
                ThumbPath = thumbRelPath,
                Mime = finalMime,
                SizeBytes = size,
                Comment = comment,
                CreatedAt = now,
                ScannedAt = now,
            };
            db.Attachments.Add(row);
            await db.SaveChangesAsync(ct);

            return (true, null, null, new AttachmentUploadResult(
                row.Id, kind, row.OriginalFilename, row.Mime,
                row.SizeBytes, row.Comment, thumbRelPath is not null, row.CreatedAt));
        }
        finally
        {
            if (tmpPath is not null && File.Exists(tmpPath))
            {
                File.Delete(tmpPath);
            }
        }
    }

    public async Task<(bool Ok, string? Code, List<Attachment>? Rows)> ValidateForLinkAsync(
        Guid uploaderId, IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return (true, null, []);
        }

        if (ids.Count > _opts.MaxPerMessage)
        {
            return (false, AttachmentsErrors.TooManyAttachments, null);
        }

        var rows = await db.Attachments.Where(a => ids.Contains(a.Id)).ToListAsync(ct);
        if (rows.Count != ids.Count)
        {
            return (false, AttachmentsErrors.AttachmentNotFound, null);
        }

        if (rows.Any(a => a.UploaderId != uploaderId))
        {
            return (false, AttachmentsErrors.NotUploader, null);
        }

        if (rows.Any(a => a.MessageId is not null))
        {
            return (false, AttachmentsErrors.AttachmentAlreadyLinked, null);
        }

        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-_opts.UnlinkedTtlMinutes);
        if (rows.Any(a => a.CreatedAt < cutoff))
        {
            return (false, AttachmentsErrors.AttachmentExpired, null);
        }

        return (true, null, rows);
    }

    public async Task<(bool Ok, string? Code, (Stream Stream, string Mime, string Filename)? Value)> OpenOriginalAsync(
        Guid callerId, Guid attachmentId, CancellationToken ct)
    {
        var attachment = await db.Attachments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == attachmentId, ct);
        if (attachment is null)
        {
            return (false, AttachmentsErrors.AttachmentNotFound, null);
        }

        if (!await AuthorizeReadAsync(callerId, attachment, ct))
        {
            return (false, AttachmentsErrors.NotAuthorized, null);
        }

        var absPath = Path.Combine(_opts.FilesRoot, attachment.StoredPath);
        if (!File.Exists(absPath))
        {
            return (false, AttachmentsErrors.AttachmentNotFound, null);
        }

        return (true, null, (File.OpenRead(absPath), attachment.Mime, attachment.OriginalFilename));
    }

    public async Task<(bool Ok, string? Code, Stream? Value)> OpenThumbAsync(
        Guid callerId, Guid attachmentId, CancellationToken ct)
    {
        var attachment = await db.Attachments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == attachmentId, ct);
        if (attachment is null || attachment.ThumbPath is null)
        {
            return (false, AttachmentsErrors.AttachmentNotFound, null);
        }

        if (!await AuthorizeReadAsync(callerId, attachment, ct))
        {
            return (false, AttachmentsErrors.NotAuthorized, null);
        }

        var absPath = Path.Combine(_opts.FilesRoot, attachment.ThumbPath);
        if (!File.Exists(absPath))
        {
            return (false, AttachmentsErrors.AttachmentNotFound, null);
        }

        return (true, null, File.OpenRead(absPath));
    }

    public async Task PurgeOnceAsync(CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-_opts.UnlinkedTtlMinutes);
        var orphans = await db.Attachments.AsNoTracking()
            .Where(a => a.MessageId == null && a.CreatedAt < cutoff)
            .Select(a => new { a.Id, a.StoredPath, a.ThumbPath })
            .ToListAsync(ct);

        if (orphans.Count == 0)
        {
            return;
        }

        foreach (var orphan in orphans)
        {
            try
            {
                TryDeleteFile(Path.Combine(_opts.FilesRoot, orphan.StoredPath));
                if (orphan.ThumbPath is not null)
                {
                    TryDeleteFile(Path.Combine(_opts.FilesRoot, orphan.ThumbPath));
                }
            }
            catch (Exception)
            {
                // best-effort file removal; DB row is purged below regardless
            }
        }

        var ids = orphans.Select(o => o.Id).ToList();
        await db.Attachments.Where(a => ids.Contains(a.Id)).ExecuteDeleteAsync(ct);
    }

    private async Task<bool> AuthorizeReadAsync(Guid callerId, Attachment attachment, CancellationToken ct)
    {
        if (attachment.MessageId is null)
        {
            return attachment.UploaderId == callerId;
        }

        var msg = await db.Messages.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == attachment.MessageId, ct);
        if (msg is null)
        {
            return false;
        }

        if (msg.Scope == MessageScope.Personal)
        {
            var chat = await db.PersonalChats.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == msg.PersonalChatId, ct);
            return chat is not null && (chat.UserAId == callerId || chat.UserBId == callerId);
        }

        return await db.RoomMembers.AnyAsync(
            m => m.RoomId == msg.RoomId && m.UserId == callerId, ct);
    }

    private static void TryDeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static (bool Ok, string? Code, string? Msg, AttachmentUploadResult? Value) Fail(
        string code, string message) =>
        (false, code, message, null);

    private static string SafeExtFrom(string filename)
    {
        var ext = Path.GetExtension(filename);
        return ext is { Length: > 0 and <= 8 } ? ext.ToLowerInvariant() : string.Empty;
    }

    private static string SafeName(string filename)
    {
        var name = Path.GetFileName(filename);
        return name.Length > 255 ? name[..255] : name;
    }
}

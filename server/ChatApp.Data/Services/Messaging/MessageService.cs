using System.Text;
using ChatApp.Data.Entities.Messaging;
using ChatApp.Data.Services.Attachments;
using ChatApp.Data.Services.Rooms;
using ChatApp.Data.Services.Social;
using ChatApp.Domain.Abstractions;
using ChatApp.Domain.Entities;
using ChatApp.Domain.Services.Attachments;
using ChatApp.Domain.Services.Messaging;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Data.Services.Messaging;

public record MessageHistoryCursor(DateTimeOffset CreatedAt, Guid Id);

public class MessageService(
    ChatDbContext db,
    IChatBroadcaster broadcaster,
    RoomPermissionService roomPermissions,
    UnreadService unread,
    AttachmentService attachments,
    UserBanService userBans)
{
    private const int MaxBodyBytes = 3000;
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 100;

    public async Task<(bool Ok, string? Code, string? Message, MessagePayload? Value)> SendAsync(
        Guid me, Guid personalChatId, string body, Guid? replyToId,
        IReadOnlyList<Guid>? attachmentIds = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return (false, MessagingErrors.BodyEmpty, "Message body cannot be empty.", null);
        }

        if (Encoding.UTF8.GetByteCount(body) > MaxBodyBytes)
        {
            return (false, MessagingErrors.BodyTooLong, "Message exceeds 3 KB.", null);
        }

        var chat = await db.PersonalChats.AsNoTracking().FirstOrDefaultAsync(p => p.Id == personalChatId, ct);
        if (chat is null)
        {
            return (false, MessagingErrors.ChatNotFound, "Chat not found.", null);
        }

        if (chat.UserAId != me && chat.UserBId != me)
        {
            return (false, MessagingErrors.NotParticipant, "You are not a participant in this chat.", null);
        }

        var partner = chat.UserAId == me ? chat.UserBId : chat.UserAId;
        if (await userBans.IsActiveAnyDirectionAsync(me, partner, ct))
        {
            return (false, MessagingErrors.UserBanned, "You cannot message this user.", null);
        }

        var ids = attachmentIds ?? [];
        var (attOk, attCode, attRows) = await attachments.ValidateForLinkAsync(me, ids, ct);
        if (!attOk)
        {
            return (false, attCode, attCode, null);
        }

        var message = new Message
        {
            Id = Guid.NewGuid(),
            Scope = MessageScope.Personal,
            PersonalChatId = personalChatId,
            AuthorId = me,
            Body = body,
            ReplyToId = replyToId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Messages.Add(message);

        if (attRows is { Count: > 0 })
        {
            foreach (var row in attRows)
            {
                row.MessageId = message.Id;
            }
        }

        await db.SaveChangesAsync(ct);

        var payload = await BuildPayloadAsync(message, ct);
        await broadcaster.BroadcastMessageCreatedToPersonalChatAsync(personalChatId, payload, ct);
        await unread.IncrementAsync(me, MessageScope.Personal, personalChatId, ct);
        return (true, null, null, payload);
    }

    public async Task<(bool Ok, string? Code, string? Message, List<MessagePayload>? Value)> GetHistoryAsync(
        Guid me, Guid personalChatId, MessageHistoryCursor? cursor = null, int limit = DefaultPageSize, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, MaxPageSize);

        var chat = await db.PersonalChats.AsNoTracking().FirstOrDefaultAsync(p => p.Id == personalChatId, ct);
        if (chat is null)
        {
            return (false, MessagingErrors.ChatNotFound, "Chat not found.", null);
        }

        if (chat.UserAId != me && chat.UserBId != me)
        {
            return (false, MessagingErrors.NotParticipant, "You are not a participant in this chat.", null);
        }

        var queryable = db.Messages.AsNoTracking()
            .Where(m => m.PersonalChatId == personalChatId && m.DeletedAt == null);

        if (cursor is not null)
            queryable = queryable.Where(m => m.CreatedAt < cursor.CreatedAt);

        var rows = await (
            from m in queryable
            join u in db.Users.AsNoTracking() on m.AuthorId equals u.Id into authorGroup
            from u in authorGroup.DefaultIfEmpty()
            orderby m.CreatedAt descending, m.Id descending
            select new
            {
                m.Id, m.PersonalChatId, m.AuthorId,
                Username = u != null ? u.Username : "deleted_user",
                DisplayName = u != null ? u.DisplayName : "Deleted user",
                AvatarPath = u != null ? u.AvatarPath : null,
                m.Body, m.ReplyToId, m.CreatedAt, m.EditedAt,
            }).Take(limit).ToListAsync(ct);

        rows.Reverse();

        var messageIds = rows.Select(r => r.Id).ToList();
        var attachmentsByMsg = await db.Attachments.AsNoTracking()
            .Where(a => a.MessageId != null && messageIds.Contains(a.MessageId!.Value))
            .ToListAsync(ct);
        var attachmentsLookup = attachmentsByMsg.ToLookup(a => a.MessageId!.Value);

        var replyLookup = await LoadReplyParentsAsync(rows.Select(r => r.ReplyToId), ct);

        var payloads = rows.Select(r =>
        {
            var reply = r.ReplyToId is { } rid && replyLookup.TryGetValue(rid, out var rp) ? rp : default;
            return new MessagePayload(
                r.Id, "personal", r.PersonalChatId, null, r.AuthorId,
                r.Username, r.DisplayName,
                r.AvatarPath is null ? null : $"/api/profile/avatar/{r.AuthorId}",
                r.Body, r.ReplyToId, r.CreatedAt, r.EditedAt,
                reply.Body, reply.AuthorDisplayName,
                ToSummaries(attachmentsLookup[r.Id]));
        }).ToList();

        return (true, null, null, payloads);
    }

    public async Task<(bool Ok, string? Code, string? Message, MessagePayload? Value)> SendToRoomAsync(
        Guid me, Guid roomId, string body, Guid? replyToId,
        IReadOnlyList<Guid>? attachmentIds = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return (false, MessagingErrors.BodyEmpty, "Message body cannot be empty.", null);
        }

        if (Encoding.UTF8.GetByteCount(body) > MaxBodyBytes)
        {
            return (false, MessagingErrors.BodyTooLong, "Message exceeds 3 KB.", null);
        }

        var roomExists = await db.Rooms.AnyAsync(r => r.Id == roomId && r.DeletedAt == null, ct);
        if (!roomExists)
        {
            return (false, MessagingErrors.RoomNotFound, "Room not found.", null);
        }

        var isMember = await roomPermissions.IsMemberAsync(roomId, me, ct);
        if (!isMember)
        {
            return (false, MessagingErrors.NotMember, "You are not a member of this room.", null);
        }

        var ids = attachmentIds ?? [];
        var (attOk, attCode, attRows) = await attachments.ValidateForLinkAsync(me, ids, ct);
        if (!attOk)
        {
            return (false, attCode, attCode, null);
        }

        var message = new Message
        {
            Id = Guid.NewGuid(),
            Scope = MessageScope.Room,
            RoomId = roomId,
            PersonalChatId = null,
            AuthorId = me,
            Body = body,
            ReplyToId = replyToId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Messages.Add(message);

        if (attRows is { Count: > 0 })
        {
            foreach (var row in attRows)
            {
                row.MessageId = message.Id;
            }
        }

        await db.SaveChangesAsync(ct);

        var payload = await BuildPayloadAsync(message, ct);
        await broadcaster.BroadcastMessageCreatedToRoomAsync(roomId, payload, ct);
        await unread.IncrementAsync(me, MessageScope.Room, roomId, ct);
        return (true, null, null, payload);
    }

    public async Task<(bool Ok, string? Code, string? Message, List<MessagePayload>? Value)> GetRoomHistoryAsync(
        Guid me, Guid roomId, MessageHistoryCursor? cursor = null, int limit = DefaultPageSize, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, MaxPageSize);

        var roomExists = await db.Rooms.AnyAsync(r => r.Id == roomId && r.DeletedAt == null, ct);
        if (!roomExists)
        {
            return (false, MessagingErrors.RoomNotFound, "Room not found.", null);
        }

        var isMember = await roomPermissions.IsMemberAsync(roomId, me, ct);
        if (!isMember)
        {
            return (false, MessagingErrors.NotMember, "You are not a member of this room.", null);
        }

        var queryable = db.Messages.AsNoTracking()
            .Where(m => m.RoomId == roomId && m.DeletedAt == null);

        if (cursor is not null)
            queryable = queryable.Where(m => m.CreatedAt < cursor.CreatedAt);

        var rows = await (
            from m in queryable
            join u in db.Users.AsNoTracking() on m.AuthorId equals u.Id into authorGroup
            from u in authorGroup.DefaultIfEmpty()
            orderby m.CreatedAt descending, m.Id descending
            select new
            {
                m.Id, m.RoomId, m.AuthorId,
                Username = u != null ? u.Username : "deleted_user",
                DisplayName = u != null ? u.DisplayName : "Deleted user",
                AvatarPath = u != null ? u.AvatarPath : null,
                m.Body, m.ReplyToId, m.CreatedAt, m.EditedAt,
            }).Take(limit).ToListAsync(ct);

        rows.Reverse();

        var messageIds = rows.Select(r => r.Id).ToList();
        var attachmentsByMsg = await db.Attachments.AsNoTracking()
            .Where(a => a.MessageId != null && messageIds.Contains(a.MessageId!.Value))
            .ToListAsync(ct);
        var attachmentsLookup = attachmentsByMsg.ToLookup(a => a.MessageId!.Value);

        var replyLookup = await LoadReplyParentsAsync(rows.Select(r => r.ReplyToId), ct);

        var payloads = rows.Select(r =>
        {
            var reply = r.ReplyToId is { } rid && replyLookup.TryGetValue(rid, out var rp) ? rp : default;
            return new MessagePayload(
                r.Id, "room", null, r.RoomId, r.AuthorId,
                r.Username, r.DisplayName,
                r.AvatarPath is null ? null : $"/api/profile/avatar/{r.AuthorId}",
                r.Body, r.ReplyToId, r.CreatedAt, r.EditedAt,
                reply.Body, reply.AuthorDisplayName,
                ToSummaries(attachmentsLookup[r.Id]));
        }).ToList();

        return (true, null, null, payloads);
    }

    private async Task<Dictionary<Guid, (string? Body, string? AuthorDisplayName)>> LoadReplyParentsAsync(
        IEnumerable<Guid?> replyToIds, CancellationToken ct)
    {
        var parentIds = replyToIds.Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        if (parentIds.Count == 0)
        {
            return new Dictionary<Guid, (string?, string?)>();
        }

        var parents = await (
            from p in db.Messages.AsNoTracking()
            where parentIds.Contains(p.Id) && p.DeletedAt == null
            join pu in db.Users.AsNoTracking() on p.AuthorId equals pu.Id into pg
            from pu in pg.DefaultIfEmpty()
            select new
            {
                p.Id, p.Body,
                AuthorDisplayName = pu != null ? pu.DisplayName : "Deleted user",
            }).ToListAsync(ct);

        return parents.ToDictionary(
            p => p.Id,
            p => ((string?)p.Body, (string?)p.AuthorDisplayName));
    }

    public async Task<(bool Ok, string? Code, string? Message, MessagePayload? Value)> EditAsync(
        Guid editorId, Guid messageId, string body, CancellationToken ct = default)
    {
        body = body.Trim();
        if (string.IsNullOrEmpty(body))
        {
            return (false, MessagingErrors.BodyEmpty, "Message body cannot be empty.", null);
        }

        if (Encoding.UTF8.GetByteCount(body) > MaxBodyBytes)
        {
            return (false, MessagingErrors.BodyTooLong, "Message exceeds 3 KB.", null);
        }

        var msg = await db.Messages.FirstOrDefaultAsync(m => m.Id == messageId, ct);
        if (msg is null)
        {
            return (false, MessagingErrors.MessageNotFound, "Message not found.", null);
        }

        if (msg.DeletedAt is not null)
        {
            return (false, MessagingErrors.MessageAlreadyDeleted, "Message has been deleted.", null);
        }

        if (msg.AuthorId != editorId)
        {
            return (false, MessagingErrors.NotAuthor, "You are not the author of this message.", null);
        }

        msg.Body = body;
        msg.EditedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var payload = await BuildPayloadAsync(msg, ct);
        if (msg.Scope == MessageScope.Personal)
        {
            await broadcaster.BroadcastMessageEditedToPersonalChatAsync(msg.PersonalChatId!.Value, payload, ct);
        }
        else
        {
            await broadcaster.BroadcastMessageEditedToRoomAsync(msg.RoomId!.Value, payload, ct);
        }

        return (true, null, null, payload);
    }

    public async Task<(bool Ok, string? Code, string? Message)> DeleteAsync(
        Guid deleterId, Guid messageId, CancellationToken ct = default)
    {
        var msg = await db.Messages.FirstOrDefaultAsync(m => m.Id == messageId, ct);
        if (msg is null)
        {
            return (false, MessagingErrors.MessageNotFound, "Message not found.");
        }

        if (msg.DeletedAt is not null)
        {
            return (false, MessagingErrors.MessageAlreadyDeleted, "Message has been deleted.");
        }

        var authorized = msg.AuthorId == deleterId;
        if (!authorized && msg.Scope == MessageScope.Room)
        {
            authorized = await roomPermissions.IsAdminOrOwnerAsync(msg.RoomId!.Value, deleterId, ct);
        }

        if (!authorized)
        {
            return (false, MessagingErrors.NotAuthorized, "You are not authorized to delete this message.");
        }

        msg.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var scopeStr = msg.Scope == MessageScope.Personal ? "personal" : "room";
        var deletedPayload = new MessageDeletedPayload(msg.Id, scopeStr, msg.PersonalChatId, msg.RoomId);
        if (msg.Scope == MessageScope.Personal)
        {
            await broadcaster.BroadcastMessageDeletedToPersonalChatAsync(msg.PersonalChatId!.Value, deletedPayload, ct);
        }
        else
        {
            await broadcaster.BroadcastMessageDeletedToRoomAsync(msg.RoomId!.Value, deletedPayload, ct);
        }

        return (true, null, null);
    }

    private async Task<MessagePayload> BuildPayloadAsync(Message msg, CancellationToken ct)
    {
        var author = msg.AuthorId.HasValue
            ? await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == msg.AuthorId.Value, ct)
            : null;
        var authorId = author?.Id;
        var authorUsername = author?.Username ?? "deleted_user";
        var authorDisplayName = author?.DisplayName ?? "Deleted user";
        var authorAvatarUrl = author?.AvatarPath is null ? null : $"/api/profile/avatar/{author.Id}";

        string? replyToBody = null;
        string? replyToAuthorDisplayName = null;
        if (msg.ReplyToId is not null)
        {
            var parent = await db.Messages.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == msg.ReplyToId && p.DeletedAt == null, ct);
            if (parent is not null)
            {
                replyToBody = parent.Body.Length > 200 ? parent.Body[..200] : parent.Body;
                var parentAuthor = parent.AuthorId.HasValue
                    ? await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == parent.AuthorId.Value, ct)
                    : null;
                replyToAuthorDisplayName = parentAuthor?.DisplayName ?? "Deleted user";
            }
        }

        var attachmentRows = await db.Attachments.AsNoTracking()
            .Where(a => a.MessageId == msg.Id)
            .ToListAsync(ct);

        return new MessagePayload(
            msg.Id,
            msg.Scope == MessageScope.Personal ? "personal" : "room",
            msg.PersonalChatId,
            msg.RoomId,
            authorId,
            authorUsername,
            authorDisplayName,
            authorAvatarUrl,
            msg.Body,
            msg.ReplyToId,
            msg.CreatedAt,
            msg.EditedAt,
            replyToBody,
            replyToAuthorDisplayName,
            ToSummaries(attachmentRows));
    }

    private static IReadOnlyList<AttachmentSummary> ToSummaries(
        IEnumerable<Attachment> rows) =>
        rows.Select(a => new AttachmentSummary(
            a.Id,
            a.Kind == AttachmentKind.Image ? "image" : "file",
            a.OriginalFilename,
            a.Mime,
            a.SizeBytes,
            a.Comment,
            a.ThumbPath is null ? null : $"/api/attachments/{a.Id}/thumb",
            $"/api/attachments/{a.Id}",
            a.CreatedAt)).ToList();
}

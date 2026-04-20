using ChatApp.Domain.Services.Attachments;

namespace ChatApp.Domain.Services.Messaging;

public sealed record MessagePayload(
    Guid Id,
    string Scope,
    Guid? PersonalChatId,
    Guid? RoomId,
    Guid? AuthorId,
    string AuthorUsername,
    string AuthorDisplayName,
    string? AuthorAvatarUrl,
    string Body,
    Guid? ReplyToId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EditedAt,
    string? ReplyToBody = null,
    string? ReplyToAuthorDisplayName = null,
    IReadOnlyList<AttachmentSummary>? Attachments = null);

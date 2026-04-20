using ChatApp.Domain.Services.Attachments;
using ChatApp.Domain.Services.Messaging;

namespace ChatApp.Api.Contracts.Messages;

public sealed record MessageResponse(
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
    IReadOnlyList<AttachmentSummary>? Attachments = null)
{
    public static MessageResponse From(MessagePayload p) => new(
        p.Id, p.Scope, p.PersonalChatId, p.RoomId, p.AuthorId, p.AuthorUsername, p.AuthorDisplayName,
        p.AuthorAvatarUrl, p.Body, p.ReplyToId, p.CreatedAt, p.EditedAt, p.ReplyToBody,
        p.ReplyToAuthorDisplayName, p.Attachments ?? []);
}

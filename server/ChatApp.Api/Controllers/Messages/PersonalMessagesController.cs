using ChatApp.Api.Contracts.Messages;
using ChatApp.Data;
using ChatApp.Data.Entities.Messaging;
using ChatApp.Data.Services.Messaging;
using ChatApp.Domain.Abstractions;
using ChatApp.Domain.Services.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Api.Controllers.Messages;

[ApiController]
[Route("api/chats/personal/{chatId:guid}/messages")]
[Authorize]
public class PersonalMessagesController(
    MessageService messages,
    UnreadService unread,
    ChatDbContext db,
    ICurrentUser current) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting("messages")]
    public async Task<IActionResult> Send(Guid chatId, [FromBody] SendMessageRequest body, CancellationToken ct)
    {
        var (ok, code, message, value) = await messages.SendAsync(current.Id, chatId, body.Body ?? string.Empty, body.ReplyToId, body.AttachmentIds, ct);
        if (!ok)
        {
            return FromError(code!, message);
        }

        return StatusCode(StatusCodes.Status201Created, MessageResponse.From(value!));
    }

    [HttpGet]
    public async Task<IActionResult> History(
        Guid chatId,
        [FromQuery] DateTimeOffset? beforeCreatedAt,
        [FromQuery] Guid? beforeId,
        [FromQuery] int? limit,
        CancellationToken ct)
    {
        if (beforeCreatedAt.HasValue != beforeId.HasValue)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "beforeCreatedAt and beforeId must be supplied together.");
        }

        MessageHistoryCursor? cursor = beforeCreatedAt.HasValue
            ? new MessageHistoryCursor(beforeCreatedAt.Value, beforeId!.Value)
            : null;

        var (ok, code, message, value) = await messages.GetHistoryAsync(
            current.Id, chatId, cursor, limit ?? 50, ct);
        if (!ok)
        {
            return FromError(code!, message);
        }

        return Ok(value!.Select(MessageResponse.From).ToList());
    }

    [HttpPost("/api/chats/personal/{chatId:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid chatId, CancellationToken ct)
    {
        var chat = await db.PersonalChats.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == chatId, ct);
        if (chat is null)
        {
            return FromError(MessagingErrors.ChatNotFound, "Chat not found.");
        }
        if (chat.UserAId != current.Id && chat.UserBId != current.Id)
        {
            return FromError(MessagingErrors.NotParticipant, "Not a participant.");
        }

        await unread.MarkReadAsync(current.Id, MessageScope.Personal, chatId, ct);
        return NoContent();
    }

    private IActionResult FromError(string code, string? message) => code switch
    {
        MessagingErrors.BodyEmpty => Problem(statusCode: StatusCodes.Status400BadRequest, title: message ?? "Body empty.", extensions: Ext(code)),
        MessagingErrors.BodyTooLong => Problem(statusCode: StatusCodes.Status400BadRequest, title: message ?? "Body too long.", extensions: Ext(code)),
        MessagingErrors.ChatNotFound => Problem(statusCode: StatusCodes.Status404NotFound, title: message ?? "Chat not found.", extensions: Ext(code)),
        MessagingErrors.NotParticipant => Problem(statusCode: StatusCodes.Status403Forbidden, title: message ?? "Not a participant.", extensions: Ext(code)),
        MessagingErrors.UserBanned => Problem(statusCode: StatusCodes.Status403Forbidden, title: message ?? "You cannot message this user.", extensions: Ext(code)),
        _ => Problem(statusCode: StatusCodes.Status400BadRequest, title: message ?? code, extensions: Ext(code)),
    };

    private static Dictionary<string, object?> Ext(string code) => new() { ["code"] = code };
}

using ChatApp.Api.Contracts.Messages;
using ChatApp.Data.Entities.Messaging;
using ChatApp.Data.Services.Messaging;
using ChatApp.Data.Services.Rooms;
using ChatApp.Domain.Abstractions;
using ChatApp.Domain.Services.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ChatApp.Api.Controllers.Messages;

[ApiController]
[Route("api/chats/room/{roomId:guid}/messages")]
[Authorize]
public class RoomMessagesController(
    MessageService messages,
    UnreadService unread,
    RoomPermissionService roomPermissions,
    ICurrentUser current) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting("messages")]
    public async Task<IActionResult> Send(Guid roomId, [FromBody] SendMessageRequest body, CancellationToken ct)
    {
        var (ok, code, message, value) = await messages.SendToRoomAsync(current.Id, roomId, body.Body ?? string.Empty, body.ReplyToId, body.AttachmentIds, ct);
        if (!ok)
        {
            return FromError(code!, message);
        }

        return StatusCode(StatusCodes.Status201Created, MessageResponse.From(value!));
    }

    [HttpGet]
    public async Task<IActionResult> History(
        Guid roomId,
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

        var (ok, code, message, value) = await messages.GetRoomHistoryAsync(
            current.Id, roomId, cursor, limit ?? 50, ct);
        if (!ok)
        {
            return FromError(code!, message);
        }

        return Ok(value!.Select(MessageResponse.From).ToList());
    }

    [HttpPost("/api/chats/room/{roomId:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid roomId, CancellationToken ct)
    {
        var isMember = await roomPermissions.IsMemberAsync(roomId, current.Id, ct);
        if (!isMember)
        {
            return FromError(MessagingErrors.NotMember, "Not a member.");
        }

        await unread.MarkReadAsync(current.Id, MessageScope.Room, roomId, ct);
        return NoContent();
    }

    private IActionResult FromError(string code, string? message) => code switch
    {
        MessagingErrors.BodyEmpty => Problem(statusCode: StatusCodes.Status400BadRequest, title: message ?? "Body empty.", extensions: Ext(code)),
        MessagingErrors.BodyTooLong => Problem(statusCode: StatusCodes.Status400BadRequest, title: message ?? "Body too long.", extensions: Ext(code)),
        MessagingErrors.RoomNotFound => Problem(statusCode: StatusCodes.Status404NotFound, title: message ?? "Room not found.", extensions: Ext(code)),
        MessagingErrors.NotMember => Problem(statusCode: StatusCodes.Status403Forbidden, title: message ?? "Not a member.", extensions: Ext(code)),
        _ => Problem(statusCode: StatusCodes.Status400BadRequest, title: message ?? code, extensions: Ext(code)),
    };

    private static Dictionary<string, object?> Ext(string code) => new() { ["code"] = code };
}

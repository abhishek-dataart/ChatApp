using ChatApp.Api.Contracts.Rooms;
using ChatApp.Api.Contracts.Social;
using ChatApp.Data.Services.Rooms;
using ChatApp.Domain.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChatApp.Api.Controllers.Rooms;

[ApiController]
[Authorize]
public class InvitationsController(InvitationService invitations, ICurrentUser current) : ControllerBase
{
    [HttpPost("api/rooms/{roomId:guid}/invitations")]
    public async Task<IActionResult> Send(Guid roomId, [FromBody] SendInvitationRequest body, CancellationToken ct)
    {
        var (ok, code, message, value) = await invitations.SendAsync(
            current.Id, roomId, body.Username, body.Note, ct);
        if (!ok)
        {
            return RoomsErrorMapper.FromError(this, code!, message);
        }

        return StatusCode(StatusCodes.Status201Created, ToOutgoing(value!));
    }

    [HttpGet("api/rooms/{roomId:guid}/invitations")]
    public async Task<IActionResult> ListOutgoing(Guid roomId, CancellationToken ct)
    {
        var (ok, code, message, value) = await invitations.ListOutgoingForRoomAsync(current.Id, roomId, ct);
        if (!ok)
        {
            return RoomsErrorMapper.FromError(this, code!, message);
        }

        return Ok(new RoomInvitationsResponse(value!.Select(ToOutgoing).ToList()));
    }

    [HttpGet("api/invitations")]
    public async Task<IActionResult> ListIncoming(CancellationToken ct)
    {
        var items = await invitations.ListIncomingAsync(current.Id, ct);
        return Ok(new IncomingInvitationsResponse(items.Select(ToIncoming).ToList()));
    }

    [HttpPost("api/invitations/{id:guid}/accept")]
    public async Task<IActionResult> Accept(Guid id, CancellationToken ct)
    {
        var (ok, code, message, value) = await invitations.AcceptAsync(current.Id, id, ct);
        if (!ok)
        {
            return RoomsErrorMapper.FromError(this, code!, message);
        }

        return Ok(RoomsMappings.ToDetailResponse(value!));
    }

    [HttpPost("api/invitations/{id:guid}/decline")]
    public async Task<IActionResult> Decline(Guid id, CancellationToken ct)
    {
        var (ok, code, message) = await invitations.DeclineAsync(current.Id, id, ct);
        if (!ok)
        {
            return RoomsErrorMapper.FromError(this, code!, message);
        }

        return NoContent();
    }

    [HttpDelete("api/invitations/{id:guid}")]
    public async Task<IActionResult> RevokeOrDecline(Guid id, CancellationToken ct)
    {
        var (ok, code, message) = await invitations.RevokeOrDeclineAsync(current.Id, id, ct);
        if (!ok)
        {
            return RoomsErrorMapper.FromError(this, code!, message);
        }

        return NoContent();
    }

    private static InvitationEntry ToIncoming(IncomingInvitationRow r) =>
        new(r.InvitationId,
            new RoomSummary(r.RoomId, r.RoomName, r.RoomDescription, r.RoomVisibility,
                r.RoomMemberCount, r.RoomCapacity, r.RoomCreatedAt, r.RoomLogoUrl),
            new UserSummary(r.InviterId, r.InviterUsername, r.InviterDisplayName, r.InviterAvatarPath),
            r.Note, r.CreatedAt);

    private static OutgoingInvitationEntry ToOutgoing(OutgoingInvitationRow r) =>
        new(r.InvitationId,
            new UserSummary(r.InviteeId, r.InviteeUsername, r.InviteeDisplayName, r.InviteeAvatarPath),
            new UserSummary(r.InviterId, r.InviterUsername, r.InviterDisplayName, r.InviterAvatarPath),
            r.Note, r.CreatedAt);
}

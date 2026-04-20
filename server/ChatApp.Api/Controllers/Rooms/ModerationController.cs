using ChatApp.Api.Contracts.Rooms;
using ChatApp.Api.Contracts.Social;
using ChatApp.Data.Services.Rooms;
using ChatApp.Domain.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChatApp.Api.Controllers.Rooms;

[ApiController]
[Route("api/rooms/{roomId:guid}")]
[Authorize]
public class ModerationController(ModerationService moderation, ICurrentUser current) : ControllerBase
{
    [HttpPost("bans")]
    public async Task<IActionResult> Ban(Guid roomId, [FromBody] BanUserRequest body, CancellationToken ct)
    {
        var (ok, code, message) = await moderation.BanAsync(current.Id, roomId, body.UserId, ct);
        if (!ok) return RoomsErrorMapper.FromError(this, code!, message);
        return NoContent();
    }

    [HttpGet("bans")]
    public async Task<IActionResult> ListBans(Guid roomId, CancellationToken ct)
    {
        var (ok, code, message, value) = await moderation.ListBansAsync(current.Id, roomId, ct);
        if (!ok) return RoomsErrorMapper.FromError(this, code!, message);

        var bans = value!.Select(b => new RoomBanEntry(
            b.BanId,
            new UserSummary(b.UserId, b.Username, b.DisplayName, b.AvatarPath),
            new UserSummary(b.BannedById, b.BannedByUsername, b.BannedByDisplayName, b.BannedByAvatarPath),
            b.CreatedAt)).ToList();

        return Ok(new RoomBansResponse(bans));
    }

    [HttpDelete("bans/{userId:guid}")]
    public async Task<IActionResult> Unban(Guid roomId, Guid userId, CancellationToken ct)
    {
        var (ok, code, message) = await moderation.UnbanAsync(current.Id, roomId, userId, ct);
        if (!ok) return RoomsErrorMapper.FromError(this, code!, message);
        return NoContent();
    }

    [HttpPatch("members/{userId:guid}/role")]
    public async Task<IActionResult> ChangeRole(Guid roomId, Guid userId, [FromBody] ChangeRoleRequest body, CancellationToken ct)
    {
        var (ok, code, message) = await moderation.ChangeRoleAsync(current.Id, roomId, userId, body.Role, ct);
        if (!ok) return RoomsErrorMapper.FromError(this, code!, message);
        return NoContent();
    }

    [HttpDelete("members/{userId:guid}")]
    public async Task<IActionResult> Kick(Guid roomId, Guid userId, CancellationToken ct)
    {
        var (ok, code, message) = await moderation.KickAsync(current.Id, roomId, userId, ct);
        if (!ok) return RoomsErrorMapper.FromError(this, code!, message);
        return NoContent();
    }

    [HttpGet("audit")]
    public async Task<IActionResult> ListAudit(
        Guid roomId,
        [FromQuery] int limit = 50,
        [FromQuery] Guid? before = null,
        CancellationToken ct = default)
    {
        var (ok, code, message, items, nextBefore) = await moderation.ListAuditAsync(current.Id, roomId, limit, before, ct);
        if (!ok) return RoomsErrorMapper.FromError(this, code!, message);

        var entries = items!.Select(a => new AuditEntry(
            a.Id,
            new UserSummary(a.ActorId, a.ActorUsername, a.ActorDisplayName, a.ActorAvatarPath),
            a.TargetId.HasValue
                ? new UserSummary(a.TargetId.Value, a.TargetUsername!, a.TargetDisplayName!, a.TargetAvatarPath)
                : null,
            a.Action, a.Detail, a.CreatedAt)).ToList();

        return Ok(new AuditResponse(entries, nextBefore));
    }
}

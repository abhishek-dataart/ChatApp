using ChatApp.Api.Contracts.Social;
using ChatApp.Data.Services.Social;
using ChatApp.Domain.Abstractions;
using ChatApp.Domain.Services.Social;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChatApp.Api.Controllers.Social;

[ApiController]
[Route("api/users")]
[Authorize]
public class BansController(UserBanService bans, ICurrentUser current) : ControllerBase
{
    [HttpPost("{id:guid}/ban")]
    public async Task<IActionResult> Ban(Guid id, CancellationToken ct)
    {
        var (ok, code, message) = await bans.BanAsync(current.Id, id, ct);
        if (!ok)
        {
            return FromError(code!, message);
        }

        return NoContent();
    }

    [HttpDelete("{id:guid}/ban")]
    public async Task<IActionResult> Unban(Guid id, CancellationToken ct)
    {
        var (ok, code, message) = await bans.UnbanAsync(current.Id, id, ct);
        if (!ok)
        {
            return FromError(code!, message);
        }

        return NoContent();
    }

    [HttpGet("bans")]
    public async Task<IActionResult> ListMyBans(CancellationToken ct)
    {
        var entries = await bans.ListMyBansAsync(current.Id, ct);
        var response = new BanListResponse(
            entries.Select(e => new Contracts.Social.BannedUserEntry(
                e.BanId,
                new UserSummary(e.User.Id, e.User.Username, e.User.DisplayName, e.User.AvatarUrl),
                e.CreatedAt)).ToList());
        return Ok(response);
    }

    [HttpGet("{id:guid}/ban-status")]
    public async Task<IActionResult> BanStatus(Guid id, CancellationToken ct)
    {
        var status = await bans.GetBanStatusAsync(current.Id, id, ct);
        return Ok(new BanStatusResponse(status.BannedByMe, status.BannedByThem));
    }

    private IActionResult FromError(string code, string? message) => code switch
    {
        SocialErrors.CannotBanSelf => Problem(statusCode: StatusCodes.Status400BadRequest, title: message ?? "Cannot ban self.", extensions: Ext(code)),
        SocialErrors.AlreadyBanned => Problem(statusCode: StatusCodes.Status409Conflict, title: message ?? "Already banned.", extensions: Ext(code)),
        SocialErrors.BanNotFound => Problem(statusCode: StatusCodes.Status404NotFound, title: message ?? "Ban not found.", extensions: Ext(code)),
        SocialErrors.UserNotFound => Problem(statusCode: StatusCodes.Status404NotFound, title: message ?? "User not found.", extensions: Ext(code)),
        _ => Problem(statusCode: StatusCodes.Status400BadRequest, title: message ?? code, extensions: Ext(code)),
    };

    private static Dictionary<string, object?> Ext(string code) => new() { ["code"] = code };
}

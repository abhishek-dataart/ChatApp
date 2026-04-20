using ChatApp.Api.Contracts.Social;
using ChatApp.Data.Services.Social;
using ChatApp.Domain.Abstractions;
using ChatApp.Domain.Services.Social;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ChatApp.Api.Controllers.Social;

[ApiController]
[Route("api/friendships")]
[Authorize]
[EnableRateLimiting("general")]
public class FriendshipsController(
    FriendshipService friendships,
    ICurrentUser current) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Send([FromBody] SendFriendRequestRequest body, CancellationToken ct)
    {
        var (ok, code, message, value) = await friendships.RequestAsync(current.Id, body.Username, body.Note, ct);
        if (!ok)
        {
            return FromError(code!, message);
        }

        var v = value!;
        var response = new PendingFriendship(v.FriendshipId, ToUserSummary(v.User), v.Note, v.CreatedAt);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await friendships.ListAsync(current.Id, ct);

        var response = new FriendshipListResponse(
            result.Friends.Select(f => new FriendSummary(f.FriendshipId, f.PersonalChatId, ToUserSummary(f.User), f.AcceptedAt)).ToList(),
            result.Incoming.Select(p => new PendingFriendship(p.FriendshipId, ToUserSummary(p.User), p.Note, p.CreatedAt)).ToList(),
            result.Outgoing.Select(p => new PendingFriendship(p.FriendshipId, ToUserSummary(p.User), p.Note, p.CreatedAt)).ToList());

        return Ok(response);
    }

    [HttpPost("{id:guid}/accept")]
    public async Task<IActionResult> Accept(Guid id, CancellationToken ct)
    {
        var (ok, code, message, value) = await friendships.AcceptAsync(current.Id, id, ct);
        if (!ok)
        {
            return FromError(code!, message);
        }

        var v = value!;
        var response = new FriendSummary(v.FriendshipId, v.PersonalChatId, ToUserSummary(v.User), v.AcceptedAt);
        return Ok(response);
    }

    [HttpPost("{id:guid}/decline")]
    public async Task<IActionResult> Decline(Guid id, CancellationToken ct)
    {
        var (ok, code, message) = await friendships.DeclineAsync(current.Id, id, ct);
        if (!ok)
        {
            return FromError(code!, message);
        }

        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var (ok, code, message) = await friendships.UnfriendOrCancelAsync(current.Id, id, ct);
        if (!ok)
        {
            return FromError(code!, message);
        }

        return NoContent();
    }

    private static UserSummary ToUserSummary(FriendshipUserSummary s) =>
        new(s.Id, s.Username, s.DisplayName, s.AvatarUrl);

    private IActionResult FromError(string code, string? message) => code switch
    {
        SocialErrors.CannotFriendSelf => Problem(statusCode: StatusCodes.Status400BadRequest, title: message ?? "Cannot friend self.", extensions: Ext(code)),
        SocialErrors.NoteTooLong => Problem(statusCode: StatusCodes.Status400BadRequest, title: message ?? "Note too long.", extensions: Ext(code)),
        SocialErrors.UserNotFound => Problem(statusCode: StatusCodes.Status404NotFound, title: message ?? "User not found.", extensions: Ext(code)),
        SocialErrors.FriendshipExists => Problem(statusCode: StatusCodes.Status409Conflict, title: message ?? "Friendship already exists.", extensions: Ext(code)),
        SocialErrors.FriendshipNotFound => Problem(statusCode: StatusCodes.Status404NotFound, title: message ?? "Friendship not found.", extensions: Ext(code)),
        SocialErrors.UserBanned => Problem(statusCode: StatusCodes.Status404NotFound, title: "User not found.", extensions: Ext(code)),
        _ => Problem(statusCode: StatusCodes.Status400BadRequest, title: message ?? code, extensions: Ext(code)),
    };

    private static Dictionary<string, object?> Ext(string code) => new() { ["code"] = code };
}

using ChatApp.Api.Contracts.Social;
using ChatApp.Data;
using ChatApp.Data.Entities.Social;
using ChatApp.Domain.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Api.Controllers.Users;

[ApiController]
[Route("api/users")]
[Authorize]
public class UserSearchController(ChatDbContext db, ICurrentUser current) : ControllerBase
{
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string? q, CancellationToken ct)
    {
        var query = (q ?? string.Empty).Trim();
        if (query.Length < 2)
        {
            return Ok(Array.Empty<UserSearchResult>());
        }

        var me = current.Id;
        var like = $"%{query}%";

        var blockedUserIds = await db.UserBans
            .AsNoTracking()
            .Where(b => b.LiftedAt == null && (b.BannerId == me || b.BannedId == me))
            .Select(b => b.BannerId == me ? b.BannedId : b.BannerId)
            .ToListAsync(ct);

        var users = await db.Users
            .AsNoTracking()
            .Where(u => u.DeletedAt == null
                && u.Id != me
                && !blockedUserIds.Contains(u.Id)
                && (EF.Functions.ILike(u.Username, like) || EF.Functions.ILike(u.DisplayName, like)))
            .OrderBy(u => u.DisplayName)
            .ThenBy(u => u.Username)
            .Take(10)
            .Select(u => new { u.Id, u.Username, u.DisplayName, u.AvatarPath })
            .ToListAsync(ct);

        if (users.Count == 0)
        {
            return Ok(Array.Empty<UserSearchResult>());
        }

        var otherIds = users.Select(u => u.Id).ToList();

        var friendships = await db.Friendships
            .AsNoTracking()
            .Where(f => f.State == FriendshipState.Accepted
                && ((f.UserIdLow == me && otherIds.Contains(f.UserIdHigh))
                    || (f.UserIdHigh == me && otherIds.Contains(f.UserIdLow))))
            .Select(f => new { f.UserIdLow, f.UserIdHigh })
            .ToListAsync(ct);

        var friendIds = friendships
            .Select(f => f.UserIdLow == me ? f.UserIdHigh : f.UserIdLow)
            .ToHashSet();

        var chatLookup = new Dictionary<Guid, Guid>();
        if (friendIds.Count > 0)
        {
            var chats = await db.PersonalChats
                .AsNoTracking()
                .Where(p =>
                    (p.UserAId == me && friendIds.Contains(p.UserBId)) ||
                    (p.UserBId == me && friendIds.Contains(p.UserAId)))
                .Select(p => new { p.Id, p.UserAId, p.UserBId })
                .ToListAsync(ct);
            foreach (var c in chats)
            {
                var other = c.UserAId == me ? c.UserBId : c.UserAId;
                chatLookup[other] = c.Id;
            }
        }

        var results = users.Select(u =>
        {
            var isFriend = friendIds.Contains(u.Id);
            Guid? chatId = isFriend && chatLookup.TryGetValue(u.Id, out var cid) ? cid : null;
            var avatarUrl = u.AvatarPath is null ? null : $"/api/profile/avatar/{u.Id}";
            return new UserSearchResult(u.Id, u.Username, u.DisplayName, avatarUrl, isFriend, chatId);
        }).ToList();

        return Ok(results);
    }
}

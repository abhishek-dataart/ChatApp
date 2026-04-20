using ChatApp.Data.Entities.Identity;
using ChatApp.Data.Entities.Social;
using ChatApp.Domain.Abstractions;
using ChatApp.Domain.Services.Social;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Data.Services.Social;

public sealed record FriendshipUserSummary(Guid Id, string Username, string DisplayName, string? AvatarUrl);

public sealed record FriendOutcome(
    Guid FriendshipId, Guid PersonalChatId, FriendshipUserSummary User, DateTimeOffset AcceptedAt);

public sealed record PendingOutcome(
    Guid FriendshipId, FriendshipUserSummary User, string? Note, DateTimeOffset CreatedAt);

public sealed record FriendshipListOutcome(
    List<FriendOutcome> Friends,
    List<PendingOutcome> Incoming,
    List<PendingOutcome> Outgoing);

public class FriendshipService(
    ChatDbContext db,
    PersonalChatService personalChats,
    UserBanService userBans,
    IChatBroadcaster broadcaster)
{
    private static (Guid Low, Guid High) Order(Guid a, Guid b) =>
        a.CompareTo(b) < 0 ? (a, b) : (b, a);

    private static FriendshipUserSummary ToUserSummary(User u) =>
        new(u.Id, u.Username, u.DisplayName,
            u.AvatarPath is null ? null : $"/api/profile/avatar/{u.Id}");

    public async Task<(bool Ok, string? Code, string? Message, PendingOutcome? Value)> RequestAsync(
        Guid me, string targetUsername, string? note, CancellationToken ct = default)
    {
        var trimmedNote = note?.Trim();
        if (string.IsNullOrEmpty(trimmedNote))
        {
            trimmedNote = null;
        }

        if (trimmedNote is not null && trimmedNote.Length > 500)
        {
            return (false, SocialErrors.NoteTooLong, "Request note must be 500 characters or fewer.", null);
        }

        var normalizedUsername = targetUsername.Trim().ToLowerInvariant();
        var target = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UsernameNormalized == normalizedUsername && u.DeletedAt == null, ct);

        if (target is null)
        {
            return (false, SocialErrors.UserNotFound, "User not found.", null);
        }

        if (target.Id == me)
        {
            return (false, SocialErrors.CannotFriendSelf, "You cannot send a friend request to yourself.", null);
        }

        var (low, high) = Order(me, target.Id);
        var exists = await db.Friendships.AnyAsync(f => f.UserIdLow == low && f.UserIdHigh == high, ct);
        if (exists)
        {
            return (false, SocialErrors.FriendshipExists, "A pending or accepted friendship already exists.", null);
        }

        if (await userBans.IsActiveAnyDirectionAsync(me, target.Id, ct))
        {
            return (false, SocialErrors.UserBanned, "Cannot send a friend request to this user.", null);
        }

        var friendship = new Friendship
        {
            Id = Guid.NewGuid(),
            UserIdLow = low,
            UserIdHigh = high,
            State = FriendshipState.Pending,
            RequesterId = me,
            RequestNote = trimmedNote,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.Friendships.Add(friendship);
        await db.SaveChangesAsync(ct);

        await NotifyBothAsync(me, target.Id, friendship.Id, "requested", ct);

        var pending = new PendingOutcome(friendship.Id, ToUserSummary(target), trimmedNote, friendship.CreatedAt);
        return (true, null, null, pending);
    }

    private async Task NotifyBothAsync(Guid a, Guid b, Guid friendshipId, string kind, CancellationToken ct)
    {
        var payload = new FriendshipChangedPayload(friendshipId, kind);
        await broadcaster.BroadcastFriendshipChangedAsync(a, payload, ct);
        await broadcaster.BroadcastFriendshipChangedAsync(b, payload, ct);
    }

    public async Task<(bool Ok, string? Code, string? Message, FriendOutcome? Value)> AcceptAsync(
        Guid me, Guid friendshipId, CancellationToken ct = default)
    {
        var friendship = await db.Friendships.FirstOrDefaultAsync(f => f.Id == friendshipId, ct);

        if (friendship is null
            || friendship.State != FriendshipState.Pending
            || friendship.RequesterId == me
            || (friendship.UserIdLow != me && friendship.UserIdHigh != me))
        {
            return (false, SocialErrors.FriendshipNotFound, "Friendship not found.", null);
        }

        var now = DateTimeOffset.UtcNow;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        friendship.State = FriendshipState.Accepted;
        friendship.AcceptedAt = now;
        await db.SaveChangesAsync(ct);

        var personalChatId = await personalChats.EnsureAsync(friendship.UserIdLow, friendship.UserIdHigh, ct);
        await tx.CommitAsync(ct);

        var other = await db.Users.AsNoTracking().FirstAsync(u => u.Id == friendship.RequesterId, ct);
        await NotifyBothAsync(friendship.UserIdLow, friendship.UserIdHigh, friendship.Id, "accepted", ct);
        var summary = new FriendOutcome(friendship.Id, personalChatId, ToUserSummary(other), now);
        return (true, null, null, summary);
    }

    public async Task<(bool Ok, string? Code, string? Message)> DeclineAsync(
        Guid me, Guid friendshipId, CancellationToken ct = default)
    {
        var friendship = await db.Friendships.FirstOrDefaultAsync(f => f.Id == friendshipId, ct);

        if (friendship is null
            || friendship.State != FriendshipState.Pending
            || friendship.RequesterId == me
            || (friendship.UserIdLow != me && friendship.UserIdHigh != me))
        {
            return (false, SocialErrors.FriendshipNotFound, "Friendship not found.");
        }

        db.Friendships.Remove(friendship);
        await db.SaveChangesAsync(ct);
        await NotifyBothAsync(friendship.UserIdLow, friendship.UserIdHigh, friendship.Id, "declined", ct);
        return (true, null, null);
    }

    public async Task<(bool Ok, string? Code, string? Message)> UnfriendOrCancelAsync(
        Guid me, Guid friendshipId, CancellationToken ct = default)
    {
        var friendship = await db.Friendships.FirstOrDefaultAsync(f => f.Id == friendshipId, ct);

        if (friendship is null || (friendship.UserIdLow != me && friendship.UserIdHigh != me))
        {
            return (false, SocialErrors.FriendshipNotFound, "Friendship not found.");
        }

        var wasAccepted = friendship.State == FriendshipState.Accepted;
        db.Friendships.Remove(friendship);
        await db.SaveChangesAsync(ct);
        await NotifyBothAsync(friendship.UserIdLow, friendship.UserIdHigh, friendship.Id, wasAccepted ? "unfriended" : "cancelled", ct);
        return (true, null, null);
    }

    public async Task<FriendshipListOutcome> ListAsync(Guid me, CancellationToken ct = default)
    {
        var friendships = await db.Friendships
            .AsNoTracking()
            .Where(f => f.UserIdLow == me || f.UserIdHigh == me)
            .ToListAsync(ct);

        if (friendships.Count == 0)
        {
            return new FriendshipListOutcome([], [], []);
        }

        var otherIds = friendships
            .Select(f => f.UserIdLow == me ? f.UserIdHigh : f.UserIdLow)
            .Distinct()
            .ToList();

        var users = await db.Users
            .AsNoTracking()
            .Where(u => otherIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, ct);

        var acceptedFriendships = friendships
            .Where(f => f.State == FriendshipState.Accepted)
            .ToList();

        var chatLookup = new Dictionary<(Guid, Guid), Guid>();
        if (acceptedFriendships.Count > 0)
        {
            var lows = acceptedFriendships.Select(p => p.UserIdLow).ToList();
            var highs = acceptedFriendships.Select(p => p.UserIdHigh).ToList();
            var chats = await db.PersonalChats
                .AsNoTracking()
                .Where(p => lows.Contains(p.UserAId) && highs.Contains(p.UserBId))
                .ToListAsync(ct);
            foreach (var c in chats)
            {
                chatLookup[(c.UserAId, c.UserBId)] = c.Id;
            }
        }

        var friends = acceptedFriendships
            .Select(f =>
            {
                var otherId = f.UserIdLow == me ? f.UserIdHigh : f.UserIdLow;
                chatLookup.TryGetValue((f.UserIdLow, f.UserIdHigh), out var pcId);
                return new FriendOutcome(f.Id, pcId, ToUserSummary(users[otherId]), f.AcceptedAt!.Value);
            })
            .OrderBy(f => f.User.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.User.Username, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var incoming = friendships
            .Where(f => f.State == FriendshipState.Pending && f.RequesterId != me)
            .Select(f =>
            {
                var otherId = f.UserIdLow == me ? f.UserIdHigh : f.UserIdLow;
                return new PendingOutcome(f.Id, ToUserSummary(users[otherId]), f.RequestNote, f.CreatedAt);
            })
            .OrderByDescending(p => p.CreatedAt)
            .ToList();

        var outgoing = friendships
            .Where(f => f.State == FriendshipState.Pending && f.RequesterId == me)
            .Select(f =>
            {
                var otherId = f.UserIdLow == me ? f.UserIdHigh : f.UserIdLow;
                return new PendingOutcome(f.Id, ToUserSummary(users[otherId]), f.RequestNote, f.CreatedAt);
            })
            .OrderByDescending(p => p.CreatedAt)
            .ToList();

        return new FriendshipListOutcome(friends, incoming, outgoing);
    }
}

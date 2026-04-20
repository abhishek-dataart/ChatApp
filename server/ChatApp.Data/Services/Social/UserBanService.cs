using ChatApp.Data.Entities.Social;
using ChatApp.Domain.Services.Social;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Data.Services.Social;

public sealed record BannedUserEntry(Guid BanId, BanUserSummary User, DateTimeOffset CreatedAt);
public sealed record BanUserSummary(Guid Id, string Username, string DisplayName, string? AvatarUrl);
public sealed record BanStatusOutcome(bool BannedByMe, bool BannedByThem);

public class UserBanService(ChatDbContext db)
{
    internal async Task<bool> IsActiveAnyDirectionAsync(Guid a, Guid b, CancellationToken ct) =>
        await db.UserBans.AnyAsync(
            ub => ub.LiftedAt == null &&
                  ((ub.BannerId == a && ub.BannedId == b) || (ub.BannerId == b && ub.BannedId == a)),
            ct);

    public async Task<(bool Ok, string? Code, string? Message)> BanAsync(
        Guid me, Guid targetId, CancellationToken ct = default)
    {
        if (me == targetId)
        {
            return (false, SocialErrors.CannotBanSelf, "You cannot ban yourself.");
        }

        var target = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == targetId && u.DeletedAt == null, ct);
        if (target is null)
        {
            return (false, SocialErrors.UserNotFound, "User not found.");
        }

        var alreadyBanned = await db.UserBans.AnyAsync(
            ub => ub.BannerId == me && ub.BannedId == targetId && ub.LiftedAt == null, ct);
        if (alreadyBanned)
        {
            return (false, SocialErrors.AlreadyBanned, "You have already banned this user.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        db.UserBans.Add(new UserBan
        {
            Id = Guid.NewGuid(),
            BannerId = me,
            BannedId = targetId,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var (low, high) = me.CompareTo(targetId) < 0 ? (me, targetId) : (targetId, me);
        await db.Friendships
            .Where(f => f.UserIdLow == low && f.UserIdHigh == high)
            .ExecuteDeleteAsync(ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return (true, null, null);
    }

    public async Task<(bool Ok, string? Code, string? Message)> UnbanAsync(
        Guid me, Guid targetId, CancellationToken ct = default)
    {
        var updated = await db.UserBans
            .Where(ub => ub.BannerId == me && ub.BannedId == targetId && ub.LiftedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(ub => ub.LiftedAt, DateTimeOffset.UtcNow), ct);

        if (updated == 0)
        {
            return (false, SocialErrors.BanNotFound, "No active ban found for this user.");
        }

        return (true, null, null);
    }

    public async Task<List<BannedUserEntry>> ListMyBansAsync(Guid me, CancellationToken ct = default)
    {
        var rows = await (
            from ub in db.UserBans.AsNoTracking()
            join u in db.Users.AsNoTracking() on ub.BannedId equals u.Id
            where ub.BannerId == me && ub.LiftedAt == null
            orderby ub.CreatedAt descending
            select new
            {
                ub.Id,
                u.Username, u.DisplayName, u.AvatarPath,
                UserId = u.Id,
                ub.CreatedAt,
            }).ToListAsync(ct);

        return rows.Select(r => new BannedUserEntry(
            r.Id,
            new BanUserSummary(r.UserId, r.Username, r.DisplayName,
                r.AvatarPath is null ? null : $"/api/profile/avatar/{r.UserId}"),
            r.CreatedAt)).ToList();
    }

    public async Task<BanStatusOutcome> GetBanStatusAsync(Guid me, Guid otherId, CancellationToken ct = default)
    {
        var bannedByMe = await db.UserBans.AnyAsync(
            ub => ub.BannerId == me && ub.BannedId == otherId && ub.LiftedAt == null, ct);
        var bannedByThem = await db.UserBans.AnyAsync(
            ub => ub.BannerId == otherId && ub.BannedId == me && ub.LiftedAt == null, ct);
        return new BanStatusOutcome(bannedByMe, bannedByThem);
    }
}

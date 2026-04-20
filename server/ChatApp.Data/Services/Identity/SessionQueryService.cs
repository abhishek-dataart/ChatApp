using Microsoft.EntityFrameworkCore;

namespace ChatApp.Data.Services.Identity;

public record SessionRow(Guid Id, string UserAgent, string Ip, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt, bool IsCurrent);

public class SessionQueryService
{
    private readonly ChatDbContext _db;

    public SessionQueryService(ChatDbContext db) => _db = db;

    public async Task<IReadOnlyList<SessionRow>> ListAsync(Guid userId, Guid currentSessionId, CancellationToken ct)
    {
        return await _db.Sessions
            .Where(s => s.UserId == userId && s.RevokedAt == null)
            .OrderByDescending(s => s.LastSeenAt)
            .Select(s => new SessionRow(
                s.Id,
                s.UserAgent,
                s.Ip,
                s.CreatedAt,
                s.LastSeenAt,
                s.Id == currentSessionId))
            .ToListAsync(ct);
    }
}

using Microsoft.EntityFrameworkCore;

namespace ChatApp.Data.Services.Identity;

public class SessionRevocationService
{
    private readonly ChatDbContext _db;

    public SessionRevocationService(ChatDbContext db) => _db = db;

    /// <summary>
    /// Revokes a single session owned by userId. Returns the session's CookieHash, or null if not found.
    /// </summary>
    public async Task<byte[]?> RevokeAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        var session = await _db.Sessions
            .Where(s => s.Id == sessionId && s.UserId == userId && s.RevokedAt == null)
            .Select(s => new { s.Id, s.CookieHash })
            .FirstOrDefaultAsync(ct);

        if (session is null)
        {
            return null;
        }

        await _db.Sessions
            .Where(s => s.Id == sessionId && s.UserId == userId && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.RevokedAt, DateTimeOffset.UtcNow), ct);

        return session.CookieHash;
    }

    /// <summary>
    /// Revokes all sessions for userId except currentSessionId. Returns the CookieHashes of evicted sessions.
    /// </summary>
    public async Task<IReadOnlyList<byte[]>> RevokeOthersAsync(Guid userId, Guid currentSessionId, CancellationToken ct)
    {
        var hashes = await _db.Sessions
            .Where(s => s.UserId == userId && s.Id != currentSessionId && s.RevokedAt == null)
            .Select(s => s.CookieHash)
            .ToListAsync(ct);

        if (hashes.Count == 0)
        {
            return hashes;
        }

        await _db.Sessions
            .Where(s => s.UserId == userId && s.Id != currentSessionId && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.RevokedAt, DateTimeOffset.UtcNow), ct);

        return hashes;
    }
}

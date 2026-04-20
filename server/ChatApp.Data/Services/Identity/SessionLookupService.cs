using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ChatApp.Data.Services.Identity;

public sealed record SessionPrincipal(Guid UserId, Guid SessionId, string Username);

public class SessionLookupService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private readonly ChatDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SessionLookupService> _logger;

    public SessionLookupService(
        ChatDbContext db,
        IMemoryCache cache,
        IServiceScopeFactory scopeFactory,
        ILogger<SessionLookupService> logger)
    {
        _db = db;
        _cache = cache;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<SessionPrincipal?> ValidateAsync(byte[] cookieHash, CancellationToken ct)
    {
        var key = CacheKey(cookieHash);
        if (_cache.TryGetValue<SessionPrincipal?>(key, out var cached))
        {
            return cached;
        }

        var now = DateTimeOffset.UtcNow;
        var row = await (from s in _db.Sessions
                         join u in _db.Users on s.UserId equals u.Id
                         where s.CookieHash == cookieHash
                            && s.RevokedAt == null
                            && (s.ExpiresAt == null || s.ExpiresAt > now)
                            && u.DeletedAt == null
                         select new { s.UserId, s.Id, u.Username })
                        .FirstOrDefaultAsync(ct);

        SessionPrincipal? principal = row is null
            ? null
            : new SessionPrincipal(row.UserId, row.Id, row.Username);

        _cache.Set(key, principal, CacheTtl);
        return principal;
    }

    public void Evict(byte[] cookieHash) => _cache.Remove(CacheKey(cookieHash));

    // Why: scoped DbContext from the request would already be disposed by the time this runs.
    // We open our own DI scope so the background update has a fresh, owned ChatDbContext.
    public void TouchLastSeen(Guid sessionId)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
                var now = DateTimeOffset.UtcNow;
                await db.Sessions
                    .Where(s => s.Id == sessionId)
                    .ExecuteUpdateAsync(upd => upd.SetProperty(s => s.LastSeenAt, now));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update last_seen_at for session {SessionId}", sessionId);
            }
        });
    }

    private static string CacheKey(byte[] cookieHash) => "session:" + Convert.ToHexString(cookieHash);
}

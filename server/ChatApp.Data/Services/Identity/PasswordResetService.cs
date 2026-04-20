using ChatApp.Data.Entities.Identity;
using ChatApp.Domain.Abstractions;
using ChatApp.Domain.Services.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Data.Services.Identity;

public class PasswordResetService
{
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(30);

    private readonly ChatDbContext _db;
    private readonly IPasswordHasher<User> _hasher;
    private readonly IPasswordResetNotifier _notifier;

    public PasswordResetService(ChatDbContext db, IPasswordHasher<User> hasher, IPasswordResetNotifier notifier)
    {
        _db = db;
        _hasher = hasher;
        _notifier = notifier;
    }

    // Always returns success to avoid leaking whether an email is registered.
    public async Task RequestAsync(string email, string ip, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email) || !AuthValidator.IsValidEmail(email))
        {
            return;
        }

        var emailNorm = AuthValidator.NormalizeEmail(email);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.EmailNormalized == emailNorm && u.DeletedAt == null, ct);
        if (user is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var token = SessionTokens.NewToken();
        var entry = new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = SessionTokens.Hash(token),
            CreatedAt = now,
            ExpiresAt = now + TokenLifetime,
            RequestIp = Truncate(ip, 64)
        };
        _db.PasswordResetTokens.Add(entry);
        await _db.SaveChangesAsync(ct);

        await _notifier.SendAsync(user.Email, user.DisplayName, token, ct);
    }

    public async Task<AuthResult<IReadOnlyList<byte[]>>> ResetAsync(string token, string newPassword, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return AuthResult<IReadOnlyList<byte[]>>.Failure(AuthErrors.InvalidResetToken);
        }
        if (!AuthValidator.IsValidPassword(newPassword))
        {
            return AuthResult<IReadOnlyList<byte[]>>.Failure(AuthErrors.ValidationFailed,
                "Password must be at least 10 characters and contain a letter and a digit.");
        }

        byte[] hash;
        try
        {
            hash = SessionTokens.Hash(token);
        }
        catch (FormatException)
        {
            return AuthResult<IReadOnlyList<byte[]>>.Failure(AuthErrors.InvalidResetToken);
        }

        var now = DateTimeOffset.UtcNow;
        var entry = await _db.PasswordResetTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.UsedAt == null && t.ExpiresAt > now, ct);

        if (entry is null)
        {
            return AuthResult<IReadOnlyList<byte[]>>.Failure(AuthErrors.InvalidResetToken);
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == entry.UserId && u.DeletedAt == null, ct);
        if (user is null)
        {
            return AuthResult<IReadOnlyList<byte[]>>.Failure(AuthErrors.InvalidResetToken);
        }

        user.PasswordHash = _hasher.HashPassword(user, newPassword);
        entry.UsedAt = now;

        var revokedHashes = await _db.Sessions
            .Where(s => s.UserId == user.Id && s.RevokedAt == null)
            .Select(s => s.CookieHash)
            .ToListAsync(ct);

        if (revokedHashes.Count > 0)
        {
            await _db.Sessions
                .Where(s => s.UserId == user.Id && s.RevokedAt == null)
                .ExecuteUpdateAsync(upd => upd.SetProperty(s => s.RevokedAt, now), ct);
        }

        // Invalidate any outstanding reset tokens for this user so a leaked link can't be reused.
        await _db.PasswordResetTokens
            .Where(t => t.UserId == user.Id && t.UsedAt == null)
            .ExecuteUpdateAsync(upd => upd.SetProperty(t => t.UsedAt, now), ct);

        await _db.SaveChangesAsync(ct);
        return AuthResult<IReadOnlyList<byte[]>>.Success(revokedHashes);
    }

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }
        return value.Length <= max ? value : value[..max];
    }
}

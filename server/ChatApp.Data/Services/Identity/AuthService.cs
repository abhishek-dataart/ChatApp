using ChatApp.Data.Entities.Identity;
using ChatApp.Domain.Services.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ChatApp.Data.Services.Identity;

public sealed record RegisterOutcome(User User, string Token, Guid SessionId);
public sealed record LoginOutcome(User User, string Token, Guid SessionId);

public class AuthService
{
    private const string DummyHash = "AQAAAAIAAYagAAAAEP0zKQhZ2dvJkXeQkL+nJh7K1kHyWzKn2nVbGz3y0qGyQmW8zpRJ0JX+H3pV0XxJDw==";

    private readonly ChatDbContext _db;
    private readonly IPasswordHasher<User> _hasher;

    public AuthService(ChatDbContext db, IPasswordHasher<User> hasher)
    {
        _db = db;
        _hasher = hasher;
    }

    public async Task<AuthResult<RegisterOutcome>> RegisterAsync(
        string email,
        string username,
        string displayName,
        string password,
        string userAgent,
        string ip,
        CancellationToken ct)
    {
        if (!AuthValidator.IsValidEmail(email))
        {
            return AuthResult<RegisterOutcome>.Failure(AuthErrors.ValidationFailed, "Invalid email.");
        }
        if (!AuthValidator.IsValidUsername(username))
        {
            return AuthResult<RegisterOutcome>.Failure(AuthErrors.ValidationFailed, "Username must be 3-20 chars, lowercase letters/digits/underscore.");
        }
        if (!AuthValidator.IsValidDisplayName(displayName))
        {
            return AuthResult<RegisterOutcome>.Failure(AuthErrors.ValidationFailed, "Display name must be 1-64 chars.");
        }
        if (!AuthValidator.IsValidPassword(password))
        {
            return AuthResult<RegisterOutcome>.Failure(AuthErrors.ValidationFailed, "Password must be at least 10 characters and contain a letter and a digit.");
        }

        var emailNorm = AuthValidator.NormalizeEmail(email);
        var userNorm = AuthValidator.NormalizeUsername(username);

        if (await _db.Users.AnyAsync(u => u.EmailNormalized == emailNorm, ct))
        {
            return AuthResult<RegisterOutcome>.Failure(AuthErrors.EmailTaken);
        }
        if (await _db.Users.AnyAsync(u => u.UsernameNormalized == userNorm, ct))
        {
            return AuthResult<RegisterOutcome>.Failure(AuthErrors.UsernameTaken);
        }

        var now = DateTimeOffset.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email.Trim(),
            EmailNormalized = emailNorm,
            Username = username.Trim(),
            UsernameNormalized = userNorm,
            DisplayName = displayName.Trim(),
            CreatedAt = now
        };
        user.PasswordHash = _hasher.HashPassword(user, password);

        _db.Users.Add(user);

        var token = SessionTokens.NewToken();
        var session = new Session
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            CookieHash = SessionTokens.Hash(token),
            UserAgent = Truncate(userAgent, 512),
            Ip = Truncate(ip, 64),
            CreatedAt = now,
            LastSeenAt = now
        };
        _db.Sessions.Add(session);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == "23505")
        {
            if (pg.ConstraintName?.Contains("email", StringComparison.OrdinalIgnoreCase) == true)
            {
                return AuthResult<RegisterOutcome>.Failure(AuthErrors.EmailTaken);
            }
            return AuthResult<RegisterOutcome>.Failure(AuthErrors.UsernameTaken);
        }

        return AuthResult<RegisterOutcome>.Success(new RegisterOutcome(user, token, session.Id));
    }

    public async Task<AuthResult<LoginOutcome>> LoginAsync(
        string email,
        string password,
        string userAgent,
        string ip,
        CancellationToken ct)
    {
        var emailNorm = string.IsNullOrEmpty(email) ? string.Empty : AuthValidator.NormalizeEmail(email);
        var user = emailNorm.Length == 0
            ? null
            : await _db.Users.FirstOrDefaultAsync(u => u.EmailNormalized == emailNorm && u.DeletedAt == null, ct);

        if (user is null)
        {
            _ = _hasher.VerifyHashedPassword(new User { PasswordHash = DummyHash }, DummyHash, password ?? string.Empty);
            return AuthResult<LoginOutcome>.Failure(AuthErrors.InvalidCredentials);
        }

        var verify = _hasher.VerifyHashedPassword(user, user.PasswordHash, password ?? string.Empty);
        if (verify == PasswordVerificationResult.Failed)
        {
            return AuthResult<LoginOutcome>.Failure(AuthErrors.InvalidCredentials);
        }

        if (verify == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _hasher.HashPassword(user, password!);
        }

        var now = DateTimeOffset.UtcNow;
        var token = SessionTokens.NewToken();
        var session = new Session
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            CookieHash = SessionTokens.Hash(token),
            UserAgent = Truncate(userAgent, 512),
            Ip = Truncate(ip, 64),
            CreatedAt = now,
            LastSeenAt = now
        };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync(ct);

        return AuthResult<LoginOutcome>.Success(new LoginOutcome(user, token, session.Id));
    }

    public async Task LogoutAsync(Guid sessionId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await _db.Sessions
            .Where(s => s.Id == sessionId && s.RevokedAt == null)
            .ExecuteUpdateAsync(upd => upd.SetProperty(s => s.RevokedAt, now), ct);
    }

    public async Task<AuthResult<IReadOnlyList<byte[]>>> ChangePasswordAsync(
        Guid userId,
        Guid currentSessionId,
        string currentPassword,
        string newPassword,
        CancellationToken ct)
    {
        if (!AuthValidator.IsValidPassword(newPassword))
        {
            return AuthResult<IReadOnlyList<byte[]>>.Failure(AuthErrors.ValidationFailed, "Password must be at least 10 characters and contain a letter and a digit.");
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.DeletedAt == null, ct);
        if (user is null)
        {
            return AuthResult<IReadOnlyList<byte[]>>.Failure(AuthErrors.InvalidCurrentPassword);
        }

        var verify = _hasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword ?? string.Empty);
        if (verify == PasswordVerificationResult.Failed)
        {
            return AuthResult<IReadOnlyList<byte[]>>.Failure(AuthErrors.InvalidCurrentPassword);
        }

        user.PasswordHash = _hasher.HashPassword(user, newPassword);

        var now = DateTimeOffset.UtcNow;
        var revokedHashes = await _db.Sessions
            .Where(s => s.UserId == userId && s.Id != currentSessionId && s.RevokedAt == null)
            .Select(s => s.CookieHash)
            .ToListAsync(ct);

        if (revokedHashes.Count > 0)
        {
            await _db.Sessions
                .Where(s => s.UserId == userId && s.Id != currentSessionId && s.RevokedAt == null)
                .ExecuteUpdateAsync(upd => upd.SetProperty(s => s.RevokedAt, now), ct);
        }

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

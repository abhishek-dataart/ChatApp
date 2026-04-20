using System.Security.Cryptography;
using ChatApp.Api.Contracts.Auth;
using ChatApp.Api.Infrastructure.Auth;
using ChatApp.Data;
using ChatApp.Data.Entities.Identity;
using ChatApp.Data.Services.Identity;
using ChatApp.Domain.Abstractions;
using ChatApp.Domain.Services.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Api.Controllers.Auth;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthService _auth;
    private readonly SessionLookupService _lookup;
    private readonly CookieWriter _cookies;
    private readonly LoginRateLimiter _loginLimiter;
    private readonly PasswordResetRateLimiter _resetLimiter;
    private readonly ICurrentUser _current;
    private readonly ChatDbContext _db;
    private readonly PasswordResetService _reset;

    public AuthController(
        AuthService auth,
        SessionLookupService lookup,
        CookieWriter cookies,
        LoginRateLimiter loginLimiter,
        PasswordResetRateLimiter resetLimiter,
        ICurrentUser current,
        ChatDbContext db,
        PasswordResetService reset)
    {
        _auth = auth;
        _lookup = lookup;
        _cookies = cookies;
        _loginLimiter = loginLimiter;
        _resetLimiter = resetLimiter;
        _current = current;
        _db = db;
        _reset = reset;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterRequest body, CancellationToken ct)
    {
        var result = await _auth.RegisterAsync(
            body.Email, body.Username, body.DisplayName, body.Password,
            UserAgent(), ClientIp(), ct);

        if (!result.IsSuccess)
        {
            return FromError(result.ErrorCode!, result.ErrorMessage);
        }

        var outcome = result.Value!;
        _cookies.Write(Response, outcome.Token);
        WriteCsrfCookie(Response);
        var me = ToMe(outcome.User, outcome.SessionId);
        return StatusCode(StatusCodes.Status201Created, me);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest body, CancellationToken ct)
    {
        var emailNorm = string.IsNullOrEmpty(body.Email) ? string.Empty : AuthValidator.NormalizeEmail(body.Email);
        if (!_loginLimiter.TryAcquire(ClientIp(), emailNorm))
        {
            return Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Too many login attempts.", extensions: new Dictionary<string, object?> { ["code"] = "rate_limited" });
        }

        var result = await _auth.LoginAsync(body.Email, body.Password, UserAgent(), ClientIp(), ct, body.KeepSignedIn);
        if (!result.IsSuccess)
        {
            return FromError(result.ErrorCode!, result.ErrorMessage);
        }

        var outcome = result.Value!;
        _cookies.Write(Response, outcome.Token, body.KeepSignedIn ? AuthService.PersistentSessionLifetime : null);
        WriteCsrfCookie(Response);
        return Ok(ToMe(outcome.User, outcome.SessionId));
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var sessionId = _current.SessionId;
        var session = await _db.Sessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        await _auth.LogoutAsync(sessionId, ct);
        if (session is not null)
        {
            _lookup.Evict(session.CookieHash);
        }
        _cookies.Clear(Response);
        ClearCsrfCookie(Response);
        return NoContent();
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest body, CancellationToken ct)
    {
        var result = await _auth.ChangePasswordAsync(
            _current.Id, _current.SessionId, body.CurrentPassword, body.NewPassword, ct);

        if (!result.IsSuccess)
        {
            return FromError(result.ErrorCode!, result.ErrorMessage);
        }

        foreach (var h in result.Value!)
        {
            _lookup.Evict(h);
        }
        return NoContent();
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest body, CancellationToken ct)
    {
        var emailNorm = string.IsNullOrEmpty(body.Email) ? string.Empty : AuthValidator.NormalizeEmail(body.Email);
        if (!_loginLimiter.TryAcquire(ClientIp(), emailNorm))
        {
            return Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Too many attempts.", extensions: new Dictionary<string, object?> { ["code"] = "rate_limited" });
        }
        // Swallow per-email exhaustion silently (return 204) so IP-rotating attackers can't
        // probe which emails have already hit the reset cap.
        if (!_resetLimiter.TryAcquire(emailNorm))
        {
            return NoContent();
        }
        await _reset.RequestAsync(body.Email ?? string.Empty, ClientIp(), ct);
        // Always 204: don't leak whether the email exists.
        return NoContent();
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest body, CancellationToken ct)
    {
        var result = await _reset.ResetAsync(body.Token, body.NewPassword, ct);
        if (!result.IsSuccess)
        {
            return FromError(result.ErrorCode!, result.ErrorMessage);
        }
        foreach (var h in result.Value!)
        {
            _lookup.Evict(h);
        }
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == _current.Id, ct);
        return user is null ? Unauthorized() : Ok(ToMe(user, _current.SessionId));
    }

    private static MeResponse ToMe(User u, Guid sessionId) => new(
        u.Id, u.Email, u.Username, u.DisplayName,
        u.AvatarPath is null ? null : $"/api/profile/avatar/{u.Id}",
        u.SoundOnMessage, sessionId);

    private IActionResult FromError(string code, string? message) => code switch
    {
        AuthErrors.EmailTaken => Problem(statusCode: StatusCodes.Status409Conflict, title: "Email already registered.", extensions: Ext(code)),
        AuthErrors.UsernameTaken => Problem(statusCode: StatusCodes.Status409Conflict, title: "Username already taken.", extensions: Ext(code)),
        AuthErrors.InvalidCredentials => Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid email or password.", extensions: Ext(code)),
        AuthErrors.InvalidCurrentPassword => Problem(statusCode: StatusCodes.Status400BadRequest, title: "Current password is incorrect.", extensions: Ext(code)),
        AuthErrors.InvalidResetToken => Problem(statusCode: StatusCodes.Status400BadRequest, title: "Reset link is invalid or expired.", extensions: Ext(code)),
        AuthErrors.ValidationFailed => Problem(statusCode: StatusCodes.Status400BadRequest, title: message ?? "Validation failed.", extensions: Ext(code)),
        _ => Problem(statusCode: StatusCodes.Status400BadRequest, title: message ?? code, extensions: Ext(code))
    };

    private static Dictionary<string, object?> Ext(string code) => new() { ["code"] = code };

    private void WriteCsrfCookie(HttpResponse response)
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        response.Cookies.Append("csrf_token", token, new CookieOptions
        {
            HttpOnly = false,
            Secure   = _cookies.IsSecure,
            SameSite = SameSiteMode.Lax,
            Path     = "/",
        });
    }

    private static void ClearCsrfCookie(HttpResponse response)
    {
        response.Cookies.Append("csrf_token", string.Empty, new CookieOptions
        {
            HttpOnly = false,
            SameSite = SameSiteMode.Lax,
            Path     = "/",
            Expires  = DateTimeOffset.UnixEpoch,
            MaxAge   = TimeSpan.Zero,
        });
    }

    private string UserAgent() => Request.Headers.UserAgent.ToString();

    private string ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

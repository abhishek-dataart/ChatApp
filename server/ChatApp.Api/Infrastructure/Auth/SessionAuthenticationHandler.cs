using System.Security.Claims;
using System.Text.Encodings.Web;
using ChatApp.Data.Services.Identity;
using ChatApp.Domain.Services.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ChatApp.Api.Infrastructure.Auth;

public class SessionAuthenticationOptions : AuthenticationSchemeOptions { }

// Single auth pipeline: this handler is the only thing populating HttpContext.User.
// Why: the previous setup combined AddCookie() with a custom middleware, which made the
// cookie handler try (and fail) to deserialize our opaque session token on every request.
public class SessionAuthenticationHandler : AuthenticationHandler<SessionAuthenticationOptions>
{
    public const string SchemeName = "Session";
    public const string SessionIdClaim = "sid";

    private readonly SessionLookupService _lookup;
    private readonly CookieWriter _cookies;

    public SessionAuthenticationHandler(
        IOptionsMonitor<SessionAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        SessionLookupService lookup,
        CookieWriter cookies)
        : base(options, logger, encoder)
    {
        _lookup = lookup;
        _cookies = cookies;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = Request.Cookies[_cookies.CookieName];
        if (string.IsNullOrEmpty(token))
        {
            return AuthenticateResult.NoResult();
        }

        byte[] hash;
        try
        {
            hash = SessionTokens.Hash(token);
        }
        catch (FormatException)
        {
            _cookies.Clear(Response);
            return AuthenticateResult.NoResult();
        }

        var principal = await _lookup.ValidateAsync(hash, Context.RequestAborted);
        if (principal is null)
        {
            _cookies.Clear(Response);
            return AuthenticateResult.NoResult();
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, principal.UserId.ToString()),
            new Claim(ClaimTypes.Name, principal.Username),
            new Claim(SessionIdClaim, principal.SessionId.ToString())
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var clp = new ClaimsPrincipal(identity);

        _lookup.TouchLastSeen(principal.SessionId);

        return AuthenticateResult.Success(new AuthenticationTicket(clp, SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}
